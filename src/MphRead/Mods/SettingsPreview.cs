using System;
using System.Diagnostics;

namespace MphRead.Mods;

/// <summary>Owner-thread, temporary preferences. Never writes a settings file.</summary>
public static class SettingsPreview
{
    private static Action? _restore, _resume;
    private static long _started;
    public const int DurationSeconds = 60;
    public static bool Active => _restore != null;
    public static double Remaining => Active
        ? Math.Max(0, DurationSeconds - Stopwatch.GetElapsedTime(_started).TotalSeconds) : 0;

    internal static void Begin(Func<Action> apply, Action resume)
    {
        if (Active) throw new InvalidOperationException("A settings preview is already active.");
        _restore = apply();
        _resume = resume;
        _started = Stopwatch.GetTimestamp();
    }

    public static void Tick() { if (Active && Remaining <= 0) End(); }
    public static void End()
    {
        var restore = _restore; var resume = _resume;
        _restore = _resume = null;
        if (restore == null) return;
        try { restore(); }
        finally { resume?.Invoke(); }
    }
}
