namespace WeakEventHandler.Fody
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using FodyTools;

    using global::Fody;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    class WeakEventHandlerWeaver
    {
        private const string MakeWeakAttributeName = "WeakEventHandler.MakeWeakAttribute";

        [NotNull]
        private readonly TypeReference _action2Type;
        [NotNull]
        private readonly MethodReference _action2Constructor;
        [NotNull]
        private readonly TypeReference _action3Type;
        [NotNull]
        private readonly MethodReference _action3Constructor;
        [NotNull]
        private readonly TypeReference _eventHandlerType;

        [NotNull]
        private readonly TypeDefinition _weakListenerType;
        [NotNull]
        private readonly MethodDefinition _weakListenerConstructor;
        [NotNull]
        private readonly MethodDefinition _weakListenerSubscribeMethod;
        [NotNull]
        private readonly MethodDefinition _weakListenerUnsubscribeMethod;
        [NotNull]
        private readonly MethodDefinition _weakListenerReleaseMethod;

        [NotNull]
        private readonly ModuleDefinition _moduleDefinition;

        [NotNull]
        private readonly ITypeSystem _typeSystem;

        public static void Weave([NotNull] ModuleDefinition moduleDefinition, [NotNull] ITypeSystem typeSystem)
        {
            if (!moduleDefinition.TryGetTypeReference(MakeWeakAttributeName, out var makeWeakAttributeReference))
                return;

            new WeakEventHandlerWeaver(moduleDefinition, typeSystem, makeWeakAttributeReference).Weave();
        }

        private WeakEventHandlerWeaver([NotNull] ModuleDefinition moduleDefinition, [NotNull] ITypeSystem typeSystem, [NotNull] TypeReference makeWeakAttributeReference)
        {
            // Debugger.Launch();

            _moduleDefinition = moduleDefinition;
            _typeSystem = typeSystem;

            var makeWeakAttribute = makeWeakAttributeReference.Resolve();

            var listenerSource = makeWeakAttribute.Module.Types.Single(t => t.Name == "WeakEventListener`3");

            var codeImporter = new CodeImporter(moduleDefinition) { NamespaceDecorator = value => "<>" + value };

            _weakListenerType = codeImporter.Import(listenerSource);
            _weakListenerConstructor = _weakListenerType.GetConstructors().Single(ctor => ctor.Parameters.Count == 4);

            var weakListenerMethods = _weakListenerType.GetMethods();

            _weakListenerSubscribeMethod = weakListenerMethods.Single(method => method.Name == "Subscribe");
            _weakListenerUnsubscribeMethod = weakListenerMethods.Single(method => method.Name == "Unsubscribe");
            _weakListenerReleaseMethod = weakListenerMethods.Single(method => method.Name == "Release");

            _action2Type = typeSystem.ImportType<Action<object, EventArgs>>();
            _action2Constructor = typeSystem.ImportMethod<Action<object, EventArgs>, object, IntPtr>(".ctor");
            _action3Type = typeSystem.ImportType<Action<Type, object, EventArgs>>();
            _action3Constructor = typeSystem.ImportMethod<Action<Type, object, EventArgs>, object, IntPtr>(".ctor");

            _eventHandlerType = typeSystem.ImportType<EventHandler<EventArgs>>();
        }

        private void Weave()
        {
            var methods = _moduleDefinition
                .GetTypes()
                .Where(c => c.IsClass)
                .SelectMany(c => c.GetMethods())
                .Where(ConsumeAttribute)
                .ToArray();

            Verify(methods);

            Weave(methods);
        }

        private void Weave([NotNull, ItemNotNull] IEnumerable<MethodDefinition> methods)
        {
            var eventInfos = new Dictionary<EventKey, EventInfo>();

            foreach (var method in methods)
            {
                Analyze(method, eventInfos);
            }

            foreach (var eventInfo in eventInfos.Values)
            {
                Weave(eventInfo);
            }
        }

        private void Analyze([NotNull] MethodDefinition method, [NotNull] Dictionary<EventKey, EventInfo> eventInfos)
        {
            var type = method.DeclaringType;

            foreach (var m in type.Methods)
            {
                var instructions = m.Body.Instructions;

                foreach (var instruction in instructions.Where(instr => instr.Operand == method))
                {
                    var createEventHandler = instruction.Next;
                    var callAddOrRemoveEvent = createEventHandler?.Next;

                    if (callAddOrRemoveEvent?.OpCode == OpCodes.Callvirt)
                    {
                        var addOrRemoveMethod = (callAddOrRemoveEvent?.Operand as MethodReference)?.Resolve();

                        var sourceType = addOrRemoveMethod?.DeclaringType;

                        var sourceEvent = sourceType?.Events?.SingleOrDefault(e => e.AddMethod == addOrRemoveMethod || e.RemoveMethod == addOrRemoveMethod);

                        if (sourceEvent == null)
                            continue;

                        var eventInfo = GetOrAdd(eventInfos, new EventKey(method, sourceEvent));

                        var eventRegistration = sourceEvent.AddMethod == addOrRemoveMethod ? EventRegistration.Add : EventRegistration.Remove;

                        eventInfo.Instructions.Add(new InstructionInfo(eventRegistration, instruction, instructions, method));
                    }
                }
            }
        }

        private void Weave([NotNull] EventInfo eventInfo)
        {
            var eventSinkMethod = eventInfo.EventSink;
            var targetType = eventSinkMethod.DeclaringType;

            var sourceEvent = eventInfo.Event;
            var sourceType = _moduleDefinition.ImportReference(sourceEvent.DeclaringType);

            var eventArgsType = _moduleDefinition.ImportReference(eventSinkMethod.Parameters[1].ParameterType);

            var weakListenerType = _weakListenerType.MakeGenericInstanceType(sourceType, targetType, eventArgsType);
            var weakListenerConstructor = _weakListenerConstructor.OnGenericType(weakListenerType);

            var eventSinkActionType = _action3Type.MakeGenericInstanceType(targetType, _typeSystem.TypeSystem.ObjectReference, eventArgsType);
            var eventSinkActionConstructor = _action3Constructor.OnGenericType(eventSinkActionType);

            var eventHandlerType = _eventHandlerType.MakeGenericInstanceType(eventArgsType);
            var eventHandlerAdapterActionType = _action2Type.MakeGenericInstanceType(sourceType, eventHandlerType);
            var eventHandlerAdapterActionConstructor = _action2Constructor.OnGenericType(eventHandlerAdapterActionType);

            var weakListenerField = new FieldDefinition($"{eventInfo}>Listener", FieldAttributes.InitOnly | FieldAttributes.Private | FieldAttributes.NotSerialized, weakListenerType);
            targetType.Fields.Add(weakListenerField);

            var addMethodAdapter = CreateStaticAddRemoveAdapter(eventInfo, sourceType, eventHandlerType, ">Add", _moduleDefinition.ImportReference(sourceEvent.AddMethod));
            var removeMethodAdapter = CreateStaticAddRemoveAdapter(eventInfo, sourceType, eventHandlerType, ">Remove", _moduleDefinition.ImportReference(sourceEvent.RemoveMethod));

            targetType.Methods.Add(addMethodAdapter);
            targetType.Methods.Add(removeMethodAdapter);

            targetType.InsertIntoConstructors(() => new[]
            {
                // this
                Instruction.Create(OpCodes.Ldarg_0), 
                // targetObject
                Instruction.Create(OpCodes.Ldarg_0), 
                // targetDelegate
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldftn, eventSinkMethod),
                Instruction.Create(OpCodes.Newobj, eventSinkActionConstructor),
                // add 
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldftn, addMethodAdapter),
                Instruction.Create(OpCodes.Newobj, eventHandlerAdapterActionConstructor),
                // remove
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldftn, removeMethodAdapter),
                Instruction.Create(OpCodes.Newobj, eventHandlerAdapterActionConstructor),

                Instruction.Create(OpCodes.Newobj, weakListenerConstructor),

                Instruction.Create(OpCodes.Stfld, weakListenerField)
            });

            targetType.InsertIntoFinalizer(
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, weakListenerField),
                Instruction.Create(OpCodes.Callvirt, _weakListenerReleaseMethod.OnGenericType(weakListenerType))
            );

            foreach (var instructionInfo in eventInfo.Instructions)
            {
                var instruction = instructionInfo.Instruction;
                var instructions = instructionInfo.Collection;

                var index = instructions.IndexOf(instruction) - 2;

                instructions.Insert(index++, Instruction.Create(OpCodes.Ldfld, weakListenerField));
                instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
                index += 1;

                instructions.RemoveAt(index);
                instructions.RemoveAt(index);
                instructions.RemoveAt(index);
                instructions.RemoveAt(index);

                var method = instructionInfo.EventRegistration == EventRegistration.Add ? _weakListenerSubscribeMethod : _weakListenerUnsubscribeMethod;

                instructions.Insert(index, Instruction.Create(OpCodes.Callvirt, method.OnGenericType(weakListenerType)));
            }
        }

        [NotNull]
        private MethodDefinition CreateStaticAddRemoveAdapter([NotNull] EventInfo eventInfo, [NotNull] TypeReference sourceType, [NotNull] GenericInstanceType eventHandlerType, [NotNull] string suffix, [NotNull] MethodReference addOrRemoveMethod)
        {
            var method = new MethodDefinition(eventInfo + suffix, MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, _typeSystem.TypeSystem.VoidReference);

            method.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.In, sourceType));
            method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.In, eventHandlerType));

            method.Body.Instructions.AddRange(
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Callvirt,addOrRemoveMethod),
                Instruction.Create(OpCodes.Ret)
            );

            return method;
        }

        private static void Verify([NotNull, ItemNotNull] IEnumerable<MethodDefinition> methods)
        {
            foreach (var method in methods)
            {
                if (method.IsStatic)
                    throw new WeavingException("static");

                if (method.Parameters.Count != 2)
                    throw new WeavingException("param != 2");

                if (method.Parameters[0].ParameterType.FullName != "System.Object")
                    throw new WeavingException("param1 != object");

                if (!IsEventArgs(method.Parameters[1].ParameterType))
                    throw new WeavingException("param1 != EventArgs");
            }
        }

        private static bool IsEventArgs(TypeReference type)
        {
            if (type == null)
                return false;

            return type.FullName == "System.EventArgs" || IsEventArgs(type.Resolve()?.BaseType);
        }

        private static bool ConsumeAttribute([NotNull] ICustomAttributeProvider attributeProvider)
        {
            const string attributeName = MakeWeakAttributeName;

            var attribute = attributeProvider.GetAttribute(attributeName);

            if (attribute == null)
                return false;

            attributeProvider.CustomAttributes.Remove(attribute);
            return true;
        }

        [NotNull]
        private static EventInfo GetOrAdd([NotNull] IDictionary<EventKey, EventInfo> items, [NotNull] EventKey key)
        {
            if (items.TryGetValue(key, out var info))
                return info;

            var newItem = new EventInfo(key);
            items.Add(key, newItem);

            return newItem;
        }

        private class EventKey : IEquatable<EventKey>
        {
            public EventKey([NotNull] MethodDefinition eventSink, [NotNull] EventDefinition eventDefinition)
            {
                EventSink = eventSink;
                Event = eventDefinition;
            }

            [NotNull]
            public MethodDefinition EventSink { get; }

            [NotNull]
            public EventDefinition Event { get; }

            public override string ToString()
            {
                return ">" + Event.Name + ">" + EventSink.Name;
            }

            #region Equatable

            public bool Equals(EventKey other)
            {
                if (other is null)
                    return false;
                if (ReferenceEquals(this, other))
                    return true;
                return Equals(EventSink, other.EventSink) && Equals(Event, other.Event);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as EventKey);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (EventSink.GetHashCode() * 397) ^ Event.GetHashCode();
                }
            }

            #endregion
        }

        private class EventInfo : EventKey
        {
            public EventInfo([NotNull] EventKey key)
                : base(key.EventSink, key.Event)
            {
            }

            [NotNull, ItemNotNull]
            public IList<InstructionInfo> Instructions { get; } = new List<InstructionInfo>();
        }

        private enum EventRegistration
        {
            Add,
            Remove
        }

        private class InstructionInfo
        {
            public InstructionInfo(EventRegistration eventRegistration, [NotNull] Instruction instruction, [NotNull] IList<Instruction> collection, [NotNull] MethodDefinition method)
            {
                EventRegistration = eventRegistration;
                Instruction = instruction;
                Collection = collection;
                Method = method;
            }

            public EventRegistration EventRegistration { get; }

            [NotNull]
            public Instruction Instruction { get; }

            [NotNull]
            public IList<Instruction> Collection { get; }

            [NotNull]
            public MethodDefinition Method { get; }
        }
    }
}
