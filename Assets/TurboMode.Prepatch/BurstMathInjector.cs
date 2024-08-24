using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections.Generic;
using System.Linq;

namespace TurboMode.Prepatch
{
    /// <summary>
    /// Various Matrix4x4D operations can happen 10k+ times per frame, so even 0.001ms overhead from IL hooks or 
    /// adding additional calls in the chain can negate the performance gains from switching them to Burst.
    /// Zero-overhead modification to make this work.
    /// So we directly patch the callers' IL with Cecil.
    /// </summary>
    public class BurstMathInjector : TurboModePrepatch
    {
        static bool enabled;

        static MethodReference transformPoint;
        static MethodReference transformVector;

        public static IEnumerable<string> TargetDLLs { get; private set; } = new[] { "Assembly-CSharp.dll" };

        public static void Initialize()
        {
            InitSharedResources();

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
            CleanupSharedResources();
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
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

            Patch_PartBehavior_IWaterDetectObject(assembly);
            Patch_TransformFrame_RecalculateLocalMatricies(assembly);
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

        private static void Patch_PartBehavior_IWaterDetectObject(AssemblyDefinition assembly)
        {
            var targetMethod = assembly.MainModule.GetType("KSP.Sim.impl.PartBehavior")
                .Methods.First(method => method.Name == "KSP.Rendering.Planets.WaterManager.IWaterDetectObject.GetPosition");

            ILContext context = new(targetMethod);
            ILCursor cursor = new(context);

            // original:
            // IL_0134: call valuetype Vector3d Vector3d::op_Addition(valuetype Vector3d, valuetype Vector3d)
            // IL_0139: call instance valuetype Vector3d Matrix4x4D::TransformPoint(valuetype Vector3d)
            // IL_013e: stloc.s 16
            cursor.GotoNext(x => x.MatchCallOrCallvirt("Matrix4x4D", "TransformPoint"));
            cursor.Remove();
            cursor.Index++; // keep stloc.s 16.
            cursor.Emit(OpCodes.Ldloca_S, (byte)16); // operate on the storage in-place.
            cursor.Emit(OpCodes.Call, transformPoint);
        }

        private static void Patch_TransformFrame_RecalculateLocalMatricies(AssemblyDefinition assembly)
        {
            var transformFrameType = assembly.MainModule.GetType("KSP.Sim.impl.TransformFrame");
            var targetMethod = transformFrameType
                .Methods.First(method => method.Name == "RecalculateLocalMatricies");

            var initTrs = assembly.MainModule.ImportReference(tmAssembly
                .MainModule.GetType("TurboMode.MathUtil")
                .Methods.First(method => method.Name == "CreateTrsMatrices")
            );

            var _localMatrix = transformFrameType.Fields.First(f => f.Name == "_localMatrix");
            var _localMatrixInverse = transformFrameType.Fields.First(f => f.Name == "_localMatrixInverse");

            var vectorVar = new VariableDefinition(assembly.MainModule.GetType("Vector3d"));
            targetMethod.Body.Variables.Add(vectorVar); // local 0
            var quaternionVar = new VariableDefinition(assembly.MainModule.GetType("QuaternionD"));
            targetMethod.Body.Variables.Add(quaternionVar); // local 1

            ILContext context = new(targetMethod);
            ILCursor cursor = new(context);

            cursor.Remove(); // ldarg.0 for later _localMatrix stfld we are replacing

            cursor.GotoNext(x => x.MatchCallOrCallvirt("Matrix4x4D", "TRS"));
            cursor.RemoveRange(7); // up to clearing the dirty flag
            // local position/rotation are values on the stack at this point
            cursor.Emit(OpCodes.Stloc_1); // QuaternionD localRotation
            cursor.Emit(OpCodes.Stloc_0); // Vector3d localPosition
            cursor.Emit(OpCodes.Ldloca_S, (byte)0);
            cursor.Emit(OpCodes.Ldloca_S, (byte)1);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldflda, _localMatrix);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldflda, _localMatrixInverse);
            cursor.Emit(OpCodes.Call, initTrs);
        }
    }
}
