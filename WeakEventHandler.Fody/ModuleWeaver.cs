namespace WeakEventHandler.Fody
{
    using System.Collections.Generic;
    using System.Linq;

    using FodyTools;

    public class ModuleWeaver : AbstractModuleWeaver
    {
        public override bool ShouldCleanReference => true;

        public override IEnumerable<string> GetAssembliesForScanning() => Enumerable.Empty<string>();

        public override void Execute()
        {
            // System.Diagnostics.Debugger.Launch(); // to enable inline debugging

            WeakEventHandlerWeaver.Weave(ModuleDefinition, this, this);
        }
    }
}
