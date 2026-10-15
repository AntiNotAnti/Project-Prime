using System;
using System.Globalization;

namespace MphRead.Mods.Launcher.Gui;

internal readonly record struct HudRgb(byte R, byte G, byte B);
internal readonly record struct HudHsv(float Hue, float Saturation, float Value);

/// <summary>Pure, bounded UI color arithmetic shared by HSV/RGB/hex inputs.</summary>
internal static class HudColorMath
{
    internal static bool TryParseHex(string? text, out string hex, out HudRgb rgb)
    {
        hex = "";
        rgb = default;
        string value = (text ?? "").Trim().TrimStart('#');
        if (value.Length == 3)
            value = $"{value[0]}{value[0]}{value[1]}{value[1]}{value[2]}{value[2]}";
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out uint packed))
            return false;
        rgb = new((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        hex = ToHex(rgb);
        return true;
    }

    internal static string ToHex(HudRgb rgb) => $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    internal static HudHsv ToHsv(HudRgb rgb)
    {
        float r = rgb.R / 255f, g = rgb.G / 255f, b = rgb.B / 255f;
        float hi = MathF.Max(r, MathF.Max(g, b)), lo = MathF.Min(r, MathF.Min(g, b));
        float d = hi - lo;
        float hue = 0f;
        if (d > 0.000001f)
        {
            if (hi == r) hue = 60f * (((g - b) / d) % 6);
            else if (hi == g) hue = 60f * ((b - r) / d + 2);
            else hue = 60f * ((r - g) / d + 4);
            if (hue < 0) hue += 360f;
        }
        return new(hue, hi == 0 ? 0f : d / hi, hi);
    }

    internal static HudRgb FromHsv(float hue, float saturation, float value)
    {
        hue = float.IsFinite(hue) ? ((hue % 360) + 360) % 360 : 0f;
        saturation = float.IsFinite(saturation) ? Math.Clamp(saturation, 0f, 1f) : 0f;
        value = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
        float chroma = value * saturation;
        float sector = hue / 60;
        float second = chroma * (1 - MathF.Abs(sector % 2 - 1));
        float r = 0, g = 0, b = 0;
        if (sector < 1) { r = chroma; g = second; }
        else if (sector < 2) { r = second; g = chroma; }
        else if (sector < 3) { g = chroma; b = second; }
        else if (sector < 4) { g = second; b = chroma; }
        else if (sector < 5) { r = second; b = chroma; }
        else { r = chroma; b = second; }
        float m = value - chroma;
        static byte Channel(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
        return new(Channel(r + m), Channel(g + m), Channel(b + m));
    }
}
