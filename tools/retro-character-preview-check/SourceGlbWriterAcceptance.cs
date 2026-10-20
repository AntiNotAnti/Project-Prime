using System.Buffers.Binary;
using System.Collections;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;
using M = OpenTK.Mathematics.Matrix4;

internal static class SourceGlbWriterAcceptance
{
    private static int _checks;
    internal static void Run()
    {
        var (model, skeleton) = Fixture(); string before = InputHash(model, skeleton);
        byte[] bytes = RetroCharacterSourceGlbWriter.Write(model, skeleton);
        Check(bytes.SequenceEqual(RetroCharacterSourceGlbWriter.Write(model, skeleton)), "writer is byte deterministic");
        var reversed = new RetroNativeSkeleton(skeleton.Bones.Reverse().ToDictionary(p => p.Key, p => p.Value));
        Check(bytes.SequenceEqual(RetroCharacterSourceGlbWriter.Write(model, reversed)), "dictionary insertion order cannot change canonical joints or bytes");
        Check(InputHash(model, skeleton) == before, "writer leaves caller geometry, flags, skin and skeleton untouched");
        Inspect(bytes, model, skeleton);
        Loader(bytes, model.Triangles.Length);

        // Deterministic capture seam: dictionary enumeration occurs after the
        // model snapshot. Mutating every model input there cannot retarget it.
        var (mutable, rig) = Fixture();
        var detached = new CallbackDictionary(rig.Bones, () =>
        { mutable.Positions[0] = 900; mutable.Normals[0] = 1; mutable.Uv0[0] = 99; mutable.ShortUvs![0] = 99; mutable.Materials[0].Flags = Cmdl.ShortUv; mutable.Skin![0][0] = (0, .9f); mutable.Triangles[0].P = 5; mutable.TriangleMaterial[0] = 2; });
        Check(bytes.SequenceEqual(RetroCharacterSourceGlbWriter.Write(mutable, new(detached))), "captured geometry and influences survive later caller mutations");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); Throws<OperationCanceledException>(() => RetroCharacterSourceGlbWriter.Write(model, skeleton, cancellation.Token), "pre-cancel");
        }
        using (var cancellation = new CancellationTokenSource())
            Throws<OperationCanceledException>(() => RetroCharacterSourceGlbWriter.Write(model,
                new(new CallbackDictionary(skeleton.Bones, cancellation.Cancel)), cancellation.Token), "cancellation during detached rig capture");
        Failures();
        Console.WriteLine($"PASS: source GLB exact corner/rig/skin preservation and refusal ({_checks} checks)");
    }

    private static void Inspect(byte[] bytes, Cmdl model, RetroNativeSkeleton skeleton)
    {
        Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) == 2
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) == bytes.Length, "complete GLB v2 header");
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        Check(jsonLength % 4 == 0 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) == 0x4e4f534a, "aligned JSON chunk");
        using var json = JsonDocument.Parse(bytes.AsMemory(20, jsonLength)); var root = json.RootElement;
        int binary = 28 + jsonLength;
        Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24 + jsonLength)) == 0x004e4942
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20 + jsonLength)) == bytes.Length - binary,
            "exact aligned BIN extent without unaccounted tail");
        Check(root.GetProperty("extras").GetProperty("sourceBasis").GetString() == "(-x,z,y)"
            && root.GetProperty("extras").GetProperty("materialPolicy").GetString() == "diagnostic-source-material-colors-v1",
            "source basis and diagnostic material policy remain explicit");
        var bones = skeleton.Bones.Values.OrderBy(b => b.Id).ToArray(); var byId = bones.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var nodes = root.GetProperty("nodes"); var skin = root.GetProperty("skins")[0];
        Check(nodes.GetArrayLength() == 40 && skin.GetProperty("joints").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(Enumerable.Range(0, 39)), "all39 joints precede one separate identity mesh");
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i]; float x = b.X, y = b.Y, z = b.Z;
            if (skeleton.Bones.TryGetValue(b.Parent, out var parent)) { x -= parent.X; y -= parent.Y; z -= parent.Z; }
            Check(nodes[i].GetProperty("name").GetString() == "source_bone_" + b.Id + "_" + b.Name
                && nodes[i].GetProperty("translation").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(new[] { -x, z, y })
                && nodes[i].GetProperty("children").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(bones.Where(c => c.Parent == b.Id).Select(c => byId[c.Id])),
                "exact whole hierarchy and local bind including forward parent references");
        }
        Check(nodes[39].GetProperty("mesh").GetInt32() == 0 && nodes[39].GetProperty("skin").GetInt32() == 0 && !nodes[39].TryGetProperty("translation", out _), "mesh remains an independent identity root");
        var primitives = root.GetProperty("meshes")[0].GetProperty("primitives");
        Check(primitives.GetArrayLength() == 2 && root.GetProperty("materials").GetArrayLength() == 3, "source groups include unused material without invented primitive");
        var allFaces = new List<int>();
        foreach (var primitive in primitives.EnumerateArray())
        {
            int material = primitive.GetProperty("material").GetInt32(); var attributes = primitive.GetProperty("attributes");
            int[] faces = primitive.GetProperty("extras").GetProperty("sourceTriangleIds").EnumerateArray().Select(v => v.GetInt32()).ToArray(); allFaces.AddRange(faces);
            Check(faces.SequenceEqual(Enumerable.Range(0, model.TriangleCount).Where(f => model.TriangleMaterial[f] == material)), "original triangle IDs survive material grouping in source order");
            float[] positions = Floats(attributes.GetProperty("POSITION").GetInt32()), normals = Floats(attributes.GetProperty("NORMAL").GetInt32()), uv = Floats(attributes.GetProperty("TEXCOORD_0").GetInt32()), weights = Floats(attributes.GetProperty("WEIGHTS_0").GetInt32());
            var positionAccessor = root.GetProperty("accessors")[attributes.GetProperty("POSITION").GetInt32()];
            float[] minimum = positionAccessor.GetProperty("min").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            float[] maximum = positionAccessor.GetProperty("max").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Check(positionAccessor.GetProperty("type").GetString() == "VEC3" && positionAccessor.GetProperty("componentType").GetInt32() == 5126
                && positionAccessor.GetProperty("count").GetInt32() == faces.Length * 3
                && minimum.SequenceEqual(material == 0 ? new[] { -39f, -38.25f, 1f } : new[] { -3f, -2.25f, 1f })
                && maximum.SequenceEqual(new[] { -1f, -.25f, 3f }), "required POSITION bounds use each primitive's converted referenced corners, not whole-model bounds");
            for (int axis = 0; axis < 3; axis++)
                Check(minimum[axis] == Enumerable.Range(0, positions.Length / 3).Min(c => positions[c * 3 + axis])
                    && maximum[axis] == Enumerable.Range(0, positions.Length / 3).Max(c => positions[c * 3 + axis]),
                    "declared POSITION extrema exactly bound actual emitted corner bytes");
            ushort[] joints = UShorts(attributes.GetProperty("JOINTS_0").GetInt32()); uint[] indices = UInts(primitive.GetProperty("indices").GetInt32());
            Check(indices.SequenceEqual(Enumerable.Range(0, faces.Length * 3).Select(i => (uint)i)), "expanded corner order preserves winding and seam identity");
            for (int corner = 0; corner < faces.Length * 3; corner++)
            {
                var c = model.Triangles[faces[corner / 3] * 3 + corner % 3];
                Check(positions.AsSpan(corner * 3, 3).SequenceEqual(new[] { -model.Positions[c.P * 3], model.Positions[c.P * 3 + 2], model.Positions[c.P * 3 + 1] })
                    && normals.AsSpan(corner * 3, 3).SequenceEqual(new[] { -model.Normals[c.N * 3], model.Normals[c.N * 3 + 2], model.Normals[c.N * 3 + 1] }), "every corner preserves exact transformed position and normal");
                var sourceUv = (model.Materials[material].Flags & Cmdl.ShortUv) == 0 ? model.Uv0 : model.ShortUvs!;
                Check(uv.AsSpan(corner * 2, 2).SequenceEqual(c.T0 < 0 ? new float[2] : sourceUv.AsSpan(c.T0 * 2, 2).ToArray()), "float/short UV pool and absent UV zero are exact per material");
                var influences = model.Skin![c.P].Where(p => p.Weight > 0).ToArray();
                for (int component = 0; component < 4; component++)
                    Check(joints[corner * 4 + component] == (component < influences.Length ? byId[influences[component].Bone] : 0)
                        && weights[corner * 4 + component] == (component < influences.Length ? influences[component].Weight : 0), "positive influence order and fractional weights survive without normalization/pruning");
            }
        }
        Check(allFaces.Order().SequenceEqual(Enumerable.Range(0, model.TriangleCount)), "every source triangle occurs exactly once");
        float[] inverse = Floats(skin.GetProperty("inverseBindMatrices").GetInt32());
        for (int i = 0; i < bones.Length; i++)
        { var b = bones[i]; Check(inverse.AsSpan(i * 16, 16).SequenceEqual(new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, b.X, -b.Z, -b.Y, 1 }), "full inverse bind is exact source global translation in converted basis"); }
        byte[][] colors = { new byte[] { 0, 64, 64, 64, 255 }, new byte[] { 0, 131, 165, 203, 255 }, new byte[] { 0, 198, 106, 182, 255 } };
        for (int i = 0; i < 3; i++)
        {
            var view = root.GetProperty("bufferViews")[root.GetProperty("images")[i].GetProperty("bufferView").GetInt32()]; int at = binary + view.GetProperty("byteOffset").GetInt32();
            byte[] png = bytes.AsSpan(at, view.GetProperty("byteLength").GetInt32()).ToArray();
            Check(PngPixel(png).SequenceEqual(colors[i]), "embedded diagnostic color is authored opaque1x1 RGBA");
        }
        ReadOnlySpan<byte> Access(int id)
        { var a = root.GetProperty("accessors")[id]; var v = root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()]; return bytes.AsSpan(binary + v.GetProperty("byteOffset").GetInt32(), v.GetProperty("byteLength").GetInt32()); }
        float[] Floats(int id) { var raw = Access(id); var values = new float[raw.Length / 4]; for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(raw.Slice(i * 4, 4)); return values; }
        ushort[] UShorts(int id) { var raw = Access(id); var values = new ushort[raw.Length / 2]; for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2, 2)); return values; }
        uint[] UInts(int id) { var raw = Access(id); var values = new uint[raw.Length / 4]; for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(i * 4, 4)); return values; }
    }

    private static void Loader(byte[] glb, int corners)
    {
        if (!CharacterSourceRootWitness.QualifiedForCurrentProcess)
        { Console.WriteLine("Source GLB loader partition check skipped: private source lease requires macOS arm64."); return; }
        string root = Path.Combine(Path.GetTempPath(), "prime-own-source-glb-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "body.glb"), glb); string hash = Sha(glb), witness = Sha(new byte[] { 1, 2, 3 });
            var receipt = new JsonObject { ["format"] = 1, ["sourceCharacter"] = "darksamus-echoes", ["nativeHunter"] = "Samus", ["characterId"] = "darksamus", ["skinSource"] = "MP2DarkSamus", ["presentationPart"] = "biped", ["visualOnly"] = true,
                ["sourceGlbSha256"] = hash, ["sourceNativeKitSha256"] = witness, ["sourceMappingSha256"] = witness, ["sourceSkeletonSha256"] = witness, ["sourceWeightsSha256"] = witness };
            foreach (string flag in new[] { "approvedForGameplay", "multiplayerEnabled", "playerSelectionEnabled", "firstPersonVerified", "alternateFormVerified", "sourceMotionRetargetVerified", "devicePerformanceVerified" }) receipt[flag] = false;
            byte[] proof = System.Text.Encoding.UTF8.GetBytes(receipt.ToJsonString()); File.WriteAllBytes(Path.Combine(root, "receipt.json"), proof);
            var entry = new JsonObject { ["hunter"] = "Samus", ["part"] = "biped", ["lod"] = 0, ["model"] = "body.glb", ["skinning"] = "weighted4", ["poseSource"] = "sourceRigPreview", ["characterId"] = "darksamus", ["skinSource"] = "MP2DarkSamus", ["provenance"] = "receipt.json", ["provenanceSha256"] = Sha(proof), ["boneMap"] = new JsonObject() };
            File.WriteAllText(Path.Combine(root, "characters.json"), new JsonObject { ["format"] = 1, ["id"] = "own-writer-39", ["models"] = new JsonArray(entry) }.ToJsonString());
            var pack = CharacterModelPack.Load(root);
            Check(pack.TryResolvePrivate(Hunter.Samus, new("darksamus", "MP2DarkSamus"), CharacterModelPart.Biped, 0, out var asset)
                && pack.TryCreateSourcePreviewLease(asset, out _), "actual owner validates writer GLB with private synthetic receipt");
            Check(pack.TryCreateSourcePreviewLease(asset, out var lease), "actual source lease issued");
            var loaded = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease);
            Check(loaded.Joints.Count == 39 && loaded.SourceRig!.Nodes.Count == 40 && loaded.IndexCount == corners
                && loaded.Primitives.Count >= 3 && loaded.Primitives.All(p => p.JointPalette is { Length: > 0 and <= 32 }), "actual loader partitions >32 source joints without dropping corners or full hierarchy");
            Check(loaded.Primitives.SelectMany(p => p.JointPalette!).Distinct().Count() == 39, "actual submitted palettes collectively retain every weighted source joint");
            Check(loaded.Primitives.All(p => p.SourcePrimitiveOrdinal is 0 or 1
                && p.SourceMaterialOrdinal == p.SourcePrimitiveOrdinal && p.SourceTriangleIds?.Length == p.Indices.Length / 3),
                "each partition retains exact source primitive/material identity and triangle-ID extent");
            Check(loaded.Primitives.Where(p => p.SourcePrimitiveOrdinal == 0).SelectMany(p => p.SourceTriangleIds!).SequenceEqual(Enumerable.Range(0, 13))
                && loaded.Primitives.Where(p => p.SourcePrimitiveOrdinal == 1).SelectMany(p => p.SourceTriangleIds!).SequenceEqual(new[] { 13 }),
                "source triangle IDs remain in exact source order across joint-palette partition boundaries");
            Check(loaded.Primitives.SelectMany(p => p.SourceTriangleIds!).Order().SequenceEqual(Enumerable.Range(0, 14)),
                "palette partitions preserve all source triangles exactly once");
            var split = loaded.Primitives.Where(p => p.SourcePrimitiveOrdinal == 0).ToArray();
            Check(split.Length >= 2 && !ReferenceEquals(split[0].SourceTriangleIds, split[1].SourceTriangleIds),
                "separate palette batches own separate source-ID subsets");
            Check(loaded.SourceRig!.Evaluate(null, loaded.Joints).All(m => CharacterSourceClip.MatrixDistance(m, M.Identity) < .00001f), "forward parent hierarchy and inverse binds evaluate to complete identity skin at rest");
            int mixedCorners = 0;
            foreach (var part in loaded.Primitives)
                foreach (uint index in part.Indices)
                {
                    var v = part.Vertices[index]; var packed = CharacterWeightedModelLoader.UnpackJoints(v.PackedJoints);
                    if (v.Weights.X == .125f)
                    { Check(v.Weights == new System.Numerics.Vector4(.125f, .25f, .5f, .125f)
                        && new[] { packed.J0, packed.J1, packed.J2, packed.J3 }.Select(j => part.JointPalette![j]).SequenceEqual(new[] { 0, 1, 2, 3 }), "actual loader preserves all four mixed influences in local palette remap"); mixedCorners++; }
                }
            Check(mixedCorners == 2, "mixed-weight position survives both ordinary and seam corners");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Failures()
    {
        void Bad(Action<Cmdl> edit, string why) { var (m, s) = Fixture(); edit(m); Throws<InvalidDataException>(() => RetroCharacterSourceGlbWriter.Write(m, s), why); }
        void BadRig(Action<Dictionary<uint, RetroNativeBone>> edit, string why) { var (m, s) = Fixture(); var bones = s.Bones.ToDictionary(p => p.Key, p => p.Value); edit(bones); Throws<InvalidDataException>(() => RetroCharacterSourceGlbWriter.Write(m, new(bones)), why); }
        var (validModel, _) = Fixture();
        Throws<InvalidDataException>(() => RetroCharacterSourceGlbWriter.Write(validModel, new RetroNativeSkeleton(null!)), "null skeleton dictionary has a controlled format refusal");
        Bad(m => m.Positions = new float[4], "position tuple extent"); Bad(m => m.Normals = new float[2], "normal tuple extent"); Bad(m => m.Uv0 = new float[3], "UV tuple extent"); Bad(m => m.ShortUvs = new float[3], "short UV tuple extent");
        Bad(m => m.Materials = Array.Empty<Cmdl.Material>(), "material table absent"); Bad(m => m.Materials = new Cmdl.Material[257], "material table count budget");
        Bad(m => m.Materials[0] = null!, "null material"); Bad(m => m.TriangleMaterial[0] = -1, "negative source material"); Bad(m => m.TriangleMaterial[0] = 3, "source material extent");
        Bad(m => m.Triangles = m.Triangles[..^1], "triangle corner count mismatch");
        foreach (int invalid in new[] { -1, 39 }) Bad(m => m.Triangles[0].P = invalid, "source position index");
        foreach (int invalid in new[] { -1, 2 }) Bad(m => m.Triangles[0].N = invalid, "source normal index");
        foreach (int invalid in new[] { -2, 42 }) Bad(m => m.Triangles[0].T0 = invalid, "source UV index");
        Bad(m => m.ShortUvs = null, "short UV material requires referenced short UV table");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -100001f })
        { Bad(m => m.Positions[0] = invalid, "bounded position"); Bad(m => m.Normals[0] = invalid, "bounded referenced normal"); Bad(m => m.Uv0[0] = invalid, "bounded referenced UV"); }
        Bad(m => Array.Clear(m.Normals), "zero normal"); Bad(m => m.Skin = null, "missing skin"); Bad(m => m.Skin!.RemoveAt(0), "incomplete skin");
        Bad(m => m.Skin![0] = null!, "null influence group"); Bad(m => m.Skin![0] = new (uint, float)[1025], "influence scan cap");
        foreach (float weight in new[] { -.1f, float.NaN, float.PositiveInfinity, .5f }) Bad(m => m.Skin![0] = new[] { (0u, weight) }, "invalid/nonunit weight");
        Bad(m => m.Skin![0] = new[] { (0u, .5f), (0u, .5f) }, "duplicate positive bone"); Bad(m => m.Skin![0] = new[] { (99999u, 1f) }, "foreign positive bone");
        Bad(m => m.Skin![0] = new[] { (0u, .2f), (101u, .2f), (102u, .2f), (103u, .2f), (104u, .2f) }, "five positive influences cannot be pruned");
        BadRig(b => b[0] = b[0] with { Parent = 96 }, "wrong external root sentinel"); BadRig(b => b[101] = b[101] with { Parent = 97 }, "multiple external roots");
        BadRig(b => { b[101] = b[101] with { Parent = 102 }; b[102] = b[102] with { Parent = 101 }; }, "cycle with otherwise valid root");
        BadRig(b => b[0] = b[0] with { X = float.NaN }, "nonfinite source bind"); BadRig(b => b[0] = b[0] with { Name = "bad\nname" }, "invalid source joint identity");
        BadRig(b => { var root = b[0]; b.Remove(0); b[99] = root; }, "dictionary key must equal source bone ID");
        BadRig(b => b[101] = null!, "null skeleton bone cannot escape controlled admission");
        BadRig(b => b[101] = b[0], "duplicate source ID under another dictionary key");
        BadRig(b => { for (int i = 39; i < 1024; i++) { uint id = (uint)(10000 + i); b[id] = new(id, 0, "extra", 0, 0, 0); } }, "complete skeleton node capture budget");
        var (huge, skeleton) = Fixture(); huge.Triangles = new Cmdl.Corner[500001]; huge.TriangleMaterial = new int[166667];
        Throws<InvalidDataException>(() => RetroCharacterSourceGlbWriter.Write(huge, skeleton), "corner/output allocation preflight budget");
        var (largePositions, s) = Fixture(); largePositions.Positions = new float[1_500_003];
        Throws<InvalidDataException>(() => RetroCharacterSourceGlbWriter.Write(largePositions, s), "unused source position pool cannot bypass capture budget");
    }

    private static (Cmdl, RetroNativeSkeleton) Fixture()
    {
        uint Id(int i) => i == 0 ? 0 : (uint)(100 + i);
        var bones = Enumerable.Range(0, 39).Reverse().Select(i => new RetroNativeBone(Id(i), i == 0 ? 97u : i == 38 ? 0u : Id(i + 1), "joint_" + i, 1 + i * 1.25f, 2 - i * .5f, 3 + i * 2f)).ToDictionary(b => b.Id);
        var model = new Cmdl { Positions = Enumerable.Range(0, 39).SelectMany(i => new[] { i + 1f, i % 3 == 2 ? 3f : 1f, -i - .25f }).ToArray(), Normals = new float[] { 0, 1, 0, 1, 0, 0 },
            Uv0 = Enumerable.Range(0, 42).SelectMany(i => new[] { i / 8f, -i / 8f }).ToArray(), ShortUvs = Enumerable.Range(0, 42).SelectMany(i => new[] { .5f + i / 16f, .25f - i / 16f }).ToArray(),
            Materials = new[] { new Cmdl.Material(), new Cmdl.Material { Flags = Cmdl.ShortUv }, new Cmdl.Material() }, TriangleMaterial = Enumerable.Repeat(0, 13).Append(1).ToArray(),
            Triangles = Enumerable.Range(0, 39).Select(i => new Cmdl.Corner { P = i, N = 0, T0 = i }).Concat(Enumerable.Range(0, 3).Select(i => new Cmdl.Corner { P = i, N = 1, T0 = i == 2 ? -1 : 39 + i })).ToArray(),
            Skin = Enumerable.Range(0, 39).Select(i => new[] { (Id(i), 1f) }).ToList() };
        model.Skin[0] = new[] { (0u, .125f), (101u, .25f), (102u, .5f), (103u, .125f), (99999u, 0f) };
        return (model, new(bones));
    }
    private static byte[] PngPixel(byte[] png)
    {
        using var data = new MemoryStream(); int at = 8;
        while (at < png.Length) { int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at, 4)); if (png.AsSpan(at + 4, 4).SequenceEqual("IDAT"u8)) data.Write(png.AsSpan(at + 8, length)); at += 12 + length; }
        data.Position = 0; using var zlib = new ZLibStream(data, CompressionMode.Decompress); using var output = new MemoryStream(); zlib.CopyTo(output); return output.ToArray();
    }
    private static string InputHash(Cmdl m, RetroNativeSkeleton s) => Sha(JsonSerializer.SerializeToUtf8Bytes(new { Model = m, Skeleton = s }, new JsonSerializerOptions { IncludeFields = true }));
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); _checks++; }
    private static void Throws<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { _checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
    private sealed class CallbackDictionary(IReadOnlyDictionary<uint, RetroNativeBone> values, Action beforeEnumeration) : IReadOnlyDictionary<uint, RetroNativeBone>
    {
        public int Count => values.Count; public IEnumerable<uint> Keys => values.Keys; public IEnumerable<RetroNativeBone> Values => values.Values; public RetroNativeBone this[uint key] => values[key];
        public bool ContainsKey(uint key) => values.ContainsKey(key); public bool TryGetValue(uint key, out RetroNativeBone value) => values.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<uint, RetroNativeBone>> GetEnumerator() { beforeEnumeration(); return values.GetEnumerator(); } IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
