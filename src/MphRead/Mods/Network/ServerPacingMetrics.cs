using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>Observe the existing pacing policy before tuning it. Counts are
/// allocation-free; elapsed wait timing is opt-in because Yield can be very frequent.</summary>
public sealed class ServerPacingMetrics
{
    public bool MeasureWaitTime { get; set; }
    public long Sleeps { get; private set; }
    public long Yields { get; private set; }
    public long Spins { get; private set; }
    public double WaitMilliseconds { get; private set; }
    internal void Note(int kind, long start)
    {
        if (kind == 0) Sleeps++; else if (kind == 1) Yields++; else Spins++;
        if (start != 0) WaitMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    public override string ToString() => $"pacing sleep={Sleeps} yield={Yields} spin={Spins}"
        + (MeasureWaitTime ? $" wait={WaitMilliseconds:0.0}ms" : "");
    internal static void Pace(double remaining, ServerPacingMetrics metrics)
    {
        long start = metrics.MeasureWaitTime ? Stopwatch.GetTimestamp() : 0;
        double coarseSleepRoom = OperatingSystem.IsWindows() ? .012 : .002;
        if (remaining > coarseSleepRoom) { Thread.Sleep(1); metrics.Note(0, start); return; }
        if (remaining > .00025 || remaining <= 0) { Thread.Yield(); metrics.Note(1, start); return; }
        long deadline = Stopwatch.GetTimestamp() + (long)(remaining * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline) Thread.SpinWait(32);
        metrics.Note(2, start);
    }
}

/// <summary>Offline scheduler measurement; no network, assets or process allocation.
/// It reports CPU and wake lateness rather than enforcing machine-specific thresholds.</summary>
public static class ServerPacingBenchmark
{
    public static int Run()
    {
        using Process process = Process.GetCurrentProcess();
        var metrics = new ServerPacingMetrics { MeasureWaitTime = true };
        var clock = Stopwatch.StartNew();
        TimeSpan cpu = process.TotalProcessorTime;
        const int frames = 120;
        var late = new double[frames];
        for (int frame = 1; frame <= frames; frame++)
        {
            double deadline = frame / 60.0;
            while (clock.Elapsed.TotalSeconds < deadline)
                ServerPacingMetrics.Pace(deadline - clock.Elapsed.TotalSeconds, metrics);
            late[frame - 1] = Math.Max(0, clock.Elapsed.TotalSeconds - deadline) * 1000;
        }
        double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        Array.Sort(late);
        Console.WriteLine($"PACING MEASUREMENT OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}"
            + $" arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} frames={frames}"
            + $" wall={clock.Elapsed.TotalMilliseconds:0.0}ms cpu={cpuMs:0.0}ms"
            + $" lateness-p50={late[60]:0.000}ms p99={late[118]:0.000}ms worst={late[119]:0.000}ms {metrics}");
        return 0;
    }
}
