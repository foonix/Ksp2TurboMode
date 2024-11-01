using KSP.Api;
using KSP.Sim;
using KSP.Sim.impl;
using System.Reflection;
using Unity.Collections;

namespace TurboMode.Patches
{
    public static class BurstifyTransformFrames
    {
        private static readonly ReflectionUtil.FieldHelper<TransformFrame, ICoordinateSystem> _mostRecentCoordinateSystemRequest
            = new(typeof(TransformFrame).GetField("_mostRecentCoordinateSystemRequest", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<TransformFrame, Matrix4x4D> _mostRecentInverseMatrix
            = new(typeof(TransformFrame).GetField("_mostRecentInverseMatrix", BindingFlags.NonPublic | BindingFlags.Instance));

        static NativeList<Matrix4x4D> tmpList = new(16, Allocator.Persistent);

        /// <summary>
        /// Drop-in replacement for TransformFrame.ComputeTransformFromOther()
        /// </summary>
        public static Matrix4x4D ComputeTransformFromOther(TransformFrame frame, ITransformFrame other)
        {
            if (frame == other)
            {
                return MathUtil.identityMatrixd;
            }
            ITransformFrameInternal otherFrameInternal = other as ITransformFrameInternal;
            if (otherFrameInternal != null && otherFrameInternal.transform.parent == frame)
            {
                return otherFrameInternal.localMatrix;
            }
            if (frame.transform.parent == otherFrameInternal)
            {
                return frame.localMatrixInverse;
            }
            ITransformFrameInternal commonParent = frame.FindCommonParent(otherFrameInternal);

            // Check for cached result.
            // The original dirty checked both frames, but after some testing it looks like I can get away with only one.
            // Disabling dirty check entirely causes some camera position glitching.  May be possible to fix in caller.
            if (other == _mostRecentCoordinateSystemRequest.Get(frame)
                && !IsHierarchyDirty(frame, commonParent) /*&& !IsHierarchyDirty(otherFrameInternal, commonParent)*/)
            {
                return _mostRecentInverseMatrix.Get(frame);
            }

            // Work out from their frame, toward the common parent, which could be this frame.
            Matrix4x4D totalLocalMatrix = MathUtil.identityMatrixd;
            GetConcatenatedLocalMatrix(ref totalLocalMatrix, otherFrameInternal, commonParent);

            // Work from the common parent into our frame from above.
            ITransformFrameInternal next = frame;
            Matrix4x4D totalInverseMatrix;
            if (next != commonParent)
            {
                while (next != commonParent)
                {
                    tmpList.Add(next.localMatrixInverse);
                    next = next._transformInternal._parentInternal;
                }
                MathUtil.InverseTransformStack(tmpList, out totalInverseMatrix);
                tmpList.Clear();
            }
            else
            {
                // The other frame is our child. We only need the forward matricies.
                totalInverseMatrix = MathUtil.identityMatrixd;
            }

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

        private static bool IsHierarchyDirty(ITransformFrameInternal current, ITransformFrameInternal other)
        {
            while (current != other)
            {
                if (current.IsLocalMatrixDirty)
                {
                    return true;
                }
                current = current._transformInternal._parentInternal;
            }
            return false;
        }

        internal static void DisposeCachedAllocations()
        {
            tmpList.Dispose();
        }
    }
}
