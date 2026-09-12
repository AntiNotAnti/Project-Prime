using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using OpenTK.Mathematics;
using BigInteger = System.Numerics.BigInteger;

namespace MphRead
{
    /// <summary>
    /// Immutable state for one local visual particle. It contains no collision,
    /// visibility, replication, damage, or other gameplay-authoritative state.
    /// </summary>
    public readonly record struct EnvironmentalParticleRecord
    {
        internal EnvironmentalParticleRecord(ulong stableKey, ulong spawnOrdinal,
            string stableLayerKey, ulong spawnRegionKey, EnvironmentalParticleKind kind,
            Vector3 initialPosition, Vector3 velocity, float size, Vector4 tint,
            float emissiveStrength, long spawnedAtTick, long lifetimeTicks)
        {
            StableKey = stableKey;
            SpawnOrdinal = spawnOrdinal;
            StableLayerKey = stableLayerKey;
            SpawnRegionKey = spawnRegionKey;
            Kind = kind;
            InitialPosition = initialPosition;
            Velocity = velocity;
            Size = size;
            Tint = tint;
            EmissiveStrength = emissiveStrength;
            SpawnedAtTick = spawnedAtTick;
            LifetimeTicks = lifetimeTicks;
        }

        public ulong StableKey { get; }
        public ulong SpawnOrdinal { get; }
        public string StableLayerKey { get; }
        public ulong SpawnRegionKey { get; }
        public EnvironmentalParticleKind Kind { get; }
        public Vector3 InitialPosition { get; }
        public Vector3 Velocity { get; }
        public float Size { get; }
        public Vector4 Tint { get; }
        public float EmissiveStrength { get; }
        public long SpawnedAtTick { get; }
        public long LifetimeTicks { get; }

        internal bool IsExpired(long presentationTick)
            => presentationTick - SpawnedAtTick >= LifetimeTicks;

        internal Vector3 PositionAt(long presentationTick)
        {
            double seconds = (presentationTick - SpawnedAtTick)
                / (double)TimeSpan.TicksPerSecond;
            return new Vector3(
                FiniteMotion(InitialPosition.X, Velocity.X, seconds),
                FiniteMotion(InitialPosition.Y, Velocity.Y, seconds),
                FiniteMotion(InitialPosition.Z, Velocity.Z, seconds));
        }

        private static float FiniteMotion(float origin, float velocity, double seconds)
        {
            double value = origin + velocity * seconds;
            if (value >= float.MaxValue) return float.MaxValue;
            if (value <= -float.MaxValue) return -float.MaxValue;
            return (float)value;
        }
    }

    /// <summary>A particle sampled at presentation time without changing its schedule.</summary>
    public readonly record struct EnvironmentalParticleSample(
        EnvironmentalParticleRecord Particle,
        Vector3 Position,
        TimeSpan Age);

    /// <summary>Immutable, oldest/key-ordered room particle snapshot.</summary>
    public sealed class EnvironmentalParticleSnapshot
    {
        private readonly ReadOnlyCollection<EnvironmentalParticleSample> _items;

        internal EnvironmentalParticleSnapshot(string roomKey, long schedulingTick,
            long sampleTick, EnvironmentalParticleSample[] items)
        {
            RoomKey = roomKey;
            SchedulingTick = schedulingTick;
            SampleTick = sampleTick;
            _items = Array.AsReadOnly(items);
        }

        public string RoomKey { get; }
        public long SchedulingTick { get; }
        public long SampleTick { get; }
        public long PresentationTick => SampleTick;
        public IReadOnlyList<EnvironmentalParticleSample> Items => _items;
    }

    /// <summary>
    /// Bounded, backend-neutral local atmosphere runtime. Spawn accumulation is
    /// derived from absolute integer TimeSpan ticks, so render frequency and
    /// repeated observation of one tick cannot alter particle state.
    /// </summary>
    public sealed class EnvironmentalParticleRuntime
    {
        public const int MaximumRoomCapacity
            = EnvironmentalParticleLayerDescriptor.MaximumSupportedParticles;
        public const int MaximumLayerCount = 64;
        public const float MaximumEmissionRatePerSecond = 1_000_000;

        private readonly List<LayerState> _layers = new();
        private readonly List<EnvironmentalParticleRecord> _particles;
        private string? _roomKey;
        private long _timelineOriginTick;
        private long? _lastPresentationTick;

        public EnvironmentalParticleRuntime(int roomCapacity)
        {
            if (roomCapacity < 1 || roomCapacity > MaximumRoomCapacity)
                throw new ArgumentOutOfRangeException(nameof(roomCapacity));
            RoomCapacity = roomCapacity;
            _particles = new List<EnvironmentalParticleRecord>(roomCapacity);
        }

        public int RoomCapacity { get; }
        public int Count => _particles.Count;
        public string? RoomKey => _roomKey;

        /// <summary>
        /// Starts a new room-local timeline. This is also the explicit room-change
        /// operation: existing particles and the old monotonic clock are discarded.
        /// </summary>
        public void EnterRoom(string roomKey,
            IEnumerable<EnvironmentalParticleLayerDescriptor> layers,
            TimeSpan presentationTime)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
                throw new ArgumentException("A stable room key is required.", nameof(roomKey));
            ArgumentNullException.ThrowIfNull(layers);
            long tick = ValidateNonNegativeTime(presentationTime);

            var newLayers = new List<LayerState>();
            var layerKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (EnvironmentalParticleLayerDescriptor descriptor in layers)
            {
                if (newLayers.Count == MaximumLayerCount)
                    throw new ArgumentOutOfRangeException(nameof(layers),
                        $"A room may define at most {MaximumLayerCount} particle layers.");
                ValidateDescriptor(descriptor);
                if (!layerKeys.Add(descriptor.StableLayerKey))
                    throw new ArgumentException(
                        $"Duplicate environmental particle layer key '{descriptor.StableLayerKey}'.",
                        nameof(layers));
                newLayers.Add(new LayerState(descriptor));
            }
            newLayers.Sort(LayerState.CompareIdentity);

            _particles.Clear();
            _layers.Clear();
            _layers.AddRange(newLayers);
            _roomKey = roomKey;
            _timelineOriginTick = tick;
            _lastPresentationTick = tick;
        }

        /// <summary>
        /// Clears the current room's visual state and begins its deterministic
        /// spawn sequence again at the supplied tick. This intentionally permits
        /// a lower tick; ordinary <see cref="Advance"/> calls do not.
        /// </summary>
        public void Reset(TimeSpan presentationTime)
        {
            RequireRoom();
            long tick = ValidateNonNegativeTime(presentationTime);
            _particles.Clear();
            for (int i = 0; i < _layers.Count; i++) _layers[i].NextSpawnOrdinal = 0;
            _timelineOriginTick = tick;
            _lastPresentationTick = tick;
        }

        public EnvironmentalParticleSnapshot Advance(TimeSpan presentationTime)
            => Advance(presentationTime, presentationTime);

        /// <summary>
        /// Advances spawn/expiry state only to <paramref name="schedulingTime"/>,
        /// then samples immutable particle motion at a later render presentation
        /// time. Repeated samples within one scheduling tick never spawn or expire.
        /// </summary>
        public EnvironmentalParticleSnapshot Advance(TimeSpan schedulingTime,
            TimeSpan samplingTime)
        {
            string roomKey = RequireRoom();
            long tick = ValidateNonNegativeTime(schedulingTime);
            long sampleTick = ValidateNonNegativeTime(samplingTime);
            if (sampleTick < tick)
                throw new ArgumentOutOfRangeException(nameof(samplingTime),
                    "Particle sampling cannot precede its scheduling tick.");
            if (_lastPresentationTick.HasValue && tick < _lastPresentationTick.Value)
                throw new ArgumentOutOfRangeException(nameof(schedulingTime),
                    "Presentation time must be monotonic until reset or room change.");
            _lastPresentationTick = tick;

            RemoveExpired(tick);
            long elapsedTicks = tick - _timelineOriginTick;
            for (int layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
            {
                LayerState layer = _layers[layerIndex];
                ulong targetOrdinal = layer.Emission.CumulativeCount(elapsedTicks);
                if (targetOrdinal == layer.NextSpawnOrdinal) continue;

                ulong due = targetOrdinal - layer.NextSpawnOrdinal;
                int generationLimit = Math.Min(RoomCapacity,
                    layer.Descriptor.MaximumParticles);
                int generateCount = due > (ulong)generationLimit
                    ? generationLimit
                    : (int)due;
                ulong firstOrdinal = targetOrdinal - (ulong)generateCount;
                for (ulong ordinal = firstOrdinal; ordinal < targetOrdinal; ordinal++)
                {
                    long spawnOffset = layer.Emission.SpawnTick(ordinal);
                    long spawnTick = checked(_timelineOriginTick + spawnOffset);
                    EnvironmentalParticleRecord particle = CreateParticle(roomKey,
                        layer.Descriptor, ordinal, spawnTick);
                    if (!particle.IsExpired(tick)) AddParticle(particle,
                        layer.Descriptor.MaximumParticles);
                }
                layer.NextSpawnOrdinal = targetOrdinal;
            }

            return CreateSnapshot(roomKey, tick, sampleTick);
        }

        private EnvironmentalParticleRecord CreateParticle(string roomKey,
            EnvironmentalParticleLayerDescriptor descriptor, ulong ordinal,
            long spawnTick)
        {
            ulong timeSeed = EnvironmentalParticleSeed.Create(roomKey,
                descriptor.SpawnRegionKey, TimeSpan.FromTicks(spawnTick));
            ulong seed = Mix(timeSeed ^ StableStringHash(descriptor.StableLayerKey)
                ^ Mix(ordinal + 0x9E3779B97F4A7C15UL));
            ulong stableKey = Mix(seed ^ 0xD1B54A32D192ED03UL);

            Vector3 position = new(
                Interpolate(descriptor.BoundsMinimum.X, descriptor.BoundsMaximum.X,
                    NextUnit(ref seed)),
                Interpolate(descriptor.BoundsMinimum.Y, descriptor.BoundsMaximum.Y,
                    NextUnit(ref seed)),
                Interpolate(descriptor.BoundsMinimum.Z, descriptor.BoundsMaximum.Z,
                    NextUnit(ref seed)));
            float size = Interpolate(descriptor.MinimumSize, descriptor.MaximumSize,
                NextUnit(ref seed));
            long lifetimeTicks = InterpolateTicks(descriptor.MinimumLifetime.Ticks,
                descriptor.MaximumLifetime.Ticks, NextBits24(ref seed));

            return new EnvironmentalParticleRecord(stableKey, ordinal,
                descriptor.StableLayerKey, descriptor.SpawnRegionKey, descriptor.Kind,
                position, descriptor.DriftVelocity, size, descriptor.Tint,
                descriptor.EmissiveStrength, spawnTick, lifetimeTicks);
        }

        private void AddParticle(EnvironmentalParticleRecord particle, int layerCapacity)
        {
            if (CountLayer(particle.StableLayerKey) >= layerCapacity)
                _particles.RemoveAt(FindEvictionIndex(particle.StableLayerKey));
            if (_particles.Count >= RoomCapacity)
                _particles.RemoveAt(FindEvictionIndex(stableLayerKey: null));
            _particles.Add(particle);
        }

        private int CountLayer(string stableLayerKey)
        {
            int count = 0;
            for (int i = 0; i < _particles.Count; i++)
                if (StringComparer.Ordinal.Equals(_particles[i].StableLayerKey,
                    stableLayerKey)) count++;
            return count;
        }

        private int FindEvictionIndex(string? stableLayerKey)
        {
            int selected = -1;
            for (int i = 0; i < _particles.Count; i++)
            {
                EnvironmentalParticleRecord candidate = _particles[i];
                if (stableLayerKey != null && !StringComparer.Ordinal.Equals(
                    stableLayerKey, candidate.StableLayerKey)) continue;
                if (selected < 0 || CompareParticles(candidate, _particles[selected]) < 0)
                    selected = i;
            }
            if (selected < 0)
                throw new InvalidOperationException("No environmental particle is available for eviction.");
            return selected;
        }

        private void RemoveExpired(long presentationTick)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
                if (_particles[i].IsExpired(presentationTick)) _particles.RemoveAt(i);
        }

        private EnvironmentalParticleSnapshot CreateSnapshot(string roomKey,
            long schedulingTick, long sampleTick)
        {
            var records = _particles.ToArray();
            Array.Sort(records, CompareParticles);
            var samples = new EnvironmentalParticleSample[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                EnvironmentalParticleRecord particle = records[i];
                long ageTicks = sampleTick - particle.SpawnedAtTick;
                samples[i] = new EnvironmentalParticleSample(particle,
                    particle.PositionAt(sampleTick), TimeSpan.FromTicks(ageTicks));
            }
            return new EnvironmentalParticleSnapshot(roomKey, schedulingTick,
                sampleTick, samples);
        }

        private string RequireRoom()
            => _roomKey ?? throw new InvalidOperationException(
                "EnterRoom must establish a presentation timeline before advancing it.");

        private static int CompareParticles(EnvironmentalParticleRecord left,
            EnvironmentalParticleRecord right)
        {
            int comparison = left.SpawnedAtTick.CompareTo(right.SpawnedAtTick);
            if (comparison != 0) return comparison;
            comparison = left.StableKey.CompareTo(right.StableKey);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(left.StableLayerKey,
                right.StableLayerKey);
            return comparison != 0
                ? comparison
                : left.SpawnOrdinal.CompareTo(right.SpawnOrdinal);
        }

        private static long ValidateNonNegativeTime(TimeSpan presentationTime)
        {
            if (presentationTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(presentationTime));
            return presentationTime.Ticks;
        }

        private static void ValidateDescriptor(
            EnvironmentalParticleLayerDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(descriptor.StableLayerKey)
                || descriptor.MaximumParticles < 1)
                throw new ArgumentException("Particle layer must be initialized.",
                    nameof(descriptor));
            if (descriptor.EmissionRatePerSecond > MaximumEmissionRatePerSecond)
                throw new ArgumentOutOfRangeException(nameof(descriptor),
                    $"Runtime emission rate may not exceed {MaximumEmissionRatePerSecond} per second.");
        }

        private static float Interpolate(float minimum, float maximum, float unit)
            => (float)((double)minimum + ((double)maximum - minimum) * unit);

        private static long InterpolateTicks(long minimum, long maximum, uint bits24)
        {
            if (minimum == maximum) return minimum;
            BigInteger range = (BigInteger)maximum - minimum;
            BigInteger offset = range * bits24 / (1 << 24);
            return minimum + (long)offset;
        }

        private static uint NextBits24(ref ulong state)
        {
            state = Mix(state);
            return (uint)(state >> 40);
        }

        private static float NextUnit(ref ulong state)
            => NextBits24(ref state) * (1f / 16_777_216f);

        private static ulong StableStringHash(string value)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            foreach (byte item in Encoding.UTF8.GetBytes(value))
            {
                hash ^= item;
                hash = unchecked(hash * prime);
            }
            return hash;
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value = unchecked(value * 0xBF58476D1CE4E5B9UL);
            value ^= value >> 27;
            value = unchecked(value * 0x94D049BB133111EBUL);
            return value ^ (value >> 31);
        }

        private sealed class LayerState
        {
            public LayerState(EnvironmentalParticleLayerDescriptor descriptor)
            {
                Descriptor = descriptor;
                Emission = PositiveFloatRatio.Create(descriptor.EmissionRatePerSecond);
            }

            public EnvironmentalParticleLayerDescriptor Descriptor { get; }
            public PositiveFloatRatio Emission { get; }
            public ulong NextSpawnOrdinal { get; set; }

            public static int CompareIdentity(LayerState left, LayerState right)
            {
                int comparison = StringComparer.Ordinal.Compare(
                    left.Descriptor.StableLayerKey, right.Descriptor.StableLayerKey);
                return comparison != 0
                    ? comparison
                    : left.Descriptor.SpawnRegionKey.CompareTo(
                        right.Descriptor.SpawnRegionKey);
            }
        }

        /// <summary>Exact positive IEEE-754 float ratio used only with integer ticks.</summary>
        private readonly struct PositiveFloatRatio
        {
            private PositiveFloatRatio(BigInteger numerator, BigInteger denominator)
            {
                Numerator = numerator;
                Denominator = denominator;
            }

            private BigInteger Numerator { get; }
            private BigInteger Denominator { get; }

            public static PositiveFloatRatio Create(float value)
            {
                uint bits = BitConverter.SingleToUInt32Bits(value);
                uint exponentBits = (bits >> 23) & 0xFF;
                uint fraction = bits & 0x7FFFFF;
                BigInteger significand;
                int exponent;
                if (exponentBits == 0)
                {
                    significand = fraction;
                    exponent = -149;
                }
                else
                {
                    significand = (1U << 23) | fraction;
                    exponent = (int)exponentBits - 150;
                }
                return exponent >= 0
                    ? new PositiveFloatRatio(significand << exponent, BigInteger.One)
                    : new PositiveFloatRatio(significand, BigInteger.One << -exponent);
            }

            public ulong CumulativeCount(long elapsedTicks)
            {
                BigInteger count = (BigInteger)elapsedTicks * Numerator
                    / (TimeSpan.TicksPerSecond * Denominator);
                return (ulong)count;
            }

            public long SpawnTick(ulong zeroBasedOrdinal)
            {
                BigInteger numerator = ((BigInteger)zeroBasedOrdinal + 1)
                    * TimeSpan.TicksPerSecond * Denominator;
                BigInteger tick = (numerator + Numerator - 1) / Numerator;
                return (long)tick;
            }
        }
    }
}
