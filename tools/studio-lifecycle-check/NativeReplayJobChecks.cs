using System.Reflection;
using System.Text.Json;
using Avalonia.VisualTree;
using Avalonia.Threading;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Replay;

internal static partial class Program
{
    private static async Task<object> RunNativeReplaySeekJobCheckAsync(StudioWindow window, string output)
    {
        var document = (ReplayStudioDocument)window.Documents.ActiveDocument!;
        var session = document.Session!; var player = session.Player;
        var seek = session.GetType().GetMethod("SeekAsync") ?? throw new MissingMethodException("Actual owner seek job API is missing.");
        var cancel = player.GetType().GetMethod("CancelSeek")!;
        var timer = (DispatcherTimer)document.Host.GetType().GetField("_viewTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document.Host)!;
        bool wasEnabled = timer.IsEnabled; timer.Stop();
        try
        {
            // Freeze only the tool's timing fixture. Production single/four-view
            // clock correctness is measured independently before this command.
            Task positioned = SeekAsync(600); await PumpUntilReadyAsync(); await positioned.WaitAsync(TimeSpan.FromSeconds(10));
            var current = player.Snapshot(); int checkpoints = player.Status.CheckpointCount; long checkpointBytes = player.Status.CheckpointBytes;
            Task backward = SeekAsync(0); player.Advance(TimeSpan.Zero);
            bool preparing = player.Status.Preparing && !player.Status.Ready;
            long oldRequest = ReadId("SeekRequestId");
            StudioJob? job = window.Jobs.Jobs.LastOrDefault(job => job.Title == "Seek replay to frame 0" && job.State == StudioJobState.Running);
            bool observed = job is not null; job?.Cancel(); bool canceled = false;
            try { await backward.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { canceled = true; }
            player.Advance(TimeSpan.Zero); var retained = player.Snapshot();
            bool safe = SameReplayWorld(current, retained) && player.Status is { Ready: true, State: "Paused" }
                && player.Status.CheckpointCount == checkpoints && player.Status.CheckpointBytes == checkpointBytes;
            bool centralCanceled = window.Jobs.Jobs.Last(job => job.Title == "Seek replay to frame 0").State == StudioJobState.Cancelled;
            Task restore = SeekAsync(17); long newerRequest = ReadId("SeekRequestId");
            cancel.Invoke(player, [oldRequest]); await PumpUntilReadyAsync(); await restore.WaitAsync(TimeSpan.FromSeconds(10));
            bool newerCompleted = player.Status.Frame == 17 && ReadId("AppliedSeekRequestId") == newerRequest && ReadId("SettledSeekRequestId") >= newerRequest;
            bool drained = !(bool)session.GetType().GetProperty("HasPendingTransportJobs")!.GetValue(session)!;
            var unpresented = player.Snapshot();
            string unpresentedCapsule = Path.Combine(output,"native-replay-seek-unpresented.capsule");
            CaptureReplayCapsule(player,unpresentedCapsule);
            // A new simulation replica is Ready before its first GPU draw. Compare
            // complete graphs only after the same explicit view is presented.
            var restored = CaptureNativeReplayJobWorld(window,Path.Combine(output,"native-replay-seek-restored.png"));
            CompareReplayCapsules(unpresentedCapsule,Path.Combine(output,"native-replay-seek-restored.png.capsule"),
                Path.Combine(output,"native-replay-seek-first-presentation-difference.json"));
            return new { PreparingObserved = preparing, JobObserved = observed, Canceled = canceled, CurrentRetained = safe,
                CentralCanceled = centralCanceled, NewerCompleted = newerCompleted, Drained = drained, Before = current, After = retained,
                UnpresentedRestored = unpresented, Restored = restored };
        }
        finally { if (wasEnabled) timer.Start(); }

        Task SeekAsync(uint frame) => (Task)seek.Invoke(session, [frame, false, CancellationToken.None])!;
        long ReadId(string name) => (long)player.GetType().GetProperty(name)!.GetValue(player)!;
        async Task PumpUntilReadyAsync()
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            do { player.Advance(TimeSpan.Zero); await Task.Delay(1); }
            while (!player.Status.Ready && wait.Elapsed < TimeSpan.FromSeconds(30));
            if (!player.Status.Ready) throw new TimeoutException("Native owner seek did not reach its ready frame.");
        }
    }

    private static async Task CheckNativeReplaySeekJobsAsync(System.Diagnostics.Process process, string output)
    {
        const string capturePrefix = "REPLAY-JOB-WORLD ";
        string referenceImage = Path.Combine(output,"native-replay-seek-reference.png");
        var before = JsonSerializer.Deserialize<StudioReplayWorldSnapshot>((await NativeCommandAsync(process,
            "capture-replay-job-world "+referenceImage,capturePrefix))[capturePrefix.Length..],new JsonSerializerOptions{IncludeFields=true})!;
        const string prefix = "REPLAY-SEEK-JOBS ";
        using var result = JsonDocument.Parse((await NativeCommandAsync(process, "replay-seek-job-check "+output, prefix))[prefix.Length..]);
        var value = result.RootElement;
        Check(value.GetProperty("PreparingObserved").GetBoolean() && value.GetProperty("JobObserved").GetBoolean(),
            "actual native backward source preparation remains observable as a central seek job");
        Check(value.GetProperty("Canceled").GetBoolean() && value.GetProperty("CentralCanceled").GetBoolean() && value.GetProperty("CurrentRetained").GetBoolean(),
            "central native seek cancellation preserves the exact published gameplay/presentation/graph and checkpoint payload accounting");
        Check(value.GetProperty("NewerCompleted").GetBoolean() && value.GetProperty("Drained").GetBoolean(),
            "late cancellation from an old native job cannot cancel the newer owner seek and both observers drain");
        var after = value.GetProperty("Restored").Deserialize<StudioReplayWorldSnapshot>(new JsonSerializerOptions{IncludeFields=true})!;
        string restoredImage = Path.Combine(output,"native-replay-seek-restored.png");
        CompareReplayCapsules(referenceImage+".capsule",restoredImage+".capsule",Path.Combine(output,"native-replay-seek-graph-difference.json"));
        CheckNativePng(referenceImage,640,360,"native seek reference explicitly presents the current view before taking the full graph oracle");
        CheckNativePng(restoredImage,640,360,"native return seek explicitly presents the same current view before complete graph comparison");
        File.WriteAllText(Path.Combine(output, "native-replay-seek-jobs.json"),JsonSerializer.Serialize(new{Checks=value,Reference=before,Restored=after},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Check(SameReplayWorld(before, after), "actual native seek job fixture returns to the same presented frame and complete private world: "
            +JsonSerializer.Serialize(new{Reference=before,Restored=after}));
    }

    private static StudioReplayWorldSnapshot CaptureNativeReplayJobWorld(StudioWindow window,string image)
    {
        var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
        var viewport=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().First(view=>view.IsEffectivelyVisible);
        var capture=viewport.Capture(640,360)??throw new InvalidOperationException("Native seek did not capture its current view.");
        StudioReplayPlayer.SavePng(image,capture);
        var snapshot=document.Session!.Player.Snapshot();
        CaptureReplayCapsule(document.Session.Player,image+".capsule");
        return snapshot;
    }
}
