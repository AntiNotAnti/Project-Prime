using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Reusable, backend-neutral storage for one dynamic draw.  A caller fills
    /// the vertices after <see cref="Prepare"/>; the topology indices are
    /// generated into the same bounded arrays and are reused by the next
    /// draw.  The scratch object is intentionally single-writer: the Android
    /// render thread submits one draw before preparing the next one.
    /// </summary>
    public sealed class RenderMeshScratch
    {
        private RenderVertex[] _vertices;
        private int[] _triangleIndices;
        private int[] _lineIndices;
        private int _vertexCount;
        private int _triangleIndexCount;
        private int _lineIndexCount;

        public RenderMeshScratch(int initialVertexCapacity = 128)
        {
            if (initialVertexCapacity < 1
                || initialVertexCapacity > RenderMeshPacking.DefaultMaximumVertices)
            {
                throw new ArgumentOutOfRangeException(nameof(initialVertexCapacity));
            }
            _vertices = new RenderVertex[initialVertexCapacity];
            _triangleIndices = new int[Math.Max(1, ComputeTriangleIndexCapacity(
                MeshPrimitiveTopology.TriangleStrip, initialVertexCapacity))];
            _lineIndices = new int[Math.Max(1, initialVertexCapacity * 2)];
        }

        public int VertexCount => _vertexCount;
        public int TriangleIndexCount => _triangleIndexCount;
        public int LineIndexCount => _lineIndexCount;
        public int VertexCapacity => _vertices.Length;
        public int TriangleIndexCapacity => _triangleIndices.Length;
        public int LineIndexCapacity => _lineIndices.Length;

        // These are intentionally internal views.  They let the GLES
        // translator and allocation/reuse tests inspect the bounded storage
        // without exposing mutable arrays as part of the public contract.
        internal RenderVertex[] VertexStorage => _vertices;
        internal int[] TriangleIndexStorage => _triangleIndices;
        internal int[] LineIndexStorage => _lineIndices;
        internal ReadOnlySpan<RenderVertex> Vertices
            => _vertices.AsSpan(0, _vertexCount);
        internal ReadOnlySpan<int> TriangleIndices
            => _triangleIndices.AsSpan(0, _triangleIndexCount);
        internal ReadOnlySpan<int> LineIndices
            => _lineIndices.AsSpan(0, _lineIndexCount);

        /// <summary>
        /// Prepare a topology and exact vertex count.  Indices are generated
        /// in the same Android winding used by <see cref="MeshCompiler"/>.
        /// </summary>
        public void Prepare(MeshPrimitiveTopology topology, int vertexCount)
        {
            if (vertexCount < 0 || vertexCount > RenderMeshPacking.DefaultMaximumVertices)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }
            int triangleCount = ComputeTriangleIndexCapacity(topology, vertexCount);
            int lineCount = topology == MeshPrimitiveTopology.LineLoop
                ? checked(vertexCount * 2) : 0;
            EnsureCapacity(vertexCount, triangleCount, lineCount);
            _vertexCount = vertexCount;
            _triangleIndexCount = triangleCount;
            _lineIndexCount = lineCount;
            FillIndices(topology, vertexCount);
        }

        public void SetVertex(int index, Vector3 position, Vector2 texCoord = default,
            Vector4? color = null, uint matrixIndex = 0,
            bool explicitColor = false, Vector3? normal = null)
        {
            if ((uint)index >= (uint)_vertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            _vertices[index] = new RenderVertex(position, color ?? Vector4.One,
                normal ?? Vector3.UnitZ, texCoord, matrixIndex,
                explicitColor ? RenderVertexFlags.ExplicitColor : RenderVertexFlags.None);
        }

        public void SetFullscreenQuad()
        {
            Prepare(MeshPrimitiveTopology.TriangleStrip, 4);
            SetVertex(0, new Vector3(1, 1, 0), new Vector2(1, 1));
            SetVertex(1, new Vector3(-1, 1, 0), new Vector2(0, 1));
            SetVertex(2, new Vector3(1, -1, 0), new Vector2(1, 0));
            SetVertex(3, new Vector3(-1, -1, 0), new Vector2(0, 0));
        }

        public void SetTexturedQuad(Vector3 topRight, Vector3 topLeft,
            Vector3 bottomRight, Vector3 bottomLeft)
        {
            Prepare(MeshPrimitiveTopology.TriangleStrip, 4);
            SetVertex(0, topRight, new Vector2(1, 0));
            SetVertex(1, topLeft, new Vector2(0, 0));
            SetVertex(2, bottomRight, new Vector2(1, 1));
            SetVertex(3, bottomLeft, new Vector2(0, 1));
        }

        public void SetSolidQuad(Vector3 topRight, Vector3 topLeft,
            Vector3 bottomRight, Vector3 bottomLeft, Vector4 color,
            bool explicitColor = false)
        {
            Prepare(MeshPrimitiveTopology.TriangleStrip, 4);
            SetVertex(0, topRight, color: color, explicitColor: explicitColor);
            SetVertex(1, topLeft, color: color, explicitColor: explicitColor);
            SetVertex(2, bottomRight, color: color, explicitColor: explicitColor);
            SetVertex(3, bottomLeft, color: color, explicitColor: explicitColor);
        }

        public void SetTriangleFan(IReadOnlyList<Vector3> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            Prepare(MeshPrimitiveTopology.TriangleFan, positions.Count);
            for (int i = 0; i < positions.Count; i++) SetVertex(i, positions[i]);
        }

        /// <summary>Fill a fan directly from an existing bounded array.</summary>
        public void SetTriangleFan(Vector3[] positions, int count)
        {
            ValidateArraySlice(positions, count);
            Prepare(MeshPrimitiveTopology.TriangleFan, count);
            for (int i = 0; i < count; i++) SetVertex(i, positions[i]);
        }

        public void SetLineLoop(IReadOnlyList<Vector3> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            Prepare(MeshPrimitiveTopology.LineLoop, positions.Count);
            for (int i = 0; i < positions.Count; i++) SetVertex(i, positions[i]);
        }

        /// <summary>Fill a line loop directly from an existing bounded array.</summary>
        public void SetLineLoop(Vector3[] positions, int count)
        {
            ValidateArraySlice(positions, count);
            Prepare(MeshPrimitiveTopology.LineLoop, count);
            for (int i = 0; i < count; i++) SetVertex(i, positions[i]);
        }

        private static void ValidateArraySlice(Vector3[] positions, int count)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (count < 0 || count > positions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        private void EnsureCapacity(int vertexCount, int triangleCount, int lineCount)
        {
            if (vertexCount > _vertices.Length)
            {
                _vertices = ResizeBounded(_vertices, vertexCount,
                    RenderMeshPacking.DefaultMaximumVertices);
            }
            if (triangleCount > _triangleIndices.Length)
            {
                _triangleIndices = ResizeBounded(_triangleIndices, triangleCount,
                    RenderMeshPacking.DefaultMaximumIndices);
            }
            if (lineCount > _lineIndices.Length)
            {
                _lineIndices = ResizeBounded(_lineIndices, lineCount,
                    RenderMeshPacking.DefaultMaximumIndices);
            }
        }

        private static T[] ResizeBounded<T>(T[] source, int required, int maximum)
        {
            if (required < 0 || required > maximum)
            {
                throw new InvalidOperationException("Dynamic mesh exceeds its bounded scratch capacity.");
            }
            int doubled = source.Length > maximum / 2 ? maximum : source.Length * 2;
            int next = Math.Max(required, doubled);
            if (next > maximum) next = maximum;
            if (next < required)
            {
                throw new InvalidOperationException("Dynamic mesh exceeds its bounded scratch capacity.");
            }
            Array.Resize(ref source, next);
            return source;
        }

        private void FillIndices(MeshPrimitiveTopology topology, int count)
        {
            switch (topology)
            {
            case MeshPrimitiveTopology.Triangles:
                for (int i = 0; i + 2 < count; i += 3)
                {
                    _triangleIndices[i] = i;
                    _triangleIndices[i + 1] = i + 1;
                    _triangleIndices[i + 2] = i + 2;
                }
                break;
            case MeshPrimitiveTopology.Quads:
                for (int i = 0, output = 0; i + 3 < count; i += 4)
                {
                    _triangleIndices[output++] = i;
                    _triangleIndices[output++] = i + 1;
                    _triangleIndices[output++] = i + 2;
                    _triangleIndices[output++] = i;
                    _triangleIndices[output++] = i + 2;
                    _triangleIndices[output++] = i + 3;
                }
                break;
            case MeshPrimitiveTopology.TriangleStrip:
                for (int i = 0, output = 0; i + 2 < count; i++)
                {
                    if ((i & 1) == 0)
                    {
                        _triangleIndices[output++] = i;
                        _triangleIndices[output++] = i + 1;
                        _triangleIndices[output++] = i + 2;
                    }
                    else
                    {
                        _triangleIndices[output++] = i + 1;
                        _triangleIndices[output++] = i;
                        _triangleIndices[output++] = i + 2;
                    }
                }
                break;
            case MeshPrimitiveTopology.QuadStrip:
                for (int i = 0, output = 0; i + 3 < count; i += 2)
                {
                    _triangleIndices[output++] = i;
                    _triangleIndices[output++] = i + 1;
                    _triangleIndices[output++] = i + 3;
                    _triangleIndices[output++] = i;
                    _triangleIndices[output++] = i + 3;
                    _triangleIndices[output++] = i + 2;
                }
                break;
            case MeshPrimitiveTopology.TriangleFan:
                for (int i = 1, output = 0; i + 1 < count; i++)
                {
                    _triangleIndices[output++] = 0;
                    _triangleIndices[output++] = i;
                    _triangleIndices[output++] = i + 1;
                }
                break;
            case MeshPrimitiveTopology.LineLoop:
                for (int i = 0; i < count; i++)
                {
                    _lineIndices[i * 2] = i;
                    _lineIndices[i * 2 + 1] = (i + 1) % count;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(topology), topology, null);
            }
        }

        private static int ComputeTriangleIndexCapacity(MeshPrimitiveTopology topology, int count)
        {
            return topology switch
            {
                MeshPrimitiveTopology.Triangles => count / 3 * 3,
                MeshPrimitiveTopology.Quads => count / 4 * 6,
                MeshPrimitiveTopology.TriangleStrip => Math.Max(0, count - 2) * 3,
                MeshPrimitiveTopology.QuadStrip => count < 4 ? 0 : (count - 2) / 2 * 6,
                MeshPrimitiveTopology.TriangleFan => Math.Max(0, count - 2) * 3,
                MeshPrimitiveTopology.LineLoop => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, null)
            };
        }
    }
}
