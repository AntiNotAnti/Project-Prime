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

    // A delayed, already authored turret shot can outlive the owner's alt form.
    // The retained turret is an ownership source only: never add it to the scene,
    // split health, find a new target or run its native cooldown a second time.
    internal BeamResultFlags ModSpawnAcceptedTurret(in FireEvent fire)
    {
        if (Hunter != Hunter.Weavel || fire.Kind != FireEventKind.TurretFire
            || fire.Weapon != (byte)BeamType.Battlehammer || !fire.HasPose
            || fire.SourceLife != NetPlayerLifecycle.Get(SlotIndex) || Health <= 0)
            return BeamResultFlags.NoSpawn;
        var equip = new EquipInfo(_scene.WeaponRules[(int)BeamType.Battlehammer], EquipInfo.Beams)
        { InfiniteAmmo = true };
        var flags = BeamSpawnFlags.NoMuzzle;
        if (fire.DoubleAtFire) flags |= BeamSpawnFlags.DoubleDamage;
        var direction = fire.Direction.Normalized();
        var node = _scene.UpdateNodeRef(NodeRef, Position, fire.Origin);
        try
        {
            NetUnlagged.BeginShot(this, fire.Origin, direction, equip);
            var result = BeamProjectileEntity.Spawn(_halfturret, equip, fire.Origin, direction, flags, node, _scene);
            NetUnlagged.EndShot(this);
            return result;
        }
        finally { NetUnlagged.AbortShot(); }
    }

    // Emit an authored shot or live continuous pulse without rerunning input,
    // inventory, cooldown, zoom, charge-release, or respawn transitions.
    internal BeamResultFlags ModSpawnAcceptedShot(in FireEvent fire)
    {
        bool continuous = fire.Kind == FireEventKind.ContinuousTick;
        if (continuous && (fire.Weapon != (byte)BeamType.ShockCoil || fire.SourceLife != NetPlayerLifecycle.Get(SlotIndex)
            || Health <= 0 || !ModIsInPlay || Flags2.TestFlag(PlayerFlags2.Spectating))) return BeamResultFlags.NoSpawn;
        if (!fire.HasPose || fire.Kind == FireEventKind.TurretFire
            || fire.Weapon > (byte)BeamType.OmegaCannon
            || !Mods.Multiplayer.WeaponResourceRules.AllowsBeam((BeamType)fire.Weapon,
                _scene.GameState.InstaGib, _scene.GameState.NoImperialist)) return BeamResultFlags.NoSpawn;
        bool affinity = continuous ? Weapons.GetAffinityBeam(Hunter) == BeamType.ShockCoil || (fire.HasContinuousContext ? fire.ContinuousPrime : IsPrimeHunter)
            : fire.PrimeAtFire || fire.AffinityAtFire;
        var weapon = _scene.WeaponRules[fire.Weapon + (affinity ? 9 : 0)];
        if (weapon.Flags.TestFlag(WeaponFlags.Continuous) != continuous) return BeamResultFlags.NoSpawn;
        // Each flight retains its own equipment (including ricochet/cluster
        // children); never lend it the current weapon's mutable equipment.
        var equip = new EquipInfo(weapon, EquipInfo.Beams)
        { ChargeLevel = fire.Charge, Zoomed = fire.ScopedAtFire, InfiniteAmmo = true };
        var flags = BeamSpawnFlags.NoMuzzle;
        if (continuous ? (fire.HasContinuousContext ? fire.ContinuousDouble : DoubleDamage) : fire.DoubleAtFire) flags |= BeamSpawnFlags.DoubleDamage;
        else if (continuous ? (fire.HasContinuousContext ? fire.ContinuousPrime : IsPrimeHunter) : fire.PrimeAtFire) flags |= BeamSpawnFlags.PrimeHunter;
        var direction = fire.Direction.Normalized();
        BeamResultFlags result;
        try
        {
            NetUnlagged.BeginShot(this, fire.Origin, direction, equip);
            if (continuous && NetFireEvents.TryAcceptedKey(this, out var sourceKey))
                NetContinuousAdmission.CaptureWorld(sourceKey, NetUnlagged.ContinuousTargetFrame(this));
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
            // Reused continuous beams are not "new" to EndShot's flight catch-up.
            // Resolve this pulse before another retained pulse reuses its entity.
            if (continuous && result != BeamResultFlags.NoSpawn)
                foreach (var beam in equip.Beams)
                    if (beam.Lifespan > 0 && beam.ModShotId == fire.ShotId) beam.Process();
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
