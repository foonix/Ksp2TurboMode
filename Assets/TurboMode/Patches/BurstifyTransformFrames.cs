using KSP.Api;
using KSP.Sim;
using KSP.Sim.impl;
using System.Reflection;

namespace TurboMode.Patches
{
    public static class BurstifyTransformFrames
    {
        private static readonly ReflectionUtil.FieldHelper<TransformFrame, ICoordinateSystem> _mostRecentCoordinateSystemRequest
            = new(typeof(TransformFrame).GetField("_mostRecentCoordinateSystemRequest", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<TransformFrame, Matrix4x4D> _mostRecentInverseMatrix
            = new(typeof(TransformFrame).GetField("_mostRecentInverseMatrix", BindingFlags.NonPublic | BindingFlags.Instance));

        /// <summary>
        /// Drop-in replacement for TransformFrame.ComputeTransformFromOther()
        /// </summary>
        public static Matrix4x4D ComputeTransformFromOther(TransformFrame frame, ITransformFrame other)
        {
            if (frame == other)
            {
                return MathUtil.identityMatrixd;
            }
            ITransformFrameInternal transformFrameInternal = other as ITransformFrameInternal;
            if (transformFrameInternal != null && transformFrameInternal.transform.parent == frame)
            {
                return transformFrameInternal.localMatrix;
            }
            if (frame.transform.parent == transformFrameInternal)
            {
                return frame.localMatrixInverse;
            }
            ITransformFrameInternal commonParent = frame.FindCommonParent(transformFrameInternal);

            // Check for cached result.  There were some IsHierarchyDirty tests here, but skipping them because
            // the callers don't seem to change the hierarchy between calls.
            // If I'm wrong abou that, consider just deleting the cache entirely.
            // IsHierarchyDirty does a lot of memory access that is redundant to the actual calculation.
            if (other == _mostRecentCoordinateSystemRequest.Get(frame))
            {
                return _mostRecentInverseMatrix.Get(frame);
            }

            Matrix4x4D totalInverseMatrix = MathUtil.identityMatrixd;
            GetConcatenatedLocalInverseMatrix(ref totalInverseMatrix, frame, commonParent);
            Matrix4x4D totalLocalMatrix = MathUtil.identityMatrixd;
            GetConcatenatedLocalMatrix(ref totalLocalMatrix, transformFrameInternal, commonParent);
            MathUtil.MultiplyWithRefBursted(ref totalLocalMatrix, totalInverseMatrix);
            return totalLocalMatrix;
        }

        private static void GetConcatenatedLocalInverseMatrix(ref Matrix4x4D totalInverseMatrix, ITransformFrameInternal current, ITransformFrameInternal other)
        {
            if (current != other)
            {
                GetConcatenatedLocalInverseMatrix(ref totalInverseMatrix, current._transformInternal._parentInternal, other);
                MathUtil.MultiplyWithRefBursted(ref totalInverseMatrix, current.localMatrixInverse);
            }
        }

        private static void GetConcatenatedLocalMatrix(ref Matrix4x4D totalLocalMatrix, ITransformFrameInternal current, ITransformFrameInternal other)
        {
            while (current != other)
            {
                MathUtil.MultiplyWithRefBursted(ref totalLocalMatrix, current.localMatrix);
                current = current._transformInternal._parentInternal;
            }
        }
    }
}
