#if MPHREAD_RMLUI_POC || MPHREAD_RMLUI
using System;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Host;

internal static class RmlUiLinuxDesktopInput
{
    internal static unsafe RmlUiLinuxIme? Attach(RmlUiHost host, NativeWindow window, Func<bool> visible)
    {
        if (!OperatingSystem.IsLinux()) return null;
        bool x11 = X11Display() != 0;
        return RmlUiLinuxIme.TryAttach(host, visible, bounds =>
        {
            GLFW.GetWindowSize(window.WindowPtr, out int width, out int height);
            GLFW.GetFramebufferSize(window.WindowPtr, out int pixelsWidth, out int pixelsHeight);
            int originX = 0, originY = 0;
            if (x11) GLFW.GetWindowPos(window.WindowPtr, out originX, out originY);
            float scaleX = pixelsWidth > 0 ? (float)width / pixelsWidth : 1;
            float scaleY = pixelsHeight > 0 ? (float)height / pixelsHeight : 1;
            return new(originX + (int)MathF.Round(bounds.X * scaleX), originY + (int)MathF.Round(bounds.Y * scaleY),
                Math.Max(1, (int)MathF.Round(bounds.Width * scaleX)), Math.Max(1, (int)MathF.Round(bounds.Height * scaleY)), !x11);
        });
    }

    internal static bool Process(RmlUiLinuxIme? ime, KeyboardKeyEventArgs e, bool released)
    {
        if (ime == null) return false;
        uint modifiers = 0;
        if (e.Shift) modifiers |= 1;
        if (e.Control) modifiers |= 4;
        if (e.Alt) modifiers |= 8;
        if (e.Command) modifiers |= 64;
        nint display = X11Display();
        uint symbol = 0;
        if (display != 0 && e.ScanCode is > 0 and < 256)
        {
            try
            {
                if (XkbGetState(display, 0x0100, out var state) == 0)
                    modifiers |= state.LockedMods | ((uint)state.Group << 13);
                if (XkbLookupKeySym(display, (byte)e.ScanCode, modifiers, out _, out nuint value) != 0)
                    symbol = checked((uint)value);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        if (symbol == 0) symbol = KeySymbol(e.Key, e.ScanCode, e.Shift);
        return ime.ProcessKey(symbol, (uint)Math.Max(0, e.ScanCode - (display != 0 ? 8 : 0)), modifiers, released);
    }
    private static nint X11Display()
    {
        try { return GLFW.GetX11Display(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException) { return 0; }
    }
    private static uint KeySymbol(Keys key, int scanCode, bool shift)
    {
        uint special = key switch
        {
            Keys.Backspace => 0xff08, Keys.Tab => shift ? 0xfe20u : 0xff09u,
            Keys.Enter => 0xff0d, Keys.Escape => 0xff1b, Keys.Delete => 0xffff,
            Keys.Home => 0xff50, Keys.Left => 0xff51, Keys.Up => 0xff52, Keys.Right => 0xff53,
            Keys.Down => 0xff54, Keys.PageUp => 0xff55, Keys.PageDown => 0xff56, Keys.End => 0xff57,
            Keys.Insert => 0xff63, Keys.KeyPadEnter => 0xff8d, Keys.Space => 0x20,
            Keys.LeftShift => 0xffe1, Keys.RightShift => 0xffe2, Keys.LeftControl => 0xffe3,
            Keys.RightControl => 0xffe4, Keys.LeftAlt => 0xffe9, Keys.RightAlt => 0xffea,
            Keys.LeftSuper => 0xffeb, Keys.RightSuper => 0xffec,
            _ => 0
        };
        if (special != 0) return special;
        string name = GLFW.GetKeyName(key, scanCode) ?? "";
        if (Rune.TryGetRuneAt(name, 0, out Rune rune))
        {
            if (shift) rune = Rune.ToUpperInvariant(rune);
            return rune.Value <= 255 ? (uint)rune.Value : 0x01000000u | (uint)rune.Value;
        }
        return 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct XkbState
    {
        public byte Group, LockedGroup;
        public ushort BaseGroup, LatchedGroup;
        public byte Mods, BaseMods, LatchedMods, LockedMods, CompatState, GrabMods,
            CompatGrabMods, LookupMods, CompatLookupMods;
        public ushort PointerButtons;
    }
    [DllImport("libX11.so.6")] private static extern int XkbGetState(nint display, uint device, out XkbState state);
    [DllImport("libX11.so.6")] private static extern int XkbLookupKeySym(nint display, byte code, uint state, out uint consumed, out nuint symbol);
}
#endif
