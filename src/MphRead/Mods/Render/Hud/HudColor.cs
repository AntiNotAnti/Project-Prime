using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;
public static class HudColor
{
    public static string Normalize(string? value) => value != null && value.Length == 7 && value[0] == '#'
        && uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _) ? value : "#FFFFFF";
    public static Vector3 Parse(string? value)
    {
        uint rgb = Convert.ToUInt32(Normalize(value)[1..], 16);
        return new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
