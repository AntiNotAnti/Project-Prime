using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using OMatrix4 = OpenTK.Mathematics.Matrix4;

namespace MphRead.Mods.Render.Characters
{
    internal readonly record struct CharacterWeightedVertex(
        NVector3 Position,
        NVector3 Normal,
        NVector2 Texcoord,
        NVector4 Weights,
        int PackedJoints);

    internal sealed record CharacterWeightedPrimitive(
        string? MaterialName,
        CharacterWeightedVertex[] Vertices,
        uint[] Indices);

    internal sealed record CharacterWeightedJoint(
        string SourceNode,
        string TargetNode,
        OMatrix4 InverseBind);

    internal sealed record CharacterWeightedModelData(
        IReadOnlyList<CharacterWeightedPrimitive> Primitives,
        IReadOnlyList<CharacterWeightedJoint> Joints)
    {
        public int VertexCount => Primitives.Sum(p => p.Vertices.Length);
        public int IndexCount => Primitives.Sum(p => p.Indices.Length);
    }

    /// <summary>
    /// Bounded glTF 2.0 reader for smooth four-influence character skins.
    ///
    /// The glTF skin is presentation data only. Joint names are retargeted to
    /// the native MPH node hierarchy through the character pack's BoneMap; the
    /// game continues to own animation state and produces the pose matrices.
    /// </summary>
    internal static class CharacterWeightedModelLoader
    {
        public const int MaximumVertices = CharacterRigidModelLoader.MaximumVertices;
        public const int MaximumIndices = CharacterRigidModelLoader.MaximumIndices;
        public const int MaximumJoints = 32;

        public static CharacterWeightedModelData Load(CharacterModelAsset asset)
        {
            if (asset.Skinning != CharacterSkinningMode.Weighted4)
                throw new InvalidDataException("Weighted character loader requires skinning=weighted4.");

            (JsonDocument document, byte[] binary) = ReadGlb(asset.ModelPath);
            using (document)
            {
                JsonElement root = document.RootElement;
                JsonElement[] buffers = Elements(root, "buffers");
                if (buffers.Length != 1 || buffers[0].TryGetProperty("uri", out _))
                    throw new InvalidDataException("Weighted character GLBs need one embedded binary buffer.");
                int declaredBufferLength = Int(buffers[0], "byteLength", -1);
                if (declaredBufferLength < 0 || declaredBufferLength > binary.Length)
                    throw new InvalidDataException("Weighted character GLB binary buffer is truncated.");

                JsonElement[] views = Elements(root, "bufferViews");
                JsonElement[] accessors = Elements(root, "accessors");
                JsonElement[] meshes = Elements(root, "meshes");
                JsonElement[] nodes = Elements(root, "nodes");
                JsonElement[] materials = Elements(root, "materials");
                JsonElement[] skins = Elements(root, "skins");
                if (skins.Length == 0)
                    throw new InvalidDataException("Weighted character GLB contains no skin.");

                var referencedSkins = new HashSet<int>();
                var skinnedNodes = new List<JsonElement>();
                foreach (JsonElement node in nodes)
                {
                    bool hasMesh = node.TryGetProperty("mesh", out _);
                    bool hasSkin = node.TryGetProperty("skin", out JsonElement skinProperty);
                    if (hasMesh && hasSkin)
                    {
                        int skin = skinProperty.GetInt32();
                        if ((uint)skin >= skins.Length)
                            throw new InvalidDataException("Weighted character node references an invalid skin.");
                        referencedSkins.Add(skin);
                        skinnedNodes.Add(node);
                        ValidateIdentityMeshNode(node);
                    }
                }
                if (skinnedNodes.Count == 0)
                    throw new InvalidDataException("Weighted character GLB has no skinned mesh node.");
                if (referencedSkins.Count != 1)
                    throw new InvalidDataException("Weighted character GLB must use one shared skin per replacement.");

                int skinIndex = referencedSkins.Single();
                JsonElement skinDef = skins[skinIndex];
                JsonElement[] jointElements = Elements(skinDef, "joints");
                if (jointElements.Length == 0 || jointElements.Length > MaximumJoints)
                    throw new InvalidDataException($"Weighted character skin must contain 1-{MaximumJoints} joints.");

                var joints = new List<CharacterWeightedJoint>(jointElements.Length);
                var seenSource = new HashSet<string>(StringComparer.Ordinal);
                OMatrix4[] inverseBinds = ReadInverseBinds(
                    accessors, views, binary, skinDef, jointElements.Length);

                for (int i = 0; i < jointElements.Length; i++)
                {
                    int nodeIndex = jointElements[i].GetInt32();
                    if ((uint)nodeIndex >= nodes.Length)
                        throw new InvalidDataException($"Weighted skin joint {i} references an invalid node.");
                    JsonElement jointNode = nodes[nodeIndex];
                    if (!jointNode.TryGetProperty("name", out JsonElement nameProperty)
                        || String.IsNullOrWhiteSpace(nameProperty.GetString()))
                        throw new InvalidDataException($"Weighted skin joint {i} must have a stable node name.");
                    string source = nameProperty.GetString()!;
                    if (!seenSource.Add(source))
                        throw new InvalidDataException($"Weighted skin repeats joint node '{source}'.");
                    if (!asset.BoneMap.TryGetValue(source, out string? target)
                        || String.IsNullOrWhiteSpace(target))
                        throw new InvalidDataException(
                            $"Weighted skin joint '{source}' is missing from the character BoneMap.");
                    joints.Add(new(source, target, inverseBinds[i]));
                }

                // A weighted map may contain helper nodes, but every mapped
                // source that participates in the skin must resolve above.
                if (joints.Count > asset.BoneMap.Count)
                    throw new InvalidDataException("Weighted character joint map is incomplete.");

                var primitives = new List<CharacterWeightedPrimitive>();
                int totalVertices = 0;
                int totalIndices = 0;
                foreach (JsonElement node in skinnedNodes)
                {
                    int meshIndex = node.GetProperty("mesh").GetInt32();
                    if ((uint)meshIndex >= meshes.Length)
                        throw new InvalidDataException("Weighted character node references an invalid mesh.");

                    foreach (JsonElement primitive in Elements(meshes[meshIndex], "primitives"))
                    {
                        if (Int(primitive, "mode", 4) != 4)
                            throw new InvalidDataException("Weighted character meshes must use triangle primitives.");
                        if (primitive.TryGetProperty("targets", out _))
                            throw new InvalidDataException("Weighted4 does not support morph targets in this slice.");

                        JsonElement attributes = primitive.GetProperty("attributes");
                        int positionAccessor = Int(attributes, "POSITION", -1);
                        int jointsAccessor = Int(attributes, "JOINTS_0", -1);
                        int weightsAccessor = Int(attributes, "WEIGHTS_0", -1);
                        if (positionAccessor < 0 || jointsAccessor < 0 || weightsAccessor < 0)
                            throw new InvalidDataException(
                                "Weighted character primitive needs POSITION, JOINTS_0 and WEIGHTS_0.");

                        NVector3[] positions = ReadVec3(
                            accessors, views, binary, positionAccessor, "POSITION");
                        NVector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalProperty)
                            ? ReadVec3(accessors, views, binary, normalProperty.GetInt32(), "NORMAL")
                            : Array.Empty<NVector3>();
                        NVector2[] texcoords = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvProperty)
                            ? ReadVec2(accessors, views, binary, uvProperty.GetInt32(), "TEXCOORD_0")
                            : Array.Empty<NVector2>();
                        int[][] jointIndices = ReadJoints(
                            accessors, views, binary, jointsAccessor);
                        NVector4[] weights = ReadWeights(
                            accessors, views, binary, weightsAccessor);

                        if (normals.Length != 0 && normals.Length != positions.Length
                            || texcoords.Length != 0 && texcoords.Length != positions.Length
                            || jointIndices.Length != positions.Length
                            || weights.Length != positions.Length)
                            throw new InvalidDataException(
                                "Weighted character attribute counts must match POSITION.");

                        uint[] indices = primitive.TryGetProperty("indices", out JsonElement indexProperty)
                            ? ReadIndices(accessors, views, binary, indexProperty.GetInt32())
                            : Enumerable.Range(0, positions.Length).Select(i => (uint)i).ToArray();
                        if (indices.Length == 0 || indices.Length % 3 != 0
                            || indices.Any(index => index >= positions.Length))
                            throw new InvalidDataException("Weighted character triangle indices are invalid.");

                        totalVertices = checked(totalVertices + positions.Length);
                        totalIndices = checked(totalIndices + indices.Length);
                        if (totalVertices > MaximumVertices || totalIndices > MaximumIndices)
                            throw new InvalidDataException("Weighted character GLB exceeds the geometry budget.");

                        if (normals.Length == 0)
                            normals = GenerateNormals(positions, indices);
                        else
                            NormalizeNormals(normals);

                        string? materialName = MaterialName(primitive, materials);
                        var vertices = new CharacterWeightedVertex[positions.Length];
                        for (int i = 0; i < positions.Length; i++)
                        {
                            if (!Finite(positions[i]))
                                throw new InvalidDataException("Weighted character contains a non-finite position.");
                            NVector2 uv = texcoords.Length == 0 ? NVector2.Zero : texcoords[i];
                            if (!Finite(uv))
                                throw new InvalidDataException("Weighted character contains a non-finite UV.");

                            int[] ji = jointIndices[i];
                            if (ji.Any(value => value < 0 || value >= joints.Count))
                                throw new InvalidDataException(
                                    $"Weighted character vertex {i} references a joint outside its skin.");
                            NVector4 weight = NormalizeWeights(weights[i], i);
                            int packed = PackJoints(ji[0], ji[1], ji[2], ji[3]);
                            vertices[i] = new(positions[i], normals[i], uv, weight, packed);
                        }
                        primitives.Add(new(materialName, vertices, indices));
                    }
                }

                if (primitives.Count == 0)
                    throw new InvalidDataException("Weighted character GLB contains no supported primitives.");
                return new(primitives, joints);
            }
        }

        internal static int PackJoints(int j0, int j1, int j2, int j3)
        {
            if ((uint)j0 >= 32 || (uint)j1 >= 32 || (uint)j2 >= 32 || (uint)j3 >= 32)
                throw new InvalidDataException("Weighted4 joint palette index exceeds 31.");
            // 20 bits total. Every value is exactly representable in IEEE-754
            // float, which lets the legacy 15-float retained vertex stride
            // carry four joint IDs through texcoord.z with no format migration.
            return j0 | (j1 << 5) | (j2 << 10) | (j3 << 15);
        }

        internal static (int J0, int J1, int J2, int J3) UnpackJoints(int packed)
            => (packed & 31, (packed >> 5) & 31, (packed >> 10) & 31, (packed >> 15) & 31);

        private static NVector4 NormalizeWeights(NVector4 weights, int vertex)
        {
            if (!Finite(weights)
                || weights.X < 0 || weights.Y < 0 || weights.Z < 0 || weights.W < 0)
                throw new InvalidDataException($"Weighted character vertex {vertex} has invalid weights.");
            float total = weights.X + weights.Y + weights.Z + weights.W;
            if (!float.IsFinite(total) || total <= 0.000001f)
                throw new InvalidDataException($"Weighted character vertex {vertex} has zero total weight.");
            return weights / total;
        }

        private static OMatrix4[] ReadInverseBinds(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, JsonElement skin, int jointCount)
        {
            var result = Enumerable.Repeat(OMatrix4.Identity, jointCount).ToArray();
            if (!skin.TryGetProperty("inverseBindMatrices", out JsonElement property))
                return result;

            AccessorView accessor = ResolveAccessor(
                accessors, views, binary, property.GetInt32(), "inverseBindMatrices",
                "MAT4", 5126, requireNormalized: false);
            if (accessor.Count != jointCount)
                throw new InvalidDataException("inverseBindMatrices count must match the skin joint count.");

            for (int i = 0; i < jointCount; i++)
            {
                ReadOnlySpan<byte> row = accessor.Row(i);
                float[] m = new float[16];
                for (int n = 0; n < 16; n++)
                {
                    m[n] = F32(row[(n * 4)..]);
                    if (!float.IsFinite(m[n]))
                        throw new InvalidDataException("Weighted character inverse bind matrix is non-finite.");
                }
                result[i] = new OMatrix4(
                    m[0], m[1], m[2], m[3],
                    m[4], m[5], m[6], m[7],
                    m[8], m[9], m[10], m[11],
                    m[12], m[13], m[14], m[15]);
            }
            return result;
        }

        private static int[][] ReadJoints(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, int accessorIndex)
        {
            if ((uint)accessorIndex >= accessors.Length)
                throw new InvalidDataException("Weighted character has an invalid JOINTS_0 accessor.");
            JsonElement accessor = accessors[accessorIndex];
            int component = Int(accessor, "componentType", -1);
            if (component is not (5121 or 5123))
                throw new InvalidDataException("JOINTS_0 must use unsigned byte or unsigned short.");
            AccessorView view = ResolveAccessor(
                accessors, views, binary, accessorIndex, "JOINTS_0", "VEC4",
                component, requireNormalized: false);

            int size = component == 5121 ? 1 : 2;
            var result = new int[view.Count][];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = view.Row(i);
                result[i] = new int[4];
                for (int n = 0; n < 4; n++)
                {
                    result[i][n] = size == 1
                        ? row[n]
                        : BinaryPrimitives.ReadUInt16LittleEndian(row[(n * 2)..]);
                }
            }
            return result;
        }

        private static NVector4[] ReadWeights(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, int accessorIndex)
        {
            if ((uint)accessorIndex >= accessors.Length)
                throw new InvalidDataException("Weighted character has an invalid WEIGHTS_0 accessor.");
            JsonElement accessor = accessors[accessorIndex];
            int component = Int(accessor, "componentType", -1);
            bool normalized = accessor.TryGetProperty("normalized", out JsonElement norm)
                && norm.GetBoolean();
            if (component == 5126 && normalized)
                throw new InvalidDataException("FLOAT WEIGHTS_0 must not be normalized.");
            if (component is not (5126 or 5121 or 5123)
                || component != 5126 && !normalized)
                throw new InvalidDataException(
                    "WEIGHTS_0 must be FLOAT or normalized unsigned byte/short.");

            AccessorView view = ResolveAccessor(
                accessors, views, binary, accessorIndex, "WEIGHTS_0", "VEC4",
                component, requireNormalized: component == 5126 ? false : true);
            var result = new NVector4[view.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = view.Row(i);
                result[i] = new(
                    WeightComponent(row, component, 0),
                    WeightComponent(row, component, 1),
                    WeightComponent(row, component, 2),
                    WeightComponent(row, component, 3));
            }
            return result;
        }

        private static float WeightComponent(ReadOnlySpan<byte> row, int component, int index)
            => component switch
            {
                5126 => F32(row[(index * 4)..]),
                5121 => row[index] / 255f,
                _ => BinaryPrimitives.ReadUInt16LittleEndian(row[(index * 2)..]) / 65535f
            };

        private static NVector3[] ReadVec3(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, int accessorIndex, string semantic)
        {
            AccessorView accessor = ResolveAccessor(
                accessors, views, binary, accessorIndex, semantic, "VEC3", 5126, false);
            var result = new NVector3[accessor.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = accessor.Row(i);
                result[i] = new(F32(row), F32(row[4..]), F32(row[8..]));
            }
            return result;
        }

        private static NVector2[] ReadVec2(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, int accessorIndex, string semantic)
        {
            AccessorView accessor = ResolveAccessor(
                accessors, views, binary, accessorIndex, semantic, "VEC2", 5126, false);
            var result = new NVector2[accessor.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> row = accessor.Row(i);
                result[i] = new(F32(row), F32(row[4..]));
            }
            return result;
        }

        private static uint[] ReadIndices(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, int accessorIndex)
        {
            if ((uint)accessorIndex >= accessors.Length)
                throw new InvalidDataException("Weighted character has an invalid index accessor.");
            JsonElement accessor = accessors[accessorIndex];
            int component = Int(accessor, "componentType", -1);
            if (component is not (5121 or 5123 or 5125))
                throw new InvalidDataException("Weighted character indices must be unsigned.");
            AccessorView view = ResolveAccessor(
                accessors, views, binary, accessorIndex, "indices", "SCALAR", component, false);
            int size = ComponentSize(component);
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

        private static string? MaterialName(JsonElement primitive, JsonElement[] materials)
        {
            if (!primitive.TryGetProperty("material", out JsonElement property))
                return null;
            int index = property.GetInt32();
            if ((uint)index >= materials.Length)
                throw new InvalidDataException("Weighted character references an invalid material.");
            if (!materials[index].TryGetProperty("name", out JsonElement nameProperty)
                || String.IsNullOrWhiteSpace(nameProperty.GetString()))
                throw new InvalidDataException(
                    $"Weighted character material {index} must have a native material name.");
            return nameProperty.GetString();
        }

        private static void ValidateIdentityMeshNode(JsonElement node)
        {
            const float epsilon = 0.00001f;
            if (node.TryGetProperty("matrix", out JsonElement matrix))
            {
                float[] expected = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
                float[] actual = matrix.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (actual.Length != 16
                    || actual.Where((value, i) => !float.IsFinite(value)
                        || MathF.Abs(value - expected[i]) > epsilon).Any())
                    throw new InvalidDataException(
                        "Weighted character mesh nodes must have an identity transform.");
            }

            if (node.TryGetProperty("translation", out JsonElement translation)
                && !VectorNear(translation, new[] { 0f, 0f, 0f }, epsilon)
                || node.TryGetProperty("rotation", out JsonElement rotation)
                && !VectorNear(rotation, new[] { 0f, 0f, 0f, 1f }, epsilon)
                || node.TryGetProperty("scale", out JsonElement scale)
                && !VectorNear(scale, new[] { 1f, 1f, 1f }, epsilon))
                throw new InvalidDataException(
                    "Weighted character mesh nodes must have identity TRS transforms.");
        }

        private static bool VectorNear(JsonElement element, float[] expected, float epsilon)
        {
            if (element.ValueKind != JsonValueKind.Array
                || element.GetArrayLength() != expected.Length) return false;
            int i = 0;
            foreach (JsonElement value in element.EnumerateArray())
            {
                float actual = value.GetSingle();
                if (!float.IsFinite(actual) || MathF.Abs(actual - expected[i++]) > epsilon)
                    return false;
            }
            return true;
        }

        private static AccessorView ResolveAccessor(JsonElement[] accessors, JsonElement[] views,
            byte[] binary, int accessorIndex, string semantic, string expectedType,
            int expectedComponent, bool requireNormalized)
        {
            if ((uint)accessorIndex >= accessors.Length)
                throw new InvalidDataException($"Weighted character has an invalid {semantic} accessor.");
            JsonElement accessor = accessors[accessorIndex];
            if (accessor.TryGetProperty("sparse", out _))
                throw new InvalidDataException($"Sparse {semantic} accessors are not supported.");
            if (!String.Equals(accessor.GetProperty("type").GetString(), expectedType,
                    StringComparison.Ordinal)
                || Int(accessor, "componentType", -1) != expectedComponent)
                throw new InvalidDataException($"Weighted character {semantic} has an unsupported format.");
            bool normalized = accessor.TryGetProperty("normalized", out JsonElement norm)
                && norm.GetBoolean();
            if (normalized != requireNormalized)
                throw new InvalidDataException(
                    $"Weighted character {semantic} normalized flag is invalid.");

            int count = Int(accessor, "count", -1);
            int budget = semantic == "indices" ? MaximumIndices : MaximumVertices;
            if (count <= 0 || count > budget)
                throw new InvalidDataException($"Weighted character {semantic} exceeds its element budget.");
            int viewIndex = Int(accessor, "bufferView", -1);
            if ((uint)viewIndex >= views.Length)
                throw new InvalidDataException($"Weighted character {semantic} has no buffer view.");

            JsonElement view = views[viewIndex];
            if (Int(view, "buffer", -1) != 0)
                throw new InvalidDataException("Weighted character bufferView references a non-embedded buffer.");
            int viewOffset = Int(view, "byteOffset", 0);
            int viewLength = Int(view, "byteLength", -1);
            int accessorOffset = Int(accessor, "byteOffset", 0);
            int componentSize = ComponentSize(expectedComponent);
            int components = expectedType switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                "MAT4" => 16,
                _ => 0
            };
            int elementSize = checked(componentSize * components);
            int stride = Int(view, "byteStride", elementSize);
            if (componentSize == 0 || components == 0
                || viewOffset < 0 || viewLength < 0 || accessorOffset < 0
                || stride < elementSize || stride > 252
                || viewOffset + (long)viewLength > binary.Length
                || accessorOffset + (long)(count - 1) * stride + elementSize > viewLength)
                throw new InvalidDataException(
                    $"Weighted character {semantic} exceeds its bufferView.");
            return new(binary, checked(viewOffset + accessorOffset), count, stride, elementSize);
        }

        private static int ComponentSize(int component) => component switch
        {
            5121 => 1,
            5123 => 2,
            5125 or 5126 => 4,
            _ => 0
        };

        private static (JsonDocument Document, byte[] Binary) ReadGlb(string path)
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (U32(header) != 0x46546C67 || U32(header[4..]) != 2
                || U32(header[8..]) != stream.Length)
                throw new InvalidDataException("Invalid weighted character GLB header.");

            JsonDocument? document = null;
            byte[]? binary = null;
            Span<byte> chunkHeader = stackalloc byte[8];
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 8)
                    throw new InvalidDataException("Truncated weighted character GLB chunk.");
                stream.ReadExactly(chunkHeader);
                uint length = U32(chunkHeader);
                uint type = U32(chunkHeader[4..]);
                if (length > stream.Length - stream.Position
                    || length > CharacterModelPack.MaximumModelBytes)
                    throw new InvalidDataException("Weighted character GLB chunk exceeds its bounds.");
                byte[] bytes = new byte[(int)length];
                stream.ReadExactly(bytes);
                if (type == 0x4E4F534A)
                {
                    if (document != null || length == 0
                        || length > CharacterModelPack.MaximumGlbJsonBytes)
                        throw new InvalidDataException("Weighted character GLB JSON chunk is invalid.");
                    document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
                }
                else if (type == 0x004E4942)
                {
                    if (binary != null)
                        throw new InvalidDataException("Weighted character GLB has multiple BIN chunks.");
                    binary = bytes;
                }
            }
            if (document == null || binary == null)
            {
                document?.Dispose();
                throw new InvalidDataException("Weighted character GLB requires JSON and BIN chunks.");
            }
            return (document, binary);
        }

        private static void NormalizeNormals(NVector3[] normals)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                float length = normals[i].Length();
                if (!float.IsFinite(length) || length < 0.000001f)
                    throw new InvalidDataException("Weighted character contains an invalid normal.");
                normals[i] /= length;
            }
        }

        private static NVector3[] GenerateNormals(NVector3[] positions, uint[] indices)
        {
            var result = new NVector3[positions.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = (int)indices[i], b = (int)indices[i + 1], c = (int)indices[i + 2];
                NVector3 normal = NVector3.Cross(
                    positions[b] - positions[a], positions[c] - positions[a]);
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
                result[i] = length < 0.000001f ? NVector3.UnitY : result[i] / length;
            }
            return result;
        }

        private static JsonElement[] Elements(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();

        private static int Int(JsonElement element, string name, int fallback = 0)
            => element.TryGetProperty(name, out JsonElement value)
                && value.TryGetInt32(out int result) ? result : fallback;

        private static uint U32(ReadOnlySpan<byte> bytes)
            => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        private static float F32(ReadOnlySpan<byte> bytes)
            => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));

        private static bool Finite(NVector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);
        private static bool Finite(NVector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        private static bool Finite(NVector4 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z) && float.IsFinite(value.W);

        private readonly struct AccessorView
        {
            private readonly byte[] _bytes;
            private readonly int _offset;
            private readonly int _stride;
            private readonly int _elementSize;
            public int Count { get; }

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
