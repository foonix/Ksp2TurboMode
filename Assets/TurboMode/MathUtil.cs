using Unity.Burst;
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
    }
}
