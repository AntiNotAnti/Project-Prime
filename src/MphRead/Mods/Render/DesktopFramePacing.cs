using System;

namespace MphRead.Mods.Render;

/// <summary>
/// Pure desktop presentation policy. Window/GLFW ownership stays with
/// RenderWindow; this class only decides which clock should pace presentation.
/// </summary>
internal static class DesktopFramePacing
{
    internal const double NativeRefreshToleranceHz = 0.75;

    /// <summary>
    /// Only the explicit Display/VSync setting relies on the monitor clock.
    /// Every positive numeric cap needs its own software deadline, including
    /// a 240 FPS cap on a 240 Hz monitor: the driver's swap interval may be
    /// ignored or overridden, and must not silently disable the FPS limit.
    /// </summary>
    internal static bool UseDisplayPacing(int cap, double refreshRate) =>
        cap == FrameTiming.DisplayRate;

    internal static bool LinuxVSyncIgnored(bool isLinux, int cap, double refreshRate,
        double measuredFrameRate, bool alreadyLatched)
        => DisplayVSyncIgnored(isLinux, cap, refreshRate, measuredFrameRate, alreadyLatched);

    internal static bool MacVSyncIgnored(bool isMac, int cap, double refreshRate,
        double measuredFrameRate, bool alreadyLatched)
        => DisplayVSyncIgnored(isMac, cap, refreshRate, measuredFrameRate, alreadyLatched);

    private static bool DisplayVSyncIgnored(bool enabled, int cap, double refreshRate,
        double measuredFrameRate, bool alreadyLatched)
    {
        if (!enabled || refreshRate <= 0 || !UseDisplayPacing(cap, refreshRate))
        {
            return false;
        }
        if (alreadyLatched)
        {
            return true;
        }

        // FrameTiming measures a two-second window. Require both a relative and
        // absolute margin so 59.94/60 and normal compositor jitter never trip
        // the fallback, while a 144 Hz display accidentally drawing at 180+ does.
        return measuredFrameRate > refreshRate * 1.12
            && measuredFrameRate - refreshRate > 5.0;
    }

    internal static double SoftwareFrequency(int cap, double refreshRate,
        bool displayPaced, bool modernPresentationBlocks, bool displayVSyncFallback)
    {
        if (displayVSyncFallback)
        {
            return refreshRate > 0 ? refreshRate : 0;
        }

        // A blocking FIFO presentation clock and a software deadline must never
        // pace the same explicit cap. If a modern backend cannot offer Immediate
        // or Mailbox, prefer smooth display cadence over a double-paced cap.
        if (displayPaced || modernPresentationBlocks)
        {
            return 0;
        }

        return cap > 0 ? cap : 0;
    }
}
