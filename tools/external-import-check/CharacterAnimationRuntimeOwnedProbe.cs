using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Render.Characters;
using N = System.Numerics.Matrix4x4;
using M = OpenTK.Mathematics.Matrix4;

// Optional owned-source CPU acceptance. Only the tiny triangle is authored here;
// all derived source files live under one disposable temporary directory.
internal static class CharacterAnimationRuntimeOwnedProbe
{
    private const string Stem = "darksamus-echoes";
    private const string ExpectedArchiveSha = "42a042f58ef2ff6395607fce60c458e54c7b10ad90c13142fbc0d143019471f1";
    private const string ExpectedCinfSha = "3a8d0a84695906ac06fee59478fac134623a4a885cb3b1a1e6e144c4670b3de4";
    private static readonly string[] SourceFiles = { Stem + ".ancs-graph.json", Stem + ".ancs.bin", Stem + ".cinf.bin", Stem + ".animations.bin" };
    internal static int Run(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-character-animation-owned-" + Guid.NewGuid().ToString("N"));
        string? before = null, after = null, errorType = null, errorSha = null;
        string core = typeof(RetroCharacterAnimationArchive).Assembly.Location, tool = typeof(CharacterAnimationRuntimeOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var caches = SnapshotCaches();
        bool cleanup = false, cachesUnchanged = false; int checks = 0, keySamples = 0, offGridSamples = 0, rootClips = 0;
        int nodes = 0, joints = 0, resources = 0, roots = 0, ordinal = -1, readBefore = 0, readAfter = 0;
        long? proofReadsBefore = null, proofReadsAfter = null; string? generation = null, bankSha = null, poseSha = null;
        var inputs = new List<object>();
        void Check(bool value, string why) { if (!value) throw new InvalidDataException(why); checks++; }
        void Refuse(Action work, string why)
        {
            try { work(); } catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or ArgumentOutOfRangeException) { checks++; return; }
            throw new InvalidDataException("Expected refusal: " + why);
        }
        try
        {
            before = HashFile(rvz);
            using (var disc = GameCubeDiscReader.Open(rvz))
                Check(disc.GameId == "G2ME01" && disc.Revision == 0 && disc.DiscNumber == 0, "Pinned Echoes edition required.");
            Directory.CreateDirectory(temporary);
            string imported = Path.Combine(temporary, "import"), packRoot = Path.Combine(temporary, "private-pack");
            var exported = RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imported, selectedIds: new[] { Stem });
            var inspection = LocalImportBundle.Inspect(imported, Stem, RetroCharacterPreviewExporter.BundleSpec(Stem));
            Check(exported.Count == 1 && inspection.State == ImportBundleState.Ready && inspection.Receipt != null,
                "Selected normal appearance must publish one complete bundle.");
            generation = inspection.Receipt!.Generation;
            string folder = Path.GetDirectoryName(exported.Single())!;
            Check(Path.GetFileName(folder) == generation && inspection.Files.All(f => Path.GetDirectoryName(f) == folder), "One immutable import generation required.");
            Directory.CreateDirectory(packRoot);
            foreach (string name in SourceFiles)
            {
                string path = Path.Combine(folder, name); LocalImportBundle.CheckPath(path, imported);
                Check(inspection.Files.Contains(path), "Archive inputs must belong to the committed generation.");
                File.Copy(path, Path.Combine(packRoot, name));
                inputs.Add(new { FileId = name, Bytes = new FileInfo(path).Length, Sha256 = HashFile(path) });
            }
            byte[] description = File.ReadAllBytes(Path.Combine(packRoot, SourceFiles[0]));
            byte[] ancs = File.ReadAllBytes(Path.Combine(packRoot, SourceFiles[1]));
            byte[] cinf = File.ReadAllBytes(Path.Combine(packRoot, SourceFiles[2]));
            byte[] bank = File.ReadAllBytes(Path.Combine(packRoot, SourceFiles[3]));
            var metadata = RetroAnimationGraphProof.Verify(description, ancs);
            var graph = new RetroAnimationGraphProof.Capture(ancs, description, metadata);
            var skeleton = RetroCharacterSource.ReadSkeleton(cinf);
            var archive = RetroCharacterAnimationArchive.Read(bank, graph, cinf, echoes: true);
            Check(archive.SourceSha256 == ExpectedArchiveSha && Sha(cinf) == ExpectedCinfSha, "Archive and raw skeleton must match independently pinned normal-source hashes.");
            Check(metadata.SourceCharacterId == Stem && metadata.SourcePart == null && metadata.Roots.Length == 76
                && skeleton.Bones.Count == 39 && skeleton.Bones[0].Parent == 97, "Original normal graph and complete root97 skeleton required.");
            resources = archive.Resources.Count; roots = archive.Roots.Count; bankSha = archive.SourceSha256;
            WriteRig(Path.Combine(packRoot, "own-triangle.glb"), skeleton);
            WriteManifest(packRoot, bankSha);
            var pack = CharacterModelPack.Load(packRoot);
            var appearance = new PrivateCharacterAppearance("darksamus", "MP2DarkSamus");
            Check(pack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var asset)
                && pack.TryCreateSourcePreviewLease(asset, out _), "Actual private owner must resolve and issue a lease.");
            Check(pack.TryCreateSourcePreviewLease(asset, out var lease), "Verified source lease required.");
            var model = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease); var rig = model.SourceRig!;
            nodes = rig.Nodes.Count; joints = model.Joints.Count;
            Check(nodes == 40 && joints == 39 && model.IndexCount == 3 && model.VertexCount == 3,
                "Complete39 skin and one identity mesh use only our triangle.");
            Check(rig.Evaluate(null, model.Joints).All(m => CharacterSourceClip.MatrixDistance(m, M.Identity) < .0001f), "Complete bind palette must be identity at rest.");
            // Find a genuine strict rigid primitive; no composite leaf substitution.
            ordinal = archive.Roots.Where(r => r.CharacterDeclared && r.StrictPlay).Select(r => r.Ordinal)
                .First(i => archive.OpenStrictPrimitive(i).RigidScale);
            var sourceClip = archive.OpenStrictPrimitive(ordinal);
            var clip = CharacterSourceClip.Load(lease, asset, rig, ordinal);
            Check(clip.ResourceId == sourceClip.Source.Id && clip.ContentSha256 == sourceClip.Source.Sha256
                && clip.Matches(lease, asset, rig), "Actual clip retains exact archive resource and owner identity.");
            Check(!clip.Matches(lease, asset with { }, rig), "Equal asset DTO cannot forge clip ownership.");
            int initialReads = CharacterSourceClip.CapturedFileReads; long? initialProofReads = ProofReads();
            var first = clip.Sample(0); poseSha = PoseHash(first);
            Check(initialReads == CharacterSourceClip.CapturedFileReads && initialProofReads == ProofReads(), "Initial sampling must not perform lazy proof IO.");
            foreach (var root in archive.Roots.Where(r => r.CharacterDeclared && r.StrictPlay))
            {
                var temporal = archive.OpenStrictPrimitive(root.Ordinal);
                if (!temporal.RigidScale) continue;
                var actual = root.Ordinal == ordinal ? clip : CharacterSourceClip.Load(lease, asset, rig, root.Ordinal);
                Check(actual.ResourceId == temporal.Source.Id && actual.ContentSha256 == temporal.Source.Sha256,
                    "Every admitted root retains its exact complete temporal resource.");
                Check(temporal.KeyCount <= 20000 && keySamples <= 2_000_000 - temporal.KeyCount, "Bound complete root sampling work.");
                int startReads = CharacterSourceClip.CapturedFileReads; long? startProofReads = ProofReads();
                var retained = actual.Sample(0); string retainedSha = PoseHash(retained);
                for (int key = 0; key < temporal.KeyCount; key++)
                { ValidatePose(actual.Sample(key * temporal.Tick), rig, model.Joints); keySamples++; }
                foreach (double time in new[] { temporal.Tick * .375, temporal.Duration * .413, temporal.Duration + temporal.Tick })
                { ValidatePose(actual.Sample(time), rig, model.Joints); offGridSamples++; }
                Check(PoseHash(retained) == retainedSha && PoseHash(actual.Sample(0)) == retainedSha,
                    "Each admitted root preserves retained snapshots and exact A/B/A determinism.");
                Check(startReads == CharacterSourceClip.CapturedFileReads && startProofReads == ProofReads(),
                    "Every admitted root samples without source/proof IO.");
                if (root.Ordinal == ordinal)
                { readBefore = startReads; readAfter = CharacterSourceClip.CapturedFileReads; proofReadsBefore = startProofReads; proofReadsAfter = ProofReads(); }
                rootClips++;
            }
            Check(rootClips > 0 && offGridSamples == rootClips * 3, "Every declared strict rigid root was sampled.");
            Check(PoseHash(first) == poseSha && PoseHash(clip.Sample(0)) == poseSha, "Retained snapshot and A/B/A sampling remain unchanged.");
            Refuse(() => ((IList<M>)first)[0] = M.Identity, "sample collection is immutable");
            Refuse(() => CharacterSourceClip.Load(lease, asset, rig, -1), "negative root");
            Refuse(() => CharacterSourceClip.Load(lease, asset, rig, roots), "root extent");
            foreach (var root in archive.Roots.Where(r => !r.StrictPlay))
                Refuse(() => CharacterSourceClip.Load(lease, asset, rig, root.Ordinal), "composite or nonzero-time root");
            foreach (var root in archive.Roots.Where(r => r.StrictPlay && !r.CharacterDeclared))
                Refuse(() => CharacterSourceClip.Load(lease, asset, rig, root.Ordinal), "undeclared source root");
            foreach (var root in archive.Roots.Where(r => r.StrictPlay))
                if (!archive.OpenStrictPrimitive(root.Ordinal).RigidScale)
                    Refuse(() => CharacterSourceClip.Load(lease, asset, rig, root.Ordinal), "source nonrigid scale");
            var secondPack = CharacterModelPack.Load(packRoot);
            Check(secondPack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var secondAsset)
                && secondPack.TryCreateSourcePreviewLease(secondAsset, out var secondLease) && secondLease.CacheKey != lease.CacheKey,
                "Independent pack owner cannot alias source animation cache identity.");

            // No disk data are consulted until an explicit proof boundary retires the lease.
            File.Delete(Path.Combine(packRoot, SourceFiles[3]));
            Check(PoseHash(clip.Sample(0)) == poseSha, "Captured clip survives unavailable source without IO.");
            Refuse(() => lease.Require(asset), "explicit strong proof detects missing archive");
            Refuse(() => clip.Sample(0), "retired lease cannot keep sampling stale content");
            File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[3]), bank);
            Check(pack.RefreshSourcePreviewProof() && pack.TryCreateSourcePreviewLease(asset, out var renewed)
                && renewed.CacheKey != lease.CacheKey, "Restored proof issues a fresh generation and cache key.");
            Refuse(() => clip.Sample(0), "refresh cannot revive a stale clip");
            Check(pack.TryCreateSourcePreviewLease(asset, out var current), "Current generation lease required.");
            var currentClip = CharacterSourceClip.Load(current, asset, rig, ordinal);
            Check(PoseHash(currentClip.Sample(0)) == poseSha, "New generation reproduces immutable source pose.");
            byte[] changed = (byte[])bank.Clone(); changed[^1] ^= 1; File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[3]), changed);
            Check(!pack.RefreshSourcePreviewProof(), "Changed source archive cannot be adopted by existing owner.");
            Refuse(() => currentClip.Sample(0), "changed proof retires new clip");
            File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[3]), bank);

            // A valid rehashed temporal variant remains outside the pinned runtime source.
            byte[] scaleBank = RetroCharacterAnimationArchive.Build(graph, cinf, true,
                id => id == sourceClip.Source.Id ? ScaleAnimation() : ExtractResource(bank, id));
            File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[3]), scaleBank); WriteManifest(packRoot, Sha(scaleBank));
            Check(Sha(scaleBank) != ExpectedArchiveSha
                && !RetroCharacterAnimationArchive.Read(scaleBank, graph, cinf, true).OpenStrictPrimitive(ordinal).RigidScale,
                "Invented scale variant has a self-consistent archive but a different original-source identity.");
            Refuse(() => CharacterModelPack.Load(packRoot), "Rehashing forged temporal bytes cannot qualify the pinned runtime archive.");
            File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[3]), bank); WriteManifest(packRoot, bankSha);
            byte[] changedCinf = (byte[])cinf.Clone(); changedCinf[12] ^= 1; // finite first-bone bind coordinate remains parseable
            Check(RetroCharacterSource.ReadSkeleton(changedCinf).Bones.Count == 39 && Sha(changedCinf) != ExpectedCinfSha,
                "Forged CINF retains a complete parseable hierarchy but changes source identity.");
            File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[2]), changedCinf); WriteManifest(packRoot, bankSha);
            Refuse(() => CharacterModelPack.Load(packRoot), "Rehashing provenance cannot qualify forged raw CINF.");
            File.WriteAllBytes(Path.Combine(packRoot, SourceFiles[2]), cinf);
            WriteRig(Path.Combine(packRoot, "own-triangle.glb"), skeleton, rootOffset: .01f); WriteManifest(packRoot, bankSha);
            var wrongBindPack = CharacterModelPack.Load(packRoot);
            Check(wrongBindPack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var wrongAsset)
                && wrongBindPack.TryCreateSourcePreviewLease(wrongAsset, out _), "Invented altered bind remains a generic source rig.");
            Check(wrongBindPack.TryCreateSourcePreviewLease(wrongAsset, out var wrongLease), "Altered bind lease required.");
            var wrongModel = CharacterWeightedModelLoader.LoadSourcePreview(wrongAsset, wrongLease);
            Refuse(() => CharacterSourceClip.Load(wrongLease, wrongAsset, wrongModel.SourceRig!, ordinal), "Exact CINF local bind cannot be changed.");
            WriteManifest(packRoot, bankSha, skin: "MP2DarkSamusFinal");
            Refuse(() => CharacterModelPack.Load(packRoot), "Final remains outside normal runtime profile.");
            Array.Clear(ancs); Array.Clear(description); Array.Clear(cinf); Array.Clear(bank);
            Check(PoseHash(first) == poseSha, "Retained pose detached from all caller input arrays.");
        }
        catch (Exception e) { errorType = e.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(e.Message)); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); cleanup = !Directory.Exists(temporary); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            try { after = HashFile(rvz); } catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            var finalCaches = SnapshotCaches(); cachesUnchanged = caches.All(p => finalCaches[p.Key].SequenceEqual(p.Value));
        }
        string coreAfter = HashFile(core), toolAfter = HashFile(tool);
        bool passed = errorType == null && cleanup && before != null && before == after && coreBefore == coreAfter && toolBefore == toolAfter && cachesUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new { Schema = 1, Passed = passed, ErrorType = errorType, ErrorMessageSha256 = errorSha,
            Checks = checks, MetadataOutputOnly = true, CpuOnly = true, RenderingActivated = false, GameplayActivated = false,
            SourceBeforeSha256 = before, SourceAfterSha256 = after, SourceUnchanged = before != null && before == after,
            CoreBeforeSha256 = coreBefore, CoreAfterSha256 = coreAfter, ToolBeforeSha256 = toolBefore, ToolAfterSha256 = toolAfter,
            TemporaryDerivedPayloadsDeleted = cleanup, RetainedSourcePayloadsWritten = !cleanup, CachesUnchanged = cachesUnchanged,
            Generation = generation, Inputs = inputs, ArchiveSha256 = bankSha, Resources = resources, Roots = roots,
            Nodes = nodes, Joints = joints, OwnTriangles = 1, StrictOrdinal = ordinal, RootClips = rootClips, KeySamples = keySamples, OffGridSamples = offGridSamples,
            CapturedFileReadsBefore = readBefore, CapturedFileReadsAfter = readAfter, ProofReadsBefore = proofReadsBefore, ProofReadsAfter = proofReadsAfter,
            InitialPoseSha256 = poseSha }));
        return passed ? 0 : 1;
    }

    private static void ValidatePose(IReadOnlyList<M> pose, CharacterSourceRig rig, IReadOnlyList<CharacterWeightedJoint> joints)
    {
        if (pose.Count != 40) throw new InvalidDataException("Complete pose required.");
        foreach (M matrix in pose) CharacterSourceRig.RequireRigid(matrix);
        var palette = rig.Evaluate(pose, joints);
        if (palette.Length != 39) throw new InvalidDataException("Complete bind palette required.");
        foreach (M matrix in palette) CharacterSourceRig.RequireRigid(matrix);
    }
    private static void WriteManifest(string root, string bankSha, string skin = "MP2DarkSamus")
    {
        string witness = Sha("own synthetic triangle"u8.ToArray());
        var receipt = new JsonObject { ["format"] = 1, ["sourceCharacter"] = Stem, ["nativeHunter"] = "Samus", ["characterId"] = "darksamus",
            ["skinSource"] = skin, ["presentationPart"] = "biped", ["visualOnly"] = true, ["sourceGlbSha256"] = HashFile(Path.Combine(root, "own-triangle.glb")),
            ["sourceNativeKitSha256"] = witness, ["sourceMappingSha256"] = witness, ["sourceSkeletonSha256"] = HashFile(Path.Combine(root, SourceFiles[2])), ["sourceWeightsSha256"] = witness };
        foreach (string flag in new[] { "approvedForGameplay", "multiplayerEnabled", "playerSelectionEnabled", "firstPersonVerified", "alternateFormVerified", "sourceMotionRetargetVerified", "devicePerformanceVerified" }) receipt[flag] = false;
        File.WriteAllText(Path.Combine(root, "receipt.json"), receipt.ToJsonString());
        var entry = new JsonObject { ["hunter"] = "Samus", ["part"] = "biped", ["lod"] = 0, ["model"] = "own-triangle.glb", ["skinning"] = "weighted4",
            ["poseSource"] = "sourceRigPreview", ["characterId"] = "darksamus", ["skinSource"] = skin, ["provenance"] = "receipt.json", ["provenanceSha256"] = HashFile(Path.Combine(root, "receipt.json")),
            ["boneMap"] = new JsonObject(), ["sourceAnimationArchive"] = SourceFiles[3], ["sourceAnimationArchiveSha256"] = bankSha };
        File.WriteAllText(Path.Combine(root, "characters.json"), new JsonObject { ["format"] = 1, ["id"] = "own-triangle-source-animation", ["models"] = new JsonArray(entry) }.ToJsonString());
    }
    private static void WriteRig(string path, RetroNativeSkeleton skeleton, float rootOffset = 0)
    {
        var bones = skeleton.Bones.Values.OrderBy(b => b.Id).ToArray(); var index = bones.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var nodes = new JsonArray(); var locals = new N[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i]; double x = b.X, y = b.Y, z = b.Z;
            if (skeleton.Bones.TryGetValue(b.Parent, out var parent)) { x -= parent.X; y -= parent.Y; z -= parent.Z; }
            locals[i] = N.CreateTranslation((float)-x + (b.Id == 0 ? rootOffset : 0), (float)z, (float)y);
            nodes.Add(new JsonObject { ["name"] = "source_bone_" + b.Id + "_" + b.Name, ["matrix"] = Floats(locals[i]),
                ["children"] = new JsonArray(bones.Where(c => c.Parent == b.Id).Select(c => JsonValue.Create(index[c.Id])).ToArray()) });
        }
        nodes.Add(new JsonObject { ["name"] = "own_identity_mesh", ["mesh"] = 0, ["skin"] = 0 });
        using var bin = new MemoryStream(); using var w = new BinaryWriter(bin); var views = new JsonArray(); var accessors = new JsonArray();
        int Access(string type, int component, int count, Action write)
        { while (bin.Position % 4 != 0) w.Write((byte)0); int offset = (int)bin.Position; write(); views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = (int)bin.Position - offset }); accessors.Add(new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = component, ["count"] = count, ["type"] = type }); return accessors.Count - 1; }
        int positions = Access("VEC3", 5126, 3, () => { foreach (float v in new float[] { 0, 0, 0, .1f, 0, 0, 0, .1f, 0 }) w.Write(v); });
        int normals = Access("VEC3", 5126, 3, () => { for (int i = 0; i < 3; i++) { w.Write(0f); w.Write(0f); w.Write(1f); } });
        int uv = Access("VEC2", 5126, 3, () => { foreach (float v in new float[] { 0, 0, 1, 0, 0, 1 }) w.Write(v); });
        int joints = Access("VEC4", 5123, 3, () => { foreach (ushort j in new ushort[] { 0, 1, 38 }) { w.Write(j); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); } });
        int weights = Access("VEC4", 5126, 3, () => { for (int i = 0; i < 3; i++) { w.Write(1f); w.Write(0f); w.Write(0f); w.Write(0f); } });
        int triangles = Access("SCALAR", 5125, 3, () => { w.Write(0u); w.Write(1u); w.Write(2u); });
        N Rest(int i) => index.TryGetValue(bones[i].Parent, out int parent) ? locals[i] * Rest(parent) : locals[i];
        int inverse = Access("MAT4", 5126, bones.Length, () => { for (int i = 0; i < bones.Length; i++) { if (!N.Invert(Rest(i), out N m)) throw new InvalidDataException("Synthetic bind inversion failed."); foreach (float v in Values(m)) w.Write(v); } });
        byte[] binary = bin.ToArray(); var document = new JsonObject { ["asset"] = new JsonObject { ["version"] = "2.0" }, ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = binary.Length }),
            ["bufferViews"] = views, ["accessors"] = accessors, ["nodes"] = nodes, ["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(Enumerable.Range(0, bones.Length).Select(i => JsonValue.Create(i)).ToArray()), ["inverseBindMatrices"] = inverse }),
            ["materials"] = new JsonArray(new JsonObject { ["name"] = "OwnTriangle" }), ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = new JsonArray(new JsonObject { ["material"] = 0,
                ["attributes"] = new JsonObject { ["POSITION"] = positions, ["NORMAL"] = normals, ["TEXCOORD_0"] = uv, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights }, ["indices"] = triangles }) }) };
        byte[] json = Encoding.UTF8.GetBytes(document.ToJsonString()); int jl = (json.Length + 3) & ~3, bl = (binary.Length + 3) & ~3;
        using var output = File.Create(path); using var glb = new BinaryWriter(output); glb.Write(0x46546c67u); glb.Write(2u); glb.Write((uint)(12 + 8 + jl + 8 + bl)); glb.Write((uint)jl); glb.Write(0x4e4f534au); glb.Write(json);
        for (int i = json.Length; i < jl; i++) glb.Write((byte)32); glb.Write((uint)bl); glb.Write(0x004e4942u); glb.Write(binary); for (int i = binary.Length; i < bl; i++) glb.Write((byte)0);
    }
    private static byte[] ScaleAnimation()
    {
        using var bytes = new MemoryStream();
        void U32(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); bytes.Write(b); }
        void F32(float value) => U32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        U32(0); F32(1); U32(0); F32(.5f); U32(0); U32(3); U32(0); U32(1); bytes.WriteByte(0);
        for (int i = 0; i < 3; i++) { U32(1); bytes.WriteByte(0); }
        U32(3); for (int i = 0; i < 9; i++) F32(2);
        U32(3); for (int i = 0; i < 3; i++) { F32(1); F32(0); F32(0); F32(0); }
        U32(3); for (int i = 0; i < 9; i++) F32(0); return bytes.ToArray();
    }
    private static byte[] ExtractResource(byte[] bank, uint id)
    {
        int at = 16 + BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(8, 4)); int count = BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(12, 4));
        for (int i = 0; i < count; i++) { uint found = BinaryPrimitives.ReadUInt32LittleEndian(bank.AsSpan(at, 4)); int length = BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(at + 4, 4)); if (found == id) return bank.AsSpan(at + 40, length).ToArray(); at += 40 + length; }
        throw new InvalidDataException("Captured resource is absent.");
    }
    private static JsonArray Floats(N m) => new(Values(m).Select(v => JsonValue.Create(v)).ToArray());
    private static float[] Values(N m) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
    private static string PoseHash(IReadOnlyList<M> pose)
    { using var bytes = new MemoryStream(); using var w = new BinaryWriter(bytes); foreach (M m in pose) foreach (float v in new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 }) w.Write(v); return Sha(bytes.ToArray()); }
    private static long? ProofReads() => typeof(CharacterModelPack).Assembly.GetType("MphRead.Mods.Render.Characters.CharacterSourceProofIO")?.GetField("OpenedReads", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as long?;
    private static Dictionary<string, object[]> SnapshotCaches()
    { var result = new Dictionary<string, object[]>(); foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" }) result.Add(name, ((IEnumerable)typeof(Read).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray()); return result; }
    private static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
