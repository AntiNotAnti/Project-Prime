using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace MphRead.Mods.Render.Characters
{
    internal readonly record struct CharacterRigidVertex(Vector3 Position, Vector3 Normal, Vector2 Texcoord, Vector3 Color);

    internal sealed record CharacterRigidPrimitive(
        string SourceNode,
        string TargetNode,
        string? MaterialName,
        CharacterRigidVertex[] Vertices,
        uint[] Indices,
        CharacterEmbeddedAlbedo? Albedo = null,
        CharacterEmbeddedMaterialMaps? MaterialMaps = null, bool DoubleSided = false);

    internal sealed record CharacterRigidModelData(IReadOnlyList<CharacterRigidPrimitive> Primitives)
    {
        public int VertexCount => Primitives.Sum(p => p.Vertices.Length);
        public int IndexCount => Primitives.Sum(p => p.Indices.Length);
    }

    /// <summary>
    /// Minimal, deliberately bounded glTF 2.0 geometry reader for rigid-node
    /// character replacements. Map Studio's importer is a different contract:
    /// this one preserves source node identity so each mesh segment can follow
    /// an already-animated native MPH node.
    /// </summary>
    internal static class CharacterRigidModelLoader
    {
        public const int MaximumVertices = 500_000;
        public const int MaximumIndices = 1_500_000;

        public static CharacterRigidModelData Load(CharacterModelAsset asset)
        {
            if (asset.Skinning != CharacterSkinningMode.RigidNodes)
                throw new InvalidDataException("This renderer slice supports rigid-node character models only.");

            (JsonDocument document, byte[] binary) = ReadGlb(asset.ModelPath);
            using (document)
            {
                JsonElement root = document.RootElement;
                JsonElement[] buffers = Elements(root, "buffers");
                if (buffers.Length != 1 || buffers[0].TryGetProperty("uri", out _))
                    throw new InvalidDataException("Character GLBs must contain exactly one embedded binary buffer.");
                int declaredBufferLength = Int(buffers[0], "byteLength", -1);
                if (declaredBufferLength < 0 || declaredBufferLength > binary.Length)
                    throw new InvalidDataException("Character GLB binary buffer is truncated.");

                JsonElement[] views = Elements(root, "bufferViews");
                JsonElement[] accessors = Elements(root, "accessors");
                JsonElement[] meshes = Elements(root, "meshes");
                JsonElement[] nodes = Elements(root, "nodes");
                JsonElement[] materials = Elements(root, "materials");

                var nodeByName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (JsonElement node in nodes)
                {
                    if (node.TryGetProperty("name", out JsonElement nameProperty)
                        && nameProperty.GetString() is string name && name.Length > 0)
                        nodeByName.Add(name, node);
                }

                var primitives = new List<CharacterRigidPrimitive>();
                var albedos = new Dictionary<int, CharacterEmbeddedAlbedo?>();
                var materialMaps = new Dictionary<int, CharacterEmbeddedMaterialMaps?>();
                int totalVertices = 0;
                int totalIndices = 0;
                foreach ((string sourceNode, string targetNode) in asset.BoneMap)
                {
                    if (!nodeByName.TryGetValue(sourceNode, out JsonElement node))
                        throw new InvalidDataException($"Character GLB no longer contains mapped node '{sourceNode}'.");
                    if (node.TryGetProperty("skin", out _))
                        throw new InvalidDataException($"Rigid node '{sourceNode}' is skinned; use a rigid segmented export or the future Weighted4 path.");
                    if (!node.TryGetProperty("mesh", out JsonElement meshProperty))
                        continue;

                    int meshIndex = meshProperty.GetInt32();
                    if ((uint)meshIndex >= meshes.Length)
                        throw new InvalidDataException($"Character node '{sourceNode}' references an invalid mesh.");
                    JsonElement[] sourcePrimitives = Elements(meshes[meshIndex], "primitives");
                    foreach (JsonElement primitive in sourcePrimitives)
                    {
                        if (Int(primitive, "mode", 4) != 4)
                            throw new InvalidDataException($"Character node '{sourceNode}' uses a non-triangle primitive.");
                        if (primitive.TryGetProperty("targets", out _))
                            throw new InvalidDataException($"Character node '{sourceNode}' uses morph targets, which are not supported by rigid replacements.");
                        JsonElement attributes = primitive.GetProperty("attributes");
                        if (attributes.TryGetProperty("JOINTS_0", out _) || attributes.TryGetProperty("WEIGHTS_0", out _))
                            throw new InvalidDataException($"Rigid node '{sourceNode}' contains skin weights; use a rigid segmented export.");

                        int positionAccessor = Int(attributes, "POSITION", -1);
                        if (positionAccessor < 0)
                            throw new InvalidDataException($"Character node '{sourceNode}' has no POSITION attribute.");

                        Vector3[] positions = ReadVec3(accessors, views, binary, positionAccessor, "POSITION");
                        Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalProperty)
                            ? ReadVec3(accessors, views, binary, normalProperty.GetInt32(), "NORMAL")
                            : Array.Empty<Vector3>();
                        Vector2[] texcoords = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvProperty)
                            ? ReadVec2(accessors, views, binary, uvProperty.GetInt32(), "TEXCOORD_0")
                            : Array.Empty<Vector2>();
                        Vector3[] colors = attributes.TryGetProperty("COLOR_0", out JsonElement colorProperty)
                            ? ReadVec3(accessors, views, binary, colorProperty.GetInt32(), "COLOR_0")
                            : Array.Empty<Vector3>();
                        if (colors.Length != 0 && colors.Length != positions.Length)
                            throw new InvalidDataException($"Character node '{sourceNode}' COLOR_0 count differs from POSITION.");
                        if (normals.Length != 0 && normals.Length != positions.Length)
                            throw new InvalidDataException($"Character node '{sourceNode}' NORMAL count differs from POSITION.");
                        if (texcoords.Length != 0 && texcoords.Length != positions.Length)
                            throw new InvalidDataException($"Character node '{sourceNode}' TEXCOORD_0 count differs from POSITION.");

                        uint[] indices = primitive.TryGetProperty("indices", out JsonElement indexProperty)
                            ? ReadIndices(accessors, views, binary, indexProperty.GetInt32())
                            : Enumerable.Range(0, positions.Length).Select(i => (uint)i).ToArray();
                        if (indices.Length == 0 || indices.Length % 3 != 0)
                            throw new InvalidDataException($"Character node '{sourceNode}' has an invalid triangle index stream.");
                        if (indices.Any(index => index >= positions.Length))
                            throw new InvalidDataException($"Character node '{sourceNode}' contains an out-of-range index.");

                        totalVertices = checked(totalVertices + positions.Length);
                        totalIndices = checked(totalIndices + indices.Length);
                        if (totalVertices > MaximumVertices || totalIndices > MaximumIndices)
                            throw new InvalidDataException("Character GLB exceeds the rigid geometry budget.");

                        if (normals.Length == 0)
                            normals = GenerateNormals(positions, indices);
                        else
                        {
                            for (int i = 0; i < normals.Length; i++)
                            {
                                float length = normals[i].Length();
                                if (!float.IsFinite(length) || length < 0.000001f)
                                    throw new InvalidDataException($"Character node '{sourceNode}' contains an invalid normal.");
                                normals[i] /= length;
                            }
                        }

                        int materialIndex = -1;
                        string? materialName = null;
                        CharacterEmbeddedAlbedo? albedo = null;
                        CharacterEmbeddedMaterialMaps? maps = null;
                        if (primitive.TryGetProperty("material", out JsonElement materialProperty))
                        {
                            materialIndex = materialProperty.GetInt32();
                            if ((uint)materialIndex >= materials.Length)
                                throw new InvalidDataException($"Character node '{sourceNode}' references an invalid material.");
                            if (materials[materialIndex].TryGetProperty("name", out JsonElement materialNameProperty))
                                materialName = materialNameProperty.GetString();
                            if (String.IsNullOrWhiteSpace(materialName))
                                throw new InvalidDataException($"Character node '{sourceNode}' material {materialIndex} must be named to map to a native material.");
                            if (!albedos.TryGetValue(materialIndex, out albedo))
                            {
                                albedo = CharacterEmbeddedMaterialLoader.ReadAlbedo(root, materials, views, binary, materialIndex);
                                albedos.Add(materialIndex, albedo);
                            }
                            if (!materialMaps.TryGetValue(materialIndex, out maps))
                            {
                                maps = CharacterEmbeddedMaterialLoader.ReadMaterialMaps(root, materials, views, binary, materialIndex, albedo);
                                materialMaps.Add(materialIndex, maps);
                            }
                        }

                        var vertices = new CharacterRigidVertex[positions.Length];
                        for (int i = 0; i < positions.Length; i++)
                        {
                            Vector2 uv = texcoords.Length == 0 ? Vector2.Zero : texcoords[i];
                            if (!Finite(positions[i]) || !Finite(uv))
                                throw new InvalidDataException($"Character node '{sourceNode}' contains non-finite vertex data.");
                            Vector3 color = colors.Length == 0 ? Vector3.One : colors[i];
                            if (!Finite(color) || color.X < 0 || color.Y < 0 || color.Z < 0
                                || color.X > 1 || color.Y > 1 || color.Z > 1)
                                throw new InvalidDataException($"Character node '{sourceNode}' has invalid COLOR_0 values.");
                            vertices[i] = new(positions[i], normals[i], uv, color);
                        }
                        primitives.Add(new(sourceNode, targetNode, materialName, vertices, indices, albedo, maps,
                            CharacterEmbeddedMaterialLoader.ReadDoubleSided(materials, materialIndex)));
                    }
                }

                if (primitives.Count == 0)
                    throw new InvalidDataException("Rigid character GLB has no mesh primitives on mapped source nodes.");
                return new CharacterRigidModelData(primitives);
            }
        }

        private static (JsonDocument Document, byte[] Binary) ReadGlb(string path)
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (U32(header) != 0x46546C67 || U32(header[4..]) != 2 || U32(header[8..]) != stream.Length)
                throw new InvalidDataException("Invalid character GLB header.");

            JsonDocument? document = null;
            byte[]? binary = null;
            Span<byte> chunkHeader = stackalloc byte[8];
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 8)
                    throw new InvalidDataException("Truncated character GLB chunk header.");
                stream.ReadExactly(chunkHeader);
                uint length = U32(chunkHeader);
                uint type = U32(chunkHeader[4..]);
                if (length > stream.Length - stream.Position || length > CharacterModelPack.MaximumModelBytes)
                    throw new InvalidDataException("Character GLB chunk exceeds its bounds.");
                byte[] bytes = new byte[(int)length];
                stream.ReadExactly(bytes);
                if (type == 0x4E4F534A)
                {
                    if (document != null || length == 0 || length > CharacterModelPack.MaximumGlbJsonBytes)
                        throw new InvalidDataException("Character GLB has an invalid JSON chunk.");
                    document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
                }
                else if (type == 0x004E4942)
                {
                    if (binary != null) throw new InvalidDataException("Character GLB has more than one BIN chunk.");
                    binary = bytes;
                }
            }
            if (document == null || binary == null)
            {
                document?.Dispose();
                throw new InvalidDataException("Character GLB must contain JSON and BIN chunks.");
            }
            return (document, binary);
        }

        private static Vector3[] ReadVec3(JsonElement[] accessors, JsonElement[] views, byte[] binary,
            int accessorIndex, string semantic)
        {
            AccessorView accessor = ResolveAccessor(accessors, views, binary, accessorIndex, semantic, "VEC3", 5126);
            var result = new Vector3[accessor.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = accessor.Row(i);
                result[i] = new Vector3(F32(row), F32(row[4..]), F32(row[8..]));
            }
            return result;
        }

        private static Vector2[] ReadVec2(JsonElement[] accessors, JsonElement[] views, byte[] binary,
            int accessorIndex, string semantic)
        {
            AccessorView accessor = ResolveAccessor(accessors, views, binary, accessorIndex, semantic, "VEC2", 5126);
            var result = new Vector2[accessor.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = accessor.Row(i);
                result[i] = new Vector2(F32(row), F32(row[4..]));
            }
            return result;
        }

        private static uint[] ReadIndices(JsonElement[] accessors, JsonElement[] views, byte[] binary, int accessorIndex)
        {
            if ((uint)accessorIndex >= accessors.Length)
                throw new InvalidDataException("Character GLB has an invalid index accessor.");
            JsonElement accessor = accessors[accessorIndex];
            if (accessor.TryGetProperty("sparse", out _))
                throw new InvalidDataException("Sparse character accessors are not supported in this slice.");
            if (!String.Equals(accessor.GetProperty("type").GetString(), "SCALAR", StringComparison.Ordinal))
                throw new InvalidDataException("Character indices must be SCALAR.");

            int componentType = Int(accessor, "componentType", -1);
            int size = componentType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 };
            if (size == 0) throw new InvalidDataException("Character indices must use unsigned byte, ushort or uint.");
            AccessorView view = ResolveAccessor(accessors, views, binary, accessorIndex, "indices", "SCALAR", componentType);
            var result = new uint[view.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = view.Row(i);
                result[i] = size switch
                {
                    1 => row[0],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(row),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(row)
                };
            }
            return result;
        }

        private static AccessorView ResolveAccessor(JsonElement[] accessors, JsonElement[] views, byte[] binary,
            int accessorIndex, string semantic, string expectedType, int expectedComponent)
        {
            if ((uint)accessorIndex >= accessors.Length)
                throw new InvalidDataException($"Character GLB has an invalid {semantic} accessor.");
            JsonElement accessor = accessors[accessorIndex];
            if (accessor.TryGetProperty("sparse", out _))
                throw new InvalidDataException($"Sparse {semantic} accessors are not supported in this slice.");
            if (!String.Equals(accessor.GetProperty("type").GetString(), expectedType, StringComparison.Ordinal)
                || Int(accessor, "componentType", -1) != expectedComponent
                || accessor.TryGetProperty("normalized", out JsonElement normalized) && normalized.GetBoolean())
                throw new InvalidDataException($"Character {semantic} accessor has an unsupported format.");
            int count = Int(accessor, "count", -1);
            if (count <= 0 || count > MaximumVertices && semantic != "indices"
                || count > MaximumIndices && semantic == "indices")
                throw new InvalidDataException($"Character {semantic} accessor exceeds its element budget.");
            int viewIndex = Int(accessor, "bufferView", -1);
            if ((uint)viewIndex >= views.Length)
                throw new InvalidDataException($"Character {semantic} accessor has no valid buffer view.");

            JsonElement view = views[viewIndex];
            if (Int(view, "buffer", -1) != 0)
                throw new InvalidDataException("Character GLB buffer view references an unsupported buffer.");
            int viewOffset = Int(view, "byteOffset", 0);
            int viewLength = Int(view, "byteLength", -1);
            int accessorOffset = Int(accessor, "byteOffset", 0);
            int elementSize = expectedType switch
            {
                "VEC3" => 12,
                "VEC2" => 8,
                _ => expectedComponent switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 }
            };
            int stride = Int(view, "byteStride", elementSize);
            if (viewOffset < 0 || viewLength < 0 || accessorOffset < 0 || stride < elementSize
                || stride > 252 || viewOffset + (long)viewLength > binary.Length
                || accessorOffset + (long)(count - 1) * stride + elementSize > viewLength)
                throw new InvalidDataException($"Character {semantic} accessor exceeds its buffer view.");
            return new(binary, checked(viewOffset + accessorOffset), count, stride, elementSize);
        }

        private static Vector3[] GenerateNormals(Vector3[] positions, uint[] indices)
        {
            var result = new Vector3[positions.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = (int)indices[i], b = (int)indices[i + 1], c = (int)indices[i + 2];
                Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                float length = normal.Length();
                if (!float.IsFinite(length) || length < 0.000001f) continue;
                normal /= length;
                result[a] += normal;
                result[b] += normal;
                result[c] += normal;
            }
            for (int i = 0; i < result.Length; i++)
            {
                float length = result[i].Length();
                result[i] = length < 0.000001f ? Vector3.UnitY : result[i] / length;
            }
            return result;
        }

        private static JsonElement[] Elements(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToArray() : System.Array.Empty<JsonElement>();

        private static int Int(JsonElement element, string name, int fallback = 0)
            => element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;

        private static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        private static float F32(ReadOnlySpan<byte> bytes)
            => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));

        private static bool Finite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        private static bool Finite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private readonly struct AccessorView
        {
            private readonly byte[] _bytes;
            private readonly int _offset;
            public int Count { get; }
            private readonly int _stride;
            private readonly int _elementSize;

            public AccessorView(byte[] bytes, int offset, int count, int stride, int elementSize)
            {
                _bytes = bytes;
                _offset = offset;
                Count = count;
                _stride = stride;
                _elementSize = elementSize;
            }

            public ReadOnlySpan<byte> Row(int index)
                => _bytes.AsSpan(checked(_offset + index * _stride), _elementSize);
        }
    }
}
