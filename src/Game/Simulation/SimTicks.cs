using System;

namespace MphRead
{
    /// <summary>
    /// An absolute position on the fixed 60 Hz simulation clock. Arithmetic is
    /// explicitly unchecked so the existing uint wire-clock wrap semantics are
    /// preserved.
    /// </summary>
    public readonly record struct SimTick(uint Value)
    {
        public SimDuration ElapsedSince(SimTick earlier)
            => new(unchecked(Value - earlier.Value));

        /// <summary>
        /// Tests a duration within the unambiguous forward half of the uint
        /// clock. A value from the future is never treated as already due.
        /// </summary>
        public bool HasElapsedSince(SimTick earlier, SimDuration duration)
        {
            uint elapsed = unchecked(Value - earlier.Value);
            return elapsed <= Int32.MaxValue && elapsed >= duration.Ticks;
        }

        public static SimTick operator +(SimTick tick, SimDuration duration)
            => new(unchecked(tick.Value + duration.Ticks));
    }

    /// <summary>A non-negative duration measured in authoritative 60 Hz ticks.</summary>
    public readonly record struct SimDuration(uint Ticks)
    {
        public static SimDuration Zero => default;
        public double TotalSeconds => Ticks / (double)SimTicks.Hz;

        public static SimDuration FromSeconds(int seconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(seconds);
            return new(checked((uint)seconds * SimTicks.Hz));
        }

        /// <summary>Converts whole milliseconds, truncating fractional ticks.</summary>
        public static SimDuration FromMilliseconds(int milliseconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
            return new(checked((uint)((long)milliseconds * SimTicks.Hz / 1000)));
        }

        public static SimDuration operator +(SimDuration left, SimDuration right)
            => new(checked(left.Ticks + right.Ticks));
    }

    /// <summary>Integer durations and offsets on the fixed 60 Hz simulation clock.</summary>
    public static class SimTicks
    {
        public const int Hz = 60;
        public const int LegacyHz = 30;
        public const int TicksPer30HzFrame = Hz / LegacyHz;
        // Retained float-second timers must keep their original multiplication/division order.
        public const float LegacyFrameSeconds = 1f / LegacyHz;

        public static int FromSeconds(int seconds) => checked(seconds * Hz);

        /// <summary>Converts whole milliseconds, truncating fractional ticks toward zero.</summary>
        public static int FromMilliseconds(int milliseconds)
            => checked((int)((long)milliseconds * Hz / 1000));

        public static int From30HzFrames(int frames) => checked(frames * TicksPer30HzFrame);
    }
}
