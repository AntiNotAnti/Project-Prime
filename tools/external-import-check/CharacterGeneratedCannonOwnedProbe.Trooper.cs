using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

internal static partial class CharacterGeneratedCannonOwnedProbe
{
    private sealed record TrooperCannonCapture(CharacterGeneratedTrooperProfile Profile, string Folder,
        byte[] Receipt, string ReceiptSha, int[][] Cut, Matrix4 Basis, Dictionary<string, string> Files);
    // Literal independent owned-source research, before cannon implementation.
    private static readonly string[][] TrooperCutPins =
    [
        ["b90f19fc1b57908301c12976cb5722bab7e312707fe254082db2a621e068b856", "455d180e5e795a82693126e745bfd18d66f02e2152bb7b60733f4d6654f6eaf9", "3fc2845fd13e710277c4c24d135f5384b6dc734d676c84fe6d73bce5f628c6cd"],
        ["1506d61977b23554d3b894e2a344f2ae4c79d5f0d1b1db376f443e6d968995fd", "3b19a35ef00b95a7a5ae9a0ed5ad47fb7aed1d7a4703d788437d3b8f282a8f21", "108735c6bb6eba2bce69e1de42e160204f0325cb2c5848f2e4a919f0eaf0d0da"],
        ["669de48e99bebe296c8f4840a9168fa455ceca96c702c19a58a9a05540f0695a", "3b19a35ef00b95a7a5ae9a0ed5ad47fb7aed1d7a4703d788437d3b8f282a8f21", "3fc2845fd13e710277c4c24d135f5384b6dc734d676c84fe6d73bce5f628c6cd"],
        ["1506d61977b23554d3b894e2a344f2ae4c79d5f0d1b1db376f443e6d968995fd", "3b19a35ef00b95a7a5ae9a0ed5ad47fb7aed1d7a4703d788437d3b8f282a8f21", "108735c6bb6eba2bce69e1de42e160204f0325cb2c5848f2e4a919f0eaf0d0da"]
    ];

    internal static int RunTrooper(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-trooper-generated-cannon-" + Guid.NewGuid().ToString("N"));
        string imports = Path.Combine(temporary, "imports"), core = typeof(CharacterModelPack).Assembly.Location,
            tool = typeof(CharacterGeneratedCannonOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var caches = Caches();
        string? sourceBefore = null, sourceAfter = null, failed = null, error = null, errorSha = null;
        int checks = 0, refusals = 0; bool cleanup = false, componentsUnchanged = false, cachesUnchanged = false;
        var captures = new List<TrooperCannonCapture>(); var measurements = new List<object>(); var admitted = new List<object>(); var refusalCases = new List<string>();
        var issuedCannons = new List<(CharacterModelAsset Asset, CharacterModelPack.SourcePreviewLease Lease, CharacterSourceRig Rig)>();
        void Check(bool value, string why) { if (!value) { failed = why; throw new InvalidDataException(why); } checks++; }
        void Reject(Action action, string why)
        {
            try { action(); }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or ArgumentException or JsonException)
            { refusals++; refusalCases.Add(why); return; }
            throw new InvalidDataException("Expected Trooper cannon refusal: " + why);
        }
        try
        {
            sourceBefore = HashFile(rvz);
            using var disc = GameCubeDiscReader.Open(rvz);
            Check(disc.GameId == "GM8E01" && disc.Revision == 2 && disc.DiscNumber == 0
                && ExternalSourceRequirements.VerifyKnown(disc).All(r => r.Status == ExternalArchiveStatus.Verified), "Exact owned Prime USA Rev2 and both pinned PAKs required.");
            string[] ids = ["trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma"];
            var output = RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: ids);
            Check(output.Count == 4, "All four Trooper cannon generations export.");
            using var pak = new RetroPakReader(disc.OpenRead("Metroid1.pak"), leaveOpen: false);
            byte[] Resource(uint id) => pak.ReadResource(id);
            byte[] ancs = Resource(0x8169653a), cinf = Resource(0x0c9669bc), animBytes = Resource(0x5528972b);
            Check(Sha(ancs) == CharacterGeneratedTrooperProfile.AncsSha && Sha(cinf) == CharacterGeneratedTrooperProfile.CinfSha
                && Sha(animBytes) == "5b6540fbfdcbf37edd24133b4475468e0f0199a744e3159df2f0b942c498ccc7", "Original ANCS/CINF/gunloop identities are unchanged.");
            var skeleton = RetroCharacterSource.ReadSkeleton(cinf); var anim = Anim.Load(animBytes, false);
            anim.Sample(0, out var rawRotations, out var rawTranslations);
            Check(skeleton.Bones.Count == 35 && skeleton.Bones[3].Parent == 2 && !skeleton.Bones.ContainsKey(2)
                && skeleton.Bones[23].Name == "R_elbow" && skeleton.Bones[34].Name == "R_gun_LCTR" && skeleton.Bones[34].Parent == 23,
                "Complete35 source joints retain root3/externalparent2 and exact elbow/locator ancestry.");
            var worlds = new Dictionary<uint, Matrix4>();
            Matrix4 World(uint id)
            {
                if (worlds.TryGetValue(id, out var found)) return found;
                var b = skeleton.Bones[id]; double[] bind = [b.X, b.Y, b.Z];
                if (skeleton.Bones.TryGetValue(b.Parent, out var parent)) { bind[0] -= parent.X; bind[1] -= parent.Y; bind[2] -= parent.Z; }
                var q = rawRotations.GetValueOrDefault(id); var p = rawTranslations.GetValueOrDefault(id) ?? bind;
                var local = (q == null ? Matrix4.Identity : Matrix4.CreateFromQuaternion(new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3])))
                    * Matrix4.CreateTranslation((float)p[0], (float)p[1], (float)p[2]);
                return worlds[id] = skeleton.Bones.ContainsKey(b.Parent) ? local * World(b.Parent) : local;
            }
            Vector3 Convert(Vector3 p) => new(-p.X, p.Z, p.Y);
            Vector3 elbow = Convert(World(23).Row3.Xyz), tip = Convert(World(34).Row3.Xyz), forward = Vector3.Normalize(tip - elbow);
            Vector3 up = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, forward) * forward), left = Vector3.Cross(up, forward);
            var independentBasis = Matrix4.CreateTranslation(-tip) * new Matrix4(left.X, up.X, forward.X, 0, left.Y, up.Y, forward.Y, 0, left.Z, up.Z, forward.Z, 0, 0, 0, 0, 1);
            for (int profileIndex = 0; profileIndex < ids.Length; profileIndex++)
            {
                string id = ids[profileIndex]; var profile = CharacterGeneratedTrooperProfile.Find(id)!;
                var state = LocalImportBundle.Inspect(imports, id, RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2));
                Check(state.State == ImportBundleState.Ready && state.Receipt != null, id + " actual generation is complete.");
                string folder = Path.GetDirectoryName(state.Files.Single(f => Path.GetFileName(f) == "characters.json"))!;
                Check(Path.GetFileName(folder) == state.Receipt!.Generation && state.Files.All(f => Path.GetDirectoryName(f) == folder), "One immutable component generation required.");
                byte[] receipt = File.ReadAllBytes(Path.Combine(folder, id + ".cannon-source.json")); string receiptSha = Sha(receipt);
                using var parsed = JsonDocument.Parse(receipt); var proof = parsed.RootElement;
                var cut = proof.GetProperty("triangles").EnumerateArray().Select(a => a.EnumerateArray().Select(n => n.GetInt32()).ToArray()).ToArray();
                float[] m = proof.GetProperty("toCannon").EnumerateArray().Select(n => n.GetSingle()).ToArray();
                Check(m.Length == 16 && m.All(float.IsFinite), "Complete finite source cannon basis.");
                var basis = new Matrix4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
                Check(CharacterSourceClip.MatrixDistance(basis, independentBasis) < .0001f, "Cannon basis reproduces from independently opened original zero-phase ANIM/CINF.");
                Check(proof.GetProperty("format").GetInt32() == 2 && proof.GetProperty("sourceCharacter").GetString() == id
                    && proof.GetProperty("clipOrdinal").GetInt32() == 1 && proof.GetProperty("clipResourceId").GetUInt32() == 0x5528972b
                    && proof.GetProperty("clipSeconds").GetDouble() == 0 && proof.GetProperty("sourceElbowId").GetInt32() == 23
                    && proof.GetProperty("sourceParticleLocatorId").GetInt32() == 34, "Trooper authoring receipt retains exact original phase/IDs/profile.");
                foreach (var input in proof.GetProperty("sourceInputs").EnumerateObject())
                {
                    string path = Path.Combine(folder, input.Name); LocalImportBundle.CheckPath(path, imports);
                    Check(state.Files.Contains(path) && HashFile(path) == input.Value.GetString(), "Every receipt input matches committed source generation.");
                }
                var refs = RetroCharacterSource.FindCharacter(ancs, profile.NativeName);
                Check(refs.Cmdl == profile.Cmdl && refs.Cskr == profile.Cskr && refs.Cinf == 0x0c9669bc, "Original profile selects its own source geometry and skin.");
                var raw = Cmdl.Load(Resource, refs.Cmdl, refs.Cskr); var expected = Enumerable.Range(0, 3).Select(_ => new List<int>()).ToArray();
                bool Descends(uint bone)
                {
                    var seen = new HashSet<uint>();
                    while (skeleton.Bones.TryGetValue(bone, out var b)) { Check(seen.Add(bone), "Original source cut ancestry is acyclic."); if (bone == 23) return true; bone = b.Parent; }
                    Check(bone == 2, "Original source ancestry ends at external parent2."); return false;
                }
                for (int face = 0; face < raw.TriangleCount; face++)
                {
                    bool selected = true;
                    for (int corner = 0; corner < 3; corner++) selected &= raw.Skin![raw.Triangles[face * 3 + corner].P].Any(v => v.Weight > 0 && Descends(v.Bone));
                    if (selected) expected[raw.TriangleMaterial[face]].Add(face);
                }
                Check(raw.Version == 2 && raw.TriangleCount == 1970 && raw.Materials.Length == 3
                    && expected.Select(a => a.Count).SequenceEqual(new[] { 10, 32, 164 }) && cut.Length == 3
                    && cut.Select((a, i) => a.SequenceEqual(expected[i])).All(v => v), "Every cannon triangle equals the independent original all3corner/any-positive23-descendant cut.");
                for (int material = 0; material < 3; material++)
                    Check(Sha(JsonSerializer.SerializeToUtf8Bytes(expected[material])) == TrooperCutPins[profileIndex][material], "Original global face IDs retain literal preimplementation research hash.");
                captures.Add(new(profile, folder, receipt, receiptSha, cut, basis, state.Files.ToDictionary(f => f, HashFile)));
                measurements.Add(new { Id = id, ReceiptSha256 = receiptSha, CutIdsJsonSha256 = Sha(JsonSerializer.SerializeToUtf8Bytes(cut)),
                    BasisSha256 = Digest(w => { foreach (float v in m) w.Write(v); }), Triangles = 206, Materials = new[] { 10, 32, 164 },
                    RawSourceBasisVerified = true, LiteralCutIdsVerified = true, PendingRuntimePin = profile.CannonReceiptSha != receiptSha });
            }
            // Bootstrap emits all four independently proved hashes before any closed pin admission.
            Console.WriteLine(JsonSerializer.Serialize(new { Kind = "trooper-generated-cannon-authoring-identities", MetadataOutputOnly = true, Profiles = measurements }));
            foreach (var c in captures)
            {
                var selection = CharacterSourcePreviewSelection.Load(c.Profile.Id, CancellationToken.None, imports);
                var local = CharacterSourcePreviewSelection.LoadForLocalBody(c.Profile.Id, CancellationToken.None, imports);
                Check(selection.NativeHunter == Hunter.Weavel && selection.HasCannon && !selection.RuntimeCannon
                    && local.HasCannon && local.RuntimeCannon && local.Generation == selection.Generation, "Viewer versus explicit local-body cannon capability is distinct.");
                var pack = selection.Pack; var appearance = selection.SelectedAppearance;
                Check(pack.TryResolvePrivate(Hunter.Weavel, appearance, CharacterModelPart.Biped, 0, out var bodyAsset)
                    && pack.TryResolvePrivate(Hunter.Weavel, appearance, CharacterModelPart.ViewModel, 0, out _), "Own profile resolves distinct body and ViewModel assets.");
                Check(pack.TryResolvePrivate(Hunter.Weavel, appearance, CharacterModelPart.ViewModel, 0, out var cannonAsset)
                    && !ReferenceEquals(bodyAsset, cannonAsset) && pack.HasSourceCannon(cannonAsset) && !pack.HasSourceCannon(bodyAsset), "Only exact ViewModel owns cannon capability.");
                Check(pack.TryCreateSourcePreviewLease(bodyAsset, out var bodyLease) && pack.TryCreateSourcePreviewLease(cannonAsset, out _), "Both actual assets issue separate source leases.");
                Check(pack.TryCreateSourcePreviewLease(cannonAsset, out var cannonLease) && bodyLease.CacheKey != cannonLease.CacheKey
                    && ReferenceEquals(bodyLease.SourceMaterials, cannonLease.SourceMaterials) && cannonLease.SourceMaterials != null, "Cannon and body share exact captured material authority with distinct owner/cache identities.");
                var materials = cannonLease.SourceMaterials!; var c1 = c.Profile.Id == "trooper-power" ? new System.Numerics.Vector4(253 / 255f, 239 / 255f, 86 / 255f, 0) : System.Numerics.Vector4.Zero;
                Check(materials.Archive.Sha256 == c.Profile.MaterialSha && materials.Archive.Binding.CharacterId == c.Profile.Id
                    && materials.Programs.Count == 3 && materials.Programs.All(p => p.InitialRegister1 == c1), "Cannon remains bound to its own complete body material archive and exact Power-only C1.");
                Check(materials.Archive.CmdlSha256 == Sha(Resource(c.Profile.Cmdl)) && materials.Archive.CskrSha256 == Sha(Resource(c.Profile.Cskr))
                    && materials.Archive.Textures.All(t => materials.Archive.RawTexture(t.ResourceId).SequenceEqual(Resource(t.ResourceId))),
                    "Cannon body archive retains original CMDL/CSKR identity and every exact original TXTR byte.");
                Check(!pack.TryCreateSourcePreviewLease(bodyAsset with { }, out _) && !pack.TryCreateSourcePreviewLease(cannonAsset with { }, out _), "Copied DTOs cannot mint cannon or body leases.");
                Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset, cannonLease), "cannon-body-lease-swap");
                Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset, bodyLease), "body-cannon-lease-swap");
                var body = CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset, bodyLease); var cannon = CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset, cannonLease);
                Check(body.IndexCount == 1970 * 3 && cannon.IndexCount == 206 * 3 && cannon.SourceRig?.Nodes.Count == 36 && cannon.Joints.Count == 35
                    && cannon.Joints.SequenceEqual(body.Joints) && body.SourceCannon == null && cannon.SourceCannon?.GeneratedTrooper == c.Profile
                    && cannon.SourceCannon.ClipOrdinal == 1 && cannon.SourceCannon.ClipResourceId == 0x5528972b && cannon.SourceCannon.TriangleCount == 206,
                    "Cut retains full35 hierarchy and correct immutable profile/phase without modifying fullbody.");
                var whole = Triangles(body); var cut = Triangles(cannon);
                Check(cut.Keys.Order().SequenceEqual(c.Cut.SelectMany(a => a).Order()) && cut.All(p => whole.TryGetValue(p.Key, out var h) && h == p.Value), "Every retained corner preserves exact captured position/normal/UV0/weights/localpalette/winding.");
                var clip = CharacterSourceClip.Load(cannonLease, cannonAsset, cannon.SourceRig!, 1); var raw = CharacterSourceClip.Load(bodyLease, bodyAsset, body.SourceRig!, 1);
                Check(clip.ResourceId == 0x5528972b && clip.Sample(0).Count == 36 && cannon.SourceAuthoringPose!.SequenceEqual(raw.Sample(0))
                    && clip.Sample(0).SequenceEqual(raw.Sample(0)), "Cannon uses the original full raw source pose, never gameplay RootPin.");
                foreach (int ordinal in new[] { 0, 2, 14, 32, int.MaxValue }) Reject(() => CharacterSourceClip.Load(cannonLease, cannonAsset, cannon.SourceRig!, ordinal), "wrong-cannon-ordinal");
                foreach (double time in new[] { double.Epsilon, -.1, .1, double.MaxValue, double.NaN, double.PositiveInfinity }) Reject(() => clip.Sample(time), "nonzero-or-nonfinite-cannon-phase");
                int reads = CharacterSourceClip.CapturedFileReads; using (var locks = new SourceFileLocks(c.Files.Keys))
                    for (int i = 0; i < 100; i++) Check(clip.Sample(0).SequenceEqual(raw.Sample(0)), "Frozen body/cannon phase sample requires no deferred file IO.");
                Check(CharacterSourceClip.CapturedFileReads == reads, "Repeated fixed phase sampling never opens captured source files.");
                Check(CharacterSourceClip.MatrixDistance(cannon.SourceCannon!.ToCannon, c.Basis) < .00001f, "Loaded source cannon retains independently verified authoring basis.");
                issuedCannons.Add((cannonAsset, cannonLease, cannon.SourceRig!));
                Negative(c);
                admitted.Add(new { c.Profile.Id, Triangles = cut.Count, Joints = cannon.Joints.Count, Batches = cannon.Primitives.Count, OwnMaterialSha256 = materials.Archive.Sha256, C1 = new[] { c1.X, c1.Y, c1.Z, c1.W } });
            }
            for (int i = 0; i < issuedCannons.Count; i++)
            {
                var own = issuedCannons[i]; var foreign = issuedCannons[(i + 1) % issuedCannons.Count];
                Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(own.Asset, foreign.Lease), "foreign-profile-cannon-lease");
                Reject(() => CharacterSourceClip.Load(own.Lease, foreign.Asset, own.Rig, 1), "foreign-profile-clip-owner");
            }
            componentsUnchanged = captures.All(c => c.Files.All(p => HashFile(p.Key) == p.Value)); Check(componentsUnchanged, "All committed input and derived generation bytes remain unchanged.");

            void Negative(TrooperCannonCapture c)
            {
                string mutable = Path.Combine(temporary, "mutable-" + c.Profile.Id); Directory.CreateDirectory(mutable);
                foreach (string path in c.Files.Keys) File.Copy(path, Path.Combine(mutable, Path.GetFileName(path)));
                string recipeName = c.Profile.Id + ".cannon-source.json", manifestPath = Path.Combine(mutable, "characters.json");
                byte[] manifest = File.ReadAllBytes(manifestPath);
                void Load()
                {
                    var p = CharacterModelPack.Load(mutable); var a = new PrivateCharacterAppearance("trooper", c.Profile.Skin);
                    if (!p.TryResolvePrivate(Hunter.Weavel, a, CharacterModelPart.Biped, 0, out var body)
                        || !p.TryResolvePrivate(Hunter.Weavel, a, CharacterModelPart.ViewModel, 0, out var cannon)) throw new InvalidDataException("Exact profile parts required.");
                    p.CaptureSourceMaterials(body, c.Profile.Id); p.CaptureGeneratedCannonMaterials(body, cannon);
                    if (!p.TryCreateSourcePreviewLease(cannon, out var lease)) throw new InvalidDataException("Exact cannon lease required.");
                    _ = CharacterWeightedModelLoader.LoadSourcePreview(cannon, lease);
                }
                Load();
                void Mutate(string name, Action<JsonObject, JsonObject> edit)
                {
                    var recipe = JsonNode.Parse(c.Receipt)!.AsObject(); var doc = JsonNode.Parse(manifest)!.AsObject();
                    var entry = doc["models"]!.AsArray().Single(e => e!["part"]!.GetValue<string>() == "ViewModel")!.AsObject();
                    try
                    {
                        edit(recipe, entry); byte[] changed = JsonSerializer.SerializeToUtf8Bytes(recipe); entry["sourceCannonSha256"] = Sha(changed);
                        File.WriteAllBytes(Path.Combine(mutable, recipeName), changed); File.WriteAllText(manifestPath, doc.ToJsonString()); Reject(Load, c.Profile.Id + ":" + name);
                    }
                    finally { File.WriteAllBytes(Path.Combine(mutable, recipeName), c.Receipt); File.WriteAllBytes(manifestPath, manifest); }
                }
                Mutate("rehashed-cut", (r, _) => r["triangles"]![0]![0] = -1);
                Mutate("rehashed-basis", (r, _) => r["toCannon"]![12] = .01);
                Mutate("wrong-phase", (r, _) => r["clipSeconds"] = .1);
                Mutate("wrong-ordinal", (r, _) => r["clipOrdinal"] = 32);
                Mutate("wrong-resource", (r, _) => r["clipResourceId"] = 0xd12fdfa3u);
                Mutate("wrong-elbow", (r, _) => r["sourceElbowId"] = 24);
                Mutate("wrong-tip", (r, _) => r["sourceParticleLocatorId"] = 32);
                Mutate("wrong-profile", (r, _) => r["sourceCharacter"] = "trooper-other");
                Mutate("wrong-source-basis", (r, _) => r["sourceBasis"] = "native");
                Mutate("false-firstperson-qualification", (r, _) => r["firstPersonVerified"] = true);
                Mutate("wrong-part", (_, e) => e["part"] = "AlternateForm");
                Mutate("wrong-skin", (_, e) => e["skinSource"] = c.Profile.Id == "trooper-power" ? "MP1IceTrooper" : "MP1PowerTrooper");
                foreach (string name in new[] { recipeName, ".source.glb", ".source.json", ".ancs.bin", ".cinf.bin", ".ancs-graph.json", ".animations.bin", ".materials.bin" })
                {
                    string file = name == recipeName ? name : c.Profile.Id + name, path = Path.Combine(mutable, file); byte[] original = File.ReadAllBytes(path), changed = (byte[])original.Clone(); changed[^1] ^= 1;
                    try { File.WriteAllBytes(path, changed); Reject(Load, c.Profile.Id + ":changed-" + name); }
                    finally { File.WriteAllBytes(path, original); }
                }
                var foreign = captures.First(other => other.Profile.Id != c.Profile.Id); string materialPath = Path.Combine(mutable, c.Profile.Id + ".materials.bin"); byte[] ownMaterial = File.ReadAllBytes(materialPath);
                try { File.Copy(Path.Combine(foreign.Folder, foreign.Profile.Id + ".materials.bin"), materialPath, true); Reject(Load, "foreign-body-material-authority"); }
                finally { File.WriteAllBytes(materialPath, ownMaterial); }
                Load();
                File.Delete(materialPath);
                try { Reject(Load, "missing-body-material-authority"); }
                finally { File.WriteAllBytes(materialPath, ownMaterial); }
                var retained = CharacterModelPack.Load(mutable); var appearance = new PrivateCharacterAppearance("trooper", c.Profile.Skin);
                Check(retained.TryResolvePrivate(Hunter.Weavel, appearance, CharacterModelPart.Biped, 0, out var retainedBody)
                    && retained.TryResolvePrivate(Hunter.Weavel, appearance, CharacterModelPart.ViewModel, 0, out _), "Restored negative fixture resolves both actual assets.");
                Check(retained.TryResolvePrivate(Hunter.Weavel, appearance, CharacterModelPart.ViewModel, 0, out var retainedCannon), "Restored fixture resolves cannon owner.");
                retained.CaptureGeneratedCannonMaterials(retainedBody, retainedCannon);
                Check(retained.TryCreateSourcePreviewLease(retainedCannon, out var retainedLease), "Restored fixture issues current cannon lease.");
                var retainedModel = CharacterWeightedModelLoader.LoadSourcePreview(retainedCannon, retainedLease);
                var retainedClip = CharacterSourceClip.Load(retainedLease, retainedCannon, retainedModel.SourceRig!, 1);
                File.Delete(Path.Combine(mutable, recipeName));
                Check(retainedClip.Sample(0).Count == 36, "Already captured fixed pose performs no deferred proof IO after receipt removal.");
                Reject(() => retainedLease.Require(retainedCannon), "explicit-proof-detects-missing-cannon-receipt");
                Reject(() => retainedClip.Sample(0), "retired-cannon-clip-never-resurrects"); Reject(Load, "missing-cannon-receipt");
            }
        }
        catch (Exception ex) { error = ex.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(ex.Message)); Console.Error.WriteLine("Trooper cannon CPU failure: " + ex); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); cleanup = !Directory.Exists(temporary); } catch (Exception ex) { error ??= ex.GetType().Name; }
            try { sourceAfter = HashFile(rvz); } catch (Exception ex) { error ??= ex.GetType().Name; }
            var final = Caches(); cachesUnchanged = caches.All(p => final[p.Key].SequenceEqual(p.Value));
        }
        bool passed = error == null && admitted.Count == 4 && componentsUnchanged && cleanup && sourceBefore == sourceAfter
            && HashFile(core) == coreBefore && HashFile(tool) == toolBefore && cachesUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = passed, Checks = checks, Refusals = refusals, FailedCheck = failed, ErrorType = error, ErrorMessageSha256 = errorSha,
            CpuOnly = true, RenderingActivated = false, GameplayActivated = false, Profiles = measurements, AdmittedProfiles = admitted, RefusalCases = refusalCases,
            SourceSha256Before = sourceBefore, SourceSha256After = sourceAfter, CoreSha256 = coreBefore, ToolSha256 = toolBefore,
            ComponentsUnchanged = componentsUnchanged, CachesUnchanged = cachesUnchanged, TemporaryDerivedPayloadsDeleted = cleanup }));
        return passed ? 0 : 1;
    }

    private sealed class SourceFileLocks : IDisposable
    {
        private readonly List<FileStream> _files = new();
        internal SourceFileLocks(IEnumerable<string> paths)
        { try { foreach (string path in paths) _files.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)); } catch { Dispose(); throw; } }
        public void Dispose() { foreach (var file in _files) file.Dispose(); }
    }
}
