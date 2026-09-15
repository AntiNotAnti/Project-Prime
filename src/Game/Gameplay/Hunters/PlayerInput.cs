using System;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        internal bool _showScoreboard;
        internal readonly float[] _pastAimX = new float[8];
        internal readonly float[] _pastAimY = new float[8];
        private float _buttonAimX = 0;
        private float _buttonAimY = 0;
        private const float _maxButtonAimX = 8;
        private const float _maxButtonAimY = 8;

        private void ProcessInput()
        {
            // Every request belongs to exactly one simulation tick. Take and
            // overwrite before any health/form/frozen early behavior so an
            // illegal request is ignored now rather than deferred until legal.
            BoostIntent boostIntent = Input.TakeBoostIntent(Controls.Boost.IsDown);
            if (_health > 0)
            {
                if (Flags1.TestFlag(PlayerFlags1.FreeLook))
                {
                    Flags1 |= PlayerFlags1.FreeLookPrevious;
                }
                else
                {
                    Flags1 &= ~PlayerFlags1.FreeLookPrevious;
                }
                Flags1 &= ~PlayerFlags1.FreeLook;
                Flags1 &= ~PlayerFlags1.Walking;
                Flags1 &= ~PlayerFlags1.Strafing;
                if (!IsBot)
                {
                    ProcessTouchInput();
                    // todo: actual pause menu should require pressed
                    if (!Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen) && Controls.Pause.IsDown)
                    {
                        _showScoreboard = true;
                    }
                    else
                    {
                        _showScoreboard = false;
                    }
                }
                if (_frozenTimer > 0)
                {
                    _frozenTimer--;
                    _timeSinceFrozen = 0;
                    if (_frozenTimer == 0)
                    {
                        _soundSource.PlaySfx(SfxId.SHOTGUN_BREAK_FREEZE);
                        if (IsAltForm)
                        {
                            CreateIceBreakEffectAlt();
                        }
                        else if (IsMainPlayer)
                        {
                            CreateIceBreakEffectGun();
                        }
                        else if (Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson))
                        {
                            int lod = Flags2.TestFlag(PlayerFlags2.Lod1) ? 1 : 0;
                            CreateIceBreakEffectBiped(_bipedModelLods[lod].Model);
                        }
                    }
                }
                if (_frozenGfxTimer > 0)
                {
                    _frozenGfxTimer--;
                    if (IsMainPlayer && _frozenGfxTimer == 0)
                    {
                        _drawIceLayer = false;
                    }
                }
                if (_timeSinceFrozen != UInt16.MaxValue)
                {
                    _timeSinceFrozen++;
                }
            }
            else
            {
                _showScoreboard = Controls.Pause.IsDown;
            }
            if (IsAltForm || IsMorphing)
            {
                ProcessAlt(boostIntent);
            }
            else
            {
                ProcessBiped();
            }
        }

        private static readonly BeamType[] _weaponOrder = new BeamType[9]
        {
            /* 0 */ BeamType.PowerBeam,
            /* 1 */ BeamType.Missile,
            /* 2 */ BeamType.VoltDriver,
            /* 3 */ BeamType.Battlehammer,
            /* 4 */ BeamType.Imperialist,
            /* 5 */ BeamType.Judicator,
            /* 6 */ BeamType.Magmaul,
            /* 7 */ BeamType.ShockCoil,
            /* 8 */ BeamType.OmegaCannon
        };

        private void ProcessTouchInput()
        {
            if (Controls.WeaponMenu.IsDown)
            {
                Flags1 |= PlayerFlags1.NoAimInput;
                Flags1 |= PlayerFlags1.WeaponMenuOpen;
                _showScoreboard = false;
            }
            bool selected = false;
            if (!Controls.WeaponMenu.IsDown)
            {
                selected = EndWeaponMenu();
            }
            if (!selected)
            {
                if (Controls.PowerBeam.IsPressed)
                {
                    if (CurrentWeapon != BeamType.PowerBeam)
                    {
                        TryEquipWeapon(BeamType.PowerBeam, debug: true);
                    }
                }
                else if (Controls.Missile.IsPressed)
                {
                    if (CurrentWeapon != BeamType.Missile)
                    {
                        TryEquipWeapon(BeamType.Missile, debug: true);
                    }
                }
                else if (Controls.VoltDriver.IsPressed)
                {
                    if (CurrentWeapon != BeamType.VoltDriver)
                    {
                        TryEquipWeapon(BeamType.VoltDriver, debug: true);
                    }
                }
                else if (Controls.Battlehammer.IsPressed)
                {
                    if (CurrentWeapon != BeamType.Battlehammer)
                    {
                        TryEquipWeapon(BeamType.Battlehammer, debug: true);
                    }
                }
                else if (Controls.Imperialist.IsPressed)
                {
                    if (CurrentWeapon != BeamType.Imperialist)
                    {
                        TryEquipWeapon(BeamType.Imperialist, debug: true);
                    }
                }
                else if (Controls.Judicator.IsPressed)
                {
                    if (CurrentWeapon != BeamType.Judicator)
                    {
                        TryEquipWeapon(BeamType.Judicator, debug: true);
                    }
                }
                else if (Controls.Magmaul.IsPressed)
                {
                    if (CurrentWeapon != BeamType.Magmaul)
                    {
                        TryEquipWeapon(BeamType.Magmaul, debug: true);
                    }
                }
                else if (Controls.ShockCoil.IsPressed)
                {
                    if (CurrentWeapon != BeamType.ShockCoil)
                    {
                        TryEquipWeapon(BeamType.ShockCoil, debug: true);
                    }
                }
                else if (Controls.OmegaCannon.IsPressed)
                {
                    if (CurrentWeapon != BeamType.OmegaCannon)
                    {
                        TryEquipWeapon(BeamType.OmegaCannon, debug: true);
                    }
                }
                else if (Controls.AffinitySlot.IsPressed)
                {
                    BeamType weapon = _weaponSlots[2];
                    if (weapon != BeamType.None && CurrentWeapon != weapon)
                    {
                        TryEquipWeapon(weapon);
                    }
                }
                else if (Controls.ScrollAllWeapons || CurrentWeapon != BeamType.PowerBeam && CurrentWeapon != BeamType.Missile)
                {
                    int currentIndex = -1;
                    for (int i = 0; i < _weaponOrder.Length; i++)
                    {
                        if (_weaponOrder[i] == CurrentWeapon)
                        {
                            currentIndex = i;
                        }
                    }
                    int nextIndex = currentIndex;
                    BeamType nextBeam = CurrentWeapon;
                    // Bounded as well as terminated by the index, because the
                    // predicate can now be false for every weapon at once --
                    // and because a CurrentWeapon that is not in the order at
                    // all leaves currentIndex at -1, which the wraps below can
                    // never land back on.
                    int steps = _weaponOrder.Length;
                    if (Controls.NextWeapon.IsPressed)
                    {
                        do
                        {
                            nextIndex++;
                            if (Controls.ScrollAllWeapons && nextIndex > 8)
                            {
                                nextIndex = 0;
                            }
                            else if (!Controls.ScrollAllWeapons && nextIndex > 7)
                            {
                                nextIndex = 2;
                            }
                            nextBeam = _weaponOrder[nextIndex];
                        }
                        while (nextIndex != currentIndex && --steps > 0 && !CanCycleToWeapon(nextBeam));
                    }
                    else if (Controls.PrevWeapon.IsPressed)
                    {
                        do
                        {
                            nextIndex--;
                            if (Controls.ScrollAllWeapons && nextIndex < 0)
                            {
                                nextIndex = 8;
                            }
                            else if (!Controls.ScrollAllWeapons && nextIndex < 2)
                            {
                                nextIndex = 7;
                            }
                            nextBeam = _weaponOrder[nextIndex];
                        }
                        while (nextIndex != currentIndex && --steps > 0 && !CanCycleToWeapon(nextBeam));
                    }
                    if (nextBeam != CurrentWeapon)
                    {
                        TryEquipWeapon(nextBeam);
                    }
                }
            }
        }

        /// <summary>
        /// Whether next/previous weapon should stop on this beam.
        ///
        /// Availability is not enough, and that was the bug: the cycle used to
        /// skip only weapons the player had not picked up, so it would happily
        /// stop on one that was out of ammo -- where <c>TryEquipWeapon</c>
        /// refuses it, plays the fail sound, and leaves the current weapon
        /// where it was. The next press then computed the same index from the
        /// same starting weapon and stopped on the same empty one, so the
        /// cycle was stuck for good in that direction and the only way past it
        /// was to cycle the other way round.
        ///
        /// The ammo test is <c>TryEquipWeapon</c>'s own, so the two cannot
        /// drift: anything this stops on is something that will equip. An
        /// empty weapon is still reachable by its own key, which is where the
        /// "you have no ammo" message belongs -- it answers a player who asked
        /// for that weapon, rather than one who asked for the next one.
        /// </summary>
        private bool CanCycleToWeapon(BeamType beam)
        {
            if (!_availableWeapons[beam])
            {
                return false;
            }
            WeaponInfo info = WeaponBalanceResolver.SelectWeapon(beam, Hunter);
            int ammo = _ammo[info.AmmoType];
            ushort ammoCost = WeaponBalanceResolver.GetEffectiveAmmoCost(
                info, Hunter, _scene.Match.Balance);
            return beam == BeamType.PowerBeam || ammo == -1 || ammo >= ammoCost;
        }

        private bool EndWeaponMenu()
        {
            bool selected = false;
            if (WeaponSelection != BeamType.None)
            {
                if (WeaponSelection != CurrentWeapon)
                {
                    TryEquipWeapon(WeaponSelection);
                    selected = true;
                }
                else if (IsMainPlayer && Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen))
                {
                    _soundSource.PlayFreeSfx(SfxId.BEAM_SWITCH_FAIL);
                }
                WeaponSelection = CurrentWeapon;
            }
            Flags1 &= ~PlayerFlags1.NoAimInput;
            Flags1 &= ~PlayerFlags1.WeaponMenuOpen;
            return selected;
        }

        private void UpdateAimFacing()
        {
            _gunVec1 = VectorMath.NormalizeOr(_gunVec1, _facingVector);
            _facingVector = ResolveAimFacing(_gunVec1, _facingVector);
        }

        private void UpdateAimFacingAfterInput(float appliedAngle)
        {
            _gunVec1 = VectorMath.NormalizeOr(_gunVec1, _facingVector);
            _facingVector = ResolveAimFacingAfterInput(
                _gunVec1, _facingVector, appliedAngle,
                _scene.Services.DynamicCrosshairTravelDegrees,
                _scene.Services.DynamicCrosshairTurnSpeed);
        }

        internal static Vector3 ResolveAimFacingAfterInput(Vector3 gunVector,
            Vector3 facingVector, float appliedAngle)
            => ResolveAimFacingAfterInput(gunVector, facingVector, appliedAngle,
                DynamicCrosshairTuning.DefaultTravelDegrees,
                DynamicCrosshairTuning.DefaultTurnSpeed);

        internal static Vector3 ResolveAimFacingAfterInput(Vector3 gunVector,
            Vector3 facingVector, float appliedAngle, float travelDegrees,
            float turnSpeed)
        {
            // The legacy mouse/button paths call both aim axes every tick,
            // including a zero delta. Treating that neutral sample as aim
            // motion made the camera continue chasing the gun and pulled the
            // dynamic reticle back to center after mouse, controller, or
            // stylus input.
            // Preserve the authored gun/camera offset until input actually
            // changes an axis.
            if (!float.IsFinite(appliedAngle) || appliedAngle == 0)
            {
                return VectorMath.NormalizeOr(facingVector, gunVector);
            }
            travelDegrees = DynamicCrosshairTuning.TravelDegrees(travelDegrees);
            turnSpeed = DynamicCrosshairTuning.TurnSpeed(turnSpeed);
            if (DynamicCrosshairTuning.UsesLegacyCameraResponse(
                travelDegrees, turnSpeed))
            {
                return ResolveAimFacing(gunVector, facingVector);
            }

            gunVector = VectorMath.NormalizeOr(gunVector, facingVector);
            facingVector = VectorMath.NormalizeOr(facingVector, gunVector);
            float dot = Math.Clamp(Vector3.Dot(gunVector, facingVector), -1, 1);
            float separation = MathF.Acos(dot);
            float travel = MathHelper.DegreesToRadians(travelDegrees);
            if (separation <= travel)
            {
                return facingVector;
            }

            // Once outside the free-aim region, follow the input itself rather
            // than a percentage of the whole gun/camera offset. Percentage
            // chasing can overshoot back inside the threshold, pause for a
            // frame, then jump again. Keeping the result on or outside the
            // boundary gives continuous camera and character-facing motion.
            float maximum = MathHelper.DegreesToRadians(travelDegrees
                + DynamicCrosshairTuning.FollowRangeDegrees);
            float turnStep = MathF.Abs(appliedAngle) * turnSpeed;
            float targetSeparation = Math.Max(travel,
                Math.Min(separation, maximum) - turnStep);
            if (targetSeparation < separation)
            {
                Vector3 tangent = facingVector - gunVector * dot;
                if (!VectorMath.TryNormalize(tangent, out tangent))
                {
                    tangent = VectorMath.Perpendicular(gunVector, facingVector);
                }
                facingVector = MathF.Cos(targetSeparation) * gunVector
                    + MathF.Sin(targetSeparation) * tangent;
            }
            return VectorMath.NormalizeOr(facingVector, gunVector);
        }

        internal static Vector3 ResolveAimFacing(Vector3 gunVector, Vector3 facingVector)
        {
            gunVector = VectorMath.NormalizeOr(gunVector, facingVector);
            facingVector = VectorMath.NormalizeOr(facingVector, gunVector);
            float dot = Vector3.Dot(gunVector, facingVector);
            if (!float.IsFinite(dot))
            {
                dot = 1;
            }
            dot = Math.Clamp(dot, -1, 1);
            if (dot < Fixed.ToFloat(3956))
            {
                Vector3 tangent = facingVector - gunVector * dot;
                if (!VectorMath.TryNormalize(tangent, out tangent))
                {
                    tangent = VectorMath.Perpendicular(gunVector, facingVector);
                }
                facingVector = Fixed.ToFloat(3956) * gunVector + Fixed.ToFloat(1060) * tangent;
            }
            Vector3 temp2 = gunVector - facingVector;
            facingVector += temp2 * 0.1f;
            return VectorMath.NormalizeOr(facingVector, gunVector);
        }

        private void UpdateAimY(float amount)
        {
            if (Controls.InvertAimY)
            {
                amount *= -1;
            }
            float sensitivity = 1;
            if (EquipInfo.Zoomed)
            {
                float normalFov = Fixed.ToFloat(Values.NormalFov) * 2;
                if (normalFov != 0) // zero will occur when the camera info is overridden to normal FOV due to cam seq
                {
                    // constant angular speed while zoomed: turn rate scales with the
                    // FOV reduction, so it tracks the mouse sensitivity setting the
                    // same way unzoomed aim does, instead of a fixed per-hunter ratio
                    sensitivity = CameraInfo.Fov / normalFov;
                }
            }
            amount *= sensitivity;
            // unimpl-controls: these calculations are different when exact aim is not set
            float prevAim = _aimY;
            _aimY += amount;
            if (IsAltForm)
            {
                _aimY = Math.Clamp(_aimY, -25, 5);
            }
            else
            {
                _aimY = Math.Clamp(_aimY, -85, 85);
            }
            float diff = MathHelper.DegreesToRadians(_aimY - prevAim);
            Matrix4 transform = GetTransformMatrix(_gunVec1, Vector3.UnitY);
            Vector3 vector;
            if (diff <= 0)
            {
                vector = new Vector3(0, MathF.Sin(diff), MathF.Cos(diff));
            }
            else
            {
                vector = new Vector3(0, -MathF.Sin(-diff), MathF.Cos(-diff));
            }
            _gunVec1 = VectorMath.NormalizeOr(Matrix.Vec3MultMtx3(vector, transform), _gunVec1);
            _aimPosition = CameraInfo.Position + _gunVec1 * Fixed.ToFloat(Values.AimDistance);
            UpdateAimFacingAfterInput(diff);
        }

        private void UpdateAimX(float amount)
        {
            if (Controls.InvertAimX)
            {
                amount *= -1;
            }
            float sensitivity = 1;
            if (EquipInfo.Zoomed)
            {
                float normalFov = Fixed.ToFloat(Values.NormalFov) * 2;
                if (normalFov != 0) // zero will occur when the camera info is overridden to normal FOV due to cam seq
                {
                    // constant angular speed while zoomed: turn rate scales with the
                    // FOV reduction, so it tracks the mouse sensitivity setting the
                    // same way unzoomed aim does, instead of a fixed per-hunter ratio
                    sensitivity = CameraInfo.Fov / normalFov;
                }
            }
            amount *= sensitivity;
            // unimpl-controls: these calculations are different when exact aim is not set
            float sin;
            float cos;
            float angle = MathHelper.DegreesToRadians(amount);
            if (amount <= 0)
            {
                sin = MathF.Sin(angle);
                cos = MathF.Cos(angle);
            }
            else
            {
                sin = -MathF.Sin(-angle);
                cos = MathF.Cos(-angle);
            }
            float x = _gunVec1.X;
            float z = _gunVec1.Z;
            _gunVec1.X = x * cos + z * sin;
            _gunVec1.Z = x * -sin + z * cos;
            _gunVec1 = VectorMath.NormalizeOr(_gunVec1, _facingVector);
            _aimPosition = CameraInfo.Position + _gunVec1 * Fixed.ToFloat(Values.AimDistance);
            if (EquipInfo.Zoomed)
            {
                _facingVector = _gunVec1;
            }
            else
            {
                UpdateAimFacingAfterInput(angle);
            }
        }

        private void ProcessBiped()
        {
            if (EquipInfo.SmokeLevel < EquipInfo.Weapon.SmokeDrain)
            {
                EquipInfo.SmokeLevel = 0;
            }
            else
            {
                EquipInfo.SmokeLevel -= EquipInfo.Weapon.SmokeDrain;
            }
            Vector3 speedDelta = Vector3.Zero;
            PlayerAnimation anim1 = PlayerAnimation.None;
            PlayerAnimation anim2 = PlayerAnimation.None;
            AnimFlags animFlags1 = AnimFlags.None;
            AnimFlags animFlags2 = AnimFlags.None;
            if (_frozenTimer == 0 && _health > 0 && !_field6D0)
            {
                if (Biped1Anim == PlayerAnimation.Turn)
                {
                    if (Biped1Frame <= Biped1FrameCount / 2)
                    {
                        Biped1Flags |= AnimFlags.Reverse;
                    }
                    else
                    {
                        Biped1Flags &= ~AnimFlags.Reverse;
                    }
                    Biped1Flags |= AnimFlags.NoLoop;
                }
                if (Biped2Anim == PlayerAnimation.Turn)
                {
                    if (Biped2Frame <= Biped2FrameCount / 2)
                    {
                        Biped2Flags |= AnimFlags.Reverse;
                    }
                    else
                    {
                        Biped2Flags &= ~AnimFlags.Reverse;
                    }
                    Biped2Flags |= AnimFlags.NoLoop;
                }
                ApplyModAim();
                if (Controls.MouseAim && !Flags1.TestFlag(PlayerFlags1.NoAimInput) && !IsBot)
                {
                    // The 1/4 was the whole of the sensitivity setting; it is
                    // now the point where one lives (1.0 = this exact feel).
                    float aimY = -Input.MouseDeltaY / 4f * Controls.MouseSensitivity
                        * (Controls.InvertMouseY ? -1 : 1);
                    float aimX = -Input.MouseDeltaX / 4f * Controls.MouseSensitivity
                        * (Controls.InvertMouseX ? -1 : 1);
                    if (_scene.CameraSequences.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true
                        || _scene.FrameAdvance || _scene.FrameAdvanceLastFrame) // skdebug
                    {
                        aimX = aimY = 0;
                    }
                    if (Controls.KeyboardAim && (Controls.AimLeft.IsDown || Controls.AimRight.IsDown
                        || Controls.AimUp.IsDown || Controls.AimDown.IsDown))
                    {
                        aimX = aimY = 0;
                    }
                    if (aimX != 0 || aimY != 0)
                    {
                        Input.HasInput = true;
                    }
                    UpdateHudShiftY(aimY);
                    UpdateHudShiftX(aimX);
                    UpdateAimY(aimY);
                    UpdateAimX(aimX);
                    if (Flags1.TestFlag(PlayerFlags1.Grounded))
                    {
                        // sktodo: threshold values
                        if (aimX > 3)
                        {
                            _timeIdle = 0;
                            anim1 = PlayerAnimation.Turn;
                            animFlags1 = AnimFlags.Reverse;
                            if (Biped2Anim == PlayerAnimation.Turn)
                            {
                                _bipedModel2.AnimInfo.Flags[0] &= ~AnimFlags.NoLoop;
                                _bipedModel2.AnimInfo.Flags[0] |= AnimFlags.Reverse;
                            }
                            if (Biped1Anim == PlayerAnimation.Turn)
                            {
                                _bipedModel1.AnimInfo.Flags[0] &= ~AnimFlags.NoLoop;
                                _bipedModel1.AnimInfo.Flags[0] |= AnimFlags.Reverse;
                            }
                        }
                        else if (aimX < -3)
                        {
                            _timeIdle = 0;
                            anim1 = PlayerAnimation.Turn;
                            if (Biped2Anim == PlayerAnimation.Turn)
                            {
                                _bipedModel2.AnimInfo.Flags[0] &= ~AnimFlags.NoLoop;
                                _bipedModel2.AnimInfo.Flags[0] &= ~AnimFlags.Reverse;
                            }
                            if (Biped1Anim == PlayerAnimation.Turn)
                            {
                                _bipedModel1.AnimInfo.Flags[0] &= ~AnimFlags.NoLoop;
                                _bipedModel1.AnimInfo.Flags[0] &= ~AnimFlags.Reverse;
                            }
                        }
                    }
                }
                if (Controls.KeyboardAim || IsBot)
                {
                    UpdateAimX(_buttonAimX);
                    UpdateAimY(_buttonAimY);
                    if (!IsBot)
                    {
                        // itodo: button aim (see free cam code for FPS-independent stuff)
                    }
                }
                bool jumping = false;
                if (!Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck))
                {
                    float digitalLateralSign = Controls.MoveRight.IsDown ? 1
                        : Controls.MoveLeft.IsDown ? -1 : 0;
                    float digitalForwardSign = Controls.MoveUp.IsDown ? 1
                        : Controls.MoveDown.IsDown ? -1 : 0;
                    float lateralSign = ResolveMovementAxis(digitalLateralSign,
                        _analogMovement.X, horizontal: true);
                    float forwardSign = ResolveMovementAxis(digitalForwardSign,
                        _analogMovement.Y, horizontal: false);
                    // unimpl-controls: the game also tests for either the free strafe flag, or the strafe button held
                    // and later, for up/down, it tests for either flag, or the look button not held

                    void MoveRightLeft(PlayerAnimation walkAnim, float sign)
                    {
                        Flags1 |= PlayerFlags1.Strafing;
                        Flags1 |= PlayerFlags1.MovingBiped;
                        if (Flags1.TestFlag(PlayerFlags1.Standing))
                        {
                            Flags1 |= PlayerFlags1.Walking;
                        }
                        else
                        {
                            Flags1 &= ~PlayerFlags1.Walking;
                        }
                        float traction = Fixed.ToFloat(Values.StrafeBipedTraction);
                        if (_jumpPadControlLockMin > 0)
                        {
                            traction *= Fixed.ToFloat(Values.JumpPadSlideFactor);
                        }
                        else if (Flags1.TestFlag(PlayerFlags1.Standing) && _slipperiness != 0)
                        {
                            traction *= Metadata.TractionFactors[_slipperiness];
                        }
                        speedDelta.X -= _field78 * traction * sign;
                        speedDelta.Z -= _field7C * traction * sign;
                        if (forwardSign == 0
                            && Flags1.TestFlag(PlayerFlags1.Grounded) && _timeSinceJumpPad > SimTicks.From30HzFrames(7))
                        {
                            anim1 = walkAnim;
                        }
                        if (!EquipInfo.Zoomed)
                        {
                            _viewTiltAngleH += Fixed.ToFloat(Values.ViewTiltIncrement) * sign / 2; // todo: FPS stuff
                            _viewTiltAngleH = Math.Clamp(_viewTiltAngleH, -180, 180);
                        }
                    }

                    void MoveForwardBack(PlayerAnimation walkAnim, float sign)
                    {
                        Flags1 |= PlayerFlags1.MovingBiped;
                        if (Flags1.TestFlag(PlayerFlags1.Standing))
                        {
                            Flags1 |= PlayerFlags1.Walking;
                        }
                        else
                        {
                            Flags1 &= ~PlayerFlags1.Walking;
                        }
                        float traction = Fixed.ToFloat(Values.WalkBipedTraction);
                        if (_jumpPadControlLockMin > 0)
                        {
                            traction *= Fixed.ToFloat(Values.JumpPadSlideFactor);
                        }
                        else if (Flags1.TestFlag(PlayerFlags1.Standing) && _slipperiness != 0)
                        {
                            traction *= Metadata.TractionFactors[_slipperiness];
                        }
                        speedDelta.X += _field70 * traction * sign;
                        speedDelta.Z += _field74 * traction * sign;
                        if (Flags1.TestFlag(PlayerFlags1.Grounded) && _timeSinceJumpPad > SimTicks.From30HzFrames(7))
                        {
                            anim1 = walkAnim;
                        }
                        if (!EquipInfo.Zoomed)
                        {
                            _viewTiltAngleV += Fixed.ToFloat(Values.ViewTiltIncrement) * sign / 2; // todo: FPS stuff
                            _viewTiltAngleV = Math.Clamp(_viewTiltAngleV, -180, 180);
                        }
                    }

                    if (lateralSign > 0)
                    {
                        MoveRightLeft(PlayerAnimation.WalkRight, sign: lateralSign);
                    }
                    else if (lateralSign < 0)
                    {
                        MoveRightLeft(PlayerAnimation.WalkLeft, sign: lateralSign);
                    }
                    if (_viewTiltAngleH < Fixed.ToFloat(500) && _viewTiltAngleH > Fixed.ToFloat(-500))
                    {
                        _viewTiltAngleH = 0;
                    }
                    else
                    {
                        _viewTiltAngleH *= 0.9f; // sktodo: FPS stuff
                    }
                    if (forwardSign > 0)
                    {
                        MoveForwardBack(PlayerAnimation.WalkForward, sign: forwardSign);
                    }
                    else if (forwardSign < 0)
                    {
                        MoveForwardBack(PlayerAnimation.WalkBackward, sign: forwardSign);
                    }
                    if (_viewTiltAngleV < Fixed.ToFloat(500) && _viewTiltAngleV > Fixed.ToFloat(-500))
                    {
                        _viewTiltAngleV = 0;
                    }
                    else
                    {
                        _viewTiltAngleV *= 0.9f; // sktodo: FPS stuff
                    }
                    if (_scene.Features.Cheats.UnlimitedJumps)
                    {
                        Flags1 &= ~PlayerFlags1.UsedJump;
                    }
                    // unimpl-controls: in the up/down code path, the game processes aim reset if that flag is off
                    // unimpl-controls: the aim input disable flag is also checked by the game
                    if (_jumpPadControlLockMin == 0 && Controls.Jump.IsPressed && !Flags1.TestFlag(PlayerFlags1.UsedJump))
                    {
                        // unimpl-controls: double tap jump is hard coded as an alternate condition to the jump input
                        jumping = true;
                        if (!Flags1.TestFlag(PlayerFlags1.Standing) || !_abilities.TestFlag(AbilityFlags.SpaceJump))
                        {
                            Flags1 |= PlayerFlags1.UsedJump;
                        }
                        if (IsPrimeHunter)
                        {
                            Speed = Speed.WithY(0.35f); // todo: FPS stuff?
                        }
                        else
                        {
                            Speed = Speed.WithY(Fixed.ToFloat(Values.JumpSpeed)); // todo: FPS stuff?
                        }
                        _timeSinceGrounded = (ushort)SimTicks.From30HzFrames(8);
                        PlayHunterSfx(HunterSfx.Jump);
                    }
                }
                // unimpl-controls: the game attempts to play free look SFX, but they don't exist
                // --> it does this in between L/R/U/D and jump, while we have jump in the condition above
                if (jumping || _timeSinceJumpPad == 1)
                {
                    animFlags1 = AnimFlags.NoLoop;
                    if (Controls.MoveUp.IsDown)
                    {
                        anim1 = PlayerAnimation.JumpForward;
                    }
                    else if (Controls.MoveDown.IsDown)
                    {
                        anim1 = PlayerAnimation.JumpBack;
                    }
                    else if (Controls.MoveLeft.IsDown)
                    {
                        anim1 = PlayerAnimation.JumpLeft;
                    }
                    else if (Controls.MoveRight.IsDown)
                    {
                        anim1 = PlayerAnimation.JumpRight;
                    }
                    else
                    {
                        anim1 = PlayerAnimation.JumpNeutral;
                    }
                }
            }
            ProcessMovement();
            UpdateCamera();
            ModRefreshNetworkAim();
            UpdateAimVecs();
            if (_frozenTimer == 0 && _health > 0 && !_field6D0)
            {
                if (!IsUnmorphing)
                {
                    if (!Controls.Shoot.IsDown)
                    {
                        Flags2 &= ~PlayerFlags2.Shooting;
                    }
                    else if (Controls.Shoot.IsPressed || !Flags2.TestFlag(PlayerFlags2.NoShotsFired))
                    {
                        Flags2 |= PlayerFlags2.Shooting;
                        Flags2 &= ~PlayerFlags2.NoShotsFired;
                    }
                    if (!_availableCharges[CurrentWeapon] || !EquipWeapon.Flags.TestFlag(WeaponFlags.CanCharge))
                    {
                        EquipInfo.ChargeLevel = 0;
                    }
                    else
                    {
                        bool releaseCharge = false;
                        if (!Flags2.TestFlag(PlayerFlags2.Shooting) || EquipInfo.Ammo < EquipInfo.ChargeCost)
                        {
                            releaseCharge = true; // charge released/insufficient
                        }
                        else
                        {
                            if (EquipInfo.ChargeLevel > 0 && GunAnimation != GunAnimation.MissileClose)
                            {
                                // the game doesn't need this condition, but we do because "the next frame will
                                // overwrite it" type stuff isn't guaranteed to get in ahead of the audio system
                                if (CurrentWeapon != BeamType.PowerBeam
                                    || EquipInfo.ChargeLevel >= SimTicks.From30HzFrames(EquipInfo.Weapon.MinCharge))
                                {
                                    PlayBeamChargeSfx(CurrentWeapon);
                                }
                                if (Biped2Flags.TestFlag(AnimFlags.Ended) || Biped2Anim == PlayerAnimation.Charge
                                    || Biped2Anim == PlayerAnimation.Shoot && Biped2Frame > 8)
                                {
                                    anim2 = PlayerAnimation.Charge;
                                }
                            }
                            if (EquipInfo.ChargeLevel >= SimTicks.From30HzFrames(EquipWeapon.FullCharge))
                            {
                                EquipInfo.SmokeLevel += EquipWeapon.SmokeChargeAmount;
                                EquipInfo.SmokeLevel = (ushort)Math.Min(EquipInfo.SmokeLevel, EquipWeapon.SmokeStart * 2); // todo: FPS stuff
                            }
                            else
                            {
                                EquipInfo.ChargeLevel++;
                                int minCharge = SimTicks.From30HzFrames(EquipWeapon.MinCharge);
                                if (EquipInfo.ChargeLevel > minCharge)
                                {
                                    int fullCharge = SimTicks.From30HzFrames(EquipWeapon.FullCharge);
                                    int chargeCost = EquipInfo.ChargeCost * 2; // todo: FPS stuff
                                    int minCost = EquipInfo.MinChargeCost * 2; // todo: FPS stuff
                                    int cost = minCost + (chargeCost - minCost) * (EquipInfo.ChargeLevel - minCharge) / (fullCharge - minCharge);
                                    if (EquipInfo.Ammo < cost / 2) // todo: FPS stuff
                                    {
                                        EquipInfo.ChargeLevel--;
                                    }
                                }
                            }
                            // todo?: auto release
                        }
                        if (releaseCharge)
                        {
                            StopBeamChargeSfx(CurrentWeapon);
                            if (EquipInfo.ChargeLevel >= SimTicks.From30HzFrames(EquipWeapon.MinCharge))
                            {
                                TryFireWeapon();
                                anim2 = PlayerAnimation.ChargeShoot;
                                animFlags2 = AnimFlags.NoLoop;
                            }
                            EquipInfo.ChargeLevel = 0;
                        }
                    }
                    if (EquipWeapon.Flags.TestFlag(WeaponFlags.CanZoom))
                    {
                        if (Controls.Zoom.IsPressed)
                        {
                            UpdateZoom(!EquipInfo.Zoomed);
                        }
                        if (EquipInfo.Zoomed && _scene.CameraSequences.Current == null)
                        {
                            // note: the game does this during cam seqs, resulting in the FOV thrashing a bit, but it has no visible effect
                            // since the sin/cos values for projection are set aside in the cam info update that's already occurred above.
                            float zoomFov = Fixed.ToFloat(EquipInfo.Weapon.ZoomFov);
                            Vector3 facing = _facingVector;

                            void CheckZoomTargets(EntityType type)
                            {
                                foreach (EntityBase entity in _scene.Entities)
                                {
                                    if (entity.Type != type || entity == this || !entity.GetTargetable())
                                    {
                                        continue;
                                    }
                                    if (entity.Type == EntityType.Object
                                        && !((ObjectEntity)entity).Data.EffectFlags.TestFlag(ObjEffFlags.WeaponZoom))
                                    {
                                        continue;
                                    }
                                    entity.GetPosition(out Vector3 position);
                                    Vector3 between = position - Position;
                                    float dot = Vector3.Dot(between, facing);
                                    if (dot > 1 && dot / between.Length >= Fixed.ToFloat(4074))
                                    {
                                        float angle = MathHelper.RadiansToDegrees(MathF.Atan2(3, dot));
                                        if (angle < zoomFov)
                                        {
                                            zoomFov = angle;
                                        }
                                    }
                                }
                            }

                            CheckZoomTargets(EntityType.Player);
                            CheckZoomTargets(EntityType.ForceFieldLock);
                            CheckZoomTargets(EntityType.Object);
                            zoomFov *= 2;
                            CameraInfo.Fov = ZoomFovTransition.StepToward(
                                CameraInfo.Fov, zoomFov);
                        }
                    }
                    if (Controls.Shoot.IsPressed && EquipInfo.ChargeLevel <= SimTicks.From30HzFrames(1)
                        || EquipWeapon.Flags.TestFlag(WeaponFlags.RepeatFire) && Flags2.TestFlag(PlayerFlags2.Shooting)
                        && (!EquipWeapon.Flags.TestFlag(WeaponFlags.CanCharge) || EquipInfo.ChargeLevel < SimTicks.From30HzFrames(EquipWeapon.MinCharge)))
                    {
                        if (TryFireWeapon())
                        {
                            anim2 = PlayerAnimation.Shoot;
                            animFlags2 |= AnimFlags.NoLoop;
                            if (Biped2Anim == PlayerAnimation.Shoot)
                            {
                                SetBiped2Animation(PlayerAnimation.Shoot, Biped2Flags);
                            }
                        }
                    }
                    // the game doesn't require pressed here, but presumably the control scheme would have the pressed flag
                    // todo: use the ability flag for the morph touch button too, even though the game doesn't
                    if (!Flags2.TestFlag(PlayerFlags2.BipedStuck) && _abilities.TestFlag(AbilityFlags.AltForm)
                        && Controls.Morph.IsPressed || IsMainPlayer && _scene.CameraSequences.Current?.ForceAlt == true)
                    {
                        bool switched = TrySwitchForms();
                        if (ShouldApplyMorphAnimation(switched, IsMorphing))
                        {
                            if (switched && IsMainPlayer && IsMorphing)
                            {
                                // the game only does this when using the touch screen button, but this is equivalent,
                                // and we want to call this beause it updates the reticle expansion
                                HudOnMorphStart();
                            }
                            anim1 = PlayerAnimation.Morph;
                            anim2 = PlayerAnimation.Morph;
                        }
                    }
                }
                speedDelta = ApplyEnhancedChillAcceleration(speedDelta,
                    _chilledTicks);
                float magBefore = MathF.Sqrt(Speed.X * Speed.X + Speed.Z * Speed.Z);
                Speed += speedDelta; // todo: FPS stuff?
                float magAfter = MathF.Sqrt(Speed.X * Speed.X + Speed.Z * Speed.Z);
                if (magAfter > magBefore && magAfter > _hSpeedCap)
                {
                    float factor;
                    if (magBefore <= _hSpeedCap)
                    {
                        factor = _hSpeedCap / magAfter;
                    }
                    else
                    {
                        factor = magBefore / magAfter;
                    }
                    Speed = Speed.WithX(Speed.X * factor).WithZ(Speed.Z * factor);
                }
                if (EquipInfo.Zoomed)
                {
                    Vector3 diff = _gunVec1 - _facingVector;
                    _facingVector += diff * 0.3f / 2; // todo: FPS stuff
                    _facingVector = VectorMath.NormalizeOr(_facingVector, _gunVec1);
                }
                // Authoritative replicas have no movement Controls on this client;
                // their delayed presentation sample supplies only the local
                // animation decision. Physics, movement flags, and control state
                // remain untouched.
                bool remoteBipedAnimation = false;
                if (anim1 == PlayerAnimation.None
                    && _desiredRemoteBipedAnimation != PlayerAnimation.None
                    && CanApplySnapshotBipedAnimation(Biped1Anim, Biped1Flags))
                {
                    anim1 = _desiredRemoteBipedAnimation;
                    remoteBipedAnimation = true;
                    if (IsSnapshotJumpAnimation(anim1))
                    {
                        animFlags1 = AnimFlags.NoLoop;
                    }
                }
                if (anim1 == PlayerAnimation.None)
                {
                    if (Flags1.TestFlag(PlayerFlags1.Grounded))
                    {
                        if (Biped1Anim == PlayerAnimation.Idle)
                        {
                            if (++_timeIdle > SimTicks.From30HzFrames(300) && _timeSinceInput > (ulong)SimTicks.From30HzFrames(300))
                            {
                                SetBiped1Animation(PlayerAnimation.Flourish, AnimFlags.NoLoop);
                            }
                        }
                        else if (!Biped1Flags.TestFlag(AnimFlags.NoLoop) || Biped1Flags.TestFlag(AnimFlags.Ended))
                        {
                            _timeIdle = 0;
                            SetBiped1Animation(PlayerAnimation.Idle, AnimFlags.None);
                        }
                    }
                }
                else if (ShouldSetBipedLocomotionAnimation(anim1,
                    Biped1Anim, remoteBipedAnimation))
                {
                    SetBiped1Animation(anim1, animFlags1);
                }
                if (anim2 == PlayerAnimation.None)
                {
                    if ((!Biped2Flags.TestFlag(AnimFlags.NoLoop) || Biped2Flags.TestFlag(AnimFlags.Ended)) && Biped2Anim != Biped1Anim)
                    {
                        SetBiped2Animation(Biped1Anim, Biped1Flags);
                        _bipedModel2.AnimInfo.Frame[0] = Biped1Frame;
                    }
                }
                else if (anim2 != Biped2Anim)
                {
                    SetBiped2Animation(anim2, animFlags2);
                }
            }
        }

        private bool TryFireWeapon()
        {
            if (!Flags2.TestFlag(PlayerFlags2.Cloaking))
            {
                _cloakTimer = 0;
            }
            bool pressed = Controls.Shoot.IsPressed;
            if (pressed || CurrentWeapon != BeamType.PowerBeam)
            {
                _autofireCooldown = (ushort)SimTicks.From30HzFrames(EquipWeapon.AutofireCooldown);
                _powerBeamAutofire = 0;
            }
            else
            {
                if (_powerBeamAutofire < UInt16.MaxValue)
                {
                    _powerBeamAutofire++;
                }
                // basically adds 0, 1, or 2 to the base autofire cooldown depending on how long the PB has repeated fire
                // --> could add more, but the min charge is reaached quickly
                int pbAuto = Math.Min(_powerBeamAutofire / 2, 90); // todo: FPS stuff
                pbAuto = (int)(pbAuto * 15 / 90f);
                _autofireCooldown = (ushort)SimTicks.From30HzFrames(pbAuto + EquipWeapon.AutofireCooldown);
            }
            if ((_timeSinceShot < SimTicks.From30HzFrames(EquipWeapon.ShotCooldown)
                || !pressed && _timeSinceShot < _autofireCooldown)
                && (!IsBot || !AiData.Flags2.TestFlag(AiFlags2.Bit20)))
            {
                return false;
            }
            if (GunAnimation == GunAnimation.UpDown)
            {
                return false;
            }
            Vector3 shotOrigin = _muzzlePos;
            Vector3 shotVec = _aimPosition - _muzzlePos;
            if (shotOrigin != _muzzlePos)
            {
            }
            if (_disruptedTimer > 0)
            {
                // random values between -3 and 3
                shotVec.X += Fixed.ToFloat((int)_scene.Random.GetRandomInt2(24576) - 12288);
                shotVec.Y += Fixed.ToFloat((int)_scene.Random.GetRandomInt2(24576) - 12288);
                shotVec.Z += Fixed.ToFloat((int)_scene.Random.GetRandomInt2(24576) - 12288);
            }
            shotVec = VectorMath.NormalizeOr(shotVec, _gunVec1);
            WeaponInfo curWeapon = EquipInfo.Weapon;
            if (IsPrimeHunter)
            {
                // Prime Hunter shots use the affinity table entry for the
                // selected beam. Reapply the profile for the temporary equip
                // so cost and projectile values remain authoritative, then
                // restore the player's actual equip below.
                EquipInfo.Weapon = WeaponBalanceResolver.SelectWeapon(
                    CurrentWeapon, Hunter, forceAffinity: true);
                WeaponBalanceResolver.Apply(EquipInfo, Hunter,
                    _scene.Match.Balance);
            }
            BeamSpawnFlags flags = BeamSpawnFlags.NoMuzzle;
            if (_doubleDmgTimer > 0)
            {
                flags |= BeamSpawnFlags.DoubleDamage;
            }
            else if (IsPrimeHunter)
            {
                flags |= BeamSpawnFlags.PrimeHunter;
            }
            BeamResultFlags result = BeamProjectileEntity.Spawn(this, EquipInfo, shotOrigin, shotVec, flags, NodeRef, _scene);
            _scene.Services.NoteFired(this, shotVec, _gunVec1);
            if (result == BeamResultFlags.NoSpawn)
            {
                EquipInfo.Weapon = curWeapon;
                WeaponBalanceResolver.Apply(EquipInfo, Hunter,
                    _scene.Match.Balance);
                PlayBeamEmptySfx(EquipInfo.Weapon.Beam);
                return false;
            }
            NoteOffensiveAction();
            // todo: update license stats
            _timeSinceShot = 0;
            if (IsMainPlayer)
            {
                HudOnFiredShot();
            }
            if (CurrentWeapon == BeamType.Missile)
            {
                Flags1 |= PlayerFlags1.ShotMissile;
            }
            if (EquipInfo.ChargeLevel < SimTicks.From30HzFrames(EquipWeapon.MinCharge))
            {
                Flags1 |= PlayerFlags1.ShotUncharged;
            }
            else
            {
                Flags1 |= PlayerFlags1.ShotCharged;
            }
            if (_muzzleEffect == null || !EquipWeapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                if (_muzzleEffect != null)
                {
                    _scene.UnlinkEffectEntry(_muzzleEffect);
                    _muzzleEffect = null;
                }
                int effectId = Metadata.MuzzleEffectIds[(int)CurrentWeapon];
                _muzzleEffect = _scene.SpawnEffectGetEntry(effectId, _gunVec2, _gunVec1, _muzzlePos);
                if (_muzzleEffect != null && !IsMainPlayer)
                {
                    _muzzleEffect.SetDrawEnabled(false);
                }
            }
            bool charged;
            if (EquipInfo.Weapon.Flags.TestFlag(WeaponFlags.PartialCharge))
            {
                charged = Flags1.TestFlag(PlayerFlags1.ShotCharged);
            }
            else
            {
                charged = EquipInfo.ChargeLevel >= SimTicks.From30HzFrames(EquipInfo.Weapon.FullCharge);
            }
            bool continuous = EquipInfo.Weapon.Flags.TestFlag(WeaponFlags.Continuous);
            bool homing = result.TestFlag(BeamResultFlags.Homing);
            float amountA = 0x3FFF * _shockCoilTimer / (float)SimTicks.Hz;
            PlayBeamShotSfx(EquipInfo.Weapon.Beam, charged, continuous, homing, amountA);
            if (EquipInfo.Weapon.Beam == BeamType.Imperialist && EquipInfo.Ammo >= EquipInfo.AmmoCost)
            {
                _soundSource.PlaySfx(SfxId.SNIPER_RELOAD);
            }
            EquipInfo.Weapon = curWeapon;
            WeaponBalanceResolver.Apply(EquipInfo, Hunter, _scene.Match.Balance);
            UnequipOmegaCannon(); // todo?: set the flag if wifi
            return true;
        }



        private void ProcessAlt(in BoostIntent boostIntent)
        {
            Vector3 speedDelta = Vector3.Zero;
            int animId = -1;
            AnimFlags animFlags = AnimFlags.None;
            bool animRequiresMovement = false;
            Flags1 |= PlayerFlags1.UsedJump;
            if (_frozenTimer == 0 && _health > 0)
            {
                // todo?: if touch movement for alt form was a thing, this would need extra conditions
                if ((!Controls.RollRight.IsDown && !Controls.RolltLeft.IsDown && !Controls.RollUp.IsDown && !Controls.RollDown.IsDown)
                    || Controls.RollRight.IsPressed || Controls.RolltLeft.IsPressed || Controls.RollUp.IsPressed || Controls.RollDown.IsPressed)
                {
                    Flags1 &= ~PlayerFlags1.AltDirOverride;
                }
                if (IsBot && _timeSinceMorphCamera > SimTicks.From30HzFrames(10) && !Flags1.TestFlag(PlayerFlags1.AltDirOverride)
                    && (MathF.Abs(CameraInfo.Field48) >= 1 / 4096f || MathF.Abs(CameraInfo.Field4C) >= 1 / 4096f))
                {
                    _altRollFbX = CameraInfo.Field48;
                    _altRollFbZ = CameraInfo.Field4C;
                    _altRollLrX = CameraInfo.Field50;
                    _altRollLrZ = CameraInfo.Field54;
                }
                // todo?: field35C targeting(?) stuff

                void UpdateAnimation(float aimX, float aimY)
                {
                    if ((Hunter == Hunter.Trace || Hunter == Hunter.Weavel)
                        && HasPhysicalAltGroundContact(Flags1))
                    {
                        // sktodo: threshold values
                        if (aimX > 3)
                        {
                            _timeIdle = 0;
                            animId = (int)WeavelAltAnim.Turn; // or TraceAltAnim.MoveBackward
                            animFlags = AnimFlags.Reverse;
                            animRequiresMovement = false;
                            if (_altModel.AnimInfo.Index[0] == animId)
                            {
                                _altModel.AnimInfo.Flags[0] &= ~AnimFlags.NoLoop;
                                _altModel.AnimInfo.Flags[0] |= AnimFlags.Reverse;
                            }
                        }
                        else if (aimX < -3)
                        {
                            _timeIdle = 0;
                            animId = (int)WeavelAltAnim.Turn; // or TraceAltAnim.MoveBackward
                            animFlags = AnimFlags.None;
                            animRequiresMovement = false;
                            if (_altModel.AnimInfo.Index[0] == animId)
                            {
                                _altModel.AnimInfo.Flags[0] &= ~AnimFlags.NoLoop;
                                _altModel.AnimInfo.Flags[0] &= ~AnimFlags.Reverse;
                            }
                        }
                    }
                }

                if (UsesStrafeAltMovement)
                {
                    // Trace, Sylux, Weavel, and Project Prime's Guardian
                    // adaptation. Guardian uses the generic grounded
                    // movement/collision pass even though its authored value
                    // does not advertise the retail strafe flag; this keeps
                    // Psycho Bit inside the normal alternate-form envelope.
                    // The pad's stick and a remote player's relayed aim, in
                    // the same place the mouse's goes in -- as in ProcessBiped,
                    // which was the only caller until now. An alt form that
                    // can aim at all is one of these three, and for them this
                    // is the same turn the mouse makes: without it a pad could
                    // walk and shoot in alt form but not look, and a puppet in
                    // alt form faced wherever its last snapshot left it.
                    ApplyModAim();
                    // ApplyModAim has already updated the gun and camera. Feed
                    // that exact applied delta into Trace/Weavel's established
                    // turn-animation decision without rotating the camera a
                    // second time.
                    Vector2 appliedLocalLook = ModTakeAppliedLocalLook();
                    if (appliedLocalLook != Vector2.Zero)
                    {
                        UpdateAnimation(appliedLocalLook.X, appliedLocalLook.Y);
                    }
                    if (Controls.MouseAim && !Flags1.TestFlag(PlayerFlags1.NoAimInput) && !IsBot)
                    {
                        float aimY = -Input.MouseDeltaY / 4f * Controls.MouseSensitivity
                            * (Controls.InvertMouseY ? -1 : 1);
                        float aimX = -Input.MouseDeltaX / 4f * Controls.MouseSensitivity
                            * (Controls.InvertMouseX ? -1 : 1);
                        if (_scene.CameraSequences.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true
                            || _scene.FrameAdvance || _scene.FrameAdvanceLastFrame) // skdebug
                        {
                            aimX = aimY = 0;
                        }
                        if (aimX != 0 || aimY != 0)
                        {
                            Input.HasInput = true;
                        }
                        UpdateHudShiftY(aimY);
                        UpdateHudShiftX(aimX);
                        UpdateAimY(aimY);
                        UpdateAimX(aimX);
                        UpdateAnimation(aimX, aimY);
                    }
                    if (Controls.KeyboardAim || IsBot)
                    {
                        UpdateAimX(_buttonAimX);
                        UpdateAimY(_buttonAimY);
                        UpdateAnimation(_buttonAimX, _buttonAimY);
                        if (!IsBot)
                        {
                            // itodo: button aim (see free cam code for FPS-independent stuff)
                        }
                    }
                    if (!Flags2.TestFlag(PlayerFlags2.BipedLock) && (Hunter != Hunter.Trace || !Flags2.TestFlag(PlayerFlags2.AltAttack)))
                    {
                        float digitalLateralSign = Controls.MoveRight.IsDown ? 1
                            : Controls.MoveLeft.IsDown ? -1 : 0;
                        float digitalForwardSign = Controls.MoveUp.IsDown ? 1
                            : Controls.MoveDown.IsDown ? -1 : 0;
                        float lateralSign = ResolveMovementAxis(digitalLateralSign,
                            _analogMovement.X, horizontal: true, roll: false);
                        float forwardSign = ResolveMovementAxis(digitalForwardSign,
                            _analogMovement.Y, horizontal: false, roll: false);
                        // unimpl-controls: the game also tests for either the free strafe flag, or the strafe button held
                        // and later, for up/down, it tests for either flag, or the look button not held

                        void MoveRightLeft(float sign)
                        {
                            Flags1 |= PlayerFlags1.Strafing;
                            Flags1 |= PlayerFlags1.MovingBiped;
                            if (Flags1.TestFlag(PlayerFlags1.Standing))
                            {
                                Flags1 |= PlayerFlags1.Walking;
                            }
                            else
                            {
                                Flags1 &= ~PlayerFlags1.Walking;
                            }
                            float traction = Fixed.ToFloat(Values.StrafeBipedTraction);
                            if (_jumpPadControlLockMin > 0)
                            {
                                traction *= Fixed.ToFloat(Values.JumpPadSlideFactor);
                            }
                            speedDelta.X -= _field78 * traction * sign;
                            speedDelta.Z -= _field7C * traction * sign;
                            if (!EquipInfo.Zoomed)
                            {
                                // todo: update field684 (using sign)
                            }
                        }

                        void MoveForwardBack(float sign)
                        {
                            Flags1 |= PlayerFlags1.MovingBiped;
                            if (Flags1.TestFlag(PlayerFlags1.Standing))
                            {
                                Flags1 |= PlayerFlags1.Walking;
                            }
                            else
                            {
                                Flags1 &= ~PlayerFlags1.Walking;
                            }
                            float traction = Fixed.ToFloat(Values.WalkBipedTraction);
                            if (_jumpPadControlLockMin > 0)
                            {
                                traction *= Fixed.ToFloat(Values.JumpPadSlideFactor);
                            }
                            else if (Flags1.TestFlag(PlayerFlags1.Standing) && _slipperiness != 0)
                            {
                                traction *= Metadata.TractionFactors[_slipperiness];
                            }
                            speedDelta.X += _field70 * traction * sign;
                            speedDelta.Z += _field74 * traction * sign;
                            if (!EquipInfo.Zoomed)
                            {
                                // todo: update field688 (using sign)
                            }
                        }

                        if (lateralSign > 0)
                        {
                            MoveRightLeft(sign: lateralSign);
                        }
                        else if (lateralSign < 0)
                        {
                            MoveRightLeft(sign: lateralSign);
                        }
                        // todo: update field684
                        if (forwardSign > 0)
                        {
                            MoveForwardBack(sign: forwardSign);
                        }
                        else if (forwardSign < 0)
                        {
                            MoveForwardBack(sign: forwardSign);
                        }
                        int movementAnim = SelectAltMovementAnimation(Hunter,
                            lateralSign, forwardSign);
                        if (movementAnim >= 0)
                        {
                            animId = movementAnim;
                            animFlags = AnimFlags.None;
                            animRequiresMovement = true;
                        }
                        // todo: update field684
                        // unimpl-controls: in the up/down code path, the game processes aim reset if that flag is off
                    }
                }
                else
                {
                    // Samus, Kanden, Spire, Noxus
                    // todo: touch roll
                    // Rolling forms use the camera basis as their movement
                    // basis. They previously skipped the shared look stream,
                    // so mouse/stick/stylus motion had no effect and the
                    // retained basis could keep accelerating in an old
                    // direction. Consume the same look frame as biped/strafe
                    // forms, then rotate both the orbit and movement basis in
                    // one deterministic step before reading movement.
                    ApplyModAim();
                    Vector2 rollingLook = ModTakeAppliedLocalLook();
                    if (Controls.KeyboardAim || IsBot)
                    {
                        UpdateAimX(_buttonAimX);
                        UpdateAimY(_buttonAimY);
                        if (!IsBot)
                        {
                            rollingLook += new Vector2(_buttonAimX, _buttonAimY);
                        }
                    }
                    // Only network-controlled rolling forms rebuild this
                    // basis from the heading sent in InputCommand.Aim. Local
                    // control rotates the retained basis from explicit yaw in
                    // ApplyRollingAltLook; ProcessMovement is free to point
                    // the rolling model direction with velocity without feeding
                    // it back into the retained control aim or WASD.
                    ModRefreshRollingAltControlBasis();
                    ApplyRollingAltLook(rollingLook);
                    float traction = Fixed.ToFloat(Values.RollAltTraction);
                    if (_jumpPadControlLockMin > 0)
                    {
                        traction *= Fixed.ToFloat(Values.JumpPadSlideFactor);
                    }
                    float rollForwardSign = ResolveMovementAxis(
                        Controls.RollUp.IsDown ? 1 : Controls.RollDown.IsDown ? -1 : 0,
                        _analogMovement.Y, horizontal: false, roll: true);
                    float rollLateralSign = ResolveMovementAxis(
                        Controls.RollRight.IsDown ? 1 : Controls.RolltLeft.IsDown ? -1 : 0,
                        _analogMovement.X, horizontal: true, roll: true);
                    speedDelta += ResolveRollingAltMovement(
                        new Vector3(_altRollFbX, 0, _altRollFbZ),
                        new Vector3(_altRollLrX, 0, _altRollLrZ),
                        rollForwardSign, rollLateralSign, traction);
                }
                // Chill affects only acceleration produced by ordinary movement
                // controls. Apply it before attacks and boosts add their impulses.
                speedDelta = ApplyEnhancedChillAcceleration(speedDelta,
                    _chilledTicks);
                if (!IsMorphing)
                {
                    if (_abilities.TestFlag(AbilityFlags.Bombs) && Controls.AltAttack.IsPressed
                        && _bombAmmo > 0 && _bombCooldown == 0 && _field35C == null)
                    {
                        SpawnBomb();
                    }
                    if (_abilities.TestFlag(AbilityFlags.NoxusAltAttack))
                    {
                        if (Controls.AltAttack.IsDown)
                        {
                            if (Controls.AltAttack.IsPressed)
                            {
                                _altAttackTime = 1;
                                _altModel.SetAnimation((int)NoxusAltAnim.Extend, AnimFlags.NoLoop);
                            }
                            else if (_altAttackTime > 0)
                            {
                                _altAttackTime++;
                                if (_altAttackTime == SimTicks.From30HzFrames(7))
                                {
                                    _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK1);
                                }
                                else
                                {
                                    int startupTime = SimTicks.From30HzFrames(Values.AltAttackStartup);
                                    if (_altAttackTime == startupTime / 2)
                                    {
                                        _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK2, loop: true);
                                    }
                                    else if (_altAttackTime >= startupTime)
                                    {
                                        _altAttackTime = (ushort)startupTime;
                                        Flags2 |= PlayerFlags2.AltAttack;
                                        NoteOffensiveAction();
                                    }
                                }
                            }
                            _altModel.AnimInfo.Frame[0] = (_altAttackTime / 2 * _altModel.AnimInfo.FrameCount[0] - 1)
                                / Values.AltAttackStartup; // todo: FPS stuff ^
                        }
                        else
                        {
                            EndAltAttack();
                        }
                    }
                    if (_abilities.TestFlag(AbilityFlags.SpireAltAttack))
                    {
                        if (Flags2.TestFlag(PlayerFlags2.AltAttack))
                        {
                            if (_altModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
                            {
                                EndAltAttack();
                            }
                        }
                        else if (Controls.AltAttack.IsPressed)
                        {
                            NoteOffensiveAction();
                            BeginSpireAltAttack();
                        }
                    }
                    if (_abilities.TestFlag(AbilityFlags.TraceAltAttack))
                    {
                        if (Flags2.TestFlag(PlayerFlags2.AltAttack) || _altAttackCooldown > 0)
                        {
                            if (HasPhysicalAltGroundContact(Flags1))
                            {
                                EndAltAttack();
                            }
                        }
                        else if (Controls.AltAttack.IsPressed)
                        {
                            Flags2 |= PlayerFlags2.AltAttack;
                            NoteOffensiveAction();
                            float attackHSpeed = Fixed.ToFloat(Values.LungeHSpeed);
                            float attackVSpeed = Fixed.ToFloat(Values.LungeVSpeed);
                            float accelX = _field70 * attackHSpeed;
                            float accelZ = _field74 * attackHSpeed;
                            if (_field70 * Speed.X + _field74 * Speed.Z < attackHSpeed)
                            {
                                Speed = Speed.WithX(accelX).WithZ(accelZ);
                            }
                            _accelerationTimer = (ushort)SimTicks.From30HzFrames(6);
                            Acceleration = new Vector3(accelX, 0, accelZ);
                            if (Speed.Y < attackVSpeed)
                            {
                                float newYSpeed = Speed.Y + attackVSpeed;
                                if (newYSpeed > attackVSpeed)
                                {
                                    newYSpeed = attackVSpeed;
                                }
                                Speed = Speed.WithY(newYSpeed);
                            }
                            animId = (int)TraceAltAnim.Attack;
                            animFlags = AnimFlags.NoLoop;
                            animRequiresMovement = false;
                            _soundSource.PlaySfx(SfxId.TRACE_ALT_ATTACK);
                        }
                    }
                    if (_abilities.TestFlag(AbilityFlags.WeavelAltAttack))
                    {
                        if (Flags2.TestFlag(PlayerFlags2.AltAttack) || _altAttackCooldown > 0)
                        {
                            if (HasPhysicalAltGroundContact(Flags1))
                            {
                                EndAltAttack();
                            }
                        }
                        else if (Controls.AltAttack.IsPressed)
                        {
                            Flags2 |= PlayerFlags2.AltAttack;
                            NoteOffensiveAction();
                            float attackHSpeed = Fixed.ToFloat(Values.LungeHSpeed);
                            float attackVSpeed = Fixed.ToFloat(Values.LungeVSpeed);
                            if (_field70 * Speed.X + _field74 * Speed.Z < attackHSpeed)
                            {
                                Speed = Speed.WithX(_field70 * attackHSpeed).WithZ(_field74 * attackHSpeed);
                            }
                            if (Speed.Y < attackVSpeed)
                            {
                                float newYSpeed = Speed.Y + attackVSpeed;
                                if (newYSpeed > attackVSpeed)
                                {
                                    newYSpeed = attackVSpeed;
                                }
                                Speed = Speed.WithY(newYSpeed);
                            }
                            animId = (int)WeavelAltAnim.Attack;
                            animFlags = AnimFlags.NoLoop;
                            animRequiresMovement = false;
                            _soundSource.PlaySfx(SfxId.WEAVEL_ALT_ATTACK);
                        }
                    }
                    if (_abilities.TestFlag(AbilityFlags.GuardianAltAttack))
                    {
                        if (_altAttackCooldown > 0)
                        {
                            if (_altAttackTime > 0)
                            {
                                EndAltAttack();
                            }
                        }
                        else if (Controls.AltAttack.IsDown)
                        {
                            if (Controls.AltAttack.IsPressed)
                            {
                                _altAttackTime = 1;
                                _altModel.SetAnimation((int)PsychoBitAltAnim.Charge,
                                    AnimFlags.NoLoop);
                                _soundSource.PlaySfx(SfxId.PSYCHOBIT_CHARGE,
                                    loop: true);
                            }
                            else if (_altAttackTime > 0)
                            {
                                int startupTime = SimTicks.From30HzFrames(
                                    Values.AltAttackStartup);
                                _altAttackTime = (ushort)Math.Min(
                                    startupTime, _altAttackTime + 1);
                                if (_altAttackTime >= startupTime)
                                {
                                    Flags2 |= PlayerFlags2.AltAttack;
                                    NoteOffensiveAction();
                                }
                            }
                        }
                        else if (_altAttackTime > 0)
                        {
                            FireGuardianPsychoBitBeam();
                            EndAltAttack();
                        }
                    }
                    if (_abilities.TestFlag(AbilityFlags.Boost))
                    {
                        if (boostIntent.IsFlick)
                        {
                            // Full-charge flick is a compatibility fallback
                            // matching Project Prime's former touch behavior;
                            // it is not asserted as AMHE1 binary fidelity.
                            if (CanActivateDirectionalBoost()
                                && TryResolveBoostDirection(boostIntent,
                                    _altRollFbX, _altRollFbZ,
                                    _altRollLrX, _altRollLrZ,
                                    out Vector3 direction))
                            {
                                _boostCharge = (ushort)SimTicks.From30HzFrames(
                                    Values.BoostChargeMax);
                                ActivateBoost(ref speedDelta, direction.X,
                                    direction.Z, aimed: true);
                                _boostCharge = 0;
                            }
                        }
                        else if (Controls.Boost.IsDown)
                        {
                            // the game plays the boost charge SFX here, but that SFX is empty
                            if (_boostCharge < SimTicks.From30HzFrames(Values.BoostChargeMax))
                            {
                                _boostCharge++;
                            }
                        }
                        else
                        {
                            if (_boostCharge > SimTicks.From30HzFrames(Values.BoostChargeMin))
                            {
                                ActivateBoost(ref speedDelta, _field70, _field74,
                                    aimed: false);
                            }
                            _boostCharge = 0;
                        }
                    }
                }
                float magBefore = MathF.Sqrt(Speed.X * Speed.X + Speed.Z * Speed.Z);
                Speed += speedDelta; // todo: FPS stuff?
                float magAfter = MathF.Sqrt(Speed.X * Speed.X + Speed.Z * Speed.Z);
                float effectiveSpeedCap = HunterBalanceResolver.ResolveAltSpeedCap(
                    _hSpeedCap, Hunter, Flags1.TestFlag(PlayerFlags1.Boosting),
                    _scene.Match.Balance);
                if (magAfter > magBefore && magAfter > effectiveSpeedCap)
                {
                    float factor;
                    if (magBefore <= effectiveSpeedCap)
                    {
                        factor = effectiveSpeedCap / magAfter;
                    }
                    else
                    {
                        factor = magBefore / magAfter;
                    }
                    Speed = Speed.WithX(Speed.X * factor).WithZ(Speed.Z * factor);
                }
                if (_field35C != null)
                {
                    Speed = Speed.WithX(0).WithZ(0);
                }
                // the game doesn't require pressed here, but presumably the control scheme would have the pressed flag
                // the game also doesn't check the ability flag here
                if (_abilities.TestFlag(AbilityFlags.AltForm) && Controls.Morph.IsPressed
                    || IsMainPlayer && _scene.CameraSequences.Current?.ForceBiped == true)
                {
                    TrySwitchForms();
                }
            }
            ProcessMovement();
            if (_frozenTimer == 0 && _health > 0
                && (Hunter == Hunter.Trace || Hunter == Hunter.Weavel
                    || Hunter == Hunter.Guardian))
            {
                // Collision/support and the actual displacement are only
                // final after ProcessMovement. Resolve the locomotion clip
                // here so jump-pad support and wall-blocked requests cannot
                // restart a directional animation.
                AnimationInfo info = _altModel.AnimInfo;
                (int animation, AnimFlags flags) = ResolveAltAnimation(
                    Hunter, Flags1, Flags2.TestFlag(PlayerFlags2.AltAttack),
                    info.Index[0], info.Flags[0], animId, animFlags,
                    Position - PrevPosition, animRequiresMovement);
                int attackAnimation = Hunter switch
                {
                    Hunter.Trace => (int)TraceAltAnim.Attack,
                    Hunter.Weavel => (int)WeavelAltAnim.Attack,
                    _ => (int)PsychoBitAltAnim.Beam
                };
                if (animation == (int)TraceAltAnim.Idle)
                {
                    if (info.Index[0] != (int)TraceAltAnim.Idle
                        && (!info.Flags[0].TestFlag(AnimFlags.NoLoop)
                            || info.Flags[0].TestFlag(AnimFlags.Ended)))
                    {
                        _altModel.SetAnimation((int)TraceAltAnim.Idle, flags);
                    }
                }
                else if (info.Index[0] != attackAnimation
                    || info.Flags[0].TestFlag(AnimFlags.Ended))
                {
                    if (animation != info.Index[0])
                    {
                        _altModel.SetAnimation(animation, flags);
                    }
                }
            }
            UpdateCamera();
        }

        /// <summary>
        /// Select the directional animation for a strafe-capable alt form.
        /// Forward/backward wins when both axes are held, matching the
        /// movement-processing order below. Sylux's alt model has no
        /// directional movement animations.
        /// </summary>
        internal static int SelectAltMovementAnimation(Hunter hunter,
            float lateralSign, float forwardSign)
        {
            if (hunter == Hunter.Trace)
            {
                if (forwardSign > 0) return (int)TraceAltAnim.MoveForward;
                if (forwardSign < 0) return (int)TraceAltAnim.MoveBackward;
                if (lateralSign > 0) return (int)TraceAltAnim.MoveRight;
                if (lateralSign < 0) return (int)TraceAltAnim.MoveLeft;
            }
            else if (hunter == Hunter.Weavel)
            {
                if (forwardSign > 0) return (int)WeavelAltAnim.MoveForward;
                if (forwardSign < 0) return (int)WeavelAltAnim.MoveBackward;
                if (lateralSign > 0) return (int)WeavelAltAnim.MoveRight;
                if (lateralSign < 0) return (int)WeavelAltAnim.MoveLeft;
            }
            else if (hunter == Hunter.Guardian
                && (forwardSign != 0 || lateralSign != 0))
            {
                // Psycho Bit has one authored hover locomotion group; it is
                // intentionally used only after the normal movement/collision
                // pass, preserving Guardian's existing alt volume envelope.
                return (int)PsychoBitAltAnim.Fly;
            }
            return -1;
        }

        /// <summary>
        /// Resolve Trace/Weavel's final alternate-form animation after the
        /// movement/collision pass. Attack state owns the clip, then a
        /// non-ended attack is allowed to finish; directional locomotion is
        /// only valid with real horizontal movement on physical ground.
        /// </summary>
        internal static (int Animation, AnimFlags Flags) ResolveAltAnimation(
            Hunter hunter, PlayerFlags1 flags, bool altAttackActive,
            int currentAnimation, AnimFlags currentFlags,
            int requestedAnimation, AnimFlags requestedFlags,
            Vector3 displacement,
            bool requestedAnimationRequiresMovement = true)
        {
            if (hunter != Hunter.Trace && hunter != Hunter.Weavel
                && hunter != Hunter.Guardian)
            {
                return (currentAnimation, currentFlags);
            }

            int attackAnimation = hunter switch
            {
                Hunter.Trace => (int)TraceAltAnim.Attack,
                Hunter.Weavel => (int)WeavelAltAnim.Attack,
                _ => (int)PsychoBitAltAnim.Beam
            };
            if (altAttackActive)
            {
                if (currentAnimation == attackAnimation)
                {
                    // In particular, do not restart an ended attack pose.
                    return (attackAnimation, currentFlags);
                }
                return (attackAnimation,
                    requestedAnimation == attackAnimation
                        ? requestedFlags : AnimFlags.NoLoop);
            }
            if (requestedAnimation == attackAnimation)
            {
                // The combat state may have ended on an immediate hit or
                // blocking impact during ProcessMovement. The accepted
                // attack still owns its one-shot presentation.
                return (attackAnimation, requestedFlags);
            }
            if (currentAnimation == attackAnimation
                && !currentFlags.TestFlag(AnimFlags.Ended))
            {
                return (attackAnimation, currentFlags);
            }
            if (HasPhysicalAltGroundContact(flags)
                && requestedAnimation >= 0
                && (!requestedAnimationRequiresMovement
                    || HasRealAltHorizontalMovement(displacement)))
            {
                return (requestedAnimation, requestedFlags);
            }
            int idleAnimation = hunter switch
            {
                Hunter.Trace => (int)TraceAltAnim.Idle,
                Hunter.Weavel => (int)WeavelAltAnim.Idle,
                _ => (int)PsychoBitAltAnim.Idle
            };
            return (idleAnimation, AnimFlags.None);
        }

        internal static bool HasRealAltHorizontalMovement(Vector3 displacement)
        {
            if (!VectorMath.IsFinite(displacement))
            {
                return false;
            }
            float lengthSquared = displacement.X * displacement.X
                + displacement.Z * displacement.Z;
            return float.IsFinite(lengthSquared)
                && lengthSquared > VectorMath.DefaultEpsilon
                    * VectorMath.DefaultEpsilon;
        }

        internal static bool ShouldStopSamusAltBoost(Hunter hunter,
            bool isAltForm, bool boosting, Vector3 speed, float altMinHSpeed)
        {
            if (hunter != Hunter.Samus || !isAltForm || !boosting
                || !VectorMath.IsFinite(speed) || !float.IsFinite(altMinHSpeed)
                || altMinHSpeed < 0)
            {
                return false;
            }
            float horizontalSpeedSquared = speed.X * speed.X
                + speed.Z * speed.Z;
            float minimumSpeedSquared = altMinHSpeed * altMinHSpeed;
            return float.IsFinite(horizontalSpeedSquared)
                && float.IsFinite(minimumSpeedSquared)
                && horizontalSpeedSquared <= minimumSpeedSquared;
        }

        internal static bool ShouldApplyMorphAnimation(bool switchSucceeded,
            bool isMorphing)
            => switchSucceeded || isMorphing;

        private void ApplyRollingAltLook(Vector2 lookDegrees)
        {
            if (!float.IsFinite(lookDegrees.X) || !float.IsFinite(lookDegrees.Y)
                || lookDegrees == Vector2.Zero)
            {
                return;
            }
            (Vector3 position, Vector3 cameraForward, Vector3 cameraLeft) = RotateRollingAltCamera(
                CameraInfo.Position, CameraInfo.Target, lookDegrees);
            CameraInfo.Position = position;
            // Bots keep their existing camera-driven navigation. A local
            // player rotates the retained control basis by explicit yaw only;
            // pitch and collision-adjusted camera position cannot affect it.
            if (IsBot)
            {
                _altRollFbX = cameraForward.X;
                _altRollFbZ = cameraForward.Z;
                _altRollLrX = cameraLeft.X;
                _altRollLrZ = cameraLeft.Z;
            }
            else if (!_networkInputActive
                && !_scene.Services.IsRemoteControlled(SlotIndex))
            {
                (Vector3 forward, Vector3 left) = RotateRollingAltControlBasis(
                    new Vector3(_altRollFbX, 0, _altRollFbZ), lookDegrees.X);
                _altRollFbX = forward.X;
                _altRollFbZ = forward.Z;
                _altRollLrX = left.X;
                _altRollLrZ = left.Z;
            }
        }

        internal static (Vector3 Forward, Vector3 Left)
            RotateRollingAltControlBasis(Vector3 retainedForward,
                float yawDegrees)
        {
            Vector3 forward = VectorMath.NormalizeHorizontalOr(retainedForward,
                -Vector3.UnitZ);
            if (float.IsFinite(yawDegrees) && yawDegrees != 0)
            {
                float yaw = MathHelper.DegreesToRadians(yawDegrees);
                float cos = MathF.Cos(yaw);
                float sin = MathF.Sin(yaw);
                forward = new Vector3(forward.X * cos + forward.Z * sin, 0,
                    forward.X * -sin + forward.Z * cos);
                forward = VectorMath.NormalizeHorizontalOr(forward,
                    -Vector3.UnitZ);
            }
            return (forward, new Vector3(forward.Z, 0, -forward.X));
        }

        internal static Vector3 ResolveRollingAltMovement(Vector3 forward,
            Vector3 left, float forwardInput, float lateralInput,
            float traction)
        {
            if (!VectorMath.IsFinite(forward) || !VectorMath.IsFinite(left)
                || !float.IsFinite(forwardInput)
                || !float.IsFinite(lateralInput) || !float.IsFinite(traction))
            {
                return Vector3.Zero;
            }
            return forward * (forwardInput * traction)
                - left * (lateralInput * traction);
        }

        internal static (Vector3 Position, Vector3 Forward, Vector3 Left)
            RotateRollingAltCamera(Vector3 position, Vector3 target, Vector2 lookDegrees)
        {
            if (!VectorMath.IsFinite(position) || !VectorMath.IsFinite(target)
                || !float.IsFinite(lookDegrees.X) || !float.IsFinite(lookDegrees.Y))
            {
                Vector3 fallback = -Vector3.UnitZ;
                return (position, fallback, new Vector3(fallback.Z, 0, -fallback.X));
            }

            Vector3 offset = position - target;
            float radius = offset.Length;
            if (!float.IsFinite(radius) || radius <= VectorMath.DefaultEpsilon)
            {
                offset = -Vector3.UnitZ;
                radius = 1;
            }

            float yaw = MathHelper.DegreesToRadians(lookDegrees.X);
            float horizontal = MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
            float currentPitch = MathF.Atan2(offset.Y, Math.Max(horizontal,
                VectorMath.DefaultEpsilon));
            // Keep the rolling camera above/near the arena while still making
            // vertical mouse/stick motion visible. These bounds avoid the
            // singular straight-up/down basis that poisoned movement fields.
            float pitch = Math.Clamp(currentPitch
                + MathHelper.DegreesToRadians(lookDegrees.Y),
                MathHelper.DegreesToRadians(-10), MathHelper.DegreesToRadians(65));
            float cos = MathF.Cos(yaw);
            float sin = MathF.Sin(yaw);
            float rotatedX = offset.X * cos + offset.Z * sin;
            float rotatedZ = offset.X * -sin + offset.Z * cos;
            Vector3 horizontalOffset = VectorMath.NormalizeHorizontalOr(
                new Vector3(rotatedX, 0, rotatedZ), -Vector3.UnitZ);
            float horizontalRadius = radius * MathF.Cos(pitch);
            Vector3 rotatedOffset = horizontalOffset * horizontalRadius;
            rotatedOffset.Y = radius * MathF.Sin(pitch);
            position = target + rotatedOffset;

            Vector3 forward = VectorMath.NormalizeHorizontalOr(target - position,
                -Vector3.UnitZ);
            Vector3 left = new(forward.Z, 0, -forward.X);
            return (position, forward, left);
        }

        private bool CanActivateDirectionalBoost()
            => CanActivateDirectionalBoost(_health > 0, _frozenTimer > 0,
                Hunter, IsAltForm, IsMorphing, IsUnmorphing,
                _abilities.TestFlag(AbilityFlags.Boost), _altAttackCooldown);

        internal static bool CanActivateDirectionalBoost(bool alive, bool frozen,
            Hunter hunter, bool isAltForm, bool isMorphing, bool isUnmorphing,
            bool hasBoostAbility, ushort cooldown)
            => alive && !frozen && hunter == Hunter.Samus && isAltForm
                && !isMorphing && !isUnmorphing && hasBoostAbility && cooldown == 0;

        /// <summary>
        /// Resolve right-positive/down-positive screen input against the
        /// established horizontal Morph Ball roll basis. Camera pitch is not
        /// part of this contract.
        /// </summary>
        internal static bool TryResolveBoostDirection(in BoostIntent intent,
            float forwardX, float forwardZ, float leftX, float leftZ,
            out Vector3 direction)
        {
            direction = Vector3.Zero;
            if (!intent.IsFlick
                || !float.IsFinite(forwardX) || !float.IsFinite(forwardZ)
                || !float.IsFinite(leftX) || !float.IsFinite(leftZ))
            {
                return false;
            }
            const float epsilon = 1 / 4096f;
            float determinant = forwardX * leftZ - forwardZ * leftX;
            if (MathF.Abs(determinant) <= epsilon) return false;
            Vector2 screen = intent.Direction;
            float forward = -screen.Y;
            float left = -screen.X;
            float x = forwardX * forward + leftX * left;
            float z = forwardZ * forward + leftZ * left;
            float magnitude = MathF.Sqrt(x * x + z * z);
            if (!(magnitude > epsilon) || !float.IsFinite(magnitude)) return false;
            direction = new Vector3(x / magnitude, 0, z / magnitude);
            return true;
        }

        private void ActivateBoost(ref Vector3 speedDelta, float boostDirX,
            float boostDirZ, bool aimed)
        {
            if (_scene.Features.FullBoostCharge)
            {
                _boostCharge = (ushort)SimTicks.From30HzFrames(Values.BoostChargeMax);
            }
            if (_boostCharge > 0)
            {
                int sfx = Metadata.HunterSfx[(int)Hunter, (int)HunterSfx.Boost];
                _soundSource.PlaySfx(sfx);
            }
            float boostHCap = Fixed.ToFloat(Values.BoostSpeedCap) * _boostCharge
                / SimTicks.From30HzFrames(Values.BoostChargeMax);
            if (_hSpeedCap < boostHCap)
            {
                _hSpeedCap = boostHCap;
            }
            float factor = Fixed.ToFloat(Values.BoostSpeedMin)
                + _boostCharge * (Fixed.ToFloat(Values.BoostSpeedMax)
                    - Fixed.ToFloat(Values.BoostSpeedMin))
                / SimTicks.From30HzFrames(Values.BoostChargeMax);
            speedDelta = speedDelta.AddX(boostDirX * factor).AddZ(boostDirZ * factor);
            _altAttackCooldown = (ushort)SimTicks.From30HzFrames(Values.AltAttackCooldown);
            Flags1 |= PlayerFlags1.Boosting;
            NoteOffensiveAction();
            _boostDamage = HunterBalanceResolver.ResolveBoostDamage(
                Values.AltAttackDamage, _boostCharge,
                SimTicks.From30HzFrames(Values.BoostChargeMax), Hunter,
                _scene.Match.Balance);
            if (IsMainPlayer)
            {
                StartBoostPresentation();
            }
            if (_boostEffect != null)
            {
                _scene.UnlinkEffectEntry(_boostEffect);
                _boostEffect = null;
            }
            Vector3 boostVec1 = aimed
                ? new Vector3(boostDirZ, 0, -boostDirX) : _gunVec2;
            Vector3 boostVec2 = aimed
                ? new Vector3(boostDirX, 0, boostDirZ) : _facingVector;
            _boostEffect = _scene.SpawnEffectGetEntry(136, boostVec1, boostVec2,
                Position); // samusDash
            if (_boostEffect != null)
            {
                _boostEffect.SetElementExtension(true);
            }
        }

        private void NoteOffensiveAction()
        {
            if (_scene.Match.Rules.CancelSpawnProtectionOnOffensiveAction)
                _spawnInvulnTimer = 0;
        }

        private void SpawnBomb()
        {
            // todo?: wi-fi condition and alternate function for spawning Lockjaw bombs
            Matrix4 transform = Matrix4.Identity;
            if (Hunter == Hunter.Kanden)
            {
                Matrix4 segMtx = _kandenSegMtx[4];
                transform = GetTransformMatrix(segMtx.Row2.Xyz, segMtx.Row1.Xyz, _kandenSegPos[4]);
            }
            else
            {
                if (Hunter == Hunter.Sylux)
                {
                    BombEntity[] registered = GetRegisteredLockjawBombs();
                    if (registered.Length >= SyluxBombs.Length)
                    {
                        NoteOffensiveAction();
                        foreach (BombEntity existing in registered) existing.Countdown = 0;
                        return;
                    }
                }
                transform = GetTransformMatrix(Vector3.UnitZ, Vector3.UnitY, Position.AddY(Fixed.ToFloat(-1000)));
            }
            var bomb = BombEntity.Spawn(this, transform, _scene);
            if (bomb != null)
            {
                NoteOffensiveAction();
                if (Hunter == Hunter.Sylux)
                {
                    if (!TryRegisterLockjawBomb(bomb))
                    {
                        bomb.Destroy();
                        _scene.RemoveEntity(bomb);
                        return;
                    }
                    // todo?: wifi stuff
                }
                bomb.NodeRef = NodeRef;
                bomb.Radius = Fixed.ToFloat(Values.BombRadius);
                bomb.SelfRadius = Fixed.ToFloat(Values.BombSelfRadius);
                int stinglarvaDelta = _scene.Match.Balance
                    .GetHunter(Hunter).StinglarvaDamageDelta;
                bomb.Damage = (ushort)Math.Clamp(Values.BombDamage + stinglarvaDelta, 0, ushort.MaxValue);
                bomb.EnemyDamage = (ushort)Math.Clamp(Values.BombEnemyDamage + stinglarvaDelta, 0, ushort.MaxValue);
                if (_doubleDmgTimer > 0)
                {
                    bomb.Damage *= 2;
                    bomb.EnemyDamage *= 2;
                }
                if (_bombAmmo >= 2)
                {
                    _bombRefillTimer = (ushort)SimTicks.From30HzFrames(Values.BombRefillTime);
                }
                _bombAmmo--;
                _bombCooldown = (ushort)SimTicks.From30HzFrames(Values.BombCooldown);
                if (Hunter == Hunter.Kanden)
                {
                    _altModel.SetAnimation((int)KandenAltAnim.TailOut, AnimFlags.NoLoop);
                }
                else if (Hunter == Hunter.Sylux && SyluxBombCount == 3)
                {
                    // todo: FPS stuff
                    _bombOveruse += (ushort)SimTicks.From30HzFrames(27);
                    if (_bombOveruse >= SimTicks.From30HzFrames(100))
                    {
                        _bombCooldown = (ushort)SimTicks.From30HzFrames(150);
                    }
                }
                bomb.PlaySpawnSfx();
            }
        }

        /// <summary>
        /// Fire the Project Prime Psycho Bit adaptation through the regular
        /// beam authority. The exact retail enemy attack is unavailable, so
        /// the authored Guardian startup/damage values are the only values
        /// used here; the existing Power Beam affinity supplies collision,
        /// lag compensation, forcefield, and projectile attribution.
        /// </summary>
        private void FireGuardianPsychoBitBeam()
        {
            if (Hunter != Hunter.Guardian || !IsAltForm || _altAttackTime == 0)
            {
                return;
            }
            Vector3 direction = VectorMath.NormalizeOr(_aimPosition
                - _volume.SpherePosition, _gunVec1);
            Vector3 origin = _volume.SpherePosition
                + direction * Fixed.ToFloat(Values.MuzzleOffset);
            int charge = Math.Min(_altAttackTime,
                SimTicks.From30HzFrames(Values.AltAttackStartup));
            var equip = new EquipInfo
            {
                Weapon = Weapons.Current[(int)BeamType.PowerBeam + 9],
                Beams = _beams,
                ChargeLevel = (ushort)charge,
                InfiniteAmmo = true
            };
            equip.UnchargedDamage = (ushort)Values.AltAttackDamage;
            equip.MinChargeDamage = (ushort)Values.AltAttackDamage;
            equip.ChargedDamage = (ushort)Values.AltAttackDamage;
            equip.HeadshotDamage = (ushort)Values.AltAttackDamage;
            equip.MinChargeHeadshotDamage = (ushort)Values.AltAttackDamage;
            equip.ChargedHeadshotDamage = (ushort)Values.AltAttackDamage;
            BeamResultFlags result = BeamProjectileEntity.Spawn(this, equip,
                origin, direction, BeamSpawnFlags.NoMuzzle | BeamSpawnFlags.FromAlt,
                NodeRef, _scene);
            if (result == BeamResultFlags.NoSpawn)
            {
                return;
            }
            _scene.Services.NoteFired(this, direction, _gunVec1);
            _soundSource.StopSfx(SfxId.PSYCHOBIT_CHARGE);
            _soundSource.PlaySfx(SfxId.PSYCHOBIT_BEAM);
            NoteOffensiveAction();
        }

        private void BeginSpireAltAttack()
        {
            if (Hunter != Hunter.Spire || !IsAltForm
                || Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                return;
            }
            Flags2 |= PlayerFlags2.AltAttack;
            _altModel.SetAnimation((int)SpireAltAnim.Attack, AnimFlags.NoLoop);
            _soundSource.PlaySfx(SfxId.SPIRE_ALT_ATTACK);
            _spireRockPosR = Position;
            _spireRockPosL = Position;
            _spireAltUp = _fieldC0;
            var cross = Vector3.Cross(_facingVector, _spireAltUp);
            _spireAltFacing = VectorMath.NormalizeOr(
                Vector3.Cross(_spireAltUp, cross), _facingVector);
        }

        private void EndAltAttack()
        {
            if (Hunter == Hunter.Samus)
            {
                Flags1 &= ~PlayerFlags1.Boosting;
            }
            else if (Hunter == Hunter.Trace || Hunter == Hunter.Weavel)
            {
                if (Flags2.TestFlag(PlayerFlags2.AltAttack))
                {
                    _altAttackCooldown = (ushort)SimTicks.From30HzFrames(Values.AltAttackCooldown);

                }
            }
            else if (Hunter == Hunter.Noxus)
            {
                if (_altAttackTime > 0)
                {
                    _soundSource.StopSfx(SfxId.NOX_TOP_ATTACK1);
                    _soundSource.StopSfx(SfxId.NOX_TOP_ATTACK2);
                    if (_altAttackTime >= SimTicks.From30HzFrames(Values.AltAttackStartup / 2))
                    {
                        _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK3);
                    }
                    _altModel.SetAnimation((int)NoxusAltAnim.Extend, AnimFlags.Paused);
                    _altAttackTime = 0;
                }
            }
            else if (Hunter == Hunter.Guardian)
            {
                if (_altAttackTime > 0)
                {
                    _soundSource.StopSfx(SfxId.PSYCHOBIT_CHARGE);
                    _altModel.SetAnimation((int)PsychoBitAltAnim.Idle,
                        AnimFlags.Paused);
                    _altAttackCooldown = (ushort)SimTicks.From30HzFrames(
                        Values.AltAttackCooldown);
                    _altAttackTime = 0;
                }
            }
            Flags2 &= ~PlayerFlags2.AltAttack;
        }

        private void ProcessMovement()
        {
            if (_accelerationTimer > 0)
            {
                _accelerationTimer--;
                Speed += Acceleration / 2; // todo: FPS stuff
            }
            var hSpeed = new Vector3(Speed.X, 0, Speed.Z);
            float hSpeedMag = hSpeed.Length;
            if (hSpeedMag == 0)
            {
                _hSpeedMag = 0;
            }
            else
            {
                hSpeed /= hSpeedMag;
                if (!UsesStrafeAltMovement)
                {
                    if (hSpeedMag > Fixed.ToFloat(Values.Field5C)) // todo: FPS stuff?
                    {
                        _field80 = hSpeed.X;
                        _field84 = hSpeed.Z;
                    }
                    if ((IsAltForm || IsMorphing) && hSpeedMag > Fixed.ToFloat(Values.Field58)) // todo: FPS stuff?
                    {
                        _field70 = hSpeed.X;
                        _field74 = hSpeed.Z;
                        _facingVector = new Vector3(_field70, 0, _field74);
                    }
                }
                if (IsAltForm)
                {
                    float altMin = Fixed.ToFloat(Values.AltMinHSpeed); // todo: FPS stuff?
                    if (_hSpeedCap <= altMin)
                    {
                        _hSpeedCap = altMin;
                    }
                    else if (hSpeedMag >= _hSpeedCap)
                    {
                        _hSpeedCap -= Fixed.ToFloat(Values.AltHSpeedCapIncrement) / 2; // todo: FPS stuff
                    }
                    else
                    {
                        _hSpeedCap = hSpeedMag;
                    }
                }
                else
                {
                    bool strafing = Flags1.TestFlag(PlayerFlags1.Strafing);
                    _hSpeedCap = Fixed.ToFloat(strafing ? Values.StrafeSpeedCap : Values.WalkSpeedCap); // todo: FPS stuff?
                }
                if (IsPrimeHunter && !IsAltForm)
                {
                    _hSpeedCap = 0.4f; // todo: FPS stuff?
                }
                _hSpeedMag = hSpeedMag;
            }
            // todo: check how much of this overwrites stuff done above
            Vector3 horizontalFacing = VectorMath.NormalizeHorizontalOr(_facingVector,
                VectorMath.NormalizeHorizontalOr(_gunVec1, Vector3.UnitZ));
            _field70 = horizontalFacing.X;
            _field74 = horizontalFacing.Z;
            _gunVec2 = new Vector3(_field74, 0, -_field70);
            _field78 = _gunVec2.X;
            _field7C = _gunVec2.Z;
            _upVector = VectorMath.NormalizeOr(Vector3.Cross(_facingVector, _gunVec2), Vector3.UnitY);
            if (UsesStrafeAltMovement)
            {
                _field80 = _field70;
                _field84 = _field74;
            }
            _aimPosition = _gunVec1 * Fixed.ToFloat(Values.AimDistance);
            _aimPosition += CameraInfo.Position;
            // unimpl-controls: this calculation is different when exact aim is not set
            float hMag = MathF.Sqrt(_gunVec1.X * _gunVec1.X + _gunVec1.Z * _gunVec1.Z);
            if (!float.IsFinite(hMag))
            {
                hMag = 0;
            }
            _aimY = MathHelper.RadiansToDegrees(MathF.Atan2(_gunVec1.Y, hMag));
            if (_aimY > 75 || _aimY < -75)
            {
                UpdateAimY(0);
            }
            if (Flags1.TestFlag(PlayerFlags1.UsedJumpPad))
            {
                // basically exclude the jump pad speed for the rest of the speed calc, then restore it below
                float prevX = Speed.X;
                Speed = Speed.AddX(-_jumpPadAccel.X);
                if (prevX <= 0 && Speed.X > 0 || prevX > 0 && Speed.X < 0)
                {
                    _jumpPadAccel.X += Speed.X / 2; // todo: FPS stuff
                    Speed = Speed.WithX(0);
                }
                float prevZ = Speed.Z;
                Speed = Speed.AddZ(-_jumpPadAccel.Z);
                if (prevZ <= 0 && Speed.Z > 0 || prevZ > 0 && Speed.Z < 0)
                {
                    _jumpPadAccel.Z += Speed.Z / 2; // todo: FPS stuff
                    Speed = Speed.WithZ(0);
                }
            }
            float slideSfxAmount = 0;
            float speedFactor;
            if (IsAltForm || IsMorphing)
            {
                if (Flags2.TestFlag(PlayerFlags2.AltAttack) && (Hunter == Hunter.Trace || Hunter == Hunter.Weavel))
                {
                    speedFactor = 0.96f;
                }
                else if (Flags1.TestFlag(PlayerFlags1.Standing))
                {
                    speedFactor = Fixed.ToFloat(Values.AltGroundSpeedFactor);
                }
                else
                {
                    speedFactor = Fixed.ToFloat(Values.AirSpeedFactor);
                }
            }
            else if (Flags1.TestFlag(PlayerFlags1.Standing))
            {
                if (Flags1.TestFlag(PlayerFlags1.Strafing))
                {
                    speedFactor = Fixed.ToFloat(Values.StrafeSpeedFactor);
                }
                else if (Flags1.TestFlag(PlayerFlags1.Walking))
                {
                    speedFactor = Fixed.ToFloat(Values.WalkSpeedFactor);
                }
                else
                {
                    speedFactor = Fixed.ToFloat(Values.StandSpeedFactor);
                }
            }
            else
            {
                speedFactor = Fixed.ToFloat(Values.AirSpeedFactor);
            }
            if (Flags1.TestFlag(PlayerFlags1.Standing) && _slipperiness != 0)
            {
                speedFactor += (1 - speedFactor) * Metadata.SlipSpeedFactors[_slipperiness];
                if (!Flags1.TestFlag(PlayerFlags1.MovingBiped))
                {
                    slideSfxAmount = 0xFFFF * _hSpeedMag / Fixed.ToFloat(Values.WalkSpeedCap);
                }
            }
            UpdateSlidingSfx(slideSfxAmount);
            Vector3 speedMul = Speed.WithX(Speed.X * speedFactor).WithZ(Speed.Z * speedFactor);
            Speed += (speedMul - Speed) / 2; // todo: FPS stuff
            if (Flags1.TestFlag(PlayerFlags1.UsedJumpPad))
            {
                Speed = Speed.AddX(_jumpPadAccel.X);
                Speed = Speed.AddZ(_jumpPadAccel.Z);
            }
            if (Flags1.TestFlag(PlayerFlags1.Standing) && _timeSinceJumpPad > SimTicks.From30HzFrames(5))
            {
                _lastJumpPad = null;
                Flags1 &= ~PlayerFlags1.UsedJumpPad;
                _jumpPadControlLock = 0;
                _jumpPadControlLockMin = 0;
            }
            if (IsAltForm)
            {
                Flags2 |= PlayerFlags2.AltFormGravity;
            }
            else if (Speed.Y <= 0.01f)
            {
                Flags2 &= ~PlayerFlags2.AltFormGravity;
            }
            if (_health > 0)
            {
                if (_jumpPadControlLock == 0 && !Flags2.TestFlag(PlayerFlags2.BipedStuck))
                {
                    if (Flags2.TestFlag(PlayerFlags2.GravityOverride))
                    {
                        Flags2 &= ~PlayerFlags2.GravityOverride;
                    }
                    else
                    {
                        if (IsAltForm || Flags2.TestFlag(PlayerFlags2.AltFormGravity))
                        {
                            if (Flags1.TestFlag(PlayerFlags1.Standing) && _slipperiness == 0 && UsesStrafeAltMovement)
                            {
                                _gravity = 0;
                            }
                            else if (Flags1.TestFlag(PlayerFlags1.Standing))
                            {
                                _gravity = Fixed.ToFloat(Values.AltGroundGravity);
                            }
                            else
                            {
                                _gravity = Fixed.ToFloat(Values.AltAirGravity);
                            }
                        }
                        else if (Flags1.TestFlag(PlayerFlags1.Standing) && _slipperiness == 0)
                        {
                            _gravity = 0;
                        }
                        else
                        {
                            _gravity = Fixed.ToFloat(Values.BipedGravity);
                        }
                    }
                    Speed = Speed.AddY(_gravity / 2); // todo: FPS stuff
                }
                Vector3 position = Position + Speed / 2; // todo: FPS stuff
                Position = position;
                // unimpl-controls: the game does more calculation here if exact aim is off
                // --> does so outside of the _health > 0 condition, before the player collision check (which is inside another _health > 0)
                CheckPlayerCollision();
            }
            if (Hunter == Hunter.Kanden && IsAltForm && Flags1.TestFlag(PlayerFlags1.Standing))
            {
                for (int i = 1; i < _kandenSegPos.Length; i++)
                {
                    _kandenSegPos[i] = _kandenSegPos[i].AddY(-0.1f / 2); // todo: FPS stuff
                }
            }
            if (_standingEntCol != null)
            {
                Vector3 position = Matrix.Vec3MultMtx4(Position, _standingEntCol.Inverse2);
                Position = Matrix.Vec3MultMtx4(position, _standingEntCol.Transform);
            }
            if (!Flags1.TestFlag(PlayerFlags1.CollidingLateral))
            {
                _horizColTimer = 0;
            }
            else if (_horizColTimer != UInt16.MaxValue)
            {
                _horizColTimer++;
            }
            Terrain prevTerrain = _standTerrain;
            bool standingPrev = Flags1.TestFlag(PlayerFlags1.Standing);
            bool noUnmorphPev = Flags1.TestFlag(PlayerFlags1.NoUnmorph);
            Flags1 &= ~PlayerFlags1.Standing;
            Flags1 &= ~PlayerFlags1.StandingPrevious;
            Flags1 &= ~PlayerFlags1.NoUnmorph;
            Flags1 &= ~PlayerFlags1.NoUnmorphPrevious;
            Flags1 &= ~PlayerFlags1.OnLava;
            Flags1 &= ~PlayerFlags1.OnAcid;
            Flags2 &= ~PlayerFlags2.SpireClimbing;
            Flags1 &= ~PlayerFlags1.CollidingLateral;
            Flags1 &= ~PlayerFlags1.NoUnmorph;
            Flags1 &= ~PlayerFlags1.CollidingEntity;
            Flags1 &= ~PlayerFlags1.Standing;
            if (standingPrev)
            {
                Flags1 |= PlayerFlags1.StandingPrevious;
            }
            if (noUnmorphPev)
            {
                Flags1 |= PlayerFlags1.NoUnmorphPrevious;
            }
            Vector3 prevC0 = _fieldC0;
            _fieldC0 = Vector3.Zero;
            CheckCollision();
            if (ShouldStopSamusAltBoost(Hunter, IsAltForm,
                Flags1.TestFlag(PlayerFlags1.Boosting), Speed,
                Fixed.ToFloat(Values.AltMinHSpeed)))
            {
                Flags1 &= ~PlayerFlags1.Boosting;
            }
            if (_field449 > 0 && _field449 < SimTicks.From30HzFrames(30))
            {
                _fieldC0 = prevC0;
            }
            else if (_fieldC0 != Vector3.Zero)
            {
                _fieldC0 = VectorMath.NormalizeOr(_fieldC0, prevC0);
            }
            else
            {
                _fieldC0 = Vector3.UnitY;
            }
            if (_standTerrain != prevTerrain)
            {
                StopTerrainSfx(prevTerrain);
            }
            if (Flags1.TestFlag(PlayerFlags1.Standing) && !Flags1.TestFlag(PlayerFlags1.StandingPrevious))
            {
                // landing
                _timeStanding = 0;
                if (PrevSpeed.Y >= 0)
                {
                    _field44C = 0;
                }
                else
                {
                    _field44C = -PrevSpeed.Y * 0.35f;
                    if (_field44C > Fixed.ToFloat(800))
                    {
                        _field44C = Fixed.ToFloat(800);
                    }
                    if (PrevSpeed.Y < -0.65f)
                    {
                        CameraInfo.SetShake(Fixed.ToFloat(204));
                    }
                }
            }
            else if (_timeStanding != UInt16.MaxValue)
            {
                _timeStanding++;
            }
            if (IsAltForm)
            {
                UpdateAltTransform();
            }
            if (Flags1.TestFlag(PlayerFlags1.Grounded))
            {
                Flags1 |= PlayerFlags1.GroundedPrevious;
            }
            else
            {
                Flags1 &= ~PlayerFlags1.GroundedPrevious;
            }
            if (Flags1.TestFlag(PlayerFlags1.Standing) || Flags2.TestFlag(PlayerFlags2.SpireClimbing))
            {
                _timeBeforeLanding = _timeSinceGrounded;
                _timeSinceGrounded = 0;
                Flags1 |= PlayerFlags1.Grounded;
            }
            else if (_timeSinceGrounded < SimTicks.From30HzFrames(90))
            {
                _timeSinceGrounded++;
                if (_timeSinceGrounded >= SimTicks.From30HzFrames(8))
                {
                    Flags1 &= ~PlayerFlags1.Grounded;
                    ResetWalkingSound();
                }
            }
            bool burning = false;
            if (_health > 0 && (_burnTimer > 0 || Hunter != Hunter.Spire
                && Flags1.TestFlag(PlayerFlags1.OnLava) && Flags1.TestFlag(PlayerFlags1.Grounded)))
            {
                burning = true;
            }
            UpdateBurningSfx(burning);
            if ((!IsAltForm || Hunter == Hunter.Weavel) && Flags1.TestFlag(PlayerFlags1.Grounded))
            {
                UpdateWalkingSfx();
            }
        }

        public PlayerControls Controls { get; } = new PlayerControls();
        internal PlayerInput Input { get; } = new PlayerInput();
        internal sealed class PlayerInput
        {
            private BoostIntent _pendingBoostIntent;

            public float MouseDeltaX { get; set; }
            public float MouseDeltaY { get; set; }
            public float ClickX { get; set; } = -1;
            public float ClickY { get; set; } = -1;
            public bool HasInput { get; set; }
            public BoostIntent ConsumedBoostIntent { get; private set; }

            /// <summary>
            /// Queue one semantic request for the next simulation tick. The
            /// first valid flick wins; a flick supersedes a queued charge, and
            /// no later producer can replace it in the same tick.
            /// </summary>
            public bool QueueBoostIntent(in BoostIntent intent)
            {
                if (intent.Activation == BoostActivation.None
                    || !BoostIntent.TryDecode(intent.Activation, intent.X, intent.Y,
                        out BoostIntent valid))
                {
                    return false;
                }
                if (_pendingBoostIntent.IsFlick) return false;
                if (valid.IsFlick || _pendingBoostIntent.Activation == BoostActivation.None)
                {
                    _pendingBoostIntent = valid;
                    return true;
                }
                return false;
            }

            public BoostIntent TakeBoostIntent(bool boostHeld)
            {
                BoostIntent pending = _pendingBoostIntent;
                _pendingBoostIntent = BoostIntent.None;
                ConsumedBoostIntent = pending.IsFlick
                    ? pending : boostHeld ? BoostIntent.Charge : BoostIntent.None;
                return ConsumedBoostIntent;
            }

            public void ClearBoostIntents()
            {
                _pendingBoostIntent = BoostIntent.None;
                ConsumedBoostIntent = BoostIntent.None;
            }
        }
    }

    public sealed class PlayerActionState
    {
        public bool IsPressed { get; set; }
        public bool IsDown { get; set; }
        public bool IsReleased { get; set; }
        public bool NeedsRepress { get; set; }
    }

    public sealed class PlayerControls
    {
        public float MouseSensitivity { get; set; } = 1;
        public bool InvertMouseX { get; set; }
        public bool InvertMouseY { get; set; }
        public bool MouseAim { get; set; } = true;
        public bool KeyboardAim { get; set; } = true;
        public bool ScrollAllWeapons { get; set; } = true;
        public bool InvertAimX { get; set; }
        public bool InvertAimY { get; set; }
        public PlayerActionState MoveLeft { get; } = new PlayerActionState();
        public PlayerActionState MoveRight { get; } = new PlayerActionState();
        public PlayerActionState MoveUp { get; } = new PlayerActionState();
        public PlayerActionState MoveDown { get; } = new PlayerActionState();
        public PlayerActionState RolltLeft { get; } = new PlayerActionState();
        public PlayerActionState RollRight { get; } = new PlayerActionState();
        public PlayerActionState RollUp { get; } = new PlayerActionState();
        public PlayerActionState RollDown { get; } = new PlayerActionState();
        public PlayerActionState AimLeft { get; } = new PlayerActionState();
        public PlayerActionState AimRight { get; } = new PlayerActionState();
        public PlayerActionState AimUp { get; } = new PlayerActionState();
        public PlayerActionState AimDown { get; } = new PlayerActionState();
        public PlayerActionState Shoot { get; } = new PlayerActionState();
        public PlayerActionState Zoom { get; } = new PlayerActionState();
        public PlayerActionState Jump { get; } = new PlayerActionState();
        public PlayerActionState Morph { get; } = new PlayerActionState();
        public PlayerActionState Boost { get; } = new PlayerActionState();
        public PlayerActionState AltAttack { get; } = new PlayerActionState();
        public PlayerActionState NextWeapon { get; } = new PlayerActionState();
        public PlayerActionState PrevWeapon { get; } = new PlayerActionState();
        public PlayerActionState WeaponMenu { get; } = new PlayerActionState();
        public PlayerActionState PowerBeam { get; } = new PlayerActionState();
        public PlayerActionState Missile { get; } = new PlayerActionState();
        public PlayerActionState VoltDriver { get; } = new PlayerActionState();
        public PlayerActionState Battlehammer { get; } = new PlayerActionState();
        public PlayerActionState Imperialist { get; } = new PlayerActionState();
        public PlayerActionState Judicator { get; } = new PlayerActionState();
        public PlayerActionState Magmaul { get; } = new PlayerActionState();
        public PlayerActionState ShockCoil { get; } = new PlayerActionState();
        public PlayerActionState OmegaCannon { get; } = new PlayerActionState();
        public PlayerActionState AffinitySlot { get; } = new PlayerActionState();
        public PlayerActionState Pause { get; } = new PlayerActionState();
        public PlayerActionState HudOverlay { get; } = new PlayerActionState();
        public PlayerActionState[] All { get; }
        public PlayerControls()
        {
            All = new[] { MoveLeft, MoveRight, MoveUp, MoveDown, RolltLeft, RollRight, RollUp, RollDown, AimLeft, AimRight, AimUp, AimDown, Shoot, Zoom, Jump, Morph, Boost, AltAttack, NextWeapon, PrevWeapon, WeaponMenu, PowerBeam, Missile, VoltDriver, Battlehammer, Imperialist, Judicator, Magmaul, ShockCoil, OmegaCannon, AffinitySlot, Pause, HudOverlay };
        }
        public void ClearAll() { foreach (var action in All) { action.IsDown = action.IsPressed = action.IsReleased = false; } }
        public void ClearPressed() { foreach (var action in All) { action.IsPressed = false; } }
    }
}
