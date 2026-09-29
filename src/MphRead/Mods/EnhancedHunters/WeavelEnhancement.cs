using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
internal static class WeavelEnhancement
{
    internal static void Hit(PlayerEntity owner)
    {
        var s = owner.EnhancedState;
        s.ValueA = (byte)Math.Min(3, s.ValueA + 1); s.DecayFrames = EnhancedHunterTuning.SiegeDecayFrames;
        EnhancedHunterTelemetry.Event(owner.Hunter, "siege-charges");
    }
    internal static void Frame(PlayerEntity owner)
    {
        var s = owner.EnhancedState;
        if (s.DecayFrames > 0 && --s.DecayFrames == 0 && s.ValueA > 0)
        { s.ValueA--; s.DecayFrames = EnhancedHunterTuning.SiegeDecayStep; }
        if (owner.IsAltForm && owner.Halfturret.Health <= 0) { s.ValueB = 0; s.TimerB = 0; }
    }
    internal static void Enter(PlayerEntity owner)
    {
        var s = owner.EnhancedState;
        EnhancedHunterTelemetry.Event(owner.Hunter, "transforms");
        EnhancedHunterTelemetry.Event(owner.Hunter, "charge-at-transform", s.ValueA);
        s.ValueB = s.ValueA; s.ValueA = 0;
        s.TimerB = s.ValueB == 3 ? EnhancedHunterTuning.OverclockFrames : 0;
        if (s.TimerB > 0) EnhancedHunterTelemetry.Event(owner.Hunter, "overclocks");
    }
    internal static bool SiegeActive(PlayerEntity owner) => EnhancedHunters.Enabled(owner)
        && (owner.EnhancedState.ValueB > 0 || owner.EnhancedState.TimerB > 0);
    internal static void AltHit(PlayerEntity owner, PlayerEntity target)
    {
        if (!SiegeActive(owner)) return;
        var s = owner.EnhancedState;
        s.CrossfireTarget = (byte)target.SlotIndex; s.CrossfireLife = EnhancedHunters.Life(target);
        s.CrossfireFrame = (int)owner.OwningScene.FrameCount;
        if ((owner.Halfturret.Position - target.Position).LengthSquared <= 225
            && EnhancedHunters.Visible(owner.OwningScene, owner.Halfturret.Position.AddY(.4f), target.Position))
            owner.Halfturret.ModEnhancedTarget(target);
    }
    internal static void TurretHit(PlayerEntity owner, PlayerEntity target)
    {
        var s = owner.EnhancedState;
        if (!SiegeActive(owner) || s.CrossfireExpiry[target.SlotIndex] > (int)owner.OwningScene.FrameCount || s.CrossfireTarget != target.SlotIndex
            || s.CrossfireLife != EnhancedHunters.Life(target)
            || (int)owner.OwningScene.FrameCount - s.CrossfireFrame > EnhancedHunterTuning.CrossfireWindow) return;
        s.CrossfireExpiry[target.SlotIndex] = (int)owner.OwningScene.FrameCount + EnhancedHunterTuning.CrossfireCooldown;
        s.CrossfireFrame = -1000;
        var delta = target.Position - owner.Halfturret.Position;
        EnhancedHunters.Bonus(owner, target, 6, (delta.LengthSquared > .001f ? delta.Normalized() : Vector3.UnitY) * .15f);
        foreach (var other in owner.OwningScene.Players.Items)
        {
            if (other == target || !EnhancedHunters.Hostile(owner, other)
                || (other.Position - target.Position).LengthSquared > 2.25f
                || !EnhancedHunters.Visible(owner.OwningScene, target.Position, other.Position)) continue;
            Vector3 radial = other.Position - target.Position;
            EnhancedHunters.Bonus(owner, other, 6, radial.LengthSquared > .001f ? radial.Normalized() * .15f : Vector3.UnitY * .15f);
        }
        EnhancedHunterTelemetry.Event(owner.Hunter, "crossfire-blasts");
    }
}
