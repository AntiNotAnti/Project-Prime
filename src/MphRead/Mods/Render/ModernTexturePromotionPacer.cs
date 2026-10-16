using System;

namespace MphRead.Mods.Render;

/// <summary>
/// Spreads non-preemptible Android texture promotions across presentation frames.
/// CPU preparation and the existing single-upload/frame rule are unchanged.
/// No Android dependencies: policy is exercised by the pacing check on CI.
/// </summary>
internal sealed class ModernTexturePromotionPacer
{
    internal const double SoftBudgetMs = 2;
    private const int MaximumRecoveryFrames = 6;
    private int _framesToSkip;
    internal int FramesToSkip => _framesToSkip;

    internal bool BeginFrame()
    {
        if (_framesToSkip == 0) return true;
        _framesToSkip--;
        return false;
    }

    internal void RecordPromotion(double elapsedMs)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0)
        {
            _framesToSkip = 0;
            return;
        }
        _framesToSkip = Math.Clamp((int)Math.Ceiling(
            Math.Min(elapsedMs, (MaximumRecoveryFrames + 1) * SoftBudgetMs)
            / SoftBudgetMs) - 1, 0, MaximumRecoveryFrames);
    }
}
