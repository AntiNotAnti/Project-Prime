using System;

namespace MphRead.Mods.Launcher.RmlUi.Host;

/// <summary>Every independent teardown runs even when a disposer or diagnostic sink fails.</summary>
internal static class RmlUiCleanup
{
    public static void Run(Action<Exception>? report, params Action[] actions)
    {
        foreach (var action in actions) Try(action, report);
    }
    public static bool Try(Action action, Action<Exception>? report = null)
    {
        try { action(); return true; }
        catch (Exception error)
        {
            try { report?.Invoke(error); } catch { }
            return false;
        }
    }
}
