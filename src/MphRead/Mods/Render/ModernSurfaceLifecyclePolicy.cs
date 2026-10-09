namespace MphRead.Mods.Render;

internal enum SurfaceAcquireCondition
{
    Success,
    Timeout,
    Outdated,
    Lost,
    Fatal
}

internal enum SurfaceAcquireAction
{
    UseTexture,
    SkipFrame,
    Reconfigure,
    RecreateSurface,
    Fail
}

/// <summary>
/// Pure modern-surface lifecycle policy. Keeping the decision table outside
/// native WebGPU calls makes timeout/loss/minimize behavior deterministic in CI
/// while the platform smoke tests continue to exercise the real surfaces.
/// </summary>
internal static class ModernSurfaceLifecyclePolicy
{
    internal static bool CanConfigure(int width, int height, bool surfaceAvailable) =>
        width > 0 && height > 0 && surfaceAvailable;

    internal static SurfaceAcquireAction AcquireAction(
        SurfaceAcquireCondition condition, bool hasTexture, int attempt)
    {
        if (condition == SurfaceAcquireCondition.Success)
            return hasTexture ? SurfaceAcquireAction.UseTexture : SurfaceAcquireAction.Fail;
        if (condition == SurfaceAcquireCondition.Timeout)
            return SurfaceAcquireAction.SkipFrame;
        if (condition == SurfaceAcquireCondition.Outdated)
            return attempt == 0 ? SurfaceAcquireAction.Reconfigure : SurfaceAcquireAction.Fail;
        if (condition == SurfaceAcquireCondition.Lost)
            return attempt == 0 ? SurfaceAcquireAction.RecreateSurface : SurfaceAcquireAction.Fail;
        return SurfaceAcquireAction.Fail;
    }

    internal static bool PresentModeChangeRequiresReconfigure(
        bool oldVsync, bool newVsync, int width, int height, bool surfaceAvailable) =>
        oldVsync != newVsync && CanConfigure(width, height, surfaceAvailable);

    // A recreated surface may recover a different present mode without a
    // settings change. Check both the saved VSync request and the live mode.
    internal static bool PresentModeNeedsReapply(
        bool appliedVsync, bool requestedVsync, int activeMode, int requestedMode) =>
        appliedVsync != requestedVsync || activeMode != requestedMode;
}
