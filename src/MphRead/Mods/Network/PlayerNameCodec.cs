using System;
using System.Text;
using MphRead.Text;

namespace MphRead.Mods.Network;

/// <summary>Unicode in application state; native MPH glyph bytes at transport boundaries.</summary>
internal static class PlayerNameCodec
{
    public const int MaxGlyphs = 24;
    public const int MaxWireBytes = MaxGlyphs * 2;
    public static bool TryMapUnicodeToGlyph(Rune rune, out ushort glyphCode) => MphGlyphMap.TryMap(rune, out glyphCode);
    public static bool IsSupportedRune(Rune rune) => TryMapUnicodeToGlyph(rune, out _);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Rune enumeration replaces invalid surrogates before NFC, which otherwise throws.
        var valid = new StringBuilder();
        foreach (Rune rune in value.EnumerateRunes())
            if (rune != Rune.ReplacementChar) valid.Append(rune.ToString());
        string canonical = valid.ToString().Normalize(NormalizationForm.FormC);
        valid.Clear();
        foreach (Rune rune in canonical.EnumerateRunes())
            if (IsSupportedRune(rune)) valid.Append(rune.ToString());
        return valid.ToString().Trim();
    }

    public static int GlyphCount(string value)
    {
        int count = 0;
        foreach (Rune rune in value.EnumerateRunes()) if (IsSupportedRune(rune)) count++;
        return count;
    }

    public static string Clamp(string? value)
    {
        string normalized = Normalize(value);
        var result = new StringBuilder();
        int count = 0;
        foreach (Rune rune in normalized.EnumerateRunes())
        {
            if (count++ == MaxGlyphs) break;
            result.Append(rune.ToString());
        }
        return result.ToString().Trim();
    }

    public static string? ValidationError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Name cannot be empty";
        string canonical;
        try { canonical = value.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return "Invalid Unicode character"; }
        foreach (Rune rune in canonical.EnumerateRunes())
            if (!IsSupportedRune(rune))
            {
                var category = Rune.GetUnicodeCategory(rune);
                string display = category is System.Globalization.UnicodeCategory.Control
                    or System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator
                    or System.Globalization.UnicodeCategory.ParagraphSeparator
                    or System.Globalization.UnicodeCategory.NonSpacingMark
                    ? $"U+{rune.Value:X4}" : rune.ToString();
                return $"Unsupported character: {display}";
            }
        return GlyphCount(canonical) > MaxGlyphs ? $"Maximum {MaxGlyphs} glyphs" : null;
    }

    public static bool TryEncode(string? value, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        destination.Clear();
        if (ValidationError(value) != null) return false;
        string canonical = Normalize(value);
        int required = 0;
        foreach (Rune rune in canonical.EnumerateRunes())
        {
            TryMapUnicodeToGlyph(rune, out ushort code);
            required += code < 128 ? 1 : 2;
        }
        if (destination.Length < required) return false;
        foreach (Rune rune in canonical.EnumerateRunes())
        {
            TryMapUnicodeToGlyph(rune, out ushort code);
            if (code < 128) destination[bytesWritten++] = (byte)code;
            else
            {
                destination[bytesWritten++] = (byte)(0xC0 | (code >> 6));
                destination[bytesWritten++] = (byte)(0x80 | (code & 0x3F));
            }
        }
        return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> source, out string value, bool padded = true)
    {
        value = "";
        if (source.Length > MaxWireBytes) return false;
        var result = new StringBuilder();
        int count = 0;
        for (int i = 0; i < source.Length; i++)
        {
            int code = source[i];
            if (code == 0 && padded)
            {
                for (; i < source.Length; i++) if (source[i] != 0) return false;
                break;
            }
            if (code >= 128)
            {
                if (code < 0xC2 || code > 0xDF || ++i >= source.Length || (source[i] & 0xC0) != 0x80) return false;
                code = ((code & 0x1F) << 6) | (source[i] & 0x3F);
            }
            string? character = MphGlyphMap.Unicode(code);
            if (character == null || ++count > MaxGlyphs) return false;
            result.Append(character);
        }
        value = result.ToString();
        // Reject noncanonical aliases, whitespace-only names and malformed data rather than repair it.
        if (value.Length == 0 || Normalize(value) != value) { value = ""; return false; }
        return true;
    }

    public static string Decode(ReadOnlySpan<byte> source) => TryDecode(source, out string value) ? value : "";

    public static string ToNative(string value)
    {
        Span<byte> bytes = stackalloc byte[MaxWireBytes];
        if (!TryEncode(Clamp(value), bytes, out int count)) return "";
        var result = new StringBuilder(count);
        for (int i = 0; i < count; i++) result.Append((char)bytes[i]);
        return result.ToString();
    }
}
