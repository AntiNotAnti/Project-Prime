using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    [Flags]
    public enum GamepadMovementDirection
    {
        None = 0,
        Up = 1 << 0,
        Right = 1 << 1,
        Down = 1 << 2,
        Left = 1 << 3
    }

    /// <summary>One fixed-step, eight-way movement result.</summary>
    public readonly struct GamepadMovementSample
    {
        public GamepadMovementSample(GamepadMovementDirection direction,
            Vector2 vector, float rawMagnitude, bool active)
        {
            Direction = direction;
            Vector = vector;
            RawMagnitude = rawMagnitude;
            Active = active;
        }

        public GamepadMovementDirection Direction { get; }
        public Vector2 Vector { get; }
        public float RawMagnitude { get; }
        public bool Active { get; }
        public bool IsActive => Active;
    }

    /// <summary>
    /// Converts a raw left stick into a digital eight-way direction.  The
    /// activation and release hysteresis intentionally use raw radial
    /// magnitude; the inner deadzone only affects the direction calculation.
    /// </summary>
    public struct GamepadMovementProcessor
    {
        public const float DefaultInnerDeadzone = 0.15f;
        public const float DefaultActivateThreshold = 0.25f;
        public const float DefaultReleaseThreshold = 0.18f;
        public const float DefaultDirectionHysteresisDegrees = 6;

        private const float SectorDegrees = 45;
        private const float HalfSectorDegrees = SectorDegrees / 2;

        private bool _active;
        private int _sector;

        public GamepadMovementProcessor()
            : this(DefaultInnerDeadzone, DefaultActivateThreshold,
                DefaultReleaseThreshold, DefaultDirectionHysteresisDegrees)
        {
        }

        public GamepadMovementProcessor(float innerDeadzone = DefaultInnerDeadzone,
            float activateThreshold = DefaultActivateThreshold,
            float releaseThreshold = DefaultReleaseThreshold,
            float directionHysteresisDegrees = DefaultDirectionHysteresisDegrees)
        {
            InnerDeadzone = Sanitize(innerDeadzone, DefaultInnerDeadzone);
            ActivateThreshold = Sanitize(activateThreshold, DefaultActivateThreshold);
            ReleaseThreshold = Math.Clamp(Sanitize(releaseThreshold, DefaultReleaseThreshold),
                0, ActivateThreshold);
            DirectionHysteresisDegrees = SanitizeDirectionHysteresis(
                directionHysteresisDegrees);
            _active = false;
            _sector = -1;
        }

        public float InnerDeadzone { get; private set; }
        public float ActivateThreshold { get; private set; }
        public float ReleaseThreshold { get; private set; }
        public float DirectionHysteresisDegrees { get; private set; }
        public readonly bool Active => _active;
        private GamepadMovementSample _processed;
        public readonly GamepadMovementSample Processed => _processed;

        public GamepadMovementSample Process(Vector2 raw)
        {
            if (!float.IsFinite(raw.X) || !float.IsFinite(raw.Y))
            {
                Reset();
                return default;
            }

            float rawMagnitude = raw.Length;
            if (!float.IsFinite(rawMagnitude))
            {
                Reset();
                return default;
            }
            if (!_active)
            {
                if (rawMagnitude >= ActivateThreshold)
                {
                    _active = true;
                }
            }
            else if (rawMagnitude < ReleaseThreshold)
            {
                _active = false;
            }

            if (!_active)
            {
                _sector = -1;
                return _processed = new GamepadMovementSample(GamepadMovementDirection.None,
                    Vector2.Zero, MathF.Min(rawMagnitude, 1), false);
            }

            StickSample processed = StickProcessor.Evaluate(raw, InnerDeadzone, 0);
            if (!processed.IsActive)
            {
                // This can only happen for an unusual custom threshold below
                // the inner deadzone. Preserve hysteresis but do not invent a
                // direction from a neutral stick.
                return _processed = new GamepadMovementSample(GamepadMovementDirection.None,
                    Vector2.Zero, MathF.Min(rawMagnitude, 1), true);
            }
            _sector = QuantizeSector(processed.Direction, _sector,
                DirectionHysteresisDegrees);
            GamepadMovementDirection direction = FromSector(_sector);
            return _processed = new GamepadMovementSample(direction, ToVector(direction),
                MathF.Min(rawMagnitude, 1), true);
        }

        public void Reset()
        {
            _active = false;
            _sector = -1;
            _processed = default;
        }

        /// <summary>Refresh settings without discarding hysteresis state.</summary>
        public void Configure(float innerDeadzone, float activateThreshold,
            float releaseThreshold,
            float directionHysteresisDegrees = DefaultDirectionHysteresisDegrees)
        {
            InnerDeadzone = Sanitize(innerDeadzone, DefaultInnerDeadzone);
            ActivateThreshold = Sanitize(activateThreshold, DefaultActivateThreshold);
            ReleaseThreshold = Math.Clamp(Sanitize(releaseThreshold, DefaultReleaseThreshold),
                0, ActivateThreshold);
            DirectionHysteresisDegrees = SanitizeDirectionHysteresis(
                directionHysteresisDegrees);
        }

        /// <summary>Quantize a unit direction to the nearest 45-degree sector.</summary>
        public static GamepadMovementDirection Quantize(Vector2 direction)
        {
            if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)
                || direction.LengthSquared <= 0)
            {
                return GamepadMovementDirection.None;
            }
            return FromSector(NearestSector(direction));
        }

        private static int QuantizeSector(Vector2 direction, int previousSector,
            float hysteresisDegrees)
        {
            int nearest = NearestSector(direction);
            if ((uint)previousSector >= 8 || nearest == previousSector)
            {
                return nearest;
            }

            float angleDegrees = MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI;
            float previousCenter = previousSector * SectorDegrees;
            float delta = WrapDegrees(angleDegrees - previousCenter);
            return MathF.Abs(delta) <= HalfSectorDegrees + hysteresisDegrees
                ? previousSector : nearest;
        }

        private static int NearestSector(Vector2 direction)
        {
            float angle = MathF.Atan2(direction.Y, direction.X);
            int sector = (int)MathF.Floor((angle + MathF.PI / 8) / (MathF.PI / 4));
            return ((sector % 8) + 8) % 8;
        }

        private static GamepadMovementDirection FromSector(int sector)
        {
            return sector switch
            {
                0 => GamepadMovementDirection.Right,
                1 => GamepadMovementDirection.Up | GamepadMovementDirection.Right,
                2 => GamepadMovementDirection.Up,
                3 => GamepadMovementDirection.Up | GamepadMovementDirection.Left,
                4 => GamepadMovementDirection.Left,
                5 => GamepadMovementDirection.Down | GamepadMovementDirection.Left,
                6 => GamepadMovementDirection.Down,
                _ => GamepadMovementDirection.Down | GamepadMovementDirection.Right
            };
        }

        private static float WrapDegrees(float degrees)
        {
            degrees %= 360;
            if (degrees > 180)
            {
                degrees -= 360;
            }
            else if (degrees < -180)
            {
                degrees += 360;
            }
            return degrees;
        }

        private static Vector2 ToVector(GamepadMovementDirection direction)
        {
            float x = (direction & GamepadMovementDirection.Right) != 0 ? 1
                : (direction & GamepadMovementDirection.Left) != 0 ? -1 : 0;
            float y = (direction & GamepadMovementDirection.Up) != 0 ? 1
                : (direction & GamepadMovementDirection.Down) != 0 ? -1 : 0;
            return new Vector2(x, y);
        }

        private static float Sanitize(float value, float fallback)
            => !float.IsFinite(value) ? fallback : Math.Clamp(value, 0, 1);

        private static float SanitizeDirectionHysteresis(float value)
            => !float.IsFinite(value) ? DefaultDirectionHysteresisDegrees
                : Math.Clamp(value, 0, HalfSectorDegrees);
    }
}
