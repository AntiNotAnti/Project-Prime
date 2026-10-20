using System;

namespace MphRead.Mods.Campaign;

// Presentation scheduling only. Campaign records retain their existing 60 Hz
// owner. Pauses reset the baseline; hitches admit at most 250 ms/eight steps.
internal sealed class CampaignCreditsClock
{
    private double? _last;
    private double _fraction;
    private bool _active;
    internal int Advance(double monotonicSeconds, bool active)
    {
        if (!double.IsFinite(monotonicSeconds) || monotonicSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        if (!active || !_active || _last == null || monotonicSeconds < _last.Value)
        {
            _last = monotonicSeconds; _fraction = 0; _active = active;
            return 0;
        }
        double elapsed = Math.Min(monotonicSeconds - _last.Value, .25);
        _last = monotonicSeconds;
        _fraction += elapsed * 30;
        int steps = Math.Min(8, (int)Math.Floor(_fraction + 1e-9));
        _fraction = Math.Max(0, _fraction - steps);
        return steps;
    }
    internal void Reset() { _last = null; _fraction = 0; _active = false; }
}
