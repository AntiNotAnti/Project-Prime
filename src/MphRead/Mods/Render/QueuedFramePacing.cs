using System;

namespace MphRead.Mods.Render;

internal static class QueuedFramePacing
{
    internal static double SelectedCadenceHz(int frameCap, double displayHz, bool presentationPaced)
    {
        double knownDisplay = double.IsFinite(displayHz) && displayHz > 0 ? displayHz : 0;
        if (frameCap > 0)
            return presentationPaced && knownDisplay > 0 ? Math.Min(frameCap, knownDisplay) : frameCap;
        return presentationPaced ? knownDisplay : 0;
    }

    internal static double RemainingMilliseconds(long frameStart, long now, long ticksPerSecond, int frameCap)
    {
        if (ticksPerSecond <= 0 || now < frameStart)
            throw new ArgumentOutOfRangeException(nameof(now));
        if (frameCap <= 0) return 0;
        double elapsed = (now - (double)frameStart) * 1000 / ticksPerSecond;
        return Math.Max(0, 1000d / frameCap - elapsed);
    }
}
