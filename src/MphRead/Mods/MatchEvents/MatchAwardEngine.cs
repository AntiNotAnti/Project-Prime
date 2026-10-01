using System;
using System.Collections.Generic;
namespace MphRead.Mods.MatchEvents;
internal enum MatchAwardKind
{
    FirstBlood, DoubleKill, TripleKill, Overkill, Killtacular, Killtrocity, Kilimanjaro,
    Killtastrophe, Killpocalypse, Killionaire, KillingSpree, KillingFrenzy, RunningRiot,
    Rampage, Untouchable, Invincible, Assist, Capture, Defender, Interceptor, PrimeSlayer
}
internal readonly record struct MatchAward(uint EventId, uint Tick, MatchSemanticActor Actor, MatchAwardKind Kind);
internal sealed class MatchAwardEngine
{
    internal const uint ChainWindowTicks = 240;
    private readonly Dictionary<MatchSemanticActor, (uint Tick, int Chain, int Spree)> _players = new();
    private readonly Queue<MatchAward> _awards = new();
    private bool _firstBlood;
    internal IReadOnlyCollection<MatchAward> Awards => _awards;
    internal void Reset() { _players.Clear(); _awards.Clear(); _firstBlood = false; }
    internal void Observe(in MatchSemanticEvent fact)
    {
        if (fact.Type is MatchSemanticEventType.PlayerKilled or MatchSemanticEventType.PlayerSuicide)
            _players.Remove(fact.Target);
        if (fact.Type == MatchSemanticEventType.PlayerSpawned)
        {
            // Bound lifetime state and discard stale slot occupants and prior lives.
            foreach (var actor in new List<MatchSemanticActor>(_players.Keys))
                if (actor.Slot == fact.Actor.Slot) _players.Remove(actor);
        }
        if (!fact.Actor.IsPlayer) return;
        if (fact.Type == MatchSemanticEventType.PlayerAssisted) Add(fact, MatchAwardKind.Assist);
        if (fact.Type == MatchSemanticEventType.ObjectiveCaptured) Add(fact, MatchAwardKind.Capture);
        if (fact.Type == MatchSemanticEventType.ObjectiveDefended) Add(fact, MatchAwardKind.Defender);
        if (fact.Type != MatchSemanticEventType.PlayerKilled || fact.Actor == fact.Target
            || (fact.Flags & (MatchSemanticEventFlags.FriendlyFire | MatchSemanticEventFlags.Environment)) != 0) return;
        if (!_firstBlood) { _firstBlood = true; Add(fact, MatchAwardKind.FirstBlood); }
        _players.TryGetValue(fact.Actor, out var previous);
        int chain = previous.Chain > 0 && fact.Tick >= previous.Tick && fact.Tick - previous.Tick <= ChainWindowTicks
            ? Math.Min(previous.Chain + 1, 10) : 1;
        int spree = Math.Min(previous.Spree + 1, 31);
        foreach (var actor in new List<MatchSemanticActor>(_players.Keys))
            if (actor.Slot == fact.Actor.Slot && actor != fact.Actor) _players.Remove(actor);
        _players[fact.Actor] = (fact.Tick, chain, spree);
        if (chain >= 2) Add(fact, (MatchAwardKind)((int)MatchAwardKind.DoubleKill + Math.Min(chain, 10) - 2));
        if (spree <= 30 && spree % 5 == 0) Add(fact, (MatchAwardKind)((int)MatchAwardKind.KillingSpree + spree / 5 - 1));
        if ((fact.Flags & MatchSemanticEventFlags.Prime) != 0) Add(fact, MatchAwardKind.PrimeSlayer);
        if ((fact.Flags & MatchSemanticEventFlags.Carrier) != 0) Add(fact, MatchAwardKind.Interceptor);
    }
    private void Add(in MatchSemanticEvent fact, MatchAwardKind kind)
    {
        if (_awards.Count == MatchSemanticEventBus.Capacity) _awards.Dequeue();
        _awards.Enqueue(new(fact.EventId, fact.Tick, fact.Actor, kind));
    }
}
