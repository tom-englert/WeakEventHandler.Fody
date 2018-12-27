namespace WeakEventHandler.Fody
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    using FodyTools;

    using global::Fody;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    public class ModuleWeaver : AbstractModuleWeaver
    {
        private TypeReference _action2Type;
        private MethodReference _action2Constructor;
        private TypeReference _eventHandlerType;

        private TypeDefinition _weakListenerType;
        private MethodDefinition _weakListenerConstructor;
        private MethodDefinition _weakListenerSubscribeMethod;
        private MethodDefinition _weakListenerUnsubscribeMethod;

        public override bool ShouldCleanReference => false;

        public override IEnumerable<string> GetAssembliesForScanning() => Enumerable.Empty<string>();

        public override void Execute()
        {
            Debugger.Launch();

            if (!ModuleDefinition.TryGetTypeReference("WeakEventHandler.MakeWeakAttribute", out var makeWeakAttributeReference))
                return;

            var makeWeakAttribute = makeWeakAttributeReference.Resolve();

            var listenerSource = makeWeakAttribute.Module.Types.Single(t => t.Name == "WeakEventListener`3");

            var codeImporter = new CodeImporter(ModuleDefinition);

            _weakListenerType = codeImporter.Import(listenerSource);
            _weakListenerConstructor = _weakListenerType.GetConstructors().Single(ctor => ctor.Parameters.Count == 3);

            var weakListenerMethods = _weakListenerType.GetMethods();

            _weakListenerSubscribeMethod = weakListenerMethods.Single(method => method.Name == "Subscribe");
            _weakListenerUnsubscribeMethod = weakListenerMethods.Single(method => method.Name == "Unsubscribe");

            _action2Type = this.ImportType<Action<object, EventArgs>>();
            _action2Constructor = this.ImportMethod<Action<object, EventArgs>, object, IntPtr>(".ctor");

            _eventHandlerType = this.ImportType<EventHandler<EventArgs>>();

            var methods = ModuleDefinition
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
            var sourceType = sourceEvent.DeclaringType;

            var eventArgsType = ModuleDefinition.ImportReference(eventSinkMethod.Parameters[1].ParameterType);

            var weakListenerType = _weakListenerType.MakeGenericInstanceType(sourceType, targetType, eventArgsType);
            var weakListenerConstructor = _weakListenerConstructor.OnGenericType(weakListenerType);

            var eventSinkActionType = _action2Type.MakeGenericInstanceType(TypeSystem.ObjectReference, eventArgsType);
            var eventSinkActionConstructor = _action2Constructor.OnGenericType(eventSinkActionType);

            var eventHandlerType = _eventHandlerType.MakeGenericInstanceType(eventArgsType);
            var eventHandlerAdapterActionType = _action2Type.MakeGenericInstanceType(sourceType, eventHandlerType);
            var eventHandlerAdapterActionConstructor = _action2Constructor.OnGenericType(eventHandlerAdapterActionType);

            var weakListenerField = new FieldDefinition($"<{eventInfo}>_WeakEventListener", FieldAttributes.InitOnly | FieldAttributes.Private | FieldAttributes.NotSerialized, weakListenerType);
            targetType.Fields.Add(weakListenerField);

            var addMethodAdapter = CreateStaticAddRemoveAdapter(eventInfo, sourceType, eventHandlerType, "_Add", sourceEvent.AddMethod);
            var removeMethodAdapter = CreateStaticAddRemoveAdapter(eventInfo, sourceType, eventHandlerType, "_Remove", sourceEvent.RemoveMethod);

            targetType.Methods.Add(addMethodAdapter);
            targetType.Methods.Add(removeMethodAdapter);

            targetType.InsertIntoConstructors(() => new[]
            {
                Instruction.Create(OpCodes.Ldarg_0),

                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldftn, eventSinkMethod),
                Instruction.Create(OpCodes.Newobj, eventSinkActionConstructor),

                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldftn, addMethodAdapter),
                Instruction.Create(OpCodes.Newobj, eventHandlerAdapterActionConstructor),

                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldftn, removeMethodAdapter),
                Instruction.Create(OpCodes.Newobj, eventHandlerAdapterActionConstructor),

                Instruction.Create(OpCodes.Newobj, weakListenerConstructor),
                Instruction.Create(OpCodes.Stfld, weakListenerField)
            });

            targetType.InsertIntoFinalizer(
                
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

                instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, method.OnGenericType(weakListenerType)));
            }
        }

        [NotNull]
        private MethodDefinition CreateStaticAddRemoveAdapter([NotNull] EventInfo eventInfo, [NotNull] TypeDefinition sourceType, [NotNull] GenericInstanceType eventHandlerType, [NotNull] string suffix, [NotNull] MethodDefinition addOrRemoveMethod)
        {
            var method = new MethodDefinition(eventInfo + suffix, MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, TypeSystem.VoidReference);

            method.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.In, sourceType));
            method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.In, eventHandlerType));

            method.Body.Instructions.AddRange(
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Callvirt, ModuleDefinition.ImportReference(addOrRemoveMethod)),
                Instruction.Create(OpCodes.Ret)
            );

            return method;
        }

        private void Verify([NotNull, ItemNotNull] IEnumerable<MethodDefinition> methods)
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
            const string attributeName = "WeakEventHandler.MakeWeakAttribute";

            var attribute = attributeProvider.GetAttribute(attributeName);

            if (attribute == null)
                return false;

            attributeProvider.CustomAttributes.Remove(attribute);
            return true;
        }

        [NotNull]
        private static EventInfo GetOrAdd([NotNull] Dictionary<EventKey, EventInfo> items, [NotNull] EventKey key)
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
                return EventSink.Name + "_" + Event.Name;
            }

            #region Equatable

            public bool Equals(EventKey other)
            {
                if (ReferenceEquals(null, other))
                    return false;
                if (ReferenceEquals(this, other))
                    return true;
                return Equals(EventSink, other.EventSink) && Equals(Event, other.Event);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != this.GetType())
                    return false;
                return Equals((EventKey)obj);
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
