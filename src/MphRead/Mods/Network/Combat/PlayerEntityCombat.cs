using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private EquipInfo? _combatPresentationEquip;
        internal CombatShot CombatBurnSource { get; private set; }

        public CombatActor ServerCombatIdentity => _scene.IsHeadless && _networkInputActive
            ? new((byte)SlotIndex, _serverConnectionId, _serverLife) : CombatActor.None;

        private void NoteServerCombatSpawn()
        {
            if (!_scene.IsHeadless || !_networkInputActive || _serverConnectionId == 0) return;
            CombatBurnSource = default;
            _serverLife = unchecked(_serverLife + 1);
            if (_serverLife == 0) _serverLife = 1;
            _serverWasAlive = true;
            ServerCombat.Current?.NoteSpawn(this);
        }

        /// <summary>Sound, animation and HUD only: never invokes the damage, death, ammo or scoring paths.</summary>
        internal void PresentCombat(in CombatEvent value, bool predictedLocalShot = false)
        {
            if (_scene.IsHeadless || !AuthoritativePlay.Active) return;
            if (value.Kind == CombatEventKind.Shot)
            {
                if (predictedLocalShot || value.Weapon > 8) return;
                BeamType weapon = (BeamType)value.Weapon;
                bool charged = (value.Flags & CombatEventFlags.Charged) != 0;
                PlayBeamShotSfx(weapon, charged, weapon == BeamType.ShockCoil, homing: false, amountA: 0);
                Vector3 up = value.Direction.LengthSquared > 0.0001f ? value.Direction.Normalized() : Vector3.UnitZ;
                Vector3 facing = Vector3.Cross(up, MathF.Abs(up.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX).Normalized();
                _scene.SpawnEffect(Metadata.MuzzleEffectIds[value.Weapon], facing, up, value.Position);
                _combatPresentationEquip ??= new EquipInfo
                {
                    Beams = _beams, InfiniteAmmo = true,
                    UnchargedDamage = 0, MinChargeDamage = 0, ChargedDamage = 0,
                    HeadshotDamage = 0, MinChargeHeadshotDamage = 0, ChargedHeadshotDamage = 0,
                    SplashDamage = 0, MinChargeSplashDamage = 0, ChargedSplashDamage = 0
                };
                _combatPresentationEquip.Weapon = Weapons.Current[value.Weapon
                    + ((value.Flags & CombatEventFlags.Affinity) != 0 ? 9 : 0)];
                _combatPresentationEquip.ChargeLevel = value.ChargeLevel;
                uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
                try
                {
                    BeamProjectileEntity.Spawn(this, _combatPresentationEquip, value.Position, up,
                        BeamSpawnFlags.NoMuzzle | (charged ? BeamSpawnFlags.Charged : 0),
                        _scene.GetNodeRefByPosition(value.Position), _scene, spreadSeed: value.SpreadSeed);
                }
                finally
                {
                    Rng.SetRng1(rng1); Rng.SetRng2(rng2);
                }
            }
            else if (value.Kind == CombatEventKind.Bomb)
            {
                if (predictedLocalShot) return;
                if (Hunter == Hunter.Sylux && SyluxBombCount >= 3)
                {
                    for (int i = 0; i < SyluxBombCount; i++)
                        if (SyluxBombs[i] != null) SyluxBombs[i]!.Countdown = 0;
                    return;
                }
                Vector3 facing = value.Direction.LengthSquared > 0.0001f ? value.Direction.Normalized() : Vector3.UnitZ;
                Vector3 up = MathF.Abs(facing.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                BombEntity? bomb = BombEntity.Spawn(this, GetTransformMatrix(facing, up, value.Position), _scene);
                if (bomb == null) return;
                if (Hunter == Hunter.Sylux)
                {
                    SyluxBombs[SyluxBombCount] = bomb;
                    bomb.BombIndex = SyluxBombCount++;
                }
                bomb.NodeRef = _scene.GetNodeRefByPosition(value.Position);
                bomb.Radius = Fixed.ToFloat(Values.BombRadius);
                bomb.SelfRadius = Fixed.ToFloat(Values.BombSelfRadius);
                bomb.Damage = bomb.EnemyDamage = 0;
                bomb.PlaySpawnSfx();
            }
            else if (value.Kind == CombatEventKind.Damage)
            {
                if ((value.Flags & CombatEventFlags.Silent) == 0) PlayHunterSfx(HunterSfx.Damage);
                if (value.Health == 0 || !IsMainPlayer || IsAltForm) return;
                Vector3 direction = value.Direction;
                if (direction.LengthSquared < 0.0001f && value.Actor.Slot < Players.Count)
                    direction = Position - Players[value.Actor.Slot].Position;
                float forward = Vector3.Dot(direction, _gunVec1);
                float right = Vector3.Dot(direction, _gunVec2);
                int indicator = MathF.Abs(forward) >= MathF.Abs(right) ? forward > 0 ? 0 : 4 : right > 0 ? 2 : 6;
                _damageIndicatorTimers[indicator] = 126;
                CameraInfo.SetShake(Math.Clamp(value.Amount * 0.01f, 0.03f, 0.25f));
            }
            // Death/spawn/form/affliction transitions are rendered from snapshots.
            // Re-running their gameplay paths here would duplicate effects and scores.
        }
    }
}
