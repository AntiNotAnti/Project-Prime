using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        // Remote snapshots do not drive Controls, so keep their locomotion
        // intent separate from the simulation state. This is consumed by the
        // game-thread animation pass rather than applied while snapshots are
        // repeatedly reconciled.
        private PlayerAnimation _desiredRemoteBipedAnimation = PlayerAnimation.None;
        private RemoteLocomotionHysteresis _remoteLocomotion;

        internal static PlayerAnimation DeriveSnapshotBipedAnimation(in SnapshotPlayer state, bool local)
            => local ? PlayerAnimation.None
                : RemoteLocomotionHysteresis.Classify(state, state.Speed);

        /// <summary>
        /// Stateless classification retained for focused tests and replay
        /// tooling. Live remote presentation uses the stateful resolver below
        /// with the velocity of the delayed presentation sample.
        /// </summary>
        internal static PlayerAnimation DeriveRemoteBipedAnimation(
            in SnapshotPlayer state, Vector3 visualSpeed)
            => RemoteLocomotionHysteresis.Classify(state, visualSpeed);

        internal void SetRemoteLocomotionIntent(in SnapshotPlayer state,
            Vector3 visualSpeed)
        {
            _desiredRemoteBipedAnimation = _remoteLocomotion.Resolve(state, visualSpeed);
        }

        internal void ResetRemoteLocomotion()
        {
            _remoteLocomotion.Reset();
            _desiredRemoteBipedAnimation = PlayerAnimation.None;
        }

        internal static bool CanApplySnapshotBipedAnimation(PlayerAnimation current, AnimFlags flags)
            => !flags.TestFlag(AnimFlags.NoLoop) || flags.TestFlag(AnimFlags.Ended)
                || IsSnapshotBipedAnimation(current);

        private static bool IsSnapshotBipedAnimation(PlayerAnimation animation)
            => animation is PlayerAnimation.Idle or PlayerAnimation.WalkForward
                or PlayerAnimation.WalkBackward or PlayerAnimation.WalkLeft
                or PlayerAnimation.WalkRight;

        internal BeamType AffinitySlotWeapon => _weaponSlots[2];
        internal InputCommand CaptureNetworkInput(uint sequence, uint viewServerTick)
            => CaptureNetworkInput(sequence, viewServerTick, 1);

        internal InputCommand CaptureNetworkInput(uint sequence, uint viewServerTick,
            uint inputEpoch)
        {
            if (_scene.Services.DesiredSpectating)
            {
                return new InputCommand(sequence, sequence, viewServerTick, InputButtons.Spectate,
                    InputButtons.None, _gunVec1, InputCommand.NoWeapon, inputEpoch);
            }
            InputButtons held = 0, pressed = 0;
            Capture(Controls.MoveLeft, InputButtons.Left, ref held, ref pressed);
            Capture(Controls.MoveRight, InputButtons.Right, ref held, ref pressed);
            Capture(Controls.MoveUp, InputButtons.Forward, ref held, ref pressed);
            Capture(Controls.MoveDown, InputButtons.Back, ref held, ref pressed);
            Capture(Controls.Shoot, InputButtons.Shoot, ref held, ref pressed);
            Capture(Controls.Zoom, InputButtons.Zoom, ref held, ref pressed);
            Capture(Controls.Jump, InputButtons.Jump, ref held, ref pressed);
            Capture(Controls.Morph, InputButtons.Morph, ref held, ref pressed);
            Capture(Controls.Boost, InputButtons.Boost, ref held, ref pressed);
            Capture(Controls.AltAttack, InputButtons.AltAttack, ref held, ref pressed);
            Capture(Controls.NextWeapon, InputButtons.NextWeapon, ref held, ref pressed);
            Capture(Controls.PrevWeapon, InputButtons.PreviousWeapon, ref held, ref pressed);
            Capture(Controls.RolltLeft, InputButtons.RollLeft, ref held, ref pressed);
            Capture(Controls.RollRight, InputButtons.RollRight, ref held, ref pressed);
            Capture(Controls.RollUp, InputButtons.RollForward, ref held, ref pressed);
            Capture(Controls.RollDown, InputButtons.RollBack, ref held, ref pressed);
            BoostIntent boostIntent = Input.ConsumedBoostIntent;
            return new InputCommand(sequence, sequence, viewServerTick, held, pressed, _gunVec1,
                (byte)CurrentWeapon, boostIntent, inputEpoch);
        }

        private static void Capture(PlayerActionState bind, InputButtons button, ref InputButtons held,
            ref InputButtons pressed)
        {
            if (bind.IsDown) held |= button;
            if (bind.IsPressed) pressed |= button;
        }

        internal void ClientActivate(in SnapshotPlayer state)
        {
            ServerDeactivate();
            Hunter = state.Hunter;
            TeamIndex = state.TeamIndex;
            Team = _scene.Match.Rules.Teams ? TeamIndex == 0 ? Team.Orange : Team.Green : Team.None;
            Recolor = _scene.Match.Rules.Teams ? TeamIndex == 0 ? 4 : 5 : 0;
            IsBot = false;
            LoadFlags = LoadFlags.SlotActive | LoadFlags.Active | LoadFlags.Initial
                | LoadFlags.Connected | LoadFlags.WasConnected;
            ReloadInit = false;
            Flags1 = 0;
            Flags2 = 0;
            Initialize();
        }

        internal void ApplyServerState(in SnapshotPlayer state, bool newLife, bool predicted = false)
        {
            bool spawned = (state.Flags & SnapshotPlayerFlags.Spawned) != 0;
            SnapshotPlayerFlags flags = state.Flags;
            bool formChanged = IsAltForm != ((flags & SnapshotPlayerFlags.AltForm) != 0)
                || IsMorphing != ((flags & SnapshotPlayerFlags.Morphing) != 0)
                || IsUnmorphing != ((flags & SnapshotPlayerFlags.Unmorphing) != 0);
            bool spectatorChanged = Flags2.TestFlag(PlayerFlags2.Spectating)
                != ((flags & SnapshotPlayerFlags.Spectating) != 0);
            bool deactivated = LoadFlags.TestFlag(LoadFlags.Active)
                && ((flags & SnapshotPlayerFlags.Active) == 0 || !spawned);
            bool died = Health > 0 && state.Health == 0;
            if (newLife || !spawned || died || formChanged || spectatorChanged)
                Input.ClearBoostIntents();
            if (newLife || deactivated || died || formChanged || spectatorChanged)
                AdvancePresentationPoseEpoch();
            if (newLife || !spawned || state.Health == 0 || formChanged || spectatorChanged)
                ResetRemoteLocomotion();
            if (spawned && (newLife || !ModIsInPlay))
            {
                ModNetSpawn(state.Position, state.Facing);
            }
            else if (!spawned && Health > 0 && state.Health == 0
                && (state.Flags & SnapshotPlayerFlags.Spectating) == 0)
            {
                ModNetDie();
            }
            Health = state.Health;
            for (int weapon = 0; weapon <= 8; weapon++)
            {
                _availableWeapons[(BeamType)weapon] = (state.AvailableWeapons & (1 << weapon)) != 0;
            }
            ModSetWeapon((BeamType)state.Weapon);
            _ammo[UA] = state.AmmoUa;
            _ammo[Missiles] = state.AmmoMissiles;
            EquipInfo.Zoomed = (state.Flags & SnapshotPlayerFlags.Zoomed) != 0;
            bool alt = (state.Flags & SnapshotPlayerFlags.AltForm) != 0;
            if ((!predicted || newLife) && IsAltForm != alt) { ModForceForm(alt); }
            ModSetSpectating((state.Flags & SnapshotPlayerFlags.Spectating) != 0);
            if ((state.Flags & SnapshotPlayerFlags.Active) != 0) LoadFlags |= LoadFlags.Active;
            else LoadFlags &= ~LoadFlags.Active;
            // Freeze gates local prediction; burn/disruption remain presentation-only
            // so snapshot reconciliation cannot start client-generated burn damage.
            _frozenTimer = state.FrozenTicks;
            Flags2 &= ~(PlayerFlags2.RadarReveal | PlayerFlags2.RadarRevealPrevious);
            if ((state.Flags & SnapshotPlayerFlags.RadarReveal) != 0) Flags2 |= PlayerFlags2.RadarReveal;
            if ((state.Flags & SnapshotPlayerFlags.RadarRevealPrevious) != 0) Flags2 |= PlayerFlags2.RadarRevealPrevious;
        }

        internal void ApplySnapshotTransform(in SnapshotPlayer state, bool local = false)
        {
            Vector3 previous = Position;
            Position = state.Position;
            PrevPosition = state.Position;
            Speed = state.Speed;
            ModRefreshNodeRef(previous);
            if (!local)
            {
                _networkInputActive = true;
                _networkAim = state.Aim;
                ModSetAim(state.Aim);
                ModSetFacing(state.Facing);
                SetTransform(_facingVector, _upVector, Position);
            }
        }

        internal void CorrectPredictedPosition(Vector3 position)
        {
            Vector3 previous = Position;
            Position = position;
            PrevPosition += position - previous;
            ModRefreshNodeRef(previous);
        }
    }

    /// <summary>
    /// Presentation-only locomotion state. The resolver deliberately keeps
    /// movement thresholds and direction history off the authoritative player
    /// state so packet jitter cannot affect controls or physics.
    /// </summary>
    internal struct RemoteLocomotionHysteresis
    {
        internal const float MoveStartSpeed = 0.02f;
        internal const float MoveStopSpeed = 0.01f;
        internal const float DirectionSwitchRatio = 1.10f;

        private const float MoveStartSpeedSquared = MoveStartSpeed * MoveStartSpeed;
        private const float MoveStopSpeedSquared = MoveStopSpeed * MoveStopSpeed;
        private const float FacingLengthSquared = 0.0001f;

        private bool _moving;
        private PlayerAnimation _direction;

        internal PlayerAnimation Resolve(in SnapshotPlayer state, Vector3 visualSpeed)
        {
            if (!TryGetBasisAndSpeed(state, visualSpeed, out Vector3 forward,
                out Vector3 speed, out float speedSquared))
            {
                Reset();
                return PlayerAnimation.None;
            }

            if (!_moving)
            {
                if (speedSquared <= MoveStartSpeedSquared)
                {
                    _direction = PlayerAnimation.Idle;
                    return PlayerAnimation.Idle;
                }
                _moving = true;
            }
            else if (speedSquared < MoveStopSpeedSquared)
            {
                Reset();
                return PlayerAnimation.Idle;
            }

            _direction = SelectDirection(forward, speed, _direction);
            return _direction;
        }

        internal void Reset()
        {
            _moving = false;
            _direction = PlayerAnimation.Idle;
        }

        internal static PlayerAnimation Classify(in SnapshotPlayer state,
            Vector3 visualSpeed)
        {
            if (!TryGetBasisAndSpeed(state, visualSpeed, out Vector3 forward,
                out Vector3 speed, out float speedSquared))
                return PlayerAnimation.None;
            if (speedSquared <= MoveStartSpeedSquared)
                return PlayerAnimation.Idle;
            return SelectDirection(forward, speed, PlayerAnimation.Idle);
        }

        private static bool TryGetBasisAndSpeed(in SnapshotPlayer state,
            Vector3 visualSpeed, out Vector3 forward, out Vector3 speed,
            out float speedSquared)
        {
            forward = speed = Vector3.Zero;
            speedSquared = 0;
            SnapshotPlayerFlags flags = state.Flags;
            if (state.Health == 0
                || (flags & (SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned))
                    != (SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned)
                || (flags & (SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.Morphing
                    | SnapshotPlayerFlags.Unmorphing | SnapshotPlayerFlags.Frozen
                    | SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch)) != 0
                || (flags & SnapshotPlayerFlags.Grounded) == 0
                || !Finite(state.Facing) || !Finite(visualSpeed))
                return false;

            forward = new Vector3(state.Facing.X, 0, state.Facing.Z);
            float facingLengthSquared = forward.LengthSquared;
            if (!Single.IsFinite(facingLengthSquared)
                || facingLengthSquared <= FacingLengthSquared)
                return false;

            speed = new Vector3(visualSpeed.X, 0, visualSpeed.Z);
            speedSquared = speed.LengthSquared;
            return Single.IsFinite(speedSquared);
        }

        private static PlayerAnimation SelectDirection(Vector3 forward,
            Vector3 speed, PlayerAnimation current)
        {
            Vector3 right = new(-forward.Z, 0, forward.X);
            float forwardDot = Vector3.Dot(speed, forward);
            float rightDot = Vector3.Dot(speed, right);
            if (!Single.IsFinite(forwardDot) || !Single.IsFinite(rightDot))
                return PlayerAnimation.None;

            float absForward = MathF.Abs(forwardDot);
            float absRight = MathF.Abs(rightDot);
            bool forwardAxis = current switch
            {
                PlayerAnimation.WalkForward or PlayerAnimation.WalkBackward
                    => absRight <= absForward * DirectionSwitchRatio,
                PlayerAnimation.WalkLeft or PlayerAnimation.WalkRight
                    => absForward > absRight * DirectionSwitchRatio,
                _ => absForward >= absRight
            };

            if (forwardAxis)
            {
                if (forwardDot > 0) return PlayerAnimation.WalkForward;
                if (forwardDot < 0) return PlayerAnimation.WalkBackward;
                return IsForwardDirection(current) ? current : PlayerAnimation.WalkForward;
            }
            if (rightDot > 0) return PlayerAnimation.WalkRight;
            if (rightDot < 0) return PlayerAnimation.WalkLeft;
            return IsStrafeDirection(current) ? current : PlayerAnimation.WalkRight;
        }

        private static bool IsForwardDirection(PlayerAnimation animation)
            => animation is PlayerAnimation.WalkForward or PlayerAnimation.WalkBackward;

        private static bool IsStrafeDirection(PlayerAnimation animation)
            => animation is PlayerAnimation.WalkLeft or PlayerAnimation.WalkRight;

        private static bool Finite(Vector3 value)
            => Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);
    }
}
