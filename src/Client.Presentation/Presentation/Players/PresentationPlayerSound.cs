using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Sound;
using MphRead.Hud;
using MphRead.Mods.Input;
using MphRead.Mods.Network;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        public void PlayHunterSfx(HunterSfx sfx)
        {
            if (_player.IsMainPlayer && sfx is HunterSfx.Damage or HunterSfx.Death)
                GamepadHaptics.Play(sfx == HunterSfx.Death
                    ? HapticEvent.Death : HapticEvent.TakingDamage,
                    unchecked((uint)_player.ModScene.FrameCount));
            int id = Metadata.HunterSfx[(int)_player.Hunter, (int)sfx];
            if (id == -1)
            {
                if (_player.Hunter != Hunter.Guardian || sfx != HunterSfx.Spawn) // todo: MP1P
                {
                    return;
                }

                id = Metadata.HunterSfx[(int)Hunter.Samus, (int)sfx];
            }

            if (sfx == HunterSfx.Death && _player.IsMainPlayer)
            {
                _player._soundSource.PlayFreeSfx(id);
            }
            else
            {
                float recency = -1;
                if (sfx == HunterSfx.Damage)
                {
                    recency = 5 / (float)SimTicks.LegacyHz;
                }

                // the game only does this if not Guardian, but the SFX switched to are the same there anyway
                if (!_player.IsMainPlayer)
                {
                    if (sfx == HunterSfx.Damage)
                    {
                        id = Metadata.HunterSfx[(int)_player.Hunter, (int)HunterSfx.DamageEnemy];
                    }
                    else if (sfx == HunterSfx.Death)
                    {
                        id = Metadata.HunterSfx[(int)_player.Hunter, (int)HunterSfx.DeathEnemy];
                    }
                }

                _player._soundSource.PlaySfx(id, recency: recency, sourceOnly: true);
            }
        }

        public int PlayMissileSfx(HunterSfx sfx)
        {
            int id = Metadata.HunterSfx[(int)_player.Hunter, (int)sfx];
            if (id == -1 || Sfx.TimedSfxMute > 0)
            {
                return -1;
            }

            return _player._soundSource.PlayFreeSfxHandle(id);
        }

        private float _damageSfxTimer = 0;
        public void PlayRandomDamageSfx()
        {
            Debug.Assert(_player.IsMainPlayer);
            if (_player.Hunter == Hunter.Samus && _damageSfxTimer == 0)
            {
                // 369 - DAMAGE2
                // 370 - DAMAGE3
                // 371 - DAMAGE4
                uint sfx = _player._scene.Random.GetRandomInt1(3) + 369;
                _player._soundSource.PlaySfx((int)sfx);
                _damageSfxTimer = 90 / (float)SimTicks.LegacyHz;
            }
        }

        public void PlayBeamEmptySfx(BeamType beam)
        {
            int sfx = Metadata.BeamSfx[(int)beam, (int)BeamSfx.Empty];
            if (sfx != -1)
            {
                _player._soundSource.PlaySfx(sfx, noUpdate: true);
            }
        }

        public void PlayBeamShotSfx(BeamType beam, bool charged, bool continuous, bool homing, float amountA)
        {
            if (_player.IsMainPlayer)
                GamepadHaptics.Play(beam == BeamType.Missile
                    ? HapticEvent.Missile : charged
                        ? HapticEvent.ChargedShotRelease : HapticEvent.WeaponFire,
                    unchecked((uint)_player.ModScene.FrameCount));
            StopBeamChargeSfx(beam);
            if (continuous)
            {
                amountA = homing ? amountA + 0x3FFF : 0;
                _player._soundSource.PlaySfx(Metadata.BeamSfx[(int)beam, (int)BeamSfx.Shot], loop: true, amountA: amountA);
                return;
            }

            BeamSfx sfx;
            if (charged)
            {
                sfx = beam == Weapons.AffinityWeapons[(int)_player.Hunter] ? BeamSfx.AffinityChargeShot : BeamSfx.ChargeShot;
            }
            else
            {
                sfx = _player.Hunter == Hunter.Weavel && beam == BeamType.Battlehammer ? BeamSfx.AffinityChargeShot : BeamSfx.Shot;
            }

            int id = Metadata.BeamSfx[(int)beam, (int)sfx];
            if (id != -1)
            {
                _player._soundSource.PlaySfx(id);
            }
        }

        public int GetBeamChargeSfx(BeamType beam)
        {
            if (beam == BeamType.Missile)
            {
                return Metadata.HunterSfx[(int)_player.Hunter, (int)HunterSfx.MissileCharge];
            }

            if (beam == BeamType.Judicator && _player.Hunter == Hunter.Noxus)
            {
                return (int)SfxId.SHOTGUN_CHARGE1_NOX;
            }

            return Metadata.BeamSfx[(int)beam, (int)BeamSfx.Charge];
        }

        public void PlayBeamChargeSfx(BeamType beam)
        {
            int sfx = GetBeamChargeSfx(beam);
            if (sfx != -1)
            {
                _player._soundSource.PlaySfx(sfx, loop: true);
            }
        }

        public void StopBeamChargeSfx(BeamType beam)
        {
            int sfx = GetBeamChargeSfx(beam);
            if (sfx != -1)
            {
                _player._soundSource.StopSfx(sfx);
            }
        }

        public void StopContinuousBeamSfx(BeamType beam)
        {
            int sfx = Metadata.BeamSfx[(int)beam, (int)BeamSfx.Shot];
            _player._soundSource.StopSfx(sfx);
            sfx = Metadata.BeamSfx[(int)beam, (int)BeamSfx.AffinityChargeShot];
            _player._soundSource.StopSfx(sfx);
        }

        private int _healthSfxHandle = -1;
        public void UpdateHealthSfx(int health)
        {
            if (Mods.Network.ClientSceneServices.PlayFor(_player._scene) != null
                || Mods.Network.ReplayPlayback.IsModern)
            {
                // Modern feedback announces threshold crossings once. Retire a
                // legacy loop if this presentation switches to a network session.
                if (_healthSfxHandle != -1)
                {
                    _player._soundSource.StopSfxByHandle(_healthSfxHandle);
                    _healthSfxHandle = -1;
                }
                return;
            }
            if (Sfx.TimedSfxMute > 0)
            {
                return;
            }

            if (health > 0 && health < 25)
            {
                if (!_player._soundSource.IsHandlePlaying(_healthSfxHandle))
                {
                    _healthSfxHandle = _player._soundSource.PlayFreeSfxHandle(SfxId.ENERGY_ALARM);
                }
            }
            else if (_healthSfxHandle != -1)
            {
                _player._soundSource.StopSfxByHandle(_healthSfxHandle);
                _healthSfxHandle = -1;
            }
        }

        public void UpdateWalkingSfx()
        {
            if (!_player.Flags1.TestFlag(PlayerFlags1.MovingBiped) || _player._hSpeedMag <= 0)
            {
                _walkSfxTimer = 10 / 30;
                _walkSfxIndex = 0;
                return;
            }

            _walkSfxTimer += _player._scene.FrameTime;
            int sfxId = -1;
            if (_walkSfxTimer >= 15 / (float)SimTicks.LegacyHz)
            {
                if (_walkSfxIndex == 0)
                {
                    sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Walk1];
                    _walkSfxIndex = 1;
                }
            }

            if (_walkSfxTimer >= 25 / (float)SimTicks.LegacyHz)
            {
                Debug.Assert(_walkSfxIndex == 1);
                sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Walk2];
                _walkSfxTimer = 5 / (float)SimTicks.LegacyHz;
                _walkSfxIndex = 0;
            }

            if (_player._standTerrain == Terrain.Lava && _player.Hunter != Hunter.Spire)
            {
                sfxId = -1;
            }

            float amountB = _player._scene.Random.GetRandomInt1(0x7FFF) * 2;
            if (sfxId != -1)
            {
                _player._soundSource.PlaySfx(sfxId, amountA: 0xFFFF, amountB: amountB);
            }
        }

        public int GetAltMovementSfx()
        {
            int sfxId;
            if (_player.Hunter == Hunter.Samus)
            {
                sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Roll];
            }
            else if (_player.Hunter == Hunter.Trace)
            {
                sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.TraceAlt];
            }
            else
            {
                sfxId = Metadata.HunterSfx[(int)_player.Hunter, (int)HunterSfx.Roll];
            }

            return sfxId;
        }

        public void UpdateAltMovementSfx()
        {
            float newAmount = 0xFFFF * _player._hSpeedMag / Fixed.ToFloat(_player.Values.AltMinHSpeed);
            UpdateMovementSfxAmount(newAmount);
            int sfxId = GetAltMovementSfx();
            if (sfxId != -1)
            {
                _player._soundSource.PlaySfx(sfxId, loop: true, amountA: _moveSfxAmount);
            }
        }

        public void UpdateSlidingSfx(float newAmount)
        {
            UpdateMovementSfxAmount(newAmount);
            int sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Slide];
            if (sfxId != -1)
            {
                _player._soundSource.PlaySfx(sfxId, loop: true, amountA: _moveSfxAmount);
            }
        }

        public void UpdateMovementSfxAmount(float newAmount)
        {
            float prevAmount = _moveSfxAmount;
            if (!_player.Flags1.TestFlag(PlayerFlags1.Grounded))
            {
                newAmount = _player.ExponentialDecay(0.5f, prevAmount);
            }
            else if (_player._scene.FrameCount % 2 == 0) // todo: FPS stuff
            {
                if (newAmount < prevAmount)
                {
                    newAmount = prevAmount + (newAmount - prevAmount) / 4;
                }
                else
                {
                    newAmount = prevAmount + (newAmount - prevAmount) / 2;
                }
            }
            else
            {
                newAmount = _moveSfxAmount;
            }

            if (newAmount < 1000)
            {
                newAmount = 0;
            }

            _moveSfxAmount = newAmount;
        }

        public void StopTerrainSfx(Terrain prevTerrain)
        {
            int curSfx;
            int prevSfx;
            if (_player.Hunter == Hunter.Samus)
            {
                curSfx = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Roll];
                prevSfx = Metadata.TerrainSfx[(int)prevTerrain, (int)TerrainSfx.Roll];
            }
            else if (_player.Hunter == Hunter.Trace)
            {
                curSfx = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.TraceAlt];
                prevSfx = Metadata.TerrainSfx[(int)prevTerrain, (int)TerrainSfx.TraceAlt];
            }
            else
            {
                curSfx = Metadata.HunterSfx[(int)_player.Hunter, (int)HunterSfx.Roll];
                prevSfx = curSfx;
            }

            if (curSfx != prevSfx && prevSfx != -1)
            {
                _player._soundSource.StopSfx(prevSfx);
            }

            curSfx = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Slide];
            prevSfx = Metadata.TerrainSfx[(int)prevTerrain, (int)TerrainSfx.Slide];
            if (curSfx != prevSfx && prevSfx != -1)
            {
                _player._soundSource.StopSfx(prevSfx);
            }
        }

        public void StopAltFormSfx()
        {
            if (_player.IsAltForm)
            {
                int sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Slide];
                if (sfxId != -1)
                {
                    _player._soundSource.StopSfx(sfxId);
                }
            }
            else
            {
                _player._soundSource.StopSfx(SfxId.NOX_TOP_ATTACK2);
                _player._soundSource.StopSfx(SfxId.NOX_TOP_ENERGY_DRAIN2);
                int sfxId = GetAltMovementSfx();
                if (sfxId != -1)
                {
                    _player._soundSource.StopSfx(sfxId);
                }
            }
        }

        public void PlayLandingSfx()
        {
            int sfxId = Metadata.TerrainSfx[(int)_player._standTerrain, (int)TerrainSfx.Land];
            float amountA = 0xFFFF * _player._timeBeforeLanding / (float)SimTicks.From30HzFrames(90); // todo: FPS stuff
            _player._soundSource.PlaySfx(sfxId, amountA: amountA);
        }

        public void UpdateBurningSfx(bool burning)
        {
            if (_player._scene.Services.IsReplica)
            {
                UpdateNetworkAfflictionPresentation();
                burning |= NetworkAfflictions.At(AfflictionPresentationTick()).Burn > 0 && _player.Health > 0;
            }
            float prevAmount = _burnSfxAmount;
            float newAmount = 0xFFFF;
            if (!burning)
            {
                newAmount = _player.ExponentialDecay(0.875f, prevAmount);
                if (newAmount < 50)
                {
                    newAmount = 0;
                }
            }

            if (newAmount > 0)
            {
                _burnSfxAmount = newAmount;
                _player._soundSource.PlaySfx(SfxId.DGN_LAVA_DAMAGE, loop: true, amountA: newAmount);
            }
            else if (prevAmount > 0)
            {
                _burnSfxAmount = 0;
                _player._soundSource.StopSfx(SfxId.DGN_LAVA_DAMAGE);
            }
        }

        private static readonly IReadOnlyList<SfxId> _dblDamageIds = new SfxId[3]
        {
            SfxId.DBL_DAMAGE_A,
            SfxId.DBL_DAMAGE_B,
            SfxId.DBL_DAMAGE_C
        };
        public bool _dblDamageSfxMuted = false;
        private int _dblDamageSfxHandle = -1;
        private SfxId _dblDamageSfxId = SfxId.None;
        public void UpdateDoubleDamageSfx(int index, bool play)
        {
            if (index != -1)
            {
                if (play)
                {
                    if (_dblDamageSfxHandle != -1)
                    {
                        _player._soundSource.StopSfxByHandle(_dblDamageSfxHandle);
                        _dblDamageSfxHandle = -1;
                    }

                    _dblDamageSfxId = _dblDamageIds[index];
                }
                else
                {
                    if (_dblDamageSfxHandle != -1)
                    {
                        _player._soundSource.StopSfxByHandle(_dblDamageSfxHandle);
                    }

                    _dblDamageSfxHandle = -1;
                    _dblDamageSfxId = SfxId.None;
                }
            }
            else if (_dblDamageSfxHandle != -1)
            {
                _player._soundSource.StopSfxByHandle(_dblDamageSfxHandle);
            }
        }

        private static readonly IReadOnlyList<SfxId> _cloakSfxIds = new SfxId[3]
        {
            SfxId.CLOAK_A,
            SfxId.CLOAK_B,
            SfxId.CLOAK_C
        };
        private bool _cloakSfxMuted = false;
        private int _cloakSfxHandle = -1;
        private SfxId _cloakSfxId = SfxId.None;
        public void UpdateCloakSfx(int index, bool play)
        {
            if (index != -1)
            {
                if (play)
                {
                    if (_cloakSfxHandle != -1)
                    {
                        _player._soundSource.StopSfxByHandle(_cloakSfxHandle);
                        _cloakSfxHandle = -1;
                    }

                    _cloakSfxId = _cloakSfxIds[index];
                }
                else
                {
                    if (_cloakSfxHandle != -1)
                    {
                        _player._soundSource.StopSfxByHandle(_cloakSfxHandle);
                    }

                    _cloakSfxHandle = -1;
                    _cloakSfxId = SfxId.None;
                }
            }
            else if (_cloakSfxHandle != -1)
            {
                _player._soundSource.StopSfxByHandle(_cloakSfxHandle);
            }
        }

        private bool _flagCarrySfxOn = false;
        private bool _flagCarrySfxMuted = false;
        private int _flagCarrySfxHandle = -1;
        public void StartFlagCarrySfx()
        {
            _flagCarrySfxOn = true;
        }

        public void StopFlagCarrySfx()
        {
            if (_flagCarrySfxHandle != -1)
            {
                _player._soundSource.StopSfxByHandle(_flagCarrySfxHandle);
            }
            _flagCarrySfxHandle = -1;
            _flagCarrySfxOn = false;
        }

        private SoundSource? _timedSfx;
        private SoundSource _timedSfxSource => _timedSfx ??= new SoundSource(_scene);

        public float ForceFieldSfxTimer = 0;
        public float DoorUnlockSfxTimer = 0;
        public float DoorChimeSfxTimer = 0;
        public void StopAllSfx()
        {
            StopFlagCarrySfx();
            // the game also suspends the weapon alarm SFX here
            UpdateHealthSfx(health: 0);
            UpdateDoubleDamageSfx(index: 0, play: false);
            UpdateCloakSfx(index: 0, play: false);
            // the game doesn't stop music if a likely download play check passes
            // todo: the game stops music here, but I think we have other cases covered, and need to not stop for teleporters
            _player._soundSource.StopFreeSfxScripts();
            _player._soundSource.StopFreeSfx(SfxId.FAST_SCROLL_UP_LOOP);
            _player._scene.Audio.StopEnvironment();
            _player._scene.Audio.StopAll(force: false);
        }

        public void PlayTimedSfx(SfxId id)
        {
            _timedSfxSource.PlaySfx(id, recency: 0, sourceOnly: true);
        }

        public void StopTimedSfx(SfxId id)
        {
            _timedSfxSource.StopSfx(id);
        }

        public void StopTimedSfx()
        {
            if (Sfx.TimedSfxMute == 0)
            {
                // the game stops the double damage and cloak SFX, but we just mute them
                _dblDamageSfxMuted = true;
                _cloakSfxMuted = true;
                if (_flagCarrySfxOn)
                {
                    _flagCarrySfxOn = false;
                    _flagCarrySfxMuted = true;
                }

                UpdateHealthSfx(health: 0);
            // the game also suspends the weapon alarm SFX here
            }

            Sfx.TimedSfxMute++;
        }

        public void RestartTimedSfx(bool force = false)
        {
            if (force || --Sfx.TimedSfxMute <= 0)
            {
                Sfx.TimedSfxMute = 0;
                _dblDamageSfxMuted = false;
                _cloakSfxMuted = false;
                if (_flagCarrySfxMuted)
                {
                    _flagCarrySfxMuted = false;
                    _flagCarrySfxOn = true;
                }
            // the game also restarts the weapon alarm SFX here
            }
        }

        public void StopLongSfx()
        {
            StopTimedSfx();
            if (Sfx.LongSfxMute == 0)
            {
                Sfx.SfxMute = true;
                Sfx.Instance.StopEnvironmentSfx();
            }

            Sfx.LongSfxMute++;
        }

        public void RestartLongSfx(bool force = false)
        {
            RestartTimedSfx(force);
            if (force || --Sfx.LongSfxMute <= 0)
            {
                Sfx.LongSfxMute = 0;
                // the game does this along with the timed SFX,
                // even though it's the long SFX suppression that sets this true
                Sfx.SfxMute = false;
            }
        }

        private float _scrollSfxTimer = 0;
        public void UpdateTimedSounds()
        {
            _timedSfxSource.Update(_player.Position, rangeIndex: -1);
            if (_damageSfxTimer > 0)
            {
                _damageSfxTimer -= _player._scene.FrameTime;
                if (_damageSfxTimer < 0)
                {
                    _damageSfxTimer = 0;
                }
            }

            if (_dblDamageSfxId != SfxId.None && !_player._soundSource.IsHandlePlaying(_dblDamageSfxHandle) && !_dblDamageSfxMuted)
            {
                _dblDamageSfxHandle = _player._soundSource.PlayFreeSfxHandle(_dblDamageSfxId);
            }

            if (_cloakSfxId != SfxId.None && !_player._soundSource.IsHandlePlaying(_cloakSfxHandle) && !_cloakSfxMuted)
            {
                _cloakSfxHandle = _player._soundSource.PlayFreeSfxHandle(_cloakSfxId);
            }

            if (_flagCarrySfxOn && !_player._soundSource.IsHandlePlaying(_flagCarrySfxHandle))
            {
                _flagCarrySfxHandle = _player._soundSource.PlayFreeSfxHandle(SfxId.FLAG_CARRIED);
            }

            // the game plays the unused WEAPON_ALARM alarm SFX here
            MusicId musicId = MusicId.Invalid;
            for (int i = 0; i < _player._scene.MessageQueue.Count; i++)
            {
                MessageInfo message = _player._scene.MessageQueue[i];
                if (message.ExecuteFrame != _player._scene.FrameCount)
                {
                    continue;
                }

                if (message.Message == Message.PlaySfxScript)
                {
                    int id = (int)message.Param1;
                    if (id == -1)
                    {
                        DoorChimeSfxTimer = 2 / (float)SimTicks.LegacyHz;
                    }
                    else if (id <= 104)
                    {
                        _timedSfxSource.PlaySfx(id | 0x4000, recency: 0, sourceOnly: true);
                    }
                }
                else if (message.Message == Message.UpdateMusic)
                {
                    if ((int)message.Param2 == 0)
                    {
                        musicId = (MusicId)(int)message.Param1;
                    }
                }
            }

            if (musicId != MusicId.Invalid && _player._scene.LocalPlayer!.Health > 0)
            {
                if (Sfx.TimedSfxMute > 0)
                {
                    Music.UpdateMusicIdIfPaused(musicId);
                }
                else
                {
                    Music.PlayMusic(musicId);
                }
            }

            // sfxtodo: escape sequence and pause stuff
            if (Sfx.LongSfxMute == 0 && DoorUnlockSfxTimer > 0)
            {
                DoorUnlockSfxTimer -= _player._scene.FrameTime;
                if (DoorUnlockSfxTimer <= 1 / (float)SimTicks.LegacyHz)
                {
                    DoorUnlockSfxTimer = 0;
                    if (_player._soundSource.CountPlayingSfx(SfxId.UNLOCK_ANIM) == 0)
                    {
                        _player._soundSource.PlayFreeSfx(SfxId.UNLOCK_ANIM);
                    }
                }
            }

            if (DoorChimeSfxTimer > 0)
            {
                DoorChimeSfxTimer -= _player._scene.FrameTime;
                if (DoorChimeSfxTimer <= 1 / (float)SimTicks.LegacyHz)
                {
                    DoorChimeSfxTimer = 0;
                    if (Sfx.TimedSfxMute == 0 && (_player._scene.CameraSequences.Current == null || !_player._scene.CameraSequences.Current.BlockInput) && _player._soundSource.CountPlayingSfx(SfxId.DOOR_UNLOCK) == 0)
                    {
                        // the game doesn't check whether the cam seq blocks input
                        _player._soundSource.PlayFreeSfx(SfxId.DOOR_UNLOCK);
                    }
                }
            }

            if (ForceFieldSfxTimer > 0)
            {
                ForceFieldSfxTimer -= _player._scene.FrameTime;
                if (ForceFieldSfxTimer <= 0)
                {
                    ForceFieldSfxTimer = 0;
                    if (Sfx.TimedSfxMute == 0)
                    {
                        _timedSfxSource.PlaySfx(SfxId.GEN_OFF, recency: 5 / (float)SimTicks.LegacyHz, sourceOnly: true);
                    }
                }
            }

            if (_scrollSfxTimer > 0)
            {
                if (_scrollSfxTimer < 2 / (float)SimTicks.LegacyHz)
                {
                    _player._soundSource.StopFreeSfx(SfxId.FAST_SCROLL_UP_LOOP);
                }
                else if (_player._soundSource.CountPlayingSfx(SfxId.FAST_SCROLL_UP_LOOP) == 0)
                {
                    _player._soundSource.PlayFreeSfx(SfxId.FAST_SCROLL_UP_LOOP);
                }

                _scrollSfxTimer -= _player._scene.FrameTime;
            }
        }

        public void SetDoorChimeTimer(float value)
        {
            DoorChimeSfxTimer = value;
        }

        public void SetDoorUnlockTimer(float value)
        {
            DoorUnlockSfxTimer = value;
        }

        public void SetForceFieldSoundTimer(float value)
        {
            ForceFieldSfxTimer = value;
        }

        public void PlayPickupSound(SfxId id)
        {
            if (Sfx.TimedSfxMute == 0)
                _player._soundSource.PlayFreeSfx(id);
        }

        public void PresentMajorPickup(ItemType itemType)
        {
            // Network matches use the authoritative WorldEvent identity. The
            // local path has no event id, so its simulation frame is the
            // presentation identity and never crosses a protocol boundary.
            if (_player.IsMainPlayer
                && ClientSceneServices.PlayFor(_player._scene) == null)
                GamepadHaptics.Play(HapticEvent.MajorPickup,
                    unchecked((uint)_player.ModScene.FrameCount));
        }
    }
}
