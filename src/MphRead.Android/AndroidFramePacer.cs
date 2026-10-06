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

    internal double Deadline(double now, int cap, bool presentationPaced = false)
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

        // A numeric cap remains a submission budget even when FIFO blocks.
        // Presentation time has already advanced `now`, so an absolute
        // deadline only waits for the unused part of that budget. Replacing a
        // numeric cap with the safety floor silently ignored it on FIFO-only
        // devices, or when Android declined an advisory refresh request.
        // Display and Unlimited leave cadence to blocking presentation.
        double interval = 1.0 / (cap <= 0
            ? maximumRate : Math.Clamp(cap, 1, maximumRate));
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

    /// <summary>
    /// True when a numeric cap matches an advertised panel mode. This informs
    /// the advisory surface-rate request; the pacer still enforces the numeric
    /// budget when the compositor chooses a different active rate.
    /// </summary>
    internal static bool MatchesNativeRefresh(int requestedCap, double maximumRefreshRate,
        ReadOnlySpan<float> supportedRefreshRates)
    {
        if (requestedCap >= maximumRefreshRate - 0.5)
        {
            return true;
        }
        foreach (float rate in supportedRefreshRates)
        {
            if (Math.Abs(rate - requestedCap) <= 0.5)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A blocking modern present mode (FIFO fallback) is a presentation clock.
    /// Numeric caps still constrain submission with an absolute deadline;
    /// presentation time consumes that same budget rather than adding a second
    /// elapsed-duration timer.
    /// </summary>
    internal static bool PresentationOwnsCadence(bool displayPaced, bool modernPresentationBlocks) =>
        displayPaced || modernPresentationBlocks;

}
