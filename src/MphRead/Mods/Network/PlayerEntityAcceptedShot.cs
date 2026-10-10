using MphRead.Mods.Network;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    internal NetTargetIdentity ModCaptureFireTarget()
    {
        if (Mods.EnhancedHunters.EnhancedHunters.UsingAffinity(this, CurrentWeapon)
            && (Hunter == Hunter.Samus || Hunter == Hunter.Kanden && EquipInfo.ChargeLevel == 0))
        {
            var target = Mods.EnhancedHunters.EnhancedHunters.Target(this);
            if (target == null || Hunter == Hunter.Kanden
                && !Mods.EnhancedHunters.EnhancedHunters.InCone(this, target, 20, 15)) return NetTargetIdentity.None;
            return NetTargetIdentity.ForSlot(target.SlotIndex);
        }
        return _modPendingHomingTarget;
    }

    // Emit an already-authored discrete shot without running present-life input,
    // inventory, cooldown, zoom, charge-release, or respawn transitions.
    internal BeamResultFlags ModSpawnAcceptedShot(in FireEvent fire)
    {
        if (!fire.HasPose || fire.Kind is FireEventKind.ContinuousTick or FireEventKind.TurretFire
            || fire.Weapon > (byte)BeamType.OmegaCannon
            || !Mods.Multiplayer.WeaponResourceRules.AllowsBeam((BeamType)fire.Weapon,
                _scene.GameState.InstaGib, _scene.GameState.NoImperialist)) return BeamResultFlags.NoSpawn;
        var weapon = _scene.WeaponRules[fire.Weapon + (fire.PrimeAtFire || fire.AffinityAtFire ? 9 : 0)];
        if (weapon.Flags.TestFlag(WeaponFlags.Continuous)) return BeamResultFlags.NoSpawn;
        // Each flight retains its own equipment (including ricochet/cluster
        // children); never lend it the current weapon's mutable equipment.
        var equip = new EquipInfo(weapon, EquipInfo.Beams)
        { ChargeLevel = fire.Charge, Zoomed = fire.ScopedAtFire, InfiniteAmmo = true };
        var flags = BeamSpawnFlags.NoMuzzle;
        if (fire.DoubleAtFire) flags |= BeamSpawnFlags.DoubleDamage;
        else if (fire.PrimeAtFire) flags |= BeamSpawnFlags.PrimeHunter;
        var direction = fire.Direction.Normalized();
        BeamResultFlags result;
        try
        {
            NetUnlagged.BeginShot(this, fire.Origin, direction, equip);
            result = BeamProjectileEntity.Spawn(this, equip, fire.Origin, direction,
                flags, NodeRef, _scene);
            if (result != BeamResultFlags.NoSpawn)
            {
                NetCampaignEvidence.Committed(this, fire);
                _scene.MatchEvents.Transition(_scene, Mods.MatchEvents.MatchSemanticEventType.WeaponFired,
                    this, weapon: fire.Weapon);
                if (!_scene.Services.IsReplica)
                    ReplayCapture.Event(ReplayEventType.WeaponFired, SlotIndex, value: fire.Weapon);
                else _scene.ReplayShotPresented?.Invoke(SlotIndex, fire.Weapon);
            }
            NetUnlagged.EndShot(this);
        }
        finally { NetUnlagged.AbortShot(); }
        // Cues must not resurrect the old body or release protection in a new life.
        if (result != BeamResultFlags.NoSpawn && Health > 0
            && (fire.SourceLife == 0 || fire.SourceLife == NetPlayerLifecycle.Get(SlotIndex)))
            PlayBeamShotSfx((BeamType)fire.Weapon, fire.Charge >= weapon.MinCharge * 2
                && weapon.Flags.TestFlag(WeaponFlags.CanCharge), false,
                result.TestFlag(BeamResultFlags.Homing), 0);
        return result;
    }
}
