using System;
using System.Collections.Immutable;
using MphRead.Mods.Network;

namespace MphRead;

/// <summary>
/// Bounded, scene-presentation-owned copies of facts useful to an observer.
/// The journal is deliberately downstream of delivery: it does not query a
/// client, a replay reader, or any process-wide session singleton.
/// </summary>
public sealed class BroadcastObservationJournal
{
    public const int Capacity = 64;

    private readonly WorldEvent[] _world = new WorldEvent[Capacity];
    private readonly CombatEvent[] _combat = new CombatEvent[Capacity];
    private readonly MatchAward[] _awards = new MatchAward[Capacity];
    private readonly MatchEvent[] _semantics = new MatchEvent[Capacity];
    private int _worldHead, _worldCount, _combatHead, _combatCount;
    private int _awardHead, _awardCount;
    private int _semanticHead, _semanticCount;
    private uint _matchId, _phaseRevision;

    public void Reset(uint matchId, uint phaseRevision)
    {
        _matchId = matchId;
        _phaseRevision = phaseRevision;
        _worldHead = _worldCount = 0;
        _combatHead = _combatCount = 0;
        _awardHead = _awardCount = 0;
        _semanticHead = _semanticCount = 0;
        Array.Clear(_world);
        Array.Clear(_combat);
        Array.Clear(_awards);
        Array.Clear(_semantics);
    }

    public bool Record(in WorldEvent value)
    {
        if (!Accept(value.MatchId, value.PhaseRevision, value.IsValid)) return false;
        return Append(_world, ref _worldHead, ref _worldCount, value, value.Id,
            static item => item.Id);
    }

    public bool Record(in CombatEvent value)
    {
        if (!value.IsValid) return false;
        return Append(_combat, ref _combatHead, ref _combatCount, value, value.Id,
            static item => item.Id);
    }

    public bool Record(in MatchAward value)
    {
        if (!Accept(value.MatchId, value.PhaseRevision, value.IsValid)) return false;
        return Append(_awards, ref _awardHead, ref _awardCount, value, value.AwardId,
            static item => item.AwardId);
    }

    public bool Record(in MatchEvent value)
    {
        if (!Accept(value.MatchId, value.PhaseRevision, value.IsValid)) return false;
        return Append(_semantics, ref _semanticHead, ref _semanticCount, value, value.Id,
            static item => item.Id);
    }

    internal BroadcastObservationFacts Snapshot(uint matchId, uint phaseRevision,
        uint deliveredTick)
    {
        if (_matchId != matchId || _phaseRevision != phaseRevision)
            return BroadcastObservationFacts.Empty;
        return new BroadcastObservationFacts(
            Copy(_combat, _combatHead, _combatCount, deliveredTick, static item => item.Tick),
            Copy(_world, _worldHead, _worldCount, deliveredTick, static item => item.Tick),
            Copy(_awards, _awardHead, _awardCount, deliveredTick, static item => item.Tick),
            Copy(_semantics, _semanticHead, _semanticCount, deliveredTick, static item => item.Tick));
    }

    private bool Accept(uint matchId, uint phaseRevision, bool valid)
    {
        if (!valid) return false;
        if (_matchId != matchId || _phaseRevision != phaseRevision)
            Reset(matchId, phaseRevision);
        return true;
    }

    private static bool Append<T>(T[] values, ref int head, ref int count,
        in T value, uint id, Func<T, uint> getId) where T : struct
    {
        for (int index = 0; index < count; index++)
        {
            int physical = (head - count + index + values.Length) % values.Length;
            if (getId(values[physical]) == id) return false;
        }
        values[head] = value;
        head = (head + 1) % values.Length;
        if (count < values.Length) count++;
        return true;
    }

    private static ImmutableArray<T> Copy<T>(T[] values, int head, int count,
        uint deliveredTick, Func<T, uint> getTick) where T : struct
    {
        var result = ImmutableArray.CreateBuilder<T>(count);
        for (int index = 0; index < count; index++)
        {
            T value = values[(head - count + index + values.Length) % values.Length];
            if (!Sequence32.IsNewer(getTick(value), deliveredTick)) result.Add(value);
        }
        return result.MoveToImmutable();
    }
}

public readonly record struct BroadcastObservationFacts(
    ImmutableArray<CombatEvent> CombatEvents,
    ImmutableArray<WorldEvent> WorldEvents,
    ImmutableArray<MatchAward> Awards,
    ImmutableArray<MatchEvent> SemanticEvents)
{
    public static BroadcastObservationFacts Empty { get; } = new([], [], [], []);
}
