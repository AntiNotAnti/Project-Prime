using System;
using System.Diagnostics;
using Android.Content;
using Android.OS;
using MphRead.Mods;
using MphRead.Mods.Render;

namespace MphRead.Droid;

/// <summary>
/// Android Dynamic Performance Framework bridge for the long-lived game render
/// thread. This is advisory scheduling feedback only: it never changes the
/// player's FPS cap, render scale, graphics settings or thermal policy.
/// </summary>
internal sealed class AndroidPerformanceHints : IDisposable
{
    private PerformanceHintManager.Session? _session;
    private long _targetNanos;
    private bool _disposed;
    private bool _failed;

    private AndroidPerformanceHints(PerformanceHintManager.Session session, long targetNanos)
    {
        _session = session;
        _targetNanos = targetNanos;
    }

    /// <summary>
    /// Must be called on the render thread so Process.MyTid() identifies the
    /// periodic workload that actually performs simulation and rendering.
    /// Unsupported devices simply return null.
    /// </summary>
    internal static AndroidPerformanceHints? TryCreate()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return null;
        }

        try
        {
            MainActivity? activity = MainActivity.Instance;
            if (activity?.GetSystemService(Context.PerformanceHintService)
                    is not PerformanceHintManager manager)
            {
                DebugLog.Line("androidperf", "ADPF performance hint manager unavailable");
                return null;
            }

            long target = TargetDurationNanos(FrameTiming.FrameRateCap);
            int tid = Android.OS.Process.MyTid();
            PerformanceHintManager.Session? session =
                manager.CreateHintSession(new[] { tid }, target);
            if (session == null)
            {
                DebugLog.Line("androidperf", "ADPF performance hint session unsupported");
                return null;
            }

            DebugLog.Line("androidperf",
                $"ADPF hint session active: tid={tid} target={target / 1_000_000.0:0.00} ms "
                + $"preferred-update={manager.PreferredUpdateRateNanos / 1_000_000.0:0.00} ms");
            return new AndroidPerformanceHints(session, target);
        }
        catch (Exception ex)
        {
            DebugLog.Line("androidperf",
                $"ADPF performance hint setup unavailable: {ex.GetBaseException().Message}");
            return null;
        }
    }

    internal void ReportFrame(long workStartTimestamp, long workEndTimestamp, int cap)
    {
        if (_disposed || _failed || _session == null)
        {
            return;
        }

        try
        {
            long target = TargetDurationNanos(cap);
            if (target != _targetNanos)
            {
                _session.UpdateTargetWorkDuration(target);
                _targetNanos = target;
            }

            long ticks = Math.Max(1, workEndTimestamp - workStartTimestamp);
            long actualNanos = Math.Max(1,
                (long)Math.Round(ticks * (1_000_000_000.0 / Stopwatch.Frequency)));
            _session.ReportActualWorkDuration(actualNanos);
        }
        catch (Exception ex)
        {
            // Vendor services are allowed to reject or tear down sessions.
            // Performance hints are an optimization, never a match-lifecycle
            // dependency, so permanently disable this session after a failure.
            _failed = true;
            DebugLog.Line("androidperf",
                $"ADPF performance hint session disabled: {ex.GetBaseException().Message}");
            Dispose();
        }
    }

    internal static long TargetDurationNanos(int cap)
    {
        double activeDisplay = AndroidPerformance.ActiveDisplayRefreshRate > 0
            ? AndroidPerformance.ActiveDisplayRefreshRate
            : AndroidPerformance.DisplayRefreshRate;
        double rate;
        if (cap == FrameTiming.DisplayRate || cap == FrameTiming.Unlimited)
        {
            // Unlimited removes the app-side presentation ceiling. ADPF still
            // needs a useful workload target, so use the active panel cadence
            // rather than interpreting the -1 sentinel as a low frame rate.
            rate = activeDisplay;
        }
        else
        {
            rate = Math.Clamp(cap, FrameTiming.MinCap, FrameTiming.MaxCap);
            if (activeDisplay > 0)
            {
                rate = Math.Min(rate, activeDisplay);
            }
        }

        rate = Math.Clamp(rate, 30.0, FrameTiming.MaxCap);
        return Math.Max(1L, (long)Math.Round(1_000_000_000.0 / rate));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _session?.Close();
        }
        catch
        {
            // Closing an advisory session must not affect game shutdown.
        }
        finally
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
