using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
internal static class KandenEnhancement
{
    internal static void Hit(PlayerEntity owner, PlayerEntity target, bool charged)
    {
        var s = owner.EnhancedState;
        if (charged)
        {
            s.ClearTarget(); EnhancedHunters.Mark(owner, target, EnhancedHunterTuning.RodFrames);
            EnhancedHunterTelemetry.Event(owner.Hunter, "lightning-rods");
        }
        else if (EnhancedHunters.HasMark(owner, target) && ++s.ValueA >= 3) Overload(owner, target);
    }
    internal static void Overload(PlayerEntity owner, PlayerEntity target)
    {
        owner.EnhancedState.ClearTarget();
        EnhancedHunterTelemetry.Event(owner.Hunter, "overloads");
        EnhancedHunters.Bonus(owner, target, 8, Impulse(owner, target));
        int count = 0;
        foreach (var other in owner.OwningScene.Players.Items)
        {
            if (other == target || !EnhancedHunters.Hostile(owner, other)
                || (other.Position - target.Position).LengthSquared > 4.5f * 4.5f
                || !EnhancedHunters.Visible(owner.OwningScene, target.Position, other.Position)) continue;
            EnhancedHunters.Bonus(owner, other, 8, Impulse(target, other));
            EnhancedHunterTelemetry.Event(owner.Hunter, "chain-targets");
            if (++count == 2) break;
        }
    }
    private static Vector3 Impulse(PlayerEntity from, PlayerEntity to)
    {
        Vector3 delta = to.Position - from.Position;
        return (delta.LengthSquared > .001f ? delta.Normalized() : Vector3.UnitY) * .16f;
    }
}
