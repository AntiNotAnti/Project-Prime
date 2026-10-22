using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException("Bookmark contract: " + message);
    checks++;
}
var backend = new MemoryBookmarks();
using var controller = new TheatreBookmarkController(backend);
Check(controller.Snapshot().Entries.Length == 0 && controller.Snapshot().Current, "empty panel binds one synthetic replay owner");
Check(controller.Add(" Opening moment ") && backend.Additions == 1
    && controller.Snapshot().Selected?.Name == "Opening moment", "Add uses current playhead and trims a bounded name");
backend.Context = backend.Context with { Frame = 125 };
Check(controller.Add("Second") && controller.Snapshot().Entries.Length == 2, "moving playhead retains same replay ownership");
Check(controller.Move(1) && controller.Snapshot().Selected?.Frame == 125, "browse reaches saved bookmark");
Check(controller.Jump() && backend.Seeks.SequenceEqual(new uint[] { 125 }), "Jump delegates the exact selected frame without another replay clock");
Check(controller.RequestRemove() && controller.Snapshot().PendingRemoval == controller.Snapshot().Selected
    && backend.Removals == 0, "Remove captures exact bookmark but does not mutate before confirmation");
Check(!controller.Add("blocked") && !controller.Move(-1) && !controller.Refresh(), "confirmation blocks replacement of selected target");
controller.CancelRemove();
Check(controller.Snapshot().PendingRemoval == null && backend.Removals == 0, "Cancel preserves all bookmarks");
Check(controller.RequestRemove(), "same exact bookmark can be selected again");
backend.Context = backend.Context with { AnnotationRevision = "external-edit" };
Check(!controller.ConfirmRemove() && backend.Removals == 0, "external persisted revision change invalidates destructive confirmation");
controller.CancelRemove();
Check(!controller.Add("stale") && !controller.Jump(), "stale panel refuses mutations and seek until refreshed");
Check(controller.Refresh() && controller.RequestRemove() && controller.ConfirmRemove()
    && backend.Removals == 1 && controller.Snapshot().Entries.Length == 1, "explicit refresh permits exact current bookmark removal");
Check(!controller.ConfirmRemove() && backend.Removals == 1, "consumed confirmation cannot remove another bookmark");
Check(!controller.Add(new string('x', 81)) && !controller.Add("line\nbreak") && backend.Additions == 2,
    "invalid names cannot reach persistence");
Check(controller.RequestRemove(), "remaining bookmark obtains independent confirmation");
backend.Context = backend.Context with { SceneGeneration = 2 };
Check(!controller.Snapshot().Current && !controller.ConfirmRemove() && backend.Removals == 1
    && !controller.Refresh(), "scene replacement cannot rebind an old annotation panel");
controller.CancelRemove();
backend.Context = backend.Context with { SceneGeneration = 1, LogicalPath = "other.ppdemo" };
Check(!controller.Add("wrong replay") && !controller.Jump(), "logical replay path replacement invalidates writes and seek");
backend.Context = backend.Context with { LogicalPath = "synthetic.ppdemo", PlaybackPath = "reopened.ppdemo" };
Check(!controller.Refresh(), "physical playback replacement invalidates panel");
backend.Context = backend.Context with { PlaybackPath = "synthetic.ppdemo", RecordingRevision = "replaced-recording" };
Check(!controller.Refresh(), "recording file revision replacement requires reopening the panel");
bool wrongThread = Task.Run(() =>
{
    try { controller.RequestRemove(); return false; }
    catch (InvalidOperationException) { return true; }
}).GetAwaiter().GetResult();
Check(wrongThread, "bookmark edits reject another thread");
controller.Dispose();
Check(!controller.Add("retired") && !controller.Refresh() && controller.Snapshot().Entries.Length == 0,
    "retirement drops selection and refuses further work");
Console.WriteLine($"PASS {checks} in-memory bookmark ownership, revision and exact confirmation checks; no replay files or scenes opened");

sealed class MemoryBookmarks : ITheatreBookmarkBackend
{
    public TheatreBookmarkContext Context = new(1, "synthetic.ppdemo", "synthetic.ppdemo", "recording-v1", "0", 60, 600);
    public ImmutableArray<TheatreBookmarkEntry> Rows = ImmutableArray<TheatreBookmarkEntry>.Empty;
    public int Additions, Removals;
    public List<uint> Seeks = new();
    public TheatreBookmarkContext Capture() => Context;
    public bool IsCurrent(TheatreBookmarkContext context)
        => Context.SceneGeneration == context.SceneGeneration && Context.LogicalPath == context.LogicalPath
            && Context.PlaybackPath == context.PlaybackPath;
    public ImmutableArray<TheatreBookmarkEntry> Read(TheatreBookmarkContext context) { Validate(context); return Rows; }
    public void Add(TheatreBookmarkContext context, string name)
    {
        Validate(context); Additions++;
        Rows = Rows.Add(new(Guid.NewGuid(), context.Frame, name, DateTime.UtcNow)); Bump();
    }
    public void Remove(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
    {
        Validate(context);
        if (!Rows.Contains(exact)) throw new InvalidOperationException("Exact bookmark changed.");
        Rows = Rows.Remove(exact); Removals++; Bump();
    }
    public void Seek(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
    {
        Validate(context);
        if (!Rows.Contains(exact)) throw new InvalidOperationException("Exact bookmark changed.");
        Seeks.Add(exact.Frame);
    }
    private void Bump() => Context = Context with { AnnotationRevision = Guid.NewGuid().ToString() };
    private void Validate(TheatreBookmarkContext context)
    {
        if (!IsCurrent(context) || Context.RecordingRevision != context.RecordingRevision
            || Context.AnnotationRevision != context.AnnotationRevision) throw new InvalidOperationException("Stale owner or file revision.");
    }
}
