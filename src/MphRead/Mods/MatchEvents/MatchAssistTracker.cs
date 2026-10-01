using System;
namespace MphRead.Mods.MatchEvents;
/// <summary>Authority-owned damage contributions: at most 8 victims × 8 contributors.</summary>
internal sealed class MatchAssistTracker
{
    internal const uint WindowTicks = 300;
    private readonly MatchSemanticActor[] _victims = new MatchSemanticActor[8];
    private readonly MatchSemanticActor[,] _actors = new MatchSemanticActor[8, 8];
    private readonly uint[,] _ticks = new uint[8, 8];
    internal void Reset() { Array.Clear(_victims); Array.Clear(_actors); Array.Clear(_ticks); }
    internal void Spawn(MatchSemanticActor victim)
    {
        if (!victim.IsPlayer) return;
        _victims[victim.Slot] = victim;
        for (int i = 0; i < 8; i++) _actors[victim.Slot, i] = default;
    }
    internal void Damage(MatchSemanticActor actor, MatchSemanticActor victim, uint tick, uint damage, bool opposing)
    {
        if (!opposing || damage == 0 || !actor.IsPlayer || !victim.IsPlayer || SameOccupant(actor, victim)) return;
        if (_victims[victim.Slot] != victim) Spawn(victim);
        _actors[victim.Slot, actor.Slot] = actor;
        _ticks[victim.Slot, actor.Slot] = tick;
    }
    internal int Collect(MatchSemanticActor victim, MatchSemanticActor killer, uint tick, Span<MatchSemanticActor> result)
    {
        if (!victim.IsPlayer || _victims[victim.Slot] != victim) return 0;
        if (result.Length < 8) throw new ArgumentException("Eight contributor slots required.", nameof(result));
        int count = 0;
        for (int slot = 0; slot < 8; slot++)
        {
            var actor = _actors[victim.Slot, slot];
            uint hit = _ticks[victim.Slot, slot];
            if (actor.IsPlayer && !SameOccupant(actor, killer) && tick >= hit && tick - hit <= WindowTicks)
                result[count++] = actor;
            _actors[victim.Slot, slot] = default;
        }
        return count;
    }
    private static bool SameOccupant(MatchSemanticActor left, MatchSemanticActor right)
        => left.Slot == right.Slot && left.SlotGeneration == right.SlotGeneration;
}
