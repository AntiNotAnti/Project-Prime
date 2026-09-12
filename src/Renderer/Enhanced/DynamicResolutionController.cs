using System;
using MphRead.Mods;

namespace MphRead;

public enum DynamicResolutionStatus : byte
{
    Disabled,
    GpuTimingUnavailable,
    Holding,
    Reduced,
    Increased
}

public readonly record struct DynamicResolutionDecision(
    int ScalePercent, DynamicResolutionStatus Status,
    double? SmoothedGpuMilliseconds);

/// <summary>
/// Deterministic Enhanced-only resolution policy driven exclusively by real
/// GPU duration samples. Missing timestamp support holds the configured scale;
/// CPU frame time is never substituted because it would couple simulation,
/// I/O, and presentation stalls into renderer quality.
/// </summary>
public sealed class DynamicResolutionController
{
    public const int MinimumScalePercent = 50;
    public const int ScaleStepPercent = 5;
    public const int ReductionWindow = 8;
    public const int IncreaseWindow = 90;
    public const int ChangeCooldownFrames = 30;
    public const double DefaultBudgetMilliseconds = 1000.0 / 60.0;

    private readonly double _budgetMilliseconds;
    private double? _smoothedGpuMilliseconds;
    private int _scalePercent;
    private int _overBudgetFrames;
    private int _underBudgetFrames;
    private int _cooldown;

    public DynamicResolutionController(int initialScalePercent = 100,
        double budgetMilliseconds = DefaultBudgetMilliseconds)
    {
        if (!double.IsFinite(budgetMilliseconds) || budgetMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
        _budgetMilliseconds = budgetMilliseconds;
        _scalePercent = Math.Clamp(initialScalePercent,
            RenderOptions.MinScale, 100);
    }

    public int ScalePercent => _scalePercent;

    public DynamicResolutionDecision Update(GraphicsPreset preset,
        int configuredMaximumScalePercent, double? gpuFrameMilliseconds)
    {
        int maximum = Math.Clamp(configuredMaximumScalePercent,
            RenderOptions.MinScale, 100);
        int minimum = Math.Min(MinimumScalePercent, maximum);
        if (_scalePercent > maximum) _scalePercent = maximum;
        if (preset != GraphicsPreset.Enhanced)
        {
            ResetHistory(maximum);
            return new DynamicResolutionDecision(maximum,
                DynamicResolutionStatus.Disabled, null);
        }
        if (gpuFrameMilliseconds is not double sample
            || !double.IsFinite(sample) || sample <= 0)
        {
            _overBudgetFrames = 0;
            _underBudgetFrames = 0;
            return new DynamicResolutionDecision(_scalePercent,
                DynamicResolutionStatus.GpuTimingUnavailable,
                _smoothedGpuMilliseconds);
        }

        _smoothedGpuMilliseconds = _smoothedGpuMilliseconds is double prior
            ? prior + (sample - prior) * 0.125 : sample;
        if (_cooldown > 0) _cooldown--;
        double smoothed = _smoothedGpuMilliseconds.Value;
        if (smoothed > _budgetMilliseconds * 1.08)
        {
            _overBudgetFrames++;
            _underBudgetFrames = 0;
        }
        else if (smoothed < _budgetMilliseconds * 0.82)
        {
            _underBudgetFrames++;
            _overBudgetFrames = 0;
        }
        else
        {
            _overBudgetFrames = 0;
            _underBudgetFrames = 0;
        }

        if (_cooldown == 0 && _overBudgetFrames >= ReductionWindow
            && _scalePercent > minimum)
        {
            _scalePercent = Math.Max(minimum,
                _scalePercent - ScaleStepPercent);
            BeginCooldown();
            return Decision(DynamicResolutionStatus.Reduced);
        }
        if (_cooldown == 0 && _underBudgetFrames >= IncreaseWindow
            && _scalePercent < maximum)
        {
            _scalePercent = Math.Min(maximum,
                _scalePercent + ScaleStepPercent);
            BeginCooldown();
            return Decision(DynamicResolutionStatus.Increased);
        }
        return Decision(DynamicResolutionStatus.Holding);
    }

    private DynamicResolutionDecision Decision(DynamicResolutionStatus status)
        => new(_scalePercent, status, _smoothedGpuMilliseconds);

    private void BeginCooldown()
    {
        _cooldown = ChangeCooldownFrames;
        _overBudgetFrames = 0;
        _underBudgetFrames = 0;
    }

    private void ResetHistory(int scale)
    {
        _scalePercent = scale;
        _smoothedGpuMilliseconds = null;
        _overBudgetFrames = 0;
        _underBudgetFrames = 0;
        _cooldown = 0;
    }
}
