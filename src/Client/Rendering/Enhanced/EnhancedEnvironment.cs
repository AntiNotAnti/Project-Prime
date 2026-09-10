using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Validated logical identity for optional Enhanced environment content.
    /// It is relative and contains no filesystem authority.
    /// </summary>
    public readonly struct EnhancedEnvironmentAssetKey : IEquatable<EnhancedEnvironmentAssetKey>
    {
        public const int MaximumLength = 256;
        public const int MaximumSegmentLength = 64;
        private readonly string? _value;

        private EnhancedEnvironmentAssetKey(string value) => _value = value;

        public bool IsValid => _value is not null;
        public string Value => _value ?? String.Empty;

        public static EnhancedEnvironmentAssetKey Parse(string value)
        {
            if (!TryParse(value, out EnhancedEnvironmentAssetKey key))
            {
                throw new FormatException("Enhanced environment asset key is invalid.");
            }
            return key;
        }

        public static bool TryParse(string? value, out EnhancedEnvironmentAssetKey key)
        {
            key = default;
            if (String.IsNullOrEmpty(value) || value.Length > MaximumLength
                || !String.Equals(value, value.Trim(), StringComparison.Ordinal)
                || value.Contains('\\') || value.Contains(':')
                || value.StartsWith('/') || value.EndsWith('/')
                || value.Contains("//", StringComparison.Ordinal))
            {
                return false;
            }

            string[] segments = value.Split('/');
            if (segments.Length is < 2 or > 8)
            {
                return false;
            }
            foreach (string segment in segments)
            {
                if (segment.Length is 0 or > MaximumSegmentLength
                    || segment is "." or ".." || !IsIdentifier(segment))
                {
                    return false;
                }
            }

            key = new EnhancedEnvironmentAssetKey(value.ToLowerInvariant());
            return true;
        }

        public bool Equals(EnhancedEnvironmentAssetKey other)
            => String.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object? obj)
            => obj is EnhancedEnvironmentAssetKey other && Equals(other);
        public override int GetHashCode()
            => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => Value;

        public static bool operator ==(EnhancedEnvironmentAssetKey left,
            EnhancedEnvironmentAssetKey right) => left.Equals(right);
        public static bool operator !=(EnhancedEnvironmentAssetKey left,
            EnhancedEnvironmentAssetKey right) => !left.Equals(right);

        private static bool IsIdentifier(string value)
        {
            if (!IsAlphaNumeric(value[0]))
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!IsAlphaNumeric(character) && character is not ('.' or '-' or '_'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAlphaNumeric(char value)
            => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    }

    /// <summary>
    /// Pure legacy metadata input used to derive conservative Enhanced defaults.
    /// FogColor is normalized to the 0..1 presentation range.
    /// </summary>
    public readonly record struct LegacyRoomEnvironment
    {
        public LegacyRoomEnvironment(bool fogEnabled, Vector3 fogColor,
            int fogSlope, int fogOffset, float farClip)
        {
            if (!IsUnitColor(fogColor))
            {
                throw new ArgumentOutOfRangeException(nameof(fogColor));
            }
            if (fogSlope is < 0 or > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(fogSlope));
            }
            if (fogOffset is < 0 or > 0x7FFF)
            {
                throw new ArgumentOutOfRangeException(nameof(fogOffset));
            }
            if (!float.IsFinite(farClip) || farClip <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(farClip));
            }

            FogEnabled = fogEnabled;
            FogColor = fogColor;
            FogSlope = fogSlope;
            FogOffset = fogOffset;
            FarClip = farClip;
        }

        public bool FogEnabled { get; }
        public Vector3 FogColor { get; }
        public int FogSlope { get; }
        public int FogOffset { get; }
        public float FarClip { get; }

        private static bool IsUnitColor(Vector3 value)
            => float.IsFinite(value.X) && value.X >= 0 && value.X <= 1
                && float.IsFinite(value.Y) && value.Y >= 0 && value.Y <= 1
                && float.IsFinite(value.Z) && value.Z >= 0 && value.Z <= 1;
    }

    /// <summary>
    /// Backend-neutral room atmosphere. All values are presentation-only and
    /// carry no gameplay, visibility, collision, or replication authority.
    /// </summary>
    public readonly record struct EnhancedEnvironment
    {
        public const float MaximumFogDensity = 1;
        public const float MaximumAbsoluteFogHeight = 1_000_000;
        public const float MaximumFogFalloff = 100;
        public const float MinimumExposure = 0.25f;
        public const float MaximumExposure = 4;

        public EnhancedEnvironment(Vector3 fogColor, float fogDensity,
            float fogHeight, float fogFalloff, float exposure,
            EnhancedEnvironmentAssetKey? lutKey,
            EnhancedEnvironmentAssetKey? reflectionProbeKey,
            int? primaryShadowLightIndex = null)
        {
            if (!IsUnitColor(fogColor))
            {
                throw new ArgumentOutOfRangeException(nameof(fogColor));
            }
            if (!float.IsFinite(fogDensity) || fogDensity < 0
                || fogDensity > MaximumFogDensity)
            {
                throw new ArgumentOutOfRangeException(nameof(fogDensity));
            }
            if (!float.IsFinite(fogHeight) || MathF.Abs(fogHeight) > MaximumAbsoluteFogHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(fogHeight));
            }
            if (!float.IsFinite(fogFalloff) || fogFalloff < 0
                || fogFalloff > MaximumFogFalloff)
            {
                throw new ArgumentOutOfRangeException(nameof(fogFalloff));
            }
            if (!float.IsFinite(exposure) || exposure < MinimumExposure
                || exposure > MaximumExposure)
            {
                throw new ArgumentOutOfRangeException(nameof(exposure));
            }
            if (lutKey.HasValue && !lutKey.Value.IsValid)
            {
                throw new ArgumentException("LUT key must be initialized.", nameof(lutKey));
            }
            if (reflectionProbeKey.HasValue && !reflectionProbeKey.Value.IsValid)
            {
                throw new ArgumentException("Reflection probe key must be initialized.",
                    nameof(reflectionProbeKey));
            }
            if (primaryShadowLightIndex is not (null or 0 or 1))
            {
                throw new ArgumentOutOfRangeException(nameof(primaryShadowLightIndex));
            }

            FogColor = fogColor;
            FogDensity = fogDensity;
            FogHeight = fogHeight;
            FogFalloff = fogFalloff;
            Exposure = exposure;
            LutKey = lutKey;
            ReflectionProbeKey = reflectionProbeKey;
            PrimaryShadowLightIndex = primaryShadowLightIndex;
        }

        public Vector3 FogColor { get; }
        public float FogDensity { get; }
        public float FogHeight { get; }
        public float FogFalloff { get; }
        public float Exposure { get; }
        public EnhancedEnvironmentAssetKey? LutKey { get; }
        public EnhancedEnvironmentAssetKey? ReflectionProbeKey { get; }
        public int? PrimaryShadowLightIndex { get; }

        internal bool IsInitialized => Exposure is >= MinimumExposure and <= MaximumExposure;

        public static EnhancedEnvironment Neutral { get; } = new(
            Vector3.Zero, fogDensity: 0, fogHeight: 0, fogFalloff: 0,
            exposure: 1, lutKey: null, reflectionProbeKey: null);

        public static EnhancedEnvironment FromRoomMetadata(RoomMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            var source = new LegacyRoomEnvironment(
                metadata.FogEnabled,
                new Vector3(
                    Math.Clamp(metadata.FogColor.Red / 31f, 0, 1),
                    Math.Clamp(metadata.FogColor.Green / 31f, 0, 1),
                    Math.Clamp(metadata.FogColor.Blue / 31f, 0, 1)),
                Math.Clamp(metadata.FogSlope, 0, 30),
                Math.Clamp(metadata.FogOffset, 0, 0x7FFF),
                float.IsFinite(metadata.FarClip) && metadata.FarClip > 0
                    ? metadata.FarClip : 1000);
            return FromLegacy(source);
        }

        public static EnhancedEnvironment FromLegacy(LegacyRoomEnvironment metadata)
        {
            if (!float.IsFinite(metadata.FarClip) || metadata.FarClip <= 0)
            {
                throw new ArgumentException("Legacy room environment must be initialized.",
                    nameof(metadata));
            }
            // Match the legacy fog's opacity at the far plane, expressed as an
            // exponential density. A 95% cap avoids an infinite density when
            // legacy fog is already fully opaque there.
            float density = 0;
            if (metadata.FogEnabled)
            {
                double fogMinimum = metadata.FogOffset / (double)0x7FFF;
                int shifted = 0x400 >> metadata.FogSlope;
                double fogSpan = 32d * shifted / 0x7FFF;
                double farOpacity = fogSpan <= 0
                    ? 0
                    : Math.Clamp((1 - fogMinimum) / fogSpan, 0, 0.95);
                density = (float)Math.Clamp(-Math.Log(1 - farOpacity) / metadata.FarClip,
                    0, MaximumFogDensity);
            }

            return new EnhancedEnvironment(metadata.FogColor, density,
                fogHeight: 0, fogFalloff: 0, exposure: 1,
                lutKey: null, reflectionProbeKey: null);
        }

        private static bool IsUnitColor(Vector3 value)
            => float.IsFinite(value.X) && value.X >= 0 && value.X <= 1
                && float.IsFinite(value.Y) && value.Y >= 0 && value.Y <= 1
                && float.IsFinite(value.Z) && value.Z >= 0 && value.Z <= 1;
    }
}
