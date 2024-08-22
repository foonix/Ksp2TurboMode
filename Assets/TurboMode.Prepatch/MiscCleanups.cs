using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections.Generic;
using System.Linq;

namespace TurboMode.Prepatch
{
    public class MiscCleanups : TurboModePrepatch
    {
        public static IEnumerable<string> TargetDLLs { get; private set; } = new[] { "Assembly-CSharp.dll" };

        private static bool enabled;

        public static void Initialize()
        {
            InitSharedResources();

            // Can't get this from TurboModePlugin, because we must avoid loading Assembly-CSharp.dll.
            enabled = config.Bind(
                "General",
                "MiscCleanups",
                true,
                "Miscellaneous small garbage cleanups and performance improvements."
            ).Value;

            if (!enabled)
            {
                logSource.LogInfo("MiscCleanups option is disabled. Skipping preload patching.");
                TargetDLLs = new string[0];
            }
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            Patch_MessageCenter_RecycleMessage(assembly);
        }

        private static void Patch_MessageCenter_RecycleMessage(AssemblyDefinition assembly)
        {
            var targetMethod = assembly
                .MainModule.GetType("KSP.Messages.MessageCenter")
                .Methods.First(method => method.Name == "RecycleMessage");

            var replacement = assembly.MainModule.ImportReference(
                tmAssembly
                .MainModule.GetType("TurboMode.Patches.MiscCleanups")
                .Methods.First(method => method.Name == "ClearMessage")
            );

            ILContext context = new(targetMethod);
            ILCursor cursor = new(context);

            cursor.GotoNext(
                x => x.MatchCallOrCallvirt("KSP.Messages.MessageCenterMessage", "Clear")
            );
            cursor.Remove();
            cursor.Emit(OpCodes.Call, replacement);
        }
    }
}
