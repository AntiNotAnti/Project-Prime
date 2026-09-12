using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Result of advancing the presentation-only transient-light timeline.
    /// A rewind discards every light from the abandoned timeline.
    /// </summary>
    public enum TransientVisualLightAdvance
    {
        Advanced,
        SameTick,
        Rewound
    }

    /// <summary>
    /// Bounded, presentation-owned lifetime storage for short-lived visual
    /// lights. It owns no entity, gameplay, network, or simulation state.
    /// </summary>
    public sealed class TransientVisualLightPool
    {
        public const int MaximumLights = 128;

        private readonly List<Entry> _entries = new(MaximumLights);
        private ulong _presentationTick;
        private TimeSpan _presentationTime;
        private bool _hasTimeline;

        public int Count => _entries.Count;
        public bool HasTimeline => _hasTimeline;
        public ulong PresentationTick => _hasTimeline ? _presentationTick : 0;
        public TimeSpan PresentationTime
            => _hasTimeline ? _presentationTime : TimeSpan.Zero;

        /// <summary>
        /// Establishes the current presentation boundary and expires lights at
        /// the exact end of their half-open lifetime interval. Repeated calls
        /// for the same tick and time are idempotent.
        /// </summary>
        public TransientVisualLightAdvance Advance(ulong presentationTick,
            TimeSpan presentationTime)
        {
            ValidateTimeline(presentationTime);
            if (!_hasTimeline)
            {
                SetTimeline(presentationTick, presentationTime);
                return TransientVisualLightAdvance.Advanced;
            }

            if (presentationTick < _presentationTick
                || presentationTime < _presentationTime)
            {
                _entries.Clear();
                SetTimeline(presentationTick, presentationTime);
                return TransientVisualLightAdvance.Rewound;
            }

            if (presentationTick == _presentationTick)
            {
                if (presentationTime != _presentationTime)
                {
                    throw new ArgumentException(
                        "One presentation tick cannot have multiple times.",
                        nameof(presentationTime));
                }
                return TransientVisualLightAdvance.SameTick;
            }

            SetTimeline(presentationTick, presentationTime);
            ExpireAtCurrentTime();
            return TransientVisualLightAdvance.Advanced;
        }

        /// <summary>
        /// Captures one immutable transient light at the current presentation
        /// boundary. An active source key is admitted once, making duplicate
        /// event delivery in one tick harmless without extending its lifetime.
        /// </summary>
        public bool TrySpawn(ulong sourceKey, Vector3 position,
            VisualLightProfile profile)
        {
            if (!_hasTimeline)
            {
                throw new InvalidOperationException(
                    "Advance must establish the presentation timeline before spawning.");
            }
            if (sourceKey == 0)
                throw new ArgumentOutOfRangeException(nameof(sourceKey));

            // Candidate construction applies the shared finite position/profile
            // validation, including the quadratic-only falloff contract.
            var candidate = new VisualLightCandidate(sourceKey, position, profile);
            TimeSpan lifetime;
            TimeSpan expirationTime;
            try
            {
                lifetime = TimeSpan.FromSeconds(profile.Lifetime);
                expirationTime = _presentationTime + lifetime;
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(nameof(profile),
                    "Visual light expiration must be finite and after spawn time.");
            }
            if (lifetime <= TimeSpan.Zero || expirationTime <= _presentationTime)
                throw new ArgumentOutOfRangeException(nameof(profile));

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Candidate.SourceKey == sourceKey) return false;
            }

            var incoming = new Entry(candidate, _presentationTick,
                _presentationTime, expirationTime);
            if (_entries.Count < MaximumLights)
            {
                _entries.Add(incoming);
                return true;
            }

            int worstIndex = 0;
            for (int i = 1; i < _entries.Count; i++)
            {
                if (CompareAdmission(_entries[worstIndex], _entries[i]) < 0)
                    worstIndex = i;
            }
            if (CompareAdmission(incoming, _entries[worstIndex]) >= 0) return false;
            _entries[worstIndex] = incoming;
            return true;
        }

        /// <summary>
        /// Returns immutable candidates in deterministic admission order:
        /// priority descending, then stable source key ascending.
        /// </summary>
        public VisualLightCandidate[] Snapshot()
        {
            if (_entries.Count == 0) return Array.Empty<VisualLightCandidate>();
            var ordered = _entries.ToArray();
            Array.Sort(ordered, CompareAdmission);
            var result = new VisualLightCandidate[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
                result[i] = ordered[i].Candidate;
            return result;
        }

        /// <summary>Discards all lights and the current timeline boundary.</summary>
        public void Reset()
        {
            _entries.Clear();
            _presentationTick = 0;
            _presentationTime = TimeSpan.Zero;
            _hasTimeline = false;
        }

        private void ExpireAtCurrentTime()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].ExpirationTime <= _presentationTime)
                    _entries.RemoveAt(i);
            }
        }

        private void SetTimeline(ulong presentationTick, TimeSpan presentationTime)
        {
            _presentationTick = presentationTick;
            _presentationTime = presentationTime;
            _hasTimeline = true;
        }

        private static int CompareAdmission(Entry left, Entry right)
        {
            int priority = right.Candidate.Profile.Priority.CompareTo(
                left.Candidate.Profile.Priority);
            return priority != 0 ? priority
                : left.Candidate.SourceKey.CompareTo(right.Candidate.SourceKey);
        }

        private static void ValidateTimeline(TimeSpan presentationTime)
        {
            if (presentationTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(presentationTime));
        }

        private readonly record struct Entry(
            VisualLightCandidate Candidate,
            ulong SpawnTick,
            TimeSpan SpawnTime,
            TimeSpan ExpirationTime);
    }
}
