namespace WeakEventHandler.Fody
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;

    using FodyTools;

    using global::Fody;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Rocks;

    public class ModuleWeaver : AbstractModuleWeaver
    {
        public override void Execute()
        {
            Debugger.Launch();

            var methods = ModuleDefinition
                .GetTypes()
                .Where(c => c.IsClass)
                .SelectMany(c => c.GetMethods())
                .Where(ConsumeLazyAttribute)
                .ToArray();

            Verify(methods);

        }

        private void Verify(IEnumerable<MethodDefinition> methods)
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

        public override IEnumerable<string> GetAssembliesForScanning() => Enumerable.Empty<string>();

        public override bool ShouldCleanReference => true;

        private static bool IsEventArgs(TypeReference type)
        {
            if (type == null)
                return false;

            return type.FullName == "System.EventArgs" || IsEventArgs(type.Resolve()?.BaseType);
        }

        private static bool ConsumeLazyAttribute([NotNull] ICustomAttributeProvider attributeProvider)
        {
            const string attributeName = "WeakEventHandler.MakeWeakAttribute";

            var attribute = attributeProvider.GetAttribute(attributeName);

            if (attribute == null)
                return false;

            attributeProvider.CustomAttributes.Remove(attribute);
            return true;
        }

    }
}
