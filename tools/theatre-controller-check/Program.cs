using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("Theatre contract: " + description);
    checks++;
}
void Settle(TheatreController controller, Func<TheatreSnapshot, bool> condition)
{
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    do { controller.Pump(); if (condition(controller.Snapshot())) return; Thread.Sleep(1); }
    while (deadline.Elapsed < TimeSpan.FromSeconds(5));
    throw new TimeoutException("Theatre completion did not settle.");
}
var backend = new TheatreBoundary();
using (var controller = new TheatreController(backend))
{
    Settle(controller, state => state.State == TheatreLibraryState.Ready);
    var first = controller.Snapshot();
    Check(ReferenceEquals(first, controller.Snapshot()), "unchanged snapshot retains its revision");
    Check(first.Entries.Length == 12 && first.VisibleEntries.Length == 8 && first.PageCount == 2, "all recordings remain available through paging");
    controller.Dispatch(TheatreAction.NextPage);
    Check(controller.Snapshot().VisibleEntries.Length == 4 && controller.SelectVisible(3), "the final item is reachable on a later page");
    Check(!controller.SelectVisible(4) && controller.Snapshot().Selected?.Path == "entry-11.ppdemo", "out of range row cannot change selection");
    controller.Dispatch(TheatreAction.Search, "λ😀");
    Check(controller.Snapshot().FilteredCount == 1 && controller.Snapshot().Selected?.Path == "entry-3.ppdemo", "Unicode search covers annotations and organization metadata");
    controller.Dispatch(TheatreAction.ClearSearch);
    controller.Dispatch(TheatreAction.NextFilter); Check(controller.Snapshot().FilteredCount == 10, "full replay view");
    controller.Dispatch(TheatreAction.NextFilter); Check(controller.Snapshot().FilteredCount == 2, "clip view");
    controller.Dispatch(TheatreAction.NextFilter); Check(controller.Snapshot().FilteredCount == 1, "favorite view");
    foreach (var expected in new[] { (TheatreFilter.RecentSevenDays, 12), (TheatreFilter.SameMap, 6), (TheatreFilter.SamePlayers, 12),
        (TheatreFilter.Annotated, 1), (TheatreFilter.Highlights, 0), (TheatreFilter.Bookmarks, 1), (TheatreFilter.Organized, 12),
        (TheatreFilter.LongSessions, 0), (TheatreFilter.ShortClips, 0), (TheatreFilter.NeedsRecovery, 0), (TheatreFilter.All, 12) })
    {
        controller.Dispatch(TheatreAction.NextFilter);
        Check(controller.Snapshot().Filter == expected.Item1 && controller.Snapshot().FilteredCount == expected.Item2,
            "smart view contract " + expected.Item1);
    }
    Check(controller.Snapshot().Filter == TheatreFilter.All, "all fourteen smart views remain selectable");
    controller.Dispatch(TheatreAction.NextSort); controller.Dispatch(TheatreAction.NextSort);
    Check(controller.Snapshot().Sort == TheatreSort.Name && controller.Snapshot().VisibleEntries[0].Title == "Alpha", "name sorting uses copied metadata");
    controller.Dispatch(TheatreAction.Delete);
    string captured = controller.Snapshot().DeletePath;
    Check(controller.Snapshot().ConfirmDelete && !controller.SelectVisible(1) && !controller.Dispatch(TheatreAction.NextFilter), "delete confirmation freezes its captured path");
    controller.Dispatch(TheatreAction.CancelDelete);
    Check(!controller.Snapshot().ConfirmDelete && backend.Executions.Count == 0, "cancel delete performs no disk mutation");
    controller.Dispatch(TheatreAction.Delete); controller.Dispatch(TheatreAction.ConfirmDelete);
    Settle(controller, state => !state.Busy && state.State != TheatreLibraryState.Loading);
    Check(backend.Executions.Single().Targets[0].Path == captured && backend.Executions[0].Operation == TheatreOperation.Delete,
        "confirmation removes only the captured archive");
    controller.Dispatch(TheatreAction.Organize, tags: "λ😀, ranked", collections: "Summer, Finals");
    Settle(controller, state => !state.Busy && state.State != TheatreLibraryState.Loading);
    Check(backend.Executions[^1].Tags == "λ😀, ranked" && backend.Executions[^1].Collections == "Summer, Finals", "organization drafts reach real service boundary without markup interpretation");
    controller.Dispatch(TheatreAction.Watch);
    Check(controller.Snapshot().Busy && !controller.Dispatch(TheatreAction.Watch), "duplicate launch is blocked while preparation runs");
    backend.Launches[^1].SetResult(new(new LaunchPlan { Kind = LaunchKind.Demo, DemoPath = captured }, ""));
    Settle(controller, state => !state.Busy);
    Check(controller.TryTakeLaunch(out var plan) && plan.Kind == LaunchKind.Demo && plan.DemoPath == captured && !controller.TryTakeLaunch(out _), "engine receives one prepared handoff");
    Check(controller.Snapshot().LaunchPending && !controller.Dispatch(TheatreAction.Watch), "consumed launch stays latched until the engine accepts or reports failure");
    controller.ReportLaunchFailure("Cannot load replay map: MapHashMismatch.");
    Check(controller.Snapshot().Error == "Cannot load replay map: MapHashMismatch." && !controller.Snapshot().LaunchPending,
        "engine failure text survives and releases controls");
    controller.Dispatch(TheatreAction.Watch); var cancelled = backend.Launches[^1];
    controller.CancelJob(); cancelled.SetResult(new(new LaunchPlan { Kind = LaunchKind.Demo, DemoPath = "stale.ppdemo" }, ""));
    Thread.Sleep(5); controller.Pump();
    Check(!controller.TryTakeLaunch(out _) && !controller.Snapshot().LaunchPending, "cancelled worker cannot publish a late launch");
    controller.Dispatch(TheatreAction.Watch); backend.Problem = "Leave the current multiplayer session before opening a replay.";
    backend.Launches[^1].SetResult(new(new LaunchPlan { Kind = LaunchKind.Demo, DemoPath = captured }, ""));
    Settle(controller, state => !state.Busy);
    Check(!controller.TryTakeLaunch(out _) && controller.Snapshot().Error == backend.Problem, "launch authority is rechecked after disk work");
    backend.Problem = "";
    Check(Task.Run(() => { try { controller.Dispatch(TheatreAction.Refresh); } catch (InvalidOperationException) { return true; } return false; }).GetAwaiter().GetResult(), "workers cannot mutate engine choices");
}
backend = new TheatreBoundary { Entries = Enumerable.Range(0, 205).Select(i => TheatreBoundary.Entry(i)).ToImmutableArray() };
using (var controller = new TheatreController(backend, manageStorage: false))
{
    Settle(controller, state => state.State == TheatreLibraryState.Ready);
    controller.Dispatch(TheatreAction.FavoriteFiltered);
    Check(backend.Executions.Count == 0 && controller.Snapshot().Error.Contains("200 OR FEWER"), "batch favorite bound cannot be bypassed by paging");
    controller.Dispatch(TheatreAction.ValidateFiltered);
    Check(backend.Executions.Count == 0 && controller.Snapshot().Error.Contains("50 OR FEWER"), "batch integrity bound applies to full filtered view");
    controller.Dispatch(TheatreAction.Search, "λ😀");
    controller.Dispatch(TheatreAction.ValidateFiltered);
    Settle(controller, state => !state.Busy);
    Check(backend.Executions.Count == 1 && backend.Executions[0].Targets.Length == 1, "batch operations use matching full view");
}
backend = new TheatreBoundary();
var retired = new TheatreController(backend);
Settle(retired, state => state.State == TheatreLibraryState.Ready);
retired.Dispatch(TheatreAction.Watch); var late = backend.Launches[^1]; retired.Dispose();
late.SetResult(new(new LaunchPlan { Kind = LaunchKind.Demo, DemoPath = "retired.ppdemo" }, ""));
Thread.Sleep(5); retired.Pump();
Check(!retired.TryTakeLaunch(out _) && retired.Snapshot().State == TheatreLibraryState.Closed, "retired library cannot leak a worker handoff");
Console.WriteLine($"THEATRE CONTRACT PASS {checks} checks.");
#if MPHREAD_RMLUI_POC
if (args.Length == 3 && args[0] == "--native") NativeTheatreCheck.Run(args[1], args[2]);
#endif

internal sealed class TheatreBoundary : ITheatreBackend
{
    public ImmutableArray<TheatreEntry> Entries = Enumerable.Range(0, 12).Select(Entry).ToImmutableArray();
    public readonly List<TaskCompletionSource<TheatreLaunchResult>> Launches = new();
    public readonly List<(TheatreOperation Operation, ImmutableArray<TheatreEntry> Targets, string Tags, string Collections)> Executions = new();
    public string Problem = "";
    public bool DesktopActions => true;
    public bool CanLaunch(out string reason) { reason = Problem; return reason.Length == 0; }
    public Task<ImmutableArray<TheatreEntry>> Scan(bool policy, CancellationToken cancellation) => Task.FromResult(Entries);
    public Task<TheatreLaunchResult> PreparePlayback(string path, CancellationToken cancellation)
    { var pending = new TaskCompletionSource<TheatreLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously); Launches.Add(pending); return pending.Task; }
    public Task<TheatreOperationResult> Execute(TheatreOperation operation, ImmutableArray<TheatreEntry> targets, string name, string tags, string collections, CancellationToken cancellation)
    { Executions.Add((operation, targets, tags, collections)); return Task.FromResult(new TheatreOperationResult("DONE", targets[0].Path, Reload: false)); }
    public Task<string?> PickImport(CancellationToken cancellation) => Task.FromResult<string?>(null);
    public bool OpenStudio(string path, out string? error) { error = null; return true; }
    public void Reveal(string path) { }
    public static TheatreEntry Entry(int i) => new()
    {
        Path = $"entry-{i}.ppdemo", Title = i == 0 ? "Alpha" : $"Replay {i:000}", Detail = "Real service boundary fixture",
        Metadata = "<button id=\"injected_action\">λ😀</button>", Recorded = DateTime.Now.AddMinutes(-i), DurationFrames = (uint)(i + 1) * 1800,
        IsClip = i is 1 or 2, Favorite = i == 2, Room = i % 2 == 0 ? "Combat Hall" : "Alinos",
        Players = i % 2 == 0 ? "Samus Trace" : "Samus Noxus", Tags = "Ranked", Collections = "Summer",
        SearchText = $"entry-{i} " + (i == 3 ? "λ😀" : "Ranked Summer"), Annotated = i == 3, BookmarkCount = i == 3 ? 1 : 0, Organized = true
    };
}
