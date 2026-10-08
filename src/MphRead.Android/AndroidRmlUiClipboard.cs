#if MPHREAD_RMLUI_ANDROID
using System;
using Android.Content;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

internal static class AndroidRmlUiClipboard
{
    private static bool Shortcut(in RmlUiPlatformInputEvent input, int code) =>
        input.Kind == RmlUiPlatformInputKind.KeyDown && input.Code == code
        && (input.Modifiers & (RmlUiInputModifiers.Control | RmlUiInputModifiers.Command)) != 0;

    // Read on the Android callback thread, then carry only immutable text to
    // the native owner. Never access the system clipboard from the GPU worker.
    internal static string? ReadForPaste(Context? context, in RmlUiPlatformInputEvent input)
    {
        if (!Shortcut(input, 53)) return null; // V
        if (context?.GetSystemService(Context.ClipboardService) is not ClipboardManager clipboard) return "";
        var clip = clipboard.PrimaryClip;
        if (clip == null || clip.ItemCount == 0) return "";
        string text = clip.GetItemAt(0)?.CoerceToText(context)?.ToString() ?? "";
        return text.Length <= 1024 * 1024 ? text : "";
    }
    internal static string? ReadAfterCopy(RmlUiHost host, in RmlUiPlatformInputEvent input)
    {
        if (!Shortcut(input, 34) && !Shortcut(input, 55)) return null; // C / X
        if (!host.TryGetTextInputState(out var state) || state.Document != input.Document
            || (state.Capabilities & RmlUiTextInputCapabilities.Protected) != 0) return null;
        return host.ReadClipboard();
    }
    internal static void Write(Context? context, string text)
    {
        if (context?.GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
            clipboard.PrimaryClip = ClipData.NewPlainText("Project Prime", text);
    }
}
#endif
