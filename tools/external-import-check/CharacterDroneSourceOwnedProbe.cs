using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;

// Owned-source authoring acceptance only. No graphics/actor/ability admission.
internal static class CharacterDroneSourceOwnedProbe
{
    private const string BaselineCore = "e0a47b84083540f3465560f6c7c36ca716cdcf110e3f45718e190cba7b3b4b2f";
    private const string SourceSha = "3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844";
    private static readonly string[] DroneIds = ["trooper-drone-alpha", "trooper-drone-gamma"];
    private static readonly string[] TrooperIds = ["trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma"];

    internal static int Run(string source, bool captureBaseline = false)
    {
        string core = typeof(RetroCharacterPreviewExporter).Assembly.Location;
        string tool = typeof(CharacterDroneSourceOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool);
        string temporary = Path.Combine(Path.GetTempPath(), "prime-drone-source-" + Guid.NewGuid().ToString("N"));
        string? before = null, after = null, failed = null, error = null;
        int checks = 0; bool sourceIo = false, cleaned = false;
        uint rng1 = Rng.Rng1, rng2 = Rng.Rng2; var bundles = new List<object>();
        void Check(bool condition, string label)
        {
            if (!condition) { failed = label; throw new InvalidDataException(label); }
            checks++;
        }
        try
        {
            if (!captureBaseline)
            {
                // This deliberately precedes any source path validation or I/O:
                // the old-core tests-first result must not touch owned media.
                Check(DroneIds.All(id => RetroCharacterPreviewExporter.Sources.Count(s => s.Id == id) == 1),
                    "independent Alpha/Gamma source-drone authoring IDs exist before owned-source I/O");
                Check(RetroCharacterPreviewExporter.Sources.Where(s => DroneIds.Contains(s.Id)).All(s =>
                    s.Game == ExternalGameKind.MetroidPrime && s.Ancs == 0x5e058399),
                    "new drone sources bind their own Prime ANCS rather than Trooper body ANCS");
                // The generated schema is agreed with the production owner
                // before this fixture makes filename or manifest assumptions.
                Check(false, "full independent drone authoring contract awaits agreed generated schema");
            }
            Check(coreBefore == BaselineCore, "old Trooper export witness uses exact frozen qualified e0a core");
            sourceIo = true; before = HashFile(source);
            Check(before == SourceSha, "baseline source is the previously verified owned Prime USA Revision2 RVZ");
            string imports = Path.Combine(temporary, "imports");
            var files = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, selectedIds: TrooperIds);
            Check(files.Count == 4, "baseline exports exactly four original Trooper appearances");
            string fingerprint = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(
                "AudioGrp.pak:157e0841155cc7b699176dd7afc746c7048dbc28\nMetroid1.pak:2147a5ea1ff654d6bbce749d5f7c1170f98a601e")));
            foreach (string id in TrooperIds)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
                var state = RetroCharacterPreviewExporter.InspectBundle(imports, id);
                Check(spec.Version == 6 && spec.SourceFingerprint == fingerprint && spec.RequiredComponents.Length == 13
                    && state.State == ImportBundleState.Ready && state.Receipt != null && state.Files.Count == 13,
                    "baseline retains original Rev2 Trooper v6 identity and all13 committed components: " + id);
                var components = state.Files.OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path =>
                {
                    Check(Path.GetFullPath(path).StartsWith(Path.GetFullPath(imports) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                        "baseline component stays inside disposable import root");
                    var component = state.Receipt!.Components.Single(c => c.Id == Path.GetFileName(path));
                    string sha = HashFile(path); long bytes = new FileInfo(path).Length;
                    Check(component.Sha256 == sha && component.Length == bytes, "baseline receipt matches complete component bytes");
                    return new { Name = Path.GetFileName(path), Length = bytes, Sha256 = sha };
                }).ToArray();
                bundles.Add(new { Id = id, spec.Version, spec.SourceFingerprint, spec.RecipeHash, Components = components });
            }
            Check(bundles.Count == 4 && Rng.Rng1 == rng1 && Rng.Rng2 == rng2, "baseline captures52 original component hashes without native RNG mutation");
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            Console.Error.WriteLine("OWNED DRONE SOURCE failure: " + ex);
        }
        finally
        {
            try
            {
                string target = Path.GetFullPath(temporary);
                if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(target).StartsWith("prime-drone-source-", StringComparison.Ordinal))
                    throw new InvalidDataException("Drone fixture cleanup escaped its own temporary root.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                cleaned = !Directory.Exists(target);
                if (sourceIo) after = HashFile(source);
                Check(cleaned && (!sourceIo || before != null && before == after), "disposable imports deleted and any opened source unchanged");
                Check(HashFile(core) == coreBefore && HashFile(tool) == toolBefore && Rng.Rng1 == rng1 && Rng.Rng2 == rng2,
                    "core/tool and native RNG unchanged by authoring fixture");
            }
            catch (Exception ex) { error ??= ex.GetType().Name; }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Pass = error == null, Checks = checks, FailedCheck = failed, ErrorType = error,
                BaselineOnly = captureBaseline, CoreSha256 = coreBefore, ToolSha256 = toolBefore,
                SourceIoAttempted = sourceIo, SourceSha256Before = before, SourceSha256After = after,
                TemporaryDerivedDataDeleted = cleaned, Bundles = bundles,
                Scope = captureBaseline ? "four original Trooper v6 export metadata witnesses,52 component hashes only"
                    : "tests-first independent Alpha/Gamma source catalog admission before owned asset I/O; generated schema acceptance pending",
                Unqualified = new[] { "GPU", "actors", "drone ability or gameplay", "native UI", "replay/network admission" }
            }));
        }
        return error == null ? 0 : 1;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
