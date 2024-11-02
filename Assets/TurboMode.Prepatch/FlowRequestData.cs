using Mono.Cecil;
using System.Collections.Generic;

namespace TurboMode.Prepatch
{

    public class FlowRequestData : TurboModePrepatch
    {

        public static IEnumerable<string> TargetDLLs { get; private set; } = new[] { "Assembly-CSharp.dll" };

        private static bool enabled;

        public static void Initialize()
        {
            InitSharedResources();

            // Can't get this from TurboModePlugin, because we must avoid loading Assembly-CSharp.dll.
            enabled = config.Bind(
                "General",
                "EnableFlowRequestOptimizations",
                true,
                "Rewirte vessel resource flow request processing."
            ).Value;

            if (!enabled)
            {
                logSource.LogInfo("EnableFlowRequestOptimizations option is disabled. Skipping preload patching.");
                TargetDLLs = new string[0];
            }
        }

        public static void Finish()
        {
            CleanupSharedResources();
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            var flowRequestData = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.Patches.FlowRequests")
            );

            var rfrmType = assembly.MainModule.GetType("KSP.Sim.ResourceSystem.ResourceFlowRequestManager");
            rfrmType.Fields.Add(new FieldDefinition("turboModeFlowRequestData", FieldAttributes.Public, flowRequestData));
        }
    }
}