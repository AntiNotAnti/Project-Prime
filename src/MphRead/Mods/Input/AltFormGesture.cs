using System;

namespace MphRead.Mods.Input
{
    [Flags]
    public enum AltMoveDirection
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 4,
        Right = 8
    }

    public enum AltFlickAction
    {
        None,
        SamusBoost,
        SpireAttack
    }

    /// <summary>
    /// Shared policy for pointer-driven alternate-form input.
    ///
    /// Rolling hunters consume directional drags as the same Roll binds used by
    /// keyboard, controller and the Android movement stick. Trace, Sylux and
    /// Weavel keep pointer motion for transformed aiming instead.
    /// </summary>
    public static class AltFormGesture
    {
        public static bool UsesRollMovement(Hunter hunter)
            => hunter is Hunter.Samus or Hunter.Kanden or Hunter.Spire or Hunter.Noxus;

        public static AltFlickAction FlickAction(Hunter hunter) => hunter switch
        {
            Hunter.Samus => AltFlickAction.SamusBoost,
            Hunter.Spire => AltFlickAction.SpireAttack,
            _ => AltFlickAction.None
        };

        // At 1x, 24 filtered mouse counts in one fixed step reaches full
        // deflection. The shared swipe sensitivity expands that to 96 counts at
        // 0.25x or contracts it to 6 at 4x without involving normal aim
        // sensitivity.
        public const float MouseDriveFullScale = 24f;

        /// <summary>
        /// Relative mouse movement has no anchor to hold away from centre, so
        /// each simulation step is its own virtual-stick sample. Zero movement
        /// means centre; stopping the mouse therefore stops normal rolling on
        /// the next simulation step, matching the precision touch/pen path.
        /// </summary>
        public static (float X, float Y) MouseDrive(float deltaX, float deltaY,
            float sensitivity)
            => Drive(deltaX, deltaY, deadZone: 0,
                fullScale: MouseDriveFullScale, sensitivity: sensitivity);

        /// <summary>
        /// Convert anchored screen-space displacement to an analogue virtual
        /// stick. The dead zone stays physically stable while sensitivity
        /// changes the travel required to reach full deflection.
        /// </summary>
        public static (float X, float Y) Drive(float x, float y, float deadZone,
            float fullScale, float sensitivity)
        {
            if (!Single.IsFinite(x) || !Single.IsFinite(y)
                || !Single.IsFinite(deadZone) || !Single.IsFinite(fullScale))
            {
                return (0, 0);
            }
            deadZone = MathF.Max(0, deadZone);
            sensitivity = Single.IsFinite(sensitivity)
                ? Math.Clamp(sensitivity, 0.25f, 4f) : 1f;
            fullScale = MathF.Max(deadZone + 0.001f, fullScale / sensitivity);

            float lengthSq = x * x + y * y;
            if (!Single.IsFinite(lengthSq) || lengthSq <= deadZone * deadZone)
            {
                return (0, 0);
            }
            float length = MathF.Sqrt(lengthSq);
            float magnitude = Math.Clamp((length - deadZone) / (fullScale - deadZone), 0, 1);
            return (x / length * magnitude, y / length * magnitude);
        }

        /// <summary>
        /// Direct-control velocity for the normal rolling-speed envelope.
        /// Boosts and other large impulses deliberately fall outside that
        /// envelope so precise swipe steering cannot erase them.
        /// </summary>
        public static bool TryPrecisionVelocity(float currentX, float currentZ,
            float driveX, float driveZ, float normalSpeed, out float x, out float z)
        {
            x = currentX;
            z = currentZ;
            if (!Single.IsFinite(currentX) || !Single.IsFinite(currentZ)
                || !Single.IsFinite(driveX) || !Single.IsFinite(driveZ)
                || !Single.IsFinite(normalSpeed) || normalSpeed <= 0)
            {
                return false;
            }

            float currentSq = currentX * currentX + currentZ * currentZ;
            float controlledLimit = normalSpeed * 1.25f;
            if (!Single.IsFinite(currentSq) || currentSq > controlledLimit * controlledLimit)
            {
                return false;
            }

            float driveSq = driveX * driveX + driveZ * driveZ;
            if (!Single.IsFinite(driveSq))
            {
                return false;
            }
            if (driveSq > 1)
            {
                float inv = 1f / MathF.Sqrt(driveSq);
                driveX *= inv;
                driveZ *= inv;
            }
            x = driveX * normalSpeed;
            z = driveZ * normalSpeed;
            return true;
        }

        /// <summary>
        /// Convert a screen-space drag (X right, Y down) to the engine's
        /// eight-way digital movement surface. Kept for input diagnostics and
        /// any discrete callers; precision swipe movement uses <see cref="Drive"/>.
        /// </summary>
        public static AltMoveDirection Direction(float x, float y, float deadZone)
        {
            if (!Single.IsFinite(x) || !Single.IsFinite(y) || !Single.IsFinite(deadZone))
            {
                return AltMoveDirection.None;
            }
            deadZone = MathF.Max(0, deadZone);
            float lengthSq = x * x + y * y;
            if (!Single.IsFinite(lengthSq) || lengthSq <= deadZone * deadZone)
            {
                return AltMoveDirection.None;
            }

            float angle = MathF.Atan2(-y, x) * (180f / MathF.PI);
            if (angle < 0)
            {
                angle += 360f;
            }

            AltMoveDirection direction = AltMoveDirection.None;
            if (angle > 22.5f && angle < 157.5f)
            {
                direction |= AltMoveDirection.Up;
            }
            if (angle > 202.5f && angle < 337.5f)
            {
                direction |= AltMoveDirection.Down;
            }
            if (angle > 112.5f && angle < 247.5f)
            {
                direction |= AltMoveDirection.Left;
            }
            if (angle < 67.5f || angle > 292.5f)
            {
                direction |= AltMoveDirection.Right;
            }
            return direction;
        }
    }
    /// <summary>
    /// Stable 2D control frame for rolling alternate forms. The rendered
    /// third-person camera may be pushed, shortened or orbited by collision,
    /// but a held movement command follows this bounded virtual yaw instead of
    /// inheriting those physical camera corrections in one simulation step.
    /// </summary>
    public static class AltFormControlBasis
    {
        public const float NormalSlewDegrees = 12f;
        public const float CollisionSlewDegrees = 4f;
        private const float InputDirectionDot = 0.9063078f; // cos(25 degrees)

        public static bool TryNormalize(float x, float z, out float normalX, out float normalZ)
        {
            normalX = 0;
            normalZ = -1;
            if (!Single.IsFinite(x) || !Single.IsFinite(z))
            {
                return false;
            }
            float lengthSq = x * x + z * z;
            if (!Single.IsFinite(lengthSq) || lengthSq < 0.000001f)
            {
                return false;
            }
            float inv = 1f / MathF.Sqrt(lengthSq);
            normalX = x * inv;
            normalZ = z * inv;
            return true;
        }

        public static (float X, float Z) Step(float currentX, float currentZ,
            float desiredX, float desiredZ, bool movementHeld, bool collisionTight)
        {
            if (!TryNormalize(desiredX, desiredZ, out desiredX, out desiredZ))
            {
                if (TryNormalize(currentX, currentZ, out currentX, out currentZ))
                {
                    return (currentX, currentZ);
                }
                return (0, -1);
            }

            // With no movement command there is nothing to destabilise. Snap
            // silently so the next press starts from exactly what the player
            // currently sees.
            if (!movementHeld)
            {
                return (desiredX, desiredZ);
            }

            if (!TryNormalize(currentX, currentZ, out currentX, out currentZ))
            {
                return (desiredX, desiredZ);
            }

            float currentYaw = MathF.Atan2(currentX, currentZ);
            float desiredYaw = MathF.Atan2(desiredX, desiredZ);
            float delta = WrapRadians(desiredYaw - currentYaw);
            float maxStep = MathHelper.DegreesToRadians(
                collisionTight ? CollisionSlewDegrees : NormalSlewDegrees);
            delta = Math.Clamp(delta, -maxStep, maxStep);
            float yaw = currentYaw + delta;
            return (MathF.Sin(yaw), MathF.Cos(yaw));
        }

        /// <summary>
        /// A controller stick has no IsPressed edge. Detect deliberate direction
        /// changes from the analogue vector itself so forward-to-side and
        /// forward-to-back transitions receive the same treatment as new WASD
        /// directions without reacting to tiny stick noise.
        /// </summary>
        public static bool SignificantInputDirectionChange(float previousX, float previousY,
            float currentX, float currentY)
        {
            float currentSq = currentX * currentX + currentY * currentY;
            if (!Single.IsFinite(currentSq) || currentSq < 0.0001f)
            {
                return false;
            }
            float previousSq = previousX * previousX + previousY * previousY;
            if (!Single.IsFinite(previousSq) || previousSq < 0.0001f)
            {
                return true;
            }

            float dot = (previousX * currentX + previousY * currentY)
                / MathF.Sqrt(previousSq * currentSq);
            return !Single.IsFinite(dot) || dot < InputDirectionDot;
        }

        public static float YawDegrees(float x, float z)
        {
            if (!TryNormalize(x, z, out x, out z))
            {
                return 0;
            }
            return MathHelper.RadiansToDegrees(MathF.Atan2(x, z));
        }

        private static float WrapRadians(float value)
        {
            while (value > MathF.PI)
            {
                value -= MathF.PI * 2;
            }
            while (value < -MathF.PI)
            {
                value += MathF.PI * 2;
            }
            return value;
        }
    }

    /// <summary>Opt-in live tracing for rolling alt-form control/camera divergence.</summary>
    public static class AltFormMoveDebug
    {
        public static bool Enabled { get; set; }
        private static int _sample;

        public static void Log(int slot, float cameraX, float cameraZ,
            float desiredX, float desiredZ, float controlX, float controlZ,
            float inputX, float inputY, bool cameraCollision, bool playerCollision,
            byte collisionFrames)
        {
            if (!Enabled || (++_sample % 6) != 0)
            {
                return;
            }
            Console.WriteLine("[altmove] "
                + $"slot={slot} "
                + $"camYaw={AltFormControlBasis.YawDegrees(cameraX, cameraZ):0.0} "
                + $"desiredYaw={AltFormControlBasis.YawDegrees(desiredX, desiredZ):0.0} "
                + $"controlYaw={AltFormControlBasis.YawDegrees(controlX, controlZ):0.0} "
                + $"input=({inputX:0.00},{inputY:0.00}) "
                + $"cameraCollision={(cameraCollision ? 1 : 0)} "
                + $"playerCollision={(playerCollision ? 1 : 0)} "
                + $"tightFrames={collisionFrames}");
        }
    }

}
