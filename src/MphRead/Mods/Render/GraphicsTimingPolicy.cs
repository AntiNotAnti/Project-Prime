using System;
using System.Threading;

namespace MphRead.Mods.Render;

// Device features are immutable. Enable this before renderer creation; ordinary
// release gameplay keeps timestamp queries and their readback buffers absent.
internal static class GraphicsTimingPolicy
{
    private static int _enabled = Environment.GetEnvironmentVariable("PP_GPU_TIMING") == "1" ? 1 : 0;
    internal static bool Enabled => Volatile.Read(ref _enabled) != 0;
    internal static void Enable() => Volatile.Write(ref _enabled, 1);
}
