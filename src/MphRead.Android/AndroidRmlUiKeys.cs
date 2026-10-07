#if MPHREAD_RMLUI_ANDROID
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Droid;

internal static class AndroidRmlUiKeys
{
    internal static Keys Physical(int code) => code switch
    {
        1 => Keys.Tab, 2 => Keys.Enter, 3 => Keys.Escape, 4 => Keys.Space,
        5 => Keys.Up, 6 => Keys.Down, 7 => Keys.Left, 8 => Keys.Right,
        9 => Keys.Home, 10 => Keys.End, 11 => Keys.PageUp, 12 => Keys.PageDown,
        13 => Keys.Backspace, 14 => Keys.Delete,
        >= 32 and <= 57 => Keys.A + (code - 32), _ => Keys.Unknown
    };
}
#endif
