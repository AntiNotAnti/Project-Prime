using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Utility
{
    public static partial class Repack
    {
        public enum RepackTexture
        {
            Inline,
            Separate,
            Shared
        }

        public enum ComputeBounds
        {
            None,
            Capped,
            Uncapped
        }

        public class RepackOptions
        {
            public RepackTexture Texture { get; set; }
            public bool IsRoom { get; set; }
            public ComputeBounds ComputeBounds { get; set; }
            public bool WriteFile { get; set; }
            public bool Compare { get; set; }
        }

        private static (Vector3i, Vector3i) CalculateBounds(IReadOnlyList<RenderInstruction> insts)
        {
            var verts = new List<Vector3i>();
            int vtxX = 0;
            int vtxY = 0;
            int vtxZ = 0;
            void Update()
            {
                verts.Add(new Vector3i(vtxX, vtxY, vtxZ));
            }
            foreach (RenderInstruction instruction in insts)
            {
                switch (instruction.Code)
                {
                case InstructionCode.VTX_16:
                    {
                        uint xy = instruction.Arguments[0];
                        int x = (int)((xy >> 0) & 0xFFFF);
                        if ((x & 0x8000) > 0)
                        {
                            x = (int)(x | 0xFFFF0000);
                        }
                        int y = (int)((xy >> 16) & 0xFFFF);
                        if ((y & 0x8000) > 0)
                        {
                            y = (int)(y | 0xFFFF0000);
                        }
                        int z = (int)(instruction.Arguments[1] & 0xFFFF);
                        if ((z & 0x8000) > 0)
                        {
                            z = (int)(z | 0xFFFF0000);
                        }
                        vtxX = x;
                        vtxY = y;
                        vtxZ = z;
                        Update();
                    }
                    break;
                case InstructionCode.VTX_10:
                    {
                        uint xyz = instruction.Arguments[0];
                        int x = (int)((xyz >> 0) & 0x3FF);
                        if ((x & 0x200) > 0)
                        {
                            x = (int)(x | 0xFFFFFC00);
                        }
                        int y = (int)((xyz >> 10) & 0x3FF);
                        if ((y & 0x200) > 0)
                        {
                            y = (int)(y | 0xFFFFFC00);
                        }
                        int z = (int)((xyz >> 20) & 0x3FF);
                        if ((z & 0x200) > 0)
                        {
                            z = (int)(z | 0xFFFFFC00);
                        }
                        vtxX = x << 6;
                        vtxY = y << 6;
                        vtxZ = z << 6;
                        Update();
                    }
                    break;
                case InstructionCode.VTX_XY:
                    {
                        uint xy = instruction.Arguments[0];
                        int x = (int)((xy >> 0) & 0xFFFF);
                        if ((x & 0x8000) > 0)
                        {
                            x = (int)(x | 0xFFFF0000);
                        }
                        int y = (int)((xy >> 16) & 0xFFFF);
                        if ((y & 0x8000) > 0)
                        {
                            y = (int)(y | 0xFFFF0000);
                        }
                        vtxX = x;
                        vtxY = y;
                        Update();
                    }
                    break;
                case InstructionCode.VTX_XZ:
                    {
                        uint xz = instruction.Arguments[0];
                        int x = (int)((xz >> 0) & 0xFFFF);
                        if ((x & 0x8000) > 0)
                        {
                            x = (int)(x | 0xFFFF0000);
                        }
                        int z = (int)((xz >> 16) & 0xFFFF);
                        if ((z & 0x8000) > 0)
                        {
                            z = (int)(z | 0xFFFF0000);
                        }
                        vtxX = x;
                        vtxZ = z;
                        Update();
                    }
                    break;
                case InstructionCode.VTX_YZ:
                    {
                        uint yz = instruction.Arguments[0];
                        int y = (int)((yz >> 0) & 0xFFFF);
                        if ((y & 0x8000) > 0)
                        {
                            y = (int)(y | 0xFFFF0000);
                        }
                        int z = (int)((yz >> 16) & 0xFFFF);
                        if ((z & 0x8000) > 0)
                        {
                            z = (int)(z | 0xFFFF0000);
                        }
                        vtxY = y;
                        vtxZ = z;
                        Update();
                    }
                    break;
                case InstructionCode.VTX_DIFF:
                    {
                        uint xyz = instruction.Arguments[0];
                        int x = (int)((xyz >> 0) & 0x3FF);
                        if ((x & 0x200) > 0)
                        {
                            x = (int)(x | 0xFFFFFC00);
                        }
                        int y = (int)((xyz >> 10) & 0x3FF);
                        if ((y & 0x200) > 0)
                        {
                            y = (int)(y | 0xFFFFFC00);
                        }
                        int z = (int)((xyz >> 20) & 0x3FF);
                        if ((z & 0x200) > 0)
                        {
                            z = (int)(z | 0xFFFFFC00);
                        }
                        vtxX += x;
                        vtxY += y;
                        vtxZ += z;
                        Update();
                    }
                    break;
                }
            }
            int minX = Int32.MaxValue;
            int maxX = Int32.MinValue;
            int minY = Int32.MaxValue;
            int maxY = Int32.MinValue;
            int minZ = Int32.MaxValue;
            int maxZ = Int32.MinValue;
            foreach (Vector3i vert in verts)
            {
                minX = Math.Min(minX, vert.X);
                maxX = Math.Max(maxX, vert.X);
                minY = Math.Min(minY, vert.Y);
                maxY = Math.Max(maxY, vert.Y);
                minZ = Math.Min(minZ, vert.Z);
                maxZ = Math.Max(maxZ, vert.Z);
            }
            return (new Vector3i(minX, minY, minZ), new Vector3i(maxX, maxY, maxZ));
        }

        public static (byte[], byte[]) PackModel(Model model)
        {
            int recolor = 0;
            var textureInfo = new List<TextureInfo>();
            for (int i = 0; i < model.Recolors[recolor].Textures.Count; i++)
            {
                Texture texture = model.Recolors[recolor].Textures[i];
                IReadOnlyList<TextureData> data = model.Recolors[recolor].TextureData[i];
                textureInfo.Add(ConvertData(texture, data));
            }
            var paletteInfo = new List<PaletteInfo>();
            foreach (IReadOnlyList<PaletteData> data in model.Recolors[recolor].PaletteData)
            {
                paletteInfo.Add(new PaletteInfo(data.Select(d => d.Data).ToList()));
            }
            var options = new RepackOptions()
            {
                Compare = false,
                ComputeBounds = ComputeBounds.None,
                IsRoom = false,
                Texture = RepackTexture.Inline,
                WriteFile = false
            };
            return PackModel((int)model.Scale.X, model.NodeMatrixIds, model.NodePosCounts, model.Materials,
                textureInfo, paletteInfo, model.Nodes, model.Meshes, model.RenderInstructionLists, model.DisplayLists, options);
        }

        public static (byte[], byte[]) PackModel(int scale, IReadOnlyList<int> nodeMtxIds, IReadOnlyList<int> nodePosScaleCounts,
            IReadOnlyList<Material> materials, IReadOnlyList<TextureInfo> textures, IReadOnlyList<PaletteInfo> palettes,
            IReadOnlyList<Node> nodes, IReadOnlyList<Mesh> meshes, IReadOnlyList<IReadOnlyList<RenderInstruction>> renders,
            IReadOnlyList<DisplayList> dlists, RepackOptions options)
        {
            byte padByte = 0;
            ushort padShort = 0;
            uint padInt = 0;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            using MemoryStream texStream = options.Texture == RepackTexture.Inline ? stream : new MemoryStream();
            using BinaryWriter texWriter = options.Texture == RepackTexture.Inline ? writer : new BinaryWriter(texStream);
            int primitiveCount = 0;
            int vertexCount = 0;
            var dlistMin = new List<Vector3i>();
            var dlistMax = new List<Vector3i>();
            var nodeMin = new List<Vector3i>();
            var nodeMax = new List<Vector3i>();
            Debug.Assert(scale > 0);
            Debug.Assert(renders.Count == dlists.Count);
            foreach (IReadOnlyList<RenderInstruction> render in renders)
            {
                (int primitives, int vertices) = GetDlistCounts(render);
                primitiveCount += primitives;
                vertexCount += vertices;
            }
            // todo: support bounds calculation for models with weighted transforms
            if (nodeMtxIds.Count > 0)
            {
                options.ComputeBounds = ComputeBounds.None;
            }
            if (options.ComputeBounds == ComputeBounds.None)
            {
                dlistMin.AddRange(dlists.Select(d => d.MinBounds.ToIntVector()));
                dlistMax.AddRange(dlists.Select(d => d.MaxBounds.ToIntVector()));
                nodeMin.AddRange(nodes.Select(n => n.MinBounds.ToFixedVector()));
                nodeMax.AddRange(nodes.Select(n => n.MaxBounds.ToFixedVector()));
            }
            else
            {
                var allMin = new List<Vector3i>();
                var allMax = new List<Vector3i>();
                foreach (IReadOnlyList<RenderInstruction> insts in renders)
                {
                    (Vector3i min, Vector3i max) = CalculateBounds(insts);
                    allMin.Add(min);
                    allMax.Add(max);
                }
                foreach (Node node in nodes)
                {
                    IEnumerable<int> ids = node.MeshCount == 0 ? node.GetAllMeshIds(nodes, root: true) : node.GetMeshIds();
                    if (!ids.Any())
                    {
                        nodeMin.Add(new Vector3i(0, 0, 0));
                        nodeMax.Add(new Vector3i(0, 0, 0));
                    }
                    else
                    {
                        var min = new Vector3i(Int32.MaxValue, Int32.MaxValue, Int32.MaxValue);
                        var max = new Vector3i(Int32.MinValue, Int32.MinValue, Int32.MinValue);
                        foreach (int id in ids)
                        {
                            int dlistId = meshes[id].DlistId;
                            Vector3i meshMin = allMin[dlistId];
                            Vector3i meshMax = allMax[dlistId];
                            min.X = Math.Min(min.X, meshMin.X);
                            min.Y = Math.Min(min.Y, meshMin.Y);
                            min.Z = Math.Min(min.Z, meshMin.Z);
                            max.X = Math.Max(max.X, meshMax.X);
                            max.Y = Math.Max(max.Y, meshMax.Y);
                            max.Z = Math.Max(max.Z, meshMax.Z);
                        }
                        nodeMin.Add(min * scale);
                        nodeMax.Add(max * scale);
                    }
                }
                int clampMin = Int16.MinValue * scale;
                int clampMax = Int16.MaxValue * scale;
                for (int i = 0; i < allMin.Count; i++)
                {
                    Vector3i min = allMin[i];
                    Vector3i max = allMax[i];
                    if (options.ComputeBounds == ComputeBounds.Capped)
                    {
                        dlistMin.Add(new Vector3i(
                            Math.Clamp(min.X * scale, clampMin, clampMax),
                            Math.Clamp(min.Y * scale, clampMin, clampMax),
                            Math.Clamp(min.Z * scale, clampMin, clampMax)
                        ));
                        dlistMax.Add(new Vector3i(
                            Math.Clamp(max.X * scale, clampMin, clampMax),
                            Math.Clamp(max.Y * scale, clampMin, clampMax),
                            Math.Clamp(max.Z * scale, clampMin, clampMax)
                        ));
                    }
                    else
                    {
                        dlistMin.Add(min);
                        dlistMax.Add(max);
                    }
                }
            }
            // header is written last
            stream.Position = Sizes.Header;
            // node mtx IDs, node pos scale counts
            int nodeMtxIdOffset;
            int actualOffset = Sizes.Header;
            if (nodeMtxIds.Count == 0)
            {
                nodeMtxIdOffset = 0;
                // sometimes the header has no offset for the matrix IDs, but the values are actually there after the header
                // in that case, we can infer their existence and count from the pos scale count's offset
                if (nodePosScaleCounts.Count == 0)
                {
                    actualOffset += sizeof(uint);
                }
                else
                {
                    actualOffset += nodePosScaleCounts.Count * sizeof(uint);
                }
            }
            else
            {
                nodeMtxIdOffset = actualOffset;
                actualOffset += nodeMtxIds.Count * sizeof(uint);
            }
            int nodePosCountOffset;
            if (nodePosScaleCounts.Count == 0)
            {
                nodePosCountOffset = 0;
            }
            else
            {
                // in the situation described above, Read is returning the pos scale counts while ignoring the matrix IDs,
                // to avoid the matrix IDs messing with anything in the model transform code, so we can rely on the former here
                nodePosCountOffset = actualOffset;
            }
            // node matrix IDs
            if (nodeMtxIds.Count == 0)
            {
                if (options.IsRoom)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        if (nodes[i].MeshCount > 0)
                        {
                            writer.Write(i);
                        }
                    }
                }
                else
                {
                    int padCount = nodePosScaleCounts.Count == 0 ? 1 : nodePosScaleCounts.Count;
                    for (int i = 0; i < padCount; i++)
                    {
                        // basically just a hack to differentiate between goreaLaser and arcWelder
                        writer.Write(nodes.Count <= 1 ? 0 : i + 1);
                    }
                }
            }
            else
            {
                foreach (int value in nodeMtxIds)
                {
                    writer.Write(value);
                }
            }
            // node pos counts
            foreach (int value in nodePosScaleCounts)
            {
                writer.Write(value);
            }
            // texture data
            var textureDataOffsets = new List<int>();
            foreach (TextureInfo texture in textures)
            {
                textureDataOffsets.Add((int)texStream.Position);
                foreach (byte data in texture.Data)
                {
                    texWriter.Write(data);
                }
            }
            // texture metadata
            int texturesOffset = textures.Count == 0 ? 0 : (int)stream.Position;
            for (int i = 0; i < textures.Count; i++)
            {
                WriteTextureMeta(textures[i], textureDataOffsets[i], writer);
            }
            // palette data
            var paletteDataOffsets = new List<int>();
            foreach (PaletteInfo palette in palettes)
            {
                paletteDataOffsets.Add((int)texStream.Position);
                foreach (ushort data in palette.Data)
                {
                    texWriter.Write(data);
                }
            }
            // palette metdata
            int paletteOffset = palettes.Count == 0 ? 0 : (int)stream.Position;
            for (int i = 0; i < palettes.Count; i++)
            {
                PaletteInfo palette = palettes[i];
                writer.Write(paletteDataOffsets[i]);
                writer.Write(palette.Data.Count * sizeof(ushort));
                writer.Write(padInt); // VramOffset
                writer.Write(padInt); // ObjectRef
            }
            // dlist data
            var dlistResults = new List<(int, int)>();
            foreach (IReadOnlyList<RenderInstruction> render in renders)
            {
                int offset = (int)stream.Position;
                int size = WriteRenderInstructions(render, writer);
                dlistResults.Add((offset, size));
            }
            // dlist metadata
            int dlistsOffset = (int)stream.Position;
            for (int i = 0; i < dlists.Count; i++)
            {
                DisplayList dlist = dlists[i];
                Vector3i minBounds = dlistMin[i];
                Vector3i maxBounds = dlistMax[i];
                (int off, int size) = dlistResults[i];
                writer.Write(off);
                writer.Write(size);
                writer.Write(minBounds.X);
                writer.Write(minBounds.Y);
                writer.Write(minBounds.Z);
                writer.Write(maxBounds.X);
                writer.Write(maxBounds.Y);
                writer.Write(maxBounds.Z);
            }
            // materials
            int matrixIdCount = 0;
            int materialsOffset = (int)stream.Position;
            foreach (Material material in materials)
            {
                int matrixId = GetTextureMatrixId(material, ref matrixIdCount);
                WriteMaterial(material, matrixId, writer);
            }
            // nodes
            int nodesOffset = (int)stream.Position;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                Vector3i minBounds = nodeMin[i];
                Vector3i maxBounds = nodeMax[i];
                WriteNode(node, minBounds, maxBounds, writer);
            }
            // meshes
            int meshesOffset = (int)stream.Position;
            foreach (Mesh mesh in meshes)
            {
                writer.Write((ushort)mesh.MaterialId);
                writer.Write((ushort)mesh.DlistId);
            }
            stream.Position = 0;
            // header
            int scaleFactor = (int)Math.Log2(scale);
            Debug.Assert(Math.Pow(2, scaleFactor) == scale);
            int scaleBase = 4096;
            int nodeAnimOffset = 0;
            int uvAnimOffset = 0;
            int matAnimOffset = 0;
            int texAnimOffset = 0;
            writer.Write(scaleFactor);
            writer.Write(scaleBase);
            writer.Write(primitiveCount);
            writer.Write(vertexCount);
            writer.Write(materialsOffset);
            writer.Write(dlistsOffset);
            writer.Write(nodesOffset);
            writer.Write((ushort)nodeMtxIds.Count);
            writer.Write(padByte); // Flags
            writer.Write(padByte);
            writer.Write(nodeMtxIdOffset);
            writer.Write(meshesOffset);
            writer.Write((ushort)textures.Count);
            writer.Write(padShort);
            writer.Write(texturesOffset);
            writer.Write((ushort)palettes.Count);
            writer.Write(padShort);
            writer.Write(paletteOffset);
            writer.Write(nodePosCountOffset);
            writer.Write(padInt); // NodePosScales
            writer.Write(padInt); // NodeInitialPosition
            writer.Write(padInt); // NodePosition
            writer.Write((ushort)materials.Count);
            writer.Write((ushort)nodes.Count);
            writer.Write(padInt); // TextureMatrixOffset
            writer.Write(nodeAnimOffset);
            writer.Write(uvAnimOffset);
            writer.Write(matAnimOffset);
            writer.Write(texAnimOffset);
            writer.Write((ushort)meshes.Count);
            writer.Write((ushort)matrixIdCount);
            Debug.Assert(stream.Position == Sizes.Header);
            return (stream.ToArray(), texStream == stream ? Array.Empty<byte>() : texStream.ToArray());
        }

        private static (int primitives, int vertices) GetDlistCounts(IReadOnlyList<RenderInstruction> dlist)
        {
            int primitiveCount = 0;
            int vertexCount = 0;
            int vertexType = -1;
            int currentVertexCount = 0;
            foreach (RenderInstruction instruction in dlist)
            {
                switch (instruction.Code)
                {
                case InstructionCode.BEGIN_VTXS:
                    Debug.Assert(vertexType == -1 && currentVertexCount == 0);
                    vertexType = (int)instruction.Arguments[0];
                    break;
                case InstructionCode.VTX_16:
                case InstructionCode.VTX_10:
                case InstructionCode.VTX_XY:
                case InstructionCode.VTX_XZ:
                case InstructionCode.VTX_YZ:
                case InstructionCode.VTX_DIFF:
                    vertexCount++;
                    currentVertexCount++;
                    break;
                case InstructionCode.END_VTXS:
                    if (vertexType == 0)
                    {
                        Debug.Assert(currentVertexCount >= 3 && currentVertexCount % 3 == 0);
                        primitiveCount += currentVertexCount / 3;
                    }
                    else if (vertexType == 1)
                    {
                        Debug.Assert(currentVertexCount >= 4 && currentVertexCount % 4 == 0);
                        primitiveCount += currentVertexCount / 4;
                    }
                    else if (vertexType == 2)
                    {
                        Debug.Assert(currentVertexCount >= 3);
                        primitiveCount += 1 + currentVertexCount - 3;
                    }
                    else if (vertexType == 3)
                    {
                        Debug.Assert(currentVertexCount >= 4 && currentVertexCount % 2 == 0);
                        primitiveCount += 1 + (currentVertexCount - 4) / 2;
                    }
                    vertexType = -1;
                    currentVertexCount = 0;
                    break;
                }
            }
            return (primitiveCount, vertexCount);
        }

        public static TextureInfo ConvertData(Texture texture, IReadOnlyList<TextureData> data)
        {
            var imageData = new List<byte>();

            if (texture.Format == TextureFormat.DirectRgb)
            {
                foreach (TextureData entry in data)
                {
                    // alpha bit is already present in the ushort value
                    imageData.Add((byte)(entry.Data & 0xFF));
                    imageData.Add((byte)(entry.Data >> 8));
                }
            }
            else if (texture.Format == TextureFormat.PaletteA3I5)
            {
                foreach (TextureData entry in data)
                {
                    byte value = (byte)entry.Data;
                    byte alpha = (byte)Math.Round(entry.Alpha * 7f / 255f);
                    value |= (byte)(alpha << 5);
                    imageData.Add(value);
                }
            }
            else if (texture.Format == TextureFormat.PaletteA5I3)
            {
                foreach (TextureData entry in data)
                {
                    byte value = (byte)entry.Data;
                    byte alpha = (byte)Math.Round(entry.Alpha * 31f / 255f);
                    value |= (byte)(alpha << 3);
                    imageData.Add(value);
                }
            }
            else if (texture.Format == TextureFormat.Palette2Bit)
            {
                for (int i = 0; i < data.Count; i += 4)
                {
                    byte value = 0;
                    for (int j = 0; j < 4 && i + j < data.Count; j++)
                    {
                        uint index = data[i + j].Data;
                        value |= (byte)(index << (2 * j));
                    }
                    imageData.Add(value);
                }
            }
            else if (texture.Format == TextureFormat.Palette4Bit)
            {
                for (int i = 0; i < data.Count; i += 2)
                {
                    byte value = 0;
                    for (int j = 0; j < 2 && i + j < data.Count; j++)
                    {
                        uint index = data[i + j].Data;
                        value |= (byte)(index << (4 * j));
                    }
                    imageData.Add(value);
                }
            }
            else if (texture.Format == TextureFormat.Palette8Bit)
            {
                foreach (TextureData entry in data)
                {
                    imageData.Add((byte)entry.Data);
                }
            }
            return new TextureInfo(texture.Format, texture.Opaque != 0, texture.Height, texture.Width, imageData);
        }

        private static void WriteTextureMeta(TextureInfo texture, int offset, BinaryWriter writer)
        {
            byte padByte = 0;
            ushort padShort = 0;
            uint padInt = 0;
            writer.Write((byte)texture.Format);
            writer.Write(padByte);
            writer.Write(texture.Width);
            writer.Write(texture.Height);
            writer.Write(padShort);
            writer.Write(offset);
            writer.Write(texture.Data.Count);
            writer.Write(padInt); // UnusedOffset
            writer.Write(padInt); // UnusedCount
            writer.Write(padInt); // VramOffset
            writer.WriteInt(texture.Opaque);
            writer.Write(padInt); // SkipVram
            writer.Write(padByte); // PackedSize
            writer.Write(padByte); // NativeTextureFormat
            writer.Write(padShort); // ObjectRef
        }

        private static int WriteRenderInstructions(IReadOnlyList<RenderInstruction> list, BinaryWriter writer)
        {
            int bytesWritten = 0;
            var arguments = new List<uint>();
            Debug.Assert(list.Count % 4 == 0);
            for (int i = 0; i < list.Count; i += 4)
            {
                arguments.Clear();
                uint packedCommands = 0;
                for (int j = 0; j < 4; j++)
                {
                    RenderInstruction inst = list[i + j];
                    uint code = (((uint)inst.Code) - 0x400) >> 2;
                    packedCommands |= code << (8 * j);
                    arguments.AddRange(inst.Arguments);
                }
                writer.Write(packedCommands);
                bytesWritten += sizeof(uint);
                foreach (uint argument in arguments)
                {
                    writer.Write(argument);
                    bytesWritten += sizeof(uint);
                }
            }
            return bytesWritten;
        }

        private static int GetTextureMatrixId(Material material, ref int indexCount)
        {
            int scaleS = Fixed.ToInt(material.ScaleS);
            int scaleT = Fixed.ToInt(material.ScaleT);
            ushort rotZ = (ushort)Math.Round(material.RotateZ / MathF.PI / 2f * 65536f);
            int transS = Fixed.ToInt(material.TranslateS);
            int transT = Fixed.ToInt(material.TranslateT);
            // materials with no texgen mode and default transform values get a matrix ID of 0 and don't contribute to the matrix count.
            // if the values are non-default, a matrix index is assigned even though the values won't be used due to the lack of texgen mode.
            // also, if the texgen mode is set, then even materials with default transform values will have a matrix index assigned, with no sharing.
            if (scaleS == 4096 && scaleT == 4096 && rotZ == 0 && transS == 0 && transT == 0 && material.TexgenMode == TexgenMode.None)
            {
                return -1;
            }
            return indexCount++;
        }

        private static void WriteMaterial(Material material, int matrixId, BinaryWriter writer)
        {
            byte padByte = 0;
            ushort padShort = 0;
            writer.WriteString(material.Name, length: 64);
            writer.Write(material.Lighting);
            writer.Write((byte)material.Culling);
            writer.Write(material.Alpha);
            writer.Write(material.Wireframe);
            writer.Write((short)material.PaletteId);
            writer.Write((short)material.TextureId);
            writer.Write((byte)material.XRepeat);
            writer.Write((byte)material.YRepeat);
            writer.Write(material.Diffuse.Red);
            writer.Write(material.Diffuse.Green);
            writer.Write(material.Diffuse.Blue);
            writer.Write(material.Ambient.Red);
            writer.Write(material.Ambient.Green);
            writer.Write(material.Ambient.Blue);
            writer.Write(material.Specular.Red);
            writer.Write(material.Specular.Green);
            writer.Write(material.Specular.Blue);
            writer.Write(padByte);
            writer.Write((uint)material.PolygonMode);
            writer.Write((byte)material.RenderMode);
            writer.Write((byte)material.AnimationFlags);
            writer.Write(padShort);
            writer.Write((uint)material.TexgenMode);
            writer.Write(padShort); // TexcoordAnimationId
            writer.Write(padShort);
            writer.Write(matrixId == -1 ? 0 : matrixId);
            writer.WriteFloat(material.ScaleS);
            writer.WriteFloat(material.ScaleT);
            writer.WriteAngle(material.RotateZ);
            writer.Write(padShort);
            writer.WriteFloat(material.TranslateS);
            writer.WriteFloat(material.TranslateT);
            writer.Write(padShort); // MaterialAnimationId
            writer.Write(padShort); // TextureAnimationId
            writer.Write(padByte); // PackedRepeatMode
            writer.Write(padByte);
            writer.Write(padShort);
        }

        private static void WriteNode(Node node, Vector3i minBounds, Vector3i maxBounds, BinaryWriter writer)
        {
            byte padByte = 0;
            ushort padShort = 0;
            uint padInt = 0;
            writer.WriteString(node.Name, length: 64);
            writer.Write((short)node.ParentIndex);
            writer.Write((short)node.ChildIndex);
            writer.Write((short)node.NextIndex);
            writer.Write(padShort);
            writer.WriteInt(node.Enabled);
            writer.Write((short)node.MeshCount);
            writer.Write((short)node.MeshId);
            writer.WriteVector3(node.Scale);
            writer.WriteAngles(node.Angle);
            writer.Write(padShort);
            writer.WriteVector3(node.Position);
            writer.WriteFloat(node.BoundingRadius);
            writer.Write(minBounds.X);
            writer.Write(minBounds.Y);
            writer.Write(minBounds.Z);
            writer.Write(maxBounds.X);
            writer.Write(maxBounds.Y);
            writer.Write(maxBounds.Z);
            writer.Write((byte)node.BillboardMode);
            writer.Write(padByte);
            writer.Write(padShort);
            for (int i = 0; i < 12; i++)
            {
                writer.Write(padInt); // transform MtxFx43
            }
            writer.Write(padInt); // BeforeTransform
            writer.Write(padInt); // AfterTransform
            writer.Write(padInt); // UnusedC8
            writer.Write(padInt); // UnusedCC
            writer.Write(padInt); // UnusedD0
            writer.Write(padInt); // UnusedD4
            writer.Write(padInt); // UnusedD8
            writer.Write(padInt); // UnusedDC
            writer.Write(padInt); // UnusedE0
            writer.Write(padInt); // UnusedE4
            writer.Write(padInt); // UnusedE8
            writer.Write(padInt); // UnusedEC
        }

        public class TextureInfo
        {
            public TextureFormat Format { get; }
            public bool Opaque { get; }
            public ushort Height { get; }
            public ushort Width { get; }
            public IReadOnlyList<byte> Data { get; }

            public TextureInfo(TextureFormat format, bool opaque, ushort height, ushort width,
                IReadOnlyList<byte> data)
            {
                Format = format;
                Opaque = opaque;
                Height = height;
                Width = width;
                Data = data;
            }
        }

        public class PaletteInfo
        {
            public IReadOnlyList<ushort> Data { get; set; }

            public PaletteInfo(IReadOnlyList<ushort> data)
            {
                Data = data;
            }
        }

        private class TexMtxMap
        {
            private readonly Dictionary<(ushort, ushort, int, int, ushort, int, int), int> _dict
                = new Dictionary<(ushort, ushort, int, int, ushort, int, int), int>();

            public int Count => _dict.Count;

            public bool TryGetValue(ushort width, ushort height, int scaleS, int scaleT, ushort rotZ, int transS, int transT, out int index)
            {
                return _dict.TryGetValue((width, height, scaleS, scaleT, rotZ, transS, transT), out index);
            }

            public void Add(ushort width, ushort height, int scaleS, int scaleT, ushort rotZ, int transS, int transT, int index)
            {
                _dict.Add((width, height, scaleS, scaleT, rotZ, transS, transT), index);
            }
        }

        public static void WriteString(this BinaryWriter writer, string value, int length)
        {
            Debug.Assert(value.Length <= length);
            int i = 0;
            for (; i < value.Length; i++)
            {
                writer.Write((byte)value[i]);
            }
            for (; i < length; i++)
            {
                writer.Write('\0');
            }
        }

        public static void WriteFloat(this BinaryWriter writer, float value)
        {
            writer.Write(Fixed.ToInt(value));
        }

        public static void WriteVector3(this BinaryWriter writer, Vector3 vector)
        {
            writer.WriteFloat(vector.X);
            writer.WriteFloat(vector.Y);
            writer.WriteFloat(vector.Z);
        }

        public static void WriteVector4(this BinaryWriter writer, Vector4 vector)
        {
            writer.WriteFloat(vector.X);
            writer.WriteFloat(vector.Y);
            writer.WriteFloat(vector.Z);
            writer.WriteFloat(vector.W);
        }

        public static void WriteColorRgb(this BinaryWriter writer, ColorRgb color)
        {
            writer.Write(color.Red);
            writer.Write(color.Green);
            writer.Write(color.Blue);
        }

        public static void WriteAngle(this BinaryWriter writer, float angle)
        {
            writer.Write((ushort)Math.Round(angle / MathF.PI / 2f * 65536f));
        }

        public static void WriteAngles(this BinaryWriter writer, Vector3 angles)
        {
            writer.WriteAngle(angles.X);
            writer.WriteAngle(angles.Y);
            writer.WriteAngle(angles.Z);
        }

        public static void WriteByte(this BinaryWriter writer, bool value)
        {
            byte yes = 1;
            byte no = 0;
            writer.Write(value ? yes : no);
        }

        public static void WriteInt(this BinaryWriter writer, bool value)
        {
            uint yes = 1;
            uint no = 0;
            writer.Write(value ? yes : no);
        }
    }
}
