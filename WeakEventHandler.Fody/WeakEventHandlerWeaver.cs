﻿namespace WeakEventHandler.Fody
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Linq;

    using FodyTools;

    using global::Fody;

    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    class WeakEventHandlerWeaver
    {
        private const string MakeWeakAttributeName = "WeakEventHandler.MakeWeakAttribute";

        private readonly TypeReference _action2Type;
        private readonly MethodReference _action2Constructor;
        private readonly TypeReference _action3Type;
        private readonly MethodReference _action3Constructor;
        private readonly CustomAttribute _generatedCodeAttribute;

        private readonly TypeDefinition _weakAdapterType;
        private readonly MethodDefinition _weakAdapterConstructor;
        private readonly MethodDefinition _weakAdapterSubscribeMethod;
        private readonly MethodDefinition _weakAdapterUnsubscribeMethod;
        private readonly MethodDefinition _weakAdapterReleaseMethod;

        private readonly ModuleDefinition _moduleDefinition;
        private readonly ITypeSystem _typeSystem;
        private readonly ILogger _logger;
        private readonly TypeDefinition _eventTargetInterface;
        private readonly CodeImporter _codeImporter;

        public static void Weave(ModuleDefinition moduleDefinition, ITypeSystem typeSystem, ILogger logger)
        {
            if (!moduleDefinition.TryGetTypeReference(MakeWeakAttributeName, out var makeWeakAttributeReference))
            {
                logger.LogWarning("No reference to WeakEventHandler.dll found. Weaving skipped.");
                return;
            }

            new WeakEventHandlerWeaver(moduleDefinition, typeSystem, logger, makeWeakAttributeReference).Weave();
        }

        private WeakEventHandlerWeaver(ModuleDefinition moduleDefinition, ITypeSystem typeSystem, ILogger logger, TypeReference makeWeakAttributeReference)
        {
            _moduleDefinition = moduleDefinition;
            _typeSystem = typeSystem;
            _logger = logger;

            var makeWeakAttribute = makeWeakAttributeReference.Resolve();

            var helperTypes = makeWeakAttribute.Module.Types;

            _codeImporter = new CodeImporter(moduleDefinition) { NamespaceDecorator = value => "<>" + value };

            _weakAdapterType = _codeImporter.Import(helperTypes.Single(t => t.Name == "WeakEventHandlerFodyWeakEventAdapter`4"));
            _eventTargetInterface = _codeImporter.Import(helperTypes.Single(t => t.Name == "IWeakEventHandlerFodyWeakEventTarget"));

            _weakAdapterConstructor = _weakAdapterType.GetConstructors().Single(ctor => ctor.Parameters.Count == 4);
            _generatedCodeAttribute = _weakAdapterType.CustomAttributes.Single(attr => attr.AttributeType.Name == nameof(GeneratedCodeAttribute));

            var weakAdapterMethods = _weakAdapterType.GetMethods().ToList();

            _weakAdapterSubscribeMethod = weakAdapterMethods.Single(method => method.Name == "Subscribe");
            _weakAdapterUnsubscribeMethod = weakAdapterMethods.Single(method => method.Name == "Unsubscribe");
            _weakAdapterReleaseMethod = weakAdapterMethods.Single(method => method.Name == "Release");

            _action2Type = typeSystem.ImportType<Action<object, EventArgs>>();
            _action2Constructor = typeSystem.ImportMethod<Action<object, EventArgs>, object, IntPtr>(".ctor");
            _action3Type = typeSystem.ImportType<Action<Type, object, EventArgs>>();
            _action3Constructor = typeSystem.ImportMethod<Action<Type, object, EventArgs>, object, IntPtr>(".ctor");
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

        private void Weave(ICollection<MethodDefinition> methods)
        {
            var eventInfos = new Dictionary<EventKey, EventInfo>();

            foreach (var method in methods)
            {
                Analyze(method, eventInfos);
            }

            _codeImporter.ILMerge();

            Verify(methods, eventInfos);

            var unsubscribeMethods = new Dictionary<TypeDefinition, MethodDefinition>();

            foreach (var eventInfo in eventInfos.Values)
            {
                Weave(eventInfo, unsubscribeMethods);
            }
        }

        private void Verify(IEnumerable<MethodDefinition> eventHandlerMethods, Dictionary<EventKey, EventInfo> eventInfos)
        {
            var unmappedMethods = eventHandlerMethods
                .Where(method => eventInfos.Values.All(eventInfo => eventInfo?.EventSinkDefinition != method))
                .ToList();

            foreach (var method in unmappedMethods)
            {
                _logger.LogWarning($"Method {method} has a MakeWeak attribute, but is not attached to any event. This method will be ignored!");
            }
        }

        private void Analyze(MethodDefinition eventHandlerMethod, Dictionary<EventKey, EventInfo> eventInfos)
        {
            var type = eventHandlerMethod.DeclaringType;

            foreach (var m in type.Methods)
            {
                var instructions = m.Body.Instructions;

                foreach (var instruction in instructions.Where(instr => (instr.Operand as MethodReference)?.Resolve() == eventHandlerMethod))
                {
                    var methodReference = (MethodReference)instruction.Operand;
                    var createEventHandler = instruction.Next;

                    if ((createEventHandler == null) || createEventHandler.OpCode != OpCodes.Newobj)
                        continue;

                    var callAddOrRemoveEvent = createEventHandler.Next;

                    if (callAddOrRemoveEvent?.OpCode != OpCodes.Callvirt)
                        continue;

                    var addOrRemoveMethod = (callAddOrRemoveEvent?.Operand as MethodReference)?.Resolve();

                    var sourceType = addOrRemoveMethod?.DeclaringType;

                    var sourceEvent = sourceType?.Events?.SingleOrDefault(e => e.AddMethod == addOrRemoveMethod || e.RemoveMethod == addOrRemoveMethod);

                    if (sourceEvent == null)
                        continue;

                    var eventInfo = GetOrAdd(eventInfos, new EventKey(methodReference, sourceEvent, (MethodReference)createEventHandler.Operand));

                    var eventRegistration = sourceEvent.AddMethod == addOrRemoveMethod ? EventRegistration.Add : EventRegistration.Remove;

                    eventInfo.Instructions.Add(new InstructionInfo(eventRegistration, instruction, instructions));
                }
            }
        }

        private void Weave(EventInfo eventInfo, Dictionary<TypeDefinition, MethodDefinition> unsubscribeMethods)
        {
            _logger.LogInfo($"Weaving the weak adapter into {eventInfo.Event} and {eventInfo.EventSink}");

            var eventSinkMethod = eventInfo.EventSink;
            var targetTypeReference = eventSinkMethod.DeclaringType;
            var targetType = targetTypeReference.Resolve();
            var unsubscribeMethod = GetOrCreateUnsubscribeMethod(targetType, unsubscribeMethods);

            var sourceEvent = eventInfo.Event;
            var sourceType = _moduleDefinition.ImportReference(sourceEvent.DeclaringType);

            var eventArgsType = _moduleDefinition.ImportReference(eventSinkMethod.Parameters[1].ParameterType);

            var eventHandlerType = eventInfo.EventHandlerConstructor.DeclaringType;
            var weakAdapterType = _weakAdapterType.MakeGenericInstanceType(sourceType, targetTypeReference, eventArgsType, eventHandlerType);
            var weakAdapterConstructor = _weakAdapterConstructor.OnGenericType(weakAdapterType);
            var weakAdapterSubscribeMethod = _weakAdapterSubscribeMethod.OnGenericType(weakAdapterType);
            var weakAdapterUnsubscribeMethod = _weakAdapterUnsubscribeMethod.OnGenericType(weakAdapterType);
            var weakAdapterReleaseMethod = _weakAdapterReleaseMethod.OnGenericType(weakAdapterType);

            var eventSinkActionType = _action3Type.MakeGenericInstanceType(targetTypeReference, _typeSystem.TypeSystem.ObjectReference, eventArgsType);
            var eventSinkActionConstructor = _action3Constructor.OnGenericType(eventSinkActionType);

            var eventHandlerAdapterActionType = _action2Type.MakeGenericInstanceType(sourceType, eventHandlerType);
            var eventHandlerAdapterActionConstructor = _action2Constructor.OnGenericType(eventHandlerAdapterActionType);

            var weakAdapterField = new FieldDefinition($"{eventInfo}>Adapter", FieldAttributes.InitOnly | FieldAttributes.Private | FieldAttributes.NotSerialized, weakAdapterType);
            weakAdapterField.CustomAttributes.Add(_generatedCodeAttribute);
            targetType.Fields.Add(weakAdapterField);
            var weakAdapterFieldReference = new FieldReference(weakAdapterField.Name, weakAdapterType, targetTypeReference);

            var addMethodAdapter = CreateStaticAddRemoveMethod(sourceType, eventHandlerType, eventInfo + "!>Add", _moduleDefinition.ImportReference(sourceEvent.AddMethod));
            var removeMethodAdapter = CreateStaticAddRemoveMethod(sourceType, eventHandlerType, eventInfo + "!>Remove", _moduleDefinition.ImportReference(sourceEvent.RemoveMethod));

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
                Instruction.Create(OpCodes.Ldftn, addMethodAdapter.OnGenericTypeOrSelf(targetTypeReference)),
                Instruction.Create(OpCodes.Newobj, eventHandlerAdapterActionConstructor),
                // remove
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldftn, removeMethodAdapter.OnGenericTypeOrSelf(targetTypeReference)),
                Instruction.Create(OpCodes.Newobj, eventHandlerAdapterActionConstructor),

                Instruction.Create(OpCodes.Newobj, weakAdapterConstructor),

                Instruction.Create(OpCodes.Stfld, weakAdapterFieldReference)
            });

            unsubscribeMethod.Body.Instructions.InsertRange(0,
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, weakAdapterFieldReference),
                Instruction.Create(OpCodes.Callvirt, weakAdapterReleaseMethod)
            );

            foreach (var instructionInfo in eventInfo.Instructions)
            {
                var instruction = instructionInfo.Instruction;
                var instructions = instructionInfo.Collection;

                int indexOfEventHandler;
                var index = indexOfEventHandler = instructions.IndexOf(instruction);

                var stackSize = -2; // new EventHandler() takes 2 args

                while (stackSize < 0)
                {
                    index -= 1;
                    if (index < 0)
                        throw new InvalidOperationException("Unable to crawl back call stack.");

                    instructions[index].ComputeStackDelta(ref stackSize);
                }

                // keep the top instruction, maybe used as jump target or sequence point in debug info.
                var topInstruction = instructions[index++].ReplaceWith(OpCodes.Ldarg_0);

                instructions.Insert(index++, Instruction.Create(OpCodes.Ldfld, weakAdapterFieldReference));
                instructions.Insert(index, topInstruction);

                index = indexOfEventHandler + 1;

                instructions.RemoveAt(index, OpCodes.Ldarg_0);
                instructions.RemoveAt(index, OpCodes.Ldftn);
                instructions.RemoveAt(index, OpCodes.Newobj);
                instructions.RemoveAt(index, OpCodes.Callvirt);

                var method = instructionInfo.EventRegistration == EventRegistration.Add ? weakAdapterSubscribeMethod : weakAdapterUnsubscribeMethod;

                instructions.Insert(index, Instruction.Create(OpCodes.Callvirt, method));
            }
        }

        private MethodDefinition GetOrCreateUnsubscribeMethod(TypeDefinition targetType, Dictionary<TypeDefinition, MethodDefinition> unsubscribeMethods)
        {
            if (unsubscribeMethods.TryGetValue(targetType, out var unsubscribeMethod))
            {
                return unsubscribeMethod;
            }

            var attributes = MethodAttributes.Private
                             | MethodAttributes.Final
                             | MethodAttributes.Virtual
                             | MethodAttributes.HideBySig
                             | MethodAttributes.NewSlot;

            unsubscribeMethod = new MethodDefinition(">WeakEvents>Unsubscribe", attributes, _typeSystem.TypeSystem.VoidReference)
            {
                HasThis = true
            };

            unsubscribeMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            unsubscribeMethods.Add(targetType, unsubscribeMethod);

            targetType.Methods.Add(unsubscribeMethod);
            var interfaceImplementation = new InterfaceImplementation(_eventTargetInterface);
            targetType.Interfaces.Add(interfaceImplementation);
            var interfaceMethod = _eventTargetInterface.GetMethods().Single();
            unsubscribeMethod.Overrides.Add(interfaceMethod);

            targetType.InsertIntoFinalizer(
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Callvirt, interfaceMethod)
            );

            return unsubscribeMethod;
        }

        private MethodDefinition CreateStaticAddRemoveMethod(TypeReference sourceType, TypeReference eventHandlerType, string name, MethodReference addOrRemoveMethod)
        {
            var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, _typeSystem.TypeSystem.VoidReference);
            method.CustomAttributes.Add(_generatedCodeAttribute);

            method.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.In, sourceType));
            method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.In, eventHandlerType));

            method.Body.Instructions.AddRange(
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Callvirt, addOrRemoveMethod),
                Instruction.Create(OpCodes.Ret)
            );

            return method;
        }

        private static void Verify(IEnumerable<MethodDefinition> eventHandlerMethods)
        {
            foreach (var method in eventHandlerMethods)
            {
                if (method.IsStatic)
                    throw new WeavingException($"MakeWeak attribute found on static method {method}. Static event handlers are not supported");

                if (method.Parameters.Count != 2
                    || method.Parameters[0].ParameterType.FullName != "System.Object"
                    || !IsEventArgs(method.Parameters[1].ParameterType)
                    || method.ReturnType.Name != typeof(void).Name)
                    throw new WeavingException($"MakeWeak attribute found on method {method}. The method does not have the void (object, EventArgs) signature.");
            }
        }

        private static bool IsEventArgs(TypeReference? type)
        {
            if (type == null)
                return false;

            return type.FullName == "System.EventArgs" || IsEventArgs(type.Resolve()?.BaseType);
        }

        private static bool ConsumeAttribute(ICustomAttributeProvider attributeProvider)
        {
            const string attributeName = MakeWeakAttributeName;

            var attribute = attributeProvider.GetAttribute(attributeName);

            if (attribute == null)
                return false;

            attributeProvider.CustomAttributes.Remove(attribute);
            return true;
        }

        private static EventInfo GetOrAdd(IDictionary<EventKey, EventInfo> items, EventKey key)
        {
            if (items.TryGetValue(key, out var info))
                return info;

            var newItem = new EventInfo(key);
            items.Add(key, newItem);

            return newItem;
        }

        private class EventKey : IEquatable<EventKey>
        {
            public EventKey(MethodReference eventSink, EventDefinition eventDefinition, MethodReference eventHandlerConstructor)
            {
                EventSink = eventSink;
                Event = eventDefinition;
                EventHandlerConstructor = eventHandlerConstructor;
                EventSinkDefinition = eventSink.Resolve();
            }

            public MethodDefinition EventSinkDefinition { get; }
            public MethodReference EventSink { get; }
            public EventDefinition Event { get; }
            public MethodReference EventHandlerConstructor { get; }

            public override string ToString()
            {
                return ">" + Event.Name + ">" + EventSink.Name;
            }

            #region Equatable

            public bool Equals(EventKey? other)
            {
                if (other is null)
                    return false;
                if (ReferenceEquals(this, other))
                    return true;
                return Equals(EventSinkDefinition, other.EventSinkDefinition) && Equals(Event, other.Event);
            }

            public override bool Equals(object? obj)
            {
                return Equals(obj as EventKey);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (EventSinkDefinition.GetHashCode() * 397) ^ Event.GetHashCode();
                }
            }

            #endregion
        }

        private class EventInfo : EventKey
        {
            public EventInfo(EventKey key)
                : base(key.EventSink, key.Event, key.EventHandlerConstructor)
            {
            }

            public IList<InstructionInfo> Instructions { get; } = new List<InstructionInfo>();
        }

        private enum EventRegistration
        {
            Add,
            Remove
        }

        private class InstructionInfo
        {
            public InstructionInfo(EventRegistration eventRegistration, Instruction instruction, IList<Instruction> collection)
            {
                EventRegistration = eventRegistration;
                Instruction = instruction;
                Collection = collection;
            }

            public EventRegistration EventRegistration { get; }

            public Instruction Instruction { get; }

            public IList<Instruction> Collection { get; }
        }
    }
}
