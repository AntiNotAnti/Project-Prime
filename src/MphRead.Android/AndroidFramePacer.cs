using System;

namespace MphRead.Droid;

// Pure presentation policy: no Android objects and no simulation state.
internal sealed class AndroidFramePacer(int maximumRate)
{
    private double _next;
    private double _last;
    private double _interval;

    internal void Reset(double now)
    {
        _next = _last = now;
        _interval = 0;
    }

    internal double Deadline(double now, int cap, bool presentationPaced = false,
        double displayRefreshRate = double.NaN)
    {
        // A negative cap is the shared true-Unlimited sentinel. When
        // presentation is nonblocking, there is deliberately no managed
        // deadline at all. A blocking compositor/present mode already owns
        // cadence and keeps the existing safety-floor bookkeeping.
        if (cap < 0 && !presentationPaced)
        {
            _interval = 0;
            _next = now;
            return now;
        }

        // Blocking presentation owns cadence. If interval application failed,
        // Display uses the observed rate (60 Hz conservative fallback when no
        // observation exists) instead of running at the runaway safety ceiling.
        double target = presentationPaced ? maximumRate : cap == 0
            ? double.IsFinite(displayRefreshRate) && displayRefreshRate > 0
                ? Math.Clamp(displayRefreshRate, 1, maximumRate) : 60
            : Math.Clamp(cap, 1, maximumRate);
        double interval = 1.0 / target;
        if (interval != _interval)
        {
            _interval = interval;
            _next = now;
        }
        return _next;
    }

    internal double BeginFrame(double now)
    {
        _next += _interval;
        if (_next < now) _next = now + _interval;
        double elapsed = Math.Max(0, now - _last);
        _last = now;
        return elapsed;
    }

    // A supported mode is only a request opportunity. Observe the active rate
    // and require a successfully applied blocking interval before dropping the cap.
    internal static bool PresentationOwnsCadence(int cap, double activeRefresh, int appliedInterval) =>
        appliedInterval == 1 && (cap == 0 || (cap > 0 && double.IsFinite(activeRefresh)
            && activeRefresh >= 1 && activeRefresh <= cap + 0.5));
}

// Explicit unset state avoids colliding with the shared Unlimited (-1) sentinel.
// A failed request remains retryable, with bounded traffic on the owner thread.
internal sealed class AndroidPresentationRequest
{
    private int? _cap;
    private float _rate;
    private long _nextAttempt;
    private int _failures;
    internal void Reset() { _cap = null; _nextAttempt = 0; _failures = 0; }
    internal bool ShouldAttempt(int cap, float rate, long now) =>
        (_cap != cap || Math.Abs(_rate - rate) > 0.5f) && now >= _nextAttempt;
    internal void Complete(int cap, float rate, long now, bool success)
    {
        if (success) { _cap = cap; _rate = rate; _failures = 0; _nextAttempt = 0; }
        else _nextAttempt = now + RetryDelayMs(++_failures);
    }
    internal static int RetryDelayMs(int failures) => 50 << Math.Clamp(failures - 1, 0, 5);
}

// Completion-to-completion intervals enclose the current frame's limiter, work
// and swap. A start-to-start interval belongs to the preceding work instead.
internal sealed class AndroidPresentedFrameClock(long frequency)
{
    private long? _previous;
    internal void Reset() => _previous = null;
    internal bool TryObserve(long completedTicks, out double elapsedSeconds)
    {
        long? previous = _previous;
        _previous = completedTicks;
        elapsedSeconds = previous.HasValue && completedTicks >= previous.Value
            ? (completedTicks - previous.Value) / (double)frequency : 0;
        return previous.HasValue && completedTicks >= previous.Value;
    }
}
