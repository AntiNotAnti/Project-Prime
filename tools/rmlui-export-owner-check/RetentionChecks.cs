using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal static class RetentionChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Actual export retention: " + message); checks++; }
        string directory = Path.Combine(Path.GetTempPath(), "prime-export-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            AwaitStopped();
            var retryBefore = ReplayExportQueue.OwnerSnapshot().Retry;
            WeakReference detached = CancelDetached(directory);
            Collect();
            Check(!detached.IsAlive && ReplayExportQueue.OwnerSnapshot().Retry == retryBefore,
                "unleased detached metadata is released without replacing the real retry receipt");
            WeakReference direct = CancelStarted(directory);
            Collect();
            Check(direct.IsAlive && IsRetry(direct),
                "successful direct Start cancellation retains its exact immutable retry manifest");
            var retired = new List<(WeakReference Manifest, WeakReference Policy)>();
            for (int i = 0; i < 12; i++) retired.Add(CancelAccepted(directory, i));
            Collect();
            Check(!direct.IsAlive && retired.Take(retired.Count - 1).All(pair => !pair.Manifest.IsAlive && !pair.Policy.IsAlive),
                "superseded cancelled manifests and their weak-table policy closures are released");
            Check(retired[^1].Manifest.IsAlive && retired[^1].Policy.IsAlive
                && IsRetry(retired[^1].Manifest),
                "only the latest accepted cancellation keeps its exact retry manifest and policy");
            Check(ReplayExportQueue.PendingCount == 0 && !ReplayVideoExporter.Active,
                "retention checks leave no additional pending render or encoder owner");
            Console.WriteLine($"PASS {checks} actual source-linked cancellation/retry receipt retention checks; no worlds or encoders");
        }
        finally { ReplayVideoExporter.Cancel(); ReplayExportQueue.ClearPending(); Directory.Delete(directory, true); }
    }
    private static ReplayVideoExportManifest Job(string directory) => new(3, DemoPlayback.PlaybackPath!, 1, 2,
        1280, 720, 60, true, false, false, Path.Combine(directory, "frame_%08d.png"),
        Path.Combine(directory, "replay.mp4"), "FAKE-NO-ENCODER");
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsRetry(WeakReference manifest) => ReferenceEquals(ReplayExportQueue.OwnerSnapshot().Retry?.Manifest, manifest.Target);
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void AwaitStopped()
    {if(!SpinWait.SpinUntil(()=>!ReplayVideoExporter.Active,TimeSpan.FromSeconds(10)))throw new InvalidOperationException("Cancelled PNG owner did not retire.");}
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CancelDetached(string directory)
    {
        var job = Job(directory); var weak = new WeakReference(job);
        typeof(ReplayVideoExporter).GetField("_job", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, job);
        ReplayVideoExporter.Cancel(); AwaitStopped(); return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CancelStarted(string directory)
    {
        var job = Job(directory); var weak = new WeakReference(job);
        if (!ReplayVideoExporter.Start(job)) throw new InvalidOperationException("Actual direct Start must acquire playback ownership.");
        ReplayVideoExporter.Cancel(); AwaitStopped(); return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference) CancelAccepted(string directory, int index)
    {
        var payload = new object(); var job = Job(directory) with { PresetName = "Cancelled " + index };
        var policy = new ReplayExportPolicy(() => { GC.KeepAlive(payload); return null; },
            () => null, ReplayExportPresentation.Capture());
        if (!ReplayExportQueue.EnqueueOwned(job, policy) || !ReplayExportQueue.Pump())
            throw new InvalidOperationException("Actual accepted Start must acquire playback ownership.");
        ReplayVideoExporter.Cancel(); AwaitStopped(); return (new(job), new(payload));
    }
}
