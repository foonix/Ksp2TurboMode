using KSP.Sim;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Profiling;

namespace TurboMode.Patches
{
    public class BurstifyTransformFrames
    {
        private static readonly ProfilerMarker s_ComputeTransformFromOther = new("ComputeTransformFromOther");

        public static List<IDetour> MakeHooks() => new()
        {
            new Hook(
                typeof(TransformFrame).GetMethod("ComputeTransformFromOther", BindingFlags.NonPublic | BindingFlags.Instance),
                (Func<Func<TransformFrame, ITransformFrame, Matrix4x4D>, TransformFrame, ITransformFrame, Matrix4x4D>)Patch_ComputeTransformFromOther),
        };

        private static Matrix4x4D Patch_ComputeTransformFromOther(Func<TransformFrame, ITransformFrame, Matrix4x4D> orig, TransformFrame frame, ITransformFrame other)
        {
            s_ComputeTransformFromOther.Begin();
            Matrix4x4D ret;
            if (TurboModePlugin.testModeEnabled)
            {
                // there are too many of these to really log,
                // but leaving this here to catch in a breakpoint.
                ret = orig(frame, other);
                var mycalc = MathUtil.ComputeTransformFromOther(frame, other);
            }
            else
            {
                ret = MathUtil.ComputeTransformFromOther(frame, other);
            }
            s_ComputeTransformFromOther.End();
            return ret;
        }
    }
}