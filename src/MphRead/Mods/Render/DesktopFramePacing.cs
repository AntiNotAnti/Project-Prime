using System;

namespace MphRead.Mods.Render;

/// <summary>
/// Pure desktop presentation policy. Window/GLFW/WebGPU ownership stays with
/// RenderWindow; this class only decides which clock should pace presentation.
/// </summary>
internal static class DesktopFramePacing
{
    internal const double NativeRefreshToleranceHz = 0.75;

    /// <summary>
    /// Only explicit Display/VSync uses compositor pacing. Even when an FPS
    /// cap equals the monitor refresh, the cap remains a software deadline
    /// whenever presentation is nonblocking. Drivers and VRR may otherwise
    /// exceed a numeric limit (such as 240 on a 240 Hz monitor).
    ///
    /// Keep this check independent of FrameTiming: the stand-alone policy
    /// regression executable compiles this source without the whole game.
    /// </summary>
    internal static bool UseDisplayPacing(int cap, double refreshRate) =>
        cap == 0;

    internal static bool LinuxVSyncIgnored(bool isLinux, int cap, double refreshRate,
        double measuredFrameRate, bool alreadyLatched)
    {
        if (!isLinux || refreshRate <= 0 || !UseDisplayPacing(cap, refreshRate))
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
        bool displayPaced, bool modernPresentationBlocks, bool linuxVSyncFallback)
    {
        if (linuxVSyncFallback)
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
