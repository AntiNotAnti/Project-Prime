#if MPHREAD_RMLUI_POC || MPHREAD_RMLUI
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    internal static class RmlUiDesktopInput
    {
        internal static unsafe void KeyDown(RmlUiHost host, KeyboardKeyEventArgs e)
        {
            int key = TranslateKey(e.Key);
            if (key == 0) return;
            bool clipboardShortcut = (e.Control || e.Command) && host.TextInputActive;
            if (clipboardShortcut && e.Key == Keys.V)
            {
                try { host.SetClipboard(GLFW.GetClipboardString(null) ?? ""); }
                catch { /* Some headless/windowing providers have no OS clipboard. */ }
            }
            host.Input.Key(key, true, Modifiers(e));
            if (clipboardShortcut && e.Key is Keys.C or Keys.X)
            {
                try { GLFW.SetClipboardString(null, host.ReadClipboard()); }
                catch { /* The native editing buffer remains available. */ }
            }
        }

        internal static int TranslateKey(Keys key) => key switch
        {
            Keys.Tab => 1,
            Keys.Enter or Keys.KeyPadEnter => 2,
            Keys.Escape => 3,
            Keys.Space => 4,
            Keys.Up => 5,
            Keys.Down => 6,
            Keys.Left => 7,
            Keys.Right => 8,
            Keys.Home => 9,
            Keys.End => 10,
            Keys.PageUp => 11,
            Keys.PageDown => 12,
            Keys.Backspace => 13,
            Keys.Delete => 14,
            >= Keys.A and <= Keys.Z => 32 + (int)key - (int)Keys.A,
            _ => 0
        };

        internal static RmlUiInputModifiers Modifiers(KeyboardKeyEventArgs e)
        {
            RmlUiInputModifiers modifiers = default;
            if (e.Shift) modifiers |= RmlUiInputModifiers.Shift;
            if (e.Control) modifiers |= RmlUiInputModifiers.Control;
            if (e.Alt) modifiers |= RmlUiInputModifiers.Alt;
            if (e.Command) modifiers |= RmlUiInputModifiers.Command;
            return modifiers;
        }
    }
}
#endif
