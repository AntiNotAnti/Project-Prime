using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Network;

// Resolution tick disambiguates DamageEventId wrap. Component is zero when
// the native source cannot prove a pellet/child identity; zero is never guessed.
internal readonly record struct CombatImpactIdentity(ShotKey Shot, byte Victim,
    ushort VictimGeneration, ushort VictimLife, ushort DamageEvent, uint ResolveTick, uint Component)
{
    internal static CombatImpactIdentity From(in ReplayShotFact fact, uint component = 0) => new(
        new(fact.AuthorityEpoch, fact.MatchId, fact.ShooterSlot, fact.ShooterGeneration,
            fact.ShooterLifeId, fact.ShotId), fact.VictimSlot, fact.VictimGeneration,
        fact.VictimLifeId, fact.DamageEventId, fact.ResolveTick, component);
}

internal enum ImpactStage : byte { LocalHit, Authority, ReplayIngress, LiveIngress, Matched,
    Synthesized, AlreadyVisible, Invalid, Stale, Duplicate, Ambiguous, Expired, KillConfirmed, KillRejected, DrawSubmitted }

internal readonly record struct ImpactDiagnostic(CombatImpactIdentity Identity, ImpactStage Stage,
    byte Weapon, uint LocalFrame, long Timestamp);

/// <summary>Scene-owner, fixed-size, opt-in cross-view evidence. No I/O or allocations on Record.</summary>
internal static class NetImpactDiagnostics
{
    internal const int Capacity = 4096;
    private static readonly ImpactDiagnostic[] Events = new ImpactDiagnostic[Capacity];
    private static readonly long[,] Counts = new long[9, Enum.GetValues<ImpactStage>().Length];
    private static int _cursor, _count;
    internal static long DrawInvariantChecks, DrawInvariantFailures;
    private static readonly long[,] Emissions = new long[9,2];
    internal static void NativeEmission(byte weapon, bool charged)
    { if (Enabled && weapon < 9) Emissions[weapon,charged?1:0]++; }
    internal static bool Enabled { get; set; }
    internal static long Overwritten { get; private set; }
    internal static int Count => _count;
    internal static void Record(in ReplayShotFact fact, ImpactStage stage, uint component = 0)
    {
        if (!Enabled) return;
        Record(CombatImpactIdentity.From(fact, component), stage, fact.Weapon, NetSession.NetFrame);
    }
    internal static void Record(in CombatImpactIdentity key, ImpactStage stage, byte weapon, uint frame)
    {
        if (!Enabled || weapon >= 9 || (uint)stage >= Counts.GetLength(1)) return;
        Counts[weapon, (int)stage]++;
        Events[_cursor] = new(key, stage, weapon, frame, Stopwatch.GetTimestamp());
        _cursor = (_cursor + 1) % Capacity;
        if (_count < Capacity) _count++; else Overwritten++;
    }
    internal static ImpactDiagnostic[] Snapshot()
    {
        var result = new ImpactDiagnostic[_count];
        for (int i = 0; i < result.Length; i++) result[i] = Events[(_cursor - _count + Capacity + i) % Capacity];
        return result;
    }
    // Explicit diagnostic/export caller only; never called by simulation or packet handlers.
    internal static void Export(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new
    {
        schema = 1, clock = "process-monotonic; compare frames across processes, not timestamps",
        timestampFrequency = Stopwatch.Frequency, overwritten = Overwritten, events = Snapshot(),
        counts = Enumerable.Range(0, 9).Select(weapon => new { weapon,
            stages = Enum.GetValues<ImpactStage>().Select(stage => new { stage = stage.ToString(), count = Counts[weapon, (int)stage] }).ToArray() }).ToArray(),
        drawInvariants = new { checks=DrawInvariantChecks, failures=DrawInvariantFailures },
        nativeEmissions = Enumerable.Range(0,9).Select(weapon => new { weapon, uncharged=Emissions[weapon,0], charged=Emissions[weapon,1] }).ToArray(),
        claimShadow = NetClaimEarlySettlement.Snapshot(),
        predictedKills = new { NetPredictedKillPresentation.Started, NetPredictedKillPresentation.Confirmed,
            NetPredictedKillPresentation.Rejected, NetPredictedKillPresentation.Expired },
        profile = NetCombatProfile.Enabled ? NetCombatProfile.Capture() : null
    }, new JsonSerializerOptions { WriteIndented = true }));
    internal static void ExportRequested()
    {
        if (!Enabled || Environment.GetEnvironmentVariable("PRIME_IMPACT_LOG") is not string path) return;
        if (_count == 0 && File.Exists(path)) return; // Preserve the just-finished match during shutdown reset.
        try { Export(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Console.WriteLine($"[impact] diagnostic export failed: {ex.Message}"); }
    }
    // Explicit match teardown only. Reuse existing bounded sim/loop histograms;
    // this adds no per-frame timing or allocation to the normal server path.
    internal static void ExportServerRequested(ServerSim? sim, ServerLoopDiagnostics loop)
    {
        if (!Enabled || sim == null || sim.Frames == 0
            || Environment.GetEnvironmentVariable("PRIME_IMPACT_LOG") is not string path) return;
        try
        {
            File.WriteAllText(path + ".server.json", JsonSerializer.Serialize(new {
                schema = 1, scope = "whole match including startup; loop excludes deliberate pacing wait",
                frames = sim.Frames, meanMs = sim.StepSeconds * 1000 / sim.Frames,
                p50Ms = sim.StepPercentile(.5), p95Ms = sim.StepPercentile(.95),
                p99Ms = sim.StepPercentile(.99), p999Ms = sim.StepPercentile(.999),
                worstMs = sim.WorstStepSeconds * 1000,
                allocatedBytes = sim.StepAllocatedBytes, allocatedBytesPerFrame = (double)sim.StepAllocatedBytes / sim.Frames,
                sim.StepFailures, sim.DroppedSteps, sim.Stalls, sim.OverrunSteps,
                loop = loop.Capture(includeHistogram: false)
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Console.WriteLine($"[impact] server metric export failed: {ex.Message}"); }
    }
    internal static void Reset()
    {
        Array.Clear(Events); Array.Clear(Counts); Array.Clear(Emissions); DrawInvariantChecks=DrawInvariantFailures=0; _cursor = _count = 0; Overwritten = 0;
    }
}
