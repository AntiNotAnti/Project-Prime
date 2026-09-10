using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Gameplay-neutral Morph Ball boost activation.</summary>
    public enum BoostActivation : byte
    {
        None = 0,
        Charge = 1,
        Flick = 2
    }

    /// <summary>
    /// A deterministic boost request. Screen-space X is positive to the right
    /// and Y is positive downward. Flick directions are normalized, quantized
    /// once to signed bytes in -127..127, and decoded back to unit length for
    /// both local and authoritative simulation.
    /// </summary>
    public readonly record struct BoostIntent
    {
        private BoostIntent(BoostActivation activation, sbyte x, sbyte y)
        {
            Activation = activation;
            X = x;
            Y = y;
        }

        public BoostActivation Activation { get; }
        public sbyte X { get; }
        public sbyte Y { get; }
        public bool IsFlick => Activation == BoostActivation.Flick;

        public Vector2 Direction
        {
            get
            {
                if (!IsFlick) return Vector2.Zero;
                var direction = new Vector2(X / 127f, Y / 127f);
                return direction.LengthSquared > 0 ? direction.Normalized() : Vector2.Zero;
            }
        }

        public static BoostIntent None => default;
        public static BoostIntent Charge => new(BoostActivation.Charge, 0, 0);

        public static bool TryCreateFlick(Vector2 screenDirection, out BoostIntent intent)
        {
            intent = default;
            if (!float.IsFinite(screenDirection.X) || !float.IsFinite(screenDirection.Y)
                || !(screenDirection.LengthSquared > 0))
            {
                return false;
            }
            Vector2 normalized = screenDirection.Normalized();
            int x = (int)MathF.Round(normalized.X * 127f, MidpointRounding.AwayFromZero);
            int y = (int)MathF.Round(normalized.Y * 127f, MidpointRounding.AwayFromZero);
            x = Math.Clamp(x, -127, 127);
            y = Math.Clamp(y, -127, 127);
            if (x == 0 && y == 0) return false;
            intent = new BoostIntent(BoostActivation.Flick, (sbyte)x, (sbyte)y);
            return true;
        }

        public static bool TryDecode(BoostActivation activation, sbyte x, sbyte y,
            out BoostIntent intent)
        {
            intent = default;
            if (x == SByte.MinValue || y == SByte.MinValue)
            {
                return false;
            }
            if (activation == BoostActivation.Flick)
            {
                if (x == 0 && y == 0) return false;
                intent = new BoostIntent(activation, x, y);
                return true;
            }
            if ((activation is BoostActivation.None or BoostActivation.Charge)
                && x == 0 && y == 0)
            {
                intent = activation == BoostActivation.Charge ? Charge : None;
                return true;
            }
            return false;
        }
    }
}
