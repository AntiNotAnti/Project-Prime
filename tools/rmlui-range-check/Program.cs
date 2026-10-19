using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException("Range contract: " + message);
    checks++;
}
var memory = new MemoryRanges();
using var ranges = new TheatreRangeController(memory);
Check(ranges.Snapshot() is { Current: true, ValidRange: false, MarkIn: null, MarkOut: null }, "empty marks come from the existing injected transport");
Check(!ranges.SaveHighlight("empty") && !ranges.CreateVirtualClip("empty") && memory.Highlights.Count == 0 && memory.Clips.Count == 0,
    "missing range cannot create annotations or clips");
Check(ranges.MarkIn() && ranges.Snapshot().MarkIn == 60, "Mark In uses captured playhead");
memory.Context = memory.Context with { Replay = memory.Context.Replay with { Frame = 180 } };
Check(ranges.MarkOut() && ranges.Snapshot() is { MarkIn: 60, MarkOut: 180, ValidRange: true }, "Mark Out creates a bounded valid transport selection");
Check(ranges.SaveHighlight("  Native highlight  ") && memory.Highlights.Single() is { Name: "Native highlight", Start: 60, End: 180 },
    "named highlight receives immutable reviewed marks and bounded trimmed name");
Check(ranges.CreateVirtualClip("Native clip") && memory.Clips.Single() is { Name: "Native clip", Start: 60, End: 180 },
    "virtual clip receives the same reviewed range without a new playback session");
memory.FailWrite = true;
Check(!ranges.SaveHighlight("Retry this name") && ranges.Snapshot() is { Current: true, MarkIn: 60, MarkOut: 180 }
    && memory.Highlights.Count == 1, "ordinary backend failure retains reviewed range and permits retry");
memory.FailWrite = false;
Check(ranges.SaveHighlight("Retry this name") && memory.Highlights[^1].Name == "Retry this name", "same entered name can be retried after file access recovers");
Check(!ranges.SaveHighlight(new string('n', 81)) && !ranges.CreateVirtualClip("line\nbreak") && memory.Clips.Count == 1,
    "invalid names cannot reach either writer");
memory.Context = memory.Context with { MarkOut = 240 };
int captures = memory.Captures;
var changed = ranges.Snapshot();
Check(!changed.Current && changed.MarkOut == 180 && changed.LiveSelection?.MarkOut == 240 && memory.Captures == captures,
    "readonly snapshot reports live marks without capturing disk witness or reauthorizing reviewed selection");
Check(!ranges.CreateVirtualClip("stale") && !ranges.Snapshot().Current && ranges.Snapshot().MarkOut == 180,
    "external mark edit clears authority while retaining previous reviewed range");
Check(!ranges.SaveHighlight("still stale") && memory.Highlights.Count == 2, "stale selection cannot mutate until explicitly refreshed");
Check(ranges.Refresh() && ranges.Snapshot() is { Current: true, MarkOut: 240 }, "Refresh reviews new marks for the same owner");
memory.Context = memory.Context with { Replay = memory.Context.Replay with { AnnotationRevision = "external" } };
Check(!ranges.SaveHighlight("stale sidecar") && !ranges.Snapshot().Current, "external annotation revision invalidates creation authority");
Check(ranges.Refresh() && ranges.SaveHighlight("fresh sidecar"), "explicit refresh accepts reviewed current sidecar revision");
memory.Context = memory.Context with { MarkIn = 240, MarkOut = 240 };
Check(ranges.Refresh() && !ranges.Snapshot().ValidRange && !ranges.CreateVirtualClip("zero"), "zero length range is rejected");
memory.Context = memory.Context with { MarkIn = 60, MarkOut = 601 };
Check(ranges.Refresh() && !ranges.SaveHighlight("outside"), "out-of-duration highlight is rejected before existing unclamped writer");
memory.Context = memory.Context with { MarkIn = 60, MarkOut = 240, Replay = memory.Context.Replay with { SceneGeneration = 2 } };
Check(!ranges.Refresh() && !ranges.Snapshot().Current, "scene generation replacement cannot rebind old range panel");
memory.Context = memory.Context with { Replay = memory.Context.Replay with { SceneGeneration = 1, PlaybackPath = "other.ppdemo" } };
Check(!ranges.MarkIn(), "physical replay replacement cannot mutate transport marks");
memory.Context = memory.Context with { Replay = memory.Context.Replay with { PlaybackPath = "synthetic.ppdemo", LogicalPath = "other.ppclip" } };
Check(!ranges.Refresh(), "logical virtual descriptor replacement cannot rebind a panel");
memory.Context = memory.Context with { Replay = memory.Context.Replay with { LogicalPath = "synthetic.ppdemo", RecordingRevision = "new file" } };
Check(!ranges.Refresh(), "recording file replacement requires a new panel");
Check(Task.Run(() => { try { ranges.MarkOut(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(),
    "range mutations reject another thread");
ranges.Dispose();
Check(!ranges.SaveHighlight("retired") && !ranges.Refresh() && !ranges.Snapshot().Current, "retired panel refuses all mutations");
Console.WriteLine($"PASS {checks} native range ownership/selection/retry checks; only in-memory fake writers, no replay files, scenes or clocks");
MaterializedChecks.Run();

sealed class MemoryRanges : ITheatreRangeBackend
{
    public TheatreRangeContext Context = new(new(1, "synthetic.ppdemo", "synthetic.ppdemo", "recording", "0", 60, 600), null, null);
    public bool FailWrite;
    public int Captures;
    public List<(string Name, uint Start, uint End)> Highlights = new(), Clips = new();
    public TheatreRangeContext Capture() { Captures++; return Context; }
    public bool IsCurrent(TheatreRangeContext context) => Context.Replay.SceneGeneration == context.Replay.SceneGeneration
        && Context.Replay.LogicalPath == context.Replay.LogicalPath && Context.Replay.PlaybackPath == context.Replay.PlaybackPath;
    public TheatreRangeSelection ReadSelection(TheatreRangeContext context)
    {
        if (!IsCurrent(context)) throw new InvalidOperationException("Synthetic owner changed.");
        return new(Context.MarkIn, Context.MarkOut, Context.Replay.Duration);
    }
    public void MarkIn(TheatreRangeContext context)
    { Validate(context); Context = Context with { MarkIn = Math.Min(context.Replay.Frame, context.MarkOut ?? context.Replay.Duration) }; }
    public void MarkOut(TheatreRangeContext context)
    { Validate(context); Context = Context with { MarkOut = Math.Max(context.Replay.Frame, context.MarkIn ?? 0) }; }
    public string SaveHighlight(TheatreRangeContext context, string name)
    {
        Validate(context); if (FailWrite) throw new IOException("Synthetic locked sidecar.");
        Highlights.Add((name, context.MarkIn!.Value, context.MarkOut!.Value));
        Context = Context with { Replay = Context.Replay with { AnnotationRevision = Guid.NewGuid().ToString() } }; return name;
    }
    public string CreateVirtualClip(TheatreRangeContext context, string name)
    {
        Validate(context); if (FailWrite) throw new IOException("Synthetic locked clip directory.");
        Clips.Add((name, context.MarkIn!.Value, context.MarkOut!.Value)); return "synthetic-new.ppclip";
    }
    private void Validate(TheatreRangeContext context)
    {
        if (!IsCurrent(context) || context.MarkIn != Context.MarkIn || context.MarkOut != Context.MarkOut
            || context.Replay.RecordingRevision != Context.Replay.RecordingRevision
            || context.Replay.AnnotationRevision != Context.Replay.AnnotationRevision)
            throw new InvalidOperationException("Stale synthetic authority.");
    }
}
