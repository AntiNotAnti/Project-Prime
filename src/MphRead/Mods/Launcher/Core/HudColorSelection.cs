#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.Core;

// Editor-only session history; choosing a color still uses the owning settings transaction.
public static class HudColorSelection
{
    public static readonly string[] Palette = { "#FFFFFF", "#D9EDF8", "#A2D3F0", "#72C7FF",
        "#55E18C", "#00BFA5", "#FFD166", "#FF9F43", "#FF6B6B", "#EF476F",
        "#B388FF", "#7E57C2", "#536DFE", "#607D8B", "#263238", "#000000" };
    private static readonly List<string> Colors = new();
    public static IReadOnlyList<string> Recent => Colors;
    public static void Remember(string hex)
    {
        string normalized = HudColor.Normalize(hex);
        if (!String.Equals(hex, normalized, StringComparison.OrdinalIgnoreCase)) return;
        Colors.Remove(normalized); Colors.Insert(0, normalized);
        if (Colors.Count > 8) Colors.RemoveAt(8);
    }
}
#endif
