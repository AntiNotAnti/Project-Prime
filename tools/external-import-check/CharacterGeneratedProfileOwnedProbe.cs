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

// Explicit CPU-only owned-source profile/authority acceptance. All mutations
// happen in disposable copies, never a committed source generation.
internal static class CharacterGeneratedProfileOwnedProbe
{
    private const string Id = "darksamus-echoes";
    private const string GlbSha = "38b03806faf22851efd3caeb4fcd22b900556b5fc4b21a2051b075b63f6fb8ca";
    internal static int Run(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-generated-profile-" + Guid.NewGuid().ToString("N"));
        string core = typeof(RetroCharacterPreviewExporter).Assembly.Location, tool = typeof(CharacterGeneratedProfileOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var caches = Caches();
        string? before = null, after = null, errorType = null, errorSha = null, generation = null, provenanceSha = null;
        bool cleaned = false, cachesUnchanged = false, retained = false, reopenRefused = false, downgradeRefused = false;
        int checks = 0, refusals = 0, clips = 0, nodes = 0, joints = 0, triangles = 0;
        var cases = new List<string>(); var inputs = new List<object>();
        void Check(bool value, string why) { if (!value) throw new InvalidDataException(why); checks++; }
        void Reject(Action work, string why)
        { try { work(); } catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException) { refusals++; cases.Add(why); return; } throw new InvalidDataException("Expected profile refusal: " + why); }
        try
        {
            before = HashFile(rvz);
            using (var disc = GameCubeDiscReader.Open(rvz))
                Check(disc.GameId == "G2ME01" && disc.Revision == 0 && disc.DiscNumber == 0, "Pinned Echoes source edition required.");
            Directory.CreateDirectory(temporary); string imports = Path.Combine(temporary, "imports");
            var exported = RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: new[] { Id });
            var spec = RetroCharacterPreviewExporter.BundleSpec(Id); var state = LocalImportBundle.Inspect(imports, Id, spec);
            Check(exported.Count == 1 && state.State == ImportBundleState.Ready && state.Receipt != null, "Product importer must publish the normal complete recipe.");
            generation = state.Receipt!.Generation; string folder = Path.GetDirectoryName(exported.Single())!;
            Check(Path.GetFileName(folder) == generation && state.Files.All(p => Path.GetDirectoryName(p) == folder), "Every component must share one immutable generation.");
            var selection = CharacterSourcePreviewSelection.Load(Id, CancellationToken.None, imports); clips = selection.Clips.Count;
            Check(selection.Generation == generation && clips > 0 && selection.Clips.Any(c => c.Ordinal > 12)
                && selection.Clips.Select(c => c.Ordinal).Distinct().Count() == clips,
                "Product selector captures the real generated generation and complete declared clip choices.");
            Check(selection.Pack.TryResolvePrivate(Hunter.Samus, CharacterSourcePreviewSelection.Appearance, CharacterModelPart.Biped, 0, out var asset)
                && selection.Pack.TryCreateSourcePreviewLease(asset, out _), "Captured selection uses the actual private pack owner.");
            Check(selection.Pack.TryCreateSourcePreviewLease(asset, out var lease) && lease.ModelSha256.Equals(GlbSha, StringComparison.OrdinalIgnoreCase), "Generated source GLB must match the independent output pin.");
            var model = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease); var rig = model.SourceRig!;
            nodes = rig.Nodes.Count; joints = model.Joints.Count; triangles = model.IndexCount / 3;
            Check(nodes == 40 && joints == 39 && triangles == 6340
                && model.Primitives.SelectMany(p => p.SourceTriangleIds ?? Array.Empty<int>()).Order().SequenceEqual(Enumerable.Range(0, 6340)),
                "Actual loader retains complete rig and every identified original source triangle.");
            int ordinal = selection.Clips[0].Ordinal; var clip = CharacterSourceClip.Load(lease, asset, rig, ordinal);
            var first = clip.Sample(0); var sample = clip.Sample(selection.Clips[0].Duration * .37);
            Check(rig.Evaluate(sample, model.Joints).Length == 39 && first.Count == 40, "Actual imported temporal adapter evaluates the complete rig.");
            Check(!selection.Pack.TryCreateSourcePreviewLease(asset with { }, out _), "Copied asset DTO cannot obtain actual owner capability.");

            string manifestPath = Path.Combine(folder, "characters.json");
            byte[] originalManifest = File.ReadAllBytes(manifestPath); var manifest = JsonNode.Parse(originalManifest)!.AsObject();
            var originalEntry = manifest["models"]![0]!.AsObject(); string provenanceFile = originalEntry["provenance"]!.GetValue<string>();
            string modelFile = originalEntry["model"]!.GetValue<string>(); byte[] originalProof = File.ReadAllBytes(Path.Combine(folder, provenanceFile)); provenanceSha = Sha(originalProof);
            byte[] originalModel = File.ReadAllBytes(Path.Combine(folder, modelFile));
            string mutable = Path.Combine(temporary, "mutable-pack"); Directory.CreateDirectory(mutable);
            string[] needed = { "characters.json", provenanceFile, modelFile, Id + ".ancs-graph.json", Id + ".ancs.bin", Id + ".cinf.bin", Id + ".animations.bin", Id + ".cannon-source.json" };
            foreach (string name in needed)
            {
                string source = Path.Combine(folder, name); LocalImportBundle.CheckPath(source, imports);
                Check(state.Files.Contains(source), "Mutable test input must belong to the committed source receipt.");
                File.Copy(source, Path.Combine(mutable, name)); inputs.Add(new { FileId = name, Bytes = new FileInfo(source).Length, Sha256 = HashFile(source) });
            }
            var mutablePack = CharacterModelPack.Load(mutable);
            Check(mutablePack.TryResolvePrivate(Hunter.Samus, CharacterSourcePreviewSelection.Appearance, CharacterModelPart.Biped, 0, out var mutableAsset)
                && mutablePack.TryCreateSourcePreviewLease(mutableAsset, out var mutableLease) && mutableLease.CacheKey != lease.CacheKey,
                "Same bytes in another actual pack do not alias the retained owner's cache identity.");

            void Mutation(string label, Action<JsonObject, JsonObject> edit, string? payloadFile = null, byte[]? changedPayload = null)
            {
                var proof = JsonNode.Parse(originalProof)!.AsObject(); var document = JsonNode.Parse(originalManifest)!.AsObject(); var entry = document["models"]![0]!.AsObject();
                byte[]? saved = payloadFile == null ? null : File.ReadAllBytes(Path.Combine(mutable, payloadFile));
                try
                {
                    if (payloadFile != null) File.WriteAllBytes(Path.Combine(mutable, payloadFile), changedPayload!);
                    edit(proof, entry); byte[] bytes = Encoding.UTF8.GetBytes(proof.ToJsonString()); entry["provenanceSha256"] = Sha(bytes);
                    File.WriteAllBytes(Path.Combine(mutable, provenanceFile), bytes); File.WriteAllText(Path.Combine(mutable, "characters.json"), document.ToJsonString());
                    Reject(() => CharacterModelPack.Load(mutable), label);
                }
                finally
                {
                    if (payloadFile != null) File.WriteAllBytes(Path.Combine(mutable, payloadFile), saved!);
                    File.WriteAllBytes(Path.Combine(mutable, provenanceFile), originalProof); File.WriteAllBytes(Path.Combine(mutable, "characters.json"), originalManifest);
                }
            }
            Mutation("unsupported-format", (p, _) => p["format"] = 3);
            Mutation("generated-kind", (p, _) => p["kind"] = "own-unqualified-profile");
            Mutation("writer-version", (p, _) => p["writerVersion"] = RetroCharacterSourceGlbWriter.Version + 1);
            Mutation("coordinate-basis", (p, _) => p["sourceBasis"] = "(x,y,z)");
            Mutation("unqualified-material-policy", (p, _) => p["materialPolicy"] = "source-gx-qualified");
            Mutation("wrong-source-part", (p, _) => p["sourcePart"] = "ball");
            Mutation("source-count", (p, _) => p["sourcePositionCount"] = 4246);
            Mutation("source-resource-id", (p, _) => p["sourceCmdl"] = 1);
            Mutation("different-native-source", (p, _) => p["sourceNativeName"] = "Battle_3");
            foreach (string flag in new[] { "approvedForGameplay", "multiplayerEnabled", "playerSelectionEnabled", "firstPersonVerified", "alternateFormVerified", "sourceMotionRetargetVerified", "devicePerformanceVerified" })
                Mutation("false-authority-" + flag, (p, _) => p[flag] = true);
            Mutation("visual-only-required", (p, _) => p["visualOnly"] = false);
            Mutation("missing-false-claim", (p, _) => p.Remove("firstPersonVerified"));
            string invented = Sha("own synthetic test-only witness"u8.ToArray());
            foreach (string field in new[] { "sourceAncsSha256", "sourceCmdlSha256", "sourceCskrSha256", "sourceCinfSha256", "sourceAnimationArchiveSha256" })
                Mutation("source-pin-" + field, (p, _) => p[field] = invented);
            Mutation("invented-native-retarget-authority", (p, _) => { p["sourceNativeKitSha256"] = invented; p["sourceMappingSha256"] = invented; });
            Mutation("wrong-presentation-part", (p, e) => { p["presentationPart"] = "viewModel"; e["part"] = "ViewModel"; });
            Mutation("final-refusal", (p, e) => { p["skinSource"] = "MP2DarkSamusFinal"; e["skinSource"] = "MP2DarkSamusFinal"; });
            Mutation("manifest-archive-identity", (_, e) => e["sourceAnimationArchiveSha256"] = invented);
            byte[] forgedGlb = (byte[])originalModel.Clone();
            int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(forgedGlb.AsSpan(12, 4));
            using (var json = JsonDocument.Parse(forgedGlb.AsMemory(20, jsonLength)))
            {
                var r = json.RootElement; int access = r.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();
                int view = r.GetProperty("accessors")[access].GetProperty("bufferView").GetInt32();
                int offset = r.GetProperty("bufferViews")[view].GetProperty("byteOffset").GetInt32(); forgedGlb[28 + jsonLength + offset] ^= 1;
            }
            Mutation("self-consistent-forged-glb", (p, _) => p["sourceGlbSha256"] = Sha(forgedGlb), modelFile, forgedGlb);
            byte[] cinf = File.ReadAllBytes(Path.Combine(mutable, Id + ".cinf.bin")); byte[] forgedCinf = (byte[])cinf.Clone(); forgedCinf[12] ^= 1;
            Mutation("raw-cinf-payload-changed", (_, _) => { }, Id + ".cinf.bin", forgedCinf);

            // Compatibility stays explicit: a self-consistent legacy PRIVATE
            // provenance is allowed by the generic owner, never this product selector.
            var legacy = JsonNode.Parse(originalProof)!.AsObject(); legacy["format"] = 1;
            foreach (string field in new[] { "sourceNativeKitSha256", "sourceMappingSha256", "sourceSkeletonSha256", "sourceWeightsSha256" }) legacy[field] = invented;
            byte[] legacyBytes = Encoding.UTF8.GetBytes(legacy.ToJsonString()); var downgraded = JsonNode.Parse(originalManifest)!.AsObject();
            var legacyModels=downgraded["models"]!.AsArray();
            for(int i=legacyModels.Count-1;i>=0;i--)if(legacyModels[i]!["part"]!.GetValue<string>().Equals("ViewModel",StringComparison.OrdinalIgnoreCase))legacyModels.RemoveAt(i);
            downgraded["models"]![0]!["provenanceSha256"] = Sha(legacyBytes);
            var components = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string source in state.Files) { LocalImportBundle.CheckPath(source, imports); components.Add(Path.GetFileName(source), File.ReadAllBytes(source)); }
            components[provenanceFile] = legacyBytes; components["characters.json"] = Encoding.UTF8.GetBytes(downgraded.ToJsonString());
            string downgradeRoot = Path.Combine(temporary, "legacy-downgrade"); LocalImportBundle.Publish(downgradeRoot, spec, components, replaceAllComponents: true);
            var legacyState = LocalImportBundle.Inspect(downgradeRoot, Id, spec);
            Check(legacyState.State == ImportBundleState.Ready, "Downgrade fixture has fully rehashed self-consistent component and bundle receipts.");
            string legacyFolder = Path.GetDirectoryName(legacyState.Files.Single(p => Path.GetFileName(p) == "characters.json"))!;
            var legacyPack = CharacterModelPack.Load(legacyFolder);
            Check(legacyPack.TryResolvePrivate(Hunter.Samus, CharacterSourcePreviewSelection.Appearance, CharacterModelPart.Biped, 0, out var legacyAsset)
                && legacyPack.TryCreateSourcePreviewLease(legacyAsset, out _), "Generic legacy private profile remains compatible.");
            Reject(() => CharacterSourcePreviewSelection.Load(Id, CancellationToken.None, downgradeRoot), "product-selector-format-downgrade"); downgradeRefused = true;
            foreach (var bytes in components.Values) Array.Clear(bytes);

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { CharacterSourcePreviewSelection.Load(Id, cancellation.Token, imports); throw new InvalidDataException("Canceled selection unexpectedly loaded."); }
                catch (OperationCanceledException) { checks++; }
                try { LocalImportBundle.Remove(imports, Id, cancellation.Token); throw new InvalidDataException("Canceled removal unexpectedly committed."); }
                catch (OperationCanceledException) { checks++; }
                Check(LocalImportBundle.Inspect(imports, Id, spec).State == ImportBundleState.Ready, "Cancellation preserves published discovery.");
            }
            Reject(() => CharacterSourcePreviewSelection.Load("darksamus-overcharged-echoes", CancellationToken.None, imports), "final-product-selection-refusal");
            Check(LocalImportBundle.Remove(imports, Id) && state.Files.All(File.Exists), "Removal withdraws discovery without deleting any generation payload.");
            Reject(() => CharacterSourcePreviewSelection.Load(Id, CancellationToken.None, imports), "removed-selection-reopen"); reopenRefused = true;
            Check(selection.Pack.TryCreateSourcePreviewLease(asset, out var retainedLease), "Retained generation still passes actual strong proof after removal.");
            var retainedModel = CharacterWeightedModelLoader.LoadSourcePreview(asset, retainedLease);
            var retainedClip = CharacterSourceClip.Load(retainedLease, asset, retainedModel.SourceRig!, ordinal);
            Check(first.SequenceEqual(clip.Sample(0)) && first.SequenceEqual(retainedClip.Sample(0))
                && retainedModel.SourceRig!.Evaluate(retainedClip.Sample(.1), retainedModel.Joints).Length == 39,
                "Retained selection and newly issued retained-generation lease remain immutable and usable."); retained = true;
            Check(!LocalImportBundle.Remove(imports, Id) && state.Files.All(File.Exists), "Repeat removal cannot delete a retained generation.");
            foreach (var input in needed) Check(HashFile(Path.Combine(folder, input)) == HashFile(Path.Combine(mutable, input)), "Negative fixtures leave original committed generation untouched.");
        }
        catch (Exception e) { errorType = e.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(e.Message)); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); cleaned = !Directory.Exists(temporary); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            try { after = HashFile(rvz); } catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            var finalCaches = Caches(); cachesUnchanged = caches.All(p => finalCaches[p.Key].SequenceEqual(p.Value));
        }
        string coreAfter = HashFile(core), toolAfter = HashFile(tool);
        bool passed = errorType == null && cleaned && before != null && before == after && coreBefore == coreAfter && toolBefore == toolAfter
            && cachesUnchanged && retained && reopenRefused && downgradeRefused;
        Console.WriteLine(JsonSerializer.Serialize(new { Schema = 1, Passed = passed, Checks = checks, Refusals = refusals, Cases = cases,
            ErrorType = errorType, ErrorMessageSha256 = errorSha, MetadataOutputOnly = true, CpuOnly = true, RenderingActivated = false,
            SourceBeforeSha256 = before, SourceAfterSha256 = after, SourceUnchanged = before != null && before == after,
            CoreBeforeSha256 = coreBefore, CoreAfterSha256 = coreAfter, ToolBeforeSha256 = toolBefore, ToolAfterSha256 = toolAfter,
            TemporaryDerivedPayloadsDeleted = cleaned, RetainedSourcePayloadsWritten = !cleaned, CachesUnchanged = cachesUnchanged,
            Generation = generation, GeneratedGlbSha256 = GlbSha, GeneratedProvenanceSha256 = provenanceSha, Inputs = inputs,
            ClipChoices = clips, Nodes = nodes, Joints = joints, SourceTriangles = triangles, RetainedSelectionAfterRemove = retained,
            RemovedSelectionReopenRefused = reopenRefused, RehashedLegacyDowngradeRefused = downgradeRefused }));
        return passed ? 0 : 1;
    }
    private static Dictionary<string, object[]> Caches()
    { var result = new Dictionary<string, object[]>(); foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" }) result.Add(name, ((IEnumerable)typeof(Read).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray()); return result; }
    private static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
