using System;
using MphRead.Mods.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private readonly record struct MovementShadowFrameParameters(
            bool ImpulseSet,
            Vector3 HorizontalImpulse,
            float HorizontalSpeedCap,
            bool SpeedFactorSet,
            float HorizontalMultiplier,
            bool GravityApplied,
            float Gravity,
            bool CollidingLateral,
            bool Morphing,
            bool UsedJumpPad,
            bool BipedStuck);

        private bool _movementShadowWindowActive;
        private NativeMovementReferenceState _movementShadowReferenceStart;
        private MovementBoundarySnapshot _movementShadowStartSnapshot;
        private MovementShadowFrameParameters _movementShadowFirstParameters;

        private bool _movementShadowFrameImpulseSet;
        private Vector3 _movementShadowFrameImpulse;
        private float _movementShadowFrameSpeedCap;
        private bool _movementShadowFrameSpeedFactorSet;
        private float _movementShadowFrameSpeedFactor;
        private bool _movementShadowFrameGravityApplied;
        private float _movementShadowFrameGravity;

        /// <summary>
        /// Start a two-substep observation window at the 60 Hz frame immediately
        /// after a native boundary. State is copied only for the diagnostic and
        /// never written back into the player.
        /// </summary>
        private void ModMovementShadowBeginFrame()
        {
            if (!MovementShadowRuntime.Enabled
                || Hunter != Hunter.Samus && Hunter != Hunter.Spire)
            {
                return;
            }

            _movementShadowFrameImpulseSet = false;
            _movementShadowFrameImpulse = Vector3.Zero;
            _movementShadowFrameSpeedCap = 0;
            _movementShadowFrameSpeedFactorSet = false;
            _movementShadowFrameSpeedFactor = 1;
            _movementShadowFrameGravityApplied = false;
            _movementShadowFrameGravity = 0;

            ulong frame = _scene.FrameCount;
            if ((frame & 1UL) == 1UL)
            {
                _movementShadowWindowActive = true;
                _movementShadowReferenceStart =
                    new NativeMovementReferenceState(Position, Speed, _facingVector);
                // The state before odd frame N is the state at native boundary N-1.
                _movementShadowStartSnapshot = ModMovementShadowSnapshot(frame - 1);
            }
        }

        private void ModMovementShadowNoteHorizontalImpulse(
            Vector3 impulse, float horizontalSpeedCap)
        {
            if (!MovementShadowRuntime.Enabled
                || Hunter != Hunter.Samus && Hunter != Hunter.Spire)
            {
                return;
            }
            _movementShadowFrameImpulseSet = true;
            _movementShadowFrameImpulse = new Vector3(impulse.X, 0, impulse.Z);
            _movementShadowFrameSpeedCap = horizontalSpeedCap;
        }

        private void ModMovementShadowNoteSpeedFactor(float horizontalMultiplier)
        {
            if (!MovementShadowRuntime.Enabled
                || Hunter != Hunter.Samus && Hunter != Hunter.Spire)
            {
                return;
            }
            _movementShadowFrameSpeedFactorSet = true;
            _movementShadowFrameSpeedFactor = horizontalMultiplier;
        }

        private void ModMovementShadowNoteGravity(float gravity)
        {
            if (!MovementShadowRuntime.Enabled
                || Hunter != Hunter.Samus && Hunter != Hunter.Spire)
            {
                return;
            }
            _movementShadowFrameGravityApplied = true;
            _movementShadowFrameGravity = gravity;
        }

        /// <summary>
        /// Capture live production movement after ProcessInput has completed its
        /// movement and collision work. Stable biped windows are also compared
        /// with one collision-free native 30 Hz reference step.
        /// </summary>
        private void ModMovementShadowObserve()
        {
            if (!MovementShadowRuntime.Enabled
                || Hunter != Hunter.Samus && Hunter != Hunter.Spire)
            {
                return;
            }

            ulong frame = _scene.FrameCount;
            MovementShadowFrameParameters parameters = CaptureMovementShadowFrameParameters();
            if ((frame & 1UL) == 1UL)
            {
                if (_movementShadowWindowActive)
                {
                    _movementShadowFirstParameters = parameters;
                }
                return;
            }

            MovementBoundarySnapshot current = ModMovementShadowSnapshot(frame);
            MovementShadowRuntime.ObserveProduction(current);
            if (!_movementShadowWindowActive)
            {
                return;
            }
            _movementShadowWindowActive = false;

            if (!ModMovementShadowStableParameters(
                _movementShadowFirstParameters, parameters))
            {
                MovementShadowRuntime.Skip("substep-parameters-changed");
                return;
            }
            if (!parameters.ImpulseSet || !parameters.SpeedFactorSet)
            {
                MovementShadowRuntime.Skip("movement-parameters-missing");
                return;
            }
            if (_movementShadowStartSnapshot.AltForm || current.AltForm
                || _movementShadowStartSnapshot.SpireClimbing || current.SpireClimbing)
            {
                MovementShadowRuntime.Skip("alt-or-spire-contact-not-modeled");
                return;
            }
            if (_movementShadowFirstParameters.Morphing || parameters.Morphing)
            {
                MovementShadowRuntime.Skip("form-transition");
                return;
            }
            if (_movementShadowFirstParameters.UsedJumpPad || parameters.UsedJumpPad
                || _movementShadowFirstParameters.BipedStuck || parameters.BipedStuck)
            {
                MovementShadowRuntime.Skip("jump-pad-or-biped-lock");
                return;
            }
            if (_movementShadowFirstParameters.CollidingLateral
                || parameters.CollidingLateral)
            {
                MovementShadowRuntime.Skip("lateral-collision");
                return;
            }
            if (_movementShadowStartSnapshot.Grounded != current.Grounded
                || _movementShadowStartSnapshot.Standing != current.Standing
                || _movementShadowStartSnapshot.StandingEntityId != current.StandingEntityId
                || _movementShadowStartSnapshot.Slipperiness != current.Slipperiness)
            {
                MovementShadowRuntime.Skip("contact-state-transition");
                return;
            }

            var input = new NativeMovementReferenceInput(
                _movementShadowFirstParameters.HorizontalImpulse,
                _movementShadowFirstParameters.HorizontalSpeedCap,
                _movementShadowFirstParameters.HorizontalMultiplier,
                _movementShadowFirstParameters.GravityApplied,
                _movementShadowFirstParameters.Gravity,
                0,
                _movementShadowReferenceStart.Facing);
            NativeMovementReferenceState referenceState =
                NativeMovementReference.Step(_movementShadowReferenceStart, input);

            string domain;
            if (current.Standing || current.Grounded)
            {
                // Collision response is intentionally not modeled yet. Preserve
                // production vertical/facing state so this comparison isolates
                // horizontal traction/cap/damping at a stable ground contact.
                domain = "ground-horizontal";
                referenceState = referenceState with
                {
                    Position = new Vector3(
                        referenceState.Position.X, current.Position.Y,
                        referenceState.Position.Z),
                    Velocity = new Vector3(
                        referenceState.Velocity.X, current.Velocity.Y,
                        referenceState.Velocity.Z),
                    Facing = current.Facing
                };
            }
            else
            {
                // Airborne windows with no lateral/contact transition can exercise
                // gravity and semi-implicit position integration as well.
                domain = "air-kinematic";
                referenceState = referenceState with { Facing = current.Facing };
            }

            var reference = new MovementBoundarySnapshot(
                frame,
                current.Slot,
                current.Hunter,
                _movementShadowStartSnapshot.AltForm,
                _movementShadowStartSnapshot.Grounded,
                _movementShadowStartSnapshot.Standing,
                _movementShadowStartSnapshot.SpireClimbing,
                _movementShadowStartSnapshot.StandingEntityId,
                referenceState.Position,
                referenceState.Velocity,
                referenceState.Facing,
                _movementShadowFirstParameters.GravityApplied
                    ? _movementShadowFirstParameters.Gravity
                    : current.Gravity,
                _movementShadowStartSnapshot.Slipperiness,
                Vector3.Zero,
                0);
            MovementShadowRuntime.ObserveReference(current, reference, domain);
        }

        private MovementShadowFrameParameters CaptureMovementShadowFrameParameters()
        {
            return new MovementShadowFrameParameters(
                _movementShadowFrameImpulseSet,
                _movementShadowFrameImpulse,
                _movementShadowFrameSpeedCap,
                _movementShadowFrameSpeedFactorSet,
                _movementShadowFrameSpeedFactor,
                _movementShadowFrameGravityApplied,
                _movementShadowFrameGravity,
                Flags1.TestFlag(PlayerFlags1.CollidingLateral),
                IsMorphing,
                Flags1.TestFlag(PlayerFlags1.UsedJumpPad),
                Flags2.TestFlag(PlayerFlags2.BipedStuck));
        }

        private MovementBoundarySnapshot ModMovementShadowSnapshot(ulong frame)
        {
            int standingEntityId = _standingEntCol?.Entity.Id ?? -1;
            return new MovementBoundarySnapshot(
                frame,
                SlotIndex,
                (int)Hunter,
                IsAltForm,
                Flags1.TestFlag(PlayerFlags1.Grounded),
                Flags1.TestFlag(PlayerFlags1.Standing),
                Flags2.TestFlag(PlayerFlags2.SpireClimbing),
                standingEntityId,
                Position,
                Speed,
                _facingVector,
                _gravity,
                _slipperiness,
                Vector3.Zero,
                0);
        }

        private static bool ModMovementShadowStableParameters(
            in MovementShadowFrameParameters first,
            in MovementShadowFrameParameters second)
        {
            const float epsilon = 1f / 1048576f;
            return first.ImpulseSet == second.ImpulseSet
                && (!first.ImpulseSet
                    || (first.HorizontalImpulse - second.HorizontalImpulse).Length <= epsilon)
                && MathF.Abs(first.HorizontalSpeedCap - second.HorizontalSpeedCap) <= epsilon
                && first.SpeedFactorSet == second.SpeedFactorSet
                && (!first.SpeedFactorSet
                    || MathF.Abs(first.HorizontalMultiplier - second.HorizontalMultiplier) <= epsilon)
                && first.GravityApplied == second.GravityApplied
                && (!first.GravityApplied
                    || MathF.Abs(first.Gravity - second.Gravity) <= epsilon);
        }
    }
}
