using System;
using System.Text;
using MphRead.Text;
using MphRead.Mods.Network;

namespace MphRead.Mods.Render;

internal readonly record struct PlayerNameLayoutResult(string Text, float Scale, bool Truncated);

internal static class PlayerNameLayout
{
    public static float Measure(string text)
    {
        float width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!PlayerNameCodec.TryMapUnicodeToGlyph(rune, out ushort code)) continue;
            int index = code - Font.Normal.MinCharacter;
            width += Font.Normal.Widths != null && index >= 0 && index < Font.Normal.Widths.Count ? Font.Normal.Widths[index] : 8;
        }
        return width;
    }

    public static PlayerNameLayoutResult Fit(string name, float maxWidth, float preferredScale, float minimumScale)
    {
        string text = PlayerNameCodec.Clamp(name);
        float width = Measure(text);
        float scale = width > 0 ? Math.Clamp(maxWidth / width, minimumScale, preferredScale) : preferredScale;
        bool truncated = width * scale > maxWidth;
        if (truncated)
        {
            text = text[..Math.Min(text.Length, PlayerNameCodec.MaxGlyphs - 3)];
            while (text.Length > 0 && Measure(text + "...") * scale > maxWidth) text = text[..^1];
            string suffix = "...";
            while (suffix.Length > 0 && Measure(suffix) * scale > maxWidth) suffix = suffix[..^1];
            text += suffix;
        }
        return new(PlayerNameCodec.ToNative(text), scale, truncated);
    }

    /// <summary>
    /// Decode one native MPH glyph emitted by <see cref="PlayerNameCodec.ToNative"/>.
    /// The caller walks forward; <paramref name="index"/> is advanced once when
    /// a valid two-byte glyph consumes its continuation byte. Malformed native
    /// text falls back to '?' instead of reading past the current line.
    /// </summary>
    internal static bool TryReadNativeGlyph(ReadOnlySpan<char> text, ref int index, int end,
        out int glyph, out char original)
    {
        glyph = 0;
        original = '\0';
        end = Math.Clamp(end, 0, text.Length);
        if ((uint)index >= (uint)end) return false;

        original = text[index];
        glyph = original;
        if ((glyph & 0x80) == 0) return true;

        if (glyph < 0xC2 || glyph > 0xDF || index + 1 >= end)
        {
            glyph = '?';
            return true;
        }

        int continuation = text[index + 1];
        if ((continuation & 0xC0) != 0x80)
        {
            glyph = '?';
            return true;
        }

        glyph = (continuation & 0x3F) | ((glyph & 0x1F) << 6);
        index++;
        return true;
    }

}
