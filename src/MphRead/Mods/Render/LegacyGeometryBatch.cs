using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Converts the legacy immediate-mode geometry emitted by the original
    /// renderer into explicit vertex/index buffers.
    ///
    /// This used to live inside the Android GLES adapter. It is deliberately
    /// GPU-API neutral now: GLES uploads these arrays to VAO/VBO/IBO objects,
    /// while the modern renderer uploads the exact same arrays to WebGPU
    /// buffers for DX12, Vulkan and Metal.
    /// </summary>
    internal sealed class LegacyGeometryBatch
    {
        internal const int FloatsPerVertex = 14;

        internal readonly List<float> Vertices = new(4096);
        internal readonly List<int> TriIndices = new(4096);
        internal readonly List<int> LineIndices = new();

        internal int VertexCount { get; private set; }

        private PrimitiveType _mode;
        private int _primitiveStart;

        internal void Clear()
        {
            Vertices.Clear();
            TriIndices.Clear();
            LineIndices.Clear();
            VertexCount = 0;
            _primitiveStart = 0;
        }

        internal void Begin(PrimitiveType mode)
        {
            _mode = mode;
            _primitiveStart = VertexCount;
        }

        internal void AddVertex(Vector3 position, Vector4 color, Vector3 normal,
            Vector3 texcoord, bool hasOwnColor)
        {
            Vertices.Add(position.X);
            Vertices.Add(position.Y);
            Vertices.Add(position.Z);
            Vertices.Add(color.X);
            Vertices.Add(color.Y);
            Vertices.Add(color.Z);
            Vertices.Add(color.W);
            Vertices.Add(normal.X);
            Vertices.Add(normal.Y);
            Vertices.Add(normal.Z);
            Vertices.Add(texcoord.X);
            Vertices.Add(texcoord.Y);
            Vertices.Add(texcoord.Z);
            Vertices.Add(hasOwnColor ? 1f : 0f);
            VertexCount++;
        }

        internal void End()
        {
            EmitIndices(_mode, _primitiveStart, VertexCount - _primitiveStart);
        }

        internal int[] BuildIndexArray()
        {
            var result = new int[TriIndices.Count + LineIndices.Count];
            TriIndices.CopyTo(result, 0);
            LineIndices.CopyTo(result, TriIndices.Count);
            return result;
        }

        private void EmitIndices(PrimitiveType mode, int first, int count)
        {
            switch (mode)
            {
            case PrimitiveType.Triangles:
                for (int i = 0; i + 2 < count; i += 3)
                {
                    Triangle(first + i, first + i + 1, first + i + 2);
                }
                break;

            case PrimitiveType.Quads:
                for (int i = 0; i + 3 < count; i += 4)
                {
                    Triangle(first + i, first + i + 1, first + i + 2);
                    Triangle(first + i, first + i + 2, first + i + 3);
                }
                break;

            case PrimitiveType.TriangleStrip:
                for (int i = 0; i + 2 < count; i++)
                {
                    if ((i & 1) == 0)
                        Triangle(first + i, first + i + 1, first + i + 2);
                    else
                        Triangle(first + i + 1, first + i, first + i + 2);
                }
                break;

            case PrimitiveType.QuadStrip:
                for (int i = 0; i + 3 < count; i += 2)
                {
                    Triangle(first + i, first + i + 1, first + i + 3);
                    Triangle(first + i, first + i + 3, first + i + 2);
                }
                break;

            case PrimitiveType.TriangleFan:
                for (int i = 1; i + 1 < count; i++)
                {
                    Triangle(first, first + i, first + i + 1);
                }
                break;

            case PrimitiveType.LineLoop:
                for (int i = 0; i < count; i++)
                {
                    LineIndices.Add(first + i);
                    LineIndices.Add(first + (i + 1) % count);
                }
                break;

            default:
                throw new ProgramException($"No explicit-buffer translation for primitive type {mode}.");
            }
        }

        private void Triangle(int a, int b, int c)
        {
            TriIndices.Add(a);
            TriIndices.Add(b);
            TriIndices.Add(c);
        }
    }
}
