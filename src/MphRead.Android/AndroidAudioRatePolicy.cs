using System;

namespace MphRead.Droid;

/// <summary>Pure validation of optional native-rate Android mixer experiments.</summary>
internal static class AndroidAudioRatePolicy
{
    internal static int Select(int originalRate, int deviceRate, bool nativeEnabled)
        => nativeEnabled && deviceRate >= 32000 && deviceRate <= 192000
            ? deviceRate : originalRate;

    internal static double BufferPeriodMs(int burstFrames, int sampleRate)
        => burstFrames > 0 && sampleRate > 0
            ? burstFrames * 1000.0 / sampleRate : 0;
}
