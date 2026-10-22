using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
internal static class SyluxEnhancement
{
    internal static void Hit(PlayerEntity owner, PlayerEntity target)
    {
        var s = owner.EnhancedState;
        if (EnhancedHunters.Target(owner) != target) s.ClearTarget();
        EnhancedHunters.Mark(owner, target, 60);
        s.LastContactFrame = (int)owner.OwningScene.FrameCount;
    }
    internal static void Frame(PlayerEntity owner)
    {
        var s = owner.EnhancedState; var target = EnhancedHunters.Target(owner);
        if (owner.IsAltForm && s.TimerB > 0) return;
        bool contact = target != null && owner.CurrentWeapon == BeamType.ShockCoil && owner.Controls.Shoot.IsDown
            && (int)owner.OwningScene.FrameCount - s.LastContactFrame <= EnhancedHunterTuning.TetherGraceFrames
            && (target.Position - owner.Position).LengthSquared <= 400;
        if (!contact)
        {
            if (target != null && s.ValueA == 100 && s.WasFiring && !owner.Controls.Shoot.IsDown)
            {
                var delta = target.Position - owner.Position;
                var impulse = delta.LengthSquared > .001f ? delta.Normalized() * .16f : Vector3.UnitY * .1f;
                EnhancedHunters.Bonus(owner, target, 6, impulse);
                EnhancedHunterMovement.PushOwner(owner, -impulse);
                EnhancedHunterTelemetry.Event(owner.Hunter, "release-bursts");
            }
            s.ClearTarget();
        }
        else
        {
            s.ContactFrames++;
            if (s.ContactFrames >= EnhancedHunterTuning.TetherFrames)
            {
                if (s.Flags == 0) EnhancedHunterTelemetry.Event(owner.Hunter, "tethers");
                EnhancedHunterTelemetry.Event(owner.Hunter, "tether-frames");
                if (s.ValueA < 100 && s.ContactFrames - EnhancedHunterTuning.TetherFrames >= EnhancedHunterTuning.OverchargeFrames)
                    EnhancedHunterTelemetry.Event(owner.Hunter, "overcharges");
                s.Flags = 1;
                s.ValueA = (byte)Math.Min(100, (s.ContactFrames - EnhancedHunterTuning.TetherFrames) * 100 / EnhancedHunterTuning.OverchargeFrames);
            }
        }
        s.WasFiring = owner.Controls.Shoot.IsDown;
    }
    internal static void Movement(PlayerEntity owner)
    {
        var s = owner.EnhancedState; var target = EnhancedHunters.Target(owner);
        if (target == null || s.Flags == 0) return;
        Vector3 delta = target.Position - owner.Position;
        if (delta.LengthSquared < .001f) return;
        Vector3 direction = delta.Normalized();
        if (!EnhancedHunters.Authority(owner))
        {
            if (s.ValueA == 100 && s.WasFiring && !owner.Controls.Shoot.IsDown)
                EnhancedHunterMovement.Predict(owner, -direction * .16f);
            s.WasFiring = owner.Controls.Shoot.IsDown;
        }
        if (owner.IsAltForm && s.TimerB > 0) owner.Speed += direction * .004f;
        else if (!owner.IsAltForm && owner.Controls.Jump.IsPressed) owner.Speed += direction * .09f;
        else if (!owner.IsAltForm && owner.Controls.MoveDown.IsDown && s.MovementCooldown == 0)
        { owner.Speed -= direction * .12f; s.MovementCooldown = 36; }
        if (owner.Speed.LengthSquared > .36f * .36f) owner.Speed = owner.Speed.Normalized() * .36f;
    }
}
