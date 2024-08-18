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
        internal static ManualLogSource LogSource;

        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        public static void Initialize()
        {
            LogSource = Logger.CreateLogSource("TurboMode.Preload");
            LogSource.LogInfo("Initialize");
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            // Patcher code here
            LogSource.LogInfo("Hello World");

            var thisAsmPath = typeof(BurstMathInjector).Assembly.Location;
            var tmAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(thisAsmPath), "..", "plugins", "TurboMode", "TurboMode.dll"));

            var targetMethod = assembly.MainModule.GetType("KSP.Sim.impl.TransformFrame")
                .Methods.First(method => method.Name == "ToLocalPosition" && method.Parameters.Count == 2);

            LogSource.LogInfo($"Target method {targetMethod}");

            var toReplace = assembly.MainModule.Types
                .First(t => t.Name == "TransformFrame")
                .Methods
                .First(m => m.Name == "ComputeTransformFromOther");

            LogSource.LogInfo($"toReplace {toReplace}");

            var replacementSrc = tmAssembly
                .MainModule.GetType("TurboMode.MathUtil")
                .Methods.First(method => method.Name == "ComputeTransformFromOther");

            var replacement = assembly.MainModule.ImportReference(replacementSrc);

            LogSource.LogInfo($"replacement {replacement}");

            var instructions = targetMethod.Body.Instructions;
            var newInstructions = new Collection<Instruction>();

            foreach (var instruction in instructions)
            {
                if (instruction.MatchCallOrCallvirt(toReplace))
                {
                    newInstructions.Add(Instruction.Create(OpCodes.Call, replacement));
                    LogSource.LogInfo($"Found call {instruction}");
                    continue;
                }

                newInstructions.Add(instruction);
            }

            instructions.Clear();
            instructions.AddRange(newInstructions);
        }
    }
}
