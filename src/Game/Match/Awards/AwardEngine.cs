using System;
using MphRead.Mods.Network;

namespace MphRead;

/// <summary>
/// Consumes authoritative semantic facts on the owning match thread and emits
/// presentation-only awards. It does not touch score, RP, rating, or health.
/// </summary>
public sealed class AwardEngine : IMatchEventSink
{
    private readonly CombatActor[] _actors = new CombatActor[AwardPolicy.MaximumActors];
    private readonly uint[] _lastKillTicks = new uint[AwardPolicy.MaximumActors];
    private readonly byte[] _killStreaks = new byte[AwardPolicy.MaximumActors];
    private readonly bool[] _knownActors = new bool[AwardPolicy.MaximumActors];
    private readonly uint[] _seenEvents = new uint[AwardPolicy.MaximumSeenEvents];
    private int _seenCount, _seenHead;
    private uint _nextAwardId = 1;
    private bool _firstHuntAwarded;
    private bool _acceptAwards = true;

    public event Action<MatchAward>? Awarded;
    public long EventsConsumed { get; private set; }
    public long DuplicateEvents { get; private set; }
    public long AwardsPublished { get; private set; }
    public long AwardsDropped { get; private set; }

    public void OnMatchEvent(in MatchEvent value)
    {
        if (!value.IsValid || value.Id == 0) throw new ArgumentException("Invalid match event.", nameof(value));
        if (!Remember(value.Id))
        {
            if (DuplicateEvents < long.MaxValue) DuplicateEvents++;
            return;
        }
        if (EventsConsumed < long.MaxValue) EventsConsumed++;

        switch (value.Kind)
        {
            case MatchEventKind.MatchStarted:
            case MatchEventKind.CountdownStarted:
                ResetRound();
                _acceptAwards = true;
                return;
            case MatchEventKind.MatchEnded:
                _acceptAwards = false;
                return;
            case MatchEventKind.PlayerSpawned:
                ResetActor(value.Subject);
                return;
            case MatchEventKind.PlayerKilled:
                HandleKill(value);
                return;
            case MatchEventKind.PlayerAssisted:
                if (_acceptAwards && value.Subject.IsValid && (value.Flags & MatchEventFlags.TeamKill) == 0)
                    Publish(value, MatchAwardKind.Assist, value.Target);
                return;
            case MatchEventKind.ObjectiveCaptured:
            case MatchEventKind.NodeCaptured:
                if (_acceptAwards) Publish(value, MatchAwardKind.Capture);
                return;
            case MatchEventKind.ObjectiveDefended:
                if (_acceptAwards) Publish(value, MatchAwardKind.Defender, value.Target);
                return;
        }
    }

    public void Reset()
    {
        Array.Clear(_actors);
        Array.Clear(_lastKillTicks);
        Array.Clear(_killStreaks);
        Array.Clear(_knownActors);
        Array.Clear(_seenEvents);
        _seenCount = _seenHead = 0;
        // Reset consumer state at a round boundary, but keep award identities
        // unique for the lifetime of this match. New MatchRuntime instances
        // start at one; delayed reliable/replay facts from an earlier round
        // must never collide with a later award in the same match.
        _firstHuntAwarded = false;
        _acceptAwards = true;
        EventsConsumed = DuplicateEvents = AwardsPublished = AwardsDropped = 0;
    }

    private void HandleKill(in MatchEvent value)
    {
        ResetActor(value.Target);
        if (!_acceptAwards) return;

        if ((value.Flags & MatchEventFlags.Suicide) != 0)
        {
            ResetActor(value.Subject);
            return;
        }
        if ((value.Flags & (MatchEventFlags.TeamKill | MatchEventFlags.EnvironmentKill)) != 0
            || !value.Subject.IsValid)
        {
            ResetActor(value.Subject);
            return;
        }

        if ((value.Flags & MatchEventFlags.ObjectiveCarrier) != 0)
            Publish(value, MatchAwardKind.Interceptor, value.Target);
        // Defender is emitted from the distinct authoritative
        // ObjectiveDefended fact. Keeping the source event separate prevents
        // replay/telemetry consumers from having to reinterpret a kill flag.
        if ((value.Flags & MatchEventFlags.PrimeTarget) != 0)
            Publish(value, MatchAwardKind.PrimeSlayer, value.Target);

        int index = EnsureActor(value.Subject);
        bool inWindow = _killStreaks[index] != 0 && AwardPolicy.WithinKillWindow(value.Tick, _lastKillTicks[index]);
        _killStreaks[index] = inWindow && _killStreaks[index] < byte.MaxValue
            ? (byte)(_killStreaks[index] + 1) : (byte)1;
        _lastKillTicks[index] = value.Tick;
        if (!_firstHuntAwarded)
        {
            _firstHuntAwarded = true;
            Publish(value, MatchAwardKind.FirstHunt, value.Target);
        }
        if (_killStreaks[index] == 2) Publish(value, MatchAwardKind.DoubleKill, value.Target, 2);
        else if (_killStreaks[index] == 3) Publish(value, MatchAwardKind.TripleKill, value.Target, 3);
    }

    private void Publish(in MatchEvent source, MatchAwardKind kind, CombatActor target = default, byte count = 1)
    {
        // The record's CLR default is an invalid zero actor, while the domain
        // uses the explicit None sentinel for an absent target.
        if (!target.IsValid) target = CombatActor.None;
        uint id = _nextAwardId++;
        if (_nextAwardId == 0) _nextAwardId = 1;
        var award = new MatchAward(id, source.Id, source.MatchId, source.PhaseRevision,
            source.Tick, kind, source.Subject, target, count);
        if (!award.IsValid) throw new InvalidOperationException("Award engine produced an invalid award.");
        if (Awarded == null)
        {
            if (AwardsDropped < long.MaxValue) AwardsDropped++;
            return;
        }
        if (AwardsPublished < long.MaxValue) AwardsPublished++;
        Awarded(award);
    }

    private int EnsureActor(CombatActor actor)
    {
        if (!actor.IsValid || actor.Slot >= AwardPolicy.MaximumActors) return -1;
        int index = actor.Slot;
        if (!_knownActors[index] || _actors[index] != actor)
        {
            _actors[index] = actor;
            _knownActors[index] = true;
            _lastKillTicks[index] = 0;
            _killStreaks[index] = 0;
        }
        return index;
    }

    private void ResetActor(CombatActor actor)
    {
        if (!actor.IsValid || actor.Slot >= AwardPolicy.MaximumActors) return;
        int index = EnsureActor(actor);
        _lastKillTicks[index] = 0;
        _killStreaks[index] = 0;
    }

    private void ResetRound()
    {
        Array.Clear(_actors);
        Array.Clear(_lastKillTicks);
        Array.Clear(_killStreaks);
        Array.Clear(_knownActors);
        _firstHuntAwarded = false;
    }

    private bool Remember(uint id)
    {
        for (int i = 0; i < _seenCount; i++)
            if (_seenEvents[(_seenHead - 1 - i + _seenEvents.Length) % _seenEvents.Length] == id)
                return false;
        _seenEvents[_seenHead] = id;
        _seenHead = (_seenHead + 1) % _seenEvents.Length;
        if (_seenCount < _seenEvents.Length) _seenCount++;
        return true;
    }
}
