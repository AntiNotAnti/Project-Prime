using System;
using System.Reflection;
using System.Threading;
namespace MphRead.Mods.Network.Telemetry;

public static class ProductionTelemetry
{
    private static NetTelemetryWriter? _writer;
    private static readonly ServerStepAccumulator _steps = new();
    private static readonly NetTelemetryEvent[] _continuous = new NetTelemetryEvent[8];
    private static readonly int[] _continuousCounts = new int[8];
    private static void FlushContinuous(int slot)
    {
        int count = _continuousCounts[slot];
        if (count > 0) EmitDirect(_continuous[slot] with { Samples = count });
        _continuousCounts[slot] = 0;
    }
    public static void RecordServerStep(uint frame, double milliseconds, long allocations, long dropped, long stalls, bool failed)
    {
        if (!Enabled) return;
        if (_steps.Add(frame, milliseconds, allocations, dropped, stalls) || failed) FlushServerSteps();
    }
    public static void FlushServerSteps()
    {
        Span<NetTelemetryEvent> events = stackalloc NetTelemetryEvent[5];
        int count = _steps.Flush(events);
        for (int i = 0; i < count; i++) Emit(events[i]);
    }
    private static readonly NetTelemetryWriter?[] _retired = new NetTelemetryWriter?[2];
    private static NetTelemetryConfig _config = new() { Enabled = false };
    public static void Configure(NetTelemetryConfig config) => _config = config;
    public static bool Enabled => _writer != null;
    public static TelemetryCounters Counters => _writer?.Counters ?? default;
    public static void Begin(string map, string mode, int players)
    {
        End();
        if (!_config.Enabled || _config.Detail == TelemetryDetail.Off) return;
        int retiring = 0;
        foreach (var prior in _retired) if (prior is { Finished: false }) retiring++;
        // Permit one previous match to drain/upload while the new match records.
        // Two stalled writers are the hard cap; gameplay never waits for either.
        if (retiring >= 2) return;
        try
        {
            var assembly = typeof(ProductionTelemetry).Assembly;
            string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            int separator = version.IndexOf('+');
            var header = new TelemetryHeader(5, NetConfig.ProtocolVersion, Guid.NewGuid().ToString("N"),
                separator < 0 ? "unknown" : version[(separator + 1)..], version,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription, mode, map, players);
            _writer = new NetTelemetryWriter(_config, header);
        }
        catch (Exception) { _writer = null; }
    }
    public static void Emit(in NetTelemetryEvent e)
    {
        if (!Enabled) return;
        if (e.Type == TelemetryEventType.ContinuousTarget && _config.Detail < TelemetryDetail.Study && e.Player < 8)
        {
            int slot = e.Player;
            ref var previous = ref _continuous[slot];
            bool same = previous.Type == e.Type && previous.Victim == e.Victim && previous.Generation == e.Generation
                && previous.Life == e.Life && previous.Result == e.Result && previous.Flags == e.Flags
                && previous.Weapon == e.Weapon && previous.A == e.A && previous.B == e.B
                && previous.F == e.F && previous.G == e.G;
            if (!same) { FlushContinuous(slot); EmitDirect(e); }
            else if (++_continuousCounts[slot] >= 60) FlushContinuous(slot);
            previous = e;
            return;
        }
        EmitDirect(e);
    }
    private static void EmitDirect(in NetTelemetryEvent e)
    {
        var writer = Volatile.Read(ref _writer);
        if (writer != null) writer.Emit(e with { TimestampMilliseconds = System.Diagnostics.Stopwatch.GetTimestamp() * (1000.0 / System.Diagnostics.Stopwatch.Frequency) });
    }
    public static void End()
    {
        FlushServerSteps();
        for (int i = 0; i < _continuous.Length; i++) FlushContinuous(i);
        Array.Clear(_continuous);
        var writer = Interlocked.Exchange(ref _writer, null);
        if (writer != null)
        {
            writer.Stop();
            for (int i = 0; i < _retired.Length; i++)
                if (_retired[i] == null || _retired[i]!.Finished) { _retired[i] = writer; break; }
        }
    }
    public static void Shutdown()
    {
        End(); long started = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var writer in _retired)
        {
            int remaining = Math.Max(0, 500 - (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (remaining > 0) writer?.WaitForExit(remaining);
        }
    }
}
