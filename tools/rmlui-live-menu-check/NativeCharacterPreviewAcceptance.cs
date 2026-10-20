using MphRead;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher;

internal static class NativeCharacterPreviewAcceptance
{
    internal static void Run(MenuSettings settings)
    {
        string root = RetroCharacterPreviewExporter.DefaultRoot;
        Check(!Directory.Exists(root), "fixture inventory starts empty in isolated user data");
        try
        {
            Requests(settings);
            CancellationOutcomes(settings);
            using (var dirty = new NativeFullSettingsController(settings))
            {
                string[] engine = dirty.Rows.Select(r => r.Read()).ToArray();
                var row = dirty.Rows.Single(r => r.Category == "Audio" && r.Label == "Sfx Volume (%)");
                Check(dirty.Stage(row, row.Value == "65" ? "35" : "65") && dirty.Dirty, "fixture stages detached settings draft");
                dirty.RequestCharacterImport(false); dirty.RequestRemoveCharacterPreview();
                Check(!dirty.Busy && !dirty.ArenaImportRequested && dirty.Confirmation.Length == 0, "dirty draft refuses import/removal");
                dirty.CheckCharacterPreview(); Wait(dirty);
                Check(dirty.Dirty && dirty.Rows.Select(r => r.Read()).SequenceEqual(engine), "read-only check preserves draft and engine owners");
                dirty.Discard();
            }
            var sources = RetroCharacterPreviewExporter.Sources.Take(2).ToArray();
            foreach (var source in sources)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(source.Id);
                LocalImportBundle.Publish(root, spec, spec.RequiredComponents.ToDictionary(c => c, c => Encoding.UTF8.GetBytes("synthetic fixture: " + c)));
            }
            var all = Snapshot(root);
            using (var remove = new NativeFullSettingsController(settings))
            {
                string captured = remove.CharacterPreviewId;
                remove.RequestRemoveCharacterPreview();
                Check(remove.Confirmation.Contains(captured), "confirmation names captured source identity");
                remove.CycleCharacterPreview();
                Check(remove.CharacterPreviewId != captured, "direct cycle exercises confirmation identity race");
                Check(!remove.Operation(243) && Same(all, Snapshot(root)), "direct removal action requires confirmation dispatch");
                Check(remove.Operation(24) && remove.Confirmation.Length == 0 && Same(all, Snapshot(root)), "confirmation cancel deletes nothing");
                Check(!remove.Operation(23), "cancelled confirmation cannot be replayed");
                // Return to initial appearance, then change selection after capture.
                while (remove.CharacterPreviewId != captured) remove.CycleCharacterPreview();
                remove.RequestRemoveCharacterPreview(); remove.CycleCharacterPreview();
                Check(remove.Operation(23), "confirmed removal dispatched"); Wait(remove);
                Check(LocalImportBundle.Inspect(root, captured).State == ImportBundleState.NotInstalled
                    && LocalImportBundle.Inspect(root, remove.CharacterPreviewId).State == ImportBundleState.Ready,
                    "confirmed removal uses captured identity, leaves newly selected source active");
                Check(all.Where(p => !p.Key.EndsWith("current.json", StringComparison.Ordinal)).All(p => Snapshot(root).GetValueOrDefault(p.Key) == p.Value),
                    "removal retains every immutable generation byte");
            }
            using (var retired = new NativeFullSettingsController(settings))
            {
                retired.CycleCharacterPreview(); retired.RequestRemoveCharacterPreview();
                var before = Snapshot(root); retired.Dispose();
                Check(!retired.Operation(23) && !retired.Operation(243) && retired.Confirmation.Length == 0,
                    "disposed owner cannot confirm or dispatch retained removal");
                retired.CheckCharacterPreview(); retired.RequestRemoveCharacterPreview(); retired.RequestCharacterImport(true); retired.RequestArenaImport();
                retired.BeginArenaImport("missing-owned-source.iso");
                Check(!retired.Busy && !retired.ArenaImportRequested && Same(before, Snapshot(root)), "retired route cannot publish new work or files");
            }
            using (var closed = new NativeFullSettingsController(settings))
            {
                Check(closed.Back() && closed.Closed, "clean controller closes");
                closed.RequestCharacterImport(true); closed.RequestArenaImport(); closed.CheckCharacterPreview(); closed.RequestRemoveCharacterPreview();
                Check(!closed.Busy && !closed.ArenaImportRequested && !closed.Operation(243), "closed owner refuses new requests and direct operation");
            }
            using (var running = new NativeFullSettingsController(settings))
            using (var cancellation = new CancellationTokenSource())
            {
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Set(running, "_operation", completion.Task); Set(running, "_arenaCancellation", cancellation);
                string id = running.CharacterPreviewId; var before = Snapshot(root); string[] engine = running.Rows.Select(r => r.Read()).ToArray();
                running.CycleCharacterPreview(); running.CheckCharacterPreview(); running.RequestRemoveCharacterPreview(); running.RequestCharacterImport(true);
                Check(running.CharacterPreviewId == id && ReferenceEquals(Get(running,"_operation"), completion.Task)
                    && running.Confirmation.Length == 0 && !running.ArenaImportRequested, "busy owner cannot replace work, selection or confirmation");
                running.Dispose(); Check(cancellation.IsCancellationRequested, "route teardown cancels active import token");
                completion.SetResult("late detached worker completion");
                Check(running.Rows.Select(r => r.Read()).SequenceEqual(engine) && Same(before, Snapshot(root)), "late worker completion publishes no settings or inventory state");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void CancellationOutcomes(MenuSettings settings)
    {
        using var owner = new NativeFullSettingsController(settings);
        var partial = new ImportBundleBatchCanceledException(new[] { "trooper-power" },
            new OperationCanceledException("synthetic second-appearance cancellation"), CancellationToken.None);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(owner, "_operation", pending.Task);
        owner.Tick(false); Check(owner.Busy, "held import remains pending before cancellation result");
        pending.SetException(partial); owner.Tick(false);
        Check(!owner.Busy && owner.Status == partial.Message && owner.Status.Contains("trooper-power", StringComparison.Ordinal)
            && !owner.Status.Contains("Previous bundle is unchanged", StringComparison.Ordinal),
            "partial Update-all cancellation displays exact committed appearance outcome");
        var failure = new ImportBundleBatchException(new[] { "trooper-power" }, new IOException("synthetic later preparation failure"));
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Set(owner,"_operation",pending.Task);
        pending.SetException(failure); owner.Tick(false);
        Check(!owner.Busy && owner.Status == "Operation failed: " + failure.Message && owner.Status.Contains("trooper-power", StringComparison.Ordinal),
            "later batch failure retains already-committed appearance in displayed outcome");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Set(owner,"_operation",pending.Task);
        pending.SetException(new OperationCanceledException("synthetic precommit cancellation")); owner.Tick(false);
        Check(!owner.Busy && owner.Status == "Local import cancelled. Previous bundle is unchanged.",
            "single-bundle precommit cancellation retains unchanged-bundle message");
    }
    private static void Requests(MenuSettings settings)
    {
        using var owner = new NativeFullSettingsController(settings);
        owner.RequestCharacterImport(false);
        var first = (string[])Get(owner, "_characterImportIds")!;
        Check(first.SequenceEqual(new[] { "darksamus-echoes" }) && owner.Busy && owner.TakeArenaImport() && !owner.TakeArenaImport(),
            "selected picker consumes one detached canonical request");
        owner.BeginArenaImport(null);
        Check(!owner.Busy && Get(owner,"_characterImportIds") == null && Get(owner,"_operation") == null, "picker cancellation clears captured IDs without work");
        owner.RequestCharacterImport(true); var echoes = (string[])Get(owner,"_characterImportIds")!;
        Check(echoes.SequenceEqual(new[] { "darksamus-echoes" }) && !ReferenceEquals(first, echoes), "Echoes all selects only its supported appearance in fresh array");
        owner.TakeArenaImport(); owner.BeginArenaImport(null); owner.CycleCharacterPreview(); owner.RequestCharacterImport(true);
        var troopers = (string[])Get(owner,"_characterImportIds")!;
        Check(troopers.SequenceEqual(new[] { "trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma" }), "Prime all captures four Troopers without Echoes");
        owner.TakeArenaImport(); owner.BeginArenaImport(null); owner.RequestCharacterImport(false);
        Check(((string[])Get(owner,"_characterImportIds")!).SequenceEqual(new[] { "trooper-power" }), "selected Trooper request excludes other appearances");
        owner.Dispose(); owner.BeginArenaImport("missing-owned-source.iso");
        Check(!owner.Busy && !owner.TakeArenaImport() && Get(owner,"_characterImportIds") == null && Get(owner,"_operation") == null,
            "dispose retires pending picker and ignores late callback");
    }
    private static object? Get(object owner,string field) => owner.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);
    private static void Set(object owner,string field,object value) => owner.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,value);
    private static void Wait(NativeFullSettingsController owner)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (owner.Busy && timer.Elapsed < TimeSpan.FromSeconds(5)) { owner.Tick(false); Thread.Sleep(1); }
        Check(!owner.Busy && !owner.Status.StartsWith("Operation failed",StringComparison.Ordinal), "background check/removal completes successfully");
    }
    private static Dictionary<string,string> Snapshot(string root) => !Directory.Exists(root) ? new() : Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
        .ToDictionary(f=>Path.GetRelativePath(root,f),f=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))));
    private static bool Same(Dictionary<string,string> a,Dictionary<string,string> b) => a.Count==b.Count && a.All(p=>b.GetValueOrDefault(p.Key)==p.Value);
    private static void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException("Native character preview: "+why); Console.WriteLine("Native character preview PASS: "+why); }
}
