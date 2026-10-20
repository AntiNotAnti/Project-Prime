using System.Buffers.Binary;
using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

// Own synthetic geometry only. This tests the writer's transport/root contract,
// not runtime pack admission, original assets, rendering or playable readiness.
internal static class PrimeTrooperGlbAcceptance
{
    private static int _checks;
    internal static void Run()
    {
        _checks = 0;
        var (model, skeleton) = Fixture(); string before = InputHash(model, skeleton);
        byte[] bytes = RetroCharacterSourceGlbWriter.WritePrimeTrooper(model, skeleton);
        Check(bytes.SequenceEqual(RetroCharacterSourceGlbWriter.WritePrimeTrooper(model, skeleton)), "deterministic Prime writer bytes");
        var reverse = new RetroNativeSkeleton(skeleton.Bones.Reverse().ToDictionary(p => p.Key, p => p.Value));
        Check(bytes.SequenceEqual(RetroCharacterSourceGlbWriter.WritePrimeTrooper(model, reverse)), "dictionary order cannot change full Prime hierarchy/bytes");
        Check(InputHash(model, skeleton) == before, "successful Prime write does not mutate caller inputs");
        Inspect(bytes, model, skeleton);
        Refuses<InvalidDataException>(() => RetroCharacterSourceGlbWriter.Write(model, skeleton), "legacy root97 writer refuses Prime root3/parent2");
        Check(InputHash(model, skeleton) == before, "legacy refusal does not mutate Prime input");

        var legacy = new RetroNativeSkeleton(skeleton.Bones.ToDictionary(p => p.Key,
            p => p.Key == 3 ? p.Value with { Parent = 97 } : p.Value));
        byte[] legacyBytes = RetroCharacterSourceGlbWriter.Write(model, legacy);
        using (var glb = new Glb(legacyBytes))
        {
            var extras = glb.Root.GetProperty("extras");
            Check(!extras.TryGetProperty("sourceRootContract", out _) && !extras.TryGetProperty("sourceRootBoneId", out _)
                && !extras.TryGetProperty("sourceExternalParentId", out _), "legacy output does not gain Prime-only root metadata");
        }
        string legacyBefore = InputHash(model, legacy);
        Refuses<InvalidDataException>(() => RetroCharacterSourceGlbWriter.WritePrimeTrooper(model, legacy), "Prime writer refuses legacy external root97");
        Check(InputHash(model, legacy) == legacyBefore, "Prime refusal preserves legacy caller input");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Refuses<OperationCanceledException>(() => RetroCharacterSourceGlbWriter.WritePrimeTrooper(model, skeleton, cancellation.Token), "pre-cancelled Prime write");
            Check(InputHash(model, skeleton) == before, "pre-cancel preserves source inputs");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var callback = new CallbackDictionary(skeleton.Bones, cancellation.Cancel);
            Refuses<OperationCanceledException>(() => RetroCharacterSourceGlbWriter.WritePrimeTrooper(model, new(callback), cancellation.Token), "cancellation during rig capture");
            Check(InputHash(model, skeleton) == before, "capture cancellation preserves underlying input");
        }
        Failures();
        Console.WriteLine($"PASS: Prime Trooper GLB full35-joint root3/parent2 transport/refusal ({_checks} checks; synthetic only)");
    }

    private static void Inspect(byte[] bytes, Cmdl model, RetroNativeSkeleton skeleton)
    {
        using var glb = new Glb(bytes); var root = glb.Root;
        var extras = root.GetProperty("extras");
        Check(extras.GetProperty("sourceRootContract").GetString() == "prime-trooper-root3-parent2-v1"
            && extras.GetProperty("sourceRootBoneId").GetUInt32() == 3
            && extras.GetProperty("sourceExternalParentId").GetUInt32() == 2, "explicit Prime root identity contract");
        Check(extras.GetProperty("sourceBasis").GetString() == "(-x,z,y)"
            && extras.GetProperty("materialPolicy").GetString() == "diagnostic-source-material-colors-v1", "source basis and diagnostic material policy remain explicit");
        var bones = skeleton.Bones.Values.OrderBy(b => b.Id).ToArray();
        var byId = bones.Select((b, i) => (b.Id, Index: i)).ToDictionary(p => p.Id, p => p.Index);
        var nodes = root.GetProperty("nodes"); var skin = root.GetProperty("skins")[0];
        Check(nodes.GetArrayLength() == 36 && root.GetProperty("skins").GetArrayLength() == 1
            && skin.GetProperty("joints").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(Enumerable.Range(0, 35)), "all35 real joints plus exactly one independent mesh; no injected sentinel bone");
        Check(root.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { byId[3], 35 }), "only original root3 and identity mesh are scene roots");
        Check(nodes[35].GetProperty("name").GetString() == "source_identity_mesh" && nodes[35].GetProperty("mesh").GetInt32() == 0
            && nodes[35].GetProperty("skin").GetInt32() == 0 && !nodes[35].TryGetProperty("translation", out _), "mesh stays independent and untransformed");
        float[] inverse = glb.Floats(skin.GetProperty("inverseBindMatrices").GetInt32(), "MAT4", 35);
        var local = new float[35][]; var parent = Enumerable.Repeat(-1, 35).ToArray();
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i]; float x = b.X, y = b.Y, z = b.Z;
            if (skeleton.Bones.TryGetValue(b.Parent, out var p)) { x -= p.X; y -= p.Y; z -= p.Z; parent[i] = byId[p.Id]; }
            local[i] = nodes[i].GetProperty("translation").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Check(nodes[i].GetProperty("name").GetString() == $"source_bone_{b.Id}_{b.Name}"
                && local[i].SequenceEqual(new[] { -x, z, y }), "exact real joint name/local bind including forward parents");
            Check(nodes[i].GetProperty("children").EnumerateArray().Select(v => v.GetInt32())
                .SequenceEqual(bones.Where(child => child.Parent == b.Id).Select(child => byId[child.Id])), "exact full child graph without remapping");
            Check(inverse.AsSpan(i * 16, 16).SequenceEqual(new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, b.X, -b.Z, -b.Y, 1 }), "inverse bind bytes exactly undo original global rest translation");
        }
        for (int i = 0; i < bones.Length; i++)
        {
            float x = 0, y = 0, z = 0; int steps = 0;
            for (int n = i; n != -1; n = parent[n]) { Check(++steps <= 35, "emitted hierarchy remains acyclic"); x += local[n][0]; y += local[n][1]; z += local[n][2]; }
            Check(x == -bones[i].X && y == bones[i].Z && z == bones[i].Y, "independent accumulated hierarchy reproduces every original global bind");
            Check(x + inverse[i * 16 + 12] == 0 && y + inverse[i * 16 + 13] == 0 && z + inverse[i * 16 + 14] == 0, "independent rest skin is identity for every real joint");
        }
        var materials = root.GetProperty("materials"); var primitives = root.GetProperty("meshes")[0].GetProperty("primitives");
        Check(materials.GetArrayLength() == 3 && primitives.GetArrayLength() == 2
            && root.GetProperty("images").GetArrayLength() == 3 && root.GetProperty("textures").GetArrayLength() == 3, "all source material ordinals retained, unused material creates no triangle");
        for (int m = 0; m < 3; m++)
            Check(materials[m].GetProperty("extras").GetProperty("sourceMaterialOrdinal").GetInt32() == m
                && materials[m].GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture").GetProperty("index").GetInt32() == m, "independent source material/diagnostic texture identities");
        var allFaces = new List<int>();
        foreach (var primitive in primitives.EnumerateArray())
        {
            int material = primitive.GetProperty("material").GetInt32(); var attributes = primitive.GetProperty("attributes");
            int[] faces = primitive.GetProperty("extras").GetProperty("sourceTriangleIds").EnumerateArray().Select(v => v.GetInt32()).ToArray(); allFaces.AddRange(faces);
            Check(faces.SequenceEqual(Enumerable.Range(0, model.TriangleCount).Where(f => model.TriangleMaterial[f] == material)), "exact source triangle IDs/order within each material");
            int corners = faces.Length * 3;
            float[] positions = glb.Floats(attributes.GetProperty("POSITION").GetInt32(), "VEC3", corners), normals = glb.Floats(attributes.GetProperty("NORMAL").GetInt32(), "VEC3", corners);
            float[] uv = glb.Floats(attributes.GetProperty("TEXCOORD_0").GetInt32(), "VEC2", corners), weights = glb.Floats(attributes.GetProperty("WEIGHTS_0").GetInt32(), "VEC4", corners);
            ushort[] joints = glb.UShorts(attributes.GetProperty("JOINTS_0").GetInt32(), "VEC4", corners);
            uint[] indices = glb.UInts(primitive.GetProperty("indices").GetInt32(), "SCALAR", corners);
            Check(indices.SequenceEqual(Enumerable.Range(0, corners).Select(i => (uint)i)), "expanded indices retain exact corner winding/seams");
            var positionAccessor = root.GetProperty("accessors")[attributes.GetProperty("POSITION").GetInt32()];
            float[] min = positionAccessor.GetProperty("min").EnumerateArray().Select(v => v.GetSingle()).ToArray(), max = positionAccessor.GetProperty("max").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            for (int axis = 0; axis < 3; axis++) Check(min[axis] == Enumerable.Range(0, corners).Min(c => positions[c * 3 + axis])
                && max[axis] == Enumerable.Range(0, corners).Max(c => positions[c * 3 + axis]), "POSITION accessor bounds match actual bytes");
            for (int c = 0; c < corners; c++)
            {
                var input = model.Triangles[faces[c / 3] * 3 + c % 3];
                Check(positions.AsSpan(c * 3, 3).SequenceEqual(new[] { -model.Positions[input.P * 3], model.Positions[input.P * 3 + 2], model.Positions[input.P * 3 + 1] })
                    && normals.AsSpan(c * 3, 3).SequenceEqual(new[] { -model.Normals[input.N * 3], model.Normals[input.N * 3 + 2], model.Normals[input.N * 3 + 1] }), "full corner position/normal values exactly preserved in source basis");
                float[] pool = (model.Materials[material].Flags & Cmdl.ShortUv) == 0 ? model.Uv0 : model.ShortUvs!;
                Check(uv.AsSpan(c * 2, 2).SequenceEqual(input.T0 == -1 ? new float[2] : pool.AsSpan(input.T0 * 2, 2).ToArray()), "float/short/absent UVs retain exact corner values");
                var influences = model.Skin![input.P].Where(w => w.Weight > 0).ToArray();
                for (int component = 0; component < 4; component++)
                    Check(joints[c * 4 + component] == (component < influences.Length ? byId[influences[component].Bone] : 0)
                        && weights[c * 4 + component] == (component < influences.Length ? influences[component].Weight : 0), "all1..4 source influences retain order/weight bits without normalization or sentinel injection");
            }
        }
        Check(allFaces.Order().SequenceEqual(Enumerable.Range(0, model.TriangleCount)), "every original face emitted exactly once");
    }

    private static void Failures()
    {
        void Bad(Action<Cmdl, Dictionary<uint, RetroNativeBone>> edit, string why)
        {
            var (m, s) = Fixture(); var bones = s.Bones.ToDictionary(p => p.Key, p => p.Value); edit(m, bones); var rig = new RetroNativeSkeleton(bones);
            string before = InputHash(m, rig); Refuses<InvalidDataException>(() => RetroCharacterSourceGlbWriter.WritePrimeTrooper(m, rig), why);
            Check(InputHash(m, rig) == before, "refusal leaves invalid caller inputs untouched: " + why);
        }
        foreach (uint version in new uint[] { 0, 1, 3, 4 }) Bad((m, _) => m.Version = version, "only MP1 CMDL version2 admitted");
        Bad((m, b) =>
        {
            var root = b[3]; b.Remove(3); b.Add(99, root with { Id = 99 }); b[37] = b[37] with { Parent = 99 };
            for (int p = 0; p < m.Skin!.Count; p++) m.Skin[p] = m.Skin[p].Select(w => (w.Bone == 3 ? 99u : w.Bone, w.Weight)).ToArray();
        }, "same35-joint graph with wrong real root ID refuses");
        foreach (uint parent in new uint[] { 0, 1, 97, 999 }) Bad((_, b) => b[3] = b[3] with { Parent = parent }, "wrong external root parent refuses");
        Bad((_, b) => b.Remove(4), "34-bone rig refuses");
        Bad((_, b) => b.Add(88, new(88, 3, "extra", 1, 2, 3)), "36-bone rig refuses");
        Bad((_, b) => { b.Remove(4); b.Add(88, new(88, 3, "unweighted", 1, 2, 3)); }, "35 bones cannot conceal missing positively weighted source bone");
        Bad((_, b) => b[5] = b[5] with { Parent = 2 }, "second external root/forest refuses");
        Bad((_, b) => b[6] = b[6] with { Parent = 5 }, "cycle refuses despite one valid root");
        Bad((_, b) => b[5] = b[5] with { Id = 500 }, "dictionary key/real bone ID mismatch refuses");
        Bad((_, b) => b[5] = null!, "null real bone refuses");
        Bad((_, b) => b[5] = b[5] with { X = float.NaN }, "nonfinite unweighted bind refuses");
        Bad((m, _) => m.Positions[0] = float.PositiveInfinity, "nonfinite position refuses");
        Bad((m, _) => m.Normals[0] = float.NaN, "nonfinite referenced normal refuses");
        Bad((m, _) => m.Uv0[0] = float.NaN, "nonfinite referenced float UV refuses");
        Bad((m, _) => m.ShortUvs![0] = float.NaN, "nonfinite referenced short UV refuses");
        Bad((m, _) => m.Skin![0] = new[] { (3u, float.NaN) }, "nonfinite source weight refuses");
        Bad((m, _) => m.Skin![0] = new[] { (3u, .5f), (3u, .5f) }, "duplicate positive source influences refuse");
        Bad((m, _) => m.Skin![0] = new[] { (3u, .5f) }, "nonunit weights refuse without repair");
        Bad((m, _) => m.Skin![0] = new[] { (3u, .2f), (4u, .2f), (5u, .2f), (6u, .2f), (7u, .2f) }, "five positive influences cannot be pruned");
        Bad((m, _) => m.Triangles[0].P = 4, "out-of-range source position refuses");
        Bad((m, _) => m.TriangleMaterial[0] = 3, "out-of-range material refuses");
    }

    private static (Cmdl Model, RetroNativeSkeleton Skeleton) Fixture()
    {
        // Sorted joint4 points forward through37, then to root3. Dictionary
        // insertion is deliberately descending, and all35 original IDs survive.
        var bones = Enumerable.Range(3, 35).Reverse().Select(id => new RetroNativeBone((uint)id,
            id == 3 ? 2u : id == 37 ? 3u : (uint)(id + 1), "prime_joint_" + id,
            id * .25f, 1 + id * .5f, -id * .125f)).ToDictionary(b => b.Id);
        var model = new Cmdl
        {
            Version = 2, Positions = new float[] { .25f, .5f, .75f, 1, 2, 3, -.5f, 1.5f, -.25f, 2, -.5f, 1 },
            Normals = new float[] { 0, 0, 1, 0, 1, 0 }, Uv0 = new float[] { .125f, .25f, .5f, .75f, 1, -.5f, 2, 3 },
            ShortUvs = new float[] { .0625f, .125f, .25f, .375f, .5f, .625f, .75f, .875f },
            Materials = new[] { new Cmdl.Material(), new Cmdl.Material { Flags = Cmdl.ShortUv }, new Cmdl.Material() },
            TriangleMaterial = new[] { 1, 0, 1 },
            Triangles = new[] { C(0, 0, 0), C(1, 0, 1), C(2, 0, 2), C(3, 1, 3), C(2, 1, 0), C(0, 1, -1), C(2, 1, 1), C(1, 1, 3), C(3, 1, -1) },
            Skin = new() { new[] { (3u, 1f), (999u, 0f) }, new[] { (4u, .25f), (3u, .75f) },
                new[] { (5u, .125f), (3u, .375f), (4u, .5f) }, new[] { (6u, .125f), (5u, .25f), (4u, .5f), (3u, .125f) } }
        };
        return (model, new(bones));
        static Cmdl.Corner C(int p, int n, int uv) => new() { P = p, N = n, T0 = uv, T1 = -1, T2 = -1, T3 = -1 };
    }

    private sealed class Glb : IDisposable
    {
        private readonly byte[] _bytes; private readonly JsonDocument _json; private readonly int _binary, _length;
        internal JsonElement Root => _json.RootElement;
        internal Glb(byte[] bytes)
        {
            _bytes = bytes;
            Check(bytes.Length >= 28 && U32(0) == 0x46546c67 && U32(4) == 2 && U32(8) == bytes.Length, "independent GLB v2 header/extent");
            int jsonLength = checked((int)U32(12));
            Check(jsonLength >= 0 && jsonLength % 4 == 0 && jsonLength <= bytes.Length - 28 && U32(16) == 0x4e4f534a, "independent aligned JSON chunk");
            _json = JsonDocument.Parse(bytes.AsMemory(20, jsonLength)); _binary = checked(28 + jsonLength); _length = checked((int)U32(20 + jsonLength));
            Check(U32(24 + jsonLength) == 0x004e4942 && _length == bytes.Length - _binary && _length % 4 == 0, "independent complete BIN chunk");
            Check(Root.GetProperty("buffers").GetArrayLength() == 1 && Root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32() == _length, "one exact embedded buffer");
        }
        private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(at, 4));
        private ReadOnlySpan<byte> Access(int id, string type, int component, int count)
        {
            var a = Root.GetProperty("accessors")[id]; var v = Root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
            int width = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new InvalidDataException("Unknown synthetic accessor.") };
            int size = component == 5123 ? 2 : 4, length = checked(width * count * size), offset = v.GetProperty("byteOffset").GetInt32();
            Check(a.GetProperty("type").GetString() == type && a.GetProperty("componentType").GetInt32() == component && a.GetProperty("count").GetInt32() == count
                && v.GetProperty("buffer").GetInt32() == 0 && v.GetProperty("byteLength").GetInt32() == length && offset >= 0 && offset <= _length - length
                && !a.TryGetProperty("sparse", out _) && !v.TryGetProperty("byteStride", out _), "typed accessor has exact independent extent/components");
            return _bytes.AsSpan(_binary + offset, length);
        }
        internal float[] Floats(int id, string type, int count) { var raw = Access(id, type, 5126, count); var values = new float[raw.Length / 4]; for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(raw.Slice(i * 4, 4)); return values; }
        internal ushort[] UShorts(int id, string type, int count) { var raw = Access(id, type, 5123, count); var values = new ushort[raw.Length / 2]; for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2, 2)); return values; }
        internal uint[] UInts(int id, string type, int count) { var raw = Access(id, type, 5125, count); var values = new uint[raw.Length / 4]; for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(i * 4, 4)); return values; }
        public void Dispose() => _json.Dispose();
    }
    private static string InputHash(Cmdl model, RetroNativeSkeleton skeleton) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { model, skeleton },
        new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })));
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException("Prime Trooper GLB fixture: " + why); _checks++; }
    private static void Refuses<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { _checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
    private sealed class CallbackDictionary(IReadOnlyDictionary<uint, RetroNativeBone> values, Action beforeEnumeration) : IReadOnlyDictionary<uint, RetroNativeBone>
    {
        public int Count => values.Count; public IEnumerable<uint> Keys => values.Keys; public IEnumerable<RetroNativeBone> Values => values.Values; public RetroNativeBone this[uint key] => values[key];
        public bool ContainsKey(uint key) => values.ContainsKey(key); public bool TryGetValue(uint key, out RetroNativeBone value) => values.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<uint, RetroNativeBone>> GetEnumerator() { beforeEnumeration(); return values.GetEnumerator(); } IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
