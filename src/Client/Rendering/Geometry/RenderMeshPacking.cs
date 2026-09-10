using System;

namespace MphRead
{
    /// <summary>
    /// Packs a portable <see cref="CpuMesh"/> into the Android GLES vertex
    /// ABI. The layout is deliberately kept independent of any graphics API:
    /// position (3), color (4), normal (3), texcoord plus matrix index (3),
    /// explicit-color flag (1), and tangent/handedness (4), for eighteen
    /// floats per vertex.
    /// </summary>
    public static class RenderMeshPacking
    {
        public const int FloatsPerVertex = 18;
        public const int DefaultMaximumVertices = 1 << 20;
        public const int DefaultMaximumIndices = 1 << 22;

        public static int RequiredVertexFloats(CpuMesh mesh)
            => checked((mesh ?? throw new ArgumentNullException(nameof(mesh))).VertexCount * FloatsPerVertex);

        public static int RequiredVertexFloats(RenderMeshScratch mesh)
            => checked((mesh ?? throw new ArgumentNullException(nameof(mesh))).VertexCount * FloatsPerVertex);

        public static int RequiredIndexCount(CpuMesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            return checked(mesh.TriangleIndexCount + mesh.LineIndexCount);
        }

        public static int RequiredIndexCount(RenderMeshScratch mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            return checked(mesh.TriangleIndexCount + mesh.LineIndexCount);
        }

        /// <summary>
        /// Byte offset of the line index stream. Triangle indices are always
        /// first, matching the direct GLES draw calls.
        /// </summary>
        public static int LineIndexOffsetBytes(CpuMesh mesh)
            => checked((mesh ?? throw new ArgumentNullException(nameof(mesh))).TriangleIndexCount * sizeof(int));

        public static int LineIndexOffsetBytes(RenderMeshScratch mesh)
            => checked((mesh ?? throw new ArgumentNullException(nameof(mesh))).TriangleIndexCount * sizeof(int));

        public static void ValidateDynamicCapacity(CpuMesh mesh,
            int maximumVertices = DefaultMaximumVertices,
            int maximumIndices = DefaultMaximumIndices)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (maximumVertices < 0) throw new ArgumentOutOfRangeException(nameof(maximumVertices));
            if (maximumIndices < 0) throw new ArgumentOutOfRangeException(nameof(maximumIndices));
            if (mesh.VertexCount > maximumVertices)
            {
                throw new InvalidOperationException(
                    $"Dynamic mesh has {mesh.VertexCount} vertices; the bound is {maximumVertices}.");
            }
            int indices = RequiredIndexCount(mesh);
            if (indices > maximumIndices)
            {
                throw new InvalidOperationException(
                    $"Dynamic mesh has {indices} indices; the bound is {maximumIndices}.");
            }
        }

        public static void ValidateDynamicCapacity(RenderMeshScratch mesh,
            int maximumVertices = DefaultMaximumVertices,
            int maximumIndices = DefaultMaximumIndices)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (maximumVertices < 0) throw new ArgumentOutOfRangeException(nameof(maximumVertices));
            if (maximumIndices < 0) throw new ArgumentOutOfRangeException(nameof(maximumIndices));
            if (mesh.VertexCount > maximumVertices)
            {
                throw new InvalidOperationException(
                    $"Dynamic mesh has {mesh.VertexCount} vertices; the bound is {maximumVertices}.");
            }
            int indices = RequiredIndexCount(mesh);
            if (indices > maximumIndices)
            {
                throw new InvalidOperationException(
                    $"Dynamic mesh has {indices} indices; the bound is {maximumIndices}.");
            }
        }

        /// <summary>
        /// Returns true when a cached GPU object cannot represent the supplied
        /// geometry. Geometry identity is compared by reference because it is
        /// a runtime-owned source key, while the CpuMesh reference and the
        /// cache's revision snapshot protect against a replacement compile
        /// (or an in-place revision update) under the same key.
        /// </summary>
        public static bool NeedsStaticUpload(object? cachedGeometryIdentity, CpuMesh? cachedMesh,
            long cachedRevision, object geometryIdentity, CpuMesh candidate)
        {
            if (geometryIdentity == null) throw new ArgumentNullException(nameof(geometryIdentity));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            return !ReferenceEquals(cachedGeometryIdentity, geometryIdentity)
                || cachedMesh == null
                || !ReferenceEquals(cachedMesh, candidate)
                || cachedRevision != candidate.Revision;
        }

        public static int PackVertices(CpuMesh mesh, Span<float> destination)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int required = RequiredVertexFloats(mesh);
            if (destination.Length < required)
            {
                throw new ArgumentException($"The destination needs {required} floats, got {destination.Length}.",
                    nameof(destination));
            }

            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                PackVertex(mesh.Vertices[i], destination, i * FloatsPerVertex);
            }
            return required;
        }

        public static int PackVertices(RenderMeshScratch mesh, Span<float> destination)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int required = RequiredVertexFloats(mesh);
            if (destination.Length < required)
            {
                throw new ArgumentException($"The destination needs {required} floats, got {destination.Length}.",
                    nameof(destination));
            }

            ReadOnlySpan<RenderVertex> vertices = mesh.Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                PackVertex(vertices[i], destination, i * FloatsPerVertex);
            }
            return required;
        }

        public static int PackIndices(CpuMesh mesh, Span<int> destination)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int required = RequiredIndexCount(mesh);
            if (destination.Length < required)
            {
                throw new ArgumentException($"The destination needs {required} indices, got {destination.Length}.",
                    nameof(destination));
            }
            mesh.TriangleIndices.AsSpan().CopyTo(destination);
            mesh.LineIndices.AsSpan().CopyTo(destination[mesh.TriangleIndexCount..]);
            return required;
        }

        public static int PackIndices(RenderMeshScratch mesh, Span<int> destination)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int required = RequiredIndexCount(mesh);
            if (destination.Length < required)
            {
                throw new ArgumentException($"The destination needs {required} indices, got {destination.Length}.",
                    nameof(destination));
            }
            mesh.TriangleIndices.CopyTo(destination);
            mesh.LineIndices.CopyTo(destination[mesh.TriangleIndexCount..]);
            return required;
        }

        private static void PackVertex(RenderVertex vertex, Span<float> destination, int offset)
        {
            destination[offset] = vertex.Position.X;
            destination[offset + 1] = vertex.Position.Y;
            destination[offset + 2] = vertex.Position.Z;
            destination[offset + 3] = vertex.Color.X;
            destination[offset + 4] = vertex.Color.Y;
            destination[offset + 5] = vertex.Color.Z;
            destination[offset + 6] = vertex.Color.W;
            destination[offset + 7] = vertex.Normal.X;
            destination[offset + 8] = vertex.Normal.Y;
            destination[offset + 9] = vertex.Normal.Z;
            destination[offset + 10] = vertex.TexCoord.X;
            destination[offset + 11] = vertex.TexCoord.Y;
            destination[offset + 12] = vertex.MatrixIndex;
            destination[offset + 13] = vertex.HasExplicitColor ? 1f : 0f;
            destination[offset + 14] = vertex.Tangent.X;
            destination[offset + 15] = vertex.Tangent.Y;
            destination[offset + 16] = vertex.Tangent.Z;
            destination[offset + 17] = vertex.Tangent.W;
        }
    }
}
