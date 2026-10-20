using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Deterministic source-space skin for the existing Weighted4 preview loader.
/// Full source joints and positive influences are retained. The deliberately
/// procedural material colors identify source groups; they do not emulate GX.
/// </summary>
public static class RetroCharacterSourceGlbWriter
{
    public const int Version = 1;
    public const string MaterialPolicy = "diagnostic-source-material-colors-v1";
    public const int MaximumBytes = 64 * 1024 * 1024;
    public const int MaximumCorners = 500_000;
    public const int MaximumMaterials = 256;

    public static byte[] Write(Cmdl model, RetroNativeSkeleton skeleton, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(skeleton);
        cancellation.ThrowIfCancellationRequested();
        if (skeleton.Bones == null) throw new InvalidDataException("Source skeleton dictionary is absent.");
        if (model.Positions == null || model.Normals == null || model.Uv0 == null || model.Triangles == null
            || model.TriangleMaterial == null || model.Materials == null || model.Skin == null
            || model.Positions.Length > 1_500_000 || model.Triangles.Length > MaximumCorners
            || model.TriangleMaterial.Length > MaximumCorners / 3 || model.Materials.Length > MaximumMaterials
            || model.Skin.Count > 500_000 || skeleton.Bones.Count is < 1 or > 1023
            || ((long)model.Positions.Length + model.Normals.Length + model.Uv0.Length + (model.ShortUvs?.Length ?? 0)) * 4 > MaximumBytes)
            throw new InvalidDataException("Source GLB capture exceeds the input budget.");
        // Detach before preflight/encoding. No caller-owned array, material,
        // influence list or dictionary is consulted after this capture.
        var skin = new List<(uint Bone, float Weight)[]>(model.Skin.Count);
        long influenceWork = 0;
        foreach (var group in model.Skin)
        {
            cancellation.ThrowIfCancellationRequested();
            if (group == null || group.Length > 1024 || (influenceWork += group.Length) > 4_000_000)
                throw new InvalidDataException("Source influence capture exceeds budget.");
            var positive = new List<(uint Bone, float Weight)>(4);
            foreach (var value in group)
            {
                if (!float.IsFinite(value.Weight) || value.Weight < 0) throw new InvalidDataException("Invalid source weight.");
                if (value.Weight == 0) continue;
                if (positive.Count == 4) throw new InvalidDataException("Source Weighted4 cannot prune positive influences.");
                positive.Add(value);
            }
            if (positive.Count == 0 || MathF.Abs(positive.Sum(v => v.Weight) - 1) > .00001f)
                throw new InvalidDataException("Source weights require an unchanged unit sum.");
            skin.Add(positive.ToArray());
        }
        var captured = new Cmdl { Positions = (float[])model.Positions.Clone(), Normals = (float[])model.Normals.Clone(),
            Uv0 = (float[])model.Uv0.Clone(), ShortUvs = (float[]?)model.ShortUvs?.Clone(),
            Triangles = (Cmdl.Corner[])model.Triangles.Clone(), TriangleMaterial = (int[])model.TriangleMaterial.Clone(), Skin = skin,
            Materials = model.Materials.Select(m => m == null ? throw new InvalidDataException("Missing source material.") : new Cmdl.Material { Flags = m.Flags }).ToArray() };
        if (skeleton.Bones.Any(p => p.Value == null || p.Key != p.Value.Id))
            throw new InvalidDataException("Source skeleton keys must match their nonnull bone identities.");
        var capturedRig = new RetroNativeSkeleton(skeleton.Bones.ToDictionary(p => p.Key, p => p.Value with { }));
        return WriteCaptured(captured, capturedRig, cancellation);
    }

    private static byte[] WriteCaptured(Cmdl model, RetroNativeSkeleton skeleton, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(skeleton);
        cancellation.ThrowIfCancellationRequested();
        // Validate the entire detached input before allocating output buffers.
        if (model.Positions == null || model.Positions.Length % 3 != 0 || model.PositionCount is < 3 or > 2_000_000
            || model.Normals == null || model.Normals.Length % 3 != 0 || model.Uv0 == null || model.Uv0.Length % 2 != 0
            || model.ShortUvs?.Length % 2 == 1 || model.Materials == null || model.Materials.Length is < 1 or > MaximumMaterials
            || model.TriangleMaterial == null || model.TriangleCount < 1 || model.Triangles == null
            || model.Triangles.Length != 3L * model.TriangleCount || model.Triangles.Length > MaximumCorners
            || model.Skin?.Count != model.PositionCount || skeleton.Bones.Count is < 1 or > 1023)
            throw new InvalidDataException("Source GLB input exceeds the complete geometry/rig budget.");
        var bones = skeleton.Bones.Values.OrderBy(b => b.Id).ToArray();
        var jointIndex = bones.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
        if (bones.Any(b => !skeleton.Bones.TryGetValue(b.Id, out var value) || value != b)
            || bones.Count(b => !jointIndex.ContainsKey(b.Parent)) != 1
            || bones.Single(b => !jointIndex.ContainsKey(b.Parent)).Parent != 97)
            throw new InvalidDataException("Source GLB requires the explicit single root97 skeleton contract.");
        foreach (var b in bones)
        {
            Bounded(b.X); Bounded(b.Y); Bounded(b.Z);
            string name = BoneName(b);
            if (name.Length > 128 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid stable source bone name.");
            var seen = new HashSet<uint>();
            for (var n = b; ; n = skeleton.Bones[n.Parent])
            {
                if (!seen.Add(n.Id)) throw new InvalidDataException("Source skeleton cycle.");
                if (!jointIndex.ContainsKey(n.Parent)) break;
            }
        }
        foreach (float v in model.Positions) Bounded(v);
        foreach (var group in model.Skin!)
        {
            if (group == null || group.Length > 1024) throw new InvalidDataException("Invalid source influence group.");
            float sum = 0; int count = 0; var used = new HashSet<uint>();
            foreach (var (bone, weight) in group)
            {
                if (!float.IsFinite(weight) || weight < 0) throw new InvalidDataException("Invalid source weight.");
                if (weight == 0) continue;
                if (!jointIndex.ContainsKey(bone) || !used.Add(bone) || ++count > 4)
                    throw new InvalidDataException("Source Weighted4 cannot prune, remap or duplicate positive influences.");
                sum += weight;
            }
            if (count == 0 || MathF.Abs(sum - 1) > .00001f) throw new InvalidDataException("Source weights require an unchanged unit sum.");
        }
        var faces = new List<int>[model.Materials.Length];
        for (int m = 0; m < faces.Length; m++)
        {
            if (model.Materials[m] == null) throw new InvalidDataException("Missing source material.");
            faces[m] = new();
        }
        for (int face = 0; face < model.TriangleCount; face++)
        {
            if ((face & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            int material = model.TriangleMaterial[face];
            if ((uint)material >= faces.Length) throw new InvalidDataException("Invalid source material ordinal.");
            faces[material].Add(face);
            var uv = (model.Materials[material].Flags & Cmdl.ShortUv) != 0 ? model.ShortUvs : model.Uv0;
            for (int c = 0; c < 3; c++)
            {
                var corner = model.Triangles[face * 3 + c];
                if ((uint)corner.P >= model.PositionCount || corner.N < 0 || 3L * corner.N + 2 >= model.Normals.Length
                    || corner.T0 < -1 || corner.T0 >= 0 && (uv == null || 2L * corner.T0 + 1 >= uv.Length))
                    throw new InvalidDataException("Source corner attributes are absent or out of bounds.");
                float nx = model.Normals[corner.N * 3], ny = model.Normals[corner.N * 3 + 1], nz = model.Normals[corner.N * 3 + 2];
                Bounded(nx); Bounded(ny); Bounded(nz);
                if (nx * nx + ny * ny + nz * nz < .00000001f) throw new InvalidDataException("Degenerate source normal.");
                if (corner.T0 >= 0) { Bounded(uv![corner.T0 * 2]); Bounded(uv[corner.T0 * 2 + 1]); }
            }
        }
        // 60 bytes per expanded corner, 64 per inverse bind. JSON/source IDs
        // have a conservative independent bound, checked again after encoding.
        long maximum = model.Triangles.Length * 72L + bones.Length * 2048L + faces.Length * 8192L + 65536;
        if (maximum > MaximumBytes) throw new InvalidDataException("Source GLB encoded size exceeds its component budget.");
        using var binary = new MemoryStream(checked(model.Triangles.Length * 60 + bones.Length * 64 + faces.Length * 128));
        using var writer = new BinaryWriter(binary);
        var views = new JsonArray(); var accessors = new JsonArray(); var primitives = new JsonArray();
        var materials = new JsonArray(); var images = new JsonArray(); var textures = new JsonArray();
        void Align() { while (binary.Position % 4 != 0) writer.Write((byte)0); }
        int View(Action write)
        {
            Align(); int offset = checked((int)binary.Position); write();
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = checked((int)binary.Position - offset) });
            return views.Count - 1;
        }
        int Access(string type, int component, int count, Action write)
        {
            int view = View(write);
            accessors.Add(new JsonObject { ["bufferView"] = view, ["componentType"] = component, ["count"] = count, ["type"] = type });
            return accessors.Count - 1;
        }
        for (int m = 0; m < faces.Length; m++)
        {
            cancellation.ThrowIfCancellationRequested();
            int material = m;
            int image = View(() => writer.Write(DiagnosticPng(material)));
            images.Add(new JsonObject { ["bufferView"] = image, ["mimeType"] = "image/png" });
            textures.Add(new JsonObject { ["source"] = m, ["sampler"] = 0 });
            materials.Add(new JsonObject { ["name"] = "source_material_" + m, ["doubleSided"] = true,
                ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = m }, ["metallicFactor"] = 0, ["roughnessFactor"] = 1 },
                ["extras"] = new JsonObject { ["sourceMaterialOrdinal"] = m, ["materialPolicy"] = MaterialPolicy } });
            if (faces[m].Count == 0) continue;
            int count = checked(faces[m].Count * 3);
            void Corners(Action<Cmdl.Corner> write)
            { foreach (int face in faces[material]) { if ((face & 1023) == 0) cancellation.ThrowIfCancellationRequested(); for (int c = 0; c < 3; c++) write(model.Triangles[face * 3 + c]); } }
            int positions = Access("VEC3", 5126, count, () => Corners(c => { writer.Write(-model.Positions[c.P * 3]); writer.Write(model.Positions[c.P * 3 + 2]); writer.Write(model.Positions[c.P * 3 + 1]); }));
            // glTF POSITION accessors require bounds. Compute in the same
            // converted source basis without changing corner values or order.
            float[] minimum = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            float[] maximumPosition = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            Corners(c =>
            {
                void Bound(int axis, float value) { minimum[axis] = MathF.Min(minimum[axis], value); maximumPosition[axis] = MathF.Max(maximumPosition[axis], value); }
                Bound(0, -model.Positions[c.P * 3]); Bound(1, model.Positions[c.P * 3 + 2]); Bound(2, model.Positions[c.P * 3 + 1]);
            });
            ((JsonObject)accessors[positions]!)["min"] = new JsonArray(minimum.Select(v => JsonValue.Create(v)).ToArray());
            ((JsonObject)accessors[positions]!)["max"] = new JsonArray(maximumPosition.Select(v => JsonValue.Create(v)).ToArray());
            int normals = Access("VEC3", 5126, count, () => Corners(c => { writer.Write(-model.Normals[c.N * 3]); writer.Write(model.Normals[c.N * 3 + 2]); writer.Write(model.Normals[c.N * 3 + 1]); }));
            var sourceUv = (model.Materials[m].Flags & Cmdl.ShortUv) != 0 ? model.ShortUvs : model.Uv0;
            int uv = Access("VEC2", 5126, count, () => Corners(c => { writer.Write(c.T0 < 0 ? 0 : sourceUv![c.T0 * 2]); writer.Write(c.T0 < 0 ? 0 : sourceUv![c.T0 * 2 + 1]); }));
            int joints = Access("VEC4", 5123, count, () => Corners(c => { int n = 0; foreach (var influence in model.Skin![c.P]) if (influence.Weight > 0) { writer.Write(checked((ushort)jointIndex[influence.Bone])); n++; } for (; n < 4; n++) writer.Write((ushort)0); }));
            int weights = Access("VEC4", 5126, count, () => Corners(c => { int n = 0; foreach (var influence in model.Skin![c.P]) if (influence.Weight > 0) { writer.Write(influence.Weight); n++; } for (; n < 4; n++) writer.Write(0f); }));
            int indices = Access("SCALAR", 5125, count, () => { for (uint i = 0; i < count; i++) writer.Write(i); });
            primitives.Add(new JsonObject { ["material"] = m, ["attributes"] = new JsonObject { ["POSITION"] = positions, ["NORMAL"] = normals, ["TEXCOORD_0"] = uv, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights }, ["indices"] = indices,
                ["extras"] = new JsonObject { ["sourceMaterialOrdinal"] = m, ["sourceTriangleIds"] = new JsonArray(faces[m].Select(i => JsonValue.Create(i)).ToArray()) } });
        }
        var nodes = new JsonArray(); var sceneRoots = new JsonArray();
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i]; float x = b.X, y = b.Y, z = b.Z;
            if (skeleton.Bones.TryGetValue(b.Parent, out var parent)) { x -= parent.X; y -= parent.Y; z -= parent.Z; }
            else sceneRoots.Add(i);
            Bounded(x); Bounded(y); Bounded(z);
            nodes.Add(new JsonObject { ["name"] = BoneName(b), ["translation"] = new JsonArray(-x, z, y),
                ["children"] = new JsonArray(bones.Where(c => c.Parent == b.Id).Select(c => JsonValue.Create(jointIndex[c.Id])).ToArray()) });
        }
        sceneRoots.Add(bones.Length);
        nodes.Add(new JsonObject { ["name"] = "source_identity_mesh", ["mesh"] = 0, ["skin"] = 0 });
        int inverse = Access("MAT4", 5126, bones.Length, () =>
        {
            foreach (var b in bones)
            { foreach (float value in new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, b.X, -b.Z, -b.Y, 1 }) writer.Write(value); }
        });
        Align();
        var document = new JsonObject { ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "Project Prime source GLB v1" },
            ["scene"] = 0, ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = sceneRoots }),
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = binary.Length }), ["bufferViews"] = views, ["accessors"] = accessors,
            ["nodes"] = nodes, ["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(Enumerable.Range(0, bones.Length).Select(i => JsonValue.Create(i)).ToArray()), ["inverseBindMatrices"] = inverse }),
            ["materials"] = materials, ["images"] = images, ["textures"] = textures, ["samplers"] = new JsonArray(new JsonObject { ["wrapS"] = 33071, ["wrapT"] = 33071 }),
            ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = primitives }),
            ["extras"] = new JsonObject { ["sourceGlbWriterVersion"] = Version, ["sourceBasis"] = "(-x,z,y)", ["materialPolicy"] = MaterialPolicy } };
        byte[] json = Encoding.UTF8.GetBytes(document.ToJsonString()); int jl = checked((json.Length + 3) & ~3);
        long total = 28L + jl + binary.Length;
        if (json.Length > 4 * 1024 * 1024 || total > MaximumBytes) throw new InvalidDataException("Encoded source GLB exceeds bounded JSON/component limits.");
        cancellation.ThrowIfCancellationRequested();
        using var output = new MemoryStream((int)total); using var glb = new BinaryWriter(output);
        glb.Write(0x46546c67u); glb.Write(2u); glb.Write((uint)total); glb.Write((uint)jl); glb.Write(0x4e4f534au); glb.Write(json);
        for (int i = json.Length; i < jl; i++) glb.Write((byte)32);
        glb.Write((uint)binary.Length); glb.Write(0x004e4942u); binary.Position = 0; binary.CopyTo(output);
        cancellation.ThrowIfCancellationRequested(); return output.ToArray();
    }

    private static string BoneName(RetroNativeBone b) => "source_bone_" + b.Id + "_" + b.Name;
    private static void Bounded(float value)
    { if (!float.IsFinite(value) || MathF.Abs(value) > 100000) throw new InvalidDataException("Source attribute is nonfinite or oversized."); }

    // One authored opaque pixel per group. Managed encoding keeps the importer
    // independent of a graphics context, native image library and source TXTRs.
    private static byte[] DiagnosticPng(int ordinal)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
            byte[] type = Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in type.Concat(data)) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
        }
        Chunk("IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0 });
        byte[] row = { 0, (byte)(64 + ordinal * 67 % 160), (byte)(64 + ordinal * 101 % 160), (byte)(64 + ordinal * 139 % 160), 255 };
        // Fixed uncompressed zlib block, independent of platform encoder versions.
        byte[] compressed = new byte[16];
        new byte[] { 0x78, 0x01, 0x01, 5, 0, 0xfa, 0xff }.CopyTo(compressed, 0); row.CopyTo(compressed, 7);
        uint a = 1, b = 0; foreach (byte value in row) { a = (a + value) % 65521; b = (b + a) % 65521; }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(compressed.AsSpan(12), b << 16 | a);
        Chunk("IDAT", compressed); Chunk("IEND", Array.Empty<byte>()); return output.ToArray();
    }
}
