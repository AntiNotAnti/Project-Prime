using System;
using System.Text;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>Presentation-only atmosphere categories. They never affect simulation.</summary>
    public enum EnvironmentalParticleKind
    {
        Embers,
        Frost,
        Dust,
        Sparks,
        EnergyMotes
    }

    /// <summary>
    /// Backend-neutral definition of a bounded local atmosphere layer. This
    /// descriptor contains no collision, damage, visibility, or replication state.
    /// </summary>
    public readonly record struct EnvironmentalParticleLayerDescriptor
    {
        public const int MaximumSupportedParticles = 4096;
        public const float MaximumEmissiveStrength = 16;

        public EnvironmentalParticleLayerDescriptor(string stableLayerKey,
            ulong spawnRegionKey, EnvironmentalParticleKind kind,
            Vector3 boundsMinimum, Vector3 boundsMaximum, int maximumParticles,
            float emissionRatePerSecond, TimeSpan minimumLifetime,
            TimeSpan maximumLifetime, float minimumSize, float maximumSize,
            Vector3 driftVelocity, Vector4 tint, float emissiveStrength)
        {
            if (string.IsNullOrWhiteSpace(stableLayerKey))
            {
                throw new ArgumentException("A stable presentation layer key is required.",
                    nameof(stableLayerKey));
            }
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            RequireFinite(boundsMinimum, nameof(boundsMinimum));
            RequireFinite(boundsMaximum, nameof(boundsMaximum));
            if (boundsMaximum.X <= boundsMinimum.X
                || boundsMaximum.Y <= boundsMinimum.Y
                || boundsMaximum.Z <= boundsMinimum.Z)
            {
                throw new ArgumentOutOfRangeException(nameof(boundsMaximum),
                    "Particle bounds must have positive extent on every axis.");
            }
            if (maximumParticles < 1 || maximumParticles > MaximumSupportedParticles)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumParticles));
            }
            if (!float.IsFinite(emissionRatePerSecond) || emissionRatePerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(emissionRatePerSecond));
            }
            if (minimumLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLifetime));
            }
            if (maximumLifetime < minimumLifetime)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumLifetime));
            }
            if (!float.IsFinite(minimumSize) || minimumSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSize));
            }
            if (!float.IsFinite(maximumSize) || maximumSize < minimumSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSize));
            }
            RequireFinite(driftVelocity, nameof(driftVelocity));
            if (!IsUnitColor(tint))
            {
                throw new ArgumentOutOfRangeException(nameof(tint));
            }
            if (!float.IsFinite(emissiveStrength) || emissiveStrength < 0
                || emissiveStrength > MaximumEmissiveStrength)
            {
                throw new ArgumentOutOfRangeException(nameof(emissiveStrength));
            }

            StableLayerKey = stableLayerKey;
            SpawnRegionKey = spawnRegionKey;
            Kind = kind;
            BoundsMinimum = boundsMinimum;
            BoundsMaximum = boundsMaximum;
            MaximumParticles = maximumParticles;
            EmissionRatePerSecond = emissionRatePerSecond;
            MinimumLifetime = minimumLifetime;
            MaximumLifetime = maximumLifetime;
            MinimumSize = minimumSize;
            MaximumSize = maximumSize;
            DriftVelocity = driftVelocity;
            Tint = tint;
            EmissiveStrength = emissiveStrength;
        }

        public string StableLayerKey { get; }
        public ulong SpawnRegionKey { get; }
        public EnvironmentalParticleKind Kind { get; }
        public Vector3 BoundsMinimum { get; }
        public Vector3 BoundsMaximum { get; }
        public int MaximumParticles { get; }
        public float EmissionRatePerSecond { get; }
        public TimeSpan MinimumLifetime { get; }
        public TimeSpan MaximumLifetime { get; }
        public float MinimumSize { get; }
        public float MaximumSize { get; }
        public Vector3 DriftVelocity { get; }
        public Vector4 Tint { get; }
        public float EmissiveStrength { get; }

        private static void RequireFinite(Vector3 value, string parameterName)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)
                || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsUnitColor(Vector4 value)
            => float.IsFinite(value.X) && value.X >= 0 && value.X <= 1
                && float.IsFinite(value.Y) && value.Y >= 0 && value.Y <= 1
                && float.IsFinite(value.Z) && value.Z >= 0 && value.Z <= 1
                && float.IsFinite(value.W) && value.W >= 0 && value.W <= 1;
    }

    /// <summary>
    /// Derives local atmosphere randomness without touching Scene or gameplay RNG.
    /// The specified UTF-8/FNV-1a encoding is stable across processes and platforms.
    /// </summary>
    public static class EnvironmentalParticleSeed
    {
        public static readonly TimeSpan DefaultTimeBucket = TimeSpan.FromSeconds(1);

        public static ulong Create(string roomKey, ulong spawnRegionKey,
            TimeSpan presentationTime)
            => Create(roomKey, spawnRegionKey, presentationTime, DefaultTimeBucket);

        public static ulong Create(string roomKey, ulong spawnRegionKey,
            TimeSpan presentationTime, TimeSpan timeBucket)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                throw new ArgumentException("A stable room key is required.", nameof(roomKey));
            }
            if (presentationTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(presentationTime));
            }
            if (timeBucket <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeBucket));
            }

            ulong bucket = (ulong)(presentationTime.Ticks / timeBucket.Ticks);
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            AddByte(ref hash, 1, prime);
            foreach (byte value in Encoding.UTF8.GetBytes(roomKey))
            {
                AddByte(ref hash, value, prime);
            }
            AddByte(ref hash, 0, prime);
            AddUInt64(ref hash, spawnRegionKey, prime);
            AddByte(ref hash, 2, prime);
            AddUInt64(ref hash, bucket, prime);
            return hash;
        }

        private static void AddUInt64(ref ulong hash, ulong value, ulong prime)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                AddByte(ref hash, (byte)(value >> shift), prime);
            }
        }

        private static void AddByte(ref ulong hash, byte value, ulong prime)
        {
            hash ^= value;
            hash = unchecked(hash * prime);
        }
    }
}
