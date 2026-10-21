using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;

// Explicit owned-source CPU probe. Only hashes, canonical IDs and counts leave
// the disposable import tree; raw material transport is not runtime rendering.
internal static class PrimeRevision2OwnedProbe
{
    private static readonly string[] Ids = { "trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma" };
    private const string MetroidSha1 = "2147a5ea1ff654d6bbce749d5f7c1170f98a601e";
    private const string AudioSha1 = "157e0841155cc7b699176dd7afc746c7048dbc28";
    private const string AncsSha256 = "7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4";
    private const string CinfSha256 = "937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9";
    private const string CskrSha256 = "68d509d274c02413e794611a206194701441a32b107119f8626292e72306de67";
    private static readonly (string Id, uint Cmdl, uint Cskr, string CmdlSha, string GraphSha, string BankSha)[] Profiles =
    {
        ("trooper-power", 4167637894, 2470884432, "73c33dc8953f0bbf637489b92880f3a5893f6b345074f4d3bb06735334ead700", "303b654c5fbe3d98165ee0ef13e62a8acf9117915c687868b124c5ba6b69f0a0", "a1b05e777345420456b1c2ace0c961a07bb2c54992536d30ef656e78593257b8"),
        ("trooper-ice", 1172758107, 784953741, "9b903148003260a95dfbf3093826b37986f177d72e2772434135d33928c597ae", "fe009241436842decaeb9c578cc23bab5f8c4f5cbfa7af924c14384d976cf3ad", "63ef357c9c0c945bb3ff97bf23b891cc028cd3de27b45dd9148d574cf927ce44"),
        ("trooper-wave", 1588964858, 899285548, "fe3d20f967430c9b63c7b345f35b3863c60db2a42831c140cf7a2df2347c05f6", "76e049702e73ca0907d4ff8e80331b7bbcd77070f39b23f0be7a85af2c8f7355", "6b95ba35dce96cd3fbfd6de5709b51bef89bbea1777ae3725556d9f484f648d7"),
        ("trooper-plasma", 539674444, 1258645658, "d4b49817fe7250abcb7e3ca98f14fc8e22a820abaf15a516aa4d59f0a2756f3b", "0a759359ccdf81f510962282f0458f3b341331743678952b649f08c07d84a9f5", "3ee7cd22d0c64374c28c08ba321c4b9d62d8b3ee653c04cd922836e089aeb7b2")
    };

    internal static int Run(string source)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-revision2-owned-" + Guid.NewGuid().ToString("N"));
        string imports = Path.Combine(temporary, "imports");
        string core = typeof(RetroCharacterPreviewExporter).Assembly.Location, tool = typeof(PrimeRevision2OwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var cachesBefore = Caches();
        string? sourceBefore = null, sourceAfter = null, errorType = null, errorSha = null;
        bool cleaned = false, reused = false, canceledPreserved = false, removed = false, retained = false, cachesUnchanged = false, migrationRollback = false, migrationRetained = false;
        int checks = 0; var archiveMetadata = new List<object>(); var bundles = new List<object>();
        var generations = new Dictionary<string, ImportBundleInspection>(StringComparer.Ordinal);
        var previousGenerations = new Dictionary<string, ImportBundleInspection>(StringComparer.Ordinal);
        var previousV3Generations = new Dictionary<string, ImportBundleInspection>(StringComparer.Ordinal);
        var previousV4Generations = new Dictionary<string, ImportBundleInspection>(StringComparer.Ordinal);
        bool v3BytesPreserved = false, v4BytesPreserved = false, v4MigrationRollback = false;
        int cpuPreviewCaptures = 0;
        void Check(bool value, string why) { if (!value) throw new InvalidDataException(why); checks++; }
        try
        {
            sourceBefore = HashFile(source);
            using (var disc = GameCubeDiscReader.Open(source))
            {
                Check(disc.GameId == "GM8E01" && disc.DiscNumber == 0 && disc.Revision == 2
                    && disc.Game == ExternalGameKind.MetroidPrime, "Exact USA revision-two disc identity is required.");
                Check(ExternalSourceRequirements.EditionFor(disc) == ExternalSourceEdition.PrimeUsaRevision2,
                    "Actual disc must select the bounded revision-two source profile.");
                var requirements = ExternalSourceRequirements.For(disc);
                Check(requirements.Count == 2 && requirements.Single(a => a.Path == "Metroid1.pak").ExpectedSha1 == MetroidSha1
                    && requirements.Single(a => a.Path == "AudioGrp.pak").ExpectedSha1 == AudioSha1,
                    "Revision-two profile must retain both exact independently pinned archives.");
                Check(ExternalSourceRequirements.For(ExternalGameKind.MetroidPrime)
                    .Single(a => a.Path == "Metroid1.pak").ExpectedSha1 != MetroidSha1,
                    "Legacy source profile remains distinct.");
                foreach (var requirement in requirements)
                {
                    var verification = ExternalSourceRequirements.Verify(disc, requirement);
                    Check(verification.Status == ExternalArchiveStatus.Verified && verification.ActualSha1 == requirement.ExpectedSha1,
                        "Actual owned archive must verify against its exact selected source profile.");
                    archiveMetadata.Add(new { FileId = requirement.Path, Status = verification.Status.ToString(),
                        requirement.ExpectedSha1, verification.ActualSha1 });
                }
            }
            // Seed frozen-v2 receipt identities with invented bytes; there is
            // no dependency on old extracted payloads or optional evidence.
            foreach (string id in Ids)
            {
                var previous = PrimeRevision2RecipeChecks.PreviousRev2(id);
                string v2Root = Path.Combine(temporary, "v2-fixture");
                LocalImportBundle.Publish(v2Root, previous, previous.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic v2 " + n)));
                var old = LocalImportBundle.Inspect(v2Root, id, previous); previousGenerations.Add(id, old);
                var snapshot = Snapshot(v2Root);
                Check(old.State == ImportBundleState.Ready && RetroCharacterPreviewExporter.InspectBundle(v2Root, id).State == ImportBundleState.Outdated
                    && Same(snapshot, Snapshot(v2Root)), "Exact old v2 receipt stays explicitly Ready while edition-aware inspection is nonmutating Outdated.");
            }
            // Run the frozen pre-material exporter in an isolated load context.
            // It writes only this probe's disposable import root. Both old/new
            // bytes remain present until the final verified cleanup.
            string baselineCore = Environment.GetEnvironmentVariable("PRIME_REV2_BASELINE_CORE")
                ?? throw new InvalidDataException("Owned migration probe requires an explicit frozen pre-material core.");
            Check(HashFile(baselineCore) == "0599a74b5d56d8fe2aa04194107dcc38224e5bfe3d5db4590f0851dd812adfe7",
                "Baseline exporter is the independently frozen pre-material production core.");
            var baselineContext = new AssemblyLoadContext("prime-rev2-v3-baseline-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var baselineAssembly = baselineContext.LoadFromAssemblyPath(Path.GetFullPath(baselineCore));
                var exporter = baselineAssembly.GetType("MphRead.Mods.Import.RetroCharacterPreviewExporter", throwOnError: true)!;
                var oldOutputs = (IReadOnlyList<string>)exporter.GetMethod("ExportFromOwnedGame", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { source, imports, CancellationToken.None, null, Ids })!;
                Check(oldOutputs.Count == 4, "Frozen v3 exporter publishes all four actual owned source generations.");
            }
            finally { baselineContext.Unload(); }
            foreach (string id in Ids)
            {
                var previous = PrimeRevision2RecipeChecks.PreviousRev3(id);
                var old = LocalImportBundle.Inspect(imports, id, previous); previousV3Generations.Add(id, old);
                var snapshot = Snapshot(imports);
                Check(old.State == ImportBundleState.Ready && old.Receipt is { Version: 3 } && old.Files.Count == 10
                    && RetroCharacterPreviewExporter.InspectBundle(imports, id).State == ImportBundleState.Outdated
                    && Same(snapshot, Snapshot(imports)), "Actual frozen ten-component v3 remains valid and current inspection is nonmutating Outdated.");
            }
            string Pointer(string id) => Path.Combine(imports, LocalImportBundle.StorageDirectory, id, "current.json");
            var oldPointers = Ids.ToDictionary(id => id, id => HashFile(Pointer(id)));
            bool stageSeen = false;
            using (var cancellation = new CancellationTokenSource())
            {
                bool canceled = false;
                var progress = new InlineProgress(stage => { if (stage == ImportPublishStage.BeforeCommit) { stageSeen = true; cancellation.Cancel(); } });
                try { RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, cancellation.Token, progress, new[] { "trooper-power" }); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && stageSeen, "Real full-bank/GLB upgrade reaches BeforeCommit and honors inline cancellation.");
            }
            void CheckPreviousBytes()
            {
                foreach (var old in previousGenerations.Values.Concat(previousV3Generations.Values).Concat(previousV4Generations.Values))
                {
                    string folder = Path.GetDirectoryName(old.Files[0])!;
                    foreach (var component in old.Receipt!.Components)
                    {
                        string path = Path.Combine(folder, component.Id);
                        Check(File.Exists(path) && new FileInfo(path).Length == component.Length && HashFile(path) == component.Sha256,
                            "Every previous v2/v3/v4 immutable component survives rollback/upgrade byte-for-byte.");
                    }
                }
            }
            CheckPreviousBytes();
            Check(Ids.All(id => HashFile(Pointer(id)) == oldPointers[id]
                && LocalImportBundle.Inspect(imports, id, PrimeRevision2RecipeChecks.PreviousRev3(id)).State == ImportBundleState.Ready
                && RetroCharacterPreviewExporter.InspectBundle(imports, id).State == ImportBundleState.Outdated),
                "Canceled material migration preserves all actual v3 current receipts and explicit old-recipe validity.");
            migrationRollback = true;
            string materialBaselineCore = Environment.GetEnvironmentVariable("PRIME_REV2_MATERIAL_BASELINE_CORE")
                ?? throw new InvalidDataException("Owned preview migration probe requires an explicit frozen material-authoring core.");
            Check(HashFile(materialBaselineCore) == "dd30361bbbd4a57159086c69cbf8ba4d4f1601a03dbf6b54b662e95875b53388",
                "Material baseline exporter is the independently frozen PR945 production core.");
            var materialBaselineContext = new AssemblyLoadContext("prime-rev2-v4-baseline-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var baselineAssembly = materialBaselineContext.LoadFromAssemblyPath(Path.GetFullPath(materialBaselineCore));
                var exporter = baselineAssembly.GetType("MphRead.Mods.Import.RetroCharacterPreviewExporter", throwOnError: true)!;
                var oldOutputs = (IReadOnlyList<string>)exporter.GetMethod("ExportFromOwnedGame", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { source, imports, CancellationToken.None, null, Ids })!;
                Check(oldOutputs.Count == 4, "Frozen v4 exporter publishes all four actual material-authoring generations.");
            }
            finally { materialBaselineContext.Unload(); }
            foreach (string id in Ids)
            {
                var old = LocalImportBundle.Inspect(imports, id, PrimeRevision2RecipeChecks.PreviousRev4(id));
                previousV4Generations.Add(id, old);
                var snapshot = Snapshot(imports);
                Check(old.State == ImportBundleState.Ready && old.Receipt is { Version: 4 } && old.Files.Count == 11
                    && RetroCharacterPreviewExporter.InspectBundle(imports, id).State == ImportBundleState.Outdated
                    && Same(snapshot, Snapshot(imports)), "Actual frozen eleven-component v4 stays explicitly valid while current preview inspection is nonmutating Outdated.");
            }
            var oldV4Pointers = Ids.ToDictionary(id => id, id => HashFile(Pointer(id)));
            using (var cancellation = new CancellationTokenSource())
            {
                bool canceled = false, v4StageSeen = false;
                var progress = new InlineProgress(stage => { if (stage == ImportPublishStage.BeforeCommit) { v4StageSeen = true; cancellation.Cancel(); } });
                try { RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, cancellation.Token, progress, new[] { "trooper-power" }); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && v4StageSeen && Ids.All(id => HashFile(Pointer(id)) == oldV4Pointers[id]
                    && LocalImportBundle.Inspect(imports, id, PrimeRevision2RecipeChecks.PreviousRev4(id)).State == ImportBundleState.Ready
                    && RetroCharacterPreviewExporter.InspectBundle(imports, id).State == ImportBundleState.Outdated),
                    "Canceled preview migration preserves all actual v4 current receipts and explicit old-recipe validity.");
            }
            CheckPreviousBytes(); v4MigrationRollback = true;
            var exported = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, selectedIds: Ids);
            Check(exported.Count == 4 && exported.Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(Ids.Select(id => id + ".obj").Order(StringComparer.Ordinal)),
                "One real default export must publish all four selected Troopers only.");
            string fingerprint = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(
                "AudioGrp.pak:" + AudioSha1 + "\nMetroid1.pak:" + MetroidSha1)));
            using var materialDisc = GameCubeDiscReader.Open(source);
            using var materialPak = new RetroPakReader(materialDisc.OpenRead("Metroid1.pak"), leaveOpen: false);
            foreach (string id in Ids)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
                var legacy = RetroCharacterPreviewExporter.BundleSpec(id);
                Check(spec.Version == 5 && legacy.Version == 1 && spec.RequiredComponents.Length == 12
                    && spec.RequiredComponents.ToHashSet().SetEquals(PrimeRevision2RecipeChecks.FullComponents(id))
                    && spec.SourceFingerprint == fingerprint && spec.SourceFingerprint != legacy.SourceFingerprint
                    && spec.RecipeHash != legacy.RecipeHash, "Recipe identity must bind the exact new source without changing legacy v1.");
                var state = RetroCharacterPreviewExporter.InspectBundle(imports, id);
                Check(state.State == ImportBundleState.Ready
                    && LocalImportBundle.Inspect(imports, id, spec).State == ImportBundleState.Ready
                    && LocalImportBundle.Inspect(imports, id, legacy).State == ImportBundleState.UnsupportedSource,
                    "Edition-aware inspection accepts v5 while an explicit legacy recipe refuses the different source.");
                var receipt = state.Receipt ?? throw new InvalidDataException("Missing committed source receipt.");
                Check(receipt.Id == id && receipt.Version == 5 && receipt.SourceFingerprint == fingerprint && state.Files.Count == 12
                    && receipt.RecipeHash == spec.RecipeHash && receipt.Components.Length == state.Files.Count
                    && receipt.RequiredComponents.Length == state.Files.Count, "Receipt must describe the complete exact generation.");
                string folder = Path.GetDirectoryName(state.Files.Single(p => Path.GetFileName(p) == id + ".obj"))!;
                Check(Path.GetFileName(folder) == receipt.Generation && state.Files.All(p => Path.GetDirectoryName(p) == folder)
                    && spec.RequiredComponents.All(name => state.Files.Contains(Path.Combine(folder, name)))
                    && exported.Contains(Path.Combine(folder, id + ".obj")), "All required authoring parts share the committed generation.");
                var componentMetadata = new List<object>();
                foreach (var component in receipt.Components)
                {
                    string path = Path.Combine(folder, component.Id); LocalImportBundle.CheckPath(path, imports);
                    Check(state.Files.Contains(path) && new FileInfo(path).Length == component.Length && HashFile(path) == component.Sha256,
                        "Every committed component must retain its receipted bytes and digest.");
                    componentMetadata.Add(new { FileId = component.Id, component.Length, component.Sha256 });
                }
                int positions = 0, triangles = 0;
                foreach (string line in File.ReadLines(Path.Combine(folder, id + ".obj")))
                {
                    if (line.StartsWith("v ", StringComparison.Ordinal)) positions++;
                    if (line.StartsWith("f ", StringComparison.Ordinal))
                    {
                        Check(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 4, "Authoring OBJ must retain triangle faces.");
                        triangles++;
                    }
                }
                using var skeleton = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, id + ".skeleton.json")));
                var root = skeleton.RootElement; int bones = root.GetProperty("BoneCount").GetInt32();
                string nativeName = RetroCharacterPreviewExporter.Sources.Single(s => s.Id == id).NativeName;
                Check(positions == 1936 && triangles == 1970 && bones == 35 && root.GetProperty("Bones").GetArrayLength() == 35
                    && root.GetProperty("Id").GetString() == id && root.GetProperty("SourceGameId").GetString() == "GM8E01"
                    && root.GetProperty("SourceCharacter").GetString() == nativeName,
                    "Actual selected appearance must retain its pinned geometry and skeleton inventory.");
                Check(RetroCharacterPreviewExporter.InspectParts(id, state).Single() is { Id: "body", Version: 4, State: ImportBundleState.Ready }
                    && state.Files.Count(p => Path.GetFileName(p) == "characters.json") == 1
                    && !state.Files.Any(p => p.EndsWith(".cannon-source.json", StringComparison.Ordinal)
                        || p.EndsWith(".parts.json", StringComparison.Ordinal)), "Rev2 bodyv4 includes exactly one generated body-viewer manifest without cannon/parts index.");
                var runtimeCaches = Caches();
                object preview = VerifyCpuPreview(id, imports, receipt.Generation, Check); cpuPreviewCaptures++;
                var localBody = CharacterSourcePreviewSelection.LoadForLocalBody(id, CancellationToken.None, imports);
                bool localBodyAdmitted = localBody.NativeHunter == Hunter.Weavel && !localBody.HasCannon && !localBody.RuntimeCannon
                    && localBody.Generation == receipt.Generation && localBody.Clips.Count == 113
                    && localBody.SelectedAppearance.CharacterId == "trooper";
                var runtimeCachesAfter = Caches();
                Check(localBodyAdmitted && runtimeCaches.All(p => runtimeCachesAfter[p.Key].SequenceEqual(p.Value)),
                    "CPU viewer and local Weavel body capture retain strict clips without cannon capability or native Read cache mutation.");
                object fullSource = VerifyFullSource(id, folder, fingerprint, Check);
                object materials = VerifyMaterials(id, folder, fingerprint, materialPak, Check);
                var previousV3 = previousV3Generations[id];
                string previousFolder = Path.GetDirectoryName(previousV3.Files[0])!;
                foreach (var component in previousV3.Receipt!.Components)
                {
                    string before = Path.Combine(previousFolder, component.Id), after = Path.Combine(folder, component.Id);
                    Check(File.ReadAllBytes(before).AsSpan().SequenceEqual(File.ReadAllBytes(after)) && HashFile(after) == component.Sha256,
                        "Each of ten preexisting actual v3 authoring files is byte/hash identical after preview transport is added.");
                }
                var previousV4 = previousV4Generations[id];
                string previousMaterialFolder = Path.GetDirectoryName(previousV4.Files[0])!;
                foreach (var component in previousV4.Receipt!.Components)
                {
                    string before = Path.Combine(previousMaterialFolder, component.Id), after = Path.Combine(folder, component.Id);
                    Check(File.ReadAllBytes(before).AsSpan().SequenceEqual(File.ReadAllBytes(after)) && HashFile(after) == component.Sha256,
                        "Each of eleven preexisting actual v4 authoring/material files is byte/hash identical after adding the preview manifest.");
                }
                Check(receipt.Generation != previousV3.Receipt.Generation && receipt.Generation != previousV4.Receipt.Generation,
                    "The generated preview upgrade publishes a distinct immutable v5 generation.");
                generations.Add(id, state);
                bundles.Add(new { Id = id, receipt.Version, receipt.RecipeHash, receipt.SourceFingerprint, receipt.Generation,
                    Positions = positions, Triangles = triangles, Bones = bones, RequiredRecipeComponents = spec.RequiredComponents.Length,
                    CommittedFiles = state.Files.Count, Components = componentMetadata, FullSource = fullSource, RawMaterials = materials,
                    CpuPreview = preview, LocalBodySelectionAdmitted = localBodyAdmitted, LocalBodyHasCannon = localBody.HasCannon,
                    PreviousV3ComponentsByteIdentical = 10, PreviousV4ComponentsByteIdentical = 11 });
            }
            CheckPreviousBytes(); migrationRetained = v3BytesPreserved = v4BytesPreserved = true;
            var initial = Snapshot(imports);
            var repeated = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, selectedIds: Ids);
            Check(repeated.Order(StringComparer.Ordinal).SequenceEqual(exported.Order(StringComparer.Ordinal)) && Same(initial, Snapshot(imports))
                && Ids.All(id => RetroCharacterPreviewExporter.InspectBundle(imports, id).Receipt?.Generation == generations[id].Receipt!.Generation),
                "Same-source update must reuse all four immutable generations without rewriting receipts or bytes.");
            reused = true;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool canceled = false;
                try { RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, cancellation.Token, selectedIds: Ids); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && Same(initial, Snapshot(imports)), "Pre-cancel must preserve every committed receipt and generation.");
                canceledPreserved = true;
            }
            foreach (string id in Ids)
            {
                Check(LocalImportBundle.Remove(imports, id) && !LocalImportBundle.Remove(imports, id)
                    && RetroCharacterPreviewExporter.InspectBundle(imports, id).State == ImportBundleState.NotInstalled,
                    "Remove withdraws discovery exactly once for the selected appearance.");
                var old = generations[id]; string folder = Path.GetDirectoryName(old.Files[0])!;
                foreach (var component in old.Receipt!.Components)
                {
                    string path = Path.Combine(folder, component.Id);
                    Check(File.Exists(path) && new FileInfo(path).Length == component.Length && HashFile(path) == component.Sha256,
                        "Removal must retain every immutable old-generation component for existing consumers.");
                }
            }
            removed = retained = true;
        }
        catch (Exception e) { errorType = e.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(e.Message)); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); cleaned = !Directory.Exists(temporary); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            try { sourceAfter = HashFile(source); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            var after = Caches(); cachesUnchanged = cachesBefore.All(p => after[p.Key].SequenceEqual(p.Value));
        }
        string coreAfter = HashFile(core), toolAfter = HashFile(tool);
        bool sourceUnchanged = sourceBefore != null && sourceBefore == sourceAfter;
        bool binariesUnchanged = coreBefore == coreAfter && toolBefore == toolAfter;
        bool passed = errorType == null && sourceUnchanged && binariesUnchanged && cachesUnchanged
            && cleaned && reused && canceledPreserved && removed && retained && migrationRollback && v4MigrationRollback
            && migrationRetained && v3BytesPreserved && v4BytesPreserved && cpuPreviewCaptures == 4 && bundles.Count == 4;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Kind = "prime-usa-revision2-owned-import", Passed = passed, Checks = checks,
            MetadataOutputOnly = true, CpuOnly = true, DefaultImporterUsed = true, RuntimeActivated = false,
            RenderingActivated = false, MaterialsQualified = false, RawMaterialTransportQualified = passed, AbilitiesQualified = false,
            CpuSourcePreviewCaptures = cpuPreviewCaptures, CpuSourcePreviewCaptureQualified = passed, SourceGpuRenderingQualified = false,
            SourceGameId = "GM8E01", Revision = 2, DiscNumber = 0, Archives = archiveMetadata, Bundles = bundles,
            SameInputGenerationReused = reused, PreCancelledReceiptsPreserved = canceledPreserved,
            ActualV3BeforeCommitRollback = migrationRollback, ActualV4BeforeCommitRollback = v4MigrationRollback,
            OldV2V3AndV4GenerationsRetainedAfterUpgrade = migrationRetained,
            AllFortyPreexistingV3ComponentsByteIdentical = v3BytesPreserved,
            AllFortyFourPreexistingV4ComponentsByteIdentical = v4BytesPreserved,
            DiscoveryRemoved = removed, OldGenerationsRetainedUntilCleanup = retained,
            SourceBeforeSha256 = sourceBefore, SourceAfterSha256 = sourceAfter, SourceUnchanged = sourceUnchanged,
            CoreBeforeSha256 = coreBefore, CoreAfterSha256 = coreAfter, ToolBeforeSha256 = toolBefore, ToolAfterSha256 = toolAfter,
            BinariesUnchanged = binariesUnchanged, CachesUnchanged = cachesUnchanged,
            TemporaryDerivedPayloadsDeleted = cleaned, SourcePayloadsRetained = !cleaned,
            ErrorType = errorType, ErrorMessageSha256 = errorSha
        }));
        return passed ? 0 : 1;
    }

    private static object VerifyCpuPreview(string id, string imports, string generation, Action<bool, string> check)
    {
        var selection = CharacterSourcePreviewSelection.Load(id, CancellationToken.None, imports);
        check(selection.NativeHunter == Hunter.Weavel && selection.Generation == generation
            && selection.SelectedAppearance.CharacterId == "trooper" && !selection.HasCannon && !selection.RuntimeCannon
            && selection.Clips.Count == 113 && selection.Clips.Select(c => c.Ordinal).Distinct().Count() == 113,
            "Actual current Trooper CPU preview captures the exact Weavel appearance/generation and113 strict clips without cannon authority.");
        check(selection.Pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var asset),
            "CPU preview resolves its exact private Weavel biped asset.");
        check(!selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out _)
            && !selection.Pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.ViewModel, 0, out _)
            && !selection.Pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.AlternateForm, 0, out _),
            "Trooper CPU preview cannot borrow a Samus owner, cannon or alternate-form capability.");
        check(selection.Pack.TryCreateSourcePreviewLease(asset, out var lease) && lease.SourceAnimations != null && lease.SourceMaterials != null,
            "CPU capture retains source animation and compiled material authority in the same exact lease.");
        var geometry = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease);
        var rig = geometry.SourceRig!; var bank = lease.SourceAnimations!; var materials = lease.SourceMaterials!;
        int root = Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name == "source_bone_3_root");
        int skeletonRoot = Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name == "source_bone_4_Skeleton_Root");
        check(geometry.Joints.Count == 35 && rig.JointNodes.Count == 35 && rig.Nodes.Count == 36
            && rig.Nodes[root].Parent == -1 && rig.Nodes[skeletonRoot].Parent == root
            && bank.RootBoneId == 3 && bank.ExternalParentId == 2 && bank.Skeleton.Bones.Count == 35 && !bank.Skeleton.Bones.ContainsKey(2)
            && geometry.IndexCount == 1970 * 3 && geometry.Primitives.Select(p => p.SourceMaterialOrdinal).Distinct().Order().SequenceEqual(new[] { 0, 1, 2 }),
            "Actual preview retains the complete35-joint/36-node root3-parent2 source hierarchy and all1970triangles across three material ordinals.");
        check(bank.Data.Resources.Count == 142 && bank.Data.Roots.Count == 167
            && selection.Clips.Select(c => c.Ordinal).Order().SequenceEqual(bank.Data.Roots.Where(r => r.CharacterDeclared && r.StrictPlay).Select(r => r.Ordinal).Order())
            && materials.Programs.Count == 3 && materials.Images.Count == 7
            && materials.Programs.All(p => p.Vertex120.Length > 0 && p.Fragment120.Length > 0)
            && materials.Policy == CharacterSourceGxProgram.TrooperPolicy
            && materials.Archive.Binding.CharacterId == id && materials.Archive.GeometrySha256 == lease.ModelSha256,
            "CPU preview captures all three original compiled GX programs/seven textures and exactly the declared strict source-bank choices.");
        int samples = 0;
        foreach (var choice in selection.Clips)
        {
            var clip = CharacterSourceClip.Load(lease, asset, rig, choice.Ordinal);
            foreach (double seconds in new[] { 0d, choice.Duration * .375, choice.Duration })
            {
                var pose = clip.Sample(seconds); samples++;
                check(pose.Count == rig.Nodes.Count && pose.All(matrix => Enumerable.Range(0, 4)
                    .All(row => Enumerable.Range(0, 4).All(column => float.IsFinite(matrix[row, column])))),
                    "Every admitted strict clip binds and samples finite full-rig source matrices at start/off-grid/end without renderer activation.");
            }
        }
        lease.Require(asset);
        return new { NativeHunter = selection.NativeHunter.ToString(), selection.Generation, selection.HasCannon, selection.RuntimeCannon,
            StrictClips = selection.Clips.Count, Joints = geometry.Joints.Count, Nodes = rig.Nodes.Count,
            GxPrograms = materials.Programs.Count, Textures = materials.Images.Count, SourceTriangles = geometry.IndexCount / 3,
            FullRigPoseSamples = samples, CpuOnly = true, GpuRenderingActivated = false, ActorBodyAdmitted = false };
    }

    private sealed record MaterialGolden(string Id, string SetSha, string[] ProgramShas, string TextureInventorySha);
    // Frozen from the read-only owned CMDL/TXTR inventory before production
    // transport existed. The aggregate includes every raw/pixel hash and extent
    // for the seven sorted texture IDs; no optional local JSON is an oracle.
    private static readonly MaterialGolden[] MaterialGoldens =
    {
        new("trooper-power", "23102073c49ae6d40deb558b14d1d4c61e884719ed73e0c55feaf76814450d2b",
            ["ea80ba014efd9c14f0a119a6c0f90357a088d462f6edc52092ba0eeda739fddb", "9367e238bfebce02df2df83efb717410c8ca83715ea38aae97d8839a9a89dab4", "a39c302d15ed10b75045323ab5ccedd58f3084b6c197f8a07822e0989a147db6"],
            "ab2c298311421697ed518ab3f9e4ff7f084dbf729b1b996b2f179d33e54e4893"),
        new("trooper-ice", "0baa64572903c4d70c654edae60e6bf7856778713d39adf35d823815cd05d384",
            ["9b8d9c684e4c0fda86a2e9afbfa7dbbb5cb2db9aac2c0f2f2c59435c8e4e16ba", "62e4071fead003cd4d698e634e99780bc98701682c240cba7c95b9d4f9a39307", "408d433cef7aabd8be19deda7ac3719d12d8d641f722c33deb1f475eaecba657"],
            "610670b7e6d679328f2ddd9c4f4bd5261c83eb59b6d2b41e36a19806a92bde29"),
        new("trooper-wave", "95d5c3ac073f47f56673e83c169d81ecb8b0aa55cc0c2d3221be8271d6d41f90",
            ["8be77e3ba257ece3a2ef27aa0adbef40da3699444c6b59ccb39d93c8a27d1537", "9a9ed55b1bde57fe20704795641751546db97218a212fe0d036e7ffd9e69617a", "a3f06ccf78d346a9a330d793ec285e6c3df71dc57c7a228617c5bbb4bcb6aa8d"],
            "312d1ab043362c3dd2cf7757f438184db990cba721f47356e9a8c42ad8948e97"),
        new("trooper-plasma", "7582c02a02bae302819c44ad525a01b1ba94d8a0ab951acf966ae30918709268",
            ["5db819e16958d9b197e78059c3b0a7b62a32650d97191bc13dc3275406717772", "6149d16e53c6a0b6de4a9c5c508a50a0b8bfddd283c09ce5967932d5c7ca0cc6", "a42b9170b05d60507c192a1d1fd642deaadc74df4c738d156726c0095937e746"],
            "0133e316101c06468afa78bde6ebfb71a8dd8406fbc9ee649825e6b688fc5839")
    };

    private static object VerifyMaterials(string id, string folder, string fingerprint, RetroPakReader pak, Action<bool, string> check)
    {
        var expected = Profiles.Single(p => p.Id == id); var golden = MaterialGoldens.Single(p => p.Id == id);
        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, id + ".materials.bin"));
        var archive = RetroCharacterMaterialArchive.Read(bytes);
        byte[] cmdl = pak.ReadResource(expected.Cmdl), cskr = pak.ReadResource(expected.Cskr);
        uint Be(byte[] value, int at) => BinaryPrimitives.ReadUInt32BigEndian(value.AsSpan(at, 4));
        check(Be(cmdl, 0) == 0xdeadbabe && Be(cmdl, 4) == 2 && Be(cmdl, 0x28) == 1
            && Sha(cmdl) == expected.CmdlSha && Sha(cskr) == CskrSha256, "Independent exact Rev2 CMDL2/CSKR source resources match frozen inventory.");
        check(archive.Binding == new RetroCharacterMaterialArchive.Source(id, expected.Cmdl, expected.Cskr, 0x0c9669bc, 0x8169653a, fingerprint)
            && bytes.AsSpan(196, 6).SequenceEqual("GM8E01"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)) == (uint)(Array.IndexOf(Ids, id) + 3)
            && archive.CmdlSha256 == expected.CmdlSha && archive.CskrSha256 == CskrSha256
            && archive.GeometrySha256 == HashFile(Path.Combine(folder, id + ".source.glb")),
            "Material archive binds the exact closed Prime profile, edition, resource tuple and unchanged source geometry.");
        int sections = checked((int)Be(cmdl, 0x24));
        int materialAt = checked((0x2c + sections * 4 + 31) / 32 * 32), materialBytes = checked((int)Be(cmdl, 0x2c));
        int textureCount = checked((int)Be(cmdl, materialAt));
        uint[] table = Enumerable.Range(0, textureCount).Select(i => Be(cmdl, materialAt + 4 + i * 4)).ToArray();
        int countAt = checked(materialAt + 4 + textureCount * 4), count = checked((int)Be(cmdl, countAt));
        int recordsAt = checked(countAt + 4 + count * 4), previous = 0;
        int capturedAt = RetroCharacterMaterialArchive.HeaderBytes + archive.Textures.Count * RetroCharacterMaterialArchive.EntryBytes;
        check(materialBytes == 608 && count == 3 && archive.Programs.Count == 3 && textureCount == 7 && archive.Textures.Count == 7
            && Sha(cmdl.AsSpan(materialAt, materialBytes).ToArray()) == golden.SetSha && archive.MaterialSetSha256 == golden.SetSha
            && bytes.AsSpan(capturedAt, materialBytes).SequenceEqual(cmdl.AsSpan(materialAt, materialBytes)),
            "Complete original608-byte material set, table, three records and opaque tail are retained exactly.");
        var json = new JsonSerializerOptions { IncludeFields = true };
        var programMetadata = new List<object>(); var referenced = new HashSet<uint>();
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            int end = checked((int)Be(cmdl, countAt + 4 + ordinal * 4)), length = end - previous;
            byte[] raw = cmdl.AsSpan(recordsAt + previous, length).ToArray();
            var material = Cmdl.ReadMaterial(cmdl, recordsAt + previous, recordsAt + end, echoes: false);
            material.TexIds = material.Tex.Select(slot => table[slot]).ToArray();
            var retained = archive.ReadProgram(ordinal); string program = JsonSerializer.Serialize(material, json);
            check(length == (ordinal == 1 ? 196 : 176) && archive.Programs[ordinal].Ordinal == ordinal
                && archive.Programs[ordinal].Length == length && archive.Programs[ordinal].RawSha256 == golden.ProgramShas[ordinal]
                && Sha(raw) == golden.ProgramShas[ordinal] && archive.RawProgram(ordinal).SequenceEqual(raw),
                "Each opaque original material record matches independently frozen bytes/hash and source CMDL extent.");
            check(material.EchoesUnknown == null && retained.EchoesUnknown == null && JsonSerializer.Serialize(retained, json) == program
                && archive.Programs[ordinal].TextureIds.SequenceEqual(material.TexIds),
                "Prime material decoding retains every selector, stage, UV animation and duplicate local texture slot without Echoes layout.");
            retained.Flags ^= 1;
            check(JsonSerializer.Serialize(archive.ReadProgram(ordinal), json) == program, "ReadProgram returns detached mutable material snapshots.");
            foreach (uint resource in material.TexIds) referenced.Add(resource);
            programMetadata.Add(new { Ordinal = ordinal, Bytes = length, RawSha256 = Sha(raw), LocalSlots = material.TexIds.Length });
            previous = end;
        }
        check(materialAt + materialBytes - recordsAt - previous == 12
            && archive.Textures.Select(t => t.ResourceId).SequenceEqual(referenced.Order()), "Opaque12-byte tail and all seven referenced texture IDs survive without dropping unused local slots.");
        var rows = new List<string>(); var textureMetadata = new List<object>();
        foreach (var texture in archive.Textures)
        {
            byte[] raw = pak.ReadResource(texture.ResourceId); var layout = RetroTextureSource.Inspect(raw);
            var decoded = archive.DecodeTexture(texture.ResourceId); string rawSha = Sha(raw), rgbaSha = Sha(decoded.Pixels);
            check(archive.RawTexture(texture.ResourceId).SequenceEqual(raw) && texture.Length == raw.Length && texture.SourceSha256 == rawSha
                && texture.Format == layout.Format && texture.Width == layout.Width && texture.Height == layout.Height
                && texture.MipCount == layout.MipCount && texture.DecodedBytes == decoded.Pixels.Length && texture.RgbaSha256 == rgbaSha,
                "Every complete original TXTR including all source mip bytes and decoded top-image digest matches exact PAK data.");
            rows.Add(FormattableString.Invariant($"{texture.ResourceId:x8}|{raw.Length}|{rawSha}|{layout.Format}|{layout.Width}|{layout.Height}|{layout.MipCount}|{rgbaSha}"));
            textureMetadata.Add(new { ResourceId = texture.ResourceId.ToString("x8"), SourceBytes = raw.Length, SourceSha256 = rawSha,
                layout.Format, layout.Width, layout.Height, layout.MipCount, RgbaSha256 = rgbaSha });
        }
        string textureSha = Sha(Encoding.UTF8.GetBytes(string.Join("\n", rows)));
        check(textureSha == golden.TextureInventorySha, "All seven texture extents/formats/raw/pixel hashes reproduce the independently frozen read-only inventory.");
        return new { ArchiveSha256 = Sha(bytes), Bytes = bytes.Length, MaterialSetBytes = materialBytes, archive.MaterialSetSha256,
            Programs = programMetadata, Textures = textureMetadata, TextureInventorySha256 = textureSha,
            OpaqueTailBytes = 12, RuntimeMaterialRenderingQualified = false };
    }

    private static object VerifyFullSource(string id, string folder, string fingerprint, Action<bool, string> check)
    {
        var expected = Profiles.Single(p => p.Id == id);
        byte[] raw = File.ReadAllBytes(Path.Combine(folder, id + ".ancs.bin")), descriptor = File.ReadAllBytes(Path.Combine(folder, id + ".ancs-graph.json")),
            cinf = File.ReadAllBytes(Path.Combine(folder, id + ".cinf.bin")), archive = File.ReadAllBytes(Path.Combine(folder, id + ".animations.bin")),
            glb = File.ReadAllBytes(Path.Combine(folder, id + ".source.glb"));
        check(Sha(raw) == AncsSha256 && Sha(descriptor) == expected.GraphSha && Sha(cinf) == CinfSha256 && Sha(archive) == expected.BankSha,
            "ANCS/graph/CINF/full-bank hashes match the independent frozen fc8 owned inventory.");
        var graph = RetroAnimationGraphProof.Verify(descriptor, raw);
        check(graph.SourceCharacterId == id && graph.SourceGameId == "GM8E01" && graph.SourceAncsId == 0x8169653a
            && graph.Cmdl == expected.Cmdl && graph.Cskr == expected.Cskr && graph.Cinf == 211184060 && graph.SourcePart == null,
            "Exact selected body references reproduce from its source graph.");
        var rig = RetroCharacterSource.ReadSkeleton(cinf);
        check(rig.Bones.Count == 35 && rig.Bones.TryGetValue(3, out var root) && root.Parent == 2 && !rig.Bones.ContainsKey(2),
            "Complete actual Prime hierarchy uses root3/parent2 without remapping or an invented external bone.");
        var bank = RetroCharacterAnimationArchive.Read(archive, new(raw, descriptor, graph), cinf, echoes: false);
        check(bank.Resources.Count == 142 && bank.Roots.Count == 167 && bank.Roots.Count(r => r.CharacterDeclared) == 130
            && bank.Roots.Count(r => r.CharacterDeclared && r.StrictPlay) == 113, "Full actual bank retains every leaf and selected-character strict declaration inventory.");
        int keys = 0, rotationChannels = 0, translationChannels = 0, poseSamples = 0; long scalars = 0;
        foreach (var resource in bank.Resources)
        {
            var clip = bank.OpenResource(resource.Id); keys += clip.KeyCount; rotationChannels += clip.RotationChannels; translationChannels += clip.TranslationChannels;
            scalars += (long)clip.KeyCount * (clip.RotationChannels * 4 + clip.TranslationChannels * 3);
            check(clip.RigidScale && clip.ScaleChannels == 0, "Prime full animation retains its real rotation/translation channels without invented scale.");
            void Pose(double seconds)
            {
                var pose = clip.Sample(seconds); poseSamples++;
                check(pose.Rotations.Count == clip.RotationChannels && pose.Translations.Count == clip.TranslationChannels && pose.Scales.Count == 0
                    && pose.Rotations.Keys.Concat(pose.Translations.Keys).All(rig.Bones.ContainsKey)
                    && pose.Rotations.Values.All(v => v.Count == 4 && v.All(double.IsFinite) && Math.Abs(v.Sum(x => x * x) - 1) <= .001)
                    && pose.Translations.Values.All(v => v.Count == 3 && v.All(double.IsFinite)),
                    "Every sampled full key/off-grid pose is finite and bound only to the complete 35-bone source skeleton.");
            }
            for (int k = 0; k < clip.KeyCount; k++) Pose(k * clip.Tick);
            Pose(clip.Tick * .375); Pose(clip.Duration);
        }
        check(keys == 3556 && rotationChannels == 4824 && translationChannels == 284 && scalars == 508624,
            "Every independently measured full temporal key/channel survives bank admission.");
        foreach (var rootClip in bank.Roots.Where(r => r.CharacterDeclared && r.StrictPlay))
            check(bank.OpenStrictPrimitive(rootClip.Ordinal).Source.Id == rootClip.ResourceId, "Declared strict roots retain exact full ANIM resource identities.");
        var composite = bank.Roots.First(r => !r.StrictPlay); bool compositeRefused = false;
        try { bank.OpenStrictPrimitive(composite.Ordinal); } catch (NotSupportedException) { compositeRefused = true; }
        check(compositeRefused, "A composite or nonzero-time source root is not silently replaced by a leaf.");
        object glbMetadata = VerifyGlb(glb, Path.Combine(folder, id + ".obj"), Path.Combine(folder, id + ".weights.json"), rig, check);
        using var provenance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, id + ".source.json"))); var p = provenance.RootElement;
        bool Text(string name, string value) => p.GetProperty(name).GetString() == value;
        bool Number(string name, long value) => p.GetProperty(name).GetInt64() == value;
        check(Number("format", 1) && Text("kind", "generated-retro-trooper-authoring-v1") && Number("writerVersion", 1)
            && Text("materialPolicy", "diagnostic-source-material-colors-v1") && Text("sourceBasis", "(-x,z,y)")
            && Text("sourceCharacterId", id) && Text("sourceNativeName", RetroCharacterPreviewExporter.Sources.Single(s => s.Id == id).NativeName)
            && Text("sourceGameId", "GM8E01") && Text("sourcePart", "body") && Text("sourceEdition", "prime-usa-rev2")
            && Text("sourceFingerprint", fingerprint) && Text("sourceRootContract", "prime-trooper-root3-parent2-v1")
            && Number("sourceRootBoneId", 3) && Number("sourceExternalParentId", 2) && !p.GetProperty("runtimeInstall").GetBoolean(),
            "Source-only provenance binds exact source/writer/basis/root/edition and refuses a runtime installation claim.");
        check(Number("sourceAncs", 0x8169653a) && Number("sourceCmdl", expected.Cmdl) && Number("sourceCskr", expected.Cskr) && Number("sourceCinf", 211184060)
            && Text("sourceAncsSha256", AncsSha256) && Text("sourceCmdlSha256", expected.CmdlSha) && Text("sourceCskrSha256", CskrSha256)
            && Text("sourceCinfSha256", CinfSha256) && Text("sourceGraphSha256", Sha(descriptor)) && Text("sourceAnimationArchiveSha256", Sha(archive))
            && Text("sourceGlbSha256", Sha(glb)) && Number("sourcePositionCount", 1936) && Number("sourceTriangleCount", 1970)
            && Number("sourceMaterialCount", 3) && Number("sourceBoneCount", 35), "Provenance preserves actual source resources, counts and complete output hashes.");
        check(new[] { "characterId", "skinSource", "nativeHunter", "presentationPart" }.All(n => !p.TryGetProperty(n, out _))
            && p.GetProperty("unqualifiedParts").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] {
                "source-materials", "first-person", "runtime-animation-and-gameplay", "selection-network-replay" }),
            "Authoring provenance invents no runtime appearance or Hunter identity and names all unfinished qualifications.");
        return new { UniqueLeaves = bank.Resources.Count, DeclaredStrictRoots = 113, TotalKeys = keys, RotationChannels = rotationChannels,
            TranslationChannels = translationChannels, ScaleChannels = 0, TotalScalars = scalars, PoseSamples = poseSamples,
            UnknownChannelBones = 0, BankSha256 = Sha(archive), GraphSha256 = Sha(descriptor), CinfSha256, Glb = glbMetadata,
            ProvenanceSha256 = HashFile(Path.Combine(folder, id + ".source.json")), SourceOnlyAuthoringProvenance = true };
    }

    // Independent glTF/BIN reads; deliberately does not call the writer or the
    // runtime model loader as its geometry oracle. Source values stay private.
    private static object VerifyGlb(byte[] bytes, string objPath, string weightsPath, RetroNativeSkeleton skeleton, Action<bool, string> check)
    {
        var positions = new List<float[]>(); var normals = new List<float[]>(); var uvs = new List<float[]>();
        var faces = new List<(int Material, (int P, int T, int N)[] Corners)>(); int material = -1;
        foreach (string line in File.ReadLines(objPath))
        {
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            if (tokens[0] is "v" or "vt" or "vn")
            {
                var values = tokens.Skip(1).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                if (tokens[0] == "v") positions.Add(values); else if (tokens[0] == "vn") normals.Add(values); else uvs.Add(values);
            }
            else if (tokens[0] == "g") material = int.Parse(tokens[1].AsSpan("material_".Length), CultureInfo.InvariantCulture);
            else if (tokens[0] == "f")
            {
                var corners = tokens.Skip(1).Select(v =>
                {
                    string[] fields = v.Split('/'); int Index(int n) => n < fields.Length && fields[n].Length > 0
                        ? int.Parse(fields[n], CultureInfo.InvariantCulture) - 1 : -1;
                    return (P: Index(0), T: Index(1), N: Index(2));
                }).ToArray();
                check(corners.Length == 3 && material is >= 0 and < 3, "OBJ oracle has explicit source triangle/material ownership.");
                faces.Add((material, corners));
            }
        }
        check(positions.Count == 1936 && faces.Count == 1970, "Independent OBJ source oracle has the exact expected source inventory.");
        check(bytes.Length >= 28 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x46546c67
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) == 2 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) == bytes.Length,
            "GLB2 declares exact complete byte extent.");
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)), binary = checked(28 + jsonLength);
        check(jsonLength is > 0 and <= 4 * 1024 * 1024 && jsonLength % 4 == 0 && binary <= bytes.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) == 0x4e4f534a
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24 + jsonLength)) == 0x004e4942
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20 + jsonLength)) == bytes.Length - binary, "Aligned JSON/BIN chunks account for every GLB byte.");
        using var document = JsonDocument.Parse(bytes.AsMemory(20, jsonLength)); var r = document.RootElement;
        var extras = r.GetProperty("extras");
        check(extras.GetProperty("sourceRootContract").GetString() == "prime-trooper-root3-parent2-v1"
            && extras.GetProperty("sourceRootBoneId").GetInt32() == 3 && extras.GetProperty("sourceExternalParentId").GetInt32() == 2
            && extras.GetProperty("sourceBasis").GetString() == "(-x,z,y)"
            && extras.GetProperty("materialPolicy").GetString() == "diagnostic-source-material-colors-v1", "GLB identifies the exact diagnostic Prime source/root contract.");
        var bones = skeleton.Bones.Values.OrderBy(b => b.Id).ToArray(); var nodes = r.GetProperty("nodes"); var skin = r.GetProperty("skins")[0];
        var boneIndex = bones.Select((bone, i) => (bone.Id, i)).ToDictionary(p => p.Id, p => p.i);
        check(nodes.GetArrayLength() == 36 && skin.GetProperty("joints").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(Enumerable.Range(0, 35))
            && r.GetProperty("materials").GetArrayLength() == 3 && nodes[35].GetProperty("mesh").GetInt32() == 0
            && nodes[35].GetProperty("skin").GetInt32() == 0 && !nodes[35].TryGetProperty("translation", out _), "All35 joints plus separate identity mesh survive without truncation.");
        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i]; float x = bone.X, y = bone.Y, z = bone.Z;
            if (skeleton.Bones.TryGetValue(bone.Parent, out var parent)) { x -= parent.X; y -= parent.Y; z -= parent.Z; }
            check(nodes[i].GetProperty("name").GetString() == "source_bone_" + bone.Id + "_" + bone.Name
                && nodes[i].GetProperty("translation").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(new[] { -x, z, y })
                && nodes[i].GetProperty("children").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(bones.Where(b => b.Parent == bone.Id).Select(b => boneIndex[b.Id])),
                "Every original joint identity, local bind and parent/child edge agrees with raw CINF.");
        }
        check(r.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { boneIndex[3], 35 }),
            "Source root3 and the identity mesh are the only glTF scene roots.");
        ReadOnlySpan<byte> Access(int id, int component, string type, int count, int width)
        {
            var a = r.GetProperty("accessors")[id]; var v = r.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
            int offset = v.GetProperty("byteOffset").GetInt32(), length = v.GetProperty("byteLength").GetInt32();
            check(a.GetProperty("componentType").GetInt32() == component && a.GetProperty("type").GetString() == type
                && a.GetProperty("count").GetInt32() == count && !a.TryGetProperty("byteOffset", out _) && !v.TryGetProperty("byteStride", out _)
                && offset >= 0 && length == checked(count * width) && offset <= bytes.Length - binary - length,
                "Independent glTF accessor extent/type/count exactly matches packed BIN data.");
            return bytes.AsSpan(binary + offset, length);
        }
        float[] Floats(int id, string type, int count, int width)
        {
            var raw = Access(id, 5126, type, count, width * 4); var values = new float[count * width];
            for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(raw.Slice(i * 4, 4));
            check(values.All(float.IsFinite), "Every raw float accessor is finite."); return values;
        }
        float[] inverse = Floats(skin.GetProperty("inverseBindMatrices").GetInt32(), "MAT4", 35, 16);
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i]; check(inverse.AsSpan(i * 16, 16).SequenceEqual(new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, b.X, -b.Z, -b.Y, 1 }),
                "Every inverse bind preserves its original global CINF translation in the exact converted basis.");
        }
        using var weights = JsonDocument.Parse(File.ReadAllBytes(weightsPath)); var w = weights.RootElement;
        var groups = w.GetProperty("groups"); int[] vertexGroups = w.GetProperty("vertexGroupIds").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        check(w.GetProperty("vertexCount").GetInt32() == 1936 && vertexGroups.Length == 1936, "Independent CSKR sidecar covers every source position.");
        var allSourceIds = new HashSet<int>(); int cornersCompared = 0, primitives = 0;
        foreach (var primitive in r.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
        {
            primitives++; var a = primitive.GetProperty("attributes"); int ordinal = primitive.GetProperty("material").GetInt32();
            int[] sourceIds = primitive.GetProperty("extras").GetProperty("sourceTriangleIds").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int count = sourceIds.Length * 3;
            check(ordinal == primitive.GetProperty("extras").GetProperty("sourceMaterialOrdinal").GetInt32()
                && sourceIds.SequenceEqual(Enumerable.Range(0, faces.Count).Where(i => faces[i].Material == ordinal)), "Material grouping preserves all global source triangle IDs in original order.");
            float[] p = Floats(a.GetProperty("POSITION").GetInt32(), "VEC3", count, 3), n = Floats(a.GetProperty("NORMAL").GetInt32(), "VEC3", count, 3),
                uv = Floats(a.GetProperty("TEXCOORD_0").GetInt32(), "VEC2", count, 2), influence = Floats(a.GetProperty("WEIGHTS_0").GetInt32(), "VEC4", count, 4);
            var jointBytes = Access(a.GetProperty("JOINTS_0").GetInt32(), 5123, "VEC4", count, 8);
            var indexBytes = Access(primitive.GetProperty("indices").GetInt32(), 5125, "SCALAR", count, 4);
            for (int f = 0; f < sourceIds.Length; f++)
            {
                int sourceId = sourceIds[f]; check(allSourceIds.Add(sourceId), "No source face is duplicated by grouping.");
                for (int c = 0; c < 3; c++)
                {
                    int v = f * 3 + c; var corner = faces[sourceId].Corners[c]; var sourceP = positions[corner.P]; var sourceN = normals[corner.N];
                    check(p.AsSpan(v * 3, 3).SequenceEqual(new[] { -sourceP[0], sourceP[2], sourceP[1] })
                        && n.AsSpan(v * 3, 3).SequenceEqual(new[] { -sourceN[0], sourceN[2], sourceN[1] })
                        && uv.AsSpan(v * 2, 2).SequenceEqual(corner.T < 0 ? new[] { 0f, 0f } : uvs[corner.T])
                        && BinaryPrimitives.ReadUInt32LittleEndian(indexBytes.Slice(v * 4, 4)) == v,
                        "Each original P/N/T0 corner and winding survives exact basis conversion without reordering.");
                    var positive = groups[vertexGroups[corner.P]].EnumerateArray().Where(g => g.GetProperty("weight").GetSingle() > 0).ToArray();
                    check(positive.Length is > 0 and <= 4, "Source influence oracle fits unchanged Weighted4.");
                    for (int j = 0; j < 4; j++)
                    {
                        ushort joint = BinaryPrimitives.ReadUInt16LittleEndian(jointBytes.Slice(v * 8 + j * 2, 2));
                        check(j < positive.Length ? joint < bones.Length && bones[joint].Id == positive[j].GetProperty("boneId").GetUInt32()
                            && influence[v * 4 + j] == positive[j].GetProperty("weight").GetSingle() : joint == 0 && influence[v * 4 + j] == 0,
                            "Every positive original bone ID/weight survives unchanged; padding is zero only.");
                    }
                    cornersCompared++;
                }
            }
        }
        check(allSourceIds.Count == 1970 && cornersCompared == 5910 && primitives == 3, "All1970 source faces/5910corners/3material primitives are complete.");
        return new { Sha256 = Sha(bytes), Bytes = bytes.Length, Nodes = 36, Joints = 35, Materials = 3, Primitives = primitives,
            SourceTriangles = allSourceIds.Count, ExactSourceCornersCompared = cornersCompared, FullRigAndInfluencesPreserved = true };
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), HashFile, StringComparer.Ordinal);
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out string? hash) && hash == p.Value);
    private static Dictionary<string, object[]> Caches()
    {
        var result = new Dictionary<string, object[]>();
        foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" })
            result.Add(name, ((IEnumerable)typeof(Read).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray());
        return result;
    }
    private static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class InlineProgress(Action<ImportPublishStage> report) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage value) => report(value); }
}
