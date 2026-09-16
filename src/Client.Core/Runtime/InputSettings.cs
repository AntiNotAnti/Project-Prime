using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MphRead.Entities;
using MphRead.Mods.Input;

namespace MphRead.Mods
{
    /// <summary>
    /// Keys and mouse feel, kept where a settings screen can edit them and a
    /// player can keep them.
    ///
    /// Upstream builds a fresh <see cref="ClientPlayerBindings"/> per player from
    /// <see cref="ClientPlayerBindings.GetDefault"/> and never reads a file, so
    /// rebinding anything lasted exactly as long as the process. This holds
    /// one canonical set, applies it to every set upstream creates, and writes
    /// it to controls.txt beside the executable -- deliberately its own file
    /// rather than a corner of MenuSettings, for the same reason launcher.txt
    /// is.
    ///
    /// Mouse sensitivity was a literal in the aim path with an "itodo" beside
    /// it. One multiplier lives here instead, where 1.0 is exactly the feel
    /// that literal gave.
    /// </summary>
    public static class InputSettings
    {
        /// <summary>
        /// controls.txt, beside the executable -- except where the program
        /// does not own that folder.
        ///
        /// It follows <see cref="Launcher.LauncherPrefs.Directory"/> rather
        /// than <c>AppContext.BaseDirectory</c> because an Android package's
        /// own directory is read-only: every write went to a path the app is
        /// not allowed to create, the exception was swallowed (as it has to
        /// be, see <see cref="Save"/>), and every rebind, every sensitivity
        /// and every pad binding was lost the moment the game was closed.
        /// The head points that one property at the app's data directory
        /// before anything reads, so this lands where the rest of a player's
        /// settings already do.
        /// </summary>
        private static string Path
            => System.IO.Path.Combine(Launcher.LauncherPrefs.Directory, "controls.txt");

        /// <summary>
        /// The shipped mouse multiplier. Keep this separate from the UI range:
        /// controls.txt may preserve values above the launcher slider's maximum.
        /// </summary>
        public const float DefaultMouseSensitivity = 1.0f;

        /// <summary>Smallest finite value accepted by the controls file.</summary>
        public const float MinimumMouseSensitivity = 0.01f;

        /// <summary>Largest finite value accepted by the controls file.</summary>
        public const float MaximumMouseSensitivity = 10.0f;

        /// <summary>Largest value exposed by the launcher settings slider.</summary>
        public const float UiMaximumMouseSensitivity = 3.0f;

        /// <summary>Launcher slider precision, in sensitivity units.</summary>
        public const float MouseSensitivityStep = 0.01f;

        /// <summary>
        /// Multiplier on mouse movement. Values are normalized at the boundary
        /// so direct edits, legacy files, and serialization share one contract.
        /// </summary>
        public static float MouseSensitivity
        {
            get => _mouseSensitivity;
            set => _mouseSensitivity = NormalizeMouseSensitivity(value);
        }

        private static float _mouseSensitivity = DefaultMouseSensitivity;

        /// <summary>
        /// Normalize an input to the finite persistence range. Non-finite
        /// values use the shipped default rather than entering the aim path or
        /// being written to controls.txt.
        /// </summary>
        public static float NormalizeMouseSensitivity(float value)
            => float.IsFinite(value)
                ? Math.Clamp(value, MinimumMouseSensitivity, MaximumMouseSensitivity)
                : DefaultMouseSensitivity;

        public static float DynamicCrosshairTravelDegrees
        {
            get => _dynamicCrosshairTravelDegrees;
            set => _dynamicCrosshairTravelDegrees
                = DynamicCrosshairTuning.TravelDegrees(value);
        }

        private static float _dynamicCrosshairTravelDegrees
            = DynamicCrosshairTuning.DefaultTravelDegrees;

        public static float DynamicCrosshairSensitivity
        {
            get => _dynamicCrosshairSensitivity;
            set => _dynamicCrosshairSensitivity
                = DynamicCrosshairTuning.MovementSensitivity(value);
        }

        private static float _dynamicCrosshairSensitivity
            = DynamicCrosshairTuning.DefaultMovementSensitivity;

        public static float DynamicCrosshairTurnSpeed
        {
            get => _dynamicCrosshairTurnSpeed;
            set => _dynamicCrosshairTurnSpeed
                = DynamicCrosshairTuning.TurnSpeed(value);
        }

        private static float _dynamicCrosshairTurnSpeed
            = DynamicCrosshairTuning.DefaultTurnSpeed;

        public static bool InvertMouseY { get; set; }
        public static bool InvertMouseX { get; set; }

        /// <summary>
        /// Whether the wheel cycles every weapon or only the affinity slots.
        ///
        /// On by default, which upstream's constant was not, and the
        /// difference is not a nicety: the cycling code runs only when this is
        /// set *or* the equipped weapon is neither the Power Beam nor the
        /// Missile, so with it off the wheel was dead in the hand every player
        /// spawns with -- "the scroll wheel does not change weapons", exactly.
        /// </summary>
        public static bool ScrollAllWeapons { get; set; } = true;

        /// <summary>
        /// Direction-only Morph Ball boost gesture switches. They are kept
        /// independent so desktop, controller and touch users can choose the
        /// source they trust without changing the shared thresholds.
        /// </summary>
        public static bool MorphBallMouseFlickBoost { get; set; } = true;
        public static bool MorphBallStickFlickBoost { get; set; } = true;
        public static bool MorphBallSwipeBoost { get; set; } = true;

        /// <summary>
        /// The key that opens the chat prompt. T, which is where every
        /// shooter since Quake has put it.
        ///
        /// Not a <see cref="Keybind"/> on <see cref="ClientPlayerBindings"/> like
        /// everything else here, and deliberately so: that class is upstream's
        /// and its every member is read by <c>ProcessAllInput</c> as something
        /// the *player* does in the world. Chat is the opposite -- it takes
        /// the keyboard away from the player -- so it is handled by the window
        /// before the game sees the key at all, and a binding the game never
        /// reads has no business in the game's binding set.
        /// </summary>
        public static PrimeKey ChatKey { get; set; } = PrimeKey.T;

        /// <summary>
        /// How far a stick must move before it counts, 0 to 0.9.
        ///
        /// Applied radially rather than per axis -- see
        /// <see cref="Input.GamepadInput"/> for why that is not the same
        /// thing. 0.2 clears the resting drift of a worn stick without
        /// swallowing a deliberate nudge.
        /// </summary>
        public static float GamepadDeadZone
        {
            get => GamepadMoveDeadZone;
            set
            {
                GamepadMoveDeadZone = value;
                GamepadLookDeadZone = value;
            }
        }

        /// <summary>
        /// Legacy shared sensitivity alias. Modern callers use independent
        /// horizontal/vertical multipliers over the configured yaw/pitch rates.
        /// </summary>
        public static float GamepadLookSensitivity
        {
            get => GamepadHorizontalSensitivity;
            set
            {
                GamepadHorizontalSensitivity = value;
                GamepadVerticalSensitivity = value;
            }
        }

        /// <summary>
        /// Invert the right stick's vertical aim. Its own setting rather than
        /// sharing the mouse's, because a great many people invert one and not
        /// the other, and there is no third thing they would rather set.
        /// </summary>
        public static bool GamepadInvertY { get; set; }

        // Controller tuning is deliberately split by purpose. The old
        // GamepadDeadZone/GamepadLookSensitivity properties below remain as
        // compatibility aliases for callers and legacy controls files.
        public static float GamepadMoveDeadZone
        {
            get => _gamepadMoveDeadZone;
            set => _gamepadMoveDeadZone = Clamp(value, 0.15f, 0, 0.9f);
        }

        private static float _gamepadMoveDeadZone = 0.15f;

        public static float GamepadLookDeadZone
        {
            get => _gamepadLookDeadZone;
            set => _gamepadLookDeadZone = Clamp(value, 0.10f, 0, 0.9f);
        }

        private static float _gamepadLookDeadZone = 0.10f;

        public static float GamepadOuterDeadZone
        {
            get => _gamepadOuterDeadZone;
            set => _gamepadOuterDeadZone = Clamp(value, 0.02f, 0, 0.5f);
        }

        private static float _gamepadOuterDeadZone = 0.02f;

        public static float GamepadMoveActivateThreshold
        {
            get => _gamepadMoveActivateThreshold;
            set
            {
                _gamepadMoveActivateThreshold = Clamp(value, 0.25f, 0, 1);
                if (_gamepadMoveReleaseThreshold > _gamepadMoveActivateThreshold)
                {
                    _gamepadMoveReleaseThreshold = _gamepadMoveActivateThreshold;
                }
            }
        }

        private static float _gamepadMoveActivateThreshold = 0.25f;

        public static float GamepadMoveReleaseThreshold
        {
            get => _gamepadMoveReleaseThreshold;
            set => _gamepadMoveReleaseThreshold = Math.Min(
                Clamp(value, 0.18f, 0, 1), _gamepadMoveActivateThreshold);
        }

        private static float _gamepadMoveReleaseThreshold = 0.18f;

        public static float GamepadLookExponent
        {
            get => _gamepadLookExponent;
            set => _gamepadLookExponent = Clamp(value, 1.60f, 0.05f, 8);
        }

        private static float _gamepadLookExponent = 1.60f;

        /// <summary>
        /// Minimum non-zero look output after the radial deadzone. This
        /// compensates for games/controllers whose first usable samples would
        /// otherwise be too small to move the camera reliably.
        /// </summary>
        public static float GamepadLookAntiDeadzone
        {
            get => _gamepadLookAntiDeadzone;
            set => _gamepadLookAntiDeadzone = Clamp(value,
                Input.GamepadLookProcessor.DefaultAntiDeadzone, 0, .5f);
        }
        private static float _gamepadLookAntiDeadzone
            = Input.GamepadLookProcessor.DefaultAntiDeadzone;

        /// <summary>
        /// Maximum low-speed controller-look filter time. Filtering fades out
        /// with stick speed so full turns remain responsive.
        /// </summary>
        public static float GamepadLookSmoothingSeconds
        {
            get => _gamepadLookSmoothingSeconds;
            set => _gamepadLookSmoothingSeconds = Clamp(value,
                Input.GamepadLookProcessor.DefaultSmoothingSeconds, 0, .25f);
        }
        private static float _gamepadLookSmoothingSeconds
            = Input.GamepadLookProcessor.DefaultSmoothingSeconds;

        public static float GamepadYawRate
        {
            get => _gamepadYawRate;
            set => _gamepadYawRate = Clamp(value, 300, 0, 2000);
        }

        private static float _gamepadYawRate = 300;

        public static float GamepadPitchRate
        {
            get => _gamepadPitchRate;
            set => _gamepadPitchRate = Clamp(value, 240, 0, 2000);
        }

        private static float _gamepadPitchRate = 240;

        public static float GamepadOuterBoostStart
        {
            get => _gamepadOuterBoostStart;
            set => _gamepadOuterBoostStart = Clamp(value, 0.95f, 0, 1);
        }

        private static float _gamepadOuterBoostStart = 0.95f;

        public static float GamepadOuterYawBoost
        {
            get => _gamepadOuterYawBoost;
            set => _gamepadOuterYawBoost = Clamp(value, 150, 0, 2000);
        }

        private static float _gamepadOuterYawBoost = 150;

        public static float GamepadOuterPitchBoost
        {
            get => _gamepadOuterPitchBoost;
            set => _gamepadOuterPitchBoost = Clamp(value, 80, 0, 2000);
        }

        private static float _gamepadOuterPitchBoost = 80;

        public static float GamepadBoostDelaySeconds
        {
            get => _gamepadBoostDelaySeconds;
            set => _gamepadBoostDelaySeconds = Clamp(value, 0.18f, 0, 10);
        }

        private static float _gamepadBoostDelaySeconds = 0.18f;

        public static float GamepadBoostRampSeconds
        {
            get => _gamepadBoostRampSeconds;
            set => _gamepadBoostRampSeconds = Clamp(value, 0.12f, 0.001f, 10);
        }

        private static float _gamepadBoostRampSeconds = 0.12f;

        public static bool GamepadOuterBoostEnabled { get; set; } = true;

        public static float GamepadTriggerPressThreshold
        {
            get => _gamepadTriggerPressThreshold;
            set
            {
                _gamepadTriggerPressThreshold = Clamp(value, 0.20f, 0, 1);
                if (_gamepadTriggerReleaseThreshold > _gamepadTriggerPressThreshold)
                {
                    _gamepadTriggerReleaseThreshold = _gamepadTriggerPressThreshold;
                }
            }
        }

        private static float _gamepadTriggerPressThreshold = 0.20f;

        public static float GamepadTriggerReleaseThreshold
        {
            get => _gamepadTriggerReleaseThreshold;
            set => _gamepadTriggerReleaseThreshold = Math.Min(
                Clamp(value, 0.12f, 0, 1), _gamepadTriggerPressThreshold);
        }

        private static float _gamepadTriggerReleaseThreshold = 0.12f;

        public static float GamepadHorizontalSensitivity
        {
            get => _gamepadHorizontalSensitivity;
            set => _gamepadHorizontalSensitivity = Clamp(value, 1, 0.01f, 10);
        }

        private static float _gamepadHorizontalSensitivity = 1;

        public static float GamepadVerticalSensitivity
        {
            get => _gamepadVerticalSensitivity;
            set => _gamepadVerticalSensitivity = Clamp(value, 1, 0.01f, 10);
        }

        private static float _gamepadVerticalSensitivity = 1;

        // More descriptive aliases for settings/UI callers.
        public static float GamepadLookSensitivityHorizontal
        {
            get => GamepadHorizontalSensitivity;
            set => GamepadHorizontalSensitivity = value;
        }

        public static float GamepadLookSensitivityVertical
        {
            get => GamepadVerticalSensitivity;
            set => GamepadVerticalSensitivity = value;
        }

        public static float GamepadZoomMultiplier
        {
            get => (GamepadZoomHorizontalMultiplier
                + GamepadZoomVerticalMultiplier) * .5f;
            set
            {
                GamepadZoomHorizontalMultiplier = value;
                GamepadZoomVerticalMultiplier = value;
            }
        }

        public static float GamepadZoomHorizontalMultiplier
        {
            get => _gamepadZoomHorizontalMultiplier;
            set => _gamepadZoomHorizontalMultiplier = Clamp(value, 1, .01f, 10);
        }
        private static float _gamepadZoomHorizontalMultiplier = 1;

        public static float GamepadZoomVerticalMultiplier
        {
            get => _gamepadZoomVerticalMultiplier;
            set => _gamepadZoomVerticalMultiplier = Clamp(value, 1, .01f, 10);
        }
        private static float _gamepadZoomVerticalMultiplier = 1;

        public static bool GamepadAutoCalibrationEnabled { get; set; } = true;

        public static Input.GamepadStickAimMode GamepadStickAimMode
        {
            get => _gamepadStickAimMode;
            set => _gamepadStickAimMode = Enum.IsDefined(value)
                ? value : Input.GamepadStickAimMode.Traditional;
        }
        private static Input.GamepadStickAimMode _gamepadStickAimMode
            = Input.GamepadStickAimMode.Traditional;

        // Aim assist remains deliberately absent from every player-facing
        // settings surface. These persisted values are an internal tuning and
        // accessibility seam for controlled tests/builds only.
        internal static bool GamepadAimAssistEnabled { get; set; } = true;
        internal static float GamepadAimAssistStrength
        {
            get => _gamepadAimAssistStrength;
            set => _gamepadAimAssistStrength = Clamp(value, 1, 0, 1);
        }
        private static float _gamepadAimAssistStrength = 1;

        public static Input.GamepadResponseCurvePreset GamepadResponseCurve
        {
            get => _gamepadResponseCurve;
            set
            {
                _gamepadResponseCurve = Enum.IsDefined(value)
                    ? value : Input.GamepadResponseCurvePreset.Balanced;
                if (_gamepadResponseCurve != Input.GamepadResponseCurvePreset.Custom)
                {
                    _gamepadLookExponent = Input.GamepadLookProfiles.ResponseExponent(
                        _gamepadResponseCurve, _gamepadLookExponent);
                }
            }
        }
        private static Input.GamepadResponseCurvePreset _gamepadResponseCurve
            = Input.GamepadResponseCurvePreset.Balanced;

        public static Input.GamepadTurnAccelerationPreset GamepadTurnAcceleration
        {
            get => _gamepadTurnAcceleration;
            set
            {
                _gamepadTurnAcceleration = Enum.IsDefined(value)
                    ? value : Input.GamepadTurnAccelerationPreset.Standard;
                Input.GamepadTurnAccelerationProfile profile
                    = Input.GamepadLookProfiles.TurnAcceleration(_gamepadTurnAcceleration);
                GamepadOuterBoostEnabled = profile.Enabled;
                GamepadOuterBoostStart = profile.OuterBoostStart;
                GamepadOuterYawBoost = profile.OuterYawBoost;
                GamepadOuterPitchBoost = profile.OuterPitchBoost;
                GamepadBoostDelaySeconds = profile.BoostDelaySeconds;
                GamepadBoostRampSeconds = profile.BoostRampSeconds;
            }
        }
        private static Input.GamepadTurnAccelerationPreset _gamepadTurnAcceleration
            = Input.GamepadTurnAccelerationPreset.Standard;

        /// <summary>SDL gamepad gyro is opt-in; unsupported hosts leave it inert.</summary>
        public static Input.GamepadGyroMode GamepadGyroMode
        {
            get => _gamepadGyroMode;
            set
            {
                Input.GamepadGyroMode next = Enum.IsDefined(value)
                    ? value : Input.GamepadGyroMode.Off;
                bool wasEnabled = _gamepadGyroMode != Input.GamepadGyroMode.Off;
                _gamepadGyroMode = next;
                if (!wasEnabled && next != Input.GamepadGyroMode.Off)
                    Input.GamepadGyro.Recalibrate();
                else if (next == Input.GamepadGyroMode.Off)
                    Input.GamepadGyro.SuppressOutput();
            }
        }
        private static Input.GamepadGyroMode _gamepadGyroMode = Input.GamepadGyroMode.Off;
        public static bool GamepadGyroEnabled
        {
            get => GamepadGyroMode != Input.GamepadGyroMode.Off;
            set => GamepadGyroMode = value
                ? GamepadGyroMode == Input.GamepadGyroMode.Off
                    ? Input.GamepadGyroMode.Always : GamepadGyroMode
                : Input.GamepadGyroMode.Off;
        }
        public static Input.GamepadGyroActivation GamepadGyroActivation
        {
            get => _gamepadGyroActivation;
            set => _gamepadGyroActivation = Enum.IsDefined(value)
                ? value : Input.GamepadGyroActivation.LeftTrigger;
        }
        private static Input.GamepadGyroActivation _gamepadGyroActivation
            = Input.GamepadGyroActivation.LeftTrigger;
        public static float GamepadGyroSensitivity
        {
            get => _gamepadGyroSensitivity;
            set => _gamepadGyroSensitivity = Clamp(value, 1, 0.01f, 10);
        }
        private static float _gamepadGyroSensitivity = 1;
        public static bool GamepadGyroInvertX { get; set; }
        public static bool GamepadGyroInvertY { get; set; }
        public static bool GamepadHapticsEnabled { get; set; } = true;
        public static float GamepadHapticsStrength
        {
            get => _gamepadHapticsStrength;
            set => _gamepadHapticsStrength = Clamp(value, 1, 0, 1);
        }
        private static float _gamepadHapticsStrength = 1;
        public static bool InputBalanceTelemetryEnabled { get; set; }

        public static bool StylusAimingEnabled { get; set; } = true;
        public static float StylusSensitivity
        {
            get => _stylusSensitivity;
            set => _stylusSensitivity = Clamp(value, 1, 0.01f, 10);
        }
        private static float _stylusSensitivity = 1;
        public static bool StylusInvertY { get; set; }
        public static Input.StylusAction StylusPrimaryAction { get; set; }
            = Input.StylusAction.Fire;
        public static Input.StylusAction StylusSecondaryAction { get; set; }
            = Input.StylusAction.Zoom;
        public static bool StylusClassicGestures { get; set; } = true;
        public static bool StylusDoubleTapJump { get; set; } = true;
        public static bool StylusFlickBoost { get; set; } = true;
        public static bool StylusPressureToFire { get; set; }
        public static float StylusPressureThreshold
        {
            get => _stylusPressureThreshold;
            set => _stylusPressureThreshold = Clamp(value, 0.35f, 0, 1);
        }
        private static float _stylusPressureThreshold = 0.35f;

        /// <summary>
        /// Optional client-only DS lower-screen weapon selector. Off keeps the
        /// existing HUD and native weapon-menu paths unchanged.
        /// </summary>
        public static NativeBottomScreenMode BottomScreenMode { get; set; }
            = NativeBottomScreenMode.Off;
        /// <summary>
        /// Opt-in retail-style DS drag-to-aim ownership for the Classic DS
        /// panel. Free preserves the existing global stylus behavior.
        /// </summary>
        public static NativeBottomScreenAimMode BottomScreenAimMode
        {
            get => _bottomScreenAimMode;
            set => _bottomScreenAimMode = Enum.IsDefined(value)
                ? value : NativeBottomScreenAimMode.Free;
        }
        private static NativeBottomScreenAimMode _bottomScreenAimMode
            = NativeBottomScreenAimMode.Free;
        /// <summary>
        /// Desktop HudOverlay activation semantics. Toggle closes after a
        /// completed selection (or a second press); Hold remains focused until
        /// the binding is released.
        /// </summary>
        public static NativeBottomScreenActivationMode BottomScreenActivation
        {
            get => _bottomScreenActivation;
            set => _bottomScreenActivation = Enum.IsDefined(value)
                ? value : NativeBottomScreenActivationMode.Toggle;
        }
        private static NativeBottomScreenActivationMode _bottomScreenActivation
            = NativeBottomScreenActivationMode.Toggle;
        public static float BottomScreenCursorSensitivity
        {
            get => _bottomScreenCursorSensitivity;
            set => _bottomScreenCursorSensitivity = Clamp(value, 1,
                NativeBottomScreenCursorOptions.MinimumSensitivity,
                NativeBottomScreenCursorOptions.MaximumSensitivity);
        }
        private static float _bottomScreenCursorSensitivity = 1;
        public static float BottomScreenCursorStartX
        {
            get => _bottomScreenCursorStartX;
            set => _bottomScreenCursorStartX = Clamp(value, .5f, 0, 1);
        }
        private static float _bottomScreenCursorStartX = .5f;
        public static float BottomScreenCursorStartY
        {
            get => _bottomScreenCursorStartY;
            set => _bottomScreenCursorStartY = Clamp(value, .5f, 0, 1);
        }
        private static float _bottomScreenCursorStartY = .5f;
        public static float BottomScreenPowerBeamX
        {
            get => _bottomScreenPowerBeamX;
            set => _bottomScreenPowerBeamX = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.PowerBeamX,
                NativeBottomScreenClassicLayoutOptions.PowerBeamRadius / 256f,
                1 - NativeBottomScreenClassicLayoutOptions.PowerBeamRadius / 256f);
        }
        private static float _bottomScreenPowerBeamX
            = NativeBottomScreenClassicLayoutOptions.Default.PowerBeamX;
        public static float BottomScreenPowerBeamY
        {
            get => _bottomScreenPowerBeamY;
            set => _bottomScreenPowerBeamY = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.PowerBeamY,
                NativeBottomScreenClassicLayoutOptions.PowerBeamRadius / 192f,
                1 - NativeBottomScreenClassicLayoutOptions.PowerBeamRadius / 192f);
        }
        private static float _bottomScreenPowerBeamY
            = NativeBottomScreenClassicLayoutOptions.Default.PowerBeamY;
        public static float BottomScreenMissileX
        {
            get => _bottomScreenMissileX;
            set => _bottomScreenMissileX = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.MissileX,
                NativeBottomScreenClassicLayoutOptions.MissileRadius / 256f,
                1 - NativeBottomScreenClassicLayoutOptions.MissileRadius / 256f);
        }
        private static float _bottomScreenMissileX
            = NativeBottomScreenClassicLayoutOptions.Default.MissileX;
        public static float BottomScreenMissileY
        {
            get => _bottomScreenMissileY;
            set => _bottomScreenMissileY = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.MissileY,
                NativeBottomScreenClassicLayoutOptions.MissileRadius / 192f,
                1 - NativeBottomScreenClassicLayoutOptions.MissileRadius / 192f);
        }
        private static float _bottomScreenMissileY
            = NativeBottomScreenClassicLayoutOptions.Default.MissileY;
        public static float BottomScreenNextWeaponX
        {
            get => _bottomScreenNextWeaponX;
            set => _bottomScreenNextWeaponX = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.NextWeaponX,
                NativeBottomScreenClassicLayoutOptions.NextWeaponRadius / 256f,
                1 - NativeBottomScreenClassicLayoutOptions.NextWeaponRadius / 256f);
        }
        private static float _bottomScreenNextWeaponX
            = NativeBottomScreenClassicLayoutOptions.Default.NextWeaponX;
        public static float BottomScreenNextWeaponY
        {
            get => _bottomScreenNextWeaponY;
            set => _bottomScreenNextWeaponY = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.NextWeaponY,
                NativeBottomScreenClassicLayoutOptions.NextWeaponRadius / 192f,
                1 - NativeBottomScreenClassicLayoutOptions.NextWeaponRadius / 192f);
        }
        private static float _bottomScreenNextWeaponY
            = NativeBottomScreenClassicLayoutOptions.Default.NextWeaponY;
        public static float BottomScreenWeaponSelectX
        {
            get => _bottomScreenWeaponSelectX;
            set => _bottomScreenWeaponSelectX = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.WeaponSelectX,
                NativeBottomScreenClassicLayoutOptions.WeaponSelectRadius / 256f,
                1 - NativeBottomScreenClassicLayoutOptions.WeaponSelectRadius / 256f);
        }
        private static float _bottomScreenWeaponSelectX
            = NativeBottomScreenClassicLayoutOptions.Default.WeaponSelectX;
        public static float BottomScreenWeaponSelectY
        {
            get => _bottomScreenWeaponSelectY;
            set => _bottomScreenWeaponSelectY = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.WeaponSelectY,
                NativeBottomScreenClassicLayoutOptions.WeaponSelectRadius / 192f,
                1 - NativeBottomScreenClassicLayoutOptions.WeaponSelectRadius / 192f);
        }
        private static float _bottomScreenWeaponSelectY
            = NativeBottomScreenClassicLayoutOptions.Default.WeaponSelectY;
        public static float BottomScreenAltFormX
        {
            get => _bottomScreenAltFormX;
            set => _bottomScreenAltFormX = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.AltFormX,
                NativeBottomScreenClassicLayoutOptions.AltFormRadius / 256f,
                1 - NativeBottomScreenClassicLayoutOptions.AltFormRadius / 256f);
        }
        private static float _bottomScreenAltFormX
            = NativeBottomScreenClassicLayoutOptions.Default.AltFormX;
        public static float BottomScreenAltFormY
        {
            get => _bottomScreenAltFormY;
            set => _bottomScreenAltFormY = Clamp(value,
                NativeBottomScreenClassicLayoutOptions.Default.AltFormY,
                NativeBottomScreenClassicLayoutOptions.AltFormRadius / 192f,
                1 - NativeBottomScreenClassicLayoutOptions.AltFormRadius / 192f);
        }
        private static float _bottomScreenAltFormY
            = NativeBottomScreenClassicLayoutOptions.Default.AltFormY;
        public static bool BottomScreenDirectionalSwipeAssist { get; set; } = true;
        public static NativeBottomScreenClassicLayoutOptions CurrentBottomScreenClassicLayout
            => new NativeBottomScreenClassicLayoutOptions(
                BottomScreenPowerBeamX, BottomScreenPowerBeamY,
                BottomScreenMissileX, BottomScreenMissileY,
                BottomScreenNextWeaponX, BottomScreenNextWeaponY,
                BottomScreenWeaponSelectX, BottomScreenWeaponSelectY,
                BottomScreenAltFormX, BottomScreenAltFormY).Sanitized();
        public static float BottomScreenAffinityVoltDriverX
        {
            get => _bottomScreenAffinityVoltDriverX;
            set => _bottomScreenAffinityVoltDriverX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.VoltDriverX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityVoltDriverX
            = NativeBottomScreenAffinityLayoutOptions.Default.VoltDriverX;
        public static float BottomScreenAffinityVoltDriverY
        {
            get => _bottomScreenAffinityVoltDriverY;
            set => _bottomScreenAffinityVoltDriverY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.VoltDriverY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityVoltDriverY
            = NativeBottomScreenAffinityLayoutOptions.Default.VoltDriverY;
        public static float BottomScreenAffinityBattlehammerX
        {
            get => _bottomScreenAffinityBattlehammerX;
            set => _bottomScreenAffinityBattlehammerX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityBattlehammerX
            = NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerX;
        public static float BottomScreenAffinityBattlehammerY
        {
            get => _bottomScreenAffinityBattlehammerY;
            set => _bottomScreenAffinityBattlehammerY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityBattlehammerY
            = NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerY;
        public static float BottomScreenAffinityImperialistX
        {
            get => _bottomScreenAffinityImperialistX;
            set => _bottomScreenAffinityImperialistX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.ImperialistX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityImperialistX
            = NativeBottomScreenAffinityLayoutOptions.Default.ImperialistX;
        public static float BottomScreenAffinityImperialistY
        {
            get => _bottomScreenAffinityImperialistY;
            set => _bottomScreenAffinityImperialistY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.ImperialistY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityImperialistY
            = NativeBottomScreenAffinityLayoutOptions.Default.ImperialistY;
        public static float BottomScreenAffinityJudicatorX
        {
            get => _bottomScreenAffinityJudicatorX;
            set => _bottomScreenAffinityJudicatorX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.JudicatorX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityJudicatorX
            = NativeBottomScreenAffinityLayoutOptions.Default.JudicatorX;
        public static float BottomScreenAffinityJudicatorY
        {
            get => _bottomScreenAffinityJudicatorY;
            set => _bottomScreenAffinityJudicatorY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.JudicatorY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityJudicatorY
            = NativeBottomScreenAffinityLayoutOptions.Default.JudicatorY;
        public static float BottomScreenAffinityMagmaulX
        {
            get => _bottomScreenAffinityMagmaulX;
            set => _bottomScreenAffinityMagmaulX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.MagmaulX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityMagmaulX
            = NativeBottomScreenAffinityLayoutOptions.Default.MagmaulX;
        public static float BottomScreenAffinityMagmaulY
        {
            get => _bottomScreenAffinityMagmaulY;
            set => _bottomScreenAffinityMagmaulY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.MagmaulY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityMagmaulY
            = NativeBottomScreenAffinityLayoutOptions.Default.MagmaulY;
        public static float BottomScreenAffinityShockCoilX
        {
            get => _bottomScreenAffinityShockCoilX;
            set => _bottomScreenAffinityShockCoilX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.ShockCoilX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityShockCoilX
            = NativeBottomScreenAffinityLayoutOptions.Default.ShockCoilX;
        public static float BottomScreenAffinityShockCoilY
        {
            get => _bottomScreenAffinityShockCoilY;
            set => _bottomScreenAffinityShockCoilY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.ShockCoilY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityShockCoilY
            = NativeBottomScreenAffinityLayoutOptions.Default.ShockCoilY;
        public static float BottomScreenAffinityPowerBeamX
        {
            get => _bottomScreenAffinityPowerBeamX;
            set => _bottomScreenAffinityPowerBeamX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityPowerBeamX
            = NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamX;
        public static float BottomScreenAffinityPowerBeamY
        {
            get => _bottomScreenAffinityPowerBeamY;
            set => _bottomScreenAffinityPowerBeamY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityPowerBeamY
            = NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamY;
        public static float BottomScreenAffinityMissileX
        {
            get => _bottomScreenAffinityMissileX;
            set => _bottomScreenAffinityMissileX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.MissileX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityMissileX
            = NativeBottomScreenAffinityLayoutOptions.Default.MissileX;
        public static float BottomScreenAffinityMissileY
        {
            get => _bottomScreenAffinityMissileY;
            set => _bottomScreenAffinityMissileY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.MissileY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityMissileY
            = NativeBottomScreenAffinityLayoutOptions.Default.MissileY;
        public static float BottomScreenAffinityAltFormX
        {
            get => _bottomScreenAffinityAltFormX;
            set => _bottomScreenAffinityAltFormX = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.AltFormX,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 256f);
        }
        private static float _bottomScreenAffinityAltFormX
            = NativeBottomScreenAffinityLayoutOptions.Default.AltFormX;
        public static float BottomScreenAffinityAltFormY
        {
            get => _bottomScreenAffinityAltFormY;
            set => _bottomScreenAffinityAltFormY = Clamp(value,
                NativeBottomScreenAffinityLayoutOptions.Default.AltFormY,
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f,
                1 - NativeBottomScreenAffinityLayoutOptions.AffinityRadius / 192f);
        }
        private static float _bottomScreenAffinityAltFormY
            = NativeBottomScreenAffinityLayoutOptions.Default.AltFormY;
        public static NativeBottomScreenAffinityLayoutOptions CurrentBottomScreenAffinityLayout
            => new NativeBottomScreenAffinityLayoutOptions(
                BottomScreenAffinityVoltDriverX, BottomScreenAffinityVoltDriverY,
                BottomScreenAffinityBattlehammerX, BottomScreenAffinityBattlehammerY,
                BottomScreenAffinityImperialistX, BottomScreenAffinityImperialistY,
                BottomScreenAffinityJudicatorX, BottomScreenAffinityJudicatorY,
                BottomScreenAffinityMagmaulX, BottomScreenAffinityMagmaulY,
                BottomScreenAffinityShockCoilX, BottomScreenAffinityShockCoilY,
                BottomScreenAffinityPowerBeamX, BottomScreenAffinityPowerBeamY,
                BottomScreenAffinityMissileX, BottomScreenAffinityMissileY,
                BottomScreenAffinityAltFormX, BottomScreenAffinityAltFormY).Sanitized();
        public static NativeBottomScreenStyle BottomScreenStyle { get; set; }
            = NativeBottomScreenStyle.ClassicDs;
        public static float BottomScreenScale
        {
            get => _bottomScreenScale;
            set => _bottomScreenScale = Clamp(value, 1, .4f, 1);
        }
        private static float _bottomScreenScale = 1;
        public static float BottomScreenCenterX
        {
            get => _bottomScreenCenterX;
            set => _bottomScreenCenterX = Clamp(value, .5f, 0, 1);
        }
        private static float _bottomScreenCenterX = .5f;
        public static float BottomScreenCenterY
        {
            get => _bottomScreenCenterY;
            set => _bottomScreenCenterY = Clamp(value, .685f, 0, 1);
        }
        private static float _bottomScreenCenterY = .685f;
        public static float BottomScreenOpacity
        {
            get => _bottomScreenOpacity;
            set => _bottomScreenOpacity = Clamp(value, .22f, .05f, 1);
        }
        private static float _bottomScreenOpacity = .22f;
        public static bool BottomScreenLabels { get; set; } = true;

        public static Input.StylusBindings CurrentStylusBindings
            => new(StylusPrimaryAction, StylusSecondaryAction);

        public static Input.ControllerPreset ControllerPreset
        {
            get => _controllerPreset;
            set => _controllerPreset = Enum.IsDefined(value)
                ? value : Input.ControllerPreset.Classic;
        }

        public static Input.ControllerPreset GamepadPreset
        {
            get => ControllerPreset;
            set => ControllerPreset = value;
        }

        private static Input.ControllerPreset _controllerPreset = Input.ControllerPreset.Classic;
        private static bool _applyingPreset;

        private static float Clamp(float value, float fallback, float minimum,
            float maximum)
            => !float.IsFinite(value) ? fallback : Math.Clamp(value, minimum, maximum);

        static InputSettings()
        {
            Input.PadBindings.Changed += () =>
            {
                if (!_applyingPreset)
                {
                    _controllerPreset = Input.ControllerPreset.Custom;
                }
            };
        }

        private static bool _creating;
        private static ClientPlayerBindings? _current;

        /// <summary>
        /// Exact in-memory state captured before a Settings view exposes the
        /// controls that edit bindings and presets eagerly. It uses the complete
        /// controls.txt schema without touching the filesystem.
        /// </summary>
        internal sealed class Snapshot
        {
            private readonly string[] _lines;
            private readonly Input.GamepadResponseCurvePreset _responseCurve;
            private readonly float _lookExponent;

            internal Snapshot(IEnumerable<string> lines,
                Input.GamepadResponseCurvePreset responseCurve, float lookExponent)
            {
                _lines = lines.ToArray();
                _responseCurve = responseCurve;
                _lookExponent = lookExponent;
            }

            internal void Restore()
            {
                LoadLines(_lines);
                // GamepadLookExponent and a named curve are independently
                // persisted for compatibility. Loading a named curve normally
                // resolves its exponent; rollback must restore the exact
                // in-memory pair that existed when Settings opened.
                _gamepadLookExponent = _lookExponent;
                _gamepadResponseCurve = _responseCurve;
            }
        }

        internal static Snapshot CaptureSnapshot()
            => new(GetStateLines(roundTrip: true), _gamepadResponseCurve,
                _gamepadLookExponent);

        /// <summary>
        /// The bindings every player is created with. The settings screen
        /// edits this set; <see cref="Apply"/> copies it onto each set upstream
        /// creates, and <see cref="ApplyToPlayers"/> onto the ones that already
        /// exist.
        /// </summary>
        public static ClientPlayerBindings Current
        {
            get
            {
                if (_current == null)
                {
                    // GetDefault calls Apply, which asks for Current: build the
                    // canonical set without letting that come back around.
                    _creating = true;
                    _current = ClientPlayerBindings.GetDefault();
                    _creating = false;
                }
                return _current;
            }
        }

        /// <summary>Every rebindable control, in the order a screen should list them.</summary>
        public static IReadOnlyList<PropertyInfo> Bindings => _bindings ??= FindBindings();

        private static PropertyInfo[]? _bindings;

        /// <summary>
        /// The ones worth putting first. Everything else -- the nine weapon
        /// slots, the roll and aim keys -- follows in declaration order, so a
        /// control added upstream shows up without being listed here.
        /// </summary>
        private static readonly string[] _order =
        {
            nameof(ClientPlayerBindings.MoveUp), nameof(ClientPlayerBindings.MoveDown),
            nameof(ClientPlayerBindings.MoveLeft), nameof(ClientPlayerBindings.MoveRight),
            nameof(ClientPlayerBindings.Jump), nameof(ClientPlayerBindings.Boost),
            nameof(ClientPlayerBindings.Shoot), nameof(ClientPlayerBindings.Zoom),
            nameof(ClientPlayerBindings.Morph), nameof(ClientPlayerBindings.AltAttack),
            nameof(ClientPlayerBindings.NextWeapon), nameof(ClientPlayerBindings.PrevWeapon),
            nameof(ClientPlayerBindings.WeaponMenu),
            nameof(ClientPlayerBindings.Pause), nameof(ClientPlayerBindings.HudOverlay)
        };

        private static PropertyInfo[] FindBindings()
        {
            PropertyInfo[] all = typeof(ClientPlayerBindings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(Keybind))
                .ToArray();
            return all
                .OrderBy(p =>
                {
                    int index = Array.IndexOf(_order, p.Name);
                    return index < 0 ? _order.Length : index;
                })
                .ToArray();
        }

        public static Keybind Bind(PropertyInfo property)
        {
            return (Keybind)property.GetValue(Current)!;
        }

        /// <summary>"Left Shift", "Mouse left", "Scroll up", "1".</summary>
        public static string Describe(Keybind bind)
        {
            switch (bind.Type)
            {
                case ButtonType.Mouse:
                    // OpenTK's enum names these Button1..Button8 and aliases
                    // the first three; ToString picks the number, which is not
                    // what anybody calls them.
                    return bind.MouseButton switch
                    {
                        PrimeMouseButton.Left => "Mouse left",
                        PrimeMouseButton.Right => "Mouse right",
                        PrimeMouseButton.Middle => "Mouse middle",
                        _ => $"Mouse {(int)bind.MouseButton + 1}"
                    };
                case ButtonType.ScrollUp:
                    return "Scroll up";
                case ButtonType.ScrollDown:
                    return "Scroll down";
                default:
                    return bind.Key == PrimeKey.Unknown ? "unbound" : KeyName(bind.Key);
            }
        }

        /// <summary>"D1" -> "1", "LeftShift" -> "Left shift", "KeyPad4" -> "Key pad 4".</summary>
        public static string KeyName(PrimeKey key)
        {
            string name = key.ToString();
            if (name.Length == 2 && name[0] == 'D' && Char.IsDigit(name[1]))
            {
                return name[1].ToString();
            }
            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i]) && !Char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                    builder.Append(Char.ToLowerInvariant(name[i]));
                }
                else
                {
                    builder.Append(name[i]);
                }
            }
            return builder.ToString();
        }

        /// <summary>"Humanise" a control's name for a screen: "AltAttack" -> "Alt attack".</summary>
        public static string ActionName(PropertyInfo property)
        {
            string name = property.Name == nameof(ClientPlayerBindings.Pause)
                ? "Scoreboard"
                : property.Name == nameof(ClientPlayerBindings.HudOverlay)
                    ? "Touch screen"
                : property.Name == nameof(ClientPlayerBindings.RolltLeft) ? "Roll left" : property.Name;
            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i]) && !Char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                    builder.Append(Char.ToLowerInvariant(name[i]));
                }
                else
                {
                    builder.Append(i == 0 ? Char.ToUpperInvariant(name[i]) : name[i]);
                }
            }
            return builder.ToString();
        }

        /// <summary>Point a control at a key, a mouse button or the wheel.</summary>
        public static void Rebind(PropertyInfo property, ButtonType type, PrimeKey key,
            PrimeMouseButton button)
        {
            Keybind bind = Bind(property);
            bind.Type = type;
            bind.Key = type == ButtonType.Key ? key : PrimeKey.Unknown;
            bind.MouseButton = button;
        }

        /// <summary>
        /// Copy the canonical bindings onto a set upstream just created. Called
        /// from GetDefault, so it covers every player in every match.
        /// </summary>
        public static void Apply(ClientPlayerBindings controls)
        {
            if (_creating || _current == null)
            {
                return;
            }
            foreach (PropertyInfo property in Bindings)
            {
                var source = (Keybind)property.GetValue(_current)!;
                var target = (Keybind)property.GetValue(controls)!;
                target.Type = source.Type;
                target.Key = source.Key;
                target.MouseButton = source.MouseButton;
            }
            controls.ScrollAllWeapons = ScrollAllWeapons;
        }

        /// <summary>
        /// Push the current values into an already-created set of portable
        /// bindings. The presentation host supplies the projection from its
        /// scene; Client.Core never reaches through a renderer-owned player.
        /// </summary>
        public static void ApplyToPlayers(IEnumerable<ClientPlayerBindings> players)
        {
            ArgumentNullException.ThrowIfNull(players);
            try
            {
                foreach (ClientPlayerBindings bindings in players) Apply(bindings);
            }
            catch (Exception)
            {
                // A presentation may replace its player list while the
                // settings surface saves. Fresh binding sets still receive
                // the canonical values through GetDefault.
            }
        }

        public static void Load()
        {
            if (!File.Exists(Path))
            {
                return;
            }
            try
            {
                LoadLines(File.ReadAllLines(Path));
            }
            catch (Exception)
            {
                // Bindings are a convenience; an unreadable file must not stop
                // the game from starting. Every exception, not only IOException:
                // a folder the user cannot read raises UnauthorizedAccessException.
            }
        }

        /// <summary>
        /// Parse controls-file lines without touching the filesystem. This is
        /// also the migration boundary used by the launcher and focused tests.
        /// Modern values are collected before legacy aliases are applied, so a
        /// modern key wins regardless of file ordering.
        /// </summary>
        public static void LoadLines(IEnumerable<string> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            float? legacyDeadzone = null;
            float? legacyLook = null;
            bool modernMoveDeadzone = false, modernLookDeadzone = false;
            bool modernHorizontal = false, modernVertical = false;
            var explicitBindings = new HashSet<Input.PadAction>();
            Input.ControllerPreset? requestedPreset = null;
            _applyingPreset = true;
            try
            {
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    int split = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || split <= 0)
                    {
                        continue;
                    }
                    string key = line[..split].Trim();
                    string value = line[(split + 1)..].Trim();
                    if (key == "sensitivity")
                    {
                        if (Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float parsed))
                            MouseSensitivity = parsed;
                        continue;
                    }
                    if (key == "dynamic_crosshair_travel_degrees"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float crosshairTravel))
                    {
                        DynamicCrosshairTravelDegrees = crosshairTravel;
                        continue;
                    }
                    if (key == "dynamic_crosshair_sensitivity"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float crosshairSensitivity))
                    {
                        DynamicCrosshairSensitivity = crosshairSensitivity;
                        continue;
                    }
                    if (key == "dynamic_crosshair_turn_speed"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float crosshairTurnSpeed))
                    {
                        DynamicCrosshairTurnSpeed = crosshairTurnSpeed;
                        continue;
                    }
                    if (key == "invert_y" && Boolean.TryParse(value, out bool invertY))
                    {
                        InvertMouseY = invertY;
                        continue;
                    }
                    if (key == "invert_x" && Boolean.TryParse(value, out bool invertX))
                    {
                        InvertMouseX = invertX;
                        continue;
                    }
                    if (key == "scroll_all_weapons" && Boolean.TryParse(value, out bool scrollAll))
                    {
                        ScrollAllWeapons = scrollAll;
                        continue;
                    }
                    if (key == "gamepad" || key == "input_schema")
                    {
                        continue;
                    }
                    if ((key == "controller_preset" || key == "gamepad_preset")
                        && Enum.TryParse(value, ignoreCase: true, out Input.ControllerPreset parsedPreset)
                        && Enum.IsDefined(parsedPreset))
                    {
                        requestedPreset = parsedPreset;
                        continue;
                    }
                    if (Input.PadBindings.TryLoad(key, value))
                    {
                        if (Enum.TryParse(key[4..], out Input.PadAction action))
                            explicitBindings.Add(action);
                        continue;
                    }
                    if (Input.TouchSettings.ReadSetting(key, value))
                    {
                        continue;
                    }
                    if (key == "gamepad_deadzone"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float deadZone))
                    {
                        legacyDeadzone = deadZone;
                        continue;
                    }
                    if (key == "gamepad_look"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float look))
                    {
                        legacyLook = look;
                        continue;
                    }
                    if (key == "gamepad_invert_y" && Boolean.TryParse(value, out bool padInvert))
                    {
                        GamepadInvertY = padInvert;
                        continue;
                    }
                    if (TryModernSetting(key, value, ref modernMoveDeadzone,
                        ref modernLookDeadzone, ref modernHorizontal, ref modernVertical))
                    {
                        continue;
                    }
                    if (key == "chat_key")
                    {
                        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                            ChatKey = PrimeKey.Unknown;
                        else if (Enum.TryParse(value, out PrimeKey chatKey)) ChatKey = chatKey;
                        continue;
                    }
                    PropertyInfo? property = Bindings.FirstOrDefault(p => p.Name == key);
                    if (property != null) ParseBind(property, value);
                }

                if (legacyDeadzone.HasValue)
                {
                    if (!modernMoveDeadzone) GamepadMoveDeadZone = legacyDeadzone.Value;
                    if (!modernLookDeadzone) GamepadLookDeadZone = legacyDeadzone.Value;
                }
                if (legacyLook.HasValue)
                {
                    if (!modernHorizontal) GamepadHorizontalSensitivity = legacyLook.Value;
                    if (!modernVertical) GamepadVerticalSensitivity = legacyLook.Value;
                }
                if (requestedPreset.HasValue)
                {
                    // Every tuning value is persisted explicitly. Loading a
                    // preset restores only omitted bindings so custom values
                    // written beside it remain authoritative.
                    ApplyPresetCore(requestedPreset.Value, explicitBindings,
                        applyTuning: false);
                }
                else if (explicitBindings.Count != 0)
                {
                    _controllerPreset = Input.ControllerPreset.Custom;
                }
            }
            finally
            {
                _applyingPreset = false;
            }
        }

        private static bool TryModernSetting(string key, string value,
            ref bool modernMoveDeadzone, ref bool modernLookDeadzone,
            ref bool modernHorizontal, ref bool modernVertical)
        {
            bool parsed = Single.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float number);
            bool boolean = Boolean.TryParse(value, out bool flag);
            switch (key)
            {
                case "gamepad_move_deadzone":
                    if (parsed) GamepadMoveDeadZone = number;
                    modernMoveDeadzone = true;
                    return true;
                case "gamepad_look_deadzone":
                    if (parsed) GamepadLookDeadZone = number;
                    modernLookDeadzone = true;
                    return true;
                case "gamepad_outer_deadzone": if (parsed) GamepadOuterDeadZone = number; return true;
                case "gamepad_move_activate":
                case "gamepad_move_activate_threshold":
                    if (parsed) GamepadMoveActivateThreshold = number; return true;
                case "gamepad_move_release":
                case "gamepad_move_release_threshold":
                    if (parsed) GamepadMoveReleaseThreshold = number; return true;
                case "gamepad_look_exponent":
                case "gamepad_response_exponent":
                    if (parsed)
                    {
                        GamepadLookExponent = number;
                        _gamepadResponseCurve = Input.GamepadResponseCurvePreset.Custom;
                    }
                    return true;
                case "gamepad_look_anti_deadzone":
                case "gamepad_anti_deadzone":
                    if (parsed) GamepadLookAntiDeadzone = number;
                    return true;
                case "gamepad_look_smoothing_seconds":
                case "gamepad_look_smoothing":
                    if (parsed) GamepadLookSmoothingSeconds = number;
                    return true;
                case "gamepad_response_curve":
                    if (Enum.TryParse(value, true,
                        out Input.GamepadResponseCurvePreset responseCurve))
                    {
                        GamepadResponseCurve = responseCurve;
                    }
                    return true;
                case "gamepad_yaw_rate":
                case "gamepad_yaw":
                    if (parsed) GamepadYawRate = number; return true;
                case "gamepad_pitch_rate":
                case "gamepad_pitch":
                    if (parsed) GamepadPitchRate = number; return true;
                case "gamepad_outer_boost_start": if (parsed) GamepadOuterBoostStart = number; return true;
                case "gamepad_outer_yaw_boost": if (parsed) GamepadOuterYawBoost = number; return true;
                case "gamepad_outer_pitch_boost": if (parsed) GamepadOuterPitchBoost = number; return true;
                case "gamepad_boost_delay":
                case "gamepad_outer_boost_delay": if (parsed) GamepadBoostDelaySeconds = number; return true;
                case "gamepad_boost_ramp":
                case "gamepad_outer_boost_ramp": if (parsed) GamepadBoostRampSeconds = number; return true;
                case "gamepad_outer_boost_enabled": if (boolean) GamepadOuterBoostEnabled = flag; return true;
                case "gamepad_turn_acceleration":
                    if (Enum.TryParse(value, true,
                        out Input.GamepadTurnAccelerationPreset acceleration))
                    {
                        GamepadTurnAcceleration = acceleration;
                    }
                    return true;
                case "gamepad_trigger_press":
                case "gamepad_trigger_press_threshold":
                    if (parsed) GamepadTriggerPressThreshold = number; return true;
                case "gamepad_trigger_release":
                case "gamepad_trigger_release_threshold":
                    if (parsed) GamepadTriggerReleaseThreshold = number; return true;
                case "gamepad_horizontal_sensitivity":
                case "gamepad_sensitivity_horizontal":
                    if (parsed) GamepadHorizontalSensitivity = number;
                    modernHorizontal = true;
                    return true;
                case "gamepad_vertical_sensitivity":
                case "gamepad_sensitivity_vertical":
                    if (parsed) GamepadVerticalSensitivity = number;
                    modernVertical = true;
                    return true;
                case "gamepad_zoom_multiplier":
                case "gamepad_zoom":
                    if (parsed) GamepadZoomMultiplier = number; return true;
                case "gamepad_zoom_horizontal_multiplier":
                    if (parsed) GamepadZoomHorizontalMultiplier = number; return true;
                case "gamepad_zoom_vertical_multiplier":
                    if (parsed) GamepadZoomVerticalMultiplier = number; return true;
                case "gamepad_auto_calibration":
                    if (boolean) GamepadAutoCalibrationEnabled = flag; return true;
                case "gamepad_stick_aim_mode":
                    if (Enum.TryParse(value, true,
                        out Input.GamepadStickAimMode stickAimMode))
                    {
                        GamepadStickAimMode = stickAimMode;
                    }
                    return true;
                case "gamepad_aim_assist":
                case "gamepad_aim_assist_enabled":
                    if (boolean) GamepadAimAssistEnabled = flag;
                    return true;
                case "gamepad_aim_assist_strength":
                    if (parsed) GamepadAimAssistStrength = number;
                    return true;
                case "gamepad_gyro":
                case "gamepad_gyro_enabled": if (boolean) GamepadGyroEnabled = flag; return true;
                case "gamepad_gyro_mode":
                    if (Enum.TryParse(value, true, out Input.GamepadGyroMode gyroMode))
                        GamepadGyroMode = gyroMode;
                    return true;
                case "gamepad_gyro_activation":
                    if (Enum.TryParse(value, true,
                        out Input.GamepadGyroActivation gyroActivation))
                    {
                        GamepadGyroActivation = gyroActivation;
                    }
                    return true;
                case "gamepad_gyro_sensitivity": if (parsed) GamepadGyroSensitivity = number; return true;
                case "gamepad_gyro_invert_x": if (boolean) GamepadGyroInvertX = flag; return true;
                case "gamepad_gyro_invert_y": if (boolean) GamepadGyroInvertY = flag; return true;
                case "gamepad_haptics":
                case "gamepad_haptics_enabled": if (boolean) GamepadHapticsEnabled = flag; return true;
                case "gamepad_haptics_strength": if (parsed) GamepadHapticsStrength = number; return true;
                case "input_balance_telemetry": if (boolean) InputBalanceTelemetryEnabled = flag; return true;
                case "stylus_aiming": if (boolean) StylusAimingEnabled = flag; return true;
                case "stylus_sensitivity": if (parsed) StylusSensitivity = number; return true;
                case "stylus_invert_y": if (boolean) StylusInvertY = flag; return true;
                case "stylus_primary":
                    if (Enum.TryParse(value, true, out Input.StylusAction primary)) StylusPrimaryAction = primary;
                    return true;
                case "stylus_secondary":
                    if (Enum.TryParse(value, true, out Input.StylusAction secondary)) StylusSecondaryAction = secondary;
                    return true;
                case "stylus_classic_gestures": if (boolean) StylusClassicGestures = flag; return true;
                case "stylus_double_tap_jump": if (boolean) StylusDoubleTapJump = flag; return true;
                case "stylus_flick_boost": if (boolean) StylusFlickBoost = flag; return true;
                case "morph_ball_mouse_flick_boost": if (boolean) MorphBallMouseFlickBoost = flag; return true;
                case "morph_ball_stick_flick_boost": if (boolean) MorphBallStickFlickBoost = flag; return true;
                case "morph_ball_swipe_boost": if (boolean) MorphBallSwipeBoost = flag; return true;
                case "stylus_pressure_to_fire": if (boolean) StylusPressureToFire = flag; return true;
                case "stylus_pressure_threshold": if (parsed) StylusPressureThreshold = number; return true;
                case "bottom_screen_mode":
                    if (Enum.TryParse(value, true, out NativeBottomScreenMode bottomScreenMode)
                        && Enum.IsDefined(bottomScreenMode))
                    {
                        BottomScreenMode = bottomScreenMode;
                    }
                    return true;
                case "bottom_screen_aim_mode":
                case "bottom_screen_true_ds_aim":
                    if (Enum.TryParse(value, true,
                        out NativeBottomScreenAimMode bottomScreenAimMode)
                        && Enum.IsDefined(bottomScreenAimMode))
                    {
                        BottomScreenAimMode = bottomScreenAimMode;
                    }
                    else if (boolean)
                    {
                        BottomScreenAimMode = flag
                            ? NativeBottomScreenAimMode.TrueDs
                            : NativeBottomScreenAimMode.Free;
                    }
                    return true;
                case "bottom_screen_activation":
                    if (Enum.TryParse(value, true,
                        out NativeBottomScreenActivationMode activationMode)
                        && Enum.IsDefined(activationMode))
                    {
                        BottomScreenActivation = activationMode;
                    }
                    return true;
                case "bottom_screen_cursor_sensitivity":
                    if (parsed) BottomScreenCursorSensitivity = number;
                    return true;
                case "bottom_screen_cursor_start_x":
                    if (parsed) BottomScreenCursorStartX = number;
                    return true;
                case "bottom_screen_cursor_start_y":
                    if (parsed) BottomScreenCursorStartY = number;
                    return true;
                case "bottom_screen_power_beam_x":
                    if (parsed) BottomScreenPowerBeamX = number;
                    return true;
                case "bottom_screen_power_beam_y":
                    if (parsed) BottomScreenPowerBeamY = number;
                    return true;
                case "bottom_screen_missile_x":
                    if (parsed) BottomScreenMissileX = number;
                    return true;
                case "bottom_screen_missile_y":
                    if (parsed) BottomScreenMissileY = number;
                    return true;
                case "bottom_screen_next_weapon_x":
                    if (parsed) BottomScreenNextWeaponX = number;
                    return true;
                case "bottom_screen_next_weapon_y":
                    if (parsed) BottomScreenNextWeaponY = number;
                    return true;
                case "bottom_screen_weapon_select_x":
                    if (parsed) BottomScreenWeaponSelectX = number;
                    return true;
                case "bottom_screen_weapon_select_y":
                    if (parsed) BottomScreenWeaponSelectY = number;
                    return true;
                case "bottom_screen_alt_form_x":
                    if (parsed) BottomScreenAltFormX = number;
                    return true;
                case "bottom_screen_alt_form_y":
                    if (parsed) BottomScreenAltFormY = number;
                    return true;
                case "bottom_screen_directional_swipe_assist":
                    if (boolean) BottomScreenDirectionalSwipeAssist = flag;
                    return true;
                case "bottom_screen_affinity_volt_driver_x":
                    if (parsed) BottomScreenAffinityVoltDriverX = number;
                    return true;
                case "bottom_screen_affinity_volt_driver_y":
                    if (parsed) BottomScreenAffinityVoltDriverY = number;
                    return true;
                case "bottom_screen_affinity_battlehammer_x":
                    if (parsed) BottomScreenAffinityBattlehammerX = number;
                    return true;
                case "bottom_screen_affinity_battlehammer_y":
                    if (parsed) BottomScreenAffinityBattlehammerY = number;
                    return true;
                case "bottom_screen_affinity_imperialist_x":
                    if (parsed) BottomScreenAffinityImperialistX = number;
                    return true;
                case "bottom_screen_affinity_imperialist_y":
                    if (parsed) BottomScreenAffinityImperialistY = number;
                    return true;
                case "bottom_screen_affinity_judicator_x":
                    if (parsed) BottomScreenAffinityJudicatorX = number;
                    return true;
                case "bottom_screen_affinity_judicator_y":
                    if (parsed) BottomScreenAffinityJudicatorY = number;
                    return true;
                case "bottom_screen_affinity_magmaul_x":
                    if (parsed) BottomScreenAffinityMagmaulX = number;
                    return true;
                case "bottom_screen_affinity_magmaul_y":
                    if (parsed) BottomScreenAffinityMagmaulY = number;
                    return true;
                case "bottom_screen_affinity_shock_coil_x":
                    if (parsed) BottomScreenAffinityShockCoilX = number;
                    return true;
                case "bottom_screen_affinity_shock_coil_y":
                    if (parsed) BottomScreenAffinityShockCoilY = number;
                    return true;
                case "bottom_screen_affinity_power_beam_x":
                    if (parsed) BottomScreenAffinityPowerBeamX = number;
                    return true;
                case "bottom_screen_affinity_power_beam_y":
                    if (parsed) BottomScreenAffinityPowerBeamY = number;
                    return true;
                case "bottom_screen_affinity_missile_x":
                    if (parsed) BottomScreenAffinityMissileX = number;
                    return true;
                case "bottom_screen_affinity_missile_y":
                    if (parsed) BottomScreenAffinityMissileY = number;
                    return true;
                case "bottom_screen_affinity_alt_form_x":
                    if (parsed) BottomScreenAffinityAltFormX = number;
                    return true;
                case "bottom_screen_affinity_alt_form_y":
                    if (parsed) BottomScreenAffinityAltFormY = number;
                    return true;
                case "bottom_screen_style":
                    if (Enum.TryParse(value, true, out NativeBottomScreenStyle bottomScreenStyle)
                        && Enum.IsDefined(bottomScreenStyle))
                    {
                        BottomScreenStyle = bottomScreenStyle;
                    }
                    return true;
                case "bottom_screen_scale": if (parsed) BottomScreenScale = number; return true;
                case "bottom_screen_center_x": if (parsed) BottomScreenCenterX = number; return true;
                case "bottom_screen_center_y": if (parsed) BottomScreenCenterY = number; return true;
                case "bottom_screen_opacity": if (parsed) BottomScreenOpacity = number; return true;
                case "bottom_screen_labels": if (boolean) BottomScreenLabels = flag; return true;
                default: return false;
            }
        }

        public static void ApplyPreset(Input.ControllerPreset preset)
        {
            if (!Enum.IsDefined(preset))
            {
                preset = Input.ControllerPreset.Classic;
            }
            if (preset == Input.ControllerPreset.Custom)
            {
                _controllerPreset = preset;
                return;
            }
            _applyingPreset = true;
            try
            {
                ApplyPresetCore(preset, preserve: null, applyTuning: true);
            }
            finally
            {
                _applyingPreset = false;
            }
        }

        private static void ApplyPresetCore(Input.ControllerPreset preset,
            HashSet<Input.PadAction>? preserve, bool applyTuning)
        {
            // Custom is a label for the current bindings, not a default layout.
            // Partial settings files must leave omitted bindings unchanged.
            if (preset == Input.ControllerPreset.Custom)
            {
                _controllerPreset = preset;
                return;
            }
            foreach (Input.PadAction action in Input.PadBindings.Actions)
            {
                if (preserve?.Contains(action) == true) continue;
                GamepadButtons value = Input.PadBindings.Default(action);
                if (preset is Input.ControllerPreset.Competitive
                    or Input.ControllerPreset.GyroCompetitive)
                {
                    value = action switch
                    {
                        Input.PadAction.Jump => GamepadButtons.LeftBumper | GamepadButtons.A,
                        Input.PadAction.PrevWeapon => GamepadButtons.DpadLeft,
                        _ => value
                    };
                }
                Input.PadBindings.SetFromPreset(action, value);
            }
            if (applyTuning)
            {
                if (preset == Input.ControllerPreset.Classic)
                {
                    ResetControllerGeneral();
                    ResetControllerAdvancedTuning();
                    ResetControllerGyro();
                }
                else
                {
                    ApplyCompetitiveControllerTuning(
                        gyro: preset == Input.ControllerPreset.GyroCompetitive);
                }
            }
            _controllerPreset = preset;
        }

        private static void ApplyCompetitiveControllerTuning(bool gyro)
        {
            GamepadHorizontalSensitivity = 1;
            GamepadVerticalSensitivity = 1;
            GamepadZoomHorizontalMultiplier = 1.10f;
            GamepadZoomVerticalMultiplier = 1f;
            GamepadMoveDeadZone = .13f;
            GamepadLookDeadZone = .07f;
            GamepadOuterDeadZone = .02f;
            GamepadMoveActivateThreshold = .20f;
            GamepadMoveReleaseThreshold = .14f;
            GamepadLookExponent = 1.35f;
            _gamepadResponseCurve = Input.GamepadResponseCurvePreset.Custom;
            GamepadYawRate = 360;
            GamepadPitchRate = 285;
            GamepadTurnAcceleration = Input.GamepadTurnAccelerationPreset.Fast;
            GamepadTriggerPressThreshold = .12f;
            GamepadTriggerReleaseThreshold = .07f;
            GamepadAutoCalibrationEnabled = true;
            GamepadStickAimMode = gyro
                ? Input.GamepadStickAimMode.FlickStick
                : Input.GamepadStickAimMode.Traditional;
            GamepadGyroMode = gyro
                ? Input.GamepadGyroMode.Always : Input.GamepadGyroMode.Off;
            GamepadGyroSensitivity = gyro ? 1.15f : 1f;
        }

        private static void ParseBind(PropertyInfo property, string value)
        {
            string[] parts = value.Split(':', 2);
            string type = parts[0].Trim();
            string name = parts.Length > 1 ? parts[1].Trim() : "";
            if (type == "ScrollUp")
            {
                Rebind(property, ButtonType.ScrollUp, PrimeKey.Unknown, PrimeMouseButton.Left);
            }
            else if (type == "ScrollDown")
            {
                Rebind(property, ButtonType.ScrollDown, PrimeKey.Unknown, PrimeMouseButton.Left);
            }
            else if (type == "Mouse" && Enum.TryParse(name, out PrimeMouseButton button))
            {
                Rebind(property, ButtonType.Mouse, PrimeKey.Unknown, button);
            }
            else if (type == "Key" && Enum.TryParse(name, out PrimeKey key))
            {
                Rebind(property, ButtonType.Key, key, PrimeMouseButton.Left);
            }
        }

        public static void Save()
        {
            try
            {
                var lines = GetSaveLines();
                File.WriteAllLines(Path, lines);
            }
            catch (Exception)
            {
                // Same reason as Load: the folder beside the executable is not
                // guaranteed to be writable, and losing a rebind is a far
                // smaller thing than taking down the window that made it --
                // which, from the pause menu, is the thread the menu runs on.
            }
        }

        /// <summary>
        /// Serialize settings without touching the filesystem. Keeping this
        /// separate makes the schema/migration boundary testable and ensures
        /// every save writes the modern controller values.
        /// </summary>
        public static IReadOnlyList<string> GetSaveLines()
            => GetStateLines(roundTrip: false);

        private static IReadOnlyList<string> GetStateLines(bool roundTrip)
        {
            string Float(float value) => value.ToString(
                roundTrip ? "R" : "0.###", CultureInfo.InvariantCulture);
            string LegacyFloat(float value) => value.ToString(
                roundTrip ? "R" : null, CultureInfo.InvariantCulture);
            var lines = new List<string>
            {
                    $"# {Branding.Name} controls. Delete a line to go back to the default.",
                    "input_schema=4",
                    $"sensitivity={Float(NormalizeMouseSensitivity(MouseSensitivity))}",
                    "dynamic_crosshair_travel_degrees=" + Float(DynamicCrosshairTravelDegrees),
                    "dynamic_crosshair_sensitivity=" + Float(DynamicCrosshairSensitivity),
                    "dynamic_crosshair_turn_speed=" + Float(DynamicCrosshairTurnSpeed),
                    $"invert_y={InvertMouseY.ToString().ToLowerInvariant()}",
                    $"invert_x={InvertMouseX.ToString().ToLowerInvariant()}",
                    $"scroll_all_weapons={ScrollAllWeapons.ToString().ToLowerInvariant()}",
                    $"morph_ball_mouse_flick_boost={MorphBallMouseFlickBoost.ToString().ToLowerInvariant()}",
                    $"morph_ball_stick_flick_boost={MorphBallStickFlickBoost.ToString().ToLowerInvariant()}",
                    $"morph_ball_swipe_boost={MorphBallSwipeBoost.ToString().ToLowerInvariant()}",
                    $"chat_key={(ChatKey == PrimeKey.Unknown ? "none" : ChatKey.ToString())}",
                    $"controller_preset={ControllerPreset}",
                    "gamepad_move_deadzone=" + Float(GamepadMoveDeadZone),
                    "gamepad_look_deadzone=" + Float(GamepadLookDeadZone),
                    "gamepad_outer_deadzone=" + Float(GamepadOuterDeadZone),
                    "gamepad_move_activate=" + Float(GamepadMoveActivateThreshold),
                    "gamepad_move_release=" + Float(GamepadMoveReleaseThreshold),
                    "gamepad_look_exponent=" + Float(GamepadLookExponent),
                    "gamepad_look_anti_deadzone=" + Float(GamepadLookAntiDeadzone),
                    "gamepad_look_smoothing_seconds=" + Float(GamepadLookSmoothingSeconds),
                    $"gamepad_response_curve={GamepadResponseCurve}",
                    "gamepad_yaw_rate=" + Float(GamepadYawRate),
                    "gamepad_pitch_rate=" + Float(GamepadPitchRate),
                    $"gamepad_turn_acceleration={GamepadTurnAcceleration}",
                    "gamepad_outer_boost_start=" + Float(GamepadOuterBoostStart),
                    "gamepad_outer_yaw_boost=" + Float(GamepadOuterYawBoost),
                    "gamepad_outer_pitch_boost=" + Float(GamepadOuterPitchBoost),
                    "gamepad_boost_delay=" + Float(GamepadBoostDelaySeconds),
                    "gamepad_boost_ramp=" + Float(GamepadBoostRampSeconds),
                    $"gamepad_outer_boost_enabled={GamepadOuterBoostEnabled.ToString().ToLowerInvariant()}",
                    "gamepad_trigger_press=" + Float(GamepadTriggerPressThreshold),
                    "gamepad_trigger_release=" + Float(GamepadTriggerReleaseThreshold),
                    "gamepad_horizontal_sensitivity=" + Float(GamepadHorizontalSensitivity),
                    "gamepad_vertical_sensitivity=" + Float(GamepadVerticalSensitivity),
                    "gamepad_zoom_multiplier=" + Float(GamepadZoomMultiplier),
                    "gamepad_zoom_horizontal_multiplier=" + Float(GamepadZoomHorizontalMultiplier),
                    "gamepad_zoom_vertical_multiplier=" + Float(GamepadZoomVerticalMultiplier),
                    $"gamepad_auto_calibration={GamepadAutoCalibrationEnabled.ToString().ToLowerInvariant()}",
                    $"gamepad_stick_aim_mode={GamepadStickAimMode}",
                    $"gamepad_aim_assist_enabled={GamepadAimAssistEnabled.ToString().ToLowerInvariant()}",
                    "gamepad_aim_assist_strength=" + Float(GamepadAimAssistStrength),
                    $"gamepad_gyro_enabled={GamepadGyroEnabled.ToString().ToLowerInvariant()}",
                    $"gamepad_gyro_mode={GamepadGyroMode}",
                    $"gamepad_gyro_activation={GamepadGyroActivation}",
                    "gamepad_gyro_sensitivity=" + Float(GamepadGyroSensitivity),
                    $"gamepad_gyro_invert_x={GamepadGyroInvertX.ToString().ToLowerInvariant()}",
                    $"gamepad_gyro_invert_y={GamepadGyroInvertY.ToString().ToLowerInvariant()}",
                    $"gamepad_haptics_enabled={GamepadHapticsEnabled.ToString().ToLowerInvariant()}",
                    "gamepad_haptics_strength=" + Float(GamepadHapticsStrength),
                    $"input_balance_telemetry={InputBalanceTelemetryEnabled.ToString().ToLowerInvariant()}",
                    $"stylus_aiming={StylusAimingEnabled.ToString().ToLowerInvariant()}",
                    "stylus_sensitivity=" + Float(StylusSensitivity),
                    $"stylus_invert_y={StylusInvertY.ToString().ToLowerInvariant()}",
                    $"stylus_primary={StylusPrimaryAction}",
                    $"stylus_secondary={StylusSecondaryAction}",
                    $"stylus_classic_gestures={StylusClassicGestures.ToString().ToLowerInvariant()}",
                    $"stylus_double_tap_jump={StylusDoubleTapJump.ToString().ToLowerInvariant()}",
                    $"stylus_flick_boost={StylusFlickBoost.ToString().ToLowerInvariant()}",
                    $"stylus_pressure_to_fire={StylusPressureToFire.ToString().ToLowerInvariant()}",
                    "stylus_pressure_threshold=" + Float(StylusPressureThreshold),
                    $"bottom_screen_mode={BottomScreenMode}",
                    $"bottom_screen_aim_mode={BottomScreenAimMode}",
                    $"bottom_screen_activation={BottomScreenActivation}",
                    "bottom_screen_cursor_sensitivity=" + Float(BottomScreenCursorSensitivity),
                    "bottom_screen_cursor_start_x=" + Float(BottomScreenCursorStartX),
                    "bottom_screen_cursor_start_y=" + Float(BottomScreenCursorStartY),
                    "bottom_screen_power_beam_x=" + Float(BottomScreenPowerBeamX),
                    "bottom_screen_power_beam_y=" + Float(BottomScreenPowerBeamY),
                    "bottom_screen_missile_x=" + Float(BottomScreenMissileX),
                    "bottom_screen_missile_y=" + Float(BottomScreenMissileY),
                    "bottom_screen_next_weapon_x=" + Float(BottomScreenNextWeaponX),
                    "bottom_screen_next_weapon_y=" + Float(BottomScreenNextWeaponY),
                    "bottom_screen_weapon_select_x=" + Float(BottomScreenWeaponSelectX),
                    "bottom_screen_weapon_select_y=" + Float(BottomScreenWeaponSelectY),
                    "bottom_screen_alt_form_x=" + Float(BottomScreenAltFormX),
                    "bottom_screen_alt_form_y=" + Float(BottomScreenAltFormY),
                    $"bottom_screen_directional_swipe_assist={BottomScreenDirectionalSwipeAssist.ToString().ToLowerInvariant()}",
                    "bottom_screen_affinity_volt_driver_x=" + Float(BottomScreenAffinityVoltDriverX),
                    "bottom_screen_affinity_volt_driver_y=" + Float(BottomScreenAffinityVoltDriverY),
                    "bottom_screen_affinity_battlehammer_x=" + Float(BottomScreenAffinityBattlehammerX),
                    "bottom_screen_affinity_battlehammer_y=" + Float(BottomScreenAffinityBattlehammerY),
                    "bottom_screen_affinity_imperialist_x=" + Float(BottomScreenAffinityImperialistX),
                    "bottom_screen_affinity_imperialist_y=" + Float(BottomScreenAffinityImperialistY),
                    "bottom_screen_affinity_judicator_x=" + Float(BottomScreenAffinityJudicatorX),
                    "bottom_screen_affinity_judicator_y=" + Float(BottomScreenAffinityJudicatorY),
                    "bottom_screen_affinity_magmaul_x=" + Float(BottomScreenAffinityMagmaulX),
                    "bottom_screen_affinity_magmaul_y=" + Float(BottomScreenAffinityMagmaulY),
                    "bottom_screen_affinity_shock_coil_x=" + Float(BottomScreenAffinityShockCoilX),
                    "bottom_screen_affinity_shock_coil_y=" + Float(BottomScreenAffinityShockCoilY),
                    "bottom_screen_affinity_power_beam_x=" + Float(BottomScreenAffinityPowerBeamX),
                    "bottom_screen_affinity_power_beam_y=" + Float(BottomScreenAffinityPowerBeamY),
                    "bottom_screen_affinity_missile_x=" + Float(BottomScreenAffinityMissileX),
                    "bottom_screen_affinity_missile_y=" + Float(BottomScreenAffinityMissileY),
                    "bottom_screen_affinity_alt_form_x=" + Float(BottomScreenAffinityAltFormX),
                    "bottom_screen_affinity_alt_form_y=" + Float(BottomScreenAffinityAltFormY),
                    $"bottom_screen_style={BottomScreenStyle}",
                    "bottom_screen_scale=" + Float(BottomScreenScale),
                    "bottom_screen_center_x=" + Float(BottomScreenCenterX),
                    "bottom_screen_center_y=" + Float(BottomScreenCenterY),
                    "bottom_screen_opacity=" + Float(BottomScreenOpacity),
                    $"bottom_screen_labels={BottomScreenLabels.ToString().ToLowerInvariant()}",
                    "gamepad_deadzone=" + LegacyFloat(GamepadDeadZone),
                    "gamepad_look=" + LegacyFloat(GamepadLookSensitivity),
                    $"gamepad_invert_y={GamepadInvertY.ToString().ToLowerInvariant()}"
            };
            foreach (Input.PadAction action in Input.PadBindings.Actions)
            {
                lines.Add($"{Input.PadBindings.SettingKey(action)}="
                    + Input.PadBindings.Get(action));
            }
            Input.TouchSettings.WriteSettings(lines);
            foreach (PropertyInfo property in Bindings)
            {
                Keybind bind = Bind(property);
                string value = bind.Type switch
                {
                    ButtonType.Mouse => $"Mouse:{bind.MouseButton}",
                    ButtonType.ScrollUp => "ScrollUp",
                    ButtonType.ScrollDown => "ScrollDown",
                    _ => $"Key:{bind.Key}"
                };
                lines.Add($"{property.Name}={value}");
            }
            return lines;
        }

        /// <summary>Put everything back the way it shipped.</summary>
        public static void Reset()
        {
            _creating = true;
            _current = ClientPlayerBindings.GetDefault();
            _creating = false;
            MouseSensitivity = DefaultMouseSensitivity;
            DynamicCrosshairTravelDegrees
                = DynamicCrosshairTuning.DefaultTravelDegrees;
            DynamicCrosshairSensitivity
                = DynamicCrosshairTuning.DefaultMovementSensitivity;
            DynamicCrosshairTurnSpeed
                = DynamicCrosshairTuning.DefaultTurnSpeed;
            InvertMouseY = false;
            InvertMouseX = false;
            ScrollAllWeapons = true;
            MorphBallMouseFlickBoost = true;
            MorphBallStickFlickBoost = true;
            MorphBallSwipeBoost = true;
            Input.TouchSettings.Reset();
            ResetBindings();
            ResetControllerGeneral();
            ResetControllerAdvancedTuning();
            ResetControllerGyro();
            GamepadHapticsEnabled = true;
            GamepadHapticsStrength = 1;
            GamepadAimAssistEnabled = true;
            GamepadAimAssistStrength = 1;
            InputBalanceTelemetryEnabled = false;
            _controllerPreset = Input.ControllerPreset.Classic;
            StylusAimingEnabled = true;
            StylusSensitivity = 1;
            StylusInvertY = false;
            StylusPrimaryAction = Input.StylusAction.Fire;
            StylusSecondaryAction = Input.StylusAction.Zoom;
            StylusClassicGestures = true;
            StylusDoubleTapJump = true;
            StylusFlickBoost = true;
            StylusPressureToFire = false;
            StylusPressureThreshold = 0.35f;
            BottomScreenMode = NativeBottomScreenMode.Off;
            BottomScreenAimMode = NativeBottomScreenAimMode.Free;
            BottomScreenActivation = NativeBottomScreenActivationMode.Toggle;
            BottomScreenCursorSensitivity = 1;
            BottomScreenCursorStartX = .5f;
            BottomScreenCursorStartY = .5f;
            BottomScreenPowerBeamX = NativeBottomScreenClassicLayoutOptions.Default.PowerBeamX;
            BottomScreenPowerBeamY = NativeBottomScreenClassicLayoutOptions.Default.PowerBeamY;
            BottomScreenMissileX = NativeBottomScreenClassicLayoutOptions.Default.MissileX;
            BottomScreenMissileY = NativeBottomScreenClassicLayoutOptions.Default.MissileY;
            BottomScreenNextWeaponX = NativeBottomScreenClassicLayoutOptions.Default.NextWeaponX;
            BottomScreenNextWeaponY = NativeBottomScreenClassicLayoutOptions.Default.NextWeaponY;
            BottomScreenWeaponSelectX = NativeBottomScreenClassicLayoutOptions.Default.WeaponSelectX;
            BottomScreenWeaponSelectY = NativeBottomScreenClassicLayoutOptions.Default.WeaponSelectY;
            BottomScreenAltFormX = NativeBottomScreenClassicLayoutOptions.Default.AltFormX;
            BottomScreenAltFormY = NativeBottomScreenClassicLayoutOptions.Default.AltFormY;
            BottomScreenDirectionalSwipeAssist = true;
            BottomScreenAffinityVoltDriverX = NativeBottomScreenAffinityLayoutOptions.Default.VoltDriverX;
            BottomScreenAffinityVoltDriverY = NativeBottomScreenAffinityLayoutOptions.Default.VoltDriverY;
            BottomScreenAffinityBattlehammerX = NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerX;
            BottomScreenAffinityBattlehammerY = NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerY;
            BottomScreenAffinityImperialistX = NativeBottomScreenAffinityLayoutOptions.Default.ImperialistX;
            BottomScreenAffinityImperialistY = NativeBottomScreenAffinityLayoutOptions.Default.ImperialistY;
            BottomScreenAffinityJudicatorX = NativeBottomScreenAffinityLayoutOptions.Default.JudicatorX;
            BottomScreenAffinityJudicatorY = NativeBottomScreenAffinityLayoutOptions.Default.JudicatorY;
            BottomScreenAffinityMagmaulX = NativeBottomScreenAffinityLayoutOptions.Default.MagmaulX;
            BottomScreenAffinityMagmaulY = NativeBottomScreenAffinityLayoutOptions.Default.MagmaulY;
            BottomScreenAffinityShockCoilX = NativeBottomScreenAffinityLayoutOptions.Default.ShockCoilX;
            BottomScreenAffinityShockCoilY = NativeBottomScreenAffinityLayoutOptions.Default.ShockCoilY;
            BottomScreenAffinityPowerBeamX = NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamX;
            BottomScreenAffinityPowerBeamY = NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamY;
            BottomScreenAffinityMissileX = NativeBottomScreenAffinityLayoutOptions.Default.MissileX;
            BottomScreenAffinityMissileY = NativeBottomScreenAffinityLayoutOptions.Default.MissileY;
            BottomScreenAffinityAltFormX = NativeBottomScreenAffinityLayoutOptions.Default.AltFormX;
            BottomScreenAffinityAltFormY = NativeBottomScreenAffinityLayoutOptions.Default.AltFormY;
            BottomScreenStyle = NativeBottomScreenStyle.ClassicDs;
            BottomScreenScale = 1;
            BottomScreenCenterX = .5f;
            BottomScreenCenterY = .685f;
            BottomScreenOpacity = .22f;
            BottomScreenLabels = true;
        }

        /// <summary>
        /// Restore keyboard, chat and controller bindings only. Mouse feel,
        /// touch layout, stylus preferences and controller tuning remain
        /// untouched so this can be offered as a focused bindings reset.
        /// </summary>
        public static void ResetBindings()
        {
            _creating = true;
            try
            {
                _current = ClientPlayerBindings.GetDefault();
            }
            finally
            {
                _creating = false;
            }
            ChatKey = PrimeKey.T;
            ResetControllerBindings();
        }

        /// <summary>Restore only the pad bindings, preserving all input tuning.</summary>
        public static void ResetControllerBindings()
        {
            _applyingPreset = true;
            try
            {
                Input.PadBindings.Reset();
            }
            finally
            {
                _applyingPreset = false;
            }
            // A custom curve remains custom after a binding-only reset. If the
            // curve is still at its shipped values, the default bindings are
            // once again the Classic preset.
            if (ControllerTuningIsDefault())
            {
                _controllerPreset = Input.ControllerPreset.Classic;
            }
            else
            {
                _controllerPreset = Input.ControllerPreset.Custom;
            }
        }

        /// <summary>
        /// Restore the core controller aim controls without touching bindings,
        /// advanced response tuning, mouse, touch or stylus preferences.
        /// </summary>
        public static void ResetControllerAimSettings()
        {
            GamepadHorizontalSensitivity = 1f;
            GamepadVerticalSensitivity = 1f;
            GamepadInvertY = false;
            GamepadMoveDeadZone = 0.15f;
            GamepadLookDeadZone = 0.10f;
            GamepadMoveActivateThreshold = 0.25f;
            GamepadMoveReleaseThreshold = 0.18f;
            GamepadLookExponent = 1.60f;
            GamepadLookAntiDeadzone = Input.GamepadLookProcessor.DefaultAntiDeadzone;
            GamepadLookSmoothingSeconds = Input.GamepadLookProcessor.DefaultSmoothingSeconds;
            _gamepadResponseCurve = Input.GamepadResponseCurvePreset.Balanced;
            GamepadYawRate = 300;
            GamepadPitchRate = 240;
            GamepadZoomMultiplier = 1f;
            GamepadAutoCalibrationEnabled = true;
            GamepadStickAimMode = Input.GamepadStickAimMode.Traditional;
            GamepadGyroEnabled = false;
            GamepadGyroSensitivity = 1f;
            GamepadGyroInvertX = false;
            GamepadGyroInvertY = false;
            GamepadGyro.ResetDevice();
        }

        /// <summary>
        /// Restore the controller's general feel without changing bindings,
        /// response-curve tuning or gyro preferences. This is the scope shown
        /// by the controller's General reset action in Settings.
        /// </summary>
        public static void ResetControllerGeneral()
        {
            GamepadHorizontalSensitivity = 1f;
            GamepadVerticalSensitivity = 1f;
            GamepadInvertY = false;
            GamepadZoomMultiplier = 1f;
            GamepadHapticsEnabled = true;
            GamepadHapticsStrength = 1f;
        }

        /// <summary>
        /// Restore controller response-curve and threshold controls. General
        /// feel, bindings, gyro, mouse, touch and stylus settings remain.
        /// </summary>
        public static void ResetControllerAdvancedTuning()
        {
            GamepadMoveDeadZone = 0.15f;
            GamepadLookDeadZone = 0.10f;
            GamepadOuterDeadZone = 0.02f;
            GamepadMoveActivateThreshold = 0.25f;
            GamepadMoveReleaseThreshold = 0.18f;
            GamepadLookExponent = 1.60f;
            GamepadLookAntiDeadzone = Input.GamepadLookProcessor.DefaultAntiDeadzone;
            GamepadLookSmoothingSeconds = Input.GamepadLookProcessor.DefaultSmoothingSeconds;
            _gamepadResponseCurve = Input.GamepadResponseCurvePreset.Balanced;
            GamepadYawRate = 300;
            GamepadPitchRate = 240;
            GamepadOuterBoostEnabled = true;
            GamepadOuterBoostStart = 0.95f;
            GamepadOuterYawBoost = 150;
            GamepadOuterPitchBoost = 80;
            GamepadBoostDelaySeconds = 0.18f;
            GamepadBoostRampSeconds = 0.12f;
            _gamepadTurnAcceleration = Input.GamepadTurnAccelerationPreset.Standard;
            GamepadTriggerPressThreshold = 0.20f;
            GamepadTriggerReleaseThreshold = 0.12f;
            GamepadAutoCalibrationEnabled = true;
            GamepadStickAimMode = Input.GamepadStickAimMode.Traditional;
        }

        /// <summary>
        /// Restore only the gyro controls and reset the live sensor integrator.
        /// This remains a preference reset; capability detection belongs to the
        /// input platform boundary, not to this settings model.
        /// </summary>
        public static void ResetControllerGyro()
        {
            GamepadGyroMode = Input.GamepadGyroMode.Off;
            GamepadGyroActivation = Input.GamepadGyroActivation.LeftTrigger;
            GamepadGyroSensitivity = 1f;
            GamepadGyroInvertX = false;
            GamepadGyroInvertY = false;
            GamepadGyro.ResetDevice();
        }

        /// <summary>
        /// Restore only the controller bindings and response curve. Mouse,
        /// keyboard, touch and stylus preferences remain untouched so the
        /// settings screen can offer a focused controller reset alongside its
        /// broader reset action.
        /// </summary>
        public static void ResetController()
        {
            ResetControllerBindings();
            ResetControllerGeneral();
            ResetControllerAdvancedTuning();
            ResetControllerGyro();
            InputBalanceTelemetryEnabled = false;
            _controllerPreset = Input.ControllerPreset.Classic;
        }

        private static bool ControllerTuningIsDefault()
        {
            return GamepadMoveDeadZone == .15f
                && GamepadLookDeadZone == .10f
                && GamepadOuterDeadZone == .02f
                && GamepadMoveActivateThreshold == .25f
                && GamepadMoveReleaseThreshold == .18f
                && GamepadLookExponent == 1.60f
                && GamepadLookAntiDeadzone == Input.GamepadLookProcessor.DefaultAntiDeadzone
                && GamepadLookSmoothingSeconds == Input.GamepadLookProcessor.DefaultSmoothingSeconds
                && GamepadResponseCurve == Input.GamepadResponseCurvePreset.Balanced
                && GamepadYawRate == 300
                && GamepadPitchRate == 240
                && GamepadOuterBoostStart == .95f
                && GamepadOuterYawBoost == 150
                && GamepadOuterPitchBoost == 80
                && GamepadBoostDelaySeconds == .18f
                && GamepadBoostRampSeconds == .12f
                && GamepadOuterBoostEnabled
                && GamepadTurnAcceleration == Input.GamepadTurnAccelerationPreset.Standard
                && GamepadTriggerPressThreshold == .20f
                && GamepadTriggerReleaseThreshold == .12f
                && GamepadHorizontalSensitivity == 1
                && GamepadVerticalSensitivity == 1
                && GamepadZoomHorizontalMultiplier == 1
                && GamepadZoomVerticalMultiplier == 1
                && GamepadAutoCalibrationEnabled
                && GamepadStickAimMode == Input.GamepadStickAimMode.Traditional
                && !GamepadGyroEnabled
                && GamepadGyroSensitivity == 1
                && !GamepadGyroInvertX
                && !GamepadGyroInvertY
                && GamepadHapticsEnabled
                && GamepadHapticsStrength == 1
                && !InputBalanceTelemetryEnabled
                && !GamepadInvertY;
        }
    }
}
