using KSP.Api;
using KSP.Sim;
using KSP.Sim.impl;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace TurboMode
{
    /// <summary>
    /// Helpers for dealing with KSP's double math, but Burst compile common operations.
    /// </summary>
    [BurstCompile]
    public static class MathUtil
    {
        static readonly Matrix4x4D identityMatrixd = new(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0);

        public static Matrix4x4D ComputeTransformFromOther(TransformFrame frame, ITransformFrame other)
        {
            if (frame == other)
            {
                return identityMatrixd;
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

            Matrix4x4D totalInverseMatrix = identityMatrixd;
            GetConcatenatedLocalInverseMatrix(ref totalInverseMatrix, frame, commonParent);
            Matrix4x4D totalLocalMatrix = identityMatrixd;
            GetConcatenatedLocalMatrix(ref totalLocalMatrix, transformFrameInternal, commonParent);
            //Matrix4x4D.MultiplyWithRef(ref totalLocalMatrix, totalInverseMatrix);
            MultiplyWithRefBursted(ref totalLocalMatrix, totalInverseMatrix);
            return totalLocalMatrix;
        }

        private static void GetConcatenatedLocalInverseMatrix(ref Matrix4x4D totalInverseMatrix, ITransformFrameInternal current, ITransformFrameInternal other)
        {
            if (current != other)
            {
                GetConcatenatedLocalInverseMatrix(ref totalInverseMatrix, current._transformInternal._parentInternal, other);
                //Matrix4x4D.MultiplyWithRef(ref totalInverseMatrix, current.localMatrixInverse);
                MultiplyWithRefBursted(ref totalInverseMatrix, current.localMatrixInverse);
            }
        }

        private static void GetConcatenatedLocalMatrix(ref Matrix4x4D totalLocalMatrix, ITransformFrameInternal current, ITransformFrameInternal other)
        {
            while (current != other)
            {
                //Matrix4x4D.MultiplyWithRef(ref totalLocalMatrix, current.localMatrix);
                MultiplyWithRefBursted(ref totalLocalMatrix, current.localMatrix);
                current = current._transformInternal._parentInternal;
            }
        }

        [BurstCompile]
        public static void MultiplyWithRefBursted(ref Matrix4x4D first, in Matrix4x4D second)
        {
            // Burst compiles out the getters/constructor, so no need for private accessors etc.
            var firstMatrix = first.ToDouble4x4();
            var secondMatrix = second.ToDouble4x4();

            first = new Matrix4x4D(math.mul(firstMatrix, secondMatrix));
        }
    }
}
