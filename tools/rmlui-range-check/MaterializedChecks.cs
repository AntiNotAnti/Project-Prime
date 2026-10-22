using System.Diagnostics;
using MphRead.Mods.Launcher.Core;

internal static class MaterializedChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Materialized clip contract: " + message);
            checks++;
        }
        void Finish(TheatreRangeController controller)
        {
            var timer = Stopwatch.StartNew();
            while (controller.Snapshot().SavingClip && timer.ElapsedMilliseconds < 2000) Thread.Sleep(1);
            Check(!controller.Snapshot().SavingClip, "queued async completion returns to its presentation owner");
        }
        var memory = new PendingClips();
        using var controller = new TheatreRangeController(memory);
        Check(controller.SaveMaterializedClip(" Original range ") && controller.Snapshot().SavingClip,
            "clip save captures one reviewed range and starts one existing-backend operation");
        Check(memory.Jobs[0].Name == "Original range" && memory.Jobs[0].Context is { MarkIn: 60, MarkOut: 180 },
            "async operation receives immutable original marks and trimmed name");
        Check(!controller.MarkIn() && !controller.Refresh() && !controller.CreateVirtualClip("other")
            && !controller.SaveMaterializedClip("parallel") && memory.Jobs.Count == 1,
            "busy operation refuses selection edits and competing writes");
        // A seek may replace the transport while retaining this exact scene/path/marks.
        memory.CurrentTransport = "replacement-transport.ppdemo";
        memory.Context = memory.Context with { Replay = memory.Context.Replay with { Frame = 240 } };
        memory.Jobs[0].Result.SetResult(new(true, "original-transport-output.ppdemo", "")); Finish(controller);
        Check(controller.Snapshot().Status.Contains("original-transport-output.ppdemo")
            && !controller.Snapshot().Status.Contains(memory.CurrentTransport),
            "seek/transport replacement publishes the original operation output rather than a changed facade value");
        Check(controller.TryTakeMaterializedClipSaved(out string name) && name == "Original range"
            && !controller.TryTakeMaterializedClipSaved(out _), "successful current operation clears only its captured name once");

        Check(controller.SaveMaterializedClip("Retry me"), "same replay can start a later clip save");
        memory.Jobs[1].Result.SetException(new IOException("synthetic locked destination")); Finish(controller);
        Check(controller.Snapshot() is { Current: true, MarkIn: 60, MarkOut: 180 }
            && controller.Snapshot().Error.Contains("synthetic locked destination") && !controller.TryTakeMaterializedClipSaved(out _),
            "write failure retains reviewed range and does not consume entered name");
        Check(controller.SaveMaterializedClip("Retry me"), "failure permits explicit retry with the same name");
        memory.Jobs[2].Result.SetResult(new(true, "saved-with-name-warning.ppdemo", "Clip saved, but name could not be written.")); Finish(controller);
        Check(controller.Snapshot().Status.Contains("saved-with-name-warning.ppdemo") && controller.Snapshot().Error.Length > 0
            && !controller.TryTakeMaterializedClipSaved(out _), "partial naming failure reports saved recording and preserves name for review");

        Check(controller.SaveMaterializedClip("Cancel me") && controller.CancelMaterializedClip()
            && memory.Jobs[3].Cancellation.IsCancellationRequested, "explicit cancellation reaches captured backend operation");
        Check(controller.SaveMaterializedClip("New operation"), "cancelled operation releases presentation for a new request");
        memory.Jobs[3].Result.SetResult(new(true, "late-cancelled-output.ppdemo", ""));
        Thread.Sleep(10); controller.Pump();
        Check(controller.Snapshot().SavingClip && !controller.Snapshot().Status.Contains("late-cancelled-output"),
            "generation fence prevents late cancelled success from replacing newer operation state");
        memory.Jobs[4].Result.SetResult(new(true, "new-operation-output.ppdemo", "")); Finish(controller);
        Check(controller.TryTakeMaterializedClipSaved(out name) && name == "New operation", "only new operation can acknowledge its name");

        Check(controller.SaveMaterializedClip("Captured old marks"), "new request captures range before external edit");
        memory.Context = memory.Context with { MarkOut = 300 };
        Check(!controller.Snapshot().Current && controller.Snapshot().MarkOut == 180, "external selection edit clears authority but retains reviewed range while saving");
        memory.Jobs[5].Result.SetResult(new(true, "captured-old-range.ppdemo", "")); Finish(controller);
        Check(controller.Snapshot().Status.Contains("captured-old-range.ppdemo") && !controller.TryTakeMaterializedClipSaved(out _),
            "completion reports captured range result without reauthorizing changed selection or clearing name");
        Check(controller.Refresh() && controller.SaveMaterializedClip("Earlier scene"), "explicit refresh reviews changed marks for a later operation");
        memory.Context = memory.Context with { Replay = memory.Context.Replay with { SceneGeneration = 2, LogicalPath = "other.ppdemo" } };
        Check(!controller.Snapshot().Current && memory.Jobs[6].Cancellation.IsCancellationRequested,
            "scene/path replacement cancels captured operation without adopting another replay");
        memory.Jobs[6].Result.SetResult(new(true, "earlier-scene-output.ppdemo", "")); Finish(controller);
        Check(controller.Snapshot().Status.Contains("earlier-scene-output.ppdemo") && !controller.TryTakeMaterializedClipSaved(out _),
            "late stale-owner result remains an original-operation report and cannot clear current UI name");

        var retiringMemory = new PendingClips();
        var retiring = new TheatreRangeController(retiringMemory);
        Check(retiring.SaveMaterializedClip("Retiring"), "retirement test starts an isolated pending operation");
        retiring.Dispose();
        Check(retiringMemory.Jobs[0].Cancellation.IsCancellationRequested && !retiring.Snapshot().Current, "retirement cancels job and drops authority");
        retiringMemory.Jobs[0].Result.SetResult(new(true, "retired-late.ppdemo", "")); Thread.Sleep(10); retiring.Pump();
        Check(!retiring.TryTakeMaterializedClipSaved(out _) && !retiring.SaveMaterializedClip("No"), "retired completion cannot resurrect UI or another operation");
        var cancelledMemory = new PendingClips();
        using var cancelled = new TheatreRangeController(cancelledMemory);
        Check(!cancelled.SaveMaterializedClip("bad\nname") && cancelledMemory.Jobs.Count == 0,
            "invalid clip name cannot launch a background operation");
        Check(cancelled.SaveMaterializedClip("Backend cancelled"), "backend cancellation test starts a captured operation");
        cancelledMemory.Jobs[0].Result.SetCanceled(); Finish(cancelled);
        Check(cancelled.Snapshot().Error.Contains("cancelled") && !cancelled.TryTakeMaterializedClipSaved(out _)
            && cancelled.Snapshot().Current, "backend cancellation preserves range/name and releases busy state for retry");
        var immediateMemory = new ImmediateClip();
        using var immediate = new TheatreRangeController(immediateMemory);
        Check(immediate.SaveMaterializedClip("Already completed"), "already-completed backend queues its exact successful result");
        Check(!immediate.CancelMaterializedClip() && !immediateMemory.Cancellation.IsCancellationRequested
            && immediate.Snapshot().Status.Contains("already-completed-original.ppdemo"),
            "Cancel pumps a just-completed queued success and preserves its actual saved result");
        Check(immediate.TryTakeMaterializedClipSaved(out name) && name == "Already completed",
            "just-completed success retains its one-time captured-name acknowledgement");
        Check(Task.Run(() => { try { controller.Pump(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(),
            "completion publication rejects another thread");
        Console.WriteLine($"PASS {checks} materialized clip async ownership/result/cancellation checks; no replay files or world opened");
        return checks;
    }
    private sealed record Job(TheatreRangeContext Context, string Name, CancellationToken Cancellation,
        TaskCompletionSource<TheatreMaterializedClipResult> Result);
    private class PendingClips : ITheatreRangeBackend
    {
        internal TheatreRangeContext Context = new(new(1, "synthetic.ppdemo", "synthetic.ppdemo", "recording", "annotation", 60, 600), 60, 180);
        internal readonly List<Job> Jobs = new();
        internal string CurrentTransport = "original-transport";
        public bool MaterializedClipsAvailable => true;
        public TheatreRangeContext Capture() => Context;
        public bool IsCurrent(TheatreRangeContext context) => context.Replay.SceneGeneration == Context.Replay.SceneGeneration
            && context.Replay.LogicalPath == Context.Replay.LogicalPath && context.Replay.PlaybackPath == Context.Replay.PlaybackPath;
        public TheatreRangeSelection ReadSelection(TheatreRangeContext context)
        {
            if (!IsCurrent(context)) throw new InvalidOperationException("Synthetic owner changed.");
            return new(Context.MarkIn, Context.MarkOut, Context.Replay.Duration);
        }
        public virtual Task<TheatreMaterializedClipResult> SaveMaterializedClip(TheatreRangeContext context, string name, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<TheatreMaterializedClipResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Jobs.Add(new(context, name, cancellation, result)); return result.Task;
        }
        public void MarkIn(TheatreRangeContext context) => throw new InvalidOperationException("Unexpected transport mark write.");
        public void MarkOut(TheatreRangeContext context) => throw new InvalidOperationException("Unexpected transport mark write.");
        public string SaveHighlight(TheatreRangeContext context, string name) => throw new InvalidOperationException("Unexpected highlight write.");
        public string CreateVirtualClip(TheatreRangeContext context, string name) => throw new InvalidOperationException("Unexpected virtual clip write.");
    }
    private sealed class ImmediateClip : PendingClips
    {
        internal CancellationToken Cancellation;
        public override Task<TheatreMaterializedClipResult> SaveMaterializedClip(TheatreRangeContext context, string name,
            CancellationToken cancellation)
        {
            Cancellation = cancellation;
            return Task.FromResult(new TheatreMaterializedClipResult(true, "already-completed-original.ppdemo", ""));
        }
    }
}
