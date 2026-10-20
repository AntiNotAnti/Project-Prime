using System.Text;
using System.Text.Json;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class LocalImportBundleTests
{
    private sealed class Progress(Action<ImportPublishStage> action) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage stage) => action(stage); }

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-local-bundle-" + Guid.NewGuid().ToString("N"));
        var spec = new ImportBundleSpec("fixture", 1, new string('a', 64), new string('b', 40), new[] { "body.json", "gun.json" });
        var parts = new Dictionary<string, byte[]> { ["body.json"] = Encoding.UTF8.GetBytes("body-v1"), ["gun.json"] = Encoding.UTF8.GetBytes("gun-v1") };
        string pointer = Path.Combine(root, LocalImportBundle.StorageDirectory, "fixture", "current.json");
        try
        {
            Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.NotInstalled, "not installed");
            var installed = LocalImportBundle.Publish(root, spec, parts);
            Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Ready, "complete ready bundle");
            Check(LocalImportBundle.Inspect(root + Path.DirectorySeparatorChar, "fixture", spec).State == ImportBundleState.Ready,
                "root trailing separator normalized");
            Check(LocalImportBundle.Publish(root + Path.DirectorySeparatorChar, spec, parts).SequenceEqual(installed),
                "root trailing separator update coalesces");
            byte[] oldPointer = File.ReadAllBytes(pointer);
            Check(LocalImportBundle.Publish(root, spec, parts).SequenceEqual(installed), "identical jobs coalesce");
            Check(LocalImportBundle.Inspect(root, "fixture", spec with { Version = 2 }).State == ImportBundleState.Outdated, "old recipe is outdated");
            Check(LocalImportBundle.Inspect(root, "fixture", spec with { SourceFingerprint = new string('c', 40) }).State == ImportBundleState.UnsupportedSource, "wrong source edition");
            Check(LocalImportBundle.Inspect(root, "fixture", spec with { RequiredComponents = new[] { "ball.json" } }).State == ImportBundleState.Incomplete, "body alone cannot satisfy missing ball");

            parts["body.json"] = Encoding.UTF8.GetBytes("body-v2");
            foreach (var boundary in new[] { ImportPublishStage.Checking, ImportPublishStage.Staging, ImportPublishStage.Verifying, ImportPublishStage.BeforeCommit })
            {
                using var cancel = new CancellationTokenSource();
                Expect<OperationCanceledException>(() => LocalImportBundle.Publish(root, spec, parts, cancel.Token,
                    new Progress(stage => { if (stage == boundary) cancel.Cancel(); })), "cancel " + boundary);
                Check(File.ReadAllBytes(pointer).SequenceEqual(oldPointer), "cancel preserves previous receipt " + boundary);
                Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Ready, "old bundle still ready " + boundary);
                Expect<IOException>(() => LocalImportBundle.Publish(root, spec, parts, progress:
                    new Progress(stage => { if (stage == boundary) throw new IOException("Injected storage failure"); })), "fault " + boundary);
                Check(File.ReadAllBytes(pointer).SequenceEqual(oldPointer), "fault preserves previous receipt " + boundary);
            }
            Check(!Directory.EnumerateDirectories(Path.GetDirectoryName(pointer)!, ".stage-*").Any(), "no staged leftovers");
            using (var held = new FileStream(Path.Combine(Path.GetDirectoryName(pointer)!, "writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Expect<IOException>(() => LocalImportBundle.Publish(root, spec, parts), "game/Studio writer lease");

            var updated = LocalImportBundle.Publish(root, spec, parts);
            Check(updated[0] != installed[0] && File.ReadAllText(installed[0]) == "body-v1", "old immutable generation survives update");
            Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Ready, "new complete generation ready");
            string gun = updated.Single(p => p.EndsWith("gun.json"));
            File.Delete(gun);
            Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Incomplete, "removed component incomplete");
            File.WriteAllText(gun, "tampered");
            Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Failed, "modified component failed");

            var repaired = LocalImportBundle.Publish(root, spec, parts);
            Check(repaired[0] != updated[0] && LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Ready,
                "reimport repairs corruption in a new immutable generation");

            // Malformed receipt fields never become paths or a partial Ready set.
            var receipt = JsonSerializer.Deserialize<ImportBundleReceipt>(File.ReadAllBytes(pointer))!;
            foreach (string invalid in new[] { "../escape.json", "Body.json", "C:/escape.json" })
            {
                File.WriteAllBytes(pointer, JsonSerializer.SerializeToUtf8Bytes(receipt with
                { Components = new[] { receipt.Components[0] with { Id = invalid } } }));
                Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Failed, "invalid component rejected");
            }
            File.WriteAllBytes(pointer, JsonSerializer.SerializeToUtf8Bytes(receipt with { Components = new[] { receipt.Components[0], receipt.Components[0] } }));
            Check(LocalImportBundle.Inspect(root, "fixture", spec).State == ImportBundleState.Failed, "duplicate receipt component rejected");
            File.WriteAllBytes(pointer, oldPointer);
            var before = File.ReadAllBytes(pointer);
            Expect<InvalidDataException>(() => LocalImportBundle.Publish(root, spec, new Dictionary<string, byte[]> { ["../escape.json"] = new byte[] { 1 } }), "unsafe import path");
            Check(File.ReadAllBytes(pointer).SequenceEqual(before), "unsafe request leaves old pointer");
            Console.WriteLine("IMPORT BUNDLE PASS: integrity/states, required parts, cancellation/fault rollback, immutable updates, coalescing and cross-process lease");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void VerifyRealEchoesSource(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-owned-echoes-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            var maps = EchoesPrivateMapImport.ReadAllArenas(source);
            var installed = EchoesPrivateMapImport.InstallLocalDefinitions(maps, root);
            var bundle = LocalImportBundle.Inspect(root, "echoes", EchoesPrivateMapImport.CurrentBundleSpec);
            Check(maps.Count == 6 && installed.Count == 6 && bundle.State == ImportBundleState.Ready, "real source six-map complete receipt");
            var entries = new MapCatalog(root).Refresh();
            Check(entries.Count(e => e.Definition != null && e.Validation.IsValid) == 6, "real source catalog generation");
            Check(entries.All(e => e.Definition?.RequiresPrivateSource == true), "real source remains private");
            string pointer = Path.Combine(root, LocalImportBundle.StorageDirectory, "echoes", "current.json");
            byte[] before = File.ReadAllBytes(pointer);
            using var cancel = new CancellationTokenSource();
            Expect<OperationCanceledException>(() => EchoesPrivateMapImport.InstallLocalDefinitions(maps, root,
                cancel.Token, new Progress(stage => { if (stage == ImportPublishStage.BeforeCommit) cancel.Cancel(); })),
                "real source cancellation before publication");
            Check(File.ReadAllBytes(pointer).SequenceEqual(before), "real source cancellation retains old receipt");
            Check(LocalImportBundle.Inspect(root, "echoes", EchoesPrivateMapImport.CurrentBundleSpec).State == ImportBundleState.Ready,
                "real source old generation remains ready");
            Console.WriteLine("PASS: owned Echoes source verified; six private collision previews compiled, bundled, catalogued and cancellation-safe. No rendering/gameplay qualification implied.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool pass, string message)
    { if (!pass) throw new Exception("Import bundle: " + message); }
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + message);
    }
}
