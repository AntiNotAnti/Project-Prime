using System.Security.Cryptography;
using MphRead.Mods.Import;

// Metadata contracts run without source media. The explicit owned-source probe
// keeps every derived byte in one disposable root, never the real inventory.
internal static class CharacterBundleAcceptance
{
    internal static void RunContract()
    {
        var specs = RetroCharacterPreviewExporter.Sources.Select(s => RetroCharacterPreviewExporter.BundleSpec(s.Id)).ToArray();
        foreach (var spec in specs)
        {
            var again = RetroCharacterPreviewExporter.BundleSpec(spec.Id);
            Check(spec.Version == 1 && spec.RecipeHash.Length == 64 && spec.SourceFingerprint.Length == 40,
                "canonical version and digest contract " + spec.Id);
            Check(spec.RequiredComponents.SequenceEqual(new[] { spec.Id + ".obj", spec.Id + ".skeleton.json", spec.Id + ".weights.json", spec.Id + ".motions.json" }),
                "body, skeleton, weights and motions required together " + spec.Id);
            Check(spec.RecipeHash == again.RecipeHash && spec.SourceFingerprint == again.SourceFingerprint && spec.RequiredComponents.SequenceEqual(again.RequiredComponents),
                "recipe stable across calls " + spec.Id);
            spec.RequiredComponents[0] = "tampered.obj";
            Check(RetroCharacterPreviewExporter.BundleSpec(spec.Id).RequiredComponents[0] == spec.Id + ".obj",
                "required inventory detached from caller " + spec.Id);
        }
        Check(specs.Select(s => s.RecipeHash).Distinct().Count() == 6, "appearance recipes remain independent");
        Check(specs.Where(s=>s.Id.StartsWith("trooper-",StringComparison.Ordinal)).Select(s => s.SourceFingerprint).Distinct().Count() == 1 && specs[0].SourceFingerprint != specs[1].SourceFingerprint,
            "four Troopers share pinned Prime source, Echoes has distinct source");
        var normal=specs.Single(s=>s.Id=="darksamus-echoes");
        var overcharged=specs.Single(s=>s.Id=="darksamus-overcharged-echoes");
        Check(normal.SourceFingerprint==overcharged.SourceFingerprint && normal.RecipeHash!=overcharged.RecipeHash,
            "distinct normal/Overcharged recipes bind the same verified Echoes edition");
        Check(RetroCharacterPreviewExporter.Sources.Single(s=>s.Id==overcharged.Id).NativeName=="Battle_3"
            && RetroCharacterPreviewExporter.Sources.Single(s=>s.Id==normal.Id).NativeName=="Dark_Samus",
            "Overcharged source name cannot silently alias the normal body");
        foreach (string? id in new string?[] { null, "", "DARKSAMUS-ECHOES", "darksamus", "../darksamus-echoes", "trooper-power.obj", "trooper-power " })
            Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.BundleSpec(id!), "unknown/noncanonical identity refused");
        string root = Path.Combine(Path.GetTempPath(), "prime-character-contract-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Expect<OperationCanceledException>(() => RetroCharacterPreviewExporter.ExportFromOwnedGame("missing-owned-source.iso", root, cancellation.Token), "pre-cancel precedes source read/output");
        Check(!Directory.Exists(root), "pre-cancel creates no output");
    }

    internal static void ProbeOwnedSource(string source)
    {
        string temporary = OperatingSystem.IsMacOS() && Directory.Exists("/private/tmp") ? "/private/tmp" : Path.GetTempPath();
        string root = Path.Combine(temporary, "prime-owned-character-bundle-" + Guid.NewGuid().ToString("N"));
        byte[] sourceBefore = HashFile(source);
        Console.WriteLine("Owned source SHA256 before: " + Convert.ToHexStringLower(sourceBefore));
        try
        {
            var files = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, root);
            Check(files.Count > 0, "owned source has supported authoring appearances");
            string[] ids = files.Select(Path.GetFileNameWithoutExtension).Select(s => s!).ToArray();
            foreach (string id in ids)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(id);
                var inspected = LocalImportBundle.Inspect(root, id, spec);
                Check(inspected.State == ImportBundleState.Ready && inspected.Receipt != null, "owned bundle Ready " + id);
                string obj = files.Single(f => Path.GetFileNameWithoutExtension(f) == id);
                string folder = Path.GetDirectoryName(obj)!;
                Check(Path.GetFileName(folder) == inspected.Receipt!.Generation && inspected.Files.Contains(obj)
                    && spec.RequiredComponents.All(c => inspected.Files.Contains(Path.Combine(folder, c)))
                    && inspected.Files.All(f => Path.GetDirectoryName(f) == folder), "same immutable generation body/rig/motions/optional graph " + id);
                // Optional ANCS graph must retain its exact raw snapshot sibling.
                string graph = Path.Combine(folder, id + ".ancs-graph.json");
                if (File.Exists(graph))
                {
                    string snapshot = Path.Combine(folder, id + ".ancs.bin");
                    Check(inspected.Files.Contains(snapshot), "graph retains raw ANCS snapshot " + id);
                    var proof = RetroAnimationGraphProof.Verify(File.ReadAllBytes(graph), File.ReadAllBytes(snapshot));
                    Check(proof.SourceCharacterId == id && !proof.NamedGraphSemanticsAccepted && proof.UninterpretedSetBytesPreservedInSnapshot,
                        "exact raw graph proof retained without named-semantic claim " + id);
                }
            }
            if(ids.Contains("darksamus-echoes") && ids.Contains("darksamus-overcharged-echoes"))
            {
                uint Body(string id)
                {
                    string obj=files.Single(f=>Path.GetFileNameWithoutExtension(f)==id);
                    using var skeleton=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(obj)!,id+".skeleton.json")));
                    return skeleton.RootElement.GetProperty("Refs").GetProperty("Cmdl").GetUInt32();
                }
                Check(Body("darksamus-echoes")!=Body("darksamus-overcharged-echoes"),"actual Overcharged source model differs from the normal body");
            }
            var original = Snapshot(root);
            Check(RetroCharacterPreviewExporter.ExportFromOwnedGame(source, root).SequenceEqual(files), "repeat import reuses immutable generation paths");
            Check(Same(original, Snapshot(root)), "repeat import preserves receipt and generation bytes");
            using var cancellation = new CancellationTokenSource();
            bool beforeCommit = false;
            var progress = new SynchronousProgress(stage => { if (stage == ImportPublishStage.BeforeCommit) { beforeCommit = true; cancellation.Cancel(); } });
            Expect<OperationCanceledException>(() => RetroCharacterPreviewExporter.ExportFromOwnedGame(source, root, cancellation.Token, progress), "BeforeCommit cancellation");
            Check(beforeCommit && Same(original, Snapshot(root)), "BeforeCommit cancellation preserves all prior receipts/files");
            if(ids.Length>1)
            {
                using var partialCancellation=new CancellationTokenSource();
                int checks=0;
                var partialProgress=new SynchronousProgress(stage=>
                { if(stage==ImportPublishStage.Checking && ++checks==3)partialCancellation.Cancel(); });
                try
                {
                    RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,partialCancellation.Token,partialProgress);
                    throw new InvalidOperationException("Expected cancellation after first complete appearance");
                }
                catch(ImportBundleBatchCanceledException ex)
                { Check(ex.CommittedIds.SequenceEqual(new[]{ids[0]}) && ex.Message.Contains(ids[0]),"actual selected batch reports first complete appearance before cancellation"); }
                Check(Same(original,Snapshot(root)),"same-content partial update retains exact good generation bytes for both appearances");
            }
            foreach (string id in ids)
            {
                var inspected = LocalImportBundle.Inspect(root, id, RetroCharacterPreviewExporter.BundleSpec(id));
                Check(inspected.State == ImportBundleState.Ready, "cancel retains Ready " + id);
                var generation = inspected.Files.ToDictionary(f => f, f => Convert.ToHexStringLower(HashFile(f)));
                Check(LocalImportBundle.Remove(root, id) && LocalImportBundle.Inspect(root, id).State == ImportBundleState.NotInstalled,
                    "remove unpublishes canonical inventory " + id);
                Check(generation.All(p => File.Exists(p.Key) && Convert.ToHexStringLower(HashFile(p.Key)) == p.Value), "remove retains immutable generation bytes " + id);
                Check(!LocalImportBundle.Remove(root, id), "repeated remove is absent " + id);
            }
            Check(File.Exists(source) && sourceBefore.SequenceEqual(HashFile(source)), "owned source unchanged after update/cancel/remove");
            Console.WriteLine("Owned source SHA256 after: " + Convert.ToHexStringLower(HashFile(source)));
            Console.WriteLine("PASS: owned character bundles, immutable siblings, reuse, BeforeCommit cancellation, safe removal");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Check(!Directory.Exists(root), "all derived probe data cleaned");
            Console.WriteLine("Probe cleanup: all derived temporary data removed");
        }
    }
    private sealed class SynchronousProgress(Action<ImportPublishStage> report) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage value) => report(value); }
    private static byte[] HashFile(string file) { using var stream = File.OpenRead(file); return SHA256.HashData(stream); }
    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexStringLower(HashFile(f)));
    private static bool Same(Dictionary<string,string> a, Dictionary<string,string> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("Character bundle PASS: " + message); }
    private static void Expect<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
}
