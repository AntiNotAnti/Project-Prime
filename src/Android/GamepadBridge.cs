using System;
using Android.Views;
using MphRead.Mods.Input;

namespace MphRead.Droid
{
    /// <summary>
    /// A pad on Android, turned into the same state the desktop polls.
    ///
    /// Android has no polling API for controllers: a pad arrives as ordinary
    /// key events for its buttons and <see cref="MotionEvent"/>s for its
    /// sticks and triggers, delivered to whichever view has focus. So this
    /// accumulates them into <see cref="GamepadInput.State"/>, which the
    /// shared mapping then reads exactly as it reads the desktop's -- one
    /// mapping, one set of dead zones, one answer to what a button means.
    ///
    /// Bluetooth needs nothing of its own here. A pad paired with the phone is
    /// an input device like any other by the time events reach an app; the
    /// only difference from USB is which driver produced them, and that is
    /// decided well below this.
    /// </summary>
    internal static class GamepadBridge
    {
        private static ControllerCapabilityOwner? _capabilityOwner;
        private static readonly ControllerDeviceLifecycle _devices = new();
        private static readonly ControllerDeviceMetadataCache _metadata = new();

        /// <summary>Sources that mean "this came from a pad and not a keyboard".</summary>
        internal static bool IsGamepad(InputSourceType source)
        {
            return (source & InputSourceType.Gamepad) == InputSourceType.Gamepad
                || (source & InputSourceType.Joystick) == InputSourceType.Joystick
                || (source & InputSourceType.Dpad) == InputSourceType.Dpad;
        }

        /// <summary>
        /// A button, up or down. Returns false when the event was not a pad's,
        /// so the caller can carry on and offer it to the keyboard handling.
        /// </summary>
        public static bool HandleKey(Keycode keyCode, KeyEvent? e, bool down)
        {
            if (e == null || !IsGamepad(e.Source))
            {
                return false;
            }
            GamepadButtons button = Map(keyCode);
            if (button == GamepadButtons.None)
            {
                return false;
            }
            if (!BeginDevice(e.DeviceId))
            {
                return false;
            }
            // A held button repeats: Android sends a stream of downs with a
            // rising repeat count, and letting those through would be
            // harmless for a held bind and wrong for a tapped one -- the
            // rising edge is worked out from this state, so a repeat that
            // arrived as a fresh press would fire twice.
            if (down && e.RepeatCount > 0)
            {
                return true;
            }
            GamepadState state = GamepadInput.State;
            state.Connected = true;
            state.Name ??= "gamepad";
            if (down)
            {
                state.Buttons |= button;
            }
            else
            {
                state.Buttons &= ~button;
            }
            GamepadInput.State = state;
            return true;
        }

        /// <summary>
        /// The sticks, the triggers and the hat. Returns false when the event
        /// was not a pad's.
        /// </summary>
        public static bool HandleMotion(MotionEvent? e)
        {
            if (e == null || !IsGamepad(e.Source)
                || e.Action != MotionEventActions.Move)
            {
                return false;
            }
            if (!BeginDevice(e.DeviceId))
            {
                return false;
            }
            GamepadState state = GamepadInput.State;
            state.Connected = true;
            state.Name ??= "gamepad";
            state.LeftX = e.GetAxisValue(Axis.X);
            // Negated, as on the desktop: Android reports a stick pushed
            // forward as -1 and everything above wants forward positive.
            state.LeftY = -e.GetAxisValue(Axis.Y);
            // Z/RZ is what almost every pad reports its right stick on;
            // RX/RY is the older convention some still use, and a pad that
            // uses neither has no right stick to read.
            state.RightX = Pick(e, Axis.Z, Axis.Rx);
            state.RightY = -Pick(e, Axis.Rz, Axis.Ry);
            // Two names for one pedal: LTRIGGER/RTRIGGER is the joystick
            // convention and BRAKE/GAS is the one Android's own gamepad
            // documentation uses. Pads report one or the other.
            state.LeftTrigger = Pick(e, Axis.Ltrigger, Axis.Brake);
            state.RightTrigger = Pick(e, Axis.Rtrigger, Axis.Gas);
            GamepadButtons buttons = state.Buttons
                & ~(GamepadButtons.DpadUp | GamepadButtons.DpadDown
                    | GamepadButtons.DpadLeft | GamepadButtons.DpadRight);
            // The d-pad arrives as a hat on most pads and as key events on the
            // rest, so both paths set the same four flags. Clearing them above
            // is what makes the hat authoritative once one has been seen --
            // a pad that sends keys never moves the hat off zero, so nothing
            // is lost by it.
            float hatX = e.GetAxisValue(Axis.HatX);
            float hatY = e.GetAxisValue(Axis.HatY);
            if (hatX < -HatPress)
            {
                buttons |= GamepadButtons.DpadLeft;
            }
            else if (hatX > HatPress)
            {
                buttons |= GamepadButtons.DpadRight;
            }
            if (hatY < -HatPress)
            {
                buttons |= GamepadButtons.DpadUp;
            }
            else if (hatY > HatPress)
            {
                buttons |= GamepadButtons.DpadDown;
            }
            state.Buttons = buttons;
            GamepadInput.State = state;
            return true;
        }

        /// <summary>
        /// Start accepting callbacks for one Activity lifetime. Input events
        /// received before this call or after <see cref="Shutdown"/> are
        /// ignored, including callbacks queued by Android before unregister.
        /// </summary>
        internal static void Activate() => _devices.Activate();

        /// <summary>
        /// End an Activity input epoch while retaining the backend owner for a
        /// possible resume. The active state is cleared before publishing the
        /// transition so no held input can latch through a pause.
        /// </summary>
        internal static void Deactivate()
        {
            _devices.Deactivate();
            _metadata.Clear();
            GamepadInput.State = default;
            _capabilityOwner?.Publish(
                ControllerCapabilitySnapshot.Disconnected(ControllerBackend.Android));
        }

        /// <summary>
        /// End the final Activity epoch and release the Android capability
        /// owner. Unlike <see cref="Deactivate"/>, this makes queued callbacks
        /// harmless until a new Activity explicitly calls <see cref="Activate"/>.
        /// </summary>
        internal static void Shutdown()
        {
            _devices.Deactivate();
            _metadata.Clear();
            GamepadInput.State = default;
            _capabilityOwner?.Dispose();
            _capabilityOwner = null;
        }

        /// <summary>Called by the Activity's input-device listener.</summary>
        internal static void OnInputDeviceAdded(InputDevice? device)
        {
            if (device == null || !IsGamepad(device.Sources)) return;
            ControllerDeviceTransition transition = _devices.ObserveAdded(device.Id);
            if (transition.Kind is not (ControllerDeviceTransitionKind.Connected
                or ControllerDeviceTransitionKind.Changed)) return;
            if (transition.Kind == ControllerDeviceTransitionKind.Connected)
            {
                _metadata.Clear();
                GamepadInput.State = default;
            }
            PublishCapabilities(device.Id, device);
        }

        /// <summary>Refresh the active device's capabilities after Android changes it.</summary>
        internal static void OnInputDeviceChanged(InputDevice? device, int deviceId)
        {
            if (device == null || !IsGamepad(device.Sources))
            {
                OnInputDeviceRemoved(deviceId);
                return;
            }
            ControllerDeviceTransition transition = _devices.ObserveChanged(deviceId);
            if (transition.Kind == ControllerDeviceTransitionKind.Changed)
            {
                PublishCapabilities(deviceId, device);
            }
        }

        /// <summary>Clear the active device immediately when Android reports removal.</summary>
        internal static void OnInputDeviceRemoved(int deviceId)
        {
            ControllerDeviceTransition transition = _devices.ObserveRemoved(deviceId);
            if (transition.Kind != ControllerDeviceTransitionKind.Disconnected) return;
            _metadata.Clear();
            GamepadInput.State = default;
            _capabilityOwner?.Publish(
                ControllerCapabilitySnapshot.Disconnected(ControllerBackend.Android));
        }

        /// <summary>
        /// Mark the Android input source unavailable without changing any
        /// persisted preference. Activity pause/focus callbacks use this for
        /// the interval in which Android may stop delivering device events.
        /// The owner remains alive so a later event can publish the same
        /// backend without creating a competing publisher.
        /// </summary>
        internal static void Disconnect()
        {
            _devices.Disconnect();
            _metadata.Clear();
            GamepadInput.State = default;
            _capabilityOwner?.Publish(
                ControllerCapabilitySnapshot.Disconnected(ControllerBackend.Android));
        }

        private static bool BeginDevice(int deviceId)
        {
            ControllerDeviceTransition transition = _devices.ObserveInput(deviceId);
            if (transition.Kind == ControllerDeviceTransitionKind.Ignored)
            {
                return false;
            }
            if (transition.Kind is ControllerDeviceTransitionKind.Connected
                or ControllerDeviceTransitionKind.Replaced)
            {
                // Do not carry button edges or axes from a removed pad into a
                // replacement. The capability transition below is what tells
                // observers that the physical source changed.
                _metadata.Clear();
                GamepadInput.State = default;
            }
            PublishCapabilities(deviceId, device: null);
            return true;
        }

        private static void PublishCapabilities(int deviceId, InputDevice? device)
        {
            if (_capabilityOwner?.IsCurrent != true)
            {
                _capabilityOwner = ControllerCapabilities.TryAcquire(
                    ControllerBackend.Android, replaceCurrent: true);
            }
            ControllerDeviceMetadata metadata;
            if (device is not null)
            {
                metadata = _metadata.Remember(
                    deviceId, device.Name, TryHasAnalogTriggers(device));
            }
            else if (!_metadata.TryGet(deviceId, out metadata))
            {
                metadata = new ControllerDeviceMetadata(deviceId, null, null);
            }
            _capabilityOwner?.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Android,
                $"device:{deviceId}",
                metadata.DeviceName ?? "Android gamepad",
                ControllerFamily.Generic,
                hasGyroscope: null,
                hasRumble: null,
                hasAnalogTriggers: metadata.HasAnalogTriggers));
        }

        private static bool? TryHasAnalogTriggers(InputDevice? device)
        {
            if (device == null) return null;
            try
            {
                bool left = device.GetMotionRange(Axis.Ltrigger) != null
                    || device.GetMotionRange(Axis.Brake) != null;
                bool right = device.GetMotionRange(Axis.Rtrigger) != null
                    || device.GetMotionRange(Axis.Gas) != null;
                return left && right;
            }
            catch (Exception)
            {
                // The device may disappear between the listener callback and
                // its property query; unknown is safer than guessing.
                return null;
            }
        }

        /// <summary>A hat is -1, 0 or 1; half is well clear of either edge.</summary>
        private const float HatPress = 0.5f;

        private static float Pick(MotionEvent e, Axis first, Axis second)
        {
            float value = e.GetAxisValue(first);
            return value != 0 ? value : e.GetAxisValue(second);
        }

        private static GamepadButtons Map(Keycode code)
        {
            return code switch
            {
                Keycode.ButtonA => GamepadButtons.A,
                Keycode.ButtonB => GamepadButtons.B,
                Keycode.ButtonX => GamepadButtons.X,
                Keycode.ButtonY => GamepadButtons.Y,
                Keycode.ButtonL1 => GamepadButtons.LeftBumper,
                Keycode.ButtonR1 => GamepadButtons.RightBumper,
                // Some pads report the triggers as buttons and never send an
                // axis for them; the axis path above sets the same flags.
                Keycode.ButtonL2 => GamepadButtons.LeftTrigger,
                Keycode.ButtonR2 => GamepadButtons.RightTrigger,
                Keycode.ButtonSelect => GamepadButtons.Back,
                Keycode.ButtonStart => GamepadButtons.Start,
                Keycode.ButtonThumbl => GamepadButtons.LeftThumb,
                Keycode.ButtonThumbr => GamepadButtons.RightThumb,
                Keycode.DpadUp => GamepadButtons.DpadUp,
                Keycode.DpadDown => GamepadButtons.DpadDown,
                Keycode.DpadLeft => GamepadButtons.DpadLeft,
                Keycode.DpadRight => GamepadButtons.DpadRight,
                _ => GamepadButtons.None
            };
        }
    }
}
