using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
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

        static MethodReference transformPoint;
        static MethodReference transformVector;

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

            transformPoint = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.MathUtil")
                .Methods.First(method => method.Name == "TransformPoint")
            );
            transformVector = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.MathUtil")
                .Methods.First(method => method.Name == "TransformVector")
            );

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

            ILContext context = new(targetMethod);
            ILCursor cursor = new(context);

            var toReplace = assembly.MainModule
                .GetType("KSP.Sim.impl.TransformFrame")
                .Methods
                .First(m => m.Name == "ComputeTransformFromOther");

            logSource.LogInfo($"toReplace {toReplace}");

            var replacementSrc = tmAssembly
                .MainModule.GetType("TurboMode.Patches.BurstifyTransformFrames")
                .Methods.First(method => method.Name == "ComputeTransformFromOther");

            var replacement = assembly.MainModule.ImportReference(replacementSrc);

            logSource.LogInfo($"replacement {replacement}");

            cursor.GotoNext(
                x => x.MatchCallOrCallvirt("KSP.Sim.impl.TransformFrame", "ComputeTransformFromOther")
            );
            cursor.Remove();
            cursor.Emit(OpCodes.Call, replacement);

            PatchVectorMultiplyCalls(cursor, "Matrix4x4D", "TransformPoint", transformPoint);
            PatchVectorMultiplyCalls(cursor, "Matrix4x4D", "TransformVector", transformVector);
        }

        private static void PatchVectorMultiplyCalls(ILCursor cursor, string typeFullName, string name, MethodReference replacement)
        {
            cursor.Goto(0);
            if (cursor.TryGotoNext(x => x.MatchCallOrCallvirt(typeFullName, name)))
            {
                logSource.LogInfo($"Patching {cursor.Next}");
                cursor.Index--;
                var secondArgumentLoader = cursor.Next;
                if (!TryConvertLdargToLdarga(secondArgumentLoader, out OpCode convertedOp, out byte which))
                {
                    logSource.LogWarning($"Unknown prior operation {cursor.Previous}");
                    return;
                }
                cursor.Remove();
                cursor.Emit(convertedOp, which);
                cursor.Remove();
                cursor.Emit(OpCodes.Call, replacement);
                // since we switched from a value return to void, put the output arg value back on the stack.
                cursor.Emit(secondArgumentLoader.OpCode, secondArgumentLoader.Operand);
            }
        }

        // Convert ldarg* instrcutions to equivalent ldarga* instructions.
        private static bool TryConvertLdargToLdarga(Instruction instr, out OpCode converted, out byte which)
        {
            if (!instr.MatchLdarg(out int whichArg))
            {
                converted = OpCodes.No;
                which = 0;
                return false;
            }
            logSource.LogInfo(OpCodes.Ldarga_S.OperandType.ToString());

            converted = OpCodes.Ldarga_S;
            which = (byte)whichArg;
            return true;
        }
    }
}