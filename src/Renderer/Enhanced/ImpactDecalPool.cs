using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>Presentation-only mark categories; none has gameplay authority.</summary>
    public enum ImpactDecalKind
    {
        BeamMark,
        MissileScorch,
        ExplosionMark,
        EnergyBurn
    }

    /// <summary>An immutable, local visual decal submitted to <see cref="ImpactDecalPool"/>.</summary>
    public readonly record struct ImpactDecalDescriptor
    {
        public ImpactDecalDescriptor(ulong stableKey, ulong regionKey, ImpactDecalKind kind,
            Vector3 position, Vector3 normal, float radius, Vector4 tint,
            TimeSpan spawnedAt, TimeSpan lifetime)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            RequireFinite(position, nameof(position));
            RequireFinite(normal, nameof(normal));
            float normalLengthSquared = normal.LengthSquared;
            if (!float.IsFinite(normalLengthSquared) || normalLengthSquared <= 1e-8f)
            {
                throw new ArgumentOutOfRangeException(nameof(normal));
            }
            if (!float.IsFinite(radius) || radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (!IsUnitColor(tint))
            {
                throw new ArgumentOutOfRangeException(nameof(tint));
            }
            if (spawnedAt < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(spawnedAt));
            }
            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }

            StableKey = stableKey;
            RegionKey = regionKey;
            Kind = kind;
            Position = position;
            Normal = normal / MathF.Sqrt(normalLengthSquared);
            Radius = radius;
            Tint = tint;
            SpawnedAt = spawnedAt;
            Lifetime = lifetime;
        }

        public ulong StableKey { get; }
        public ulong RegionKey { get; }
        public ImpactDecalKind Kind { get; }
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public float Radius { get; }
        public Vector4 Tint { get; }
        public TimeSpan SpawnedAt { get; }
        public TimeSpan Lifetime { get; }

        internal bool IsInitialized => Radius > 0 && Lifetime > TimeSpan.Zero;

        internal bool IsExpired(TimeSpan presentationTime)
            => presentationTime >= SpawnedAt
                && presentationTime.Ticks - SpawnedAt.Ticks >= Lifetime.Ticks;

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
    /// Fixed-capacity local decal storage. Expiry and eviction use presentation
    /// time only and cannot influence authoritative state.
    /// </summary>
    public sealed class ImpactDecalPool
    {
        public const int MaximumSupportedCapacity = 4096;

        private readonly List<ImpactDecalDescriptor> _items;
        private readonly ReadOnlyCollection<ImpactDecalDescriptor> _readOnlyItems;
        private TimeSpan? _lastPresentationTime;

        public ImpactDecalPool(int globalCapacity, int perRegionCapacity)
        {
            if (globalCapacity < 1 || globalCapacity > MaximumSupportedCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(globalCapacity));
            }
            if (perRegionCapacity < 1 || perRegionCapacity > globalCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(perRegionCapacity));
            }

            GlobalCapacity = globalCapacity;
            PerRegionCapacity = perRegionCapacity;
            _items = new List<ImpactDecalDescriptor>(globalCapacity);
            _readOnlyItems = _items.AsReadOnly();
        }

        public int GlobalCapacity { get; }
        public int PerRegionCapacity { get; }
        public IReadOnlyList<ImpactDecalDescriptor> Items => _readOnlyItems;

        /// <summary>
        /// Adds one non-expired unique decal. Returns false for a duplicate key or
        /// a decal already expired at the observed presentation time.
        /// </summary>
        public bool TryAdd(ImpactDecalDescriptor decal, TimeSpan presentationTime)
        {
            ValidateTime(presentationTime);
            if (!decal.IsInitialized)
            {
                throw new ArgumentException("Decal must be initialized.", nameof(decal));
            }
            if (decal.SpawnedAt > presentationTime)
            {
                throw new ArgumentOutOfRangeException(nameof(decal),
                    "A decal cannot spawn after the observed presentation time.");
            }

            _lastPresentationTime = presentationTime;
            RemoveExpiredCore(presentationTime);
            if (decal.IsExpired(presentationTime))
            {
                return false;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].StableKey == decal.StableKey)
                {
                    return false;
                }
            }

            if (CountRegion(decal.RegionKey) >= PerRegionCapacity)
            {
                _items.RemoveAt(FindEvictionIndex(decal.RegionKey));
            }
            if (_items.Count >= GlobalCapacity)
            {
                _items.RemoveAt(FindEvictionIndex(regionKey: null));
            }
            _items.Add(decal);
            return true;
        }

        /// <summary>Expires decals at a monotonic presentation time.</summary>
        public int Advance(TimeSpan presentationTime)
        {
            ObserveTime(presentationTime);
            return RemoveExpiredCore(presentationTime);
        }

        public int CountRegion(ulong regionKey)
        {
            int count = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].RegionKey == regionKey)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Clears visual marks while preserving the current presentation clock.</summary>
        public void Clear() => _items.Clear();

        /// <summary>Clears visual marks and starts a new presentation timeline.</summary>
        public void Reset()
        {
            _items.Clear();
            _lastPresentationTime = null;
        }

        private void ObserveTime(TimeSpan presentationTime)
        {
            ValidateTime(presentationTime);
            _lastPresentationTime = presentationTime;
        }

        private void ValidateTime(TimeSpan presentationTime)
        {
            if (presentationTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(presentationTime));
            }
            if (_lastPresentationTime.HasValue
                && presentationTime < _lastPresentationTime.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(presentationTime),
                    "Presentation time must be monotonic until the pool is reset.");
            }
        }

        private int RemoveExpiredCore(TimeSpan presentationTime)
        {
            int removed = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].IsExpired(presentationTime))
                {
                    _items.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        private int FindEvictionIndex(ulong? regionKey)
        {
            int selectedIndex = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                ImpactDecalDescriptor candidate = _items[i];
                if (regionKey.HasValue && candidate.RegionKey != regionKey.Value)
                {
                    continue;
                }
                if (selectedIndex < 0 || CompareEviction(candidate, _items[selectedIndex]) < 0)
                {
                    selectedIndex = i;
                }
            }
            if (selectedIndex < 0)
            {
                throw new InvalidOperationException("No decal is available for eviction.");
            }
            return selectedIndex;
        }

        private static int CompareEviction(ImpactDecalDescriptor left,
            ImpactDecalDescriptor right)
        {
            int comparison = left.SpawnedAt.CompareTo(right.SpawnedAt);
            return comparison != 0
                ? comparison
                : left.StableKey.CompareTo(right.StableKey);
        }
    }
}
