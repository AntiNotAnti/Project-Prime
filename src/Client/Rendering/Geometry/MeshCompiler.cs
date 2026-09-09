using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Converts the legacy immediate-mode topology into indexed triangles and
    /// optional line indices. The winding intentionally mirrors the proven
    /// Android path in GlEs exactly, including the odd/even triangle-strip
    /// correction and DS quad/quad-strip order.
    /// </summary>
    public enum MeshPrimitiveTopology
    {
        Triangles,
        Quads,
        TriangleStrip,
        QuadStrip,
        TriangleFan,
        LineLoop
    }

    public static class MeshCompiler
    {
        public static CpuMesh Compile(IReadOnlyList<RenderVertex> source,
            MeshPrimitiveTopology topology, int start = 0, int count = -1)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (start < 0 || start > source.Count) throw new ArgumentOutOfRangeException(nameof(start));
            if (count < 0) count = source.Count - start;
            if (count < 0 || start > source.Count - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var vertices = new RenderVertex[count];
            for (int i = 0; i < count; i++) vertices[i] = source[start + i];
            var triangles = new List<int>(TriangleCapacity(topology, count));
            var lines = new List<int>(topology == MeshPrimitiveTopology.LineLoop ? count * 2 : 0);
            AppendIndices(triangles, lines, topology, 0, count);
            return new CpuMesh(vertices, triangles.ToArray(), lines.ToArray());
        }

        public static CpuMesh Compile(RenderVertex[] source, MeshPrimitiveTopology topology,
            int start = 0, int count = -1)
            => Compile((IReadOnlyList<RenderVertex>)source, topology, start, count);

        /// <summary>
        /// Compiles one DS display-list instruction stream without touching a
        /// graphics API. This is the exact state machine used by the legacy
        /// renderer: packed fixed-point vertices, normal/colour/texcoord
        /// state, matrix restores, and the original primitive winding all
        /// become CPU-owned data before any backend upload.
        /// </summary>
        public static CpuMesh CompileDisplayList(IReadOnlyList<RenderInstruction> instructions,
            int textureWidth, int textureHeight, bool texgen, bool isRoom)
        {
            if (instructions == null) throw new ArgumentNullException(nameof(instructions));
            var vertices = new List<RenderVertex>();
            var triangles = new List<int>();
            var lines = new List<int>();
            Vector3 position = Vector3.Zero;
            Vector4 color = Vector4.One;
            Vector3 normal = Vector3.UnitZ;
            Vector2 texCoord = texgen ? new Vector2(0.5f) : Vector2.Zero;
            uint matrixIndex = 0;
            bool explicitColor = false;
            bool inPrimitive = false;
            int primitiveStart = 0;
            MeshPrimitiveTopology topology = MeshPrimitiveTopology.Triangles;

            void AddVertex()
            {
                if (!inPrimitive) throw new ProgramException("Vertex command outside BEGIN_VTXS.");
                vertices.Add(new RenderVertex(position, color, normal, texCoord, matrixIndex,
                    explicitColor ? RenderVertexFlags.ExplicitColor : RenderVertexFlags.None));
            }

            for (int i = 0; i < instructions.Count; i++)
            {
                RenderInstruction instruction = instructions[i];
                IReadOnlyList<uint> args = instruction.Arguments;
                switch (instruction.Code)
                {
                case InstructionCode.BEGIN_VTXS:
                    if (inPrimitive) throw new ProgramException("Nested BEGIN_VTXS in display list.");
                    topology = args[0] switch
                    {
                        0 => MeshPrimitiveTopology.Triangles,
                        1 => MeshPrimitiveTopology.Quads,
                        2 => MeshPrimitiveTopology.TriangleStrip,
                        3 => MeshPrimitiveTopology.QuadStrip,
                        _ => throw new ProgramException("Invalid geometry type")
                    };
                    primitiveStart = vertices.Count;
                    inPrimitive = true;
                    break;
                case InstructionCode.COLOR:
                    color = DecodeColor(args[0], alpha: 1f);
                    explicitColor = true;
                    break;
                case InstructionCode.DIF_AMB:
                    {
                        uint packed = args[0];
                        color = DecodeColor(packed, ((packed >> 15) & 1) != 0 ? 1f : 0f);
                        explicitColor = true;
                    }
                    break;
                case InstructionCode.NORMAL:
                    {
                        uint packed = args[0];
                        normal = new Vector3(DecodeSigned(packed, 0, 0x3FF, 0x200),
                            DecodeSigned(packed, 10, 0x3FF, 0x200),
                            DecodeSigned(packed, 20, 0x3FF, 0x200)) / 512f;
                    }
                    break;
                case InstructionCode.TEXCOORD:
                    if (textureWidth <= 0 || textureHeight <= 0)
                    {
                        throw new ProgramException("TEXCOORD requires positive texture dimensions.");
                    }
                    {
                        uint packed = args[0];
                        int s = DecodeSigned(packed, 0, 0xFFFF, 0x8000);
                        int t = DecodeSigned(packed, 16, 0xFFFF, 0x8000);
                        texCoord = new Vector2(s / 16f / textureWidth, t / 16f / textureHeight);
                    }
                    break;
                case InstructionCode.VTX_16:
                    {
                        uint xy = args[0];
                        int x = DecodeSigned(xy, 0, 0xFFFF, 0x8000);
                        int y = DecodeSigned(xy, 16, 0xFFFF, 0x8000);
                        int z = DecodeSigned(args[1], 0, 0xFFFF, 0x8000);
                        position = new Vector3(Fixed.ToFloat(x), Fixed.ToFloat(y), Fixed.ToFloat(z));
                        AddVertex();
                    }
                    break;
                case InstructionCode.VTX_10:
                    {
                        uint packed = args[0];
                        position = new Vector3(DecodeSigned(packed, 0, 0x3FF, 0x200),
                            DecodeSigned(packed, 10, 0x3FF, 0x200),
                            DecodeSigned(packed, 20, 0x3FF, 0x200)) / 64f;
                        AddVertex();
                    }
                    break;
                case InstructionCode.VTX_XY:
                    {
                        uint packed = args[0];
                        int x = DecodeSigned(packed, 0, 0xFFFF, 0x8000);
                        int y = DecodeSigned(packed, 16, 0xFFFF, 0x8000);
                        position.X = Fixed.ToFloat(x);
                        position.Y = Fixed.ToFloat(y);
                        AddVertex();
                    }
                    break;
                case InstructionCode.VTX_XZ:
                    {
                        uint packed = args[0];
                        int x = DecodeSigned(packed, 0, 0xFFFF, 0x8000);
                        int z = DecodeSigned(packed, 16, 0xFFFF, 0x8000);
                        position.X = Fixed.ToFloat(x);
                        position.Z = Fixed.ToFloat(z);
                        AddVertex();
                    }
                    break;
                case InstructionCode.VTX_YZ:
                    {
                        uint packed = args[0];
                        int y = DecodeSigned(packed, 0, 0xFFFF, 0x8000);
                        int z = DecodeSigned(packed, 16, 0xFFFF, 0x8000);
                        position.Y = Fixed.ToFloat(y);
                        position.Z = Fixed.ToFloat(z);
                        AddVertex();
                    }
                    break;
                case InstructionCode.VTX_DIFF:
                    {
                        uint packed = args[0];
                        position += new Vector3(Fixed.ToFloat(DecodeSigned(packed, 0, 0x3FF, 0x200)),
                            Fixed.ToFloat(DecodeSigned(packed, 10, 0x3FF, 0x200)),
                            Fixed.ToFloat(DecodeSigned(packed, 20, 0x3FF, 0x200)));
                        AddVertex();
                    }
                    break;
                case InstructionCode.END_VTXS:
                    if (!inPrimitive) throw new ProgramException("END_VTXS without BEGIN_VTXS.");
                    AppendIndices(triangles, lines, topology, primitiveStart, vertices.Count - primitiveStart);
                    inPrimitive = false;
                    break;
                case InstructionCode.MTX_RESTORE:
                    if (!isRoom) matrixIndex = args[0];
                    break;
                case InstructionCode.NOP:
                    break;
                default:
                    throw new ProgramException($"Unknown opcode {instruction.Code}.");
                }
            }
            if (inPrimitive) throw new ProgramException("Display list ended inside a primitive.");
            return new CpuMesh(vertices.ToArray(), triangles.ToArray(), lines.ToArray());
        }

        private static Vector4 DecodeColor(uint packed, float alpha)
        {
            return new Vector4((packed & 0x1F) / 31f,
                ((packed >> 5) & 0x1F) / 31f,
                ((packed >> 10) & 0x1F) / 31f, alpha);
        }

        private static int DecodeSigned(uint packed, int shift, int mask, int signBit)
        {
            int value = (int)((packed >> shift) & (uint)mask);
            return (value & signBit) != 0 ? value | ~mask : value;
        }

        public static void AppendIndices(List<int> triangles, List<int> lines,
            MeshPrimitiveTopology topology, int start, int count)
        {
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            switch (topology)
            {
            case MeshPrimitiveTopology.Triangles:
                for (int i = 0; i + 2 < count; i += 3)
                {
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                    triangles.Add(start + i + 2);
                }
                break;
            case MeshPrimitiveTopology.Quads:
                for (int i = 0; i + 3 < count; i += 4)
                {
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                    triangles.Add(start + i + 2);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 2);
                    triangles.Add(start + i + 3);
                }
                break;
            case MeshPrimitiveTopology.TriangleStrip:
                for (int i = 0; i + 2 < count; i++)
                {
                    if ((i & 1) == 0)
                    {
                        triangles.Add(start + i);
                        triangles.Add(start + i + 1);
                        triangles.Add(start + i + 2);
                    }
                    else
                    {
                        triangles.Add(start + i + 1);
                        triangles.Add(start + i);
                        triangles.Add(start + i + 2);
                    }
                }
                break;
            case MeshPrimitiveTopology.QuadStrip:
                for (int i = 0; i + 3 < count; i += 2)
                {
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                    triangles.Add(start + i + 3);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 3);
                    triangles.Add(start + i + 2);
                }
                break;
            case MeshPrimitiveTopology.TriangleFan:
                for (int i = 1; i + 1 < count; i++)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
                break;
            case MeshPrimitiveTopology.LineLoop:
                for (int i = 0; i < count; i++)
                {
                    lines.Add(start + i);
                    lines.Add(start + ((i + 1) % count));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unknown mesh topology.");
            }
        }

        private static int TriangleCapacity(MeshPrimitiveTopology topology, int count)
        {
            return topology switch
            {
                MeshPrimitiveTopology.Triangles => count / 3 * 3,
                MeshPrimitiveTopology.Quads => count / 4 * 6,
                MeshPrimitiveTopology.TriangleStrip => Math.Max(0, count - 2) * 3,
                MeshPrimitiveTopology.QuadStrip => Math.Max(0, (count - 2) / 2) * 6,
                MeshPrimitiveTopology.TriangleFan => Math.Max(0, count - 2) * 3,
                _ => 0
            };
        }
    }
}
