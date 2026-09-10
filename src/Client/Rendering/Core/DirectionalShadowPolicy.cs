using System;
using OpenTK.Mathematics;

namespace MphRead
{
    internal static class ShadowMath
    {
        private const float MinimumDirectionMagnitude = 0.0001f;

        public static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        public static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = default;
            if (!IsFinite(value)) return false;
            float scale = MathF.Max(MathF.Abs(value.X),
                MathF.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)));
            if (scale < MinimumDirectionMagnitude) return false;
            Vector3 scaled = value / scale;
            float length = scaled.Length;
            if (!float.IsFinite(length) || length <= 0) return false;
            normalized = scaled / length;
            return IsFinite(normalized);
        }
    }

    /// <summary>User-facing shadow tiers. Numeric resource sizes remain internal.</summary>
    public enum ShadowQuality : byte
    {
        Off,
        Low,
        High
    }

    /// <summary>Bounded implementation settings selected by a shadow quality tier.</summary>
    public readonly record struct ShadowQualitySettings
    {
        public ShadowQualitySettings(bool enabled, int mapSize, int pcfRadius)
        {
            if (!enabled)
            {
                if (mapSize != 0 || pcfRadius != 0)
                    throw new ArgumentException("Disabled shadows cannot allocate a map or PCF kernel.");
            }
            else
            {
                if (mapSize is < 256 or > 4096 || (mapSize & (mapSize - 1)) != 0)
                    throw new ArgumentOutOfRangeException(nameof(mapSize));
                if (pcfRadius != 1)
                    throw new ArgumentOutOfRangeException(nameof(pcfRadius));
            }
            Enabled = enabled;
            MapSize = mapSize;
            PcfRadius = pcfRadius;
        }

        public bool Enabled { get; }
        public int MapSize { get; }
        public int PcfRadius { get; }
        public int PcfTapCount => Enabled ? (PcfRadius * 2 + 1) * (PcfRadius * 2 + 1) : 0;
    }

    public static class ShadowQualityPolicy
    {
        public const ShadowQuality DefaultQuality = ShadowQuality.High;
        public const int LowMapSize = 1024;
        public const int HighMapSize = 2048;
        public const int PcfRadius = 1;

        public static ShadowQualitySettings Resolve(ShadowQuality quality)
            => quality switch
            {
                ShadowQuality.Low => new(true, LowMapSize, PcfRadius),
                ShadowQuality.High => new(true, HighMapSize, PcfRadius),
                _ => new(false, 0, 0)
            };

        public static ShadowQualitySettings Default => Resolve(DefaultQuality);
    }

    /// <summary>
    /// One selected room light suitable for the initial single directional
    /// shadow. SourceIndex is stable and allows authored room data to override
    /// automatic dominance without introducing a second light representation.
    /// </summary>
    public readonly record struct PrimaryShadowLight(
        int SourceIndex,
        Vector3 Direction,
        Vector3 Color,
        float ColorIntensity);

    public static class PrimaryShadowLightPolicy
    {
        /// <summary>
        /// Selects one of the two room directional lights. A valid authored
        /// override wins; otherwise non-negative Rec. 709 luminance determines
        /// dominance and source order breaks exact ties.
        /// </summary>
        public static bool TrySelect(
            Vector3 light1Direction,
            Vector3 light1Color,
            Vector3 light2Direction,
            Vector3 light2Color,
            int? environmentOverride,
            out PrimaryShadowLight selected)
        {
            bool firstValid = TryCreate(0, light1Direction, light1Color, out PrimaryShadowLight first);
            bool secondValid = TryCreate(1, light2Direction, light2Color, out PrimaryShadowLight second);

            if (environmentOverride == 0 && firstValid)
            {
                selected = first;
                return true;
            }
            if (environmentOverride == 1 && secondValid)
            {
                selected = second;
                return true;
            }
            if (!firstValid)
            {
                selected = second;
                return secondValid;
            }
            if (!secondValid || first.ColorIntensity >= second.ColorIntensity)
            {
                selected = first;
                return true;
            }
            selected = second;
            return true;
        }

        private static bool TryCreate(int sourceIndex, Vector3 direction, Vector3 color,
            out PrimaryShadowLight result)
        {
            result = default;
            if (!ShadowMath.TryNormalize(direction, out Vector3 normalizedDirection)
                || !ShadowMath.IsFinite(color))
            {
                return false;
            }

            Vector3 boundedColor = new(
                MathF.Max(color.X, 0),
                MathF.Max(color.Y, 0),
                MathF.Max(color.Z, 0));
            float intensity = Vector3.Dot(boundedColor,
                new Vector3(0.2126f, 0.7152f, 0.0722f));
            if (!float.IsFinite(intensity) || intensity <= 0)
            {
                return false;
            }
            result = new PrimaryShadowLight(sourceIndex, normalizedDirection,
                boundedColor, intensity);
            return true;
        }
    }

    public readonly record struct StableShadowProjection(
        Matrix4 View,
        Matrix4 Projection,
        Matrix4 ViewProjection,
        Vector3 SnappedCenter,
        float WorldUnitsPerTexel,
        float HalfExtent,
        float DepthRange);

    /// <summary>
    /// Backend-neutral math for the initial camera-centered directional shadow.
    /// The orthographic footprint and depth range are clamped before use, and
    /// its light-space X/Y center is snapped to whole shadow texels.
    /// </summary>
    public static class StableShadowProjectionPolicy
    {
        public const float DefaultHalfExtent = 48;
        public const float MinimumHalfExtent = 1;
        public const float MaximumHalfExtent = 256;
        public const float DefaultDepthRange = 128;
        public const float MinimumDepthRange = 2;
        public const float MaximumDepthRange = 1024;

        public static bool TryCreate(Vector3 cameraWorldPosition, Vector3 lightDirection,
            ShadowQualitySettings quality, float requestedHalfExtent,
            float requestedDepthRange, out StableShadowProjection result)
        {
            result = default;
            if (!quality.Enabled || quality.MapSize <= 0
                || !ShadowMath.IsFinite(cameraWorldPosition)
                || !ShadowMath.TryNormalize(lightDirection, out Vector3 forward))
            {
                return false;
            }

            float halfExtent = Bounded(requestedHalfExtent, DefaultHalfExtent,
                MinimumHalfExtent, MaximumHalfExtent);
            float depthRange = Bounded(requestedDepthRange, DefaultDepthRange,
                MinimumDepthRange, MaximumDepthRange);
            Vector3 upReference = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) < 0.99f
                ? Vector3.UnitY : Vector3.UnitZ;
            Vector3 right = Vector3.Cross(upReference, forward).Normalized();
            Vector3 up = Vector3.Cross(forward, right).Normalized();

            float worldUnitsPerTexel = 2 * halfExtent / quality.MapSize;
            float centerX = Snap(Vector3.Dot(cameraWorldPosition, right), worldUnitsPerTexel);
            float centerY = Snap(Vector3.Dot(cameraWorldPosition, up), worldUnitsPerTexel);
            float centerZ = Vector3.Dot(cameraWorldPosition, forward);
            Vector3 snappedCenter = right * centerX + up * centerY + forward * centerZ;
            Vector3 eye = snappedCenter - forward * (depthRange * 0.5f);

            Matrix4 view = Matrix4.LookAt(eye, snappedCenter, up);
            Matrix4 projection = Matrix4.CreateOrthographic(
                halfExtent * 2, halfExtent * 2, 0, depthRange);
            Matrix4 viewProjection = view * projection;
            if (!IsFinite(view) || !IsFinite(projection) || !IsFinite(viewProjection))
            {
                return false;
            }

            result = new StableShadowProjection(view, projection, viewProjection,
                snappedCenter, worldUnitsPerTexel, halfExtent, depthRange);
            return true;
        }

        private static float Bounded(float value, float fallback, float minimum, float maximum)
            => Math.Clamp(float.IsFinite(value) ? value : fallback, minimum, maximum);

        private static float Snap(float value, float interval)
            => MathF.Round(value / interval, MidpointRounding.AwayFromZero) * interval;

        private static bool IsFinite(Matrix4 value)
        {
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                if (!float.IsFinite(value[row, column])) return false;
            }
            return true;
        }
    }

    public static class ShadowPcfPolicy
    {
        public const int KernelWidth = 3;
        public const int TapCount = KernelWidth * KernelWidth;

        /// <summary>Returns one row-major 3x3 sampling offset in normalized UV space.</summary>
        public static Vector2 GetOffset(int tapIndex, int mapSize)
        {
            if ((uint)tapIndex >= TapCount)
                throw new ArgumentOutOfRangeException(nameof(tapIndex));
            if (mapSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(mapSize));
            int x = tapIndex % KernelWidth - 1;
            int y = tapIndex / KernelWidth - 1;
            float texel = 1f / mapSize;
            return new Vector2(x * texel, y * texel);
        }
    }
}
