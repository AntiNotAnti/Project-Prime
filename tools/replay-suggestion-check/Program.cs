using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var candidate = ImmutableArray.Create(new TheatreEditorEntry("highlight:0", 10, 80, "Annotation", "SCORE 75", FocusFrame: 30));

var backend = new SuggestionsBackend();
using (var controller = new TheatreEditorController(backend))
{
    Check(controller.GenerateSuggestions(), "generate starts");
    var first = backend.Work[^1];
    Check(backend.Saves == 0 && controller.Snapshot().Context.Highlights.IsEmpty, "generation neither saves nor publishes pending work");
    first.Completion.SetResult(candidate);
    Check(controller.Snapshot().Context.Highlights.SequenceEqual(candidate) && backend.Saves == 0, "completed immutable candidates publish without persistence");
    Check(controller.RejectSuggestion() && controller.Snapshot().Context.Highlights.IsEmpty && backend.Saves == 0, "reject removes only in-memory proposal");
    Check(controller.GenerateSuggestions(), "start replaceable worker");
    var old = backend.Work[^1];
    Check(controller.GenerateSuggestions() && old.Token.IsCancellationRequested, "new generation cancels old worker");
    var replacement = backend.Work[^1];
    old.Completion.SetResult(candidate);
    Check(controller.Snapshot().Context.Highlights.IsEmpty, "late replaced worker cannot publish");
    replacement.Completion.SetResult(candidate);
    Check(controller.Snapshot().Context.Highlights.Length == 1, "replacement publishes its own result");
    Check(controller.GenerateSuggestions(), "start refresh worker");
    var refreshed = backend.Work[^1];
    backend.Context = backend.Context with { AnnotationRevision = "annotation-v2" };
    Check(controller.Refresh() && refreshed.Token.IsCancellationRequested, "review refresh cancels previous annotation witness");
    refreshed.Completion.SetResult(candidate);
    Check(controller.Snapshot().Context.Highlights.IsEmpty, "old annotation review cannot republish after refresh");
    Check(controller.GenerateSuggestions(), "start reel-edit worker");
    var reelEdit = backend.Work[^1];
    Check(controller.Stage(TheatreEditorEditKind.AddSelection) && controller.Confirm(), "explicit acceptance alone saves");
    reelEdit.Completion.SetResult(candidate);
    Check(controller.Snapshot().Context.Highlights.IsEmpty && backend.Saves == 1, "changed reviewed reel revision refuses worker result");
    Check(controller.GenerateSuggestions(), "start owner-loss worker");
    var ownerLoss = backend.Work[^1];
    backend.Current = false;
    controller.Snapshot();
    Check(ownerLoss.Token.IsCancellationRequested, "owner loss cancels worker");
    ownerLoss.Completion.SetResult(candidate);
    Check(controller.Snapshot().Context.Highlights.IsEmpty, "owner-lost completion does not publish");
}
Check(backend.Retired == 1, "controller retirement releases backend once");
var retiring = new SuggestionsBackend();
var retiringController = new TheatreEditorController(retiring);
Check(retiringController.GenerateSuggestions(), "start retiring worker");
var retired = retiring.Work[^1];
retiringController.Dispose(); retired.Completion.SetResult(candidate);
Check(retired.Token.IsCancellationRequested && retiring.Retired == 1 && retiring.Saves == 0, "retirement cancels pending work without saving");
foreach (string revision in new[] { "source", "annotation", "reel" })
{
    var stale = new SuggestionsBackend();
    using var review = new TheatreEditorController(stale);
    Check(review.GenerateSuggestions(), "start external revision worker " + revision);
    stale.Context = revision switch
    {
        "source" => stale.Context with { SourceRevision = "changed-source" },
        "annotation" => stale.Context with { AnnotationRevision = "changed-annotation" },
        _ => stale.Context with { ReelRevision = "changed-reel" }
    };
    stale.Work[^1].Completion.SetResult(candidate);
    var state = review.Snapshot();
    Check(state.Context.Highlights.IsEmpty && state.Error.Contains("revision changed") && stale.Saves == 0,
        "completion validates external " + revision + " revision without persistence");
}
var variety = new SuggestionsBackend();
var manual = new TheatreEditorEntry("manual", 0, 20, "Manual", "", Camera: 2);
var proposals = ImmutableArray.CreateBuilder<TheatreEditorEntry>();
proposals.Add(new("blocked", 5, 15, "Blocked by manual", "", Actor: 0));
proposals.Add(new("a0", 20, 40, "Actor0 first", "", Actor: 0));
proposals.Add(new("overlap", 30, 45, "Overlapping candidate", "", Actor: 1));
proposals.Add(new("a1", 40, 60, "Actor0 second", "", Actor: 0));
proposals.Add(new("a2", 60, 80, "Actor0 excess", "", Actor: 0));
for (int i = 0; i < 12; i++) proposals.Add(new("other:" + i, (uint)(100 + i * 20), (uint)(120 + i * 20), "Other" + i, "", Actor: i / 2 + 1));
variety.Context = variety.Context with { Reel = ImmutableArray.Create(manual), Highlights = proposals.ToImmutable() };
using (var varied = new TheatreEditorController(variety))
{
    Check(varied.NextTab() && !varied.Stage(TheatreEditorEditKind.AddHighlight) && variety.Saves == 0,
        "individual overlapping suggestion refuses staging and preserves manual reel");
    Check(varied.Stage(TheatreEditorEditKind.AddTopHighlights), "bulk acceptance stages varied candidates");
    var after = varied.Snapshot().Pending!.After;
    Check(after.Length == 9 && after[0] == manual && variety.Saves == 0, "bulk limit eight preserves manual identity and does not save before confirmation");
    Check(after.Count(row => row.Name.StartsWith("Actor0")) == 2 && !after.Any(row => row.Name.Contains("excess")),
        "bulk limits repeated actor to two candidates");
    Check(!after.Any(row => row.Name is "Blocked by manual" or "Overlapping candidate")
        && after.Skip(1).Zip(after.Skip(2)).All(pair => pair.First.EndFrame <= pair.Second.Frame),
        "bulk excludes saved and candidate overlaps while retaining adjacent windows");
    Check(varied.Confirm() && variety.Saves == 1 && variety.Context.Reel[0] == manual,
        "explicit confirmation commits varied ranges while preserving manual segment");
}
Console.WriteLine($"PASS {checks} source-linked suggestion controller checks; synthetic backend, no game assets");

internal sealed class SuggestionsBackend : ITheatreEditorBackend, ITheatreEditorSuggestionBackend
{
    internal sealed record Pending(TaskCompletionSource<ImmutableArray<TheatreEditorEntry>> Completion, CancellationToken Token);
    internal readonly List<Pending> Work = new();
    internal int Saves, Retired;
    internal bool Current = true;
    internal TheatreEditorContext Context = new(1, new object(), new object(), "source.ppdemo", "source.ppdemo", "source-v1", "annotation-v1", "reel-v1",
        30, 600, 0, 10, 80, true, ImmutableArray<TheatreEditorPlayer>.Empty, ImmutableArray<TheatreEditorEntry>.Empty,
        ImmutableArray<TheatreEditorEntry>.Empty, ImmutableArray<TheatreEditorEntry>.Empty, ImmutableArray<TheatreEditorEntry>.Empty,
        ImmutableArray<TheatreEditorEntry>.Empty, new object());
    public TheatreEditorContext Capture() => Context;
    public TheatreEditorLive ReadLive(TheatreEditorContext context) => new(Current, false, "synthetic", false, false, false, false);
    public void ValidateSuggestions(TheatreEditorContext context)
    {
        if (!Current || !ReferenceEquals(context.Owner, Context.Owner)
            || !ReferenceEquals(context.Transport, Context.Transport)
            || context.SourceRevision != Context.SourceRevision || context.AnnotationRevision != Context.AnnotationRevision
            || context.ReelRevision != Context.ReelRevision)
            throw new TheatreEditorStaleException("review revision changed");
    }
    public Task<ImmutableArray<TheatreEditorEntry>> GenerateSuggestions(TheatreEditorContext context, CancellationToken cancellation)
    {
        var result = new TaskCompletionSource<ImmutableArray<TheatreEditorEntry>>();
        Work.Add(new(result, cancellation)); return result.Task;
    }
    public void Seek(TheatreEditorContext context, TheatreEditorEntry exact, bool selectRange) { }
    public void InspectShot(TheatreEditorContext context, TheatreEditorEntry? previous, int direction, int player) { }
    public void ToggleHud(TheatreEditorContext context, TheatreEditorHud hud) { }
    public TheatreEditorContext Commit(TheatreEditorEdit reviewed)
    { Saves++; return Context = Context with { Reel = reviewed.After, ReelRevision = "reel-v" + (Saves + 1) }; }
    public void Retire() => Retired++;
}
