using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.Network;

// Opt-in local aggregates: no shot, player, address, position or account identifiers.
// Stopwatch durations are elapsed wall time, INCLUDING scheduling and GC, not CPU time.
// Fixed aggregates are owned by the single dedicated simulation thread; scopes overlap.
internal static class NetPerformanceEvidence
{
    internal enum Operation { Step, PumpBefore, PumpAfter, Replication, Eligibility, Settlement }
    private static readonly string? Output = Environment.GetEnvironmentVariable("PRIME_SERVER_PERF_OUTPUT");
    internal static bool Enabled => !string.IsNullOrWhiteSpace(Output);
    private static readonly long[,] Counts = new long[6, 257], Ticks = new long[6, 257], Maximum = new long[6, 257];
    private static readonly long[,] Histogram = new long[6, 32];
    private static TimeSpan PauseStart;
    private static TimeSpan CpuStart;
    private static bool Written;
    private static TimeSpan CpuTime() { using var process = Process.GetCurrentProcess(); return process.TotalProcessorTime; }
    internal static void BeginRun()
    {
        if (!Enabled) return;
        Array.Clear(Counts); Array.Clear(Ticks); Array.Clear(Maximum); Array.Clear(Histogram);
        TerminalResult = -1; Written = false;
        PauseStart = GC.GetTotalPauseDuration(); CpuStart = CpuTime();
    }
    [ThreadStatic] internal static int TerminalResult;
    internal static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    internal static void End(Operation operation, long start, int result = -1)
    {
        if (start == 0) return;
        long elapsed = Stopwatch.GetTimestamp() - start;
        int op = (int)operation, outcome = result is >= 0 and <= 255 ? result : 256;
        Counts[op, outcome]++; Ticks[op, outcome] += elapsed;
        Maximum[op, outcome] = Math.Max(Maximum[op, outcome], elapsed);
        double microseconds = elapsed * (1000000d / Stopwatch.Frequency);
        int bucket = 0;
        while (bucket < 31 && microseconds > (1L << bucket)) bucket++;
        Histogram[op, bucket]++;
    }
    internal static void Write()
    {
        if (!Enabled || Written) return;
        try
        {
            var rows = new System.Collections.Generic.List<object>();
            for (int op = 0; op < 6; op++)
                for (int result = 0; result < 257; result++)
                    if (Counts[op, result] != 0) rows.Add(new {
                        operation = ((Operation)op).ToString(), result = result == 256 ? (int?)null : result,
                        count = Counts[op, result], elapsedTicks = Ticks[op, result], maximumTicks = Maximum[op, result] });
            var histograms = new long[6][];
            for (int op = 0; op < 6; op++) { histograms[op] = new long[32]; for (int b = 0; b < 32; b++) histograms[op][b] = Histogram[op, b]; }
            var report = new {
                stopwatchFrequency = Stopwatch.Frequency, rows, histograms,
                histogramUnit = "microseconds; upper bounds 2^bucket; bucket31 includes overflow",
                processCpuMilliseconds = (CpuTime() - CpuStart).TotalMilliseconds,
                gcPauseMilliseconds = (GC.GetTotalPauseDuration() - PauseStart).TotalMilliseconds,
                scope = "dedicated server run after BeginRun; operation scopes overlap; eligibility is Judge result, settlement is terminal Answer result; no latency measurement"
            };
            // CreateNew preserves every previous run, including failed/partial evidence.
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(Output!, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.Write(payload); stream.Flush(flushToDisk: true);
            }
            Written = true;
        }
        catch (IOException ex) { Console.Error.WriteLine("[serverperf] evidence write failed: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine("[serverperf] evidence write failed: " + ex.Message); }
    }
}
