using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Replay;

internal static class FrameWriterChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static ReplayExportPixels Frame(int index) => new(new byte[] { 20, 30, 40 }, 1, 1, index.ToString());
        static ReplayFrameWriteResult Wait(ReplayFrameWriter writer)
            => writer.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var paths = new List<string>();
        int producer = Environment.CurrentManagedThreadId, worker = producer;
        var queue = new ReplayFrameWriter(frame =>
        {
            worker = Environment.CurrentManagedThreadId;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture writer was not released.");
            paths.Add(frame.Path);
        });
        try
        {
            using (var first = queue.TryReserve()) { check(first != null, "PNG slot reserved before readback"); first!.Submit(Frame(0)); }
            check(entered.Wait(TimeSpan.FromSeconds(10)), "PNG worker starts independently of the producer");
            using (var second = queue.TryReserve()) second!.Submit(Frame(1));
            using (var third = queue.TryReserve()) third!.Submit(Frame(2));
            check(queue.Pending == 3 && queue.TryReserve() == null && queue.MaximumPending == 3,
                "backpressure bounds queued, reserved and in-flight RGB buffers together");
            queue.Complete();
            check(!queue.Completion.IsCompleted && queue.TryReserve() == null,
                "complete closes admission but waits for the last PNG before encoder handoff");
        }
        finally { release.Set(); }
        var result = Wait(queue);
        check(worker != producer && paths.SequenceEqual(new[] { "0", "1", "2" })
            && result.Written == 3 && result.Error == null && !result.Cancelled && queue.Pending == 0,
            "one worker drains each exact sequence frame in order without skips or duplicate writes");
        queue.Dispose();

        entered.Reset(); release.Reset();
        var failed = new ReplayFrameWriter(_ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture failed writer was not released.");
            throw new IOException("injected disk full");
        });
        try
        {
            using (var reservation = failed.TryReserve()) reservation!.Submit(Frame(0));
            check(entered.Wait(TimeSpan.FromSeconds(10)), "failure fixture starts its first PNG");
            using (var reservation = failed.TryReserve()) reservation!.Submit(Frame(1));
            using (var reservation = failed.TryReserve()) reservation!.Submit(Frame(2));
        }
        finally { release.Set(); }
        result = Wait(failed);
        check(result.Error == "injected disk full" && result.Written == 0 && failed.Pending == 0
            && failed.TryReserve() == null, "worker failure is observable, retires buffers and closes admission");
        failed.Dispose();

        entered.Reset(); release.Reset();
        var cancelled = new ReplayFrameWriter(_ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture cancellation writer was not released.");
        });
        try
        {
            using (var reservation = cancelled.TryReserve()) reservation!.Submit(Frame(0));
            check(entered.Wait(TimeSpan.FromSeconds(10)), "cancel fixture owns an in-flight PNG");
            using (var reservation = cancelled.TryReserve()) reservation!.Submit(Frame(1));
            cancelled.Cancel();
            check(!cancelled.Completion.IsCompleted && cancelled.TryReserve() == null,
                "cancel keeps output ownership until an already-running native PNG write returns");
        }
        finally { release.Set(); }
        result = Wait(cancelled);
        check(result.Cancelled && result.Error == null && result.Written == 1 && cancelled.Pending == 0,
            "cancel drains pending buffers without starting the next PNG");
        cancelled.Dispose();

        var abandoned = new ReplayFrameWriter(_ => { });
        using (var reservation = abandoned.TryReserve())
        {
            check(reservation != null && abandoned.Pending == 1, "readback owns its reservation before submission");
        }
        check(abandoned.Pending == 0, "failed readback releases its unsubmitted buffer reservation");
        abandoned.Complete(); Wait(abandoned); abandoned.Dispose();

        var lifetime = new ReplayFrameWriter(static _ => { });
        var pixelsReference = SubmitLifetimeFrame(lifetime);
        check(SpinWait.SpinUntil(() => lifetime.Written == 1 && lifetime.Pending == 0, TimeSpan.FromSeconds(10)),
            "completed PNG releases its capacity before the worker waits for another frame");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!pixelsReference.IsAlive, "idle live writer retains no previously encoded RGB buffer");
        lifetime.Complete(); Wait(lifetime); lifetime.Dispose();

        long now = Stopwatch.GetTimestamp(), interval = Stopwatch.Frequency / 10;
        check(ReplayAuxiliaryPreviewPolicy.Reuse(true, true, now, now)
            && ReplayAuxiliaryPreviewPolicy.Reuse(true, true, now + interval / 2, now),
            "gesture previews reuse optional auxiliary tiles within their 100 ms refresh budget");
        check(!ReplayAuxiliaryPreviewPolicy.Reuse(false, true, now, now)
            && !ReplayAuxiliaryPreviewPolicy.Reuse(true, false, now, now)
            && !ReplayAuxiliaryPreviewPolicy.Reuse(true, true, now + interval + 1, now)
            && !ReplayAuxiliaryPreviewPolicy.Reuse(true, true, now - 1, now),
            "settle, stale identity/geometry, expired cache and reversed clock force fresh auxiliary renders");

        foreach (int fps in new[] { 30, 60, 120, 240 })
        {
            check(ReplayExportRates.IsSupported(fps), $"{fps} FPS is admitted by the actual export pipeline");
            var sampler = new ReplayExportSampler(123, 603, fps);
            check(sampler.Count == 8L * fps + 1, $"{fps} FPS inclusive sequence has exact rational length");
            for (long index = 0; index < sampler.Count; index++)
            {
                var sample = sampler.At(index);
                double expected = 123 + index * 60d / fps;
                if (sample.Frame != expected || sample.SimulationFrame != Math.Ceiling(expected))
                    throw new InvalidDataException($"{fps} FPS sample {index} drifted.");
            }
            check(true, $"{fps} FPS every sample preserves exact canonical time without accumulation");
        }

        CheckExporterDrain(check);
        CheckDetachedPng(check);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubmitLifetimeFrame(ReplayFrameWriter writer)
    {
        byte[] pixels = { 20, 30, 40 };
        var reference = new WeakReference(pixels);
        using var reservation = writer.TryReserve();
        reservation!.Submit(new(pixels, 1, 1, "lifetime"));
        return reference;
    }

    private static void CheckExporterDrain(Action<bool, string> check)
    {
        static void Set(string field, object? value) => typeof(ReplayVideoExporter)
            .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
        static ReplayVideoExportManifest Manifest() => new(3, "detached-worker-fixture.ppdemo", 1, 2,
            64, 64, 60, false, false, false, "detached/frame_%08d.png", "detached/video.mp4", "",
            EncoderExecutable: "");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        foreach (bool cancel in new[] { false, true })
        {
            entered.Reset(); release.Reset();
            var job = Manifest();
            var writer = new ReplayFrameWriter(_ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Exporter fixture writer was not released.");
            });
            using (var reservation = writer.TryReserve())
                reservation!.Submit(new(new byte[] { 30, 40, 50 }, 1, 1, "0"));
            Set("_frameWriter", writer); Set("_finishingFrames", job); Set("_encodingJob", job);
            Set("_written", 1); Set("_state", ReplayExportState.Encoding);
            writer.Complete();
            try
            {
                check(entered.Wait(TimeSpan.FromSeconds(10)) && ReplayVideoExporter.Active
                    && ReferenceEquals(ReplayVideoExporter.OwnedManifest, job)
                    && ReplayVideoExporter.State == ReplayExportState.Encoding,
                    "exporter retains exact manifest/output ownership while its last PNG drains");
                if (cancel)
                {
                    ReplayVideoExporter.Cancel();
                    check(ReplayVideoExporter.Active && ReferenceEquals(ReplayVideoExporter.OwnedManifest, job),
                        "cancelled exporter cannot hand its output directory to another job before PNG retirement");
                }
            }
            finally { release.Set(); }
            check(SpinWait.SpinUntil(() => !ReplayVideoExporter.Active, TimeSpan.FromSeconds(10))
                && ReplayVideoExporter.State == (cancel ? ReplayExportState.Cancelled : ReplayExportState.Completed)
                && ReplayVideoExporter.OwnedManifest == null && ReplayVideoExporter.FramesWritten == 1,
                cancel ? "exporter cancellation releases detached worker after its final write"
                    : "PNG-only exporter completes only after its exact frame sequence drains");
        }
    }

    private static void CheckDetachedPng(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "prime-detached-png-" + Guid.NewGuid().ToString("N") + ".png");
        var original = MphRead.Mods.ScreenCapture.PngWriter;
        MphRead.Mods.ScreenCapture.PngWriter = null;
        try
        {
            var queue = new ReplayFrameWriter(MphRead.Mods.ScreenCapture.ExportPngWriter());
            using (var reservation = queue.TryReserve())
                reservation!.Submit(new(Enumerable.Repeat((byte)180, 2 * 3 * 3).ToArray(), 2, 3, path));
            queue.Complete();
            var result = queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            queue.Dispose();
            check(result.Error == null && result.Written == 1 && File.Exists(path),
                "real PNG encoder creates a detached image on its worker without a Scene or GL context");
            if (File.Exists(path))
            {
                byte[] png = File.ReadAllBytes(path);
                check(png.Length > 24 && BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) == 2
                    && BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) == 3,
                    "detached PNG retains the exact readback dimensions");
            }
        }
        finally { MphRead.Mods.ScreenCapture.PngWriter = original; File.Delete(path); }
    }
}
