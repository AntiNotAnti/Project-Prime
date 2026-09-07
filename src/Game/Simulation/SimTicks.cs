namespace MphRead
{
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
