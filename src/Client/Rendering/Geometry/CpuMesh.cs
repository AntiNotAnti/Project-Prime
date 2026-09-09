using System;

namespace MphRead
{
    /// <summary>CPU-side indexed geometry, before any backend upload.</summary>
    public sealed class CpuMesh
    {
        public RenderVertex[] Vertices { get; }
        public int[] TriangleIndices { get; }
        /// <summary>Compatibility alias for backends that call the triangle stream Indices.</summary>
        public int[] Indices => TriangleIndices;
        public int[] LineIndices { get; }

        public int VertexCount => Vertices.Length;
        public int TriangleIndexCount => TriangleIndices.Length;
        public int LineIndexCount => LineIndices.Length;

        public long Revision { get; }

        public CpuMesh(RenderVertex[] vertices, int[] triangleIndices, int[]? lineIndices = null,
            long revision = 0)
        {
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            TriangleIndices = triangleIndices ?? throw new ArgumentNullException(nameof(triangleIndices));
            LineIndices = lineIndices ?? Array.Empty<int>();
            Revision = revision == 0
                ? System.Threading.Interlocked.Increment(ref _nextRevision)
                : revision;
            if (Revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            ValidateIndices(TriangleIndices, Vertices.Length, nameof(triangleIndices));
            ValidateIndices(LineIndices, Vertices.Length, nameof(lineIndices));
        }

        private static long _nextRevision;

        private static void ValidateIndices(int[] indices, int vertexCount, string name)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                if ((uint)indices[i] >= (uint)vertexCount)
                {
                    throw new ArgumentException($"{name} contains vertex index {indices[i]} outside {vertexCount} vertices.", name);
                }
            }
        }
    }
}
