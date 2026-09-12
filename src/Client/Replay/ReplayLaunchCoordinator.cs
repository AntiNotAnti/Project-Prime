using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Applies the platform-neutral part of a replay launch. Theatre may prepare
/// the file before handing the plan to a native host; consuming that prepared
/// reader and configuring an optional highlight reel must be identical on
/// desktop and Android.
/// </summary>
internal static class ReplayLaunchCoordinator
{
    internal static bool TryStart(in LaunchPlan plan, out string? error)
    {
        if (plan.Kind != LaunchKind.Replay)
        {
            error = "Only replay launch plans can start replay playback.";
            return false;
        }

        if (!ReplayPlayback.ConsumePrepared(plan.ReplayPath)
            && !ReplayPlayback.Join(plan.ReplayPath))
        {
            error = ReplayPlayback.LastError
                ?? "That file could not be read as a replay.";
            return false;
        }

        try
        {
            if (plan.ReplayHighlights is { Count: > 0 } highlights)
            {
                ReplayPlayback.ConfigureHighlights(highlights);
            }
            else if (plan.ReplayStartFrame is uint rangeStart
                && plan.ReplayEndFrame is uint rangeEnd)
            {
                ReplayPlayback.ConfigureRange(rangeStart, rangeEnd,
                    plan.ReplayFocus);
            }
            else if (plan.ReplayStartFrame is uint startFrame
                && !ReplayPlayback.Seek(startFrame))
            {
                throw new InvalidOperationException(
                    "That replay event has no checkpoint inside the bounded seek window.");
            }
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException)
        {
            // A replay launch is optional presentation. Do not leave a
            // successfully opened session active after its requested range
            // configuration was rejected.
            ReplayPlayback.Stop();
            error = exception.Message;
            return false;
        }
    }
}
