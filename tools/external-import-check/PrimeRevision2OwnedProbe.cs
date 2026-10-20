using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;

// Explicit owned-source CPU probe. Only hashes, canonical IDs and counts leave
// the disposable import tree; no runtime or source-material qualification.
internal static class PrimeRevision2OwnedProbe
{
    private static readonly string[] Ids = { "trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma" };
    private const string MetroidSha1 = "2147a5ea1ff654d6bbce749d5f7c1170f98a601e";
    private const string AudioSha1 = "157e0841155cc7b699176dd7afc746c7048dbc28";

    internal static int Run(string source)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-revision2-owned-" + Guid.NewGuid().ToString("N"));
        string imports = Path.Combine(temporary, "imports");
        string core = typeof(RetroCharacterPreviewExporter).Assembly.Location, tool = typeof(PrimeRevision2OwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var cachesBefore = Caches();
        string? sourceBefore = null, sourceAfter = null, errorType = null, errorSha = null;
        bool cleaned = false, reused = false, canceledPreserved = false, removed = false, retained = false, cachesUnchanged = false;
        int checks = 0; var archiveMetadata = new List<object>(); var bundles = new List<object>();
        var generations = new Dictionary<string, ImportBundleInspection>(StringComparer.Ordinal);
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
            var exported = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, selectedIds: Ids);
            Check(exported.Count == 4 && exported.Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(Ids.Select(id => id + ".obj").Order(StringComparer.Ordinal)),
                "One real default export must publish all four selected Troopers only.");
            string fingerprint = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(
                "AudioGrp.pak:" + AudioSha1 + "\nMetroid1.pak:" + MetroidSha1)));
            foreach (string id in Ids)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
                var legacy = RetroCharacterPreviewExporter.BundleSpec(id);
                Check(spec.Version == 2 && legacy.Version == 1 && spec.RequiredComponents.Length == 4
                    && spec.SourceFingerprint == fingerprint && spec.SourceFingerprint != legacy.SourceFingerprint
                    && spec.RecipeHash != legacy.RecipeHash, "Recipe identity must bind the exact new source without changing legacy v1.");
                var state = RetroCharacterPreviewExporter.InspectBundle(imports, id);
                Check(state.State == ImportBundleState.Ready
                    && LocalImportBundle.Inspect(imports, id, spec).State == ImportBundleState.Ready
                    && LocalImportBundle.Inspect(imports, id, legacy).State == ImportBundleState.UnsupportedSource,
                    "Edition-aware inspection accepts v2 while an explicit legacy recipe refuses the different source.");
                var receipt = state.Receipt ?? throw new InvalidDataException("Missing committed source receipt.");
                Check(receipt.Id == id && receipt.Version == 2 && receipt.SourceFingerprint == fingerprint
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
                generations.Add(id, state);
                bundles.Add(new { Id = id, receipt.Version, receipt.RecipeHash, receipt.SourceFingerprint, receipt.Generation,
                    Positions = positions, Triangles = triangles, Bones = bones, RequiredRecipeComponents = spec.RequiredComponents.Length,
                    CommittedFiles = state.Files.Count, Components = componentMetadata });
            }
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
            && cleaned && reused && canceledPreserved && removed && retained && bundles.Count == 4;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Kind = "prime-usa-revision2-owned-import", Passed = passed, Checks = checks,
            MetadataOutputOnly = true, CpuOnly = true, DefaultImporterUsed = true, RuntimeActivated = false,
            RenderingActivated = false, MaterialsQualified = false, AbilitiesQualified = false,
            SourceGameId = "GM8E01", Revision = 2, DiscNumber = 0, Archives = archiveMetadata, Bundles = bundles,
            SameInputGenerationReused = reused, PreCancelledReceiptsPreserved = canceledPreserved,
            DiscoveryRemoved = removed, OldGenerationsRetainedUntilCleanup = retained,
            SourceBeforeSha256 = sourceBefore, SourceAfterSha256 = sourceAfter, SourceUnchanged = sourceUnchanged,
            CoreBeforeSha256 = coreBefore, CoreAfterSha256 = coreAfter, ToolBeforeSha256 = toolBefore, ToolAfterSha256 = toolAfter,
            BinariesUnchanged = binariesUnchanged, CachesUnchanged = cachesUnchanged,
            TemporaryDerivedPayloadsDeleted = cleaned, SourcePayloadsRetained = !cleaned,
            ErrorType = errorType, ErrorMessageSha256 = errorSha
        }));
        return passed ? 0 : 1;
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
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
