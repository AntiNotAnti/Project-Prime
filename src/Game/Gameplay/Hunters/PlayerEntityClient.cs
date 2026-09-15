using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>
    /// Allocation-free diagnostic picture for one player. Counters are
    /// monotonic for the lifetime of the player slot and describe real
    /// reconciliation/fallback paths.
    /// </summary>
    internal readonly record struct AltFormDiagnostic(
        byte Slot,
        Hunter Hunter,
        bool AuthorityAltForm,
        bool PresentedAltForm,
        bool Morphing,
        bool Unmorphing,
        AltActionPhase AltActionPhase,
        ushort AltActionTicks,
        uint PoseEpoch,
        Vector3 ControlHeading,
        CameraType CameraType,
        long FormReconciliationEvents,
        long ForcedFormCorrections,
        long AltActionCorrections,
        long MorphPhaseMismatches,
        long InvalidHeadingFallbacks,
        long CameraRecoveryEvents);

    public partial class PlayerEntity
    {
        // Remote snapshots do not drive Controls, so keep their locomotion
        // intent separate from the simulation state. This is consumed by the
        // game-thread animation pass rather than applied while snapshots are
        // repeatedly reconciled.
        private PlayerAnimation _desiredRemoteBipedAnimation = PlayerAnimation.None;
        // A replay actor has no local Controls/Walking flag. Keep the weapon
        // bob decision beside the delayed snapshot animation intent so the
        // first-person replay view follows recorded presentation state rather
        // than whatever collision happened to run on the viewer.
        private bool _replayWeaponBob;
        private RemoteLocomotionHysteresis _remoteLocomotion;
        // Replicated alt actions are presentation state, not PlayerFlags2
        // gameplay state. Keeping the phase here prevents a remote snapshot
        // from activating damage, projectiles, or cooldown logic locally.
        private AltActionState _presentedAltAction;
        private long _altFormReconciliationEvents;
        private long _altFormForcedCorrections;
        private long _altActionCorrections;
        private long _altMorphPhaseMismatches;
        private long _altInvalidHeadingFallbacks;
        private bool _presentedNoxusIntroAudio;
        private bool _presentedNoxusLoopAudio;

        internal AltActionState PresentedAltAction => _presentedAltAction;
        internal bool HasPresentedAltAction
            => _presentedAltAction.Phase != AltActionPhase.None;

        internal AltFormDiagnostic CaptureAltFormDiagnostic()
            => CaptureAltFormDiagnostic(IsAltForm, IsMorphing, IsUnmorphing,
                _presentedAltAction);

        internal AltFormDiagnostic CaptureAltFormDiagnostic(
            in SnapshotPlayer authorityState)
        {
            SnapshotPlayerFlags flags = authorityState.Flags;
            return CaptureAltFormDiagnostic(
                flags.TestFlag(SnapshotPlayerFlags.AltForm),
                flags.TestFlag(SnapshotPlayerFlags.Morphing),
                flags.TestFlag(SnapshotPlayerFlags.Unmorphing),
                ResolveSnapshotAltAction(authorityState));
        }

        private AltFormDiagnostic CaptureAltFormDiagnostic(bool authorityAltForm,
            bool authorityMorphing, bool authorityUnmorphing,
            AltActionState authorityAction)
        {
            bool rollingAlt = authorityAltForm && !UsesStrafeAltMovement;
            Vector3 controlHeading = ResolveAltDiagnosticControlHeading(
                rollingAlt, new Vector3(_altRollFbX, 0, _altRollFbZ),
                _facingVector);
            return new AltFormDiagnostic((byte)SlotIndex, Hunter,
                authorityAltForm, IsAltForm, authorityMorphing,
                authorityUnmorphing, authorityAction.Phase,
                authorityAction.Ticks, PresentationPoseEpoch, controlHeading,
                CameraType, _altFormReconciliationEvents,
                _altFormForcedCorrections, _altActionCorrections,
                _altMorphPhaseMismatches, _altInvalidHeadingFallbacks,
                CameraRecoveryEvents: 0);
        }

        internal static Vector3 ResolveAltDiagnosticControlHeading(
            bool rollingAlt, Vector3 retainedHeading, Vector3 fallbackHeading)
        {
            if (rollingAlt && VectorMath.TryNormalizeHorizontal(
                    retainedHeading, out Vector3 retained))
            {
                return retained;
            }
            return VectorMath.NormalizeHorizontalOr(fallbackHeading,
                -Vector3.UnitZ);
        }

        internal static PlayerAnimation DeriveSnapshotBipedAnimation(in SnapshotPlayer state, bool local)
            => local ? PlayerAnimation.None
                : RemoteLocomotionHysteresis.Classify(state, state.Speed);

        internal static bool HasSnapshotFormChanged(bool currentAltForm,
            SnapshotPlayerFlags flags)
            => currentAltForm != ((flags & SnapshotPlayerFlags.AltForm) != 0);

        internal static bool ShouldStartSnapshotFormSwitch(bool currentAltForm,
            bool currentMorphing, bool currentUnmorphing,
            SnapshotPlayerFlags flags)
        {
            bool morphing = (flags & SnapshotPlayerFlags.Morphing) != 0;
            bool unmorphing = (flags & SnapshotPlayerFlags.Unmorphing) != 0;
            if (morphing)
            {
                return !currentAltForm && !currentMorphing && !currentUnmorphing;
            }
            if (unmorphing)
            {
                return currentAltForm && !currentMorphing && !currentUnmorphing;
            }
            return false;
        }

        internal static bool ShouldForceSnapshotForm(bool currentAltForm,
            SnapshotPlayerFlags flags)
        {
            SnapshotPlayerFlags transition = SnapshotPlayerFlags.Morphing
                | SnapshotPlayerFlags.Unmorphing;
            return (flags & transition) == 0
                && HasSnapshotFormChanged(currentAltForm, flags);
        }

        internal static AltActionState ResolveSnapshotAltAction(
            in SnapshotPlayer state)
        {
            // Protocols through 24 are adapted by the frozen replay codecs,
            // but direct state fixtures and old callers may still provide the
            // compatibility bit without the appended suffix.
            if (state.AltAction.Phase == AltActionPhase.None
                && state.AltAction.Ticks == 0)
            {
                return AltActionState.FromLegacy(state.Hunter, state.Flags);
            }
            return state.AltAction;
        }

        internal static bool ShouldApplyReplicatedAltAction(bool spawned,
            ushort health, Hunter hunter, SnapshotPlayerFlags flags,
            AltActionState action)
        {
            return action.Phase != AltActionPhase.None
                && AltActionState.IsValidFor(hunter, spawned && health > 0,
                    (flags & SnapshotPlayerFlags.AltForm) != 0, flags, action);
        }

        internal static bool ShouldReconcileReplicatedAltAction(bool predicted,
            bool newLife) => !predicted || newLife;

        internal static bool ShouldAdvanceAltActionPoseEpoch(
            AltActionState current, AltActionState next)
            => current.Phase != next.Phase
                || next.Phase != AltActionPhase.None
                && current.Phase == next.Phase
                && next.Ticks < current.Ticks;

        /// <summary>Map 60 Hz authoritative elapsed ticks to the authored
        /// 30 Hz frame used when an actor joins or seeks mid-action.</summary>
        internal static int ResolveAltActionPresentationFrame(
            AltActionState action, int frameCount)
        {
            if (frameCount <= 0 || action.Phase != AltActionPhase.Active)
                return 0;
            return Math.Clamp(action.Ticks / SimTicks.TicksPer30HzFrame,
                0, frameCount - 1);
        }

        /// <summary>
        /// Resolve Noxus's authored extension frame from the replicated phase.
        /// It is pure so replay and live presentation seek the same pose even
        /// when a snapshot skips several simulation ticks.
        /// </summary>
        internal static int ResolveNoxusAltPresentationFrame(
            AltActionState action, int frameCount, int startupTicks)
        {
            if (frameCount <= 0 || action.Phase == AltActionPhase.None)
                return 0;
            if (action.Phase is AltActionPhase.Active or AltActionPhase.Recovery)
                return frameCount - 1;
            // The authored formula advances the 30 Hz animation frame every
            // two authoritative simulation ticks. Keep that quantization while
            // accepting the startup duration in the same 60 Hz unit as the
            // replicated phase state.
            int startupFrames = Math.Max(1,
                startupTicks / SimTicks.TicksPer30HzFrame);
            long numerator = (long)(action.Ticks / 2) * frameCount - 1;
            int frame = (int)(numerator / startupFrames);
            return Math.Clamp(frame, 0, frameCount - 1);
        }

        // Source-compatibility wrappers for focused tests and older callers.
        internal static bool ShouldApplyReplicatedAltAttack(bool spawned,
            ushort health, Hunter hunter, SnapshotPlayerFlags flags)
            => ShouldApplyReplicatedAltAction(spawned, health, hunter, flags,
                AltActionState.FromLegacy(hunter, flags));

        internal static bool ShouldReconcileReplicatedAltAttack(Hunter hunter,
            bool predicted, bool newLife)
            => (!predicted || newLife) && SupportsReplicatedAltAttack(hunter);

        /// <summary>
        /// Stateless classification retained for focused tests and replay
        /// tooling. Live remote presentation uses the stateful resolver below
        /// with the velocity of the delayed presentation sample.
        /// </summary>
        internal static PlayerAnimation DeriveRemoteBipedAnimation(
            in SnapshotPlayer state, Vector3 visualSpeed)
            => RemoteLocomotionHysteresis.Classify(state, visualSpeed);

        internal static bool ShouldAdvanceReplayWeaponBob(
            in SnapshotPlayer state, Vector3 visualSpeed)
            => IsBipedWalk(DeriveRemoteBipedAnimation(state, visualSpeed));

        internal void SetRemoteLocomotionIntent(in SnapshotPlayer state,
            Vector3 visualSpeed, bool trajectoryHeld = false)
        {
            _desiredRemoteBipedAnimation = _remoteLocomotion.Resolve(state,
                visualSpeed, trajectoryHeld);
            _replayWeaponBob = IsBipedWalk(_desiredRemoteBipedAnimation);
        }

        internal void ResetRemoteLocomotion()
        {
            _remoteLocomotion.Reset();
            _desiredRemoteBipedAnimation = PlayerAnimation.None;
            _replayWeaponBob = false;
        }

        internal void ResetPresentedAltAction()
        {
            AltActionState previous = _presentedAltAction;
            _presentedAltAction = AltActionState.None;
            ClearReplicatedAltActionPresentation(previous,
                playNoxusRelease: false);
        }

        /// <summary>
        /// Apply only the visual consequence of an authoritative alt-action
        /// phase. This deliberately never calls Begin/EndAltAttack: those
        /// helpers own gameplay activation, damage, effects, and cooldowns.
        /// </summary>
        private void ApplyReplicatedAltAction(in SnapshotPlayer state,
            bool reset)
        {
            AltActionState next = reset ? AltActionState.None
                : ResolveSnapshotAltAction(state);
            if (!reset && !ShouldApplyReplicatedAltAction(
                    (state.Flags & SnapshotPlayerFlags.Spawned) != 0,
                    state.Health, state.Hunter, state.Flags, next))
            {
                next = AltActionState.None;
            }

            AltActionState previous = _presentedAltAction;
            _presentedAltAction = next;
            bool actionDiscontinuity = ShouldAdvanceAltActionPoseEpoch(
                previous, next);
            if (actionDiscontinuity)
            {
                _altActionCorrections++;
            }
            if (next.Phase == AltActionPhase.None)
            {
                ClearReplicatedAltActionPresentation(previous,
                    playNoxusRelease: !reset);
                return;
            }
            ApplyReplicatedAltActionPresentation(previous, next,
                actionDiscontinuity);
        }

        private void ApplyReplicatedAltActionPresentation(
            AltActionState previous, AltActionState action,
            bool actionDiscontinuity)
        {
            if (_altModel == null || !IsAltForm) return;
            switch (Hunter)
            {
                case Hunter.Trace:
                    if (action.Phase == AltActionPhase.Active
                        && (actionDiscontinuity || _altModel.AnimInfo.Index[0]
                            != (int)TraceAltAnim.Attack))
                    {
                        _altModel.SetAnimation((int)TraceAltAnim.Attack,
                            AnimFlags.NoLoop);
                        SeekReplicatedAltActionAnimation(action);
                    }
                    break;
                case Hunter.Weavel:
                    if (action.Phase == AltActionPhase.Active
                        && (actionDiscontinuity || _altModel.AnimInfo.Index[0]
                            != (int)WeavelAltAnim.Attack))
                    {
                        _altModel.SetAnimation((int)WeavelAltAnim.Attack,
                            AnimFlags.NoLoop);
                        SeekReplicatedAltActionAnimation(action);
                    }
                    break;
                case Hunter.Spire:
                    if (action.Phase == AltActionPhase.Active
                        && (actionDiscontinuity || _altModel.AnimInfo.Index[0]
                            != (int)SpireAltAnim.Attack))
                    {
                        _altModel.SetAnimation((int)SpireAltAnim.Attack,
                            AnimFlags.NoLoop);
                        SeekReplicatedAltActionAnimation(action);
                        _spireRockPosR = Position;
                        _spireRockPosL = Position;
                        _spireAltUp = _fieldC0;
                        Vector3 cross = Vector3.Cross(_facingVector, _spireAltUp);
                        _spireAltFacing = VectorMath.NormalizeOr(
                            Vector3.Cross(_spireAltUp, cross), _facingVector);
                    }
                    break;
                case Hunter.Guardian:
                    // Psycho Bit's authored attack groups are enemy clips,
                    // not a safe player locomotion/attack mapping. Keep the
                    // replicated action as effects/audio/gameplay state and
                    // leave the model on its stable player pose.
                    EnsureGuardianAltStablePose();
                    break;
                case Hunter.Noxus:
                    if (action.Phase is AltActionPhase.Charging
                        or AltActionPhase.Active)
                    {
                        if (actionDiscontinuity || _altModel.AnimInfo.Index[0]
                            != (int)NoxusAltAnim.Extend)
                        {
                            _altModel.SetAnimation((int)NoxusAltAnim.Extend,
                                AnimFlags.NoLoop);
                        }
                        _altModel.AnimInfo.Frame[0] =
                            ResolveNoxusAltPresentationFrame(action,
                                _altModel.AnimInfo.FrameCount[0],
                                SimTicks.From30HzFrames(Values.AltAttackStartup));
                        ApplyReplicatedNoxusAudio(previous, action,
                            actionDiscontinuity);
                    }
                    // Recovery is reserved. Do not synthesize a Noxus
                    // recovery animation or timer that the authority did not
                    // provide.
                    break;
            }
        }

        private void SeekReplicatedAltActionAnimation(AltActionState action)
        {
            _altModel!.AnimInfo.Frame[0] = ResolveAltActionPresentationFrame(
                action, _altModel.AnimInfo.FrameCount[0]);
        }

        /// <summary>
        /// Presentation-only Noxus audio. Gameplay input owns the authoritative
        /// attack; this adapter emits only the authored charge cues for remote
        /// and replay actors and never calls an activation helper.
        /// </summary>
        private void ApplyReplicatedNoxusAudio(AltActionState previous,
            AltActionState action, bool actionDiscontinuity)
        {
            bool rewound = actionDiscontinuity
                && previous.Phase == action.Phase
                && action.Ticks < previous.Ticks;
            bool skippedActionBoundary = actionDiscontinuity
                && previous.Phase != AltActionPhase.None
                && previous.Phase != AltActionPhase.Charging
                && action.Phase == AltActionPhase.Charging;
            if (rewound || skippedActionBoundary)
                ClearReplicatedNoxusAudio(previous, playRelease: false);

            int introTick = Math.Max(0,
                SimTicks.From30HzFrames(7) - 1);
            int loopTick = Math.Max(0,
                SimTicks.From30HzFrames(Values.AltAttackStartup) / 2 - 1);
            if (action.Phase == AltActionPhase.Active)
            {
                _presentedNoxusIntroAudio = true;
                if (!_presentedNoxusLoopAudio)
                {
                    _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK2, loop: true);
                    _presentedNoxusLoopAudio = true;
                }
                return;
            }
            if (action.Phase != AltActionPhase.Charging) return;

            bool bootstrap = previous.Phase != AltActionPhase.Charging
                || action.Ticks < previous.Ticks;
            if (!_presentedNoxusIntroAudio && action.Ticks >= introTick)
            {
                // Joining after the looping charge has begun should bootstrap
                // only that stable loop, not replay a stale one-shot intro.
                if (!bootstrap || action.Ticks < loopTick)
                    _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK1);
                _presentedNoxusIntroAudio = true;
            }
            if (!_presentedNoxusLoopAudio && action.Ticks >= loopTick)
            {
                _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK2, loop: true);
                _presentedNoxusLoopAudio = true;
            }
        }

        private void ClearReplicatedAltActionPresentation(
            AltActionState previous, bool playNoxusRelease)
        {
            if (previous.Phase == AltActionPhase.None)
                return;
            if (Hunter == Hunter.Noxus)
                ClearReplicatedNoxusAudio(previous, playNoxusRelease);
            if (_altModel == null) return;
            switch (Hunter)
            {
                case Hunter.Trace:
                    _altModel.SetAnimation((int)TraceAltAnim.Idle,
                        AnimFlags.None);
                    break;
                case Hunter.Weavel:
                    _altModel.SetAnimation((int)WeavelAltAnim.Idle,
                        AnimFlags.None);
                    break;
                case Hunter.Guardian:
                    EnsureGuardianAltStablePose();
                    break;
                case Hunter.Noxus:
                    _altModel.SetAnimation((int)NoxusAltAnim.Extend,
                        AnimFlags.Paused);
                    break;
                // Spire has one authored alt animation group; normal end
                // handling leaves its final pose in place.
                case Hunter.Spire:
                    break;
            }
        }

        private void ClearReplicatedNoxusAudio(AltActionState previous,
            bool playRelease)
        {
            if (_presentedNoxusIntroAudio)
                _soundSource.StopSfx(SfxId.NOX_TOP_ATTACK1);
            if (_presentedNoxusLoopAudio)
                _soundSource.StopSfx(SfxId.NOX_TOP_ATTACK2);
            int releaseTick = Math.Max(0,
                SimTicks.From30HzFrames(Values.AltAttackStartup) / 2 - 1);
            if (playRelease && (previous.Phase == AltActionPhase.Active
                || previous.Phase == AltActionPhase.Charging
                && previous.Ticks >= releaseTick))
            {
                _soundSource.PlaySfx(SfxId.NOX_TOP_ATTACK3);
            }
            _presentedNoxusIntroAudio = false;
            _presentedNoxusLoopAudio = false;
        }

        internal static bool CanApplySnapshotBipedAnimation(PlayerAnimation current, AnimFlags flags)
            => !flags.TestFlag(AnimFlags.NoLoop) || flags.TestFlag(AnimFlags.Ended)
                || IsSnapshotBipedAnimation(current);

        private static bool IsSnapshotBipedAnimation(PlayerAnimation animation)
            => animation is PlayerAnimation.Idle or PlayerAnimation.WalkForward
                or PlayerAnimation.WalkBackward or PlayerAnimation.WalkLeft
                or PlayerAnimation.WalkRight or PlayerAnimation.JumpNeutral
                or PlayerAnimation.JumpForward or PlayerAnimation.JumpBack
                or PlayerAnimation.JumpLeft or PlayerAnimation.JumpRight;

        internal static bool IsSnapshotJumpAnimation(PlayerAnimation animation)
            => animation is PlayerAnimation.JumpNeutral
                or PlayerAnimation.JumpForward or PlayerAnimation.JumpBack
                or PlayerAnimation.JumpLeft or PlayerAnimation.JumpRight;

        internal static bool ShouldSetBipedLocomotionAnimation(
            PlayerAnimation desired, PlayerAnimation current,
            bool remoteAnimation)
            => desired != current
                || !remoteAnimation && IsSnapshotJumpAnimation(desired);

        internal static bool IsBipedWalk(PlayerAnimation animation)
            => animation is PlayerAnimation.WalkForward or PlayerAnimation.WalkBackward
                or PlayerAnimation.WalkLeft or PlayerAnimation.WalkRight;

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
            if (AnalogMovementPresent)
            {
                // GamepadInput captures the pre-pad digital state before it
                // ORs legacy direction binds into Controls. This keeps the
                // command's digital component exact while the two axes carry
                // the controller's radial contribution.
                held |= DigitalMovementButtonsBeforeAnalog
                    | DigitalRollButtonsBeforeAnalog;
                pressed |= DigitalMovementPressedBeforeAnalog
                    | DigitalRollPressedBeforeAnalog;
            }
            else
            {
                Capture(Controls.MoveLeft, InputButtons.Left, ref held, ref pressed);
                Capture(Controls.MoveRight, InputButtons.Right, ref held, ref pressed);
                Capture(Controls.MoveUp, InputButtons.Forward, ref held, ref pressed);
                Capture(Controls.MoveDown, InputButtons.Back, ref held, ref pressed);
                Capture(Controls.RolltLeft, InputButtons.RollLeft, ref held, ref pressed);
                Capture(Controls.RollRight, InputButtons.RollRight, ref held, ref pressed);
                Capture(Controls.RollUp, InputButtons.RollForward, ref held, ref pressed);
                Capture(Controls.RollDown, InputButtons.RollBack, ref held, ref pressed);
            }
            Capture(Controls.Shoot, InputButtons.Shoot, ref held, ref pressed);
            Capture(Controls.Zoom, InputButtons.Zoom, ref held, ref pressed);
            Capture(Controls.Jump, InputButtons.Jump, ref held, ref pressed);
            Capture(Controls.Morph, InputButtons.Morph, ref held, ref pressed);
            Capture(Controls.Boost, InputButtons.Boost, ref held, ref pressed);
            Capture(Controls.AltAttack, InputButtons.AltAttack, ref held, ref pressed);
            Capture(Controls.NextWeapon, InputButtons.NextWeapon, ref held, ref pressed);
            Capture(Controls.PrevWeapon, InputButtons.PreviousWeapon, ref held, ref pressed);
            BoostIntent boostIntent = Input.ConsumedBoostIntent;
            sbyte moveX = 0, moveY = 0;
            bool analogPresent = AnalogMovementPresent
                && AnalogMovementCodec.TryQuantize(AnalogMovement,
                    out moveX, out moveY);
            if (!analogPresent)
            {
                moveX = moveY = 0;
            }
            return new InputCommand(sequence, sequence, viewServerTick, held, pressed, ModInputAim,
                (byte)CurrentWeapon, boostIntent, inputEpoch, moveX, moveY,
                analogPresent);
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
            Recolor = PlayableHunterCatalog.ValidateRecolor(Hunter,
                _scene.Match.Rules.Teams ? TeamIndex == 0 ? 4 : 5 : 0);
            IsBot = false;
            LoadFlags = LoadFlags.SlotActive | LoadFlags.Active | LoadFlags.Initial
                | LoadFlags.Connected | LoadFlags.WasConnected;
            ReloadInit = false;
            Flags1 = 0;
            Flags2 = 0;
            Initialize();
            // Authoritative activation can replace the placeholder hunter after
            // the scene presentation has already loaded. Initialize rebuilds the
            // model list, so register those replacement meshes before the next
            // sealed render frame consumes them.
            _scene.InitEntity(this);
        }

        internal void ApplyServerState(in SnapshotPlayer state, bool newLife,
            bool predicted = false, bool reconcileWeapon = true,
            bool preservePredictedCharge = false)
        {
            SetClientCombatIdentity(state);
            bool spawned = (state.Flags & SnapshotPlayerFlags.Spawned) != 0;
            SnapshotPlayerFlags flags = state.Flags;
            bool formChanged = SupportsAltForm(Hunter)
                && HasSnapshotFormChanged(IsAltForm, flags);
            bool spectatorChanged = Flags2.TestFlag(PlayerFlags2.Spectating)
                != ((flags & SnapshotPlayerFlags.Spectating) != 0);
            bool reconcileAltAction = ShouldReconcileReplicatedAltAction(
                predicted, newLife);
            bool resetAltAction = !spawned || state.Health == 0
                || (flags & (SnapshotPlayerFlags.Spectating
                    | SnapshotPlayerFlags.Unmorphing)) != 0
                || (flags & SnapshotPlayerFlags.AltForm) == 0;
            AltActionState incomingAltAction = ResolveSnapshotAltAction(state);
            AltActionState desiredAltAction = _presentedAltAction;
            if (reconcileAltAction)
            {
                desiredAltAction = resetAltAction
                    || !ShouldApplyReplicatedAltAction(spawned, state.Health,
                        state.Hunter, flags, incomingAltAction)
                    ? AltActionState.None : incomingAltAction;
            }
            bool altActionPhaseChanged = reconcileAltAction
                && ShouldAdvanceAltActionPoseEpoch(_presentedAltAction,
                    desiredAltAction);
            bool deactivated = LoadFlags.TestFlag(LoadFlags.Active)
                && ((flags & SnapshotPlayerFlags.Active) == 0 || !spawned);
            bool died = Health > 0 && state.Health == 0;
            bool snapshotTransition = (flags & (SnapshotPlayerFlags.Morphing
                | SnapshotPlayerFlags.Unmorphing)) != 0;
            bool snapshotMorphing = (flags & SnapshotPlayerFlags.Morphing) != 0;
            bool snapshotUnmorphing = (flags & SnapshotPlayerFlags.Unmorphing) != 0;
            bool morphPhaseMismatch = snapshotTransition
                && (snapshotMorphing != IsMorphing
                    || snapshotUnmorphing != IsUnmorphing);
            if (formChanged) _altFormReconciliationEvents++;
            if (morphPhaseMismatch) _altMorphPhaseMismatches++;
            if (newLife) ClearPowerupPresentationState();
            if (newLife || !spawned || died || formChanged || spectatorChanged)
                Input.ClearBoostIntents();
            if (newLife || deactivated || died || formChanged || spectatorChanged
                || altActionPhaseChanged)
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
            ApplyReplicatedPowerupState(state, reset: !spawned || state.Health == 0
                || (flags & SnapshotPlayerFlags.Spectating) != 0);
            ApplyEnhancedSnapshot(state, reset: !spawned || state.Health == 0
                || (flags & SnapshotPlayerFlags.Spectating) != 0);
            Health = state.Health;
            for (int weapon = 0; weapon <= 8; weapon++)
            {
                _availableWeapons[(BeamType)weapon] = (state.AvailableWeapons & (1 << weapon)) != 0;
            }
            if (reconcileWeapon)
            {
                ModSetWeapon((BeamType)state.Weapon);
            }
            _ammo[UA] = state.AmmoUa;
            _ammo[Missiles] = state.AmmoMissiles;
            int fullCharge = SimTicks.From30HzFrames(EquipInfo.Weapon.FullCharge);
            EquipInfo.ChargeLevel = ResolveSnapshotCharge(
                EquipInfo.ChargeLevel, state.ChargeLevel, fullCharge,
                predicted, preservePredictedCharge);
            EquipInfo.Zoomed = (state.Flags & SnapshotPlayerFlags.Zoomed) != 0;
            bool alt = (state.Flags & SnapshotPlayerFlags.AltForm) != 0;
            if (!predicted && SupportsAltForm(Hunter)
                && ShouldStartSnapshotFormSwitch(IsAltForm,
                IsMorphing, IsUnmorphing, flags))
            {
                ModStartFormSwitch(transferHalfturretHealth: false);
            }
            else if ((!predicted || newLife) && SupportsAltForm(Hunter)
                && !snapshotTransition
                && ShouldForceSnapshotForm(IsAltForm, flags))
            {
                ModForceForm(alt, transferHalfturretHealth: false);
            }
            ModSetSpectating((state.Flags & SnapshotPlayerFlags.Spectating) != 0);
            if ((state.Flags & SnapshotPlayerFlags.Active) != 0) LoadFlags |= LoadFlags.Active;
            else LoadFlags &= ~LoadFlags.Active;
            // Freeze gates local prediction; burn/disruption remain presentation-only
            // so snapshot reconciliation cannot start client-generated burn damage.
            _frozenTimer = state.FrozenTicks;
            Flags2 &= ~(PlayerFlags2.RadarReveal | PlayerFlags2.RadarRevealPrevious);
            if ((state.Flags & SnapshotPlayerFlags.RadarReveal) != 0) Flags2 |= PlayerFlags2.RadarReveal;
            if ((state.Flags & SnapshotPlayerFlags.RadarRevealPrevious) != 0) Flags2 |= PlayerFlags2.RadarRevealPrevious;
            if (reconcileAltAction)
            {
                ApplyReplicatedAltAction(state, resetAltAction);
            }
        }

        internal static ushort ResolveSnapshotCharge(ushort current,
            ushort authoritative, int fullCharge, bool predicted,
            bool preservePredictedCharge)
            => predicted || preservePredictedCharge
                ? current : (ushort)Math.Min(authoritative, fullCharge);

        /// <summary>
        /// Applies only the authoritative presentation timers. Pickup effects
        /// are intentionally not replayed here: the snapshot is a state
        /// correction, not a second pickup event. Expiry and lifecycle clears
        /// still retire any effect that a prior live/replay state created.
        /// </summary>
        private void ApplyReplicatedPowerupState(in SnapshotPlayer state, bool reset)
        {
            bool hadDoubleDamage = _doubleDmgTimer > 0;
            bool hadCloaking = Flags2.TestFlag(PlayerFlags2.Cloaking);
            if (reset)
            {
                ClearPowerupPresentationState();
                return;
            }
            else
            {
                _doubleDmgTimer = state.DoubleDamageTicks;
                _cloakTimer = state.CloakTicks;
                _deathaltTimer = state.DeathaltTicks;
                bool cloaking = (state.Flags & SnapshotPlayerFlags.Cloaking) != 0
                    && _cloakTimer > 0;
                if (cloaking) Flags2 |= PlayerFlags2.Cloaking;
                else
                {
                    Flags2 &= ~PlayerFlags2.Cloaking;
                    _targetAlpha = 1;
                }
            }

            if (_doubleDmgTimer == 0)
            {
                if (hadDoubleDamage && IsMainPlayer) UpdateDoubleDamageSfx(0, play: false);
                if (_doubleDmgEffect != null)
                {
                    _scene.UnlinkEffectEntry(_doubleDmgEffect);
                    _doubleDmgEffect = null;
                }
            }
            if (_deathaltTimer == 0 && _deathaltEffect != null)
            {
                _scene.UnlinkEffectEntry(_deathaltEffect);
                _deathaltEffect = null;
            }
            if (!Flags2.TestFlag(PlayerFlags2.Cloaking)
                && hadCloaking && IsMainPlayer)
            {
                UpdateCloakSfx(0, play: false);
            }
        }

        private void ClearPowerupPresentationState()
        {
            _doubleDmgTimer = 0;
            _cloakTimer = 0;
            _deathaltTimer = 0;
            Flags2 &= ~PlayerFlags2.Cloaking;
            _targetAlpha = 1;
            if (IsMainPlayer)
            {
                UpdateDoubleDamageSfx(0, play: false);
                UpdateCloakSfx(0, play: false);
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
        // Extrapolation already covers the first 50 ms of a snapshot gap.
        // Keep the last authored motion for another 100 ms so a single late
        // packet cannot visibly snap every remote player to idle.
        internal const int HeldMotionGraceTicks = 6;

        private const float MoveStartSpeedSquared = MoveStartSpeed * MoveStartSpeed;
        private const float MoveStopSpeedSquared = MoveStopSpeed * MoveStopSpeed;
        private const float FacingLengthSquared = 0.0001f;

        private bool _moving;
        private PlayerAnimation _direction;
        private int _heldMotionTicks;

        internal PlayerAnimation Resolve(in SnapshotPlayer state,
            Vector3 visualSpeed, bool trajectoryHeld = false)
        {
            if (!TryGetBasisAndSpeed(state, visualSpeed, out Vector3 forward,
                out Vector3 speed, out float speedSquared))
            {
                Reset();
                return PlayerAnimation.None;
            }

            if (trajectoryHeld && IsMoving(_direction)
                && _heldMotionTicks++ < HeldMotionGraceTicks)
            {
                return _direction;
            }
            _heldMotionTicks = 0;

            if ((state.Flags & SnapshotPlayerFlags.Grounded) == 0)
            {
                _moving = true;
                _direction = SelectJumpDirection(forward, speed,
                    speedSquared, _direction);
                return _direction;
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
            _heldMotionTicks = 0;
        }

        internal static PlayerAnimation Classify(in SnapshotPlayer state,
            Vector3 visualSpeed)
        {
            if (!TryGetBasisAndSpeed(state, visualSpeed, out Vector3 forward,
                out Vector3 speed, out float speedSquared))
                return PlayerAnimation.None;
            if ((state.Flags & SnapshotPlayerFlags.Grounded) == 0)
                return SelectJumpDirection(forward, speed, speedSquared,
                    PlayerAnimation.Idle);
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

        private static PlayerAnimation SelectJumpDirection(Vector3 forward,
            Vector3 speed, float speedSquared, PlayerAnimation current)
        {
            if (speedSquared <= MoveStopSpeedSquared)
                return PlayerAnimation.JumpNeutral;
            PlayerAnimation walk = SelectDirection(forward, speed,
                ToWalkDirection(current));
            return walk switch
            {
                PlayerAnimation.WalkForward => PlayerAnimation.JumpForward,
                PlayerAnimation.WalkBackward => PlayerAnimation.JumpBack,
                PlayerAnimation.WalkLeft => PlayerAnimation.JumpLeft,
                PlayerAnimation.WalkRight => PlayerAnimation.JumpRight,
                _ => PlayerAnimation.JumpNeutral
            };
        }

        private static PlayerAnimation ToWalkDirection(PlayerAnimation animation)
            => animation switch
            {
                PlayerAnimation.JumpForward => PlayerAnimation.WalkForward,
                PlayerAnimation.JumpBack => PlayerAnimation.WalkBackward,
                PlayerAnimation.JumpLeft => PlayerAnimation.WalkLeft,
                PlayerAnimation.JumpRight => PlayerAnimation.WalkRight,
                _ => animation
            };

        private static bool IsMoving(PlayerAnimation animation)
            => PlayerEntity.IsBipedWalk(animation)
                || PlayerEntity.IsSnapshotJumpAnimation(animation);

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
