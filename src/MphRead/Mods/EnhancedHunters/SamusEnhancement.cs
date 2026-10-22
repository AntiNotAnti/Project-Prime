using MphRead.Entities;

namespace MphRead.Mods.EnhancedHunters;
internal static class SamusEnhancement
{
    internal static void Frame(PlayerEntity owner)
    {
        var s = owner.EnhancedState;
        if (owner.CurrentWeapon != BeamType.Missile || owner.IsAltForm) { s.ClearTarget(); return; }
        PlayerEntity? best = null; float distance = float.MaxValue;
        foreach (var other in owner.OwningScene.Players.Items)
        {
            if (!EnhancedHunters.Hostile(owner, other) || !other.GetTargetable()
                || !EnhancedHunters.InCone(owner, other, 30, 8)) continue;
            float d = (other.Position - owner.Position).LengthSquared;
            if (d < distance) { best = other; distance = d; }
        }
        if (best == null) return; // TimerA supplies the acquisition grace.
        if (EnhancedHunters.Target(owner) != best) s.ClearTarget();
        EnhancedHunters.Mark(owner, best, EnhancedHunterTuning.LockGraceFrames);
        if (s.ValueA < 3 && ++s.ContactFrames >= EnhancedHunterTuning.LockPipFrames)
        {
            s.ValueA++; s.ContactFrames = 0;
            if (s.ValueA == 3) EnhancedHunterTelemetry.Event(owner.Hunter, "full-locks");
        }
    }
}
