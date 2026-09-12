using System;
using OpenTK.Mathematics;

namespace MphRead
{
    internal readonly record struct EnhancedLutSample(
        Vector2 LowerUv,
        Vector2 UpperUv,
        float SliceBlend);

    /// <summary>Addressing for a 16-cube stored as sixteen horizontal tiles.</summary>
    internal static class EnhancedLutAddressing
    {
        public const int Dimension = 16;
        public const int TextureWidth = Dimension * Dimension;
        public const int TextureHeight = Dimension;

        public static EnhancedLutSample Address(Vector3 displayColor)
        {
            ValidateUnit(displayColor, nameof(displayColor));

            float blue = displayColor.Z * (Dimension - 1);
            int lowerSlice = (int)MathF.Floor(blue);
            int upperSlice = Math.Min(lowerSlice + 1, Dimension - 1);
            float blend = blue - lowerSlice;

            Vector2 lowerUv = SliceUv(lowerSlice, displayColor.X, displayColor.Y);
            Vector2 upperUv = SliceUv(upperSlice, displayColor.X, displayColor.Y);
            return new EnhancedLutSample(lowerUv, upperUv, blend);
        }

        private static Vector2 SliceUv(int slice, float red, float green)
        {
            float pixelX = slice * Dimension + red * (Dimension - 1) + 0.5f;
            float pixelY = green * (Dimension - 1) + 0.5f;
            return new Vector2(pixelX / TextureWidth, pixelY / TextureHeight);
        }

        private static void ValidateUnit(Vector3 value, string parameterName)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)
                || !float.IsFinite(value.Z) || value.X < 0 || value.X > 1
                || value.Y < 0 || value.Y > 1 || value.Z < 0 || value.Z > 1)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    internal static class EnhancedDistortionMath
    {
        public static Vector2 ClampOffset(Vector2 offset, float maximumMagnitude)
        {
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (!float.IsFinite(maximumMagnitude) || maximumMagnitude < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMagnitude));
            }
            if (maximumMagnitude == 0) return Vector2.Zero;

            double lengthSquared = (double)offset.X * offset.X
                + (double)offset.Y * offset.Y;
            double maximumSquared = (double)maximumMagnitude * maximumMagnitude;
            if (lengthSquared <= maximumSquared) return offset;

            double scale = maximumMagnitude / Math.Sqrt(lengthSquared);
            return new Vector2((float)(offset.X * scale), (float)(offset.Y * scale));
        }
    }
}
