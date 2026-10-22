using System.Collections.Generic;
using System.Diagnostics;
namespace MphRead.Mods.MatchEvents;
/// <summary>Independent legacy replay-marker parity. Snapshot markers may legitimately coalesce facts;
/// mismatches are evidence to inspect before migration, never permission to change gameplay.</summary>
internal sealed class MatchSemanticParity
{
    private readonly List<MatchSemanticEvent> _pending = new();
    internal long Matched { get; private set; }
    internal long MissingSemantic { get; private set; }
    internal long MissingLegacy { get; private set; }
    internal int Pending => _pending.Count;
    internal void Reset() { _pending.Clear(); Matched = MissingSemantic = MissingLegacy = 0; }
    internal void Semantic(in MatchSemanticEvent fact)
    {
        if (fact.Type is not (MatchSemanticEventType.Headshot or MatchSemanticEventType.PlayerKilled)
            || !fact.Actor.IsPlayer || fact.Actor == fact.Target) return;
        if (_pending.Count == MatchSemanticEventBus.Capacity) { _pending.RemoveAt(0); MissingLegacy++; }
        _pending.Add(fact);
    }
    internal void Legacy(uint tick, MatchSemanticEventType type, MatchSemanticActor actor,
        MatchSemanticActor target, int weapon, bool headshot)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            var fact = _pending[i];
            if (fact.Type == type && fact.Actor == actor && fact.Target == target && fact.Weapon == weapon
                && fact.Tick <= tick && tick - fact.Tick <= 120
                && ((fact.Flags & MatchSemanticEventFlags.Headshot) != 0) == headshot)
            { _pending.RemoveAt(i); Matched++; return; }
        }
        MissingSemantic++;
        Debug.WriteLine($"[semantic-parity] No semantic {type} for legacy marker tick={tick} actor={actor} target={target}");
    }
    internal void Advance(uint tick)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
            if (tick >= _pending[i].Tick && tick - _pending[i].Tick > 120)
            {
                Debug.WriteLine($"[semantic-parity] No legacy marker for semantic {_pending[i].Type} id={_pending[i].EventId}");
                _pending.RemoveAt(i); MissingLegacy++;
            }
    }
}
