using System;
using MphRead.Mods;
using OpenTK.Mathematics;

namespace MphRead
{
    internal readonly record struct SdlGpuEnhancedSurfaceConfiguration(
        uint Width, uint Height);

    internal readonly record struct SdlGpuEnhancedSurfacePlan(
        SdlGpuEnhancedSurfaceConfiguration Configuration,
        uint SsaoWidth,
        uint SsaoHeight,
        long EstimatedBytes)
    {
        public const long MaximumEstimatedBytes = 256L * 1024 * 1024;
        private const long SurfaceBytesPerPixel = 8;
        // D32S8 is conservatively budgeted even when the selected target is D24S8.
        private const long DepthBytesPerPixel = 8;
        private const long SsaoBytesPerPixel = 1;

        public static SdlGpuEnhancedSurfacePlan Create(uint width, uint height)
        {
            if (width == 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height == 0) throw new ArgumentOutOfRangeException(nameof(height));

            uint ssaoWidth = checked((width + 1) / 2);
            uint ssaoHeight = checked((height + 1) / 2);
            long surfacePixels = checked((long)width * height);
            long ssaoPixels = checked((long)ssaoWidth * ssaoHeight);
            long estimatedBytes = checked(surfacePixels
                * (SurfaceBytesPerPixel + DepthBytesPerPixel)
                + ssaoPixels * SsaoBytesPerPixel * 2);
            if (estimatedBytes > MaximumEstimatedBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"Enhanced surface and SSAO targets require {estimatedBytes} bytes, "
                    + $"exceeding their {MaximumEstimatedBytes}-byte budget.");
            }
            return new(new(width, height), ssaoWidth, ssaoHeight,
                estimatedBytes);
        }
    }

    /// <summary>Suppresses per-frame retries only for the failed immutable configuration.</summary>
    internal sealed class SdlGpuConfigurationFailureCache<T>
        where T : struct, IEquatable<T>
    {
        private T? _failed;

        public T? FailedConfiguration => _failed;

        public bool ShouldAttempt(T configuration)
            => !_failed.HasValue || !_failed.Value.Equals(configuration);

        public void RecordFailure(T configuration) => _failed = configuration;

        public void RecordSuccess() => _failed = null;
    }

    internal static class SdlGpuSurfaceCelPolicy
    {
        public static bool UsesSurfaceData(GraphicsPreset preset,
            bool celEnabled, float outline, bool surfaceAvailable)
            => preset == GraphicsPreset.Enhanced && celEnabled && outline > 0
                && surfaceAvailable;

        public static bool RequiresLegacyDepthSampling(GraphicsPreset preset,
            bool celEnabled, float outline, bool surfaceAvailable,
            bool depthSampleable)
            => celEnabled && outline > 0 && depthSampleable
                && !UsesSurfaceData(preset, celEnabled, outline,
                    surfaceAvailable);

        public static float RelativeDepthDifference(float center, float neighbor)
        {
            if (!float.IsFinite(center) || center <= 0)
                throw new ArgumentOutOfRangeException(nameof(center));
            if (!float.IsFinite(neighbor) || neighbor < 0)
                throw new ArgumentOutOfRangeException(nameof(neighbor));
            return MathF.Abs(neighbor - center) / MathF.Max(center, 0.0001f);
        }
    }

    internal static class SdlGpuAmbientOcclusionPolicy
    {
        public static bool UsesForPass(RenderPassKind pass)
            => pass == RenderPassKind.Opaque;

        public static Vector3 Compose(Vector3 ambient, Vector3 directional,
            Vector3 point, Vector3 reflection, Vector3 emission,
            float ambientOcclusion)
        {
            if (!float.IsFinite(ambientOcclusion)
                || ambientOcclusion < 0 || ambientOcclusion > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(ambientOcclusion));
            }
            return ambient * ambientOcclusion + directional + point
                + reflection + emission;
        }
    }

    /// <summary>CPU reference for the shader's view-space SSAO geometry tests.</summary>
    internal static class SdlGpuSsaoReference
    {
        public static Vector2 ProjectViewPosition(Vector3 position,
            float verticalFieldOfViewRadians, float aspectRatio)
        {
            ValidateProjection(position, verticalFieldOfViewRadians,
                aspectRatio);
            float tangent = MathF.Tan(verticalFieldOfViewRadians * 0.5f);
            float inverseDepth = 1f / -position.Z;
            float ndcX = position.X * inverseDepth / (tangent * aspectRatio);
            float ndcY = position.Y * inverseDepth / tangent;
            return new Vector2(ndcX * 0.5f + 0.5f,
                0.5f - ndcY * 0.5f);
        }

        public static Vector3 ReconstructViewPosition(Vector2 uv,
            float linearDepth, float verticalFieldOfViewRadians,
            float aspectRatio)
        {
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
                throw new ArgumentOutOfRangeException(nameof(uv));
            if (!float.IsFinite(linearDepth) || linearDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(linearDepth));
            ValidateProjection(new Vector3(0, 0, -linearDepth),
                verticalFieldOfViewRadians, aspectRatio);
            float tangent = MathF.Tan(verticalFieldOfViewRadians * 0.5f);
            float ndcX = uv.X * 2 - 1;
            float ndcY = 1 - uv.Y * 2;
            return new Vector3(ndcX * linearDepth * tangent * aspectRatio,
                ndcY * linearDepth * tangent, -linearDepth);
        }

        public static bool IsOccluding(Vector3 center, Vector3 normal,
            Vector3 samplePosition, Vector3 neighborPosition,
            float radius, float bias)
        {
            if (!IsFinite(center) || !IsFinite(normal)
                || !IsFinite(samplePosition) || !IsFinite(neighborPosition))
                throw new ArgumentOutOfRangeException(nameof(center));
            if (!float.IsFinite(radius) || radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!float.IsFinite(bias) || bias < 0 || bias >= radius)
                throw new ArgumentOutOfRangeException(nameof(bias));
            if (normal.LengthSquared <= 0)
                throw new ArgumentOutOfRangeException(nameof(normal));
            Vector3 delta = neighborPosition - center;
            float distance = delta.Length;
            if (distance <= bias || distance > radius) return false;
            float hemisphereSeparation = Vector3.Dot(delta,
                normal.Normalized());
            float sampleDepth = -samplePosition.Z;
            float neighborDepth = -neighborPosition.Z;
            return hemisphereSeparation > bias
                && neighborDepth < sampleDepth - bias;
        }

        private static void ValidateProjection(Vector3 position,
            float verticalFieldOfViewRadians, float aspectRatio)
        {
            if (!IsFinite(position) || position.Z >= 0)
                throw new ArgumentOutOfRangeException(nameof(position));
            if (!float.IsFinite(verticalFieldOfViewRadians)
                || verticalFieldOfViewRadians <= 0
                || verticalFieldOfViewRadians >= MathF.PI)
                throw new ArgumentOutOfRangeException(
                    nameof(verticalFieldOfViewRadians));
            if (!float.IsFinite(aspectRatio) || aspectRatio <= 0)
                throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }

    internal enum SdlGpuSsaoPassKind : byte
    {
        Raw,
        BilateralHorizontal,
        BilateralVertical
    }

    internal static class SdlGpuSsaoPassPlan
    {
        public static ReadOnlySpan<SdlGpuSsaoPassKind> Sequence =>
        [
            SdlGpuSsaoPassKind.Raw,
            SdlGpuSsaoPassKind.BilateralHorizontal,
            SdlGpuSsaoPassKind.BilateralVertical
        ];
    }
}
