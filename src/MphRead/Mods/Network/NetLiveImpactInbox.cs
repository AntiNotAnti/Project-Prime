using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

internal sealed class ImpactIdentityWindow
{
    private readonly CombatImpactIdentity[] _keys = new CombatImpactIdentity[512];
    private int _count, _cursor;
    private readonly HashSet<CombatImpactIdentity> _seen = new(512);
    internal bool Add(in CombatImpactIdentity key)
    {
        if (_seen.Contains(key)) return false;
        if (_count == _keys.Length) _seen.Remove(_keys[_cursor]);
        _seen.Add(key);
        _keys[_cursor] = key; _cursor = (_cursor + 1) % _keys.Length;
        _count = Math.Min(_count + 1, _keys.Length); return true;
    }
    internal void Reset() { Array.Clear(_keys); _seen.Clear(); _count = _cursor = 0; }
}

internal static class NetLiveImpactInbox
{
    internal const int Capacity = 128, MaxAgeFrames = 90;
    private static readonly LiveCombatImpact[] Pending = new LiveCombatImpact[Capacity];
    private static readonly uint[] Arrived = new uint[Capacity];
    private static readonly ImpactIdentityWindow Seen = new();
    private static int _head;
    internal static int Count { get; private set; }
    internal static bool Current(in LiveCombatImpact impact) => impact.Matches(NetSession.CurrentMatchId,
        NetSession.AuthorityEpoch, NetPlayerLifecycle.Generation(impact.Fact.ShooterSlot),
        NetPlayerLifecycle.Get(impact.Fact.ShooterSlot), NetPlayerLifecycle.Generation(impact.Fact.VictimSlot),
        NetPlayerLifecycle.Get(impact.Fact.VictimSlot));
    internal static bool Accept(ReadOnlySpan<byte> bytes)
    {
        if (!NetCombatFactPublisher.LiveEnabled || NetSession.Role != NetRole.Client || DemoPlayback.IsActive
            || !LiveCombatImpactPacket.TryRead(bytes, out var impact)) return false;
        int age = unchecked((int)(NetSession.LastSnapshotFrame - impact.Fact.ResolveTick));
        // A new hit can precede its snapshot. Permit a bounded look-ahead, never replay backlog.
        if (!Current(impact) || age > MaxAgeFrames || age < -MaxAgeFrames)
        { NetImpactDiagnostics.Record(impact.Fact, ImpactStage.Stale); return false; }
        if (!Seen.Add(impact.Identity)) { NetImpactDiagnostics.Record(impact.Fact, ImpactStage.Duplicate); return false; }
        if (Count == Capacity) { NetImpactDiagnostics.Record(impact.Fact, ImpactStage.Expired); return false; }
        int at = (_head + Count) % Capacity; Pending[at] = impact; Arrived[at] = NetSession.NetFrame; Count++;
        NetImpactDiagnostics.Record(impact.Fact, ImpactStage.LiveIngress, impact.Presentation.Component);
        NetPredictedKillPresentation.Fact(impact.Fact);
        return true;
    }
    internal static bool TryTake(out LiveCombatImpact impact, out uint arrived)
    {
        impact = default; arrived = 0;
        if (Count == 0) return false;
        impact = Pending[_head]; arrived = Arrived[_head]; Pending[_head] = default;
        _head = (_head + 1) % Capacity; Count--; return true;
    }
    internal static void Reset() { Array.Clear(Pending); Array.Clear(Arrived); Seen.Reset(); _head = Count = 0; }
}
