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
        // Display-paced modes already wait in the presentation driver. This is
        // true for Display and for explicit caps that map to a native panel
        // refresh (for example 120 on a 120 Hz phone). Only impose the runaway
        // safety floor in that case, never a second software display clock.
        double interval = 1.0 / (presentationPaced || cap <= 0
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
    /// True when an explicit numeric cap maps to a refresh mode the panel can
    /// natively present. In that case SurfaceFlinger/presentation owns cadence;
    /// adding a managed timer would double-pace the frame.
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
    /// A blocking modern present mode (FIFO fallback) is a presentation clock
    /// even when a non-native numeric cap originally asked for software pacing.
    /// Never sleep to one cadence and then block on another.
    /// </summary>
    internal static bool PresentationOwnsCadence(bool displayPaced, bool modernPresentationBlocks) =>
        displayPaced || modernPresentationBlocks;

}
