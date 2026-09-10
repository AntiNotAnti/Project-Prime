using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Tracks deliberate look ownership.  Movement buttons and controller
    /// connection state are intentionally absent: only an actual look sample
    /// can change the active source.
    /// </summary>
    public struct LookDeviceTracker
    {
        private LookDeviceKind _active;

        public readonly LookDeviceKind ActiveLookDevice => _active;
        public readonly bool IsControllerLook
            => _active is LookDeviceKind.GamepadStick or LookDeviceKind.GamepadGyro;
        public readonly bool IsPrecisionLook
            => _active is LookDeviceKind.Mouse or LookDeviceKind.Touch or LookDeviceKind.Stylus;

        /// <summary>Observe a source with a caller-provided processed magnitude.</summary>
        public bool Observe(LookDeviceKind device, float magnitude,
            float gamepadLookDeadzone = 0.10f)
        {
            if (!IsDeliberate(device, magnitude, gamepadLookDeadzone))
            {
                return false;
            }
            _active = device;
            return true;
        }

        /// <summary>Observe a vector contribution using its radial magnitude.</summary>
        public bool Observe(LookDeviceKind device, Vector2 contribution,
            float gamepadLookDeadzone = 0.10f)
        {
            if (!float.IsFinite(contribution.X) || !float.IsFinite(contribution.Y))
            {
                return false;
            }
            return Observe(device, contribution.Length, gamepadLookDeadzone);
        }

        /// <summary>
        /// Claim a source when the caller has already established that its
        /// event is deliberate. Useful for a stylus contact with zero initial
        /// delta, which still owns aim without snapping the camera.
        /// </summary>
        public bool Claim(LookDeviceKind device)
        {
            if (device == LookDeviceKind.None || !IsSingleDevice(device))
            {
                return false;
            }
            _active = device;
            return true;
        }

        public void Reset() => _active = LookDeviceKind.None;

        public static bool IsPrecisionDevice(LookDeviceKind device)
            => (device & (LookDeviceKind.Mouse | LookDeviceKind.Touch
                | LookDeviceKind.Stylus)) != 0;

        public static bool IsControllerDevice(LookDeviceKind device)
            => (device & (LookDeviceKind.GamepadStick | LookDeviceKind.GamepadGyro)) != 0;

        public static bool IsSingleDevice(LookDeviceKind device)
        {
            int value = (int)device;
            return value > 0 && (value & (value - 1)) == 0;
        }

        private static bool IsDeliberate(LookDeviceKind device, float magnitude,
            float gamepadLookDeadzone)
        {
            if (!IsSingleDevice(device) || !float.IsFinite(magnitude)
                || magnitude <= 0)
            {
                return false;
            }
            if (device == LookDeviceKind.GamepadStick)
            {
                if (!float.IsFinite(gamepadLookDeadzone))
                {
                    gamepadLookDeadzone = 0.10f;
                }
                return magnitude > Math.Clamp(gamepadLookDeadzone, 0, 1);
            }
            // A pixel/point/degree event below this is numerical noise, not
            // deliberate motion. Stylus contact can use Claim() separately.
            return magnitude > 1e-6f;
        }
    }
}
