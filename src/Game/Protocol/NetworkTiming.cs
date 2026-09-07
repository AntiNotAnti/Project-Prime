namespace MphRead.Mods.Network
{
    /// <summary>Timing contract shared by snapshot presentation and server rewind policy.</summary>
    public static class NetworkTiming
    {
        public const uint InterpolationDelayTicks = 6; // Fixed 100 ms at 60 Hz.
    }
}
