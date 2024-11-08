using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections.Generic;
using System.Linq;

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
                "Improved code for vessel resource flow request processing and related processes."
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

            var groupCacheData = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.Models.ResourceContainerGroupCache")
            );
            var rcgType = assembly.MainModule.GetType("KSP.Sim.ResourceSystem.ResourceContainerGroup");
            rcgType.Fields.Add(new FieldDefinition("resourceContainerGroupCache", FieldAttributes.Public, groupCacheData));

            var rcgsGetCachedCapacityUnits = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.Patches.FlowRequests")
                .Methods.First(method => method.Name == "GetResourceSeqeuenceCapacityUnitsCached")
            );
            var rcgsGetCachedStoredUnits = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.Patches.FlowRequests")
                .Methods.First(method => method.Name == "GetResourceSequenceStoredUnitsCached")
            );
            var rcgGetCachedCapacityUnits = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.Patches.FlowRequests")
                .Methods.First(method => method.Name == "GetResourceGroupCapacityUnitsCached")
            );
            var rcgGetCachedStoredUnits = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.Patches.FlowRequests")
                .Methods.First(method => method.Name == "GetResourceGroupStoredUnitsCached")
            );

            var rcgsOriginalGetCapacity = assembly.MainModule.GetType("KSP.Sim.ResourceSystem.ResourceContainerGroupSequence")
                .Methods.First(method => method.Name == "GetResourceCapacityUnits" && method.Parameters.Count == 1);
            var rcgsOriginalGetStored = assembly.MainModule.GetType("KSP.Sim.ResourceSystem.ResourceContainerGroupSequence")
                .Methods.First(method => method.Name == "GetResourceStoredUnits" && method.Parameters.Count == 1);
            var rcgOriginalGetCapacity = assembly.MainModule.GetType("KSP.Sim.ResourceSystem.ResourceContainerGroup")
                .Methods.First(method => method.Name == "GetResourceCapacityUnits" && method.Parameters.Count == 1);
            var rcgOriginalGetStored = assembly.MainModule.GetType("KSP.Sim.ResourceSystem.ResourceContainerGroup")
                .Methods.First(method => method.Name == "GetResourceStoredUnits" && method.Parameters.Count == 1);

            // Normally, my preference here would be to patch the callers instead of the callee.
            // That would allow me to better vet which situations the caller can handle potentially stale data.
            // However, most of the UI callers are actually calling IResourceContainer, whicih
            // could be a RCGS, RCG, or ResourceContainer directly.  This complicates patching the callers.
            // An entire switch block would need to be injected, and that could add overhead to the caller.

            // If there turns out to be some case (in a mod, maybe) that absoultely needs present data,
            // consider exposing the sync method instead.

            OverrideBodyWithCallTo(rcgsOriginalGetCapacity, rcgsGetCachedCapacityUnits);
            OverrideBodyWithCallTo(rcgsOriginalGetStored, rcgsGetCachedStoredUnits);
            OverrideBodyWithCallTo(rcgOriginalGetCapacity, rcgGetCachedCapacityUnits);
            OverrideBodyWithCallTo(rcgOriginalGetStored, rcgGetCachedStoredUnits);
        }
    }
}