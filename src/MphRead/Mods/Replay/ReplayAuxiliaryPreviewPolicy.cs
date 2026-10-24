using System.Diagnostics;

namespace MphRead.Mods.Replay;

internal static class ReplayAuxiliaryPreviewPolicy
{
    // Only optional auxiliary tiles age during a gesture. The main camera is
    // always fresh, and releasing the gesture forces a full quality refresh.
    internal static bool Reuse(bool interacting, bool valid, long now, long completed)
        => interacting && valid && now >= completed
            && Stopwatch.GetElapsedTime(completed, now).TotalMilliseconds < 100;
}
