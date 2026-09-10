using System;

namespace MphRead
{
    /// <summary>
    /// Backend-neutral reference for Enhanced distance fog. The optional
    /// height term attenuates density above the authored fog height using the
    /// average density at the camera and fragment, which stays stable when
    /// either endpoint crosses the layer.
    /// </summary>
    internal static class EnhancedFogMath
    {
        public static float DistanceFactor(float distance, float density)
        {
            ValidateDistanceDensity(distance, density);
            return ExponentialFactor((double)distance * density);
        }

        public static float DistanceHeightFactor(float distance, float density,
            float cameraHeight, float worldHeight, float fogHeight, float fogFalloff)
        {
            ValidateDistanceDensity(distance, density);
            if (!float.IsFinite(cameraHeight))
                throw new ArgumentOutOfRangeException(nameof(cameraHeight));
            if (!float.IsFinite(worldHeight))
                throw new ArgumentOutOfRangeException(nameof(worldHeight));
            if (!float.IsFinite(fogHeight))
                throw new ArgumentOutOfRangeException(nameof(fogHeight));
            if (!float.IsFinite(fogFalloff) || fogFalloff < 0)
                throw new ArgumentOutOfRangeException(nameof(fogFalloff));

            double cameraDensity = HeightDensity(cameraHeight, fogHeight, fogFalloff);
            double worldDensity = HeightDensity(worldHeight, fogHeight, fogFalloff);
            double averageDensity = (cameraDensity + worldDensity) * 0.5;
            return ExponentialFactor((double)distance * density * averageDensity);
        }

        private static double HeightDensity(float height, float fogHeight, float falloff)
        {
            double aboveLayer = Math.Max(0, (double)height - fogHeight);
            return Math.Exp(-aboveLayer * falloff);
        }

        private static float ExponentialFactor(double opticalDepth)
        {
            if (opticalDepth <= 0) return 0;
            if (double.IsPositiveInfinity(opticalDepth)) return 1;
            return (float)Math.Clamp(1 - Math.Exp(-opticalDepth), 0, 1);
        }

        private static void ValidateDistanceDensity(float distance, float density)
        {
            if (!float.IsFinite(distance) || distance < 0)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (!float.IsFinite(density) || density < 0)
                throw new ArgumentOutOfRangeException(nameof(density));
        }
    }
}
