using MphRead.Entities;

namespace MphRead.Mods.EnhancedHunters;
internal static class TraceEnhancement
{
    internal static void Hit(PlayerEntity owner, PlayerEntity target, bool perfect)
    {
        var s = owner.EnhancedState;
        EnhancedHunters.Mark(owner, target, perfect ? EnhancedHunterTuning.PerfectMarkFrames : EnhancedHunterTuning.MarkFrames);
        s.Flags = (byte)((perfect ? 1 : 0) | 2);
        if (s.StationaryShot) { s.GhostFrames = 48; EnhancedHunterTelemetry.Event(owner.Hunter, "ghost-steps"); }
        s.StationaryShot = false;
        EnhancedHunterTelemetry.Event(owner.Hunter, perfect ? "perfect-marks" : "marks");
    }
    internal static float LaunchScale(PlayerEntity owner)
    {
        var s = owner.EnhancedState;
        s.GhostFrames = 0;
        var target = EnhancedHunters.Target(owner);
        if (!EnhancedHunters.Enabled(owner) || (s.Flags & 2) == 0 || target == null || !EnhancedHunters.InCone(owner, target, (s.Flags & 1) != 0 ? 16 : 12, 45)) return 1;
        s.Flags &= unchecked((byte)~2);
        float scale = (s.Flags & 1) != 0 ? 1.35f : 1.25f;

        EnhancedHunterTelemetry.Event(owner.Hunter, "predator-lunges");
        return scale;
    }
    internal static void AltHit(PlayerEntity owner, PlayerEntity target)
    {
        var s = owner.EnhancedState;
        if (!EnhancedHunters.HasMark(owner, target)) return;
        int ghost = (s.Flags & 1) != 0 ? 75 : 45;
        s.ClearTarget(); s.GhostFrames = ghost;
        EnhancedHunterTelemetry.Event(owner.Hunter, "ghost-steps");
        EnhancedHunterTelemetry.Event(owner.Hunter, "assassinations");
    }
    internal static int CloakIncrement(PlayerEntity owner)
    {
        if (!EnhancedHunters.Enabled(owner) || owner.Controls.Shoot.IsDown) return 1;
        var target = EnhancedHunters.Target(owner);
        if (target == null || EnhancedHunters.Visible(owner, target)) return 1;
        // 2.75x over four deterministic simulation ticks.
        return (owner.EnhancedState.Flags & 1) != 0 && owner.OwningScene.FrameCount % 4 != 0 ? 3 : 2;
    }
}
