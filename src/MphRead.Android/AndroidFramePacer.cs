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

    internal double Deadline(double now, int cap, bool displayPaced = false)
    {
        // Display-paced modes already wait in the presentation driver. This is
        // true for Display and for explicit caps that map to a native panel
        // refresh (for example 120 on a 120 Hz phone). Only impose the runaway
        // safety floor in that case, never a second software display clock.
        double interval = 1.0 / (displayPaced || cap <= 0
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

    internal static double BudgetRate(int cap, double activeRefreshRate) =>
        cap <= 0 ? activeRefreshRate : Math.Min(cap, activeRefreshRate);

    internal static bool Behind(double frameMs, double activeWorkMs, double budgetMs) =>
        frameMs > budgetMs * 1.12 || activeWorkMs > budgetMs * 0.96;

    // Present can include GPU back-pressure as well as idle vsync wait. Treat
    // it conservatively for upscaling, but never as proof of overload alone.
    internal static bool HasHeadroom(double frameMs, double activeWorkMs, double presentMs, double budgetMs) =>
        frameMs <= budgetMs * 1.08 && activeWorkMs + presentMs < budgetMs * 0.72;
}
