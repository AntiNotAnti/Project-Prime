using System;
using System.Collections.Generic;
namespace MphRead.Mods.MatchEvents;
/// <summary>Scene-owner-only, bounded passive history. No gameplay or presentation callbacks.</summary>
internal sealed class MatchSemanticEventBus
{
    internal const int Capacity = 1024;
    private readonly Queue<MatchSemanticEvent> _events = new();
    private readonly MatchSemanticActor[] _deaths = new MatchSemanticActor[8];
    private uint _lastId;
    private uint _lastTick;
    internal ushort MatchId { get; private set; }
    internal ulong AuthorityEpoch { get; private set; }
    internal IReadOnlyCollection<MatchSemanticEvent> Events => _events;
    internal MatchSemanticEvent LastEvent { get; private set; }
    internal MatchAwardEngine Awards { get; } = new();
    internal void Begin(ushort match, ulong epoch)
    {
        if (match == 0 || epoch == 0) throw new ArgumentOutOfRangeException(nameof(match));
        if (match == MatchId && epoch == AuthorityEpoch) return;
        MatchId = match; AuthorityEpoch = epoch; _lastId = 0; _lastTick = 0;
        _events.Clear(); LastEvent = default; Array.Clear(_deaths); Awards.Reset();
    }
    // Refuse exhaustion: caller must explicitly begin a new authority epoch.
    internal bool TryEmit(uint tick, MatchSemanticEventType type, MatchSemanticActor actor,
        MatchSemanticActor target, int weapon = -1, MatchSemanticEventFlags flags = 0, int entity = -1, int value = 0)
        => _lastId != uint.MaxValue && Accept(new(_lastId + 1, MatchId, AuthorityEpoch,
            tick, type, actor, target, weapon, flags, entity, value));
    // Stage A is an ordered local stream. High-water dedup survives history eviction.
    // Do NOT reuse Accept for reordered wire delivery; a future receiver needs its own bounded reorder window.
    internal bool Accept(in MatchSemanticEvent fact)
    {
        if (MatchId == 0 || fact.MatchId != MatchId || fact.AuthorityEpoch != AuthorityEpoch
            || fact.EventId == 0 || fact.EventId <= _lastId || fact.Tick < _lastTick
            || !Enum.IsDefined(fact.Type) || (fact.Flags & ~(MatchSemanticEventFlags)127) != 0
            || !ValidActor(fact.Actor) || !ValidActor(fact.Target) || !ValidIdentity(fact)) return false;
        if (fact.Type is MatchSemanticEventType.PlayerKilled or MatchSemanticEventType.PlayerSuicide)
        {
            var old = _deaths[fact.Target.Slot];
            if (old.IsPlayer && (fact.Target.SlotGeneration < old.SlotGeneration
                || fact.Target.SlotGeneration == old.SlotGeneration && fact.Target.Life <= old.Life)) return false;
            _deaths[fact.Target.Slot] = fact.Target;
        }
        _lastId = fact.EventId; _lastTick = fact.Tick;
        if (_events.Count == Capacity) _events.Dequeue();
        _events.Enqueue(fact); LastEvent = fact; Awards.Observe(fact); return true;
    }
    private static bool ValidIdentity(in MatchSemanticEvent fact) => fact.Type switch
    {
        MatchSemanticEventType.PlayerSpawned or MatchSemanticEventType.WeaponFired
            or MatchSemanticEventType.ObjectivePickedUp or MatchSemanticEventType.ObjectiveDropped
            or MatchSemanticEventType.ObjectiveCaptured or MatchSemanticEventType.ObjectiveDefended
            or MatchSemanticEventType.NodeCaptured => fact.Actor.IsPlayer && fact.Target == MatchSemanticActor.None,
        MatchSemanticEventType.PlayerKilled => fact.Target.IsPlayer && fact.Actor != fact.Target,
        MatchSemanticEventType.PlayerSuicide => fact.Actor.IsPlayer && fact.Actor == fact.Target,
        MatchSemanticEventType.Headshot or MatchSemanticEventType.PlayerAssisted => fact.Actor.IsPlayer && fact.Target.IsPlayer,
        _ => true
    };
    private static bool ValidActor(MatchSemanticActor actor) => actor.IsPlayer || actor == MatchSemanticActor.None;
}
