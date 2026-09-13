using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// A gamepad, on any platform, driving the game.
    ///
    /// The same trick the Android touch controls use, from the other end:
    /// rather than fork <c>ProcessAllInput</c>, this waits until it has run
    /// and then *adds* the pad's contribution to the same <see cref="Keybind"/>
    /// flags the keyboard just filled in. Everything downstream -- firing,
    /// morphing, the weapon wheel, what goes on the wire as an intent -- reads
    /// those flags and cannot tell where they came from, so a pad and a
    /// keyboard work at once and neither had to be special-cased.
    ///
    /// Aim is the one thing that cannot go through a keybind, because a stick
    /// is analogue and a key is not. It goes in where the mouse's does, at
    /// <c>ApplyModAim</c>, in the same units (degrees of turn per frame) and
    /// at the same point in the frame -- an aim applied at a different moment
    /// from the mouse's would feel different for reasons nobody could name.
    ///
    /// Where the state comes from is the platform's business:
    /// <see cref="GamepadDesktop"/> polls GLFW, and the Android head adds up
    /// the events its window receives. Both write <see cref="State"/>.
    /// </summary>
    public static class GamepadInput
    {
        internal static Action? PollPlatformForMenuAction { get; set; }
        internal static Func<StylusState> ConsumePlatformStylus { get; set; }
            = static () => StylusState.Empty;
        internal static Action CancelPlatformStylus { get; set; } = static () => { };

        internal static void PollPlatformForMenu() => PollPlatformForMenuAction?.Invoke();

        /// <summary>The pad as of this frame.</summary>
        public static GamepadState State;

        /// <summary>
        /// The last capability snapshot published by the active platform
        /// owner. Unlike <see cref="State"/>, this changes only on backend or
        /// device transitions and is safe for settings observers to retain.
        /// </summary>
        public static ControllerCapabilitySnapshot Capabilities
            => ControllerCapabilities.Current;

        /// <summary>
        /// Raised when the active backend or device capability snapshot
        /// changes. Platform publishers stay outside the frame loop.
        /// </summary>
        public static event EventHandler<ControllerCapabilityChangedEventArgs>? CapabilitiesChanged
        {
            add => ControllerCapabilities.Changed += value;
            remove => ControllerCapabilities.Changed -= value;
        }

        /// <summary>
        /// Physical buttons remain on <see cref="GamepadState.Buttons"/>.
        /// Trigger axes are synthesized here, once, after fixed-step
        /// hysteresis. Menus may read the side-effect-free preview below.
        /// </summary>
        public static GamepadButtons EffectiveButtons
        {
            get
            {
                if (_gameplayInputQuarantine || _postQuarantineUntilRelease)
                    return GamepadButtons.None;
                TriggerProcessor preview = _triggers;
                preview.Configure(InputSettings.GamepadTriggerPressThreshold,
                    InputSettings.GamepadTriggerReleaseThreshold);
                TriggerSample trigger = preview.Preview(State.LeftTrigger,
                    State.RightTrigger);
                return GameplayButtons() | trigger.Buttons;
            }
        }

        public static GamepadButtons PressedButtons => _pressed;
        public static GamepadButtons ReleasedButtons => _released;

        private static GamepadButtons _previous;
        private static bool _gameplayInputQuarantine;
        private static bool _postQuarantineUntilRelease;

        /// <summary>Buttons that went down this frame, for the one-shot actions.</summary>
        private static GamepadButtons _pressed;
        private static GamepadButtons _released;
        private static GamepadButtons _effective;
        private static GamepadMovementProcessor _movement = new(
            GamepadMovementProcessor.DefaultInnerDeadzone,
            GamepadMovementProcessor.DefaultActivateThreshold,
            GamepadMovementProcessor.DefaultReleaseThreshold);
        private static TriggerProcessor _triggers = new();
        private static GamepadLookProcessor _look = new();
        private static long _timingGeneration = long.MinValue;
        private static bool _lookConfigured;
        private static bool _gyroAllowedForSimulation;

        /// <summary>The fixed-step movement result used by <see cref="Apply"/>.</summary>
        public static GamepadMovementSample Movement => _movement.Processed;

        /// <summary>Latest side-effect-free render-rate velocity in degrees/sec.</summary>
        public static Vector2 AimAngularVelocity { get; private set; }

        /// <summary>Local look ownership and render prediction boundary.</summary>
        public static LookInputCoordinator LookCoordinator { get; } = LookInputCoordinator.Shared;

        /// <summary>
        /// True while a pad is connected.
        ///
        /// There used to be a setting beside this -- "Use a connected gamepad"
        /// -- and it is gone. It could only ever matter to somebody who had a
        /// pad attached and did not want it, which the automatic handover
        /// answers on its own: nothing on screen changes until a pad button is
        /// actually pressed, and a finger takes the game straight back. What
        /// it cost was a row in the settings that read like it might be the
        /// reason the pad was not working, in the one screen somebody with a
        /// pad that is not working will go looking.
        /// </summary>
        public static bool Active => State.Connected;

        /// <summary>
        /// Whether the pad is being *held*, as opposed to merely connected.
        ///
        /// The dead zone rather than zero, and the trigger threshold rather
        /// than zero, because every pad's sticks and triggers drift at rest
        /// and a pad lying on a table would otherwise look like a pad in
        /// somebody's hands. That is the whole question the Android head asks
        /// before it puts the touch controls away.
        /// </summary>
        public static bool InUse
        {
            get
            {
                if (!Active)
                {
                    return false;
                }
                if (EffectiveButtons != GamepadButtons.None)
                {
                    return true;
                }
                return State.LeftX * State.LeftX + State.LeftY * State.LeftY
                        >= InputSettings.GamepadMoveReleaseThreshold
                            * InputSettings.GamepadMoveReleaseThreshold
                    || State.RightX * State.RightX + State.RightY * State.RightY
                        > InputSettings.GamepadLookDeadZone * InputSettings.GamepadLookDeadZone
                    || State.LeftTrigger >= InputSettings.GamepadTriggerPressThreshold
                    || State.RightTrigger >= InputSettings.GamepadTriggerPressThreshold;
            }
        }

        /// <summary>
        /// What the right stick asked for this frame, in degrees of turn --
        /// the same unit <c>UpdateAimX</c> and <c>UpdateAimY</c> take, and the
        /// same unit the mouse arrives in after its own division.
        /// </summary>
        public static float AimDeltaX { get; private set; }
        public static float AimDeltaY { get; private set; }

        /// <summary>
        /// Called once per fixed simulation step. Native polling only updates
        /// <see cref="State"/> and <see cref="SampleNativeFrame"/>; timing,
        /// hysteresis, button edges and stateful look prediction advance here.
        /// </summary>
        public static void BeginFrame(bool allowLook = true, bool zoomed = false,
            long timingGeneration = 0)
        {
            if (_postQuarantineUntilRelease && !PhysicalControllerInputActive())
                _postQuarantineUntilRelease = false;
            if (!Active)
            {
                ResetControllerState();
                return;
            }

            if (_timingGeneration != timingGeneration)
            {
                ResetTimingState(timingGeneration);
            }

            bool quarantined = InputQuarantined;
            _triggers.Configure(InputSettings.GamepadTriggerPressThreshold,
                InputSettings.GamepadTriggerReleaseThreshold);
            TriggerSample trigger = _triggers.Process(
                quarantined ? 0 : State.LeftTrigger,
                quarantined ? 0 : State.RightTrigger);
            _effective = quarantined ? GamepadButtons.None
                : State.Buttons | trigger.Buttons;
            _pressed = _effective & ~_previous;
            _released = _previous & ~_effective;
            _previous = _effective;
            _movement.Configure(InputSettings.GamepadMoveDeadZone,
                InputSettings.GamepadMoveActivateThreshold,
                InputSettings.GamepadMoveReleaseThreshold);
            _movement.Process(quarantined ? Vector2.Zero
                : new Vector2(State.LeftX, State.LeftY));

            bool weaponRadial = (_effective & PadBindings.Get(PadAction.WeaponWheel)) != 0;
            if (!allowLook || weaponRadial)
            {
                _gyroAllowedForSimulation = false;
                _look.Reset();
                AimAngularVelocity = Vector2.Zero;
                AimDeltaX = AimDeltaY = 0;
                GamepadGyro.SuppressOutput();
                LookCoordinator.SetStatefulVelocity(Vector2.Zero);
            }
            else
            {
                ConfigureLookProcessor();
                LookDeviceKind activeOwner = LookCoordinator.ActiveLookDevice;
                if (LookDeviceTracker.IsPrecisionDevice(activeOwner))
                {
                    // A precision-device handoff must reacquire sustained
                    // outer-ring boost instead of inheriting old controller
                    // timing across ownership boundaries.
                    _look.Reset();
                }
                GamepadLookSample look = _look.Advance(new Vector2(State.RightX,
                    State.RightY), (float)FrameTiming.StepSeconds);
                _gyroAllowedForSimulation = GyroAllowed(zoomed);
                if (!_gyroAllowedForSimulation) GamepadGyro.SuppressOutput();
                Vector2 gyro = _gyroAllowedForSimulation
                    ? GamepadGyro.Sample(NowSeconds()) : Vector2.Zero;
                AimAngularVelocity = look.AngularVelocity + gyro;
                AimDeltaX = AimAngularVelocity.X * (float)FrameTiming.StepSeconds;
                AimDeltaY = AimAngularVelocity.Y * (float)FrameTiming.StepSeconds;
                Vector2 rawLook = new(State.RightX, State.RightY);
                LookDeviceKind owner = gyro != Vector2.Zero
                    ? LookDeviceKind.GamepadGyro : LookDeviceKind.GamepadStick;
                LookDeviceKind contributors = (look.AngularVelocity != Vector2.Zero
                    ? LookDeviceKind.GamepadStick : LookDeviceKind.None)
                    | (gyro != Vector2.Zero ? LookDeviceKind.GamepadGyro : LookDeviceKind.None);
                Vector2 raw = owner == LookDeviceKind.GamepadGyro
                    ? gyro.Normalized() : rawLook;
                float magnitude = owner == LookDeviceKind.GamepadGyro
                    ? gyro.Length : look.Magnitude;
                LocalLookFrame frame = new LocalLookFrame(owner,
                    new Vector2(AimDeltaX, AimDeltaY), raw, magnitude)
                    .WithContributors(contributors);
                // Keep render prediction current even when a precision source
                // owns this frame or the stick has returned to drift. This is
                // state publication only; ownership still changes through the
                // fixed-step coordinator submission below.
                LookCoordinator.SetStatefulVelocity(AimAngularVelocity);
                LookCoordinator.SubmitStateful(frame, AimAngularVelocity,
                    InputSettings.GamepadLookDeadZone);
            }
            // The scene host performs the one fixed-step extraction after all
            // platform contributors for this tick have been submitted.
        }

        /// <summary>
        /// Evaluate raw controller state at native/render cadence without
        /// advancing boost timers or button/movement hysteresis.
        /// </summary>
        public static void SampleNativeFrame(long timingGeneration = 0)
        {
            if (!Active)
            {
                ResetControllerState();
                return;
            }
            if (_timingGeneration != timingGeneration)
            {
                ResetTimingState(timingGeneration);
            }
            if ((EffectiveButtons & PadBindings.Get(PadAction.WeaponWheel)) != 0)
            {
                // The radial owns the right stick for selection. Clear the
                // render-side velocity too, otherwise a stale preview could
                // turn the camera between fixed steps while the wheel is open.
                _look.Reset();
                AimAngularVelocity = Vector2.Zero;
                LookCoordinator.SetStatefulVelocity(Vector2.Zero);
                return;
            }
            ConfigureLookProcessor();
            GamepadLookSample sample = _look.Evaluate(new Vector2(State.RightX,
                State.RightY));
            AimAngularVelocity = sample.AngularVelocity
                + (_gyroAllowedForSimulation
                    ? GamepadGyro.Sample(NowSeconds()) : Vector2.Zero);
            LookCoordinator.SetStatefulVelocity(AimAngularVelocity);
        }

        public static void Reset()
        {
            ResetProcessors();
            _previous = GamepadButtons.None;
            _effective = GamepadButtons.None;
            _pressed = GamepadButtons.None;
            _released = GamepadButtons.None;
            AimDeltaX = AimDeltaY = 0;
            AimAngularVelocity = Vector2.Zero;
            GamepadGyro.Reset();
            LookCoordinator.Reset();
            _timingGeneration = long.MinValue;
        }

        /// <summary>
        /// Reset time-dependent controller processing without forgetting the
        /// physical button state. A render stall or dropped catch-up step is
        /// not a button release: clearing <c>_previous</c> here turned every
        /// held bumper, d-pad direction, or quick-swap button into another
        /// press on the next fixed tick.
        /// </summary>
        private static void ResetTimingState(long timingGeneration)
        {
            _movement.Reset();
            _look.Reset();
            _lookConfigured = false;
            _pressed = GamepadButtons.None;
            _released = GamepadButtons.None;
            AimDeltaX = AimDeltaY = 0;
            AimAngularVelocity = Vector2.Zero;
            _gyroAllowedForSimulation = false;
            GamepadGyro.Reset();
            // A frame-timing discontinuity invalidates controller filters,
            // not an active relative-input capture. Resetting the whole
            // coordinator here discarded mouse/touch/stylus movement already
            // received for this frame and made a held stylus epoch stale
            // until the player lifted it.
            LookCoordinator.ResetControllerState();
            _timingGeneration = timingGeneration;
        }

        public static void ResetLook()
        {
            _look.Reset();
            AimDeltaX = AimDeltaY = 0;
            AimAngularVelocity = Vector2.Zero;
            GamepadGyro.Reset();
            LookCoordinator.Reset();
        }

        /// <summary>
        /// Reset only controller-owned state. The shared look coordinator may
        /// still contain a mouse/touch/stylus event that must survive a
        /// controller disconnect and be consumed by the fixed simulation.
        /// </summary>
        public static void ResetControllerState()
        {
            ResetProcessors();
            _previous = GamepadButtons.None;
            _effective = GamepadButtons.None;
            _pressed = GamepadButtons.None;
            _released = GamepadButtons.None;
            AimDeltaX = AimDeltaY = 0;
            AimAngularVelocity = Vector2.Zero;
            _gyroAllowedForSimulation = false;
            GamepadGyro.ResetDevice();
            LookCoordinator.ResetControllerState();
            _timingGeneration = long.MinValue;
        }

        /// <summary>
        /// Hide all controller gameplay state while a replay-owned surface is
        /// active. The physical state remains readable by that surface so a
        /// rising edge can be consumed without leaking a held action into the
        /// live scene.
        /// </summary>
        internal static void BeginGameplayInputQuarantine()
        {
            _gameplayInputQuarantine = true;
            _postQuarantineUntilRelease = false;
            Reset();
        }

        /// <summary>
        /// Keep held buttons neutral until their physical release. This is
        /// deliberately separate from <see cref="Reset"/> so scene teardown
        /// cannot reintroduce a stale button edge.
        /// </summary>
        internal static void EndGameplayInputQuarantine()
        {
            if (!_gameplayInputQuarantine) return;
            _gameplayInputQuarantine = false;
            _postQuarantineUntilRelease = PhysicalControllerInputActive();
            Reset();
        }

        internal static bool InputQuarantined
            => _gameplayInputQuarantine || _postQuarantineUntilRelease;

        private static GamepadButtons GameplayButtons()
            => InputQuarantined ? GamepadButtons.None : State.Buttons;

        private static bool PhysicalControllerInputActive()
        {
            if (State.Buttons != GamepadButtons.None) return true;
            if (!float.IsFinite(State.LeftX) || !float.IsFinite(State.LeftY)
                || !float.IsFinite(State.RightX) || !float.IsFinite(State.RightY)
                || !float.IsFinite(State.LeftTrigger)
                || !float.IsFinite(State.RightTrigger))
            {
                // A malformed native sample is not a release. Keep the
                // post-handoff quarantine in place until a finite neutral
                // sample arrives rather than leaking undefined input.
                return true;
            }
            float moveRelease = InputSettings.GamepadMoveReleaseThreshold;
            float lookRelease = InputSettings.GamepadLookDeadZone;
            float triggerRelease = InputSettings.GamepadTriggerReleaseThreshold;
            return State.LeftX * State.LeftX + State.LeftY * State.LeftY
                    >= moveRelease * moveRelease
                || State.RightX * State.RightX + State.RightY * State.RightY
                    > lookRelease * lookRelease
                || State.LeftTrigger > triggerRelease
                || State.RightTrigger > triggerRelease;
        }

        /// <summary>
        /// True once for each press of Start, which opens and closes the pause
        /// menu.
        ///
        /// Taken rather than read, and not a keybind like everything else,
        /// because the pause menu is not a thing the *player* does: it is a
        /// window the host platform opens, so it has to be acted on by
        /// whatever owns a window rather than by the entity. The desktop
        /// consumes this after the frame updates and Android in its own loop.
        /// </summary>
        public static bool TakeMenuPress()
        {
            GamepadButtons menu = PadBindings.Get(PadAction.Menu);
            if (menu == GamepadButtons.None || (_pressed & menu) == 0)
            {
                return false;
            }
            // Cleared so a frame that is drawn twice, or a caller that asks
            // twice, cannot open the menu and close it again in one press.
            _pressed &= ~menu;
            return true;
        }

        /// <summary>
        /// Add the pad to what the keyboard and mouse already said, for the
        /// player this machine is driving.
        ///
        /// After <c>PlayerEntity.ProcessInput</c>, never instead of it: the
        /// binds are filled from the keyboard first and this only ever turns
        /// things on, so a player with a hand on each works, and a pad sitting
        /// on a desk contributes nothing.
        /// </summary>
        public static void Apply(PlayerEntity? player)
        {
            if (player == null)
            {
                return;
            }
            if (!Active || player.IsBot
                || !player.LoadFlags.TestFlag(LoadFlags.Active))
            {
                player.ClearAnalogMovement();
                return;
            }
            ClientPlayerBindings controls = player.GetPresentation().Bindings;
            // Capture the non-controller axis before adding the legacy
            // digital binds. This lets the authoritative movement path retain
            // keyboard/touch intent while using the radial sample for a pad's
            // own contribution.
            Vector2 digitalBeforeAnalog = new(
                controls.MoveRight.IsDown ? 1 : controls.MoveLeft.IsDown ? -1 : 0,
                controls.MoveUp.IsDown ? 1 : controls.MoveDown.IsDown ? -1 : 0);
            Vector2 digitalRollBeforeAnalog = new(
                controls.RollRight.IsDown ? 1 : controls.RolltLeft.IsDown ? -1 : 0,
                controls.RollUp.IsDown ? 1 : controls.RollDown.IsDown ? -1 : 0);
            InputButtons digitalMovementButtons = InputButtons.None;
            if (controls.MoveLeft.IsDown) digitalMovementButtons |= InputButtons.Left;
            if (controls.MoveRight.IsDown) digitalMovementButtons |= InputButtons.Right;
            if (controls.MoveUp.IsDown) digitalMovementButtons |= InputButtons.Forward;
            if (controls.MoveDown.IsDown) digitalMovementButtons |= InputButtons.Back;
            InputButtons digitalMovementPressed = InputButtons.None;
            if (controls.MoveLeft.IsPressed) digitalMovementPressed |= InputButtons.Left;
            if (controls.MoveRight.IsPressed) digitalMovementPressed |= InputButtons.Right;
            if (controls.MoveUp.IsPressed) digitalMovementPressed |= InputButtons.Forward;
            if (controls.MoveDown.IsPressed) digitalMovementPressed |= InputButtons.Back;
            InputButtons digitalRollButtons = InputButtons.None;
            if (controls.RolltLeft.IsDown) digitalRollButtons |= InputButtons.RollLeft;
            if (controls.RollRight.IsDown) digitalRollButtons |= InputButtons.RollRight;
            if (controls.RollUp.IsDown) digitalRollButtons |= InputButtons.RollForward;
            if (controls.RollDown.IsDown) digitalRollButtons |= InputButtons.RollBack;
            InputButtons digitalRollPressed = InputButtons.None;
            if (controls.RolltLeft.IsPressed) digitalRollPressed |= InputButtons.RollLeft;
            if (controls.RollRight.IsPressed) digitalRollPressed |= InputButtons.RollRight;
            if (controls.RollUp.IsPressed) digitalRollPressed |= InputButtons.RollForward;
            if (controls.RollDown.IsPressed) digitalRollPressed |= InputButtons.RollBack;
            GamepadMovementSample movement = _movement.Processed;
            if (movement.Active)
            {
                player.SetAnalogMovement(movement.Vector, digitalBeforeAnalog,
                    digitalRollBeforeAnalog, digitalMovementButtons,
                    digitalRollButtons, digitalMovementPressed,
                    digitalRollPressed);
            }
            else
            {
                player.ClearAnalogMovement();
            }
            // Both sets, as the touch controls do: walking reads Move and the
            // morph ball reads Roll, and a player who has bound them to
            // different keys expects the stick to drive whichever form they
            // are in.
            Hold(controls.MoveUp, (movement.Direction & GamepadMovementDirection.Up) != 0);
            Hold(controls.RollUp, (movement.Direction & GamepadMovementDirection.Up) != 0);
            Hold(controls.MoveDown, (movement.Direction & GamepadMovementDirection.Down) != 0);
            Hold(controls.RollDown, (movement.Direction & GamepadMovementDirection.Down) != 0);
            Hold(controls.MoveLeft, (movement.Direction & GamepadMovementDirection.Left) != 0);
            Hold(controls.RolltLeft, (movement.Direction & GamepadMovementDirection.Left) != 0);
            Hold(controls.MoveRight, (movement.Direction & GamepadMovementDirection.Right) != 0);
            Hold(controls.RollRight, (movement.Direction & GamepadMovementDirection.Right) != 0);

            // Which button each of these is on is the player's business now:
            // see PadBindings, which starts as the table that used to be
            // written out here. Two of them drive two binds apiece, which is
            // why PadAction has fewer entries and PlayerControls has more --
            // FIRE is both attacks, for the reason the touch button is (the DS
            // had one attack button, and the game's own defaults still bind
            // the gun and the alt form's attack to the same one), and JUMP is
            // also the ball's boost.
            GamepadButtons shoot = PadBindings.Get(PadAction.Shoot);
            Hold(controls.Shoot, shoot);
            Hold(controls.AltAttack, shoot);
            Hold(controls.Zoom, PadBindings.Get(PadAction.Zoom));
            GamepadButtons jump = PadBindings.Get(PadAction.Jump);
            Hold(controls.Jump, jump);
            Hold(controls.Boost, jump);
            if (!Down(PadBindings.Get(PadAction.WeaponWheel)) && !player.GetPresentation().WeaponRadial.Open)
                Hold(controls.Morph, PadBindings.Get(PadAction.Morph));
            Hold(controls.QuickSwap, PadBindings.Get(PadAction.QuickSwap));
            // The client radial owns right-stick selection; it never moves the mouse cursor.
            Hold(controls.Pause, PadBindings.Get(PadAction.Scoreboard));

            Hold(controls.NextWeapon, PadBindings.Get(PadAction.NextWeapon));
            Hold(controls.PrevWeapon, PadBindings.Get(PadAction.PrevWeapon));
            Hold(controls.Missile, PadBindings.Get(PadAction.Missile));
            Hold(controls.PowerBeam, PadBindings.Get(PadAction.PowerBeam));

            // And say that somebody is playing. The binds above are ored on
            // after the pass that answers that question for the keyboard, so
            // without this a player holding nothing but a pad reads as idle --
            // which lowers their gun off the screen and leaves them unable to
            // fire. See PlayerEntity.ModNoteInput.
            if (InUse)
            {
                player.ModNoteInput();
            }
        }

        private static void Hold(Keybind bind, GamepadButtons buttons)
        {
            // An unbound action is None, and None & anything is None, so this
            // needs no case of its own: it simply never holds anything down.
            Hold(bind, (_effective & buttons) != 0, (_pressed & buttons) != 0);
        }

        private static void Hold(Keybind bind, bool down)
        {
            // No edge of its own: a stick direction is a state, and the things
            // that read IsPressed are all buttons.
            Hold(bind, down, pressed: false);
        }

        /// <summary>
        /// Turn a bind on, never off.
        ///
        /// The or is the whole point: <c>ProcessInput</c> has already written
        /// what the keyboard and mouse are doing, and a pad that assigned
        /// instead of adding would release a key somebody is holding every
        /// frame it did not have that button pressed.
        /// </summary>
        private static void Hold(Keybind bind, bool down, bool pressed)
        {
            if (down)
            {
                bind.IsDown = true;
            }
            if (pressed)
            {
                bind.IsPressed = true;
            }
        }

        private static bool Down(GamepadButtons buttons)
            => (_effective & buttons) != 0;

        internal static bool GyroAllowed(bool zoomed)
        {
            return InputSettings.GamepadGyroMode switch
            {
                GamepadGyroMode.Always => true,
                GamepadGyroMode.ZoomOnly => zoomed,
                GamepadGyroMode.HoldButton => Down(
                    InputSettings.GamepadGyroActivation switch
                    {
                        GamepadGyroActivation.LeftBumper => GamepadButtons.LeftBumper,
                        GamepadGyroActivation.RightThumb => GamepadButtons.RightThumb,
                        _ => GamepadButtons.LeftTrigger
                    }),
                _ => false
            };
        }

        private static double NowSeconds()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private static void ResetProcessors()
        {
            _movement.Reset();
            _triggers.Reset();
            _look.Reset();
            _lookConfigured = false;
        }

        private static GamepadLookProcessor CreateLookProcessor()
            => new(InputSettings.GamepadLookDeadZone, InputSettings.GamepadOuterDeadZone,
                InputSettings.GamepadLookExponent, InputSettings.GamepadYawRate,
                InputSettings.GamepadPitchRate, InputSettings.GamepadOuterBoostStart,
                InputSettings.GamepadOuterYawBoost, InputSettings.GamepadOuterPitchBoost,
                InputSettings.GamepadBoostDelaySeconds, InputSettings.GamepadBoostRampSeconds,
                InputSettings.GamepadOuterBoostEnabled,
                InputSettings.GamepadHorizontalSensitivity,
                InputSettings.GamepadVerticalSensitivity,
                InputSettings.GamepadInvertY, InputSettings.GamepadZoomMultiplier);

        private static void ConfigureLookProcessor()
        {
            if (!_lookConfigured)
            {
                _look = CreateLookProcessor();
                _lookConfigured = true;
                return;
            }
            _look.Configure(InputSettings.GamepadLookDeadZone,
                InputSettings.GamepadOuterDeadZone, InputSettings.GamepadLookExponent,
                InputSettings.GamepadYawRate, InputSettings.GamepadPitchRate,
                InputSettings.GamepadOuterBoostStart, InputSettings.GamepadOuterYawBoost,
                InputSettings.GamepadOuterPitchBoost, InputSettings.GamepadBoostDelaySeconds,
                InputSettings.GamepadBoostRampSeconds, InputSettings.GamepadOuterBoostEnabled,
                InputSettings.GamepadHorizontalSensitivity,
                InputSettings.GamepadVerticalSensitivity, InputSettings.GamepadInvertY,
                InputSettings.GamepadZoomMultiplier);
        }
    }
}
