using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Campaign;
using MphRead.Mods.Import;

// Opt-in production loader acceptance. Only identity hashes and numeric
// admission metadata are emitted; no models are decoded or frames sampled.
internal static class OwnedLoadProbe
{
    private const int AssemblyMaximum = 256 * 1024 * 1024;
    private const string ManifestSha256 = "d99c74781f2c04b105b07f9baf8ef1eb7e8d06b5c1419cdfc715d2518d873a72";
    private const string MarmSha256 = "1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059";
    private const string TextSha256 = "742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114";
    private const string FontSha256 = "3f89b432c47b96e52d0837fdd1fa2eb1165df9309665640a0bea02208300e7ef";
    private const string MetricsSha256 = "1619a5a096d200058a3781424e7d68a287821ad4fe435a6b45e00cffab3b5a95";
    private sealed class InputProof
    {
        internal required CampaignCreditsLoad.Input Pin;
        internal required string Before;
        internal string? After;
    }

    internal static int Run(string ownedRoot)
    {
        int checks = 0;
        string stage = "Initialize", root = "";
        string? errorType = null, errorMessageSha256 = null;
        string? coreBefore = null, coreAfter = null, toolBefore = null, toolAfter = null;
        string corePath = typeof(CampaignCreditsSource).Assembly.Location;
        string toolPath = typeof(OwnedLoadProbe).Assembly.Location;
        var inputs = new List<InputProof>();
        Dictionary<string, object[]>? cachesBefore = null, cachesAfter = null;
        bool cachesUnchanged = false;
        CampaignCreditsCatalog? catalog = null;
        try
        {
            coreBefore = HashAssembly(corePath); toolBefore = HashAssembly(toolPath);
            cachesBefore = SnapshotCaches();
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRoot));
            var manifest = CampaignCreditsLoad.Inputs.ToArray();
            Check(manifest.Length == 33 && manifest.Select(p => p.Path).Distinct(StringComparer.Ordinal).Count() == 33,
                "Production loader source inventory differs.");
            string manifestText = String.Concat(manifest.Select(p => p.Path + "|"
                + p.Bytes.ToString(CultureInfo.InvariantCulture) + "|" + p.Sha256 + "\n"));
            Check(Hash(Encoding.UTF8.GetBytes(manifestText)) == ManifestSha256,
                "Production loader manifest differs from the independent pinned inventory identity.");
            stage = "HashSourceBefore";
            foreach (var pin in manifest)
            {
                string before = HashSource(root, pin);
                inputs.Add(new() { Pin = pin, Before = before });
                Check(before == pin.Sha256, "Owned input differs from its pinned source identity.");
            }
            stage = "ProductionLoaderRead";
            catalog = CampaignCreditsLoad.Read(new(root, "AMHE1", Language.English), CancellationToken.None);
            stage = "ValidateDetachedCatalog";
            Check(catalog.Document.MarmSha256 == MarmSha256 && catalog.Document.TextSha256 == TextSha256,
                "Loaded frontend identities differ.");
            Check(catalog.Document.Pages.Select(p => p.Index).SequenceEqual(new[] { 6, 7, 8, 9, 10, 11, 12, 49, 50 })
                && catalog.Document.Pages.Sum(p => p.Items.Count) == 74 && catalog.Document.Strings.Count == 48,
                "Loaded selected pages, items or text inventory differs.");
            Check(catalog.Widgets.Count == 39 && catalog.Models.Count == 8 && catalog.TexturePairs.Count == 28,
                "Loaded model, widget or texture-pair inventory differs.");
            Check(catalog.Font.ModelSha256 == FontSha256 && catalog.Font.MetricsSha256 == MetricsSha256
                && catalog.Font.GlyphCount == 480, "Loaded font identities or metric extent differ.");
            Check(catalog.Frames.MarmSha256 == MarmSha256 && catalog.Frames.TextSha256 == TextSha256
                && catalog.Frames.Frames.Count == 39 && catalog.Frames.Animations.Count == 18,
                "Loaded frame table differs from the exact frontend source.");
            var budget = catalog.Budget;
            Check(budget.SourceBytes == 406552 && budget.SamplerCount == 17 && budget.SamplerVertices == 456
                && budget.SamplerTriangles == 282 && budget.TextureRgbaBytes == 904192
                && budget.TextLayouts == 174 && budget.TextCharacters == 10704 && budget.SamplerPixels == 388864,
                "Loaded aggregate admission budget differs from its pinned metadata witness.");
            var expectedModelHashes = manifest.Where(p => p.Path.EndsWith("_Model.bin", StringComparison.Ordinal))
                .Select(p => p.Sha256).Order(StringComparer.Ordinal);
            Check(catalog.Models.Keys.Order(StringComparer.Ordinal).SequenceEqual(expectedModelHashes),
                "Loaded canonical model identities differ from the exact source inventory.");
            var expectedAnimations = manifest.Where(p => p.Path.EndsWith("_Anim.bin", StringComparison.Ordinal))
                .Select(p => p.Sha256).Order(StringComparer.Ordinal).ToArray();
            Check(catalog.Frames.Animations.Values.Select(p => p.Sha256).Order(StringComparer.Ordinal)
                .SequenceEqual(expectedAnimations), "Loaded duration identities differ from the exact source inventory.");
            Check(inputs.Count == 33, "Production loader probe did not track its complete source inventory.");
            stage = "Complete";
        }
        catch (Exception error) { RecordFailure(error); }
        finally
        {
            foreach (var input in inputs)
            {
                try { input.After = HashSource(root, input.Pin); }
                catch (Exception error) { RecordFailure(error); }
            }
            try
            {
                coreAfter = HashAssembly(corePath); toolAfter = HashAssembly(toolPath);
                cachesAfter = SnapshotCaches();
                cachesUnchanged = cachesBefore != null && cachesBefore.Count == cachesAfter.Count
                    && cachesBefore.All(p => cachesAfter.TryGetValue(p.Key, out var values) && p.Value.SequenceEqual(values));
            }
            catch (Exception error) { RecordFailure(error); }
        }
        bool sourceUnchanged = inputs.Count > 0 && inputs.All(p => p.After == p.Before);
        bool assembliesUnchanged = coreBefore != null && toolBefore != null && coreBefore == coreAfter && toolBefore == toolAfter;
        bool passed = errorType == null && catalog != null && inputs.Count == 33
            && sourceUnchanged && assembliesUnchanged && cachesUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Edition = "AMHE1", Passed = passed, Stage = stage, Checks = checks,
            ErrorType = errorType, ErrorMessageSha256 = errorMessageSha256,
            MetadataOutputOnly = true, ProductionLoaderUsed = true, ManifestSha256,
            SourceInputCount = inputs.Count, SourceUnchanged = sourceUnchanged,
            CoreAssemblySha256Before = coreBefore, CoreAssemblySha256After = coreAfter,
            ToolAssemblySha256Before = toolBefore, ToolAssemblySha256After = toolAfter,
            AssembliesUnchanged = assembliesUnchanged, CachesUnchanged = cachesUnchanged,
            CacheCountsBefore = cachesBefore?.ToDictionary(p => p.Key, p => p.Value.Length),
            CacheCountsAfter = cachesAfter?.ToDictionary(p => p.Key, p => p.Value.Length),
            Catalog = catalog == null ? null : new { ModelCount = catalog.Models.Count, WidgetCount = catalog.Widgets.Count,
                TexturePairCount = catalog.TexturePairs.Count, catalog.Budget, catalog.Font.ModelSha256,
                catalog.Font.MetricsSha256, catalog.Document.MarmSha256, catalog.Document.TextSha256 },
            ModelsDecoded = false, FramesSampled = false, RenderingActivated = false, GLActivated = false,
            SceneCreated = false, UiAudioOrSaveTouched = false, SourcePayloadsWritten = false,
            GameTextExported = false, CoordinatesExported = false, PixelsExported = false,
            SourceInputs = inputs.Select(p => new { FileId = p.Pin.Path, p.Pin.Bytes,
                ExpectedSha256 = p.Pin.Sha256, Sha256Before = p.Before, Sha256After = p.After }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;

        void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidDataException(message); }
        void RecordFailure(Exception error)
        {
            if (errorType != null) return;
            errorType = error.GetType().Name; errorMessageSha256 = Hash(Encoding.UTF8.GetBytes(error.Message));
        }
    }

    private static string HashSource(string root, CampaignCreditsLoad.Input pin)
    {
        if (pin.Bytes is < 1 or > CampaignCreditsLoad.MaxReadBytes) throw new InvalidDataException("Owned source extent is invalid.");
        string path = Path.GetFullPath(Path.Combine(root, pin.Path.Replace('/', Path.DirectorySeparatorChar)));
        LocalImportBundle.CheckPath(path, root);
        using var stream = CampaignCreditsSourceFile.Open(path, CancellationToken.None);
        if (stream.Length != pin.Bytes) throw new InvalidDataException("Owned source extent differs from its pin.");
        string hash = HashExtent(stream, pin.Bytes);
        LocalImportBundle.CheckPath(path, root);
        return hash;
    }
    private static string HashAssembly(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > AssemblyMaximum) throw new InvalidDataException("Assembly hash extent is invalid.");
        return HashExtent(stream, stream.Length);
    }
    private static string HashExtent(Stream stream, long expected)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        try
        {
            long remaining = expected;
            while (remaining > 0)
            {
                int count = stream.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                if (count == 0) throw new EndOfStreamException("Hash input was truncated.");
                hash.AppendData(buffer, 0, count); remaining -= count;
            }
            if (stream.ReadByte() != -1 || stream.Length != expected) throw new InvalidDataException("Hash input changed its extent.");
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally { Array.Clear(buffer); }
    }
    private static Dictionary<string, object[]> SnapshotCaches()
    {
        var result = new Dictionary<string, object[]>(StringComparer.Ordinal);
        foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" })
        {
            var field = typeof(Read).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidDataException("CPU cache witness is unavailable.");
            var values = field.GetValue(null) as IEnumerable
                ?? throw new InvalidDataException("CPU cache witness has unsupported shape.");
            result.Add(name, values.Cast<object>().ToArray());
        }
        return result;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
