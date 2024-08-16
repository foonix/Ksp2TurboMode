using KSP.Api;
using KSP.Sim.impl;
using Unity.Burst;

namespace TurboMode
{
    /// <summary>
    /// Helpers for dealing with KSP's double math, but Burst compile common operations.
    /// </summary>
    [BurstCompile]
    public static class MathUtil
    {
        static readonly Matrix4x4D identityMatrixd = new(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0);

        public static Matrix4x4D ComputeTransformFromOther(NonRotatingFrame frame, ICoordinateSystem other)
        {
            ITransformFrameInternal transformFrameInternal = other as ITransformFrameInternal;
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
            first = new Matrix4x4D(first.ToDouble4x4() * second.ToDouble4x4());
        }
    }
}
