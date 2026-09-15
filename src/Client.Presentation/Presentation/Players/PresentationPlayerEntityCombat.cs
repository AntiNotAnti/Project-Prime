using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private EquipInfo? _combatPresentationEquip;
        internal static bool CanPresentAuthoritativeCombat(bool isHeadless,
            bool isReplica) => !isHeadless && isReplica;

        public void PresentCombat(in CombatEvent value, bool predictedLocalShot = false)
        {
            if (!CanPresentAuthoritativeCombat(_player._scene.IsHeadless,
                    _player._scene.Services.IsReplica))
                return;
            if (value.Kind == CombatEventKind.Affliction)
            {
                PresentNetworkAffliction(value);
                return;
            }
            if (value.Kind == CombatEventKind.Effect)
            {
                if ((value.Flags & CombatEventFlags.LingeringHeat) != 0)
                {
                    int effectId = Metadata.BeamDrawEffects[4];
                    if (effectId != 0)
                        Presentation.SpawnEffect(effectId, Vector3.UnitX,
                            Vector3.UnitY, value.Position);
                }
                return;
            }
            if (value.Kind == CombatEventKind.Shot)
            {
                ClientSceneServices.PlayFor(_player._scene)?.ObserveAuthoritativeProjectileVisual(value,
                    visualWillSpawn: !predictedLocalShot && value.Weapon <= 8);
                if (value.Weapon > 8)
                    return;
                BeamType weapon = (BeamType)value.Weapon;
                if (WeaponVisualLightProfiles.TryGet(weapon,
                    out VisualLightProfile muzzleLight))
                {
                    ulong lightKey = Presentation.GetTransientVisualLightSourceKey(
                        TransientVisualLightSourceKind.MuzzleFlash, value.Id);
                    Presentation.TrySpawnTransientVisualLight(lightKey,
                        value.Position, muzzleLight);
                }
                // The predicted local path already produced its muzzle effect
                // and projectile. Its authoritative acknowledgement still owns
                // this event-idempotent render light, then stops before replaying
                // either of those existing visuals.
                if (predictedLocalShot)
                    return;
                bool charged = (value.Flags & CombatEventFlags.Charged) != 0;
                PlayBeamShotSfx(weapon, charged, weapon == BeamType.ShockCoil, homing: false, amountA: 0);
                Vector3 up = value.Direction.LengthSquared > 0.0001f ? value.Direction.Normalized() : Vector3.UnitZ;
                Vector3 facing = Vector3.Cross(up, MathF.Abs(up.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX).Normalized();
                Presentation.SpawnEffect(Metadata.MuzzleEffectIds[value.Weapon], facing, up, value.Position);
                _combatPresentationEquip ??= new EquipInfo
                {
                    Beams = _player._beams,
                    InfiniteAmmo = true,
                    UnchargedDamage = 0,
                    MinChargeDamage = 0,
                    ChargedDamage = 0,
                    HeadshotDamage = 0,
                    MinChargeHeadshotDamage = 0,
                    ChargedHeadshotDamage = 0,
                    SplashDamage = 0,
                    MinChargeSplashDamage = 0,
                    ChargedSplashDamage = 0
                };
                _combatPresentationEquip.Weapon = Weapons.Current[value.Weapon + ((value.Flags & CombatEventFlags.Affinity) != 0 ? 9 : 0)];
                _combatPresentationEquip.ChargeLevel = value.ChargeLevel;
                uint rng1 = _player._scene.Random.Rng1, rng2 = _player._scene.Random.Rng2;
                try
                {
                    BeamProjectileEntity.Spawn(_player, _combatPresentationEquip, value.Position, up, BeamSpawnFlags.NoMuzzle | (charged ? BeamSpawnFlags.Charged : 0), _player._scene.GetNodeRefByPosition(value.Position), _player._scene, spreadSeed: value.SpreadSeed);
                }
                finally
                {
                    _player._scene.Random.SetRng1(rng1);
                    _player._scene.Random.SetRng2(rng2);
                }
            }
            else if (value.Kind == CombatEventKind.Bomb)
            {
                if (predictedLocalShot)
                    return;
                if (_player.Hunter == Hunter.Sylux)
                {
                    BombEntity[] registered = _player.GetRegisteredLockjawBombs();
                    if (registered.Length >= _player.SyluxBombs.Length)
                    {
                        foreach (BombEntity existing in registered) existing.Countdown = 0;
                        return;
                    }
                }

                Vector3 facing = value.Direction.LengthSquared > 0.0001f ? value.Direction.Normalized() : Vector3.UnitZ;
                Vector3 up = MathF.Abs(facing.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                BombEntity? bomb = BombEntity.Spawn(_player, PlayerEntity.GetTransformMatrix(facing, up, value.Position), _player._scene);
                if (bomb == null)
                    return;
                if (_player.Hunter == Hunter.Sylux)
                {
                    if (!_player.TryRegisterLockjawBomb(bomb))
                    {
                        bomb.Destroy();
                        _player._scene.RemoveEntity(bomb);
                        return;
                    }
                }

                bomb.NodeRef = _player._scene.GetNodeRefByPosition(value.Position);
                bomb.Radius = Fixed.ToFloat(_player.Values.BombRadius);
                bomb.SelfRadius = Fixed.ToFloat(_player.Values.BombSelfRadius);
                bomb.Damage = bomb.EnemyDamage = 0;
                bomb.PlaySpawnSfx();
            }
            else if (value.Kind == CombatEventKind.Damage)
            {
                if (value.Health != 0 && (value.Flags & CombatEventFlags.Silent) == 0)
                    PlayHunterSfx(HunterSfx.Damage);
                if (value.Health == 0 || !_player.IsMainPlayer || _player.IsAltForm)
                    return;
                Vector3 direction = value.Direction;
                ObserveVisorDamage(value);
                // Direction is an authoritative fact. Do not consult a reused attacker slot for a fallback.
                int indicator = MphRead.Combat.CombatFeedback.DamageSector(direction, _player._gunVec1, _player._gunVec2);
                if (indicator >= 0) _damageIndicatorTimers[indicator] = (ushort)SimTicks.From30HzFrames(63);
                _player.CameraInfo.SetShake(Math.Clamp(value.Amount * 0.01f, 0.03f, 0.25f));
                if ((value.Flags & CombatEventFlags.Concussive) != 0)
                {
                    _player.CameraInfo.SetShake(0.25f);
                    _player.ApplyEnhancedConcussion();
                }
            }
        // Death/spawn/form/affliction transitions are rendered from snapshots.
        // Re-running their gameplay paths here would duplicate effects and scores.
        }
    }
}
