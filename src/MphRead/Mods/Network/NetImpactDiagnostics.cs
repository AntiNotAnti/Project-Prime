using System;
using System.Diagnostics;
using System.IO;
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
    Synthesized, AlreadyVisible, Invalid, Stale, Duplicate, Ambiguous, Expired, KillConfirmed, KillRejected }

internal readonly record struct ImpactDiagnostic(CombatImpactIdentity Identity, ImpactStage Stage,
    byte Weapon, uint LocalFrame, long Timestamp);

/// <summary>Scene-owner, fixed-size, opt-in cross-view evidence. No I/O or allocations on Record.</summary>
internal static class NetImpactDiagnostics
{
    internal const int Capacity = 4096;
    private static readonly ImpactDiagnostic[] Events = new ImpactDiagnostic[Capacity];
    private static readonly long[,] Counts = new long[9, Enum.GetValues<ImpactStage>().Length];
    private static int _cursor, _count;
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
        timestampFrequency = Stopwatch.Frequency, overwritten = Overwritten, events = Snapshot()
    }, new JsonSerializerOptions { WriteIndented = true }));
    internal static void Reset()
    {
        Array.Clear(Events); Array.Clear(Counts); _cursor = _count = 0; Overwritten = 0;
    }
}
