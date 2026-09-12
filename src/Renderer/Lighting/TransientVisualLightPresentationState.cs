using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    public enum TransientVisualLightSourceKind : byte
    {
        MuzzleFlash = 1
    }

    /// <summary>Stable reset-scoped identities for explicit presentation events.</summary>
    public static class TransientVisualLightSourceKey
    {
        public static ulong ForEvent(TransientVisualLightSourceKind kind,
            ulong presentationScope, uint eventId)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (presentationScope == 0)
                throw new ArgumentOutOfRangeException(nameof(presentationScope));

            ulong hash = Append(14695981039346656037ul, 0x5452414E534C4954ul);
            hash = Append(hash, (byte)kind);
            hash = Append(hash, presentationScope);
            hash = Append(hash, eventId);
            return hash == 0 ? 1 : hash;
        }

        private const ulong Prime = 1099511628211ul;
        private static ulong Append(ulong hash, byte value)
            => (hash ^ value) * Prime;
        private static ulong Append(ulong hash, uint value)
        {
            hash = Append(hash, (byte)value);
            hash = Append(hash, (byte)(value >> 8));
            hash = Append(hash, (byte)(value >> 16));
            return Append(hash, (byte)(value >> 24));
        }
        private static ulong Append(ulong hash, ulong value)
        {
            hash = Append(hash, (uint)value);
            return Append(hash, (uint)(value >> 32));
        }
    }

    /// <summary>
    /// Bounded staging between discrete presentation events and the next
    /// immutable render-frame capture. No gameplay object is retained.
    /// </summary>
    internal sealed class TransientVisualLightPresentationState
    {
        private readonly TransientVisualLightPool _pool = new();
        private readonly List<VisualLightCandidate> _pending
            = new(TransientVisualLightPool.MaximumLights);

        public int ActiveCount => _pool.Count;
        public int PendingCount => _pending.Count;

        public bool TryQueue(ulong stableSourceKey, Vector3 position,
            VisualLightProfile profile)
        {
            if (stableSourceKey == 0)
                throw new ArgumentOutOfRangeException(nameof(stableSourceKey));
            var candidate = new VisualLightCandidate(stableSourceKey, position, profile);
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].SourceKey == stableSourceKey) return false;
            }

            if (_pending.Count < TransientVisualLightPool.MaximumLights)
            {
                _pending.Add(candidate);
                return true;
            }

            int worstIndex = 0;
            for (int i = 1; i < _pending.Count; i++)
            {
                if (Compare(_pending[worstIndex], _pending[i]) < 0)
                    worstIndex = i;
            }
            if (Compare(candidate, _pending[worstIndex]) >= 0) return false;
            _pending[worstIndex] = candidate;
            return true;
        }

        public VisualLightCandidate[] CaptureFrame(ulong presentationTick,
            TimeSpan presentationTime)
        {
            _pool.Advance(presentationTick, presentationTime);
            if (_pending.Count != 0)
            {
                _pending.Sort(Compare);
                for (int i = 0; i < _pending.Count; i++)
                {
                    VisualLightCandidate candidate = _pending[i];
                    _pool.TrySpawn(candidate.SourceKey, candidate.Position,
                        candidate.Profile);
                }
                _pending.Clear();
            }
            return _pool.Snapshot();
        }

        public void Reset()
        {
            _pending.Clear();
            _pool.Reset();
        }

        private static int Compare(VisualLightCandidate left,
            VisualLightCandidate right)
        {
            int priority = right.Profile.Priority.CompareTo(left.Profile.Priority);
            return priority != 0 ? priority
                : left.SourceKey.CompareTo(right.SourceKey);
        }
    }
}
