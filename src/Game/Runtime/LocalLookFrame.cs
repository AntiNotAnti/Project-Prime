using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// A platform-neutral local look contribution.
    ///
    /// <see cref="Device"/> is the source that owns the frame.  The
    /// coordinator fills <see cref="Contributors"/> with the complete set of
    /// deliberate sources seen before the fixed-step consume.  Keeping that
    /// information in the consumed value prevents a precision event followed
    /// by a stick sample from being mistaken for a controller-only frame.
    /// </summary>
    public readonly struct LocalLookFrame
    {
        public LocalLookFrame(LookDeviceKind device, Vector2 deltaDegrees,
            Vector2 rawDirection, float magnitude)
            : this(device, deltaDegrees, rawDirection, magnitude, device,
                aimAssistEnabled: false, aimAssistStrength: 0,
                controllerDeltaDegrees: device == LookDeviceKind.GamepadStick
                    ? deltaDegrees : Vector2.Zero,
                precisionDeltaDegrees: device.IsPrecision()
                    ? deltaDegrees : Vector2.Zero,
                mouseDeltaDegrees: device == LookDeviceKind.Mouse
                    ? deltaDegrees : Vector2.Zero,
                touchDeltaDegrees: device == LookDeviceKind.Touch
                    ? deltaDegrees : Vector2.Zero,
                stylusDeltaDegrees: device == LookDeviceKind.Stylus
                    ? deltaDegrees : Vector2.Zero,
                rawMouseDelta: device == LookDeviceKind.Mouse
                    ? rawDirection : Vector2.Zero)
        {
        }

        internal LocalLookFrame(LookDeviceKind device, Vector2 deltaDegrees,
            Vector2 rawDirection, float magnitude, Vector2 controllerDeltaDegrees,
            Vector2 precisionDeltaDegrees)
            : this(device, deltaDegrees, rawDirection, magnitude, device,
                aimAssistEnabled: false, aimAssistStrength: 0,
                controllerDeltaDegrees: controllerDeltaDegrees,
                precisionDeltaDegrees: precisionDeltaDegrees,
                mouseDeltaDegrees: Vector2.Zero,
                touchDeltaDegrees: Vector2.Zero,
                stylusDeltaDegrees: Vector2.Zero,
                rawMouseDelta: Vector2.Zero)
        {
        }

        internal LocalLookFrame(LookDeviceKind device, Vector2 deltaDegrees,
            Vector2 rawDirection, float magnitude, Vector2 controllerDeltaDegrees,
            Vector2 mouseDeltaDegrees, Vector2 touchDeltaDegrees,
            Vector2 stylusDeltaDegrees, Vector2 rawMouseDelta = default)
            : this(device, deltaDegrees, rawDirection, magnitude, device,
                aimAssistEnabled: false, aimAssistStrength: 0,
                controllerDeltaDegrees: controllerDeltaDegrees,
                precisionDeltaDegrees: mouseDeltaDegrees + touchDeltaDegrees
                    + stylusDeltaDegrees,
                mouseDeltaDegrees: mouseDeltaDegrees,
                touchDeltaDegrees: touchDeltaDegrees,
                stylusDeltaDegrees: stylusDeltaDegrees,
                rawMouseDelta: rawMouseDelta)
        {
        }

        private LocalLookFrame(LookDeviceKind device, Vector2 deltaDegrees,
            Vector2 rawDirection, float magnitude, LookDeviceKind contributors,
            bool aimAssistEnabled, float aimAssistStrength,
            Vector2 controllerDeltaDegrees, Vector2 precisionDeltaDegrees,
            Vector2 mouseDeltaDegrees, Vector2 touchDeltaDegrees,
            Vector2 stylusDeltaDegrees, Vector2 rawMouseDelta)
        {
            Device = device;
            DeltaDegrees = deltaDegrees;
            RawDirection = rawDirection;
            Magnitude = magnitude;
            Contributors = contributors;
            AimAssistEnabled = aimAssistEnabled;
            AimAssistStrength = float.IsFinite(aimAssistStrength)
                ? System.Math.Clamp(aimAssistStrength, 0, 1) : 0;
            ControllerDeltaDegrees = controllerDeltaDegrees;
            PrecisionDeltaDegrees = precisionDeltaDegrees;
            MouseDeltaDegrees = mouseDeltaDegrees;
            TouchDeltaDegrees = touchDeltaDegrees;
            StylusDeltaDegrees = stylusDeltaDegrees;
            RawMouseDelta = rawMouseDelta;
        }

        public LookDeviceKind Device { get; }
        public Vector2 DeltaDegrees { get; }
        public Vector2 RawDirection { get; }
        public float Magnitude { get; }
        public LookDeviceKind Contributors { get; }
        /// <summary>Stateful controller component of the aggregate delta.</summary>
        public Vector2 ControllerDeltaDegrees { get; }
        /// <summary>Relative precision-device component of the aggregate delta.</summary>
        public Vector2 PrecisionDeltaDegrees { get; }
        public Vector2 MouseDeltaDegrees { get; }
        public Vector2 TouchDeltaDegrees { get; }
        public Vector2 StylusDeltaDegrees { get; }
        /// <summary>Untransformed relative mouse pixels for gesture detection.</summary>
        public Vector2 RawMouseDelta { get; }
        public bool AimAssistEnabled { get; }
        public float AimAssistStrength { get; }

        public bool HasPrecisionContributor
            => (Contributors & (LookDeviceKind.Mouse | LookDeviceKind.Touch
                | LookDeviceKind.Stylus)) != 0;

        public bool HasControllerContributor
            => (Contributors & (LookDeviceKind.GamepadStick | LookDeviceKind.GamepadGyro)) != 0;

        public bool IsFinite
            => float.IsFinite(DeltaDegrees.X) && float.IsFinite(DeltaDegrees.Y)
                && float.IsFinite(RawDirection.X) && float.IsFinite(RawDirection.Y)
                && float.IsFinite(Magnitude)
                && float.IsFinite(ControllerDeltaDegrees.X)
                && float.IsFinite(ControllerDeltaDegrees.Y)
                && float.IsFinite(PrecisionDeltaDegrees.X)
                && float.IsFinite(PrecisionDeltaDegrees.Y)
                && float.IsFinite(MouseDeltaDegrees.X)
                && float.IsFinite(MouseDeltaDegrees.Y)
                && float.IsFinite(TouchDeltaDegrees.X)
                && float.IsFinite(TouchDeltaDegrees.Y)
                && float.IsFinite(StylusDeltaDegrees.X)
                && float.IsFinite(StylusDeltaDegrees.Y)
                && float.IsFinite(RawMouseDelta.X)
                && float.IsFinite(RawMouseDelta.Y);

        public LocalLookFrame WithContributors(LookDeviceKind contributors)
            => new(Device, DeltaDegrees, RawDirection, Magnitude, contributors,
                AimAssistEnabled, AimAssistStrength, ControllerDeltaDegrees,
                PrecisionDeltaDegrees, MouseDeltaDegrees, TouchDeltaDegrees,
                StylusDeltaDegrees, RawMouseDelta);

        public LocalLookFrame WithAimAssist(bool enabled, float strength)
            => new(Device, DeltaDegrees, RawDirection, Magnitude, Contributors,
                enabled, strength, ControllerDeltaDegrees, PrecisionDeltaDegrees,
                MouseDeltaDegrees, TouchDeltaDegrees, StylusDeltaDegrees,
                RawMouseDelta);

        public LocalLookFrame WithDelta(Vector2 deltaDegrees)
            => new(Device, deltaDegrees, RawDirection, Magnitude, Contributors,
                AimAssistEnabled, AimAssistStrength, ControllerDeltaDegrees,
                PrecisionDeltaDegrees, MouseDeltaDegrees, TouchDeltaDegrees,
                StylusDeltaDegrees, RawMouseDelta);

        public LocalLookFrame WithComponents(Vector2 controllerDeltaDegrees,
            Vector2 precisionDeltaDegrees)
            => new(Device, controllerDeltaDegrees + precisionDeltaDegrees,
                RawDirection, Magnitude, Contributors, AimAssistEnabled,
                AimAssistStrength, controllerDeltaDegrees, precisionDeltaDegrees,
                MouseDeltaDegrees, TouchDeltaDegrees, StylusDeltaDegrees,
                RawMouseDelta);

        public static LocalLookFrame Empty => default;
    }

    internal static class LookDeviceKindExtensions
    {
        public static bool IsPrecision(this LookDeviceKind device)
            => (device & (LookDeviceKind.Mouse | LookDeviceKind.Touch
                | LookDeviceKind.Stylus)) != 0;
    }
}
