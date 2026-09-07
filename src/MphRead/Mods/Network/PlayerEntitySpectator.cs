using System;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        /// <summary>
        /// Applies the desired participation state on the simulation owner.
        /// Releases objective ownership through the existing drop/reset logic,
        /// without manufacturing a combat death or altering scores and lives.
        /// </summary>
        internal void ServerSetSpectating(bool spectating)
        {
            if (!_scene.IsHeadless)
            {
                throw new InvalidOperationException("Spectator participation is server-owned.");
            }
            if (Flags2.TestFlag(PlayerFlags2.Spectating) == spectating)
            {
                return;
            }
            Controls.ClearAll();
            Input.HasInput = false;
            Speed = PrevSpeed = Vector3.Zero;
            EquipInfo.ChargeLevel = 0;
            EquipInfo.Zoomed = false;
            if (spectating)
            {
                _health = 0;
                _healthRecovery = 0;
                _ammoRecovery[0] = _ammoRecovery[1] = 0;
                _frozenTimer = _frozenGfxTimer = _disruptedTimer = _burnTimer = 0;
                _doubleDmgTimer = _deathaltTimer = _cloakTimer = 0;
                _boostCharge = 0;
                _halfturret.Die();
                Flags2 |= PlayerFlags2.Spectating | PlayerFlags2.HideModel;
                Flags2 &= ~PlayerFlags2.Cloaking;
                LoadFlags &= ~(LoadFlags.Active | LoadFlags.Spawned);
                _respawnTimer = RespawnTime;
                Mods.Network.WorldStateCapture.ReleasePlayer(_scene, this);
                _soundSource.StopAllSfx(force: true);
                // Participation changes run before the scene's entity pass.
                // These iterators preserve their next node across removals.
                foreach (BeamProjectileEntity beam in _scene.GetBeamProjectileEntities())
                {
                    if (beam.Owner != this && beam.Owner != _halfturret) { continue; }
                    beam.Destroy();
                    _scene.RemoveEntity(beam);
                }
                foreach (BombEntity bomb in _scene.GetBombEntities())
                {
                    if (bomb.Owner != this) { continue; }
                    bomb.Destroy();
                    _scene.RemoveEntity(bomb);
                }
            }
            else
            {
                Flags2 &= ~PlayerFlags2.Spectating;
                LoadFlags |= LoadFlags.Active;
                _respawnTimer = RespawnTime;
                // The rejoin action already asks to respawn. Keep the normal
                // minimum delay, then let ProcessPlayer select a legal spawn.
                _timeSinceDead = UInt16.MaxValue;
            }
        }
    }
}
