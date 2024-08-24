using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace TurboMode
{
    /// <summary>
    /// Helpers for dealing with KSP's double math, but Burst compile common operations.
    /// </summary>
    [BurstCompile]
    public static class MathUtil
    {
        public static readonly Matrix4x4D identityMatrixd = new(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0);

        [BurstCompile]
        public static void MultiplyWithRefBursted(ref Matrix4x4D first, in Matrix4x4D second)
        {
            // Burst compiles out the getters/constructor, so no need for private accessors etc.
            var firstMatrix = first.ToDouble4x4();
            var secondMatrix = second.ToDouble4x4();

            first = new Matrix4x4D(math.mul(firstMatrix, secondMatrix));
        }

        /// <summary>
        /// Translate, rotate, and scale the provided Vector3d by the TRS matrix.
        /// </summary>
        [BurstCompile]
        public static void TransformPoint(in Matrix4x4D matrix, ref Vector3d vector)
        {
            // Burst inlines this.
            vector = matrix.TransformPoint(vector);
        }

        /// <summary>
        /// Rotate and scale (but not translate) the provided Vector3d by the TRS matrix.
        /// </summary>
        [BurstCompile]
        public static void TransformVector(in Matrix4x4D matrix, ref Vector3d vector)
        {
            // Burst inlines this.
            vector = matrix.TransformVector(vector);
        }

        /// <summary>
        /// Multiply the provided stack of matrices from last to first.
        /// </summary>
        /// <param name="stack">A NativeList of matries used as a stack.  At least one matrix is required.</param>
        /// <param name="result">The result of matrix multiplication.</param>
        [BurstCompile]
        public static void InverseTransformStack(in NativeList<Matrix4x4D> stack, out Matrix4x4D result)
        {
            double4x4 combined = stack[^1].ToDouble4x4();
            for (int i = stack.Length - 1; i > 0; i--)
            {
                var next = stack[i - 1].ToDouble4x4();
                combined = math.mul(combined, next);
            }
            result = new Matrix4x4D(combined);
        }

        [BurstCompile]
        public static void CreateTrsMatrices(
            in Vector3d localPosition, in QuaternionD localRotation,
            ref Matrix4x4D localMatrix, ref Matrix4x4D localMatrixInverse)
        {
            localMatrix = Matrix4x4D.TRS(localPosition, localRotation);
            localMatrixInverse = localMatrix.GetInverse();
        }
    }
}
