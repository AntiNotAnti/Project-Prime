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
    internal static int RunFinal(string rvz)
    {
        const string finalId = "darksamus-overcharged-echoes", normalId = "darksamus-echoes";
        const string fullCutPin = "3cfbcc8781cc8c08c5cfa96af29745975f1ccaf35e063a9f456d5afffc499ded";
        const string trimmedCutPin = "7a91aae9dbe748d23143b8c2ea655435df1db1403ec59fb84a43242577cdd594";
        const string removedPin = "30715234934fe9b6f379ec456f1d4d5fba7d022ef2b0fcf7d57a65474a110e11";
        string temporary = Path.Combine(Path.GetTempPath(), "prime-final-generated-cannon-" + Guid.NewGuid().ToString("N"));
        string imports = Path.Combine(temporary, "imports"), core = typeof(CharacterModelPack).Assembly.Location, tool = typeof(CharacterGeneratedCannonOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var caches = Caches();
        string? before = null, after = null, failed = null, error = null, errorSha = null, receiptSha = null, generation = null;
        int checks = 0, refusals = 0; bool cleanup = false, unchanged = false, cachesUnchanged = false, admitted = false;
        var refusalCases = new List<string>(); object? metadata = null;
        void Check(bool value, string why) { if (!value) { failed = why; throw new InvalidDataException(why); } checks++; }
        void Reject(Action action, string why)
        {
            try { action(); }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or ArgumentException or JsonException)
            { refusals++; refusalCases.Add(why); return; }
            throw new InvalidDataException("Expected Final cannon refusal: " + why);
        }
        try
        {
            before = HashFile(rvz);
            using (var disc = GameCubeDiscReader.Open(rvz))
                Check(disc.GameId == "G2ME01" && disc.Revision == 0 && disc.DiscNumber == 0
                    && ExternalSourceRequirements.VerifyKnown(disc).All(r => r.Status == ExternalArchiveStatus.Verified), "Exact owned Echoes USA and three pinned source archives required.");
            var outputs = RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: [finalId, normalId]);
            Check(outputs.Count == 2, "Independent normal and Final immutable generations export.");
            var state = LocalImportBundle.Inspect(imports, finalId, RetroCharacterPreviewExporter.BundleSpec(finalId));
            var normalState = LocalImportBundle.Inspect(imports, normalId, RetroCharacterPreviewExporter.BundleSpec(normalId));
            Check(state.State == ImportBundleState.Ready && normalState.State == ImportBundleState.Ready && state.Receipt != null && normalState.Receipt != null, "Both independent source generations are complete.");
            string folder = Path.GetDirectoryName(state.Files.Single(f => Path.GetFileName(f) == "characters.json"))!;
            string normalFolder = Path.GetDirectoryName(normalState.Files.Single(f => Path.GetFileName(f) == "characters.json"))!;
            generation = state.Receipt!.Generation;
            Check(Path.GetFileName(folder) == generation && state.Files.All(f => Path.GetDirectoryName(f) == folder), "Final cannon and body components share one committed generation.");
            var sourceFiles = state.Files.Concat(normalState.Files).ToDictionary(f => f, HashFile);
            string recipeName = finalId + ".cannon-source.json"; byte[] receipt = File.ReadAllBytes(Path.Combine(folder, recipeName)); receiptSha = Sha(receipt);
            using var parsed = JsonDocument.Parse(receipt); var proof = parsed.RootElement;
            int[][] declared = proof.GetProperty("triangles").EnumerateArray().Select(a => a.EnumerateArray().Select(n => n.GetInt32()).ToArray()).ToArray();
            float[] matrix = proof.GetProperty("toCannon").EnumerateArray().Select(n => n.GetSingle()).ToArray();
            Check(matrix.Length == 16 && matrix.All(float.IsFinite), "Final authoring basis is complete and finite.");
            var basis = new Matrix4(matrix[0],matrix[1],matrix[2],matrix[3],matrix[4],matrix[5],matrix[6],matrix[7],matrix[8],matrix[9],matrix[10],matrix[11],matrix[12],matrix[13],matrix[14],matrix[15]);
            Check(proof.GetProperty("format").GetInt32() == 2 && proof.GetProperty("sourceCharacter").GetString() == finalId
                && proof.GetProperty("clipOrdinal").GetInt32() == 32 && proof.GetProperty("clipResourceId").GetUInt32() == 0xd12fdfa3
                && proof.GetProperty("clipSeconds").GetDouble() == 0 && proof.GetProperty("sourceElbowId").GetInt32() == 24
                && proof.GetProperty("sourceParticleLocatorId").GetInt32() == 32, "Final recipe has its own identity and exact raw authoring clip/phase/locators.");
            Check(receiptSha == "46c828de8c0c7f6ec6102216400891c30475036bc2aa7da0f7b6b752abf0752c"
                && receiptSha == CharacterSourceCannonRecipe.QualifiedFinalReceiptSha256, "Independent Final receipt is exactly pinned, never normal's receipt.");
            foreach (var input in proof.GetProperty("sourceInputs").EnumerateObject())
            {
                string path = Path.Combine(folder, input.Name); LocalImportBundle.CheckPath(path, imports);
                Check(state.Files.Contains(path) && HashFile(path) == input.Value.GetString(), "Final receipt binds only its own committed source generation.");
            }
            string fullCutSha, trimmedCutSha, removedSha, independentBasisSha;
            using (var resources = new OwnedResources(rvz))
            {
                byte[] ancs = resources.Read(0x39900a11); var refs = RetroCharacterSource.FindCharacter(ancs, "Battle_3");
                Check(refs.Cmdl == 0xfaa12c05 && refs.Cskr == 0x918e83d3 && refs.Cinf == 0x4abc8a50, "Final source geometry/skin are distinct from normal.");
                byte[] cmdl = resources.Read(refs.Cmdl), cskr = resources.Read(refs.Cskr), cinf = resources.Read(refs.Cinf), animBytes = resources.Read(0xd12fdfa3);
                Check(Sha(cmdl) == "f49d5a84271eb08ed780170887e4301af428303c24fda87a55ac60cf865b17db"
                    && Sha(cskr) == "ae7f5ddf6bef6c9c8aa5903757e7e6ba8e0bd70693806898d7e6f8531dc646a1"
                    && Sha(animBytes) == "4bdb85ff6b36f10c5b330158fa7968f2af9203f293c00b8fb7796d32306ed197", "Original Final CMDL/CSKR and shared rigid ANIM match preimplementation source pins.");
                var raw = Cmdl.Load(resources.Read, refs.Cmdl, refs.Cskr); var skeleton = RetroCharacterSource.ReadSkeleton(cinf);
                Check(raw.TriangleCount == 12734 && raw.Materials.Length == 9 && skeleton.Bones.Count == 39
                    && skeleton.Bones[0].Parent == 97 && !skeleton.Bones.ContainsKey(97), "Final retains complete9 materials and39 source joints rooted at0/external97.");
                bool Under(uint bone)
                {
                    var seen = new HashSet<uint>();
                    while (skeleton.Bones.TryGetValue(bone, out var b)) { if (!seen.Add(bone)) throw new InvalidDataException("Raw ancestry cycle."); if (bone == 24) return true; bone = b.Parent; }
                    Check(bone == 97, "Raw Final source ancestry remains complete."); return false;
                }
                bool Drop(int face, bool wrongFlip = false)
                {
                    if (raw.TriangleMaterial[face] != 5) return false;
                    return Enumerable.Range(0, 3).Any(c =>
                    {
                        int t = raw.Triangles[face * 3 + c].T0; if (t < 0) return false;
                        float u = raw.Uv0[t * 2], v = raw.Uv0[t * 2 + 1];
                        return MathF.Abs(u - .489f) < .002f && MathF.Abs((wrongFlip ? 1 - v : v) - .764f) < .002f;
                    });
                }
                var full = Enumerable.Range(0, 9).Select(_ => new List<int>()).ToArray();
                var trimmed = Enumerable.Range(0, 9).Select(_ => new List<int>()).ToArray(); var removed = new List<int>(); int wrongFlipped = 0;
                for (int face = 0; face < raw.TriangleCount; face++)
                {
                    bool selected = Enumerable.Range(0, 3).All(c => raw.Skin![raw.Triangles[face * 3 + c].P].Any(v => v.Weight > 0 && Under(v.Bone)));
                    if (!selected) continue; int material = raw.TriangleMaterial[face]; full[material].Add(face);
                    if (Drop(face)) removed.Add(face); else trimmed[material].Add(face);
                    if (Drop(face, true)) wrongFlipped++;
                }
                fullCutSha = Sha(JsonSerializer.SerializeToUtf8Bytes(full)); trimmedCutSha = Sha(JsonSerializer.SerializeToUtf8Bytes(trimmed)); removedSha = Sha(JsonSerializer.SerializeToUtf8Bytes(removed));
                Check(full.Sum(a => a.Count) == 805 && fullCutSha == fullCutPin && removed.Count == 12 && removedSha == removedPin
                    && wrongFlipped == 0 && trimmedCutSha == trimmedCutPin && trimmed.Select(a => a.Count).SequenceEqual(new[] { 0,0,0,392,66,314,0,21,0 })
                    && declared.Length == 9 && declared.Select((a, i) => a.SequenceEqual(trimmed[i])).All(v => v),
                    "Exact independent whole-source805→793 trim removes only the12 pinned rawUV material5 faces; flippedV removes none.");
                var anim = Anim.Load(animBytes, true); anim.Sample(0, out var rotations, out var translations); var worlds = new Dictionary<uint, Matrix4>();
                Matrix4 World(uint id)
                {
                    if (worlds.TryGetValue(id, out var found)) return found; var b = skeleton.Bones[id]; double[] bind = [b.X, b.Y, b.Z];
                    if (skeleton.Bones.TryGetValue(b.Parent, out var parent)) { bind[0] -= parent.X; bind[1] -= parent.Y; bind[2] -= parent.Z; }
                    var q = rotations.GetValueOrDefault(id); var p = translations.GetValueOrDefault(id) ?? bind;
                    var local = (q == null ? Matrix4.Identity : Matrix4.CreateFromQuaternion(new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]))) * Matrix4.CreateTranslation((float)p[0], (float)p[1], (float)p[2]);
                    return worlds[id] = skeleton.Bones.ContainsKey(b.Parent) ? local * World(b.Parent) : local;
                }
                Vector3 Convert(Vector3 p) => new(-p.X, p.Z, p.Y);
                Vector3 elbow = Convert(World(24).Row3.Xyz), tip = Convert(World(32).Row3.Xyz), forward = Vector3.Normalize(tip - elbow);
                Vector3 up = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, forward) * forward), left = Vector3.Cross(up, forward);
                var independent = Matrix4.CreateTranslation(-tip) * new Matrix4(left.X,up.X,forward.X,0,left.Y,up.Y,forward.Y,0,left.Z,up.Z,forward.Z,0,0,0,0,1);
                independentBasisSha = Digest(w => { foreach (float v in Values(independent)) w.Write(v); });
                Check(CharacterSourceClip.MatrixDistance(basis, independent) < .0001f && Vector3.TransformPosition(tip, basis).Length < .00001f,
                    "Final authoring basis independently reproduces original zero-phase source elbow and muzzle.");
            }
            metadata = new { Id = finalId, ReceiptSha256 = receiptSha, FullSourceCutSha256 = fullCutSha, TrimmedCutSha256 = trimmedCutSha, RemovedIdsSha256 = removedSha,
                WholeSourceTriangles = 805, CannonTriangles = 793, RemovedTriangles = 12, MaterialTriangles = new[] { 0,0,0,392,66,314,0,21,0 }, IndependentBasisSha256 = independentBasisSha };
            Console.WriteLine(JsonSerializer.Serialize(new { Kind = "final-generated-cannon-authoring-identities", MetadataOutputOnly = true, Profile = metadata }));
            var selection = CharacterSourcePreviewSelection.Load(finalId, CancellationToken.None, imports); var local = CharacterSourcePreviewSelection.LoadForLocalBody(finalId, CancellationToken.None, imports);
            var normal = CharacterSourcePreviewSelection.LoadForLocalBody(normalId, CancellationToken.None, imports);
            Check(selection.HasCannon && !selection.RuntimeCannon && local.HasCannon && local.RuntimeCannon && selection.Generation == local.Generation, "Final viewer authoring capability is distinct from explicit local-body cannon capability.");
            var pack = selection.Pack; var appearance = selection.SelectedAppearance;
            Check(pack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var bodyAsset)
                && pack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.ViewModel, 0, out _), "Final owns distinct exact body/ViewModel assets.");
            Check(pack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.ViewModel, 0, out var cannonAsset) && pack.HasSourceCannon(cannonAsset) && !pack.HasSourceCannon(bodyAsset), "Only Final ViewModel grants its own cannon capability.");
            Check(pack.TryCreateSourcePreviewLease(bodyAsset, out var bodyLease) && pack.TryCreateSourcePreviewLease(cannonAsset, out _), "Final body and cannon issue separate leases.");
            Check(pack.TryCreateSourcePreviewLease(cannonAsset, out var cannonLease) && bodyLease.CacheKey != cannonLease.CacheKey
                && ReferenceEquals(bodyLease.SourceMaterials, cannonLease.SourceMaterials) && cannonLease.SourceMaterials != null, "Final cannon shares exact ownbody raw material authority, with independent lease/cache identity.");
            var materials = cannonLease.SourceMaterials!;
            Check(materials.Archive.Binding.CharacterId == finalId && materials.Archive.Sha256 == "69d27a8e4867b4ec50084260a6fb570936e79edb7b08ad2bbd04a58d5204df8d"
                && materials.Programs.Count == 9 && materials.Programs.All(p => p.InitialRegister1 == System.Numerics.Vector4.Zero), "All9 Final source material programs remain exact ownbody authority and zero initial C1.");
            string[] baseShaders = materials.Programs.Select(p => p.Vertex120 + p.Vertex300 + p.Fragment120 + p.Fragment300).ToArray();
            var variant = materials.ForFinalCannon(); var otherVariant = materials.ForFinalCannon(); int positiveAlphaSamples = 0;
            Check(!ReferenceEquals(materials, variant) && !ReferenceEquals(variant, otherVariant)
                && ReferenceEquals(materials.Archive, variant.Archive) && ReferenceEquals(materials.Images, variant.Images)
                && !ReferenceEquals(materials.Programs, variant.Programs) && variant.Programs.Count == 9
                && variant.Policy != materials.Policy, "Final compile variant has independent program/policy ownership and shares exact immutable body archive/images.");
            for (int ordinal = 0; ordinal < 9; ordinal++)
            {
                var original = materials.Programs[ordinal]; var changed = variant.Programs[ordinal];
                bool alpha = original.DrawPass == CharacterSourceGxProgram.Pass.Alpha;
                Check(original.PresentationAlphaScale == 1 && changed.PresentationAlphaScale == (alpha ? .55f : 1)
                    && !ReferenceEquals(original, changed) && original.DrawPass == changed.DrawPass
                    && original.Stages.SequenceEqual(changed.Stages) && original.Textures.SequenceEqual(changed.Textures)
                    && original.Generators.SequenceEqual(changed.Generators) && original.Animations.SequenceEqual(changed.Animations)
                    && original.InitialRegister1 == changed.InitialRegister1 && original.InitialRegisterDependencies == changed.InitialRegisterDependencies,
                    "Variant preserves every TEV stage/dependency/texture/UV input, changing only Alpha-pass presentation scale.");
                Check(original.Vertex120 == changed.Vertex120 && original.Vertex300 == changed.Vertex300
                    && (alpha ? changed.Fragment120.Replace("*0.55", "", StringComparison.Ordinal) == original.Fragment120
                        && changed.Fragment300.Replace("*0.55", "", StringComparison.Ordinal) == original.Fragment300
                        && changed.Fragment120 != original.Fragment120 && changed.Fragment300 != original.Fragment300
                        : changed.Fragment120 == original.Fragment120 && changed.Fragment300 == original.Fragment300),
                    "Desktop and ES variant GLSL alter only final output alpha for Alpha passes; geometry/RGB/additive/opaque source remain exact.");
                foreach (float textureAlpha in new[] { 0f,.25f,.75f,1f })
                {
                    var samples = Enumerable.Repeat(new System.Numerics.Vector4(.2f,.4f,.7f,textureAlpha), original.Stages.Count).ToArray();
                    var raster = new System.Numerics.Vector4(.3f,.6f,.8f,.9f); var a = original.Evaluate(samples, raster); var b = changed.Evaluate(samples, raster);
                    Check(a.X == b.X && a.Y == b.Y && a.Z == b.Z && MathF.Abs(b.W - a.W * (alpha ? .55f : 1)) < .000001f,
                        "Independent before/after TEV CPU outputs preserve RGB and scale only Alpha-pass output once.");
                    if (alpha && a.W > 0) positiveAlphaSamples++;
                }
                if (!alpha) Reject(() => CharacterSourceGxProgram.Compile(materials.Archive.ReadProgram(ordinal), presentationAlphaScale:.55f), "non-Alpha-pass-cannot-scale-presentation");
            }
            Check(positiveAlphaSamples > 0 && materials.Programs.Select(p => p.Vertex120 + p.Vertex300 + p.Fragment120 + p.Fragment300).SequenceEqual(baseShaders)
                && materials.Programs.All(p => p.PresentationAlphaScale == 1), "Nonzero alpha proves effect while repeated variant compilation leaves original body programs byte-identical.");
            Reject(() => variant.ForFinalCannon(), "Final-variant-cannot-double-apply-alpha");
            int alphaOrdinal = Enumerable.Range(0, 9).First(i => materials.Programs[i].DrawPass == CharacterSourceGxProgram.Pass.Alpha);
            foreach (float invalid in new[] { -.1f,1.01f,float.NaN,float.PositiveInfinity })
                Reject(() => CharacterSourceGxProgram.Compile(materials.Archive.ReadProgram(alphaOrdinal), presentationAlphaScale:invalid), "invalid-Final-presentation-alpha");
            using (var resources = new OwnedResources(rvz)) Check(materials.Archive.Textures.All(t => materials.Archive.RawTexture(t.ResourceId).SequenceEqual(resources.Read(t.ResourceId))), "Every Final body TXTR remains byte-identical for cannon materials.");
            Check(!pack.TryCreateSourcePreviewLease(bodyAsset with { }, out _) && !pack.TryCreateSourcePreviewLease(cannonAsset with { }, out _), "Copied body/cannon DTOs cannot mint authority.");
            Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset, cannonLease), "Final-cannon-body-lease-swap"); Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset, bodyLease), "Final-body-cannon-lease-swap");
            var body = CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset, bodyLease); var cannon = CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset, cannonLease);
            Check(body.IndexCount == 12734 * 3 && cannon.IndexCount == 793 * 3 && cannon.SourceRig?.Nodes.Count == 40 && cannon.Joints.Count == 39 && cannon.Joints.SequenceEqual(body.Joints)
                && body.SourceCannon == null && cannon.SourceCannon is { IsGenerated: true, GeneratedFinal: true, ClipOrdinal: 32, ClipResourceId: 0xd12fdfa3, TriangleCount: 793 }, "Final own793 cut preserves whole39 rig and fixed phase without changing fullbody.");
            var whole = Triangles(body); var cut = Triangles(cannon);
            Check(cut.Keys.Order().SequenceEqual(declared.SelectMany(a => a).Order()) && cut.All(p => whole.TryGetValue(p.Key, out var h) && h == p.Value), "Final retained whole source IDs preserve every captured corner/UV0/weight/palette/winding.");
            var clip = CharacterSourceClip.Load(cannonLease, cannonAsset, cannon.SourceRig!, 32); var bodyClip = CharacterSourceClip.Load(bodyLease, bodyAsset, body.SourceRig!, 32);
            Check(cannon.SourceAuthoringPose!.SequenceEqual(bodyClip.Sample(0)) && clip.Sample(0).SequenceEqual(bodyClip.Sample(0)), "Final cannon authoring pose remains complete original raw time0.");
            foreach (int ordinal in new[] { 0,1,31,33,int.MaxValue }) Reject(() => CharacterSourceClip.Load(cannonLease, cannonAsset, cannon.SourceRig!, ordinal), "foreign-Final-cannon-clip");
            foreach (double time in new[] { double.Epsilon,-.1,.1,double.NaN,double.PositiveInfinity }) Reject(() => clip.Sample(time), "nonzero-or-nonfinite-Final-phase");
            int reads = CharacterSourceClip.CapturedFileReads;
            using (var locks = new SourceFileLocks(state.Files)) for (int i = 0; i < 100; i++) Check(clip.Sample(0).SequenceEqual(bodyClip.Sample(0)), "Captured Final fixed pose samples with every generation file exclusively locked.");
            Check(reads == CharacterSourceClip.CapturedFileReads, "Frozen Final clip performs no lazy source IO.");
            Check(normal.Pack.TryResolvePrivate(Hunter.Samus, normal.SelectedAppearance, CharacterModelPart.ViewModel, 0, out var normalAsset)
                && normal.Pack.TryCreateSourcePreviewLease(normalAsset, out _), "Alias negative fixture resolves independently valid normal cannon.");
            Check(normal.Pack.TryCreateSourcePreviewLease(normalAsset, out var normalLease), "Normal source cannon owner is valid before cross-profile swaps.");
            Check(normalLease.SourceMaterials != null, "Normal alias fixture has genuine normal material authority.");
            Reject(() => normalLease.SourceMaterials!.ForFinalCannon(), "normal-material-authority-cannot-mint-Final-variant");
            Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset, normalLease), "Final-rejects-normal-lease"); Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(normalAsset, cannonLease), "normal-rejects-Final-lease");
            Reject(() => pack.CaptureGeneratedCannonMaterials(normalAsset, cannonAsset), "Final-rejects-normal-body-material-authority");
            Check(CharacterSourceClip.MatrixDistance(cannon.SourceCannon!.ToCannon, basis) < .00001f, "Loaded Final basis matches its own independently verified receipt.");

            string mutable = Path.Combine(temporary, "mutable"); Directory.CreateDirectory(mutable); foreach (string path in state.Files) File.Copy(path, Path.Combine(mutable, Path.GetFileName(path)));
            byte[] manifest = File.ReadAllBytes(Path.Combine(mutable, "characters.json"));
            void Load()
            {
                var p = CharacterModelPack.Load(mutable);
                if (!p.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var b) || !p.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.ViewModel, 0, out var c)) throw new InvalidDataException("Exact Final source parts required.");
                p.CaptureSourceMaterials(b, finalId); p.CaptureGeneratedCannonMaterials(b, c);
                if (!p.TryCreateSourcePreviewLease(c, out var lease)) throw new InvalidDataException("Exact Final lease required."); _ = CharacterWeightedModelLoader.LoadSourcePreview(c, lease);
            }
            void Mutate(string label, Action<JsonObject, JsonObject> edit)
            {
                var recipe = JsonNode.Parse(receipt)!.AsObject(); var doc = JsonNode.Parse(manifest)!.AsObject(); var e = doc["models"]!.AsArray().Single(n => n!["part"]!.GetValue<string>() == "ViewModel")!.AsObject();
                try { edit(recipe, e); byte[] changed = JsonSerializer.SerializeToUtf8Bytes(recipe); e["sourceCannonSha256"] = Sha(changed); File.WriteAllBytes(Path.Combine(mutable, recipeName), changed); File.WriteAllText(Path.Combine(mutable, "characters.json"), doc.ToJsonString()); Reject(Load, label); }
                finally { File.WriteAllBytes(Path.Combine(mutable, recipeName), receipt); File.WriteAllBytes(Path.Combine(mutable, "characters.json"), manifest); }
            }
            Load();
            Mutate("rehashed-Final-cut", (r, _) => r["triangles"]![5]![0] = -1); Mutate("rehashed-Final-basis", (r, _) => r["toCannon"]![12] = .01);
            Mutate("Final-cannot-alias-normal-source", (r, _) => r["sourceCharacter"] = normalId); Mutate("Final-cannot-alias-normal-skin", (_, e) => e["skinSource"] = "MP2DarkSamus");
            Mutate("wrong-Final-part", (_, e) => e["part"] = "AlternateForm"); Mutate("wrong-Final-phase", (r, _) => r["clipSeconds"] = .1); Mutate("wrong-Final-ordinal", (r, _) => r["clipOrdinal"] = 1);
            Mutate("wrong-Final-resource", (r, _) => r["clipResourceId"] = 0x5528972bu); Mutate("wrong-Final-elbow", (r, _) => r["sourceElbowId"] = 23); Mutate("wrong-Final-tip", (r, _) => r["sourceParticleLocatorId"] = 34);
            Mutate("false-Final-gameplay-qualification", (r, _) => r["gameplayMuzzleChanged"] = true);
            foreach (string name in new[] { recipeName,finalId+".source.glb",finalId+".source.json",finalId+".ancs.bin",finalId+".cinf.bin",finalId+".ancs-graph.json",finalId+".animations.bin",finalId+".materials.bin" })
            {
                string path = Path.Combine(mutable, name); byte[] saved = File.ReadAllBytes(path), changed = (byte[])saved.Clone(); changed[^1] ^= 1;
                try { File.WriteAllBytes(path, changed); Reject(Load, "changed-own-Final-" + name); } finally { File.WriteAllBytes(path, saved); }
            }
            foreach (var (ownName, foreignName) in new[] { (recipeName,normalId+".cannon-source.json"),(finalId+".materials.bin",normalId+".materials.bin"),(finalId+".source.glb",normalId+".source.glb") })
            {
                string path = Path.Combine(mutable, ownName); byte[] saved = File.ReadAllBytes(path);
                try { File.Copy(Path.Combine(normalFolder, foreignName), path, true); Reject(Load, "normal-cannot-substitute-Final-" + ownName); } finally { File.WriteAllBytes(path, saved); }
            }
            Load(); var retained = CharacterModelPack.Load(mutable);
            Check(retained.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var retainedBody) && retained.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.ViewModel, 0, out _), "Restored Final fixture resolves exact source owner.");
            Check(retained.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.ViewModel, 0, out var retainedCannon), "Restored Final cannon resolves.");
            retained.CaptureSourceMaterials(retainedBody, finalId); retained.CaptureGeneratedCannonMaterials(retainedBody, retainedCannon);
            Check(retained.TryCreateSourcePreviewLease(retainedCannon, out var retainedLease), "Restored Final cannon lease is admissible.");
            var retainedModel = CharacterWeightedModelLoader.LoadSourcePreview(retainedCannon, retainedLease); var retainedClip = CharacterSourceClip.Load(retainedLease, retainedCannon, retainedModel.SourceRig!, 32);
            File.Delete(Path.Combine(mutable, recipeName)); Check(retainedClip.Sample(0).Count == 40, "Captured Final phase does not defer proof IO after receipt removal.");
            Reject(() => retainedLease.Require(retainedCannon), "explicit-proof-detects-missing-Final-receipt"); Reject(() => retainedClip.Sample(0), "retired-Final-clip-never-resurrects"); Reject(Load, "missing-Final-receipt");
            unchanged = sourceFiles.All(p => HashFile(p.Key) == p.Value); Check(unchanged, "All committed normal and Final component bytes remain unchanged after isolated negative fixtures."); admitted = true;
        }
        catch (Exception ex) { error = ex.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(ex.Message)); Console.Error.WriteLine("Final cannon CPU failure: " + ex); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); cleanup = !Directory.Exists(temporary); } catch (Exception ex) { error ??= ex.GetType().Name; }
            try { after = HashFile(rvz); } catch (Exception ex) { error ??= ex.GetType().Name; }
            var final = Caches(); cachesUnchanged = caches.All(p => final[p.Key].SequenceEqual(p.Value));
        }
        bool passed = error == null && admitted && unchanged && cleanup && before == after && HashFile(core) == coreBefore && HashFile(tool) == toolBefore && cachesUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = passed, Checks = checks, Refusals = refusals, FailedCheck = failed, ErrorType = error, ErrorMessageSha256 = errorSha,
            CpuOnly = true, RenderingActivated = false, GameplayActivated = false, LightingParityQualified = false, Profile = metadata, Generation = generation, ReceiptSha256 = receiptSha,
            SourceSha256Before = before, SourceSha256After = after, CoreSha256 = coreBefore, ToolSha256 = toolBefore, ComponentsUnchanged = unchanged, CachesUnchanged = cachesUnchanged,
            TemporaryDerivedPayloadsDeleted = cleanup, RefusalCases = refusalCases }));
        return passed ? 0 : 1;
    }
}
