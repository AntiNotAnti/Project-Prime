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
        private const float SnapshotBipedIdleSpeedSquared = 0.0001f;
        private const float SnapshotBipedFacingLengthSquared = 0.0001f;
        private PlayerAnimation _desiredSnapshotBipedAnimation = PlayerAnimation.None;

        internal static PlayerAnimation DeriveSnapshotBipedAnimation(in SnapshotPlayer state, bool local)
        {
            SnapshotPlayerFlags flags = state.Flags;
            if (local || state.Health == 0
                || (flags & (SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned))
                    != (SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned)
                || (flags & (SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.Morphing
                    | SnapshotPlayerFlags.Unmorphing | SnapshotPlayerFlags.Frozen
                    | SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch)) != 0
                || (flags & SnapshotPlayerFlags.Grounded) == 0
                || !IsFiniteVector(state.Facing) || !IsFiniteVector(state.Speed))
            {
                return PlayerAnimation.None;
            }

            Vector3 forward = new(state.Facing.X, 0, state.Facing.Z);
            float facingLengthSquared = forward.LengthSquared;
            if (!float.IsFinite(facingLengthSquared)
                || facingLengthSquared <= SnapshotBipedFacingLengthSquared)
            {
                return PlayerAnimation.None;
            }

            Vector3 speed = new(state.Speed.X, 0, state.Speed.Z);
            float speedSquared = speed.LengthSquared;
            if (!float.IsFinite(speedSquared))
            {
                return PlayerAnimation.None;
            }
            if (speedSquared <= SnapshotBipedIdleSpeedSquared)
            {
                return PlayerAnimation.Idle;
            }

            Vector3 right = new(-forward.Z, 0, forward.X);
            float forwardDot = Vector3.Dot(speed, forward);
            float rightDot = Vector3.Dot(speed, right);
            if (!float.IsFinite(forwardDot) || !float.IsFinite(rightDot))
            {
                return PlayerAnimation.None;
            }

            // A tie chooses the forward/backward axis so the result is stable
            // at diagonal crossings. The basis matches ProcessBiped movement.
            if (MathF.Abs(forwardDot) >= MathF.Abs(rightDot))
            {
                return forwardDot >= 0 ? PlayerAnimation.WalkForward : PlayerAnimation.WalkBackward;
            }
            return rightDot >= 0 ? PlayerAnimation.WalkRight : PlayerAnimation.WalkLeft;
        }

        private static bool IsFiniteVector(Vector3 value) => float.IsFinite(value.X)
            && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        internal static bool CanApplySnapshotBipedAnimation(PlayerAnimation current, AnimFlags flags)
            => !flags.TestFlag(AnimFlags.NoLoop) || flags.TestFlag(AnimFlags.Ended)
                || IsSnapshotBipedAnimation(current);

        private static bool IsSnapshotBipedAnimation(PlayerAnimation animation)
            => animation is PlayerAnimation.Idle or PlayerAnimation.WalkForward
                or PlayerAnimation.WalkBackward or PlayerAnimation.WalkLeft
                or PlayerAnimation.WalkRight;

        internal BeamType AffinitySlotWeapon => _weaponSlots[2];
        internal InputCommand CaptureNetworkInput(uint sequence, uint viewServerTick)
        {
            if (_scene.Services.DesiredSpectating)
            {
                return new InputCommand(sequence, sequence, viewServerTick, InputButtons.Spectate,
                    InputButtons.None, _gunVec1, InputCommand.NoWeapon);
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
            return new InputCommand(sequence, sequence, viewServerTick, held, pressed, _gunVec1,
                (byte)CurrentWeapon);
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
            _desiredSnapshotBipedAnimation = DeriveSnapshotBipedAnimation(state, local);
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
}
