using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Cil;
using MonoMod.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TurboMode
{
    /// <summary>
    /// Various Matrix4x4D operations can happen 10k+ times per frame, so even 0.001ms overhead from IL hooks or 
    /// adding additional calls in the chain can negate the performance gains from switching them to Burst.
    /// Zero-overhead modification to make this work.
    /// So we directly patch the callers' IL with Cecil.
    /// </summary>
    public class BurstMathInjector
    {
        static bool enabled;

        static ConfigFile config;
        static ManualLogSource logSource;
        static AssemblyDefinition tmAssembly;

        public static IEnumerable<string> TargetDLLs { get; private set; } = new[] { "Assembly-CSharp.dll" };

        public static void Initialize()
        {
            logSource = Logger.CreateLogSource("TurboMode.Preload");
            logSource.LogInfo("BurstMath Initialize()");

            var configPath = Utility.CombinePaths(Paths.ConfigPath, "TurboMode.cfg");
            config = new ConfigFile(configPath, saveOnInit: false)
            {
                // We have to Bind() here to get the value, but avoid writing because we don't have all of the settings.
                SaveOnConfigSet = false
            };

            // Can't get this from TurboModePlugin, because we must avoid loading Assembly-CSharp.dll.
            enabled = config.Bind(
                "General",
                "BurstMath",
                true,
                "Use Unity Burst code to speed up certain math operations, such as floating origin and reference frame calculations."
            ).Value;

            if (!enabled)
            {
                logSource.LogInfo("BurstMath option is disabled. Skipping preload patching.");
                TargetDLLs = new string[0];
            }
        }

        public static void Finish()
        {
            // free up unused
            config = null;
            logSource = null;
            tmAssembly = null;
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            // Get our main assembly signatures without actually loading it.
            var thisAsmPath = typeof(BurstMathInjector).Assembly.Location;
            tmAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(thisAsmPath), "..", "plugins", "TurboMode", "TurboMode.dll"));


            PatchComputeTransformFromOtherCaller(
                assembly,
                assembly.MainModule.GetType("KSP.Sim.impl.TransformFrame")
                .Methods.First(method => method.Name == "ToLocalPosition" && method.Parameters.Count == 2)
            );
            PatchComputeTransformFromOtherCaller(
                assembly,
                assembly.MainModule.GetType("KSP.Sim.impl.TransformFrame")
                .Methods.First(method => method.Name == "ToLocalVector" && method.Parameters.Count == 2)
            );
            PatchComputeTransformFromOtherCaller(
                assembly,
                assembly.MainModule.GetType("KSP.Sim.impl.TransformFrame")
                .Methods.First(method => method.Name == "ToLocalTransformationMatrix")
            );
        }

        private static void PatchComputeTransformFromOtherCaller(
            AssemblyDefinition assembly,
            MethodDefinition targetMethod
            )
        {
            logSource.LogInfo($"Target method {targetMethod}");

            var toReplace = assembly.MainModule.Types
                .First(t => t.Name == "TransformFrame")
                .Methods
                .First(m => m.Name == "ComputeTransformFromOther");

            logSource.LogInfo($"toReplace {toReplace}");

            var replacementSrc = tmAssembly
                .MainModule.GetType("TurboMode.Patches.BurstifyTransformFrames")
                .Methods.First(method => method.Name == "ComputeTransformFromOther");

            var replacement = assembly.MainModule.ImportReference(replacementSrc);

            logSource.LogInfo($"replacement {replacement}");

            var instructions = targetMethod.Body.Instructions;
            var newInstructions = new Collection<Instruction>();

            foreach (var instruction in instructions)
            {
                if (instruction.MatchCallOrCallvirt(toReplace))
                {
                    newInstructions.Add(Instruction.Create(OpCodes.Call, replacement));
                    logSource.LogInfo($"Found call {instruction}");
                    continue;
                }

                newInstructions.Add(instruction);
            }

            instructions.Clear();
            instructions.AddRange(newInstructions);
        }
    }
}
