using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Produces the small, deterministic hemisphere kernel used by the
    /// half-resolution Enhanced SSAO pass. The explicit generator avoids
    /// framework-random behavior changing samples between platforms.
    /// </summary>
    internal static class EnhancedSsaoKernel
    {
        public const int MinimumSampleCount = 8;
        public const int MaximumSampleCount = 12;
        public const uint DefaultSeed = 0x5052494D;

        public static IReadOnlyList<Vector3> Create(int sampleCount,
            uint seed = DefaultSeed)
        {
            if (sampleCount < MinimumSampleCount || sampleCount > MaximumSampleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            }

            var samples = new Vector3[sampleCount];
            uint state = seed ^ 0x9E3779B9u;
            for (int i = 0; i < samples.Length; i++)
            {
                var direction = new Vector3(
                    NextUnit(ref state) * 2 - 1,
                    NextUnit(ref state) * 2 - 1,
                    NextUnit(ref state));
                if (direction.LengthSquared < 1e-8f)
                {
                    direction = Vector3.UnitZ;
                }
                else
                {
                    direction.Normalize();
                }

                float radius = 0.1f + 0.9f * NextUnit(ref state);
                float progress = sampleCount == 1 ? 1 : i / (sampleCount - 1f);
                float distribution = 0.1f + 0.9f * progress * progress;
                samples[i] = direction * radius * distribution;
            }
            return Array.AsReadOnly(samples);
        }

        private static float NextUnit(ref uint state)
        {
            // Xorshift32 has a fully specified result on every target. Avoid
            // its all-zero fixed point without making zero an invalid seed.
            if (state == 0) state = 0x6D2B79F5u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) * (1f / 16_777_216f);
        }
    }
}
