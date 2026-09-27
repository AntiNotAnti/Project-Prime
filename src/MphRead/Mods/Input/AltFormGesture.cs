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
        SpireMomentum
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

        // Samus deliberately keeps cartridge-style morph-ball inertia. The
        // precision velocity owner remains available for the other rolling
        // forms, where it is useful for direct pointer positioning.
        public static bool UsesPrecisionSwipe(Hunter hunter)
            => hunter is Hunter.Kanden or Hunter.Spire or Hunter.Noxus;

        public static AltFlickAction FlickAction(Hunter hunter) => hunter switch
        {
            Hunter.Samus => AltFlickAction.SamusBoost,
            Hunter.Spire => AltFlickAction.SpireMomentum,
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
        /// means centre. Precision-drive hunters stop controlled rolling there;
        /// Samus instead stops applying traction and keeps stock morph-ball inertia.
        /// </summary>
        public static (float X, float Y) MouseDrive(float deltaX, float deltaY,
            float sensitivity)
            => Drive(deltaX, deltaY, deadZone: 0,
                fullScale: MouseDriveFullScale, sensitivity: sensitivity);

        /// <summary>
        /// Stock-style rolling input from movement that happened in this
        /// simulation step. Unlike <see cref="Drive"/>, the axes are scaled
        /// independently and are never radially normalized: a diagonal swipe
        /// can therefore request full forward and full lateral traction, just
        /// like holding two native Roll directions. There is no dead zone or
        /// temporal carry. When the pointer stops, this returns zero and the
        /// morph ball's own inertia/damping remains in charge.
        /// </summary>
        public static (float X, float Y) StockRollDrive(float deltaX, float deltaY,
            float fullScale, float sensitivity)
        {
            if (!Single.IsFinite(deltaX) || !Single.IsFinite(deltaY)
                || !Single.IsFinite(fullScale) || fullScale <= 0)
            {
                return (0, 0);
            }
            sensitivity = Single.IsFinite(sensitivity)
                ? Math.Clamp(sensitivity, 0.25f, 4f) : 1f;
            float scale = sensitivity / MathF.Max(fullScale, 0.001f);
            return (Math.Clamp(deltaX * scale, -1, 1),
                Math.Clamp(deltaY * scale, -1, 1));
        }

        public static (float X, float Y) StockRollMouseDrive(float deltaX,
            float deltaY, float sensitivity)
            => StockRollDrive(deltaX, deltaY, MouseDriveFullScale, sensitivity);

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
    /// True when a movement vector has changed enough to count as a deliberate
    /// new direction. Controller sticks do not produce IsPressed edges, and a
    /// keyboard diagonal can change just by releasing one key, so rolling alt
    /// movement compares the vectors themselves.
    /// </summary>
    public static class AltFormInputDirection
    {
        private const float DirectionDot = 0.9063078f; // cos(25 degrees)

        public static bool SignificantChange(float previousX, float previousY,
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
            return !Single.IsFinite(dot) || dot < DirectionDot;
        }

        public static float YawDegrees(float x, float z)
        {
            float sq = x * x + z * z;
            if (!Single.IsFinite(sq) || sq < 0.000001f)
            {
                return 0;
            }
            return MathF.Atan2(x, z) * (180f / MathF.PI);
        }
    }

    /// <summary>Opt-in live tracing for rolling alt-form camera/basis divergence.</summary>
    public static class AltFormMoveDebug
    {
        public static bool Enabled { get; set; }
        private static int _sample;

        public static void Log(int slot, float cameraX, float cameraZ,
            float basisX, float basisZ, float inputX, float inputY,
            bool cameraCollision, bool playerCollision, bool locked,
            byte clearFrames, bool directionChanged)
        {
            if (!Enabled || (++_sample % 6) != 0)
            {
                return;
            }
            Console.WriteLine("[altmove] "
                + $"slot={slot} "
                + $"camYaw={AltFormInputDirection.YawDegrees(cameraX, cameraZ):0.0} "
                + $"basisYaw={AltFormInputDirection.YawDegrees(basisX, basisZ):0.0} "
                + $"input=({inputX:0.00},{inputY:0.00}) "
                + $"cameraCollision={(cameraCollision ? 1 : 0)} "
                + $"playerCollision={(playerCollision ? 1 : 0)} "
                + $"locked={(locked ? 1 : 0)} clear={clearFrames} "
                + $"inputChanged={(directionChanged ? 1 : 0)}");
        }
    }

}
