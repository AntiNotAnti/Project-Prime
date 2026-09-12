using System;

namespace MphRead
{
    /// <summary>Small deterministic helpers shared by local visual animation profiles.</summary>
    internal static class PresentationTimeMath
    {
        public static double Seconds(TimeSpan presentationTime)
            => presentationTime.TotalSeconds;

        public static float StableUnit(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9ul;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBul;
            value ^= value >> 31;
            return (value >> 40) * (1f / 16_777_216f);
        }

        public static float Fraction(double value)
        {
            float result = (float)(value - Math.Floor(value));
            // A double fraction just below one can round to exactly one when
            // converted to float. Keep wrapped shader coordinates in [0, 1).
            return result < 1 ? result : 0.99999994f;
        }
    }
}
