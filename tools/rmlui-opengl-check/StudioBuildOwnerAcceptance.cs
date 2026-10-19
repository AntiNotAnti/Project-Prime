using System.Collections.Concurrent;
using System.Reflection;
using MphRead;
using MphRead.Formats;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using System.Numerics;

/// <summary>Real retained build owner, held analysis workers and private actual package IO; no game assets or GL.</summary>
internal static class StudioBuildOwnerAcceptance
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(CustomRooms.UserMapDirectory, "native-build-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string previousRoot = GameFiles.Root;
        var pathsField = typeof(Paths).GetField("_allPaths", BindingFlags.NonPublic | BindingFlags.Static)!;
        var paths = (Dictionary<string, string>)pathsField.GetValue(null)!;
        var previousPaths = new Dictionary<string, string>(paths);
        var publishedField = typeof(GameFiles).GetField("_publishedPathsRoot", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previousPublishedRoot = publishedField.GetValue(null);
        try
        {
            GameFiles.Root = Path.Combine(root, "missing-game-files");
            using var owner = new MapStudioScreen(preview: true);
            check(owner.TryAcquireNativeEditor(out var acquired, out _) && acquired != null,
                "build fixture acquires actual retained editor lease");
            using var session = acquired!;
            INativeStudioEditorBuild build = session;
            using var scheduler = new Scheduler(Path.Combine(root, "build-cache"));
            session.BuildSchedulerOverride = scheduler;
            var document = Field<MapDocument>(owner, "_document");
            var viewport = Field<MapViewport>(owner, "_viewport");
            var history = document.History;
            var writer = Field<MapAutosaveService>(owner, "_autosave");
            var watcher = Field<MapSourceWatchService>(owner, "_sourceWatch");
            Guid id = document.Project.Definition.Geometry.First().Id;
            string authored = document.Project.Definition.Serialize();
            var state = document.CurrentStateId;
            int commands = history.CommandCount;
            var lease = session.Capture().Ticket.Lease;
            check(Field<Scene?>(viewport, "_renderer") == null, "build owner fixture has no renderer or Scene");

            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Validate).Success,
                "explicit Validate starts detached analysis on actual owner");
            var held = scheduler.TakeAnalysis();
            var running = build.CaptureBuild();
            check(running.Busy && session.Capture().Busy && held.Thread != Environment.CurrentManagedThreadId,
                "analysis runs off owner thread and holds the retained editor busy");
            check(!session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Move, new(1, 0, 0)).Success
                && !build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Validate).Success,
                "held analysis rejects document edits and competing builds");
            held.Answer.SetResult(Analysis(id, "first-analysis"));
            WaitUntil(() => Field<Task?>(Field<object>(session, "_buildWork"), "Task")!.IsCompleted);
            check(Field<object?>(owner, "_work") != null && document.Project.Definition.Serialize() == authored,
                "completed worker cannot publish or release owner busy state before explicit Pump");
            var analyzed = Pump(build);
            check(analyzed.Stage == NativeStudioBuildStage.Complete && analyzed.Fingerprint == "first-analysis"
                && analyzed.Diagnostics.Single().ObjectId == id && analyzed.Budgets.Count == 1,
                "owner Pump publishes exact same-state diagnostic and budget results");
            viewport.FocusWorld(new[] { new Vector3(999, 999, 999) });
            check(build.SelectBuildProblem(analyzed.Operation, 0).Success && document.ActiveObjectId == id
                && viewport.CameraTarget != new Vector3(999, 999, 999),
                "same-state diagnostic selects its actual object ID and frames the retained viewport");
            check(ReferenceEquals(document, Field<MapDocument>(owner, "_document"))
                && ReferenceEquals(history, document.History) && ReferenceEquals(writer, Field<MapAutosaveService>(owner, "_autosave"))
                && ReferenceEquals(watcher, Field<MapSourceWatchService>(owner, "_sourceWatch"))
                && document.CurrentStateId == state && history.CommandCount == commands
                && document.Project.Definition.Serialize() == authored && session.Capture().Ticket.Lease == lease,
                "analysis and problem framing preserve exact document/history/writer/watcher/state/lease");

            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Navigation).Success,
                "explicit Navigation starts a distinct held analysis");
            held = scheduler.TakeAnalysis();
            check(held.Navigation, "navigation request forwards navigation flag to scheduler");
            document.Edit("External owner change during analysis", map => MapObjects.Find(map, id)!.Position[0] += 3);
            string changed = document.Project.Definition.Serialize();
            held.Answer.SetResult(Analysis(id, "late-stale-analysis"));
            var stale = Pump(build);
            check(stale.Stage == NativeStudioBuildStage.Failed && !stale.ResultCurrent
                && stale.Diagnostics.Count == 0 && document.Project.Definition.Serialize() == changed,
                "late analysis for changed authored state is rejected without publishing rows or geometry");
            check(!build.SelectBuildProblem(analyzed.Operation, 0).Success
                && !build.SelectBuildProblem(stale.Operation, 0).Success,
                "diagnostic rows from replaced operations and stale states are refused");

            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Validate).Success,
                "changed-state failure permits an explicit analysis retry");
            held = scheduler.TakeAnalysis();
            Guid cancelOperation = build.CaptureBuild().Operation;
            build.CancelBuild(cancelOperation);
            check(build.CaptureBuild().Stage == NativeStudioBuildStage.Canceling && session.Capture().Busy,
                "canceled held scheduler keeps busy ownership until worker actually retires");
            held.Answer.SetResult(Analysis(id, "canceled-answer"));
            check(Pump(build).Stage == NativeStudioBuildStage.Failed && !session.Capture().Busy,
                "retired cancellation drains and releases actual owner busy state");
            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Validate).Success,
                "canceled analysis permits a new operation");
            held = scheduler.TakeAnalysis();
            held.Answer.SetResult(Analysis(id, "retry-analysis"));
            check(Pump(build).Fingerprint == "retry-analysis", "retry publishes only its own analysis answer");

            string destination = Path.Combine(root, "reviewed.ppmap");
            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Package, destination).Success,
                "actual package scheduler prepares a private review archive");
            var review = Pump(build);
            string reviewPath = scheduler.LastPackage!;
            check(review.Stage == NativeStudioBuildStage.ReadyPackage && File.Exists(reviewPath)
                && !File.Exists(destination) && Path.GetFullPath(reviewPath) != Path.GetFullPath(destination),
                "package review holds actual immutable archive privately without writing requested destination");
            byte[] reviewedBytes = File.ReadAllBytes(reviewPath);
            check(review.Fingerprint == MapContentIdentity.FromPackage(reviewPath).PackageHash.ToString()
                && document.Project.Definition.Serialize() == changed,
                "review fingerprint identifies actual package bytes and leaves authored document unchanged");
            check(build.ConfirmBuildPackage(review.Operation).Success, "explicit Confirm authorizes reviewed package publication");
            var exported = Pump(build);
            check(exported.Stage == NativeStudioBuildStage.Complete && File.ReadAllBytes(destination).SequenceEqual(reviewedBytes)
                && !Directory.Exists(Path.GetDirectoryName(reviewPath)),
                "Confirm exports exact reviewed bytes and cleans private review directory");

            byte[] original = File.ReadAllBytes(destination);
            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Package, destination).Success,
                "existing package destination requires a fresh captured review");
            review = Pump(build); reviewPath = scheduler.LastPackage!;
            byte[] raced = { 31, 41, 59, 26 };
            File.WriteAllBytes(destination, raced);
            check(build.ConfirmBuildPackage(review.Operation).Success, "version mismatch is checked by actual publication worker");
            check(Pump(build).Stage == NativeStudioBuildStage.Failed && File.ReadAllBytes(destination).SequenceEqual(raced)
                && !Directory.Exists(Path.GetDirectoryName(reviewPath)),
                "destination changed after review is refused unchanged and review temporary files are cleaned");

            File.WriteAllBytes(destination, original);
            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Package, destination).Success,
                "overwrite race fixture captures prior package version");
            review = Pump(build); reviewPath = scheduler.LastPackage!;
            using (var reached = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                session.BuildCheckpoint = (stage, token) =>
                { if (stage == "before-package-publication") { reached.Set(); release.Wait(token); } };
                // Checkpoints are captured at BeginBuild, so prepare again with the held publication checkpoint.
                build.ClearBuild(review.Operation);
                check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Package, destination).Success,
                    "package publication captures held worker checkpoint");
                review = Pump(build); reviewPath = scheduler.LastPackage!;
                check(build.ConfirmBuildPackage(review.Operation).Success, "reviewed overwrite starts bounded publication");
                try
                {
                    if (!reached.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Package publication checkpoint was not reached.");
                    File.WriteAllBytes(destination, raced);
                }
                finally { release.Set(); }
                exported = Pump(build);
            }
            session.BuildCheckpoint = null;
            string recovery = Directory.EnumerateFiles(root, "reviewed.ppmap.prime-asset-recovery-*").Single();
            check(exported.Stage == NativeStudioBuildStage.Failed && !File.Exists(destination)
                && File.ReadAllBytes(recovery).SequenceEqual(raced) && exported.Status.Contains(recovery, StringComparison.Ordinal)
                && !Directory.Exists(Path.GetDirectoryName(reviewPath)),
                "overwrite race refuses publication and retains actual displaced external bytes at reported recovery path");

            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Package, destination).Success,
                "canceled package review can be prepared after overwrite failure");
            review = Pump(build); reviewPath = scheduler.LastPackage!;
            build.CancelBuild(review.Operation);
            check(!build.ConfirmBuildPackage(review.Operation).Success && !File.Exists(destination)
                && !Directory.Exists(Path.GetDirectoryName(reviewPath)),
                "canceling Ready review refuses Confirm and cleans its private archive without destination write");
            check(!Directory.EnumerateFiles(root, ".prime-asset-*.tmp", SearchOption.AllDirectories).Any(),
                "package success/refusal/cancellation leave no publication temporary files");

            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Runtime).Success,
                "runtime preflight uses same bounded owner operation");
            var missingFiles = Pump(build);
            check(missingFiles.Stage == NativeStudioBuildStage.Failed && missingFiles.Status.Contains("SET UP GAME FILES", StringComparison.Ordinal)
                && scheduler.RuntimeCalls == 0 && document.Project.Definition.Serialize() == changed,
                "runtime without game files fails before scheduler/install and preserves retained authored state");

            check(build.BeginBuild(session.Capture().Ticket, NativeStudioBuildKind.Validate).Success,
                "close fixture starts held worker");
            held = scheduler.TakeAnalysis();
            check(!session.TryClose().Success && session.Capture().Alive && viewport.NativePresentationLeased,
                "close cancels held build but keeps editor and original lease until worker retirement");
            held.Answer.SetResult(Analysis(id, "late-close-answer"));
            check(Pump(build).Stage == NativeStudioBuildStage.Failed && session.TryClose().Success
                && !session.Capture().Alive && ReferenceEquals(document, Field<MapDocument>(owner, "_document"))
                && ReferenceEquals(history, document.History) && Field<Scene?>(viewport, "_renderer") == null,
                "retired worker permits close with original document/history retained and no Scene or GL resources");
        }
        finally
        {
            GameFiles.Root = previousRoot;
            paths.Clear();
            foreach (var entry in previousPaths) paths.Add(entry.Key, entry.Value);
            publishedField.SetValue(null, previousPublishedRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    private static MapAnalysisResult Analysis(Guid id, string fingerprint)
    {
        var validation = new MapValidationResult();
        validation.Diagnostics.Add(new("NATIVE-BUILD-TEST", MapDiagnosticSeverity.Warning, "Select actual authored object", id));
        validation.Budgets.Add(new("Fixture objects", 1, 8));
        return new(fingerprint, new MapCompilation(null, validation), navigation: false);
    }

    private static NativeStudioBuildSnapshot Pump(INativeStudioEditorBuild build)
    {
        NativeStudioBuildSnapshot result = build.CaptureBuild();
        for (int i = 0; i < 30000 && result.Busy; i++)
        { Thread.Sleep(1); result = build.CaptureBuild(); }
        if (result.Busy) throw new TimeoutException("Retained build worker did not retire.");
        return result;
    }
    private static void WaitUntil(Func<bool> ready)
    { if (!SpinWait.SpinUntil(ready, TimeSpan.FromSeconds(10))) throw new TimeoutException("Held build worker did not reach checkpoint."); }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(value)!;

    private sealed record PendingAnalysis(TaskCompletionSource<MapAnalysisResult> Answer, bool Navigation, int Thread);
    private sealed class Scheduler(string cache) : IMapBuildScheduler, IDisposable
    {
        private readonly ConcurrentQueue<PendingAnalysis> _pending = new();
        private readonly ConcurrentBag<TaskCompletionSource<MapAnalysisResult>> _answers = new();
        private readonly MapBuildScheduler _packages = new(cache);
        internal string? LastPackage;
        internal int RuntimeCalls;
        internal PendingAnalysis TakeAnalysis()
        {
            WaitUntil(() => !_pending.IsEmpty);
            return _pending.TryDequeue(out var pending) ? pending : throw new InvalidOperationException("Missing analysis request.");
        }
        public Task<MapAnalysisResult> AnalyzeAsync(MapBuildSnapshot snapshot, bool navigation = false, CancellationToken cancellation = default)
        {
            var answer = new TaskCompletionSource<MapAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _answers.Add(answer);
            _pending.Enqueue(new(answer, navigation, Environment.CurrentManagedThreadId));
            return answer.Task;
        }
        public Task<MapBuildResult> BuildAsync(MapBuildSnapshot snapshot, CancellationToken cancellation = default)
        { Interlocked.Increment(ref RuntimeCalls); throw new InvalidOperationException("Runtime build must stop at missing-files preflight."); }
        public async Task<string> PackageAsync(MapBuildSnapshot snapshot, string destination, CancellationToken cancellation = default)
        {
            string result = await _packages.PackageAsync(snapshot, destination, cancellation).ConfigureAwait(false);
            LastPackage = result;
            return result;
        }
        public void Dispose()
        { foreach (var answer in _answers) answer.TrySetCanceled(); }
    }
}
