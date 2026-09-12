using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Builds one finite tangent frame from an already indexed triangle mesh.
    /// Source vertices are never welded, so authored hard edges and UV seams
    /// remain tangent-frame boundaries.
    /// </summary>
    public static class MeshTangentGenerator
    {
        private const float Epsilon = 0.00000001f;

        public static RenderVertex[] Generate(RenderVertex[] source,
            int[] triangleIndices)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(triangleIndices);
            if (triangleIndices.Length % 3 != 0)
            {
                throw new ArgumentException(
                    "A tangent source must contain complete indexed triangles.",
                    nameof(triangleIndices));
            }

            var tangentSums = new Vector3[source.Length];
            var bitangentSums = new Vector3[source.Length];
            for (int i = 0; i < triangleIndices.Length; i += 3)
            {
                int i0 = triangleIndices[i];
                int i1 = triangleIndices[i + 1];
                int i2 = triangleIndices[i + 2];
                if ((uint)i0 >= (uint)source.Length
                    || (uint)i1 >= (uint)source.Length
                    || (uint)i2 >= (uint)source.Length)
                {
                    throw new ArgumentException(
                        "Tangent indices must refer to source vertices.",
                        nameof(triangleIndices));
                }

                RenderVertex v0 = source[i0];
                RenderVertex v1 = source[i1];
                RenderVertex v2 = source[i2];
                if (!IsFinite(v0.Position) || !IsFinite(v1.Position)
                    || !IsFinite(v2.Position) || !IsFinite(v0.TexCoord)
                    || !IsFinite(v1.TexCoord) || !IsFinite(v2.TexCoord))
                {
                    continue;
                }

                Vector3 edge1 = v1.Position - v0.Position;
                Vector3 edge2 = v2.Position - v0.Position;
                if (Vector3.Cross(edge1, edge2).LengthSquared <= Epsilon)
                {
                    continue;
                }
                Vector2 uv1 = v1.TexCoord - v0.TexCoord;
                Vector2 uv2 = v2.TexCoord - v0.TexCoord;
                float determinant = uv1.X * uv2.Y - uv1.Y * uv2.X;
                if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= Epsilon)
                {
                    continue;
                }

                float reciprocal = 1f / determinant;
                Vector3 tangent = (edge1 * uv2.Y - edge2 * uv1.Y) * reciprocal;
                Vector3 bitangent = (edge2 * uv1.X - edge1 * uv2.X) * reciprocal;
                if (!IsFinite(tangent) || !IsFinite(bitangent))
                {
                    continue;
                }
                tangentSums[i0] += tangent;
                tangentSums[i1] += tangent;
                tangentSums[i2] += tangent;
                bitangentSums[i0] += bitangent;
                bitangentSums[i1] += bitangent;
                bitangentSums[i2] += bitangent;
            }

            var result = new RenderVertex[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                RenderVertex vertex = source[i];
                Vector3 normal = SafeNormalize(vertex.Normal, Vector3.UnitZ);
                Vector3 tangent = tangentSums[i]
                    - normal * Vector3.Dot(normal, tangentSums[i]);
                tangent = SafeNormalize(tangent, Perpendicular(normal));
                Vector3 bitangent = bitangentSums[i];
                float handedness = IsFinite(bitangent)
                    && bitangent.LengthSquared > Epsilon
                    && Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0
                        ? -1f : 1f;
                result[i] = new RenderVertex(vertex.Position, vertex.Color,
                    vertex.Normal, vertex.TexCoord,
                    new Vector4(tangent, handedness), vertex.MatrixIndex,
                    vertex.Flags);
            }
            return result;
        }

        private static Vector3 Perpendicular(Vector3 normal)
        {
            Vector3 axis = MathF.Abs(normal.X) <= MathF.Abs(normal.Y)
                && MathF.Abs(normal.X) <= MathF.Abs(normal.Z)
                    ? Vector3.UnitX
                    : MathF.Abs(normal.Y) <= MathF.Abs(normal.Z)
                        ? Vector3.UnitY : Vector3.UnitZ;
            return SafeNormalize(Vector3.Cross(axis, normal), Vector3.UnitX);
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            float lengthSquared = value.LengthSquared;
            return IsFinite(value) && float.IsFinite(lengthSquared)
                && lengthSquared > Epsilon
                    ? value / MathF.Sqrt(lengthSquared) : fallback;
        }

        private static bool IsFinite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }
}
