namespace WeakEventHandler.Fody
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    using FodyTools;

    using JetBrains.Annotations;

    public class ModuleWeaver : AbstractModuleWeaver
    {
        public override bool ShouldCleanReference => true;

        [NotNull]
        public override IEnumerable<string> GetAssembliesForScanning() => Enumerable.Empty<string>();

        public override void Execute()
        {
            // Debugger.Launch();

            WeakEventHandlerWeaver.Weave(ModuleDefinition, this);
        }
    }
}
