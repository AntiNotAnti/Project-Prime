using System;
using MphRead.Entities;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Network;

/// <summary>
/// Platform-neutral instant replay command. Native hosts translate keys and
/// forward the logical edge; the rolling timeline and file export remain in
/// shared Client code.
/// </summary>
internal static class ReplayQuickCapture
{
    internal const uint RecentSeconds = 10;
    internal const uint RecentFrames = RecentSeconds * 60;

    internal static bool IsHotkey(in WindowKeyEvent key)
        => IsHotkey(key.Key, key.Control, key.Command, key.Repeat)
            && key.Down;

    internal static bool IsHotkey(Keys key, bool control, bool command,
        bool repeat)
        => !repeat && (key == Keys.F10
            || key == Keys.R && (control || command));

    internal static bool HandleInput(WindowInputSnapshot input,
        ScenePresentation presentation)
    {
        if (ReplayPlayback.IsActive || ClientInputState.PauseOpen
            || Chat.ChatBox.Composing) return false;
        foreach (WindowKeyEvent key in input.KeyEvents ?? Array.Empty<WindowKeyEvent>())
        {
            if (IsHotkey(key)) return Execute(presentation);
        }
        return false;
    }

    internal static bool Execute(ScenePresentation presentation)
    {
        bool saved = ReplayRecorder.QueueSaveRecent(RecentFrames, out string? path);
        string message = saved ? $"SAVING LAST {RecentSeconds} SECONDS"
            : ReplayRecorder.LastQuickCaptureError ?? "REPLAY CLIP NOT SAVED";
        Console.WriteLine(saved
            ? $"[replay] {message}: {path}"
            : $"[replay] {message}");
        if (presentation.World.LocalPlayer is PlayerEntity player)
        {
            player.GetPresentation().QueueHudMessage(128, 50, 256, 2.5f,
                category: 0, message);
        }
        return true;
    }
}
