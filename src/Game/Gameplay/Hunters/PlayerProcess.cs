using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Sound;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        internal static bool ShouldStartChargeEffect(int chargeLevel, int minCharge,
            int fullCharge, bool hasEffect, bool fullChargeEffect)
            => chargeLevel >= minCharge && chargeLevel < fullCharge
                && !hasEffect && !fullChargeEffect;

        public override bool Process()
        {
            bool result = ProcessPlayer();
            SetTransform(_facingVector, _upVector, Position);
            // Collision reads the animated rocks even when no client renders this player.
            // Previously only Draw refreshed them, leaving dedicated-server positions stale.
            if (result && LoadFlags.TestFlag(LoadFlags.Active) && Hunter == Hunter.Spire
                && Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                UpdateSpireAltAttack();
            }
            return result;
        }

        public bool ProcessPlayer()
        {
            if (!LoadFlags.TestFlag(LoadFlags.Connected) && LoadFlags.TestFlag(LoadFlags.WasConnected))
            {
                LoadFlags |= LoadFlags.Disconnected;
                LoadFlags &= ~LoadFlags.Active;
            }
            if (!LoadFlags.TestFlag(LoadFlags.Active))
            {
                Input.ClearBoostIntents();
                // Returning false here makes Scene.UpdateScene destroy the
                // entity and drop it from the entity list, and AddPlayer is
                // inert once the room has loaded -- so a slot vacated (or
                // never occupied) at load time could never be filled again.
                // That is why a peer who joined afterwards existed on the
                // roster, went active, and still never spawned: nothing was
                // calling Process on it any more.
                return _scene.IsHeadless || _scene.Services.KeepSlotAlive(this);
            }
            // todo?: something with wifi lockjaw bomb
            if (Flags2.TestFlag(PlayerFlags2.UnequipOmegaCannon))
            {
                UnequipOmegaCannon();
                Flags2 &= ~PlayerFlags2.UnequipOmegaCannon;
            }
            TryApplyPendingAutoEquip();
            if (IsBot)
            {
                if (AiData.Flags3.TestFlag(AiFlags3.Bit5))
                {
                    foreach (PlayerEntity other in _scene.GetPlayerEntities())
                    {
                        if (!other.IsBot || other == this)
                        {
                            continue;
                        }
                        _scene.SendMessage(Message.Destroyed, this, null, 0, 0, delay: 1);
                        other.AiData.Flags2 |= AiFlags2.Bit13;
                    }
                    AiData.Flags3 &= ~AiFlags3.Bit5;
                }
                if (AiData.Flags3.TestFlag(AiFlags3.Invulnerable))
                {
                    // not multiplying by 2 since this is meant to reset every frame, and run out as soon as it's not
                    _spawnInvulnTimer = 2;
                    AiData.Flags3 &= ~AiFlags3.Invulnerable;
                }
                if (AiData.Flags3.TestFlag(AiFlags3.Bit1))
                {
                    // spawnEffectMP or spawnEffect
                    int effectId = _scene.Players.ActiveCount > 2 && !_scene.Features.MaxPlayerDetail ? 33 : 31;
                    _scene.SpawnEffect(effectId, Vector3.UnitX, Vector3.UnitY, Position);
                    PlayHunterSfx(HunterSfx.Spawn);
                    AiData.Flags3 &= ~AiFlags3.Bit1;
                    AiData.Flags3 |= AiFlags3.Bit2;
                }
                else if (AiData.Flags3.TestFlag(AiFlags3.Bit2) && _curAlpha <= 1 / 31f)
                {
                    _health = 0;
                    Flags2 |= PlayerFlags2.HideModel;
                    AiData.Flags3 &= ~AiFlags3.Bit2;
                    AiData.Flags1 = false;
                    _soundSource.StopAllSfx(force: true);
                }
                if (AiData.Flags3.TestFlag(AiFlags3.Despawned))
                {
                    _scene.SendMessage(Message.Destroyed, this, null, 0, 0, delay: 1);
                    _health = 0;
                    Flags2 |= PlayerFlags2.HideModel;
                    AiData.Flags3 &= ~AiFlags3.Despawned;
                    AiData.Flags1 = false;
                    _soundSource.StopAllSfx(force: true);
                }
                Debug.Assert(LoadFlags.TestFlag(LoadFlags.Active));
            }
            // display swap update happens here for main player
            if (IsMainPlayer && _scene.CameraSequences.Current?.BlockInput == true)
            {
                Controls.ClearAll();
                Input.ClearBoostIntents();
            }
            PrevPosition = Position;
            PrevSpeed = Speed;
            Flags1 &= ~PlayerFlags1.AltFormPrevious;
            if (Flags1.TestFlag(PlayerFlags1.AltForm))
            {
                Flags1 |= PlayerFlags1.AltFormPrevious;
            }
            Flags1 &= ~PlayerFlags1.MovingBiped;
            Flags1 &= ~PlayerFlags1.ShotCharged;
            Flags1 &= ~PlayerFlags1.ShotMissile;
            Flags1 &= ~PlayerFlags1.ShotUncharged;
            _crushBits = 0;
            if (_respawnTimer > 0)
            {
                _respawnTimer--;
                if ((_scene.Match.Rules.Mode == MatchMode.Survival || _scene.Match.Rules.Mode == MatchMode.TeamSurvival)
                    && _scene.Match.TeamDeaths[SlotIndex] > _scene.Match.Rules.LegacyPointGoal)
                {
                    if (IsMainPlayer)
                    {
                        QueueHudMessage(128, 152, 1 / 1000f, 0, 243); // you lost all your lives! you're out of the game
                    }
                    if (_respawnTimer == 0)
                    {
                        _respawnTimer = 1;
                    }
                }
            }
            if (_health == 0)
            {
                if (_respawnTimer == 0)
                {
                    if (_scene.Room?.LoadEntityId >= 0)
                    {
                        TeleporterEntity? targetTeleporter = null;
                        foreach (TeleporterEntity teleporter in _scene.GetTeleporterEntities())
                        {
                            if (teleporter.Data.LoadIndex == _scene.Room.LoadEntityId)
                            {
                                targetTeleporter = teleporter;
                                break;
                            }
                        }
                        if (targetTeleporter != null)
                        {
                            targetTeleporter.SetTriggered();
                            Spawn(targetTeleporter.Position, targetTeleporter.FacingVector,
                                targetTeleporter.UpVector, targetTeleporter.NodeRef, respawn: true);
                        }
                        else
                        {
                            DoorEntity? targetDoor = null;
                            foreach (DoorEntity door in _scene.GetDoorEntities())
                            {
                                if (door.Data.OutConnectorId == _scene.Room.LoadEntityId)
                                {
                                    targetDoor = door;
                                    break;
                                }
                            }
                            if (targetDoor != null)
                            {
                                Vector3 facing = targetDoor.FacingVector;
                                Vector3 position = targetDoor.Position + facing * 2;
                                Spawn(position, facing, targetDoor.UpVector, targetDoor.NodeRef, respawn: true);
                            }
                        }
                        _scene.Room.LoadEntityId = -1;
                    }
                    else
                    {
                        int time = GetTimeUntilRespawn();
                        if (IsMainPlayer) // todo: and some global is not set
                        {
                            // press FIRE to begin / press FIRE to respawn
                            int messageId = _scene.CameraSequences.Current?.IsIntro == true ? 245 : 244;
                            if (!_scene.Features.Bugfixes.NoStrayRespawnText || time > 0
                                || _scene.Match.Rules.Mode != MatchMode.Survival && _scene.Match.Rules.Mode != MatchMode.TeamSurvival)
                            {
                                QueueHudMessage(128, 162, 1 / 1000f, 0, messageId);
                                if (time < SimTicks.From30HzFrames(150))
                                {
                                    string message = Text.Strings.GetHudMessage(246); // SPAWNING IN %d...
                                    int seconds = (time + SimTicks.Hz) / SimTicks.Hz;
                                    QueueHudMessage(128, 152, 1 / 1000f, 0, message.Replace("%d", seconds.ToString()));
                                }
                            }
                        }
                        if (!_scene.Services.IsReplica
                            && (Controls.Shoot.IsDown || time <= 0 || IsBot
                            || _scene.Services.ForceSpawn(this))) // todo: or forced
                        {
                            // todo?: something with wi-fi
                            // else...
                            PlayerSpawnEntity? respawn = GetRespawnPoint();
                            if (respawn != null)
                            {
                                Vector3 position = ForcedSpawnPos ?? respawn.Position;
                                Spawn(position, respawn.FacingVector, respawn.UpVector, respawn.NodeRef, respawn: true);
                            }
                        }
                    }
                }
            }
            _volume = CollisionVolume.Move(_volumeUnxf, Position);
            if (IsMainPlayer && !IsAltForm)
            {
                _soundSource.Update(Position, rangeIndex: -1);
            }
            else
            {
                int rangeIndex = 1;
                _soundSource.Update(Position, rangeIndex);
            }
            if (IsMainPlayer)
            {
                UpdateHealthSfx(_health);
            }
            else
            {
                UpdateNodeRefVolume();
            }
            if (_damageInvulnTimer > 0)
            {
                _damageInvulnTimer--;
            }
            if (_spawnInvulnTimer > 0)
            {
                _spawnInvulnTimer--;
            }
            if (_disruptedTimer > 0)
            {
                _disruptedTimer--;
            }
            if (!_scene.Services.IsReplica && (_scene.Match.Rules.Mode == MatchMode.Survival || _scene.Match.Rules.Mode == MatchMode.TeamSurvival))
            {
                if (Flags2.TestFlag(PlayerFlags2.RadarReveal))
                {
                    Flags2 |= PlayerFlags2.RadarRevealPrevious;
                }
                Flags2 &= ~PlayerFlags2.RadarReveal;
                if (_health == 0)
                {
                    _hidingTimer = 0;
                }
                else
                {
                    if (_scene.Match.RadarPlayers)
                    {
                        if (IsMainPlayer)
                        {
                            QueueHudMessage(128, 170, 1 / 1000f, 0, 247); // FACE OFF!
                        }
                        _hidingTimer = 0;
                    }
                    else
                    {
                        int revealTime = SimTicks.From30HzFrames(_scene.Players.ActiveCount > 2 ? 600 : 300);
                        Vector3 moved = Position - IdlePosition;
                        if (moved.LengthSquared >= 25)
                        {
                            // todo: FPS stuff
                            _hidingTimer = (ushort)(_hidingTimer > SimTicks.From30HzFrames(35) ? _hidingTimer - SimTicks.From30HzFrames(35) : 0);
                            if (_hidingTimer < revealTime && _hidingTimer > revealTime - SimTicks.From30HzFrames(35))
                            {
                                // give the player at least a second before they're revealed again
                                _hidingTimer = (ushort)(revealTime - SimTicks.From30HzFrames(35));
                            }
                        }
                        if (_hidingTimer < revealTime + SimTicks.From30HzFrames(150))
                        {
                            _hidingTimer++;
                        }
                        if (_hidingTimer >= revealTime)
                        {
                            Flags2 |= PlayerFlags2.RadarReveal;
                            if (IsMainPlayer && (_scene.FrameCount & (8 * 2)) == 0) // todo: FPS stuff
                            {
                                QueueHudMessage(128, 150, 1 / 1000f, 0, 248); // position revealed!
                                QueueHudMessage(128, 160, 1 / 1000f, 0, 249); // RETURN TO BATTLE!
                            }
                        }
                    }
                }
            }
            if (_timeSinceHitTarget != UInt16.MaxValue && ++_timeSinceHitTarget > 1)
            {
                _shockCoilTimer = 0;
                _shockCoilTarget = null;
                if (_timeSinceHitTarget >= SimTicks.From30HzFrames(210))
                {
                    _lastTarget = null;
                }
            }
            if (_timeSinceShot != UInt16.MaxValue)
            {
                _timeSinceShot++;
            }
            if (_altAttackCooldown > 0)
            {
                _altAttackCooldown--;
            }
            if (_jumpPadControlLock > 0)
            {
                _jumpPadControlLock--;
            }
            if (_jumpPadControlLock == 0)
            {
                _lastJumpPad = null;
            }
            if (_jumpPadControlLockMin > 0)
            {
                _jumpPadControlLockMin--;
            }
            if (_timeSinceJumpPad != UInt16.MaxValue)
            {
                _timeSinceJumpPad++;
            }
            if (Hunter == Hunter.Samus)
            {
                if (_bombRefillTimer > 0)
                {
                    _bombRefillTimer--;
                }
                else
                {
                    _bombAmmo = 3;
                }
            }
            else if (Hunter == Hunter.Sylux)
            {
                ValidateLockjawBombState();
                if (_bombCooldown > 0)
                {
                    _bombAmmo = 0;
                }
                else
                {
                    _bombAmmo = (byte)(3 - SyluxBombCount);
                }
            }
            else
            {
                _bombAmmo = 1;
            }
            if (_bombCooldown > 0)
            {
                _bombCooldown--;
                if (Hunter == Hunter.Kanden && _bombCooldown == SimTicks.From30HzFrames(10))
                {
                    _altModel.SetAnimation((int)KandenAltAnim.TailIn, AnimFlags.NoLoop);
                }
            }
            // todo?: FH leftover ammo recharge stuff
            if (_timeSinceMorphCamera != UInt16.MaxValue)
            {
                _timeSinceMorphCamera++;
            }
            if (Hunter == Hunter.Sylux && _bombOveruse > 0)
            {
                _bombOveruse--;
            }
            if (_deathaltTimer > 0)
            {
                _deathaltTimer--;
                if (_deathaltEffect == null)
                {
                    int effectId = 181; // deathBall
                    _deathaltEffect = _scene.SpawnEffectGetEntry(effectId, Vector3.UnitX, Vector3.UnitY, _volume.SpherePosition);
                    _deathaltEffect?.SetElementExtension(true);
                }
                else
                {
                    _deathaltEffect.Transform(Vector3.UnitY, Vector3.UnitX, _volume.SpherePosition);
                }
            }
            else if (_deathaltEffect != null)
            {
                _scene.UnlinkEffectEntry(_deathaltEffect);
                _deathaltEffect = null;
            }
            if (Flags2.TestFlag(PlayerFlags2.Cloaking))
            {
                Debug.Assert(_cloakTimer != 0);
                _cloakTimer--;
                if (_cloakTimer > 0)
                {
                    _targetAlpha = 3 / 31f;
                    if (IsMainPlayer)
                    {
                        if (_cloakTimer == SimTicks.From30HzFrames(210))
                        {
                            UpdateCloakSfx(index: 1, play: true);
                        }
                        else if (_cloakTimer == SimTicks.From30HzFrames(120))
                        {
                            UpdateCloakSfx(index: 2, play: true);
                        }
                    }
                }
                else
                {
                    Flags2 &= ~PlayerFlags2.Cloaking;
                    _targetAlpha = 1;
                    _soundSource.PlayFreeSfx(SfxId.CLOAK_OFF);
                    if (IsMainPlayer)
                    {
                        UpdateCloakSfx(index: 0, play: false);
                    }
                }
            }
            else
            {
                _targetAlpha = 1;
                if ((Hunter == Hunter.Trace || IsPrimeHunter) && _hSpeedMag < 0.05f && Speed.Y < 0.05f && Speed.Y > -0.05f)
                {
                    if (_cloakTimer >= SimTicks.From30HzFrames(30))
                    {
                        if (Hunter == Hunter.Trace && IsAltForm)
                        {
                            // todo: set to 5/31 if recent touch input
                            _targetAlpha = 1 / 31f;
                        }
                        else if (CurrentWeapon == BeamType.Imperialist)
                        {
                            _targetAlpha = 5 / 31f;
                        }
                    }
                    else
                    {
                        _cloakTimer++;
                    }
                }
                else
                {
                    _cloakTimer = 0;
                }
            }
            if (IsBot && AiData.Flags3.TestFlag(AiFlags3.Bit2))
            {
                _targetAlpha = 0;
            }
            if (_health > 0)
            {
                if (Flags2.TestFlag(PlayerFlags2.Cloaking) || !Flags2.TestFlag(PlayerFlags2.AltAttack))
                {
                    if (_curAlpha < _targetAlpha)
                    {
                        _curAlpha += 2 / 31f / 2; // todo: FPS stuff
                        if (_curAlpha > _targetAlpha)
                        {
                            _curAlpha = _targetAlpha;
                        }
                    }
                    else if (_curAlpha > _targetAlpha)
                    {
                        _curAlpha -= 1 / 31f / 2; // todo: FPS stuff
                        if (_curAlpha < _targetAlpha)
                        {
                            _curAlpha = _targetAlpha;
                        }
                    }
                }
                else
                {
                    _cloakTimer = 0;
                    _curAlpha = 1;
                    _targetAlpha = 1;
                }
            }
            else if (IsAltForm || IsMorphing)
            {
                _curAlpha -= 2 / 31f / 2; // todo FPS stuff
                if (_curAlpha < 0)
                {
                    _curAlpha = 0;
                }
            }
            int ammo = EquipInfo.Ammo;
            if (ammo >= 0 && ammo < EquipInfo.Weapon.AmmoCost)
            {
                int slot = 0;
                int priority = 0;
                for (int i = 0; i < 3; i++)
                {
                    BeamType slotWeap = _weaponSlots[i];
                    if (slotWeap != BeamType.None)
                    {
                        WeaponInfo slotInfo = Weapons.Current[(int)slotWeap];
                        if (slotInfo.Priority > priority && _ammo[slotInfo.AmmoType] >= slotInfo.AmmoCost)
                        {
                            priority = slotInfo.Priority;
                            slot = i;
                        }
                    }
                }
                if (IsMainPlayer)
                {
                    ShowNoAmmoMessage();
                }
                TryEquipWeapon(_weaponSlots[slot]);
            }
            ProcessInput();
            if (Flags1.TestFlag(PlayerFlags1.Boosting) && _hSpeedMag <= Fixed.ToFloat(Values.AltMinHSpeed))
            {
                Flags1 &= ~PlayerFlags1.Boosting;
            }
            if (Flags1.TestFlag(PlayerFlags1.Walking))
            {
                _gunViewBob += 14 / 2f; // todo: FPS stuff
                if (_gunViewBob > 450)
                {
                    _gunViewBob -= 180;
                }
                if (_walkViewBob < Fixed.ToFloat(Values.WalkBobMax))
                {
                    _walkViewBob += 1 / 2f; // todo: FPS stuff
                    if (_walkViewBob > Fixed.ToFloat(Values.WalkBobMax))
                    {
                        _walkViewBob = Fixed.ToFloat(Values.WalkBobMax);
                    }
                }
            }
            else
            {
                if (_walkViewBob > 0)
                {
                    _walkViewBob -= Fixed.ToFloat(Values.WalkBobMax) / 32 / 2f; // todo: FPS stuff
                    if (_walkViewBob < 0)
                    {
                        _walkViewBob = 0;
                    }
                }
                if (_gunViewBob >= 360)
                {
                    _gunViewBob += 14 / 2f; // todo: FPS stuff
                    if (_gunViewBob > 450)
                    {
                        _gunViewBob = 450;
                    }
                }
                else
                {
                    _gunViewBob -= 14 / 2f; // todo: FPS stuff
                    if (_gunViewBob < 270)
                    {
                        _gunViewBob = 270;
                    }
                }
            }
            // todo: FPS stuff
            if (_healthRecovery > 0)
            {
                if (_tickedHealthRecovery)
                {
                    _tickedHealthRecovery = false;
                }
                else
                {
                    int previousRecoveryHealth = _health;
                    if (_healthRecovery <= 3)
                    {
                        _health += _healthRecovery;
                        _healthRecovery = 0;
                    }
                    else
                    {
                        _health += 3;
                        _healthRecovery -= 3;
                        PlayScrollPresentation();
                    }
                    if (_health > _healthMax)
                    {
                        _health = _healthMax;
                    }
                    _scene.Services.Combat?.NoteHealing(this, _health - previousRecoveryHealth);
                }
            }
            else
            {
                _tickedHealthRecovery = false;
            }
            for (int i = 0; i < 2; i++)
            {
                if (_ammoRecovery[i] > 0)
                {
                    if (_tickedAmmoRecovery[i])
                    {
                        _tickedAmmoRecovery[i] = false;
                    }
                    else
                    {
                        if (_ammoRecovery[i] <= 3)
                        {
                            // the game stops the scroll SFX here, which is unnecessary
                            _ammo[i] += _ammoRecovery[i];
                            _ammoRecovery[i] = 0;
                        }
                        else
                        {
                            _ammo[i] += 3;
                            _ammoRecovery[i] -= 3;
                            PlayScrollPresentation();
                        }
                        if (_ammo[i] > _ammoMax[i])
                        {
                            _ammo[i] = _ammoMax[i];
                        }
                    }
                }
                else
                {
                    _tickedAmmoRecovery[i] = false;
                }
            }
            if (_health > 0)
            {
                if (Flags1.TestFlag(PlayerFlags1.Grounded) && !Flags1.TestFlag(PlayerFlags1.GroundedPrevious))
                {
                    PlayLandingSfx();
                }
                if (IsAltForm)
                {
                    UpdateAltMovementSfx();
                }
            }
            UpdateGunAnimation();
            // Gun material, texture, and texcoord tracks advance at the
            // authored 30 Hz cadence. Shock Coil enters its held shot state
            // once in UpdateGunAnimation and is intentionally not restarted.
            if (_scene.FrameCount != 0 && _scene.FrameCount % 2 == 0) // todo: FPS stuff
            {
                _gunModel.UpdateAnimFrames();
            }
            if (IsMainPlayer && _gunModel.AnimInfo.Frame[0] == 15 && _scene.FrameCount % 2 == 0 // todo: FPS stuff
                && (GunAnimation == GunAnimation.Unknown9 || GunAnimation == GunAnimation.MissileShot))
            {
                OpenMissilePresentation();
            }
            PickUpItems();
            if (!IsAltForm)
            {
                UpdateAimVecs();
            }
            if (_muzzleEffect != null)
            {
                int id = Metadata.MuzzleEffectIds[(int)BeamType.ShockCoil];
                if (_muzzleEffect.IsFinished || _muzzleEffect.EffectId == id && !Flags1.TestFlag(PlayerFlags1.ShotUncharged))
                {
                    _scene.UnlinkEffectEntry(_muzzleEffect);
                    _muzzleEffect = null;
                }
                else if (IsMainPlayer)
                {
                    _muzzleEffect.Transform(_gunVec2, _gunVec1, _muzzlePos);
                }
            }
            if (EquipInfo.ChargeLevel < SimTicks.From30HzFrames(EquipInfo.Weapon.MinCharge))
            {
                if (_chargeEffect != null)
                {
                    _scene.UnlinkEffectEntry(_chargeEffect);
                    _chargeEffect = null;
                }
                Flags2 &= ~PlayerFlags2.ChargeEffect;
            }
            else
            {
                if (ShouldStartChargeEffect(EquipInfo.ChargeLevel,
                    SimTicks.From30HzFrames(EquipInfo.Weapon.MinCharge),
                    SimTicks.From30HzFrames(EquipInfo.Weapon.FullCharge),
                    _chargeEffect != null, Flags2.TestFlag(PlayerFlags2.ChargeEffect)))
                {
                    int effectId = Metadata.ChargeEffectIds[(int)CurrentWeapon];
                    _chargeEffect = _scene.SpawnEffectGetEntry(effectId, _gunVec2, _gunVec1, _muzzlePos);
                    Flags2 &= ~PlayerFlags2.ChargeEffect;
                }
                else if (EquipInfo.ChargeLevel >= SimTicks.From30HzFrames(EquipInfo.Weapon.FullCharge))
                {
                    if (!Flags2.TestFlag(PlayerFlags2.ChargeEffect))
                    {
                        if (_chargeEffect != null)
                        {
                            _scene.UnlinkEffectEntry(_chargeEffect);
                            _chargeEffect = null;
                        }
                        int effectId = Metadata.ChargeLoopEffectIds[(int)CurrentWeapon];
                        _chargeEffect = _scene.SpawnEffectGetEntry(effectId, _gunVec2, _gunVec1, _muzzlePos);
                        if (_chargeEffect != null)
                        {
                            _chargeEffect.SetElementExtension(true);
                            Flags2 |= PlayerFlags2.ChargeEffect;
                        }
                    }
                    // Snapshot-replicated enemy charge is presentation state,
                    // not permission to shake this client's camera.
                    if (IsMainPlayer)
                    {
                        CameraInfo.SetShake(0.023f);
                    }
                }
                if (_chargeEffect != null)
                {
                    _chargeEffect.Transform(_gunVec2, _gunVec1, _muzzlePos);
                }
            }
            if (_frozenTimer == 0)
            {
                UpdateAnimFrames(_bipedModel1);
                UpdateAnimFrames(_bipedModel2);
            }
            if ((IsAltForm || IsMorphing) && _frozenTimer == 0)
            {
                UpdateAnimFrames(_altModel);
            }
            if (_boostEffect != null)
            {
                _boostEffect.Transform(_gunVec2, _facingVector, Position);
                if (!IsAltForm && !IsMorphing)
                {
                    _scene.UnlinkEffectEntry(_boostEffect);
                    _boostEffect = null;
                }
                else if (!Flags1.TestFlag(PlayerFlags1.Boosting))
                {
                    if (_boostEffect.IsFinished)
                    {
                        _scene.UnlinkEffectEntry(_boostEffect);
                        _boostEffect = null;
                    }
                    else
                    {
                        _boostEffect.SetElementExtension(false);
                    }
                }
            }
            if (_furlEffect != null)
            {
                _furlEffect.Transform(_gunVec2, _facingVector, Position);
                if (!IsAltForm && !IsMorphing || _furlEffect.IsFinished)
                {
                    _scene.UnlinkEffectEntry(_furlEffect);
                    _furlEffect = null;
                }
            }
            if (Hunter == Hunter.Samus)
            {
                UpdateMorphBallTrail();
            }
            if (_timeSinceDamage != UInt16.MaxValue)
            {
                _timeSinceDamage++;
            }
            if (_timeSincePickup != UInt16.MaxValue)
            {
                _timeSincePickup++;
            }
            if (_timeSinceHeal != UInt16.MaxValue)
            {
                _timeSinceHeal++;
            }
            if (!Flags1.TestFlag(PlayerFlags1.Standing) && _timeSinceStanding != UInt16.MaxValue)
            {
                _timeSinceStanding++;
            }
            if (_field449 != UInt16.MaxValue)
            {
                _field449++;
            }
            if (Input.HasInput || IsBot && !AiData.Flags3.TestFlag(AiFlags3.NoInput))
            {
                _timeSinceInput = 0;
            }
            else
            {
                _timeSinceInput++;
            }
            if (_aimY < 60 && _aimY > -60 && !EquipInfo.Zoomed && _health > 0 && !_scene.Features.NoIdleSway)
            {
                int swayStart = SimTicks.From30HzFrames(Values.SwayStartTime);
                if (_scene.Features.DelayedIdleSway)
                {
                    swayStart *= 4;
                }
                if (_timeSinceInput == swayStart)
                {
                    _field40C = 0;
                    float factor1 = (_scene.Random.GetRandomInt2(Values.SwayLimit) - Values.SwayLimit / 2) / 4096f;
                    float factor2 = (_scene.Random.GetRandomInt2(Values.SwayLimit) - Values.SwayLimit / 2) / 4096f;
                    _field410 = _facingVector;
                    _field41C = _field410;
                    _field428 = _field410;
                    _field41C += _gunVec2 * factor1 + _upVector * factor2;
                }
                else if (_timeSinceInput > swayStart)
                {
                    _field40C += 1f / Values.SwayIncrement / 2; // todo: FPS stuff
                    if (_field40C >= 1)
                    {
                        _field40C = 0;
                        float factor1 = (_scene.Random.GetRandomInt2(Values.SwayLimit) - Values.SwayLimit / 2) / 4096f;
                        float factor2 = (_scene.Random.GetRandomInt2(Values.SwayLimit) - Values.SwayLimit / 2) / 4096f;
                        _field410 = _field41C;
                        _field41C = _field428;
                        _field41C += _gunVec2 * factor1 + _upVector * factor2;
                    }
                    float angle = 180 * _field40C + 180;
                    float factor = (MathF.Cos(MathHelper.DegreesToRadians(angle)) + 1) / 2;
                    _facingVector = _field410 + (_field41C - _field410) * factor;
                    _facingVector = _facingVector.Normalized();
                }
            }
            if (EquipInfo.SmokeLevel < EquipInfo.Weapon.SmokeStart * 2) // todo: FPS stuff
            {
                if (EquipInfo.SmokeLevel > EquipInfo.Weapon.SmokeMinimum * 2) // todo: FPS stuff
                {
                    if (Flags1.TestFlag(PlayerFlags1.DrawGunSmoke) && _smokeAlpha < 1)
                    {
                        _smokeAlpha = Math.Min(_smokeAlpha + 1 / 31f / 2, 1); // todo: FPS stuff
                    }
                }
                else if (_smokeAlpha > 0)
                {
                    _smokeAlpha = Math.Max(_smokeAlpha - 1 / 31f / 2, 0); // todo: FPS stuff
                }
                else if (Flags1.TestFlag(PlayerFlags1.DrawGunSmoke))
                {
                    EquipInfo.SmokeLevel = 0;
                    Flags1 &= ~PlayerFlags1.DrawGunSmoke;
                }
            }
            else
            {
                if (!Flags1.TestFlag(PlayerFlags1.DrawGunSmoke))
                {
                    Flags1 |= PlayerFlags1.DrawGunSmoke;
                    if (!_scene.IsHeadless)
                    {
                        _gunSmokeModel.SetAnimation(0);
                    }
                }
                if (_smokeAlpha < 1)
                {
                    _smokeAlpha = Math.Min(_smokeAlpha + 1 / 31f / 2, 1); // todo: FPS stuff
                }
            }
            if (!_scene.IsHeadless && Flags1.TestFlag(PlayerFlags1.DrawGunSmoke))
            {
                UpdateAnimFrames(_gunSmokeModel);
            }
            if (_health == 0)
            {
                if (_timeSinceDead != UInt16.MaxValue)
                {
                    _timeSinceDead++;
                }
                if (_respawnTimer <= 1)
                {
                    Flags2 |= PlayerFlags2.HideModel;
                }
            }
            if (!EquipInfo.Zoomed && _scene.CameraSequences.Current == null)
            {
                // note: the game does this during cam seqs, resulting in the FOV thrashing a bit, but it has no visible effect
                // since the sin/cos values for projection are set aside in the cam info update that's already occurred above.
                float normalFov = Fixed.ToFloat(Values.NormalFov) * 2;
                CameraInfo.Fov = ZoomFovTransition.StepBackToNormal(
                    CameraInfo.Fov, normalFov);
            }
            if (_bipedModel2.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
            {
                if (IsMorphing)
                {
                    Flags1 &= ~PlayerFlags1.Morphing;
                    UpdateForm(altForm: true);
                }
                else if (IsUnmorphing)
                {
                    if (IsMainPlayer && _scene.CameraSequences.Current != null)
                    {
                        _scene.CameraSequences.Current.InitialCamInfo.NodeRef = NodeRef;
                    }
                    else
                    {
                        CameraInfo.NodeRef = NodeRef;
                    }
                    Flags1 &= ~PlayerFlags1.Unmorphing;
                    if (_burnTimer > 0)
                    {
                        CreateBurnEffect();
                    }
                }
            }
            UpdateLightSources(_volume.SpherePosition);
            // todo?: if wifi and not main player
            // else...
            if (NodeRef != NodeRef.None)
            {
                int index = Flags1.TestFlag(PlayerFlags1.AltFormPrevious) ? 2 : 0;
                Vector3 prevPos = PrevPosition + PlayerVolumes[(int)Hunter, index].SpherePosition;
                index = IsAltForm ? 2 : 0;
                Vector3 curPos = Position + PlayerVolumes[(int)Hunter, index].SpherePosition;
                // Upstream's one-hop test, for every player including this
                // one. **Do not put an absolute position lookup in front of
                // it.**
                //
                // That was tried, to cure a local player's NodeRef freezing at
                // its spawn value: GetNodeRefByPosition was preferred and the
                // hop kept only as a fallback. It cost far more than it
                // bought. That lookup bounds a room part by the planes of the
                // portals opening out of it and nothing else, so a part with
                // two portals is an unbounded wedge, several parts' wedges
                // overlap, and it returns a confidently wrong part far more
                // readily than it returns none. On Elder Passage
                // (MP4 HIGHGROUND - EXPANDED) it put the player in the
                // four-node hall "rmHallB" while they stood in the open valley
                // that all ten of that room's spawn points are authored into,
                // and RoomEntity.UpdateRoomParts walks the portal graph from
                // there -- so the room drew as a black screen with the gun and
                // a few floating item pickups in it, from eight of the ten
                // spawns, on every platform and in local play as much as
                // online. Measured with `-maptest ROOM -renderprobe`: 8.7-19%
                // of the frame lit with the lookup in front, 94-99.9% with it
                // gone.
                //
                // The lookup is not sound enough to override the hop, and it
                // is not sound enough to be a "have I left this part" check
                // either -- asked about the correct part 7 at seven of those
                // ten spawns, it says no. A remote puppet still uses it
                // (PlayerEntityNetAim.ModRefreshNodeRef) because a puppet's
                // position is written in rather than walked, so the hop cannot
                // work there at all and a wrong part costs only that one
                // player's visibility; here it costs the whole room. Curing
                // the freeze needs a real point-in-node test against the node
                // data, not this one.
                NodeRef = _scene.UpdateNodeRef(NodeRef, prevPos, curPos);
                if (_scene.CameraSequences.Current == null || !IsMainPlayer)
                {
                    if (CameraType == CameraType.Free)
                    {
                        CameraInfo.NodeRef = _scene.UpdateNodeRef(CameraInfo.NodeRef, CameraInfo.PrevPosition, CameraInfo.Position);
                    }
                    else if (CameraType != CameraType.Spectator)
                    {
                        CameraInfo.NodeRef = _scene.UpdateNodeRef(NodeRef, curPos, CameraInfo.Position);
                    }
                }
                // todo?: something if wifi
            }
            if (Flags1.TestFlag(PlayerFlags1.Standing) && (_timeStanding == 0 || _scene.FrameCount % (4 * 2) == 0) // todo: FPS stuff
                && (Flags1.TestFlag(PlayerFlags1.OnAcid) || (Flags1.TestFlag(PlayerFlags1.OnLava) && Hunter != Hunter.Spire)))
            {
                DamageFlags flags = DamageFlags.IgnoreInvuln;
                if (Flags1.TestFlag(PlayerFlags1.OnLava))
                {
                    flags |= DamageFlags.NoSfx;
                }
                TakeDamage(1, flags, direction: null, source: null);
            }
            Debug.Assert(_scene.Room != null);
            if (_scene.Room.Meta.HasLimits)
            {
                if (Position.Y < _scene.Room.Meta.PlayerMin.Y)
                {
                    TakeDamage(0, DamageFlags.Death, direction: null, source: null);
                }
                Position = Vector3.Clamp(Position, _scene.Room.Meta.PlayerMin.WithY(Position.Y), _scene.Room.Meta.PlayerMax);
            }
            if (Position.Y < _scene.Room.Meta.KillHeight)
            {
                TakeDamage(0, DamageFlags.Death, direction: null, source: null);
            }
            // todo: update license stats
            Flags2 &= ~PlayerFlags2.NoFormSwitch;
            if (_doubleDmgTimer > 0)
            {
                _doubleDmgTimer--;
                if (IsMainPlayer)
                {
                    // the game checks this last, so it might create and destroy the effect on the same frame
                    if (_doubleDmgTimer == 0)
                    {
                        UpdateDoubleDamageSfx(index: 0, play: false);
                        if (_doubleDmgEffect != null)
                        {
                            _scene.UnlinkEffectEntry(_doubleDmgEffect);
                            _doubleDmgEffect = null;
                        }
                    }
                    else
                    {
                        if (!IsAltForm && !IsMorphing && !IsUnmorphing)
                        {
                            if (_doubleDmgEffect != null)
                            {
                                _doubleDmgEffect.Transform(_upVector, _gunVec1, _muzzlePos);
                            }
                            else
                            {
                                _doubleDmgEffect = _scene.SpawnEffectGetEntry(244, _upVector, _gunVec1, _muzzlePos); // doubleDamageGun
                                _doubleDmgEffect?.SetElementExtension(true);
                            }
                        }
                        else if (_doubleDmgEffect != null)
                        {
                            _scene.UnlinkEffectEntry(_doubleDmgEffect);
                            _doubleDmgEffect = null;
                        }
                        if (_doubleDmgTimer == SimTicks.From30HzFrames(210))
                        {
                            UpdateDoubleDamageSfx(index: 1, play: true);
                            UpdateDoubleDamageSpeed(2);
                        }
                        else if (_doubleDmgTimer == SimTicks.From30HzFrames(120))
                        {
                            UpdateDoubleDamageSfx(index: 2, play: true);
                            UpdateDoubleDamageSpeed(3);
                        }
                    }
                }
            }
            if (_burnTimer > 0)
            {
                _burnTimer--;
                if (_burnTimer % SimTicks.From30HzFrames(8) == 0)
                {
                    TakeDamage(1, DamageFlags.NoSfx | DamageFlags.Burn | DamageFlags.NoDmgInvuln, direction: null, _burnedBy);
                }
                if (_burnEffect != null)
                {
                    if (_scene.CameraSequences.Current?.BlockInput == true)
                    {
                        _scene.UnlinkEffectEntry(_burnEffect);
                        _burnEffect = null;
                    }
                    else if (!IsMainPlayer || IsAltForm || IsMorphing)
                    {
                        var facing = new Vector3(_field70, 0, _field74);
                        _burnEffect.Transform(facing, Vector3.UnitY, _volume.SpherePosition);
                    }
                    else
                    {
                        _burnEffect.Transform(_upVector, _gunVec1, _muzzlePos);
                    }
                }
            }
            else if (_burnEffect != null)
            {
                _scene.UnlinkEffectEntry(_burnEffect);
                _burnEffect = null;
            }
            // todo?: something for wifi
            return true;
        }

        public void ActivateJumpPad(JumpPadEntity jumpPad, Vector3 vector, ushort lockTime)
        {
            if (_timeSinceJumpPad > SimTicks.From30HzFrames(5))
            {
                _soundSource.PlaySfx(SfxId.JUMP_PAD);
            }
            Speed = vector;
            _jumpPadAccel = vector;
            _lastJumpPad = jumpPad;
            Flags1 |= PlayerFlags1.UsedJumpPad;
            lockTime = (ushort)SimTicks.From30HzFrames(lockTime);
            _jumpPadControlLock = lockTime;
            _jumpPadControlLockMin = Math.Max(lockTime, (ushort)SimTicks.From30HzFrames(5));
            _timeSinceJumpPad = 0;
            Flags1 &= ~PlayerFlags1.UsedJump;
            Flags1 |= PlayerFlags1.Standing;
            if (IsAltForm)
            {
                float accelY = _jumpPadAccel.Y;
                float altGrav = Fixed.ToFloat(Values.AltAirGravity);
                float bipedGrav = Fixed.ToFloat(Values.BipedGravity);
                float altFactor = -accelY / altGrav;
                float bipedFactor = -accelY / bipedGrav;
                float lockInc = ((accelY * bipedFactor) + (bipedGrav * (bipedFactor * bipedFactor) / 2)
                    - ((accelY * altFactor) + (altGrav * (altFactor * altFactor) / 2)))
                    / accelY + 2;
                _jumpPadControlLock += (ushort)(lockInc * SimTicks.TicksPer30HzFrame);
            }
        }

        private void PickUpItems()
        {
            if (_scene.Services.IsReplica) { return; }
            if (_health == 0 || IgnoreItemPickups
                || IsMainPlayer && _scene.CameraSequences.Current?.BlockInput == true)
            {
                return;
            }
            // todo: visualize
            float distSqr = _volume.SphereRadius + 0.45f;
            distSqr *= distSqr;
            foreach (ItemInstanceEntity item in _scene.GetItemInstanceEntities())
            {
                bool inRange = false;
                if (IsAltForm)
                {
                    Vector3 between = item.Position - _volume.SpherePosition;
                    if (Vector3.Dot(between, between) < distSqr)
                    {
                        inRange = true;
                    }
                }
                else
                {
                    Vector3 between = item.Position - Position;
                    Vector3 lateral = between.WithY(0);
                    if (Vector3.Dot(lateral, lateral) < distSqr
                        && between.Y >= Fixed.ToFloat(Values.MinPickupHeight)
                        && between.Y <= Fixed.ToFloat(Values.MaxPickupHeight))
                    {
                        inRange = true;
                    }
                }
                if (!inRange)
                {
                    continue;
                }

                void PlaySfx(SfxId sfx)
                {
                    if (IsMainPlayer)
                    {
                        PlayPickupSound(sfx);
                    }
                }

                bool pickedUp = false;
                switch (item.ItemType)
                {
                case ItemType.HealthMedium:
                case ItemType.HealthSmall:
                case ItemType.HealthBig:
                    if (!IsPrimeHunter)
                    {
                        pickedUp = true;
                        _timeSinceHeal = 0;
                        GainHealth(_healthPickupAmounts[(int)item.ItemType]);
                        PlaySfx(item.ItemType == ItemType.HealthSmall ? SfxId.POWER_UP1 : SfxId.POWER_UP2);
                    }
                    break;
                case ItemType.UASmall:
                case ItemType.UABig:
                case ItemType.MissileSmall:
                case ItemType.MissileBig:
                    pickedUp = true;
                    int slot = item.ItemType == ItemType.UASmall || item.ItemType == ItemType.UABig ? 0 : 1;
                    _timeSincePickup = 0;
                    int amount;
                    if (item.ItemType == ItemType.UABig || item.ItemType == ItemType.MissileBig)
                    {
                        amount = 100;
                        PlaySfx(SfxId.AMMO_POWER_UP2);
                    }
                    else
                    {
                        amount = 50;
                        PlaySfx(SfxId.AMMO_POWER_UP1);
                    }
                    _ammo[slot] += amount;
                    if (_ammo[slot] > _ammoMax[slot])
                    {
                        _ammo[slot] = _ammoMax[slot];
                    }
                    // todo: update story save
                    break;
                case ItemType.VoltDriver:
                case ItemType.Battlehammer:
                case ItemType.Imperialist:
                case ItemType.Judicator:
                case ItemType.Magmaul:
                case ItemType.ShockCoil:
                case ItemType.OmegaCannon:
                case ItemType.AffinityWeapon:
                    pickedUp = true;
                    PickUpWeapon(item.ItemType);
                    if (IsMainPlayer) PresentMajorPickup(item.ItemType);
                    break;
                case ItemType.DoubleDamage:
                    pickedUp = true;
                    _timeSincePickup = 0;
                    _doubleDmgTimer = (ushort)SimTicks.From30HzFrames(900);
                    if (IsMainPlayer)
                    {
                        PresentMajorPickup(item.ItemType);
                        _soundSource.PlayFreeSfx(SfxId.DOUBLE_DAMAGE_POWER_UP);
                        UpdateDoubleDamageSfx(index: 0, play: true);
                        UpdateDoubleDamageSpeed(1);
                    }
                    break;
                case ItemType.Cloak:
                    pickedUp = true;
                    _timeSincePickup = 0;
                    _cloakTimer = (ushort)SimTicks.From30HzFrames(900);
                    Flags2 |= PlayerFlags2.Cloaking;
                    if (IsMainPlayer)
                    {
                        PresentMajorPickup(item.ItemType);
                        _soundSource.PlayFreeSfx(SfxId.CLOAK_POWER_UP);
                        UpdateCloakSfx(index: 0, play: true);
                    }
                    break;
                case ItemType.Deathalt:
                    pickedUp = true;
                    _timeSincePickup = 0;
                    _deathaltTimer = (ushort)SimTicks.From30HzFrames(900);
                    if (!IsAltForm && !IsMorphing)
                    {
                        TrySwitchForms(force: true);
                    }
                    if (IsMainPlayer)
                    {
                        PresentMajorPickup(item.ItemType);
                        _soundSource.PlayFreeSfx(SfxId.DOUBLE_DAMAGE_POWER_UP);
                    }
                    break;
                case ItemType.ArtifactKey:
                    pickedUp = true;
                    if (IsMainPlayer)
                    {
                        _soundSource.PlayFreeSfx(SfxId.KEY_PICKUP);
                    }
                    break;
                default:
                    pickedUp = true;
                    break;
                }
                if (pickedUp)
                {
                        item.OnPickedUp(this);
                }
            }
        }

        private void PickUpWeapon(ItemType itemType)
        {
            BeamType weapon;
            bool affinityPickup = itemType == ItemType.AffinityWeapon;
            bool affinityGrantsMissileAmmo = false;
            if (itemType == ItemType.AffinityWeapon)
            {
                if (Hunter == Hunter.Samus || Hunter == Hunter.Guardian) // game doesn't check for Guardian
                {
                    affinityGrantsMissileAmmo = true;
                }
                weapon = Weapons.GetAffinityBeam(Hunter);
            }
            else
            {
                weapon = itemType switch
                {
                    ItemType.VoltDriver => BeamType.VoltDriver,
                    ItemType.Battlehammer => BeamType.Battlehammer,
                    ItemType.Imperialist => BeamType.Imperialist,
                    ItemType.Judicator => BeamType.Judicator,
                    ItemType.Magmaul => BeamType.Magmaul,
                    ItemType.ShockCoil => BeamType.ShockCoil,
                    ItemType.OmegaCannon => BeamType.OmegaCannon,
                    _ => BeamType.None
                };
            }
            if (weapon == BeamType.None)
            {
                return;
            }
            WeaponInfo info = Weapons.Current[(int)weapon];
            if (affinityGrantsMissileAmmo)
            {
                _ammo[Missiles] = Math.Min(_ammo[Missiles] + 50, _ammoMax[Missiles]);
            }
            else if (_ammo[info.AmmoType] < 60)
            {
                _ammo[info.AmmoType] = Math.Min(_ammo[info.AmmoType] + 60, 60);
            }
            bool newlyAvailable = !_availableWeapons[weapon];
            if (newlyAvailable)
            {
                _availableWeapons[weapon] = true;
                _availableCharges[weapon] = true;
            }
            if (affinityPickup)
            {
                // Affinity pickups are the player's signature weapon.  They
                // always become the selected weapon, including ammo-only
                // pickups, while the slot is updated even if a morph/weapon
                // transition temporarily defers the actual equip.
                bool autoEquipRequested = CurrentWeapon != weapon;
                UpdateAffinityWeaponSlot(weapon);
                if (CurrentWeapon == weapon)
                {
                    _pendingAutoEquipWeapon = BeamType.None;
                }
                else if (IsAltForm || IsMorphing || IsUnmorphing
                    || GunAnimation == GunAnimation.UpDown
                    || !TryEquipWeapon(weapon, suppressFailureSound: true))
                {
                    _pendingAutoEquipWeapon = weapon;
                }
                if (autoEquipRequested)
                {
                    MarkAuthoritativeWeaponPickup();
                }
                if (IsMainPlayer)
                {
                    PlayPickupSound(newlyAvailable
                        ? SfxId.WEAPON_POWER_UP : SfxId.AMMO_POWER_UP1);
                }
                return;
            }
            if (newlyAvailable)
            {
                BeamType previousWeapon = CurrentWeapon;
                BeamType slot2Weapon = _weaponSlots[2];
                int slot2Index = (int)slot2Weapon;
                BeamType affinityWeapon = Weapons.GetAffinityBeam(Hunter);
                if (slot2Weapon == BeamType.None || weapon == BeamType.OmegaCannon
                    || (info.Priority > Weapons.Current[slot2Index].Priority || weapon == affinityWeapon)
                    && (!Flags2.TestFlag(PlayerFlags2.Shooting) || CurrentWeapon != slot2Weapon))
                {
                    // todo: update HUD
                    if ((info.Priority > EquipInfo.Weapon.Priority || weapon == BeamType.OmegaCannon
                        || weapon == affinityWeapon) && !Flags2.TestFlag(PlayerFlags2.Shooting))
                    {
                        if (!TryEquipWeapon(weapon))
                        {
                            UpdateAffinityWeaponSlot(weapon, slot: 2);
                        }
                    }
                    else if (!Flags2.TestFlag(PlayerFlags2.Shooting) || CurrentWeapon != slot2Weapon)
                    {
                        UpdateAffinityWeaponSlot(weapon, slot: 2);
                    }
                }
                if (CurrentWeapon != previousWeapon)
                {
                    MarkAuthoritativeWeaponPickup();
                }
                if (IsMainPlayer)
                {
                    PlayPickupSound(SfxId.WEAPON_POWER_UP);
                }
            }
            else if (IsMainPlayer)
            {
                PlayPickupSound(SfxId.AMMO_POWER_UP1);
            }
        }

        private static readonly int[] _healthPickupAmounts = new int[3] { 30, 60, 100 };

        public void GainHealth(uint health)
        {
            GainHealth((int)health);
        }

        public void GainHealth(int health)
        {
            int previousHealth = _health;
            if (_health > 0)
            {
                if (Flags2.TestFlag(PlayerFlags2.Halfturret))
                {
                    if (_health <= _halfturret.Health)
                    {
                        _health += health - health / 2;
                        _halfturret.Health += health / 2;
                    }
                    else
                    {
                        _health += health / 2;
                        _halfturret.Health += health - health / 2;
                    }
                    if (_halfturret.Health > 100)
                    {
                        _halfturret.Health = 100;
                    }
                }
                else
                {
                    _health += health;
                }
                if (_health > _healthMax)
                {
                    _health = _healthMax;
                }
            }
            _scene.Services.Combat?.NoteHealing(this, _health - previousHealth);
        }

        private bool TrySwitchForms(bool force = false,
            bool transferHalfturretHealth = true)
        {
            if (!force && (IsMorphing || IsUnmorphing || _frozenTimer > 0 || _field6D0 || _deathaltTimer > 0
                    || Flags2.TestFlag(PlayerFlags2.NoFormSwitch)
                    || !IsAltForm && Flags2.TestFlag(PlayerFlags2.BipedStuck)
                    || IsAltForm && MorphCamera != null))
            {
                if (IsMainPlayer && (_scene.CameraSequences.Current == null || !_scene.CameraSequences.Current.BlockInput))
                {
                    _soundSource.PlayFreeSfx(SfxId.BEAM_SWITCH_FAIL);
                }
                return false;
            }
            if (!SupportsAltForm(Hunter))
            {
                return false;
            }

            void AfterSwitch()
            {
                UpdateZoom(false);
                EquipInfo.ChargeLevel = 0;
                EquipInfo.SmokeLevel = 0;
                if (_burnTimer > 0)
                {
                    CreateBurnEffect();
                }
            }

            if (!IsAltForm)
            {
                EnterAltForm(transferHalfturretHealth);
                AfterSwitch();
                return true;
            }
            if (!Flags1.TestFlag(PlayerFlags1.NoUnmorph))
            {
                ExitAltForm(transferHalfturretHealth);
                AfterSwitch();
                return true;
            }
            if (IsMainPlayer)
            {
                _soundSource.PlayFreeSfx(SfxId.BEAM_SWITCH_FAIL);
            }
            return false;
        }

        private void UpdateAimVecs()
        {
            Vector3 facing = _facingVector;
            Vector3 up = _upVector;
            _gunDrawPos = Fixed.ToFloat(Values.FieldB8) * facing
                + CameraInfo.Position
                + Fixed.ToFloat(Values.FieldB0) * _gunVec2
                + Fixed.ToFloat(Values.FieldB4) * up;
            float cos = MathF.Cos(MathHelper.DegreesToRadians(_gunViewBob));
            _gunDrawPos.Y += Fixed.ToFloat(20) * cos;
            if (_scene.Features.FixedWeapon)
            {
                // Rides rigidly with the camera instead of lagging half a
                // step behind the aim point -- Quake's static weapon, rather
                // than the DS game's drifting one. Shot direction is
                // unaffected: it's recomputed from _aimPosition - _muzzlePos
                // wherever a beam actually fires (PlayerInput.cs:1014), and
                // this only moves the muzzle offset point by a few units.
                _aimVec = facing;
            }
            else
            {
                _aimVec = _aimPosition - _gunDrawPos;
                float dot = Vector3.Dot(_aimVec, facing);
                Vector3 vec = facing * dot;
                _aimVec = (_aimVec + (vec - _aimVec) / 2).Normalized();
            }
            _muzzlePos = _gunDrawPos + _aimVec * Fixed.ToFloat(Values.MuzzleOffset);
        }

        private void InitAltTransform()
        {
            _field4E8 = _gunVec2;
            Vector3 up = _upVector;
            _modelTransform.Row0.X = _gunVec2.X; // right?
            _modelTransform.Row0.Y = 0;
            _modelTransform.Row0.Z = _gunVec2.Z;
            _modelTransform.Row1.X = up.X;
            _modelTransform.Row1.Y = up.Y;
            _modelTransform.Row1.Z = up.Z;
            _modelTransform.Row2.X = _field70; // facing?
            _modelTransform.Row2.Y = 0;
            _modelTransform.Row2.Z = _field74;
            _modelTransform.Row2.Xyz = Vector3.Cross(_modelTransform.Row0.Xyz, _modelTransform.Row1.Xyz);
            _modelTransform.Row1.Xyz = Vector3.Cross(_modelTransform.Row2.Xyz, _modelTransform.Row0.Xyz);
            _modelTransform.Row0.Xyz = Vector3.Normalize(_modelTransform.Row0.Xyz);
            _modelTransform.Row1.Xyz = Vector3.Normalize(_modelTransform.Row1.Xyz);
            _modelTransform.Row2.Xyz = Vector3.Normalize(_modelTransform.Row2.Xyz);
        }

        private void UpdateAltTransform()
        {
            if (Hunter == Hunter.Noxus)
            {
                _altWobble += (5 - _altWobble) / 32 / 2; // todo: FPS stuff
                _altWobble = Math.Clamp(_altWobble, Fixed.ToFloat(Values.AltMinWobble), Fixed.ToFloat(Values.AltMaxWobble));
                _altTiltX -= _altTiltX / 8 / 2; // todo: FPS stuff
                _altTiltZ -= _altTiltZ / 8 / 2; // todo: FPS stuff
                _altTiltX += -(_altTiltX + Fixed.ToFloat(25) * (Speed.X - PrevSpeed.X)) / 32 / 2; // todo: FPS stuff
                _altTiltZ += -(_altTiltZ + Fixed.ToFloat(25) * (Speed.Z - PrevSpeed.Z)) / 32 / 2; // todo: FPS stuff
                float minSpinAccel = Fixed.ToFloat(Values.AltMinSpinAccel);
                float maxSpinAccel = Fixed.ToFloat(Values.AltMaxSpinAccel);
                _altSpinSpeed += (minSpinAccel
                    + (_altAttackTime * (maxSpinAccel - minSpinAccel) / (SimTicks.From30HzFrames(Values.AltAttackStartup)))
                    - _altSpinSpeed) / 32 / 2; // todo: FPS stuff
                _altSpinSpeed = Math.Clamp(_altSpinSpeed, Fixed.ToFloat(Values.AltMinSpinSpeed), Fixed.ToFloat(Values.AltMaxSpinSpeed));
                _altSpinRot += _altSpinSpeed / 2; // todo: FPS stuff
                while (_altSpinRot > 360)
                {
                    _altSpinRot -= 360;
                }
                var rotX = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(_altWobble));
                var rotY = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(_altSpinRot));
                Matrix4 transform = rotX * rotY;
                float mag = MathF.Sqrt(_altTiltX * _altTiltX + _altTiltZ * _altTiltZ);
                if (mag != 0)
                {
                    var axis = new Vector3(_altTiltZ / mag, 0, -_altTiltX / mag);
                    float angle = mag * Fixed.ToFloat(Values.AltTiltAngleMax);
                    angle = MathF.Min(angle, Fixed.ToFloat(Values.AltTiltAngleCap));
                    var rotAxis = Matrix4.CreateFromAxisAngle(axis, MathHelper.DegreesToRadians(angle));
                    transform *= rotAxis;
                }
                _modelTransform = transform;
            }
            else if (Hunter == Hunter.Kanden)
            {
                UpdateStinglarvaSegments();
            }
            else if (Hunter == Hunter.Samus || Hunter == Hunter.Spire)
            {
                Vector3 axis = Vector3.Zero;
                float altRadius = Fixed.ToFloat(Values.AltColRadius);
                if (Hunter == Hunter.Spire || Flags1.TestFlag(PlayerFlags1.CollidingEntity))
                {
                    // todo: FPS stuff
                    axis.X = altRadius * (Speed.Z / 2);
                    axis.Z = -altRadius * (Speed.X / 2);
                }
                else
                {
                    axis.X = altRadius * (Position.Z - PrevPosition.Z);
                    axis.Z = -altRadius * (Position.X - PrevPosition.X);
                }
                float mag = axis.Length;
                if (mag > 0)
                {
                    axis /= mag;
                    float angle = mag / (altRadius * altRadius);
                    var rotMtx = Matrix4.CreateFromAxisAngle(axis, angle);
                    Matrix4 transform = _modelTransform * rotMtx;
                    if (Hunter == Hunter.Samus)
                    {
                        if (Vector3.Dot(transform.Row0.Xyz, axis) < 0)
                        {
                            axis *= -1;
                        }
                        axis = Vector3.Cross(transform.Row0.Xyz, axis);
                        float mbAngle = axis.Length;
                        if (mbAngle > 0)
                        {
                            if (mbAngle > 0.125f)
                            {
                                float div = Math.Min(angle / Fixed.ToFloat(3216), 1);
                                mbAngle *= div / 8;
                            }
                            rotMtx = Matrix4.CreateFromAxisAngle(axis, mbAngle);
                            transform *= rotMtx;
                        }
                    }
                    transform.Row2.Xyz = Vector3.Cross(transform.Row0.Xyz, transform.Row1.Xyz);
                    transform.Row1.Xyz = Vector3.Cross(transform.Row2.Xyz, transform.Row0.Xyz);
                    transform.Row0.Xyz = transform.Row0.Xyz.Normalized();
                    transform.Row1.Xyz = transform.Row1.Xyz.Normalized();
                    transform.Row2.Xyz = transform.Row2.Xyz.Normalized();
                    if (Hunter == Hunter.Spire)
                    {
                        for (int i = 0; i < _spireAltVecs.Length; i++)
                        {
                            _spireAltVecs[i] = Matrix.Vec3MultMtx3(Metadata.SpireAltVectors[i], transform);
                        }
                    }
                    _modelTransform = transform;
                }
            }
            else
            {
                _modelTransform = GetTransformMatrix(new Vector3(_field80, 0, _field84), Vector3.UnitY);
            }
        }

        private void UpdateStinglarvaSegments()
        {
            const int cycle = 13 * SimTicks.TicksPer30HzFrame;
            float angle = 359f * (_scene.FrameCount % cycle) / (cycle - 1);
            float factor = 0.3f * MathF.Sin(MathHelper.DegreesToRadians(angle)) * _hSpeedMag;
            _kandenSegPos[0] = Position.AddX(_field78 * factor).AddZ(_field7C * factor);
            Vector3 dir;
            if (Speed.LengthSquared > 0.02f)
            {
                dir = new Vector3(Speed.X + _field80 / 4, Speed.Y, Speed.Z + _field84 / 4);
            }
            else
            {
                dir = new Vector3(_field80, 0, _field84);
            }
            dir = dir.Normalized();
            if (Vector3.Dot(dir, _kandenSegMtx[0].Row2.Xyz) < Fixed.ToFloat(-5))
            {
                dir.X += Fixed.ToFloat(5);
            }
            dir = _kandenSegMtx[0].Row2.Xyz + 0.3f * (dir - _kandenSegMtx[0].Row2.Xyz);
            dir = dir.Normalized();
            if (dir.X != 0 || dir.Z != 0)
            {
                _kandenSegMtx[0] = GetTransformMatrix(dir, Vector3.UnitY);
            }
            else
            {
                var facing = new Vector3(-_field70, 0, -_field74);
                var up = new Vector3(dir.X, dir.Y, 0);
                _kandenSegMtx[0] = GetTransformMatrix(facing, up);
            }
            _kandenSegMtx[0].Row3.Xyz = _kandenSegPos[0];
            Debug.Assert(_kandenSegPos.Length == _kandenSegMtx.Length);
            for (int i = 1; i < _kandenSegPos.Length; i++)
            {
                angle += 85;
                while (angle >= 360)
                {
                    angle -= 360;
                }
                factor = 0.12f * MathF.Sin(MathHelper.DegreesToRadians(angle)) * _hSpeedMag;
                Vector3 segPos = _kandenSegPos[i];
                segPos = segPos.AddX(_field78 * factor).AddZ(_field7C * factor);
                _kandenSegPos[i] = segPos;
                dir = (_kandenSegPos[i - 1] - segPos).Normalized();
                Matrix4 prevMtx = _kandenSegMtx[i - 1];
                float dot = Vector3.Dot(dir, prevMtx.Row2.Xyz);
                if (dot < Fixed.ToFloat(2896))
                {
                    var axis = Vector3.Cross(dir, prevMtx.Row2.Xyz);
                    float mag = axis.Length;
                    axis /= mag;
                    float atan = MathF.Atan2(mag, dot);
                    atan -= MathHelper.DegreesToRadians(45);
                    var rotMtx = Matrix4.CreateFromAxisAngle(axis, atan);
                    dir = Matrix.Vec3MultMtx3(dir, rotMtx);
                }
                _kandenSegMtx[i] = GetTransformMatrix(dir, _kandenSegMtx[0].Row1.Xyz);
                float dist = KandenAltNodeDistances[i - 1];
                dir *= dist;
                _kandenSegPos[i] = _kandenSegPos[i - 1] - dir;
                _kandenSegMtx[i].Row3.Xyz = _kandenSegPos[i];
            }
        }

        private void EnterAltForm(bool transferHalfturretHealth = true)
        {
            _altRollFbX = _field70;
            _altRollFbZ = _field74;
            _altRollLrX = _gunVec2.X;
            _altRollLrZ = _gunVec2.Z;
            Flags1 |= PlayerFlags1.Morphing;
            var camFacing = new Vector3(_field70, 0, _field74);
            SwitchCamera(Values.AltFormStrafe != 0 ? CameraType.Third2 : CameraType.Third1, camFacing);
            InitAltTransform();
            _modelTransform.Row3.Xyz = Vector3.Zero;
            if (Hunter == Hunter.Spire)
            {
                for (int i = 0; i < _spireAltVecs.Length; i++)
                {
                    _spireAltVecs[i] = Vector3.Zero;
                }
                _altModel.SetAnimation((int)SpireAltAnim.Attack, AnimFlags.Paused);
            }
            else if (Hunter == Hunter.Noxus)
            {
                _altSpinSpeed = Fixed.ToFloat(Values.AltMinSpinAccel);
                _altTiltX = 0;
                _altTiltZ = 0;
                _altSpinRot = 0;
                _altWobble = 0;
                // animation frames updated later based on attack timer
                _altModel.SetAnimation((int)NoxusAltAnim.Extend, AnimFlags.Paused);
            }
            else if (Hunter == Hunter.Weavel)
            {
                _altModel.SetAnimation((int)WeavelAltAnim.Idle);
                SetWeavelHalfturretActive(true, transferHalfturretHealth);
            }
            else if (Hunter == Hunter.Samus)
            {
                _furlEffect = _scene.SpawnEffectGetEntry(30, _gunVec2, _facingVector, Position); // samusFurl
            }
            else if (Hunter == Hunter.Kanden)
            {
                _altModel.SetAnimation((int)KandenAltAnim.Idle, AnimFlags.Paused);
            }
            else if (Hunter == Hunter.Trace)
            {
                _altModel.SetAnimation((int)TraceAltAnim.Idle);
            }
            else if (Hunter == Hunter.Sylux)
            {
                _altModel.SetAnimation((int)SyluxAltAnim.Idle);
            }
            if (EquipInfo.ChargeLevel > 0)
            {
                StopBeamChargeSfx(CurrentWeapon);
                SetGunAnimation(GunAnimation.Idle, AnimFlags.NoLoop);
            }
            EquipInfo.ChargeLevel = 0;
            SetBipedAnimation(PlayerAnimation.Morph, AnimFlags.NoLoop);
            PlayHunterSfx(HunterSfx.Morph);
        }

        public void ExitAltForm(bool transferHalfturretHealth = true)
        {
            SetWeavelHalfturretActive(false, transferHalfturretHealth);
            Flags1 &= ~PlayerFlags1.Morphing;
            Flags1 |= PlayerFlags1.Unmorphing;
            SwitchCamera(CameraType.First, _facingVector);
            // the game stops the boost charge SFX here, but that SFX is empty
            _boostCharge = 0;
            SetBipedAnimation(PlayerAnimation.Unmorph, AnimFlags.NoLoop);
            if (Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                EndAltAttack();
            }
            UpdateZoom(false);
            EquipInfo.ChargeLevel = 0;
            EquipInfo.SmokeLevel = 0;
            PlayHunterSfx(HunterSfx.Unmorph);
            if (IsAltForm)
            {
                UpdateForm(altForm: false);
            }
        }

        private void UpdateForm(bool altForm)
        {
            if (altForm != IsAltForm) AdvancePresentationPoseEpoch();
            if (altForm)
            {
                Flags1 |= PlayerFlags1.AltForm;
            }
            else
            {
                Flags1 &= ~PlayerFlags1.AltForm;
            }
            // todo?: update HUD if main player
            CollisionVolume nextVolume = altForm
                ? PlayerVolumes[(int)Hunter, 2]
                : PlayerVolumes[(int)Hunter, 0];
            bool grounded = ShouldPreserveFormBottom(Flags1);
            Position = ResolveFormOrigin(Position, _volumeUnxf, nextVolume, grounded);
            _volumeUnxf = nextVolume;
            _volume = CollisionVolume.Move(_volumeUnxf, Position);
            if (altForm)
            {
                InitAltTransform();
                _field80 = _field70;
                _field84 = _field74;
                if (Hunter == Hunter.Kanden)
                {
                    _kandenSegPos[0] = Position;
                    var facing = new Vector3(_field70, 0, _field74);
                    _kandenSegMtx[0] = GetTransformMatrix(facing, Vector3.UnitY, Position);
                    Debug.Assert(_kandenSegPos.Length == _kandenSegMtx.Length);
                    for (int i = 1; i < _kandenSegPos.Length; i++)
                    {
                        float dist = -KandenAltNodeDistances[i - 1];
                        _kandenSegPos[i] = _kandenSegPos[i - 1] + facing * dist;
                        Matrix4 matrix = _kandenSegMtx[0];
                        matrix.Row3.Xyz = _kandenSegPos[i];
                        _kandenSegMtx[i] = matrix;
                    }
                }
                else if (Hunter == Hunter.Spire)
                {
                    _scene.SpawnEffect(37, Vector3.UnitX, Vector3.UnitY, Position); // spireAltSlam
                    CameraInfo.SetShake(0.3f);
                    foreach (PlayerEntity other in _scene.GetPlayerEntities())
                    {
                        if (other == this)
                        {
                            continue;
                        }
                        if (other.Flags1.TestFlag(PlayerFlags1.Standing) && Vector3.DistanceSquared(Position, other.Position) < 16)
                        {
                            other.CameraInfo.SetShake(0.3f);
                            if (other.Speed.Y < 0.15f)
                            {
                                other.Speed = other.Speed.WithY(0.15f);
                            }
                        }
                    }
                }
            }
            else
            {
                _gunVec1 = _facingVector;
            }
            StopAltFormSfx();
        }

        /// <summary>
        /// Adjust a player origin while changing between the biped and
        /// alternate-form spheres. Grounded transitions keep the feet on the
        /// same world plane; airborne transitions keep the sphere center in
        /// place so a morph does not teleport the player vertically.
        /// </summary>
        internal static Vector3 ResolveFormOrigin(Vector3 origin,
            CollisionVolume previous, CollisionVolume next, bool grounded)
        {
            Vector3 previousCenter = origin + previous.SpherePosition;
            if (!grounded)
            {
                return previousCenter - next.SpherePosition;
            }

            Vector3 result = origin;
            result.X = previousCenter.X - next.SpherePosition.X;
            result.Z = previousCenter.Z - next.SpherePosition.Z;
            float previousBottom = previousCenter.Y - previous.SphereRadius;
            result.Y = previousBottom + next.SphereRadius - next.SpherePosition.Y;
            return result;
        }

        internal static bool SupportsAltForm(Hunter hunter)
            => hunter >= Hunter.Samus && hunter <= Hunter.Weavel;

        internal static bool ShouldPreserveFormBottom(PlayerFlags1 flags)
            => flags.TestAny(PlayerFlags1.Standing
                | PlayerFlags1.StandingPrevious);

        internal static bool SupportsReplicatedAltAttack(Hunter hunter)
            => hunter is Hunter.Trace or Hunter.Spire or Hunter.Weavel;

        private void SetWeavelHalfturretActive(bool active,
            bool transferHealth = true)
        {
            if (active)
            {
                if (Hunter != Hunter.Weavel
                    || Flags2.TestFlag(PlayerFlags2.Halfturret))
                {
                    return;
                }
                Flags2 |= PlayerFlags2.Halfturret;
                _halfturret.NodeRef = NodeRef;
                int health = Health;
                _scene.AddEntity(_halfturret);
                if (!transferHealth)
                {
                    // Replica activation still needs the turret entity for
                    // presentation, but its Initialize routine must not split
                    // the already-authoritative snapshot health.
                    Health = health;
                }
                return;
            }
            if (!Flags2.TestFlag(PlayerFlags2.Halfturret))
            {
                return;
            }
            Flags2 &= ~PlayerFlags2.Halfturret;
            if (transferHealth && _halfturret.Health > 0)
            {
                GainHealth(_halfturret.Health);
            }
            _halfturret.Die();
        }

        internal void BeginReplicatedAltAttack()
        {
            if (!IsAltForm || !SupportsReplicatedAltAttack(Hunter))
            {
                return;
            }
            if (Hunter == Hunter.Spire)
            {
                BeginSpireAltAttack();
                return;
            }
            if (Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                return;
            }
            Flags2 |= PlayerFlags2.AltAttack;
            if (Hunter == Hunter.Trace)
            {
                _altModel.SetAnimation((int)TraceAltAnim.Attack, AnimFlags.NoLoop);
            }
            else if (Hunter == Hunter.Weavel)
            {
                _altModel.SetAnimation((int)WeavelAltAnim.Attack, AnimFlags.NoLoop);
            }
        }

        private void CreateBurnEffect()
        {
            if (_burnEffect != null)
            {
                _scene.UnlinkEffectEntry(_burnEffect);
                _burnEffect = null;
            }
            if (!IsUnmorphing)
            {
                Vector3 up;
                Vector3 facing;
                Vector3 position;
                int effectId;
                if (IsAltForm || IsMorphing || !IsMainPlayer)
                {
                    position = _volume.SpherePosition;
                    up = Vector3.UnitY;
                    facing = new Vector3(_field70, 0, _field74);
                    effectId = IsAltForm || IsMorphing ? 187 : 189; // flamingAltForm or flamingHunter
                }
                else
                {
                    position = _muzzlePos;
                    up = _gunVec1;
                    facing = _upVector;
                    effectId = 188; // flamingGun
                }
                _burnEffect = _scene.SpawnEffectGetEntry(effectId, facing, up, position);
                _burnEffect?.SetElementExtension(true);
            }
        }

        private void CreateIceBreakEffectGun()
        {
            int effectId = 231; // iceShatter
            Vector3 playerUp = _upVector;
            Vector3 up = _facingVector;
            Vector3 facing;
            if (up.Z <= -0.9f || up.Z >= 0.9f)
            {
                facing = Vector3.Cross(Vector3.UnitX, up).Normalized();
            }
            else
            {
                facing = Vector3.Cross(Vector3.UnitZ, up).Normalized();
            }
            Vector3 position = CameraInfo.Position + up / 2;
            Vector3 spawnPos = position;
            _scene.SpawnEffect(effectId, facing, up, spawnPos);
            spawnPos = position + _gunVec2 * 0.4f;
            _scene.SpawnEffect(effectId, facing, up, spawnPos);
            spawnPos = position - _gunVec2 * 0.4f;
            _scene.SpawnEffect(effectId, facing, up, spawnPos);
            spawnPos = position + playerUp * 0.4f;
            _scene.SpawnEffect(effectId, facing, up, spawnPos);
            spawnPos = position - playerUp * 0.4f;
            _scene.SpawnEffect(effectId, facing, up, spawnPos);
        }

        private void CreateIceBreakEffectBiped(Model model)
        {
            Debug.Assert(model.Nodes.Count > 1);
            int effectId = 231; // iceShatter
            for (int i = 1; i < model.Nodes.Count; i++)
            {
                Node node = model.Nodes[i];
                Vector3 pos = _bipedIceTransforms[i].Row3.Xyz;
                Vector3 up;
                if (node.ChildIndex <= 0)
                {
                    up = (pos - Position).Normalized();
                }
                else
                {
                    up = (_bipedIceTransforms[node.ChildIndex].Row3.Xyz - pos).Normalized();
                }
                Vector3 facing;
                if (up.Z <= -0.9f || up.Z >= 0.9f)
                {
                    facing = Vector3.Cross(Vector3.UnitX, up).Normalized();
                }
                else
                {
                    facing = Vector3.Cross(Vector3.UnitZ, up).Normalized();
                }
                _scene.SpawnEffect(effectId, facing, up, pos);
            }
        }

        private void CreateIceBreakEffectAlt()
        {

            int effectId = 231; // iceShatter
            _scene.SpawnEffect(effectId, Vector3.UnitX, Vector3.UnitY, _volume.SpherePosition);
            _scene.SpawnEffect(effectId, Vector3.UnitY, Vector3.UnitX, _volume.SpherePosition);
            _scene.SpawnEffect(effectId, Vector3.UnitY, -Vector3.UnitX, _volume.SpherePosition);
            _scene.SpawnEffect(effectId, Vector3.UnitY, Vector3.UnitX, _volume.SpherePosition);
            _scene.SpawnEffect(effectId, Vector3.UnitY, -Vector3.UnitX, _volume.SpherePosition);
        }

        private PlayerSpawnEntity? GetRespawnPoint() => _scene.SpawnDirector.Select(this);

        private int GetTimeUntilRespawn()
        {
            int count = 0;
            if (_scene.Match.Rules.Mode != MatchMode.Survival && _scene.Match.Rules.Mode != MatchMode.TeamSurvival)
            {
                if (_scene.Players.ActiveCount > 3)
                {
                    count = SimTicks.From30HzFrames(900) - _timeSinceDead;
                }
                else if (_scene.Players.ActiveCount > 2)
                {
                    count = SimTicks.From30HzFrames(600) - _timeSinceDead;
                }
                else
                {
                    count = SimTicks.From30HzFrames(300) - _timeSinceDead;
                }
            }
            else if (!LoadFlags.TestFlag(LoadFlags.Spawned))
            {
                count = SimTicks.From30HzFrames(210) - _timeSinceDead;
            }
            return count;
        }

        public override void HandleMessage(MessageInfo info)
        {
            if (info.Message == Message.Damage)
            {
                TakeDamage((int)info.Param1, DamageFlags.IgnoreInvuln, direction: null, source: null);
            }
            else if (info.Message == Message.Death)
            {
                TakeDamage((int)info.Param1, DamageFlags.Death, direction: null, source: null);
            }
            else if (info.Message == Message.Gravity)
            {
                float gravity = Fixed.ToFloat((int)info.Param1);
                if (!Flags1.TestFlag(PlayerFlags1.Standing) && !Flags2.TestFlag(PlayerFlags2.AltAttack)
                    && gravity != 0 && _jumpPadControlLock == 0)
                {
                    Flags2 |= PlayerFlags2.GravityOverride;
                    _gravity = gravity;
                }
            }
            else if (info.Message == Message.SetCamSeqAi)
            {
                if (IsBot)
                {
                    AiData.Flags2 |= AiFlags2.AiStart;
                }
            }
            else if (info.Message == Message.Impact)
            {
                if (info.Param1 is EntityBase target && target != this
                    && (target is ForceFieldLockEntity || target.Type == EntityType.Halfturret || target.Type == EntityType.Player))
                {
                    _lastTarget = target;
                    _timeSinceHitTarget = 0;
                    if (info.Sender.Type == EntityType.BeamProjectile)
                    {
                        var beam = (BeamProjectileEntity)info.Sender;
                        if (beam.Beam == BeamType.ShockCoil)
                        {
                            if (target == _shockCoilTarget)
                            {
                                _shockCoilTimer++;
                            }
                            else
                            {
                                _shockCoilTimer = 0;
                                _shockCoilTarget = target;
                            }
                        }
                    }
                }
            }
            else if (info.Message == Message.PreventFormSwitch)
            {
                Flags2 |= PlayerFlags2.NoFormSwitch;
            }
            else if (info.Message == Message.DripMoatPlatform)
            {
                if ((int)info.Param1 == 0)
                {
                    Flags2 &= ~PlayerFlags2.BipedLock;
                }
                else
                {
                    Flags2 |= PlayerFlags2.BipedLock;
                }
            }
        }

        public override void Destroy()
        {
            ResetLockjawBombState();
            _soundSource.StopAllSfx();
            if (_furlEffect != null)
            {
                _scene.UnlinkEffectEntry(_furlEffect);
                _furlEffect = null;
            }
            if (_boostEffect != null)
            {
                _scene.UnlinkEffectEntry(_boostEffect);
                _boostEffect = null;
            }
            if (_burnEffect != null)
            {
                _scene.UnlinkEffectEntry(_burnEffect);
                _burnEffect = null;
            }
            if (_chargeEffect != null)
            {
                _scene.UnlinkEffectEntry(_chargeEffect);
                _chargeEffect = null;
            }
            if (_muzzleEffect != null)
            {
                _scene.UnlinkEffectEntry(_muzzleEffect);
                _muzzleEffect = null;
            }
            if (_doubleDmgEffect != null)
            {
                _scene.UnlinkEffectEntry(_doubleDmgEffect);
                _doubleDmgEffect = null;
            }
            if (_deathaltEffect != null)
            {
                _scene.UnlinkEffectEntry(_deathaltEffect);
                _deathaltEffect = null;
            }
            base.Destroy();
        }
    }
}
