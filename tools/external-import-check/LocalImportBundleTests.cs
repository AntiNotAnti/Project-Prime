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
            VerifyComponentNames(root);
            VerifyRemoval(root);
            VerifySymlinkPaths(root);
            VerifyMacSystemAliases();
            VerifyDetachedPublicationPaths(root);
            var completeSpec = new ImportBundleSpec("complete-refresh", 1, new string('a',64), new string('b',40), new[]{"body.obj"});
            LocalImportBundle.Publish(root,completeSpec,new Dictionary<string,byte[]>{["body.obj"]=new byte[]{1},["optional.bin"]=new byte[]{2}});
            LocalImportBundle.Publish(root,completeSpec,new Dictionary<string,byte[]>{["body.obj"]=new byte[]{3}},replaceAllComponents:true);
            var refreshed=LocalImportBundle.Inspect(root,completeSpec.Id,completeSpec);
            Check(refreshed.State==ImportBundleState.Ready && refreshed.Files.Count==1 && refreshed.Files[0].EndsWith("body.obj"),
                "complete selected refresh cannot retain a stale optional component");
            Console.WriteLine("IMPORT BUNDLE PASS: integrity/states, required parts, bounded flat artifacts, cancellation/fault rollback, immutable updates, coalescing, retained removal and writer lease");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void VerifyDetachedPublicationPaths(string root)
    {
        var spec = new ImportBundleSpec("detached-return-fixture", 1, new string('a', 64), new string('b', 40), new[] { "body.obj" });
        var replacements = new Dictionary<string, byte[]> { ["body.obj"] = new byte[] { 1 }, ["body.skeleton.json"] = new byte[] { 2 } };
        string[] expected = replacements.Keys.ToArray();
        var published = LocalImportBundle.Publish(root, spec, replacements, progress: new Progress(stage =>
        {
            if (stage != ImportPublishStage.BeforeCommit) return;
            replacements.Clear();
            replacements.Add("../escape.json", new byte[] { 3 });
        }));
        var inspection = LocalImportBundle.Inspect(root, spec.Id, spec);
        Check(inspection.State == ImportBundleState.Ready && published.All(File.Exists)
            && published.Select(Path.GetFileName).SequenceEqual(expected)
            && inspection.Receipt!.Components.Select(component => component.Id).Order().SequenceEqual(expected.Order()),
            "post-clone caller mutation cannot change committed return paths or receipt inventory");
    }

    private static void VerifySymlinkPaths(string root)
    {
        string physical = Path.Combine(root, "symlink-target"), alias = Path.Combine(root, "user-alias");
        Directory.CreateDirectory(physical);
        try { Directory.CreateSymbolicLink(alias, physical); }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
        {
            Console.WriteLine("IMPORT BUNDLE SKIP: symbolic-link fixture requires Windows symbolic-link permission.");
            return;
        }
        var spec = new ImportBundleSpec("symlink-fixture", 1, new string('a', 64), new string('b', 40), new[] { "body.obj" });
        var parts = new Dictionary<string, byte[]> { ["body.obj"] = new byte[] { 1, 2, 3 } };
        var installed = LocalImportBundle.Publish(physical, spec, parts);
        string pointer = Path.Combine(physical, LocalImportBundle.StorageDirectory, spec.Id, "current.json");
        byte[] before = File.ReadAllBytes(pointer);
        foreach (string rejectedRoot in new[] { alias, alias + Path.DirectorySeparatorChar, Path.Combine(alias, "chosen-root") })
        {
            Expect<InvalidDataException>(() => LocalImportBundle.Publish(rejectedRoot, spec, parts), "reject root/ancestor alias publication");
            Expect<InvalidDataException>(() => LocalImportBundle.Remove(rejectedRoot, spec.Id), "reject root/ancestor alias removal");
            Check(LocalImportBundle.HasReceipt(rejectedRoot, spec.Id) == (rejectedRoot != Path.Combine(alias, "chosen-root")),
                "presence discovery distinguishes existing unsafe receipt from absent redirected child");
            Check(LocalImportBundle.Inspect(rejectedRoot, spec.Id, spec).State == ImportBundleState.Failed,
                "root/ancestor alias inspection fails closed");
        }
        Check(!Directory.Exists(Path.Combine(physical, "chosen-root")), "ancestor alias cannot create redirected output");
        Check(File.ReadAllBytes(pointer).SequenceEqual(before) && File.Exists(installed[0]),
            "rejected aliases preserve real receipt and generation");

        // Missing targets must not hide reparse points from File.Exists checks.
        string dangling = Path.Combine(root, "dangling-alias");
        Directory.CreateSymbolicLink(dangling, Path.Combine(root, "missing-target"));
        Expect<InvalidDataException>(() => LocalImportBundle.Publish(Path.Combine(dangling, "child"), spec, parts),
            "reject dangling ancestor alias");

        // Validate ancestry inside the storage tree too, including a linked receipt.
        string retained = pointer + ".retained";
        File.Move(pointer, retained);
        try
        {
            File.CreateSymbolicLink(pointer, retained);
            Check(LocalImportBundle.Inspect(physical, spec.Id, spec).State == ImportBundleState.Failed, "reject symlink receipt");
            Expect<InvalidDataException>(() => LocalImportBundle.Remove(physical, spec.Id), "cannot retire symlink receipt");
        }
        finally { File.Delete(pointer); File.Move(retained, pointer); }
    }

    private static void VerifyMacSystemAliases()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var spec = new ImportBundleSpec("system-alias-fixture", 1, new string('a', 64), new string('b', 40), new[] { "body.json" });
        var parts = new Dictionary<string, byte[]> { ["body.json"] = new byte[] { 1 } };
        foreach (string alias in new[] { "/var", "/tmp", "/etc" })
        {
            string? target = new DirectoryInfo(alias).LinkTarget;
            string expected = "/private" + alias;
            if (target == null || Path.GetFullPath(Path.Combine("/", target)) != expected) continue;
            // Read-only absence probes exercise /etc without requiring system writes.
            string missing = Path.Combine(alias, "prime-nonexistent-bundle-" + Guid.NewGuid().ToString("N"));
            Check(LocalImportBundle.Inspect(missing, spec.Id, spec).State == ImportBundleState.NotInstalled,
                "known macOS system alias accepts ordinary absent root: " + alias);
        }
        string? temporaryTarget = new DirectoryInfo("/tmp").LinkTarget;
        if (temporaryTarget == null || Path.GetFullPath(Path.Combine("/", temporaryTarget)) != "/private/tmp") return;
        string temporary = Path.Combine("/tmp", "prime-system-alias-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            var installed = LocalImportBundle.Publish(temporary + Path.DirectorySeparatorChar, spec, parts);
            Check(installed.All(path => path.StartsWith("/private/tmp/", StringComparison.Ordinal)), "known system alias returns normalized generation paths");
            Check(LocalImportBundle.HasReceipt(temporary, spec.Id)
                && LocalImportBundle.Inspect(temporary, spec.Id, spec).State == ImportBundleState.Ready,
                "known macOS temp alias remains discoverable and readable");
            Check(LocalImportBundle.Remove(temporary, spec.Id), "known macOS temp alias remains removable");
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static void VerifyComponentNames(string root)
    {
        string[] names = { "body.obj", "body.skeleton.json", "body.weights.json", "body.ancs.bin", "body.source-rig.glb",
            "lod0.material.2.json", new string('a', 96) + ".obj" };
        var spec = new ImportBundleSpec("artifact-fixture", 1, new string('c', 64), new string('d', 40), names);
        var parts = names.ToDictionary(name => name, name => Encoding.UTF8.GetBytes("synthetic fixture: " + name));
        var installed = LocalImportBundle.Publish(root, spec, parts);
        var ready = LocalImportBundle.Inspect(root, spec.Id, spec);
        Check(ready.State == ImportBundleState.Ready && ready.Receipt!.Schema == 1
            && ready.Files.Count == names.Length, "supported artifact names retain schema 1");
        Check(installed.All(file => names.Contains(Path.GetFileName(file))), "artifacts stay flat in their generation");
        string pointer = Path.Combine(root, LocalImportBundle.StorageDirectory, spec.Id, "current.json");
        byte[] before = File.ReadAllBytes(pointer);
        var receipt = ready.Receipt!;
        foreach (string invalid in new[] { "", ".json", "body..json", "body.obj.", "body/mesh.obj", "body\\mesh.bin",
            "../body.glb", "/body.json", "C:/body.json", "body:mesh.obj", "Body.obj", "body.GLB", "body mesh.obj",
            "bödy.obj", "body.exe", "body.json.exe", "con.obj", "aux.bin", "nul.json", "lpt1.glb", "com9.obj",
            new string('a', 97) + ".obj" })
        {
            Expect<InvalidDataException>(() => LocalImportBundle.Publish(root, spec,
                new Dictionary<string, byte[]> { [invalid] = new byte[] { 1 } }), "unsafe artifact replacement: " + invalid);
            Expect<InvalidDataException>(() => LocalImportBundle.Publish(root,
                spec with { RequiredComponents = new[] { invalid } }, parts), "unsafe artifact requirement: " + invalid);
            Check(File.ReadAllBytes(pointer).SequenceEqual(before), "unsafe artifact leaves old receipt");
            File.WriteAllBytes(pointer, JsonSerializer.SerializeToUtf8Bytes(receipt with
            { Components = new[] { receipt.Components[0] with { Id = invalid } } }));
            Check(LocalImportBundle.Inspect(root, spec.Id, spec).State == ImportBundleState.Failed,
                "unsafe artifact receipt rejected: " + invalid);
            File.WriteAllBytes(pointer, before);
        }
    }

    private static void VerifyRemoval(string root)
    {
        var spec = new ImportBundleSpec("removal-fixture", 1, new string('e', 64), new string('f', 40), new[] { "body.obj" });
        var parts = new Dictionary<string, byte[]> { ["body.obj"] = Encoding.UTF8.GetBytes("synthetic immutable body") };
        var installed = LocalImportBundle.Publish(root, spec, parts);
        string folder = Path.Combine(root, LocalImportBundle.StorageDirectory, spec.Id);
        string pointer = Path.Combine(folder, "current.json"), lockPath = Path.Combine(folder, "writer.lock");
        string source = Path.Combine(root, "unmanaged-source.bin");
        File.WriteAllText(source, "source remains private and untouched");
        byte[] before = File.ReadAllBytes(pointer);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Expect<OperationCanceledException>(() => LocalImportBundle.Remove(root, spec.Id, cancelled.Token), "cancel removal");
        }
        Check(File.ReadAllBytes(pointer).SequenceEqual(before)
            && !Directory.EnumerateFiles(folder, "retired-*.json").Any(), "cancel removal retains current receipt");
        using (var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Expect<IOException>(() => LocalImportBundle.Remove(root, spec.Id), "remove shares game/Studio writer lease");
        Check(File.ReadAllBytes(pointer).SequenceEqual(before), "busy removal retains current receipt");

        // A scene can keep its file open throughout withdrawal and reinstallation.
        using var live = new FileStream(installed[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        Check(LocalImportBundle.Remove(root, spec.Id), "remove installed bundle");
        Check(!File.Exists(pointer) && !LocalImportBundle.HasReceipt(root, spec.Id)
            && LocalImportBundle.Inspect(root, spec.Id, spec).State == ImportBundleState.NotInstalled,
            "removed receipt no longer discovered");
        string retired = Directory.GetFiles(folder, "retired-*.json").Single();
        Check(File.ReadAllBytes(retired).SequenceEqual(before), "withdrawal retains exact receipt bytes");
        Check(File.Exists(lockPath) && File.Exists(installed[0])
            && File.ReadAllText(source) == "source remains private and untouched", "remove retains lock, generation and source");
        using (var reader = new StreamReader(live, Encoding.UTF8, leaveOpen: true))
            Check(reader.ReadToEnd() == "synthetic immutable body", "live generation remains readable");
        Check(!LocalImportBundle.Remove(root + Path.DirectorySeparatorChar, spec.Id)
            && Directory.GetFiles(folder, "retired-*.json").Length == 1, "repeated removal harmless");

        var reinstalled = LocalImportBundle.Publish(root, spec, parts);
        Check(LocalImportBundle.Inspect(root, spec.Id, spec).State == ImportBundleState.Ready
            && reinstalled.SequenceEqual(installed) && File.Exists(retired), "reinstall reuses immutable generation and retains retired receipt");
        Check(LocalImportBundle.Remove(root, spec.Id)
            && Directory.GetFiles(folder, "retired-*.json").Length == 2, "each withdrawal retains a unique receipt");
        Expect<InvalidDataException>(() => LocalImportBundle.Remove(root, "../escape"), "invalid removal ID");
        Check(!LocalImportBundle.Remove(root, "absent-fixture"), "remove absent bundle harmless");
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
