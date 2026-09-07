using System;
using System.Collections.Generic;

namespace MphRead
{
    /// <summary>
    /// Immutable compatibility samples for the old binary32 fixed-step clock.
    /// Mutable simulation state stores tick indices, never accumulated seconds.
    /// These projections preserve authored interpolation and expiration rounding.
    /// </summary>
    internal static class LegacyTickProjection
    {
        // All authored durations are ushort 30 Hz frames. This margin covers the
        // binary32 accumulation error at that maximum (verified by boundary tests).
        internal const int MaximumTicks = ushort.MaxValue * SimTicks.TicksPer30HzFrame + 4096;
        private static readonly float[] ElapsedSamples = CreateElapsed();
        private static readonly Dictionary<float, Countdown> AuthoredCountdowns = CreateAuthored();

        private static float[] CreateElapsed()
        {
            var samples = new float[MaximumTicks + 1];
            for (int tick = 1; tick < samples.Length; tick++)
                samples[tick] = samples[tick - 1] + 1f / SimTicks.Hz;
            return samples;
        }

        internal static float Elapsed(int ticks) => ElapsedSamples[ticks];

        internal static int FirstAtLeast(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds > ElapsedSamples[^1])
                throw new ArgumentOutOfRangeException(nameof(seconds));
            int low = 0, high = ElapsedSamples.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (ElapsedSamples[middle] < seconds) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        internal static int LastAtMost(float seconds)
        {
            int first = FirstAtLeast(seconds);
            return ElapsedSamples[first] > seconds ? first - 1 : first;
        }

        private static Dictionary<float, Countdown> CreateAuthored()
        {
            var values = new Dictionary<float, Countdown>();
            void Add(float seconds)
            {
                if (!values.ContainsKey(seconds)) values.Add(seconds, new Countdown(seconds));
            }
            Add(0);
            Add(4 * SimTicks.LegacyFrameSeconds); // collided beam tail
            foreach (IReadOnlyList<WeaponInfo> table in new[] { Weapons.WeaponsMP, Weapons.PlatformWeapons,
                Weapons.Ricochets, Weapons.ForceFieldLockWeapons })
                foreach (WeaponInfo weapon in table)
                {
                    Add(weapon.UnchargedLifespan * SimTicks.LegacyFrameSeconds);
                    Add(weapon.MinChargeLifespan * SimTicks.LegacyFrameSeconds);
                    Add(weapon.ChargedLifespan * SimTicks.LegacyFrameSeconds);
                }
            return values;
        }

        internal static Countdown ForCountdown(float seconds)
            => AuthoredCountdowns.TryGetValue(seconds, out Countdown? value) ? value : new Countdown(seconds);

        internal sealed class Countdown
        {
            private readonly float[] _samples;
            internal int Ticks => _samples.Length - 1;
            internal float Remaining(int remainingTicks) => _samples[Ticks - remainingTicks];

            internal Countdown(float seconds)
            {
                if (!float.IsFinite(seconds) || seconds > ushort.MaxValue * SimTicks.LegacyFrameSeconds)
                    throw new ArgumentOutOfRangeException(nameof(seconds));
                var values = new List<float> { seconds };
                while (seconds > 0)
                {
                    if (values.Count > MaximumTicks) throw new ArgumentOutOfRangeException(nameof(seconds));
                    seconds -= 1f / SimTicks.Hz;
                    values.Add(seconds);
                }
                _samples = values.ToArray();
            }
        }
    }
}
