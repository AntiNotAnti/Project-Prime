using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
internal static class EnhancedHunterProjectiles
{
    internal static void Spawned(BeamProjectileEntity beam, BeamProjectileEntity? parent)
    {
        if (beam.Owner is HalfturretEntity turret && EnhancedHunters.Authority(turret.Owner))
        {
            if (turret.Owner.EnhancedState.ValueB > 0)
            {
                turret.Owner.EnhancedState.ValueB--;
                beam.EnhancedSiegeRound = true;
                beam.Velocity *= 1.2f; beam.Speed *= 1.2f;
                beam.SplashRadius = .9f; beam.SplashDamage = 3;
                EnhancedHunterTelemetry.Event(turret.Owner.Hunter, "siege-rounds");
            }
            return;
        }
        if (beam.Owner is not PlayerEntity owner || !EnhancedHunters.UsingAffinity(owner, beam.Beam)) return;
        if (beam.EnhancedMicroSeeker)
        {
            beam.Damage = beam.HeadshotDamage = 6; beam.SplashDamage = 3; beam.SplashRadius = .65f;
            beam.Afflictions = Affliction.None; beam.Flags &= ~BeamFlags.SelfDamage;
            beam.Flags |= BeamFlags.Homing;
            beam.Homing = Math.Max(beam.Homing, .2f); beam.Target = EnhancedHunters.Target(owner);
            return;
        }
        if (owner.Hunter == Hunter.Kanden && !beam.Flags.TestFlag(BeamFlags.Charged))
        {
            var target = EnhancedHunters.Target(owner);
            if (target != null && EnhancedHunters.InCone(owner, target, 20, 15))
            { beam.Target = target; beam.Flags |= BeamFlags.Homing; beam.Homing = Math.Max(beam.Homing, .06f); }
        }
        if (parent != null) return;
        if (owner.Hunter == Hunter.Samus && beam.Flags.TestFlag(BeamFlags.Charged))
        {
            var state = owner.EnhancedState;
            int count = state.ValueA;
            if (EnhancedHunters.Authority(owner))
            { EnhancedHunterTelemetry.Event(owner.Hunter, "barrages"); EnhancedHunterTelemetry.Event(owner.Hunter, "pips-at-fire", count); }
            if (!EnhancedHunters.Authority(owner) && count == 0 && state.TimerB > 0) count = state.ValueB;
            if (EnhancedHunters.Target(owner) == null) count = 0;
            state.ValueA = 0; state.ContactFrames = 0;
            var equip = new EquipInfo { Weapon = owner.EquipInfo.Weapon, Beams = owner.EquipInfo.Beams,
                InfiniteAmmo = true, UnchargedDamage = 6, HeadshotDamage = 6, SplashDamage = 3 };
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = (beam.Direction + beam.Right * ((i - (count - 1) * .5f) * .07f)).Normalized();
                BeamProjectileEntity.Spawn(owner, equip, beam.Position, direction, BeamSpawnFlags.NoMuzzle,
                    beam.NodeRef, owner.OwningScene, parent: beam, enhancedMicro: true);
            }
            EnhancedHunterTelemetry.Event(owner.Hunter, "micro-missiles", count);
            state.ValueB = (byte)count; state.TimerB = 18;
        }
    }
    internal static void Impact(BeamProjectileEntity beam, CollisionResult collision, EntityBase? hit)
    {
        if (beam.Owner is not PlayerEntity owner || !EnhancedHunters.UsingAffinity(owner, beam.Beam)) return;
        if (!EnhancedHunters.Authority(owner))
        {
            if (owner.Hunter == Hunter.Spire)
                foreach (var zone in owner.OwningScene.EnhancedWorld.Zones)
                    if (zone.Type == EnhancedZoneType.MagmaPool && EnhancedHunterWorld.Owned(owner, zone)
                        && EnhancedHunterWorld.Contains(zone, collision.Position)
                        && EnhancedHunterWorld.Contains(zone, owner.Position))
                        EnhancedHunterMovement.Predict(owner, Vector3.UnitY * .3f + owner.FacingVector.WithY(0) * .12f);
            return;
        }
        if (owner.Hunter == Hunter.Spire)
        {
            EnhancedHunterTelemetry.Event(owner.Hunter, "ricochet-impacts");
            EnhancedHunterTelemetry.Event(owner.Hunter, "ricochet-level-total", beam.EnhancedBounceCount);
            if (owner.OwningScene.EnhancedWorld.Detonate(owner, collision.Position)) return;
            if (hit == null && collision.Plane.Y >= .65f
                && (beam.EnhancedFullCharge || beam.EnhancedBounceCount >= 2))
                owner.OwningScene.EnhancedWorld.Add(owner, EnhancedZoneType.MagmaPool, collision.Position);
        }
        else if (owner.Hunter == Hunter.Noxus && hit == null && collision.Plane.Y >= .65f
            && beam.Flags.TestFlag(BeamFlags.Charged))
            owner.OwningScene.EnhancedWorld.Add(owner, EnhancedZoneType.IcePatch, collision.Position);
    }
    internal static void IceWaveFloor(BeamProjectileEntity beam)
    {
        if (beam.Owner is not PlayerEntity owner || owner.Hunter != Hunter.Noxus
            || !EnhancedHunters.Authority(owner) || !EnhancedHunters.UsingAffinity(owner, beam.Beam)) return;
        Vector3 from = beam.Position, to = from + beam.Direction * beam.MaxDistance;
        CollisionResult collision = default;
        var candidates = CollisionDetection.GetCandidatesForLimits(from, to, 0, null, Vector3.Zero,
            includeEntities: false, owner.OwningScene);
        if (CollisionDetection.CheckBetweenPoints(candidates, from, to, TestFlags.Beams,
            owner.OwningScene, ref collision) && collision.Plane.Y >= .65f)
            owner.OwningScene.EnhancedWorld.Add(owner, EnhancedZoneType.IcePatch, collision.Position);
    }
    internal static void ScaleExplosion(BeamProjectileEntity beam)
    {
        if (beam.Owner is not PlayerEntity owner || owner.Hunter != Hunter.Spire
            || !EnhancedHunters.UsingAffinity(owner, beam.Beam)) return;
        beam.SplashRadius *= beam.EnhancedBounceCount switch { 1 => 1.15f, 2 => 1.25f, 3 => 1.35f, _ => 1 };
        if (beam.EnhancedBounceCount == 3) beam.SplashDamage *= 1.15f;
    }
}
