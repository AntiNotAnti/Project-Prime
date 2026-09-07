using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private EquipInfo? _combatPresentationEquip;
        public void PresentCombat(in CombatEvent value, bool predictedLocalShot = false)
        {
            if (_player._scene.IsHeadless || !AuthoritativePlay.Active)
                return;
            if (value.Kind == CombatEventKind.Affliction)
            {
                PresentNetworkAffliction(value);
                return;
            }
            if (value.Kind == CombatEventKind.Shot)
            {
                if (predictedLocalShot || value.Weapon > 8)
                    return;
                BeamType weapon = (BeamType)value.Weapon;
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
                uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
                try
                {
                    BeamProjectileEntity.Spawn(_player, _combatPresentationEquip, value.Position, up, BeamSpawnFlags.NoMuzzle | (charged ? BeamSpawnFlags.Charged : 0), _player._scene.GetNodeRefByPosition(value.Position), _player._scene, spreadSeed: value.SpreadSeed);
                }
                finally
                {
                    Rng.SetRng1(rng1);
                    Rng.SetRng2(rng2);
                }
            }
            else if (value.Kind == CombatEventKind.Bomb)
            {
                if (predictedLocalShot)
                    return;
                if (_player.Hunter == Hunter.Sylux && _player.SyluxBombCount >= 3)
                {
                    for (int i = 0; i < _player.SyluxBombCount; i++)
                        if (_player.SyluxBombs[i] != null)
                            _player.SyluxBombs[i]!.Countdown = 0;
                    return;
                }

                Vector3 facing = value.Direction.LengthSquared > 0.0001f ? value.Direction.Normalized() : Vector3.UnitZ;
                Vector3 up = MathF.Abs(facing.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                BombEntity? bomb = BombEntity.Spawn(_player, PlayerEntity.GetTransformMatrix(facing, up, value.Position), _player._scene);
                if (bomb == null)
                    return;
                if (_player.Hunter == Hunter.Sylux)
                {
                    _player.SyluxBombs[_player.SyluxBombCount] = bomb;
                    bomb.BombIndex = _player.SyluxBombCount++;
                }

                bomb.NodeRef = _player._scene.GetNodeRefByPosition(value.Position);
                bomb.Radius = Fixed.ToFloat(_player.Values.BombRadius);
                bomb.SelfRadius = Fixed.ToFloat(_player.Values.BombSelfRadius);
                bomb.Damage = bomb.EnemyDamage = 0;
                bomb.PlaySpawnSfx();
            }
            else if (value.Kind == CombatEventKind.Damage)
            {
                if ((value.Flags & CombatEventFlags.Silent) == 0)
                    PlayHunterSfx(HunterSfx.Damage);
                if (value.Health == 0 || !_player.IsMainPlayer || _player.IsAltForm)
                    return;
                Vector3 direction = value.Direction;
                if (direction.LengthSquared < 0.0001f && value.Actor.Slot < PlayerEntity.Players.Count)
                    direction = _player.Position - PlayerEntity.Players[value.Actor.Slot].Position;
                float forward = Vector3.Dot(direction, _player._gunVec1);
                float right = Vector3.Dot(direction, _player._gunVec2);
                int indicator = MathF.Abs(forward) >= MathF.Abs(right) ? forward > 0 ? 0 : 4 : right > 0 ? 2 : 6;
                _damageIndicatorTimers[indicator] = 126;
                _player.CameraInfo.SetShake(Math.Clamp(value.Amount * 0.01f, 0.03f, 0.25f));
            }
        // Death/spawn/form/affliction transitions are rendered from snapshots.
        // Re-running their gameplay paths here would duplicate effects and scores.
        }
    }
}
