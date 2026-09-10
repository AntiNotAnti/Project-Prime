using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// One presentation-owned visual-light candidate. SourceKey must be stable
    /// for the visible source across frames; it is never a gameplay identity.
    /// Lifetime remains metadata for continuously resubmitted sources. A
    /// lifetime-managed transient-light pool is still remaining VE3 work.
    /// </summary>
    public readonly record struct VisualLightCandidate
    {
        private readonly RenderVisualLight _renderLight;

        public VisualLightCandidate(ulong sourceKey, Vector3 position, VisualLightProfile profile)
        {
            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position),
                    "Visual light position must be finite.");
            }
            // A default profile bypasses its validating constructor.
            if (profile.Radius <= 0 || profile.Intensity <= 0 || profile.Lifetime <= 0
                || profile.Falloff <= 0)
            {
                throw new ArgumentException("Visual light profile must be initialized.", nameof(profile));
            }
            SourceKey = sourceKey;
            Position = position;
            Profile = profile;
            CollapseBySourceKey = true;
            Lifetime = profile.Lifetime;
            Falloff = profile.Falloff;
            _renderLight = new RenderVisualLight(position, profile.Color,
                profile.Radius, profile.Intensity, profile.Priority);
        }

        private VisualLightCandidate(ulong sourceKey, RenderVisualLight light)
        {
            if (!IsFinite(light.Position) || !IsFinite(light.Color))
                throw new ArgumentException("Legacy visual light values must be finite.", nameof(light));
            if (!float.IsFinite(light.Radius) || light.Radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(light));
            if (!float.IsFinite(light.Intensity) || light.Intensity < 0)
                throw new ArgumentOutOfRangeException(nameof(light));
            SourceKey = sourceKey;
            Position = light.Position;
            Profile = default;
            CollapseBySourceKey = false;
            // Legacy sources are continuously submitted. Lifetime is retained
            // only as metadata until VE3 gains a transient-light pool.
            Lifetime = 1;
            Falloff = VisualLightProfile.SupportedFalloff;
            _renderLight = light;
        }

        public ulong SourceKey { get; }
        public Vector3 Position { get; }
        public VisualLightProfile Profile { get; }
        internal bool CollapseBySourceKey { get; }
        internal float Lifetime { get; }
        internal float Falloff { get; }
        internal RenderVisualLight RenderLight => _renderLight;

        internal static VisualLightCandidate FromLegacy(RenderVisualLight light)
            => FromLegacy(VisualLightSourceKey.ForLegacy(light), light);

        internal static VisualLightCandidate FromLegacy(ulong sourceKey,
            RenderVisualLight light)
            => new(sourceKey, light);

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    /// <summary>Inspectable, deterministic inputs to visual-light ranking.</summary>
    public readonly record struct VisualLightCandidateScore(
        int Priority,
        float CameraRelevance,
        float DistanceSquared,
        float Intensity,
        ulong SourceKey);

    public static class VisualLightSelection
    {
        public const int MaximumSupportedLights = 8;

        /// <summary>
        /// Selects the most relevant lights in rank order using memory bounded
        /// by <paramref name="maximumCount"/>. Priority dominates, followed by
        /// camera attenuation, distance, intensity, and stable source identity.
        /// </summary>
        public static VisualLightCandidate[] Select(IReadOnlyList<VisualLightCandidate> candidates,
            Vector3 cameraPosition, int maximumCount = MaximumSupportedLights)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            if (!IsFinite(cameraPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(cameraPosition));
            }
            if ((uint)maximumCount > MaximumSupportedLights)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            }
            if (maximumCount == 0 || candidates.Count == 0)
            {
                return Array.Empty<VisualLightCandidate>();
            }

            var selected = new VisualLightCandidate[maximumCount];
            int selectedCount = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                TryInsert(candidates[i], cameraPosition, selected, ref selectedCount);
            }

            if (selectedCount == selected.Length) return selected;
            var result = new VisualLightCandidate[selectedCount];
            selected.AsSpan(0, selectedCount).CopyTo(result);
            return result;
        }

        /// <summary>
        /// Inserts into a caller-owned fixed-capacity rank buffer. Profiled
        /// duplicate source keys collapse to the stronger candidate. Legacy
        /// compatibility candidates collapse only when all light values match.
        /// </summary>
        internal static bool TryInsert(VisualLightCandidate candidate,
            Vector3 cameraPosition, Span<VisualLightCandidate> selected,
            ref int selectedCount)
        {
            if (!IsFinite(cameraPosition))
                throw new ArgumentOutOfRangeException(nameof(cameraPosition));
            if (selected.Length > MaximumSupportedLights)
                throw new ArgumentOutOfRangeException(nameof(selected));
            if ((uint)selectedCount > (uint)selected.Length)
                throw new ArgumentOutOfRangeException(nameof(selectedCount));
            if (selected.Length == 0) return false;

            var ranked = new RankedCandidate(candidate, Score(candidate, cameraPosition));
            for (int i = 0; i < selectedCount; i++)
            {
                if (selected[i].SourceKey != candidate.SourceKey) continue;
                if (!candidate.CollapseBySourceKey || !selected[i].CollapseBySourceKey)
                {
                    // Legacy keys are compact hashes of exact light values.
                    // A hash collision is not source identity, so only an
                    // exactly identical legacy candidate is a duplicate.
                    if (candidate == selected[i]) return false;
                    continue;
                }
                var existing = new RankedCandidate(selected[i],
                    Score(selected[i], cameraPosition));
                if (Compare(ranked, existing) >= 0) return false;
                for (int j = i; j < selectedCount - 1; j++)
                {
                    selected[j] = selected[j + 1];
                }
                selectedCount--;
                break;
            }

            int insertion = selectedCount;
            for (int i = 0; i < selectedCount; i++)
            {
                var existing = new RankedCandidate(selected[i],
                    Score(selected[i], cameraPosition));
                if (Compare(ranked, existing) < 0)
                {
                    insertion = i;
                    break;
                }
            }
            if (insertion >= selected.Length) return false;

            int shiftEnd = Math.Min(selectedCount, selected.Length - 1);
            for (int i = shiftEnd; i > insertion; i--)
            {
                selected[i] = selected[i - 1];
            }
            selected[insertion] = candidate;
            if (selectedCount < selected.Length) selectedCount++;
            return true;
        }

        public static VisualLightCandidateScore Score(VisualLightCandidate candidate,
            Vector3 cameraPosition)
        {
            if (!IsFinite(cameraPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(cameraPosition));
            }
            Vector3 offset = candidate.Position - cameraPosition;
            float distanceSquared = offset.LengthSquared;
            float distance = MathF.Sqrt(distanceSquared);
            RenderVisualLight light = candidate.RenderLight;
            float relevance = light.Intensity
                * DistanceAttenuation(distance, light.Radius, candidate.Falloff);
            return new VisualLightCandidateScore(light.Priority, relevance,
                distanceSquared, light.Intensity, candidate.SourceKey);
        }

        public static float DistanceAttenuation(float distance, float radius, float falloff)
        {
            if (!float.IsFinite(distance) || distance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }
            if (!float.IsFinite(radius) || radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (!float.IsFinite(falloff) || falloff <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(falloff));
            }
            float linear = Math.Clamp(1 - distance / radius, 0, 1);
            return MathF.Pow(linear, falloff);
        }

        private static int Compare(RankedCandidate left, RankedCandidate right)
        {
            int comparison = right.Score.Priority.CompareTo(left.Score.Priority);
            if (comparison != 0) return comparison;
            comparison = right.Score.CameraRelevance.CompareTo(left.Score.CameraRelevance);
            if (comparison != 0) return comparison;
            comparison = left.Score.DistanceSquared.CompareTo(right.Score.DistanceSquared);
            if (comparison != 0) return comparison;
            comparison = right.Score.Intensity.CompareTo(left.Score.Intensity);
            if (comparison != 0) return comparison;
            comparison = left.Score.SourceKey.CompareTo(right.Score.SourceKey);
            if (comparison != 0) return comparison;

            // Source keys are expected to be unique. This final value ordering
            // still makes accidental duplicate-key candidates input-order neutral.
            comparison = CompareVector(left.Candidate.Position, right.Candidate.Position);
            if (comparison != 0) return comparison;
            comparison = CompareVector(left.Candidate.RenderLight.Color,
                right.Candidate.RenderLight.Color);
            if (comparison != 0) return comparison;
            comparison = left.Candidate.RenderLight.Radius.CompareTo(
                right.Candidate.RenderLight.Radius);
            if (comparison != 0) return comparison;
            comparison = left.Candidate.Lifetime.CompareTo(right.Candidate.Lifetime);
            if (comparison != 0) return comparison;
            return left.Candidate.Falloff.CompareTo(right.Candidate.Falloff);
        }

        private static int CompareVector(Vector3 left, Vector3 right)
        {
            int comparison = left.X.CompareTo(right.X);
            if (comparison != 0) return comparison;
            comparison = left.Y.CompareTo(right.Y);
            return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private readonly record struct RankedCandidate(
            VisualLightCandidate Candidate,
            VisualLightCandidateScore Score);
    }
}
