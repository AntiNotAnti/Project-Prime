using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenTK.Mathematics;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;

namespace MphRead.Mods.Campaign;

/// <summary>Bounded native Latin1 glyph layout. Coordinates remain in the original Y-up menu space.
/// Behavior reference: Second Hunt41452f2 MenuText/MenuEngine; rendering and UI ownership stay elsewhere.</summary>
public static class CampaignCreditsTextLayout
{
    public const int MaxTextBytes = 4096, MaxLines = 512, MaxMeasurementBytes = 8 * 1024 * 1024;
    public readonly record struct Glyph(int GlyphId, float X, float Y, float Depth, float U0, float V0, float U1, float V1);
    public sealed class Snapshot
    {
        private readonly CampaignCreditsSource.TextStyle _style;
        public string FontModelSha256 { get; }
        public string FontMetricsSha256 { get; }
        public string TextSha256 { get; }
        public int LineCount => LineWidths.Count;
        public IReadOnlyList<int> LineWidths { get; }
        public IReadOnlyList<Glyph> Glyphs { get; }
        internal Snapshot(CampaignCreditsFontSource.Snapshot font, string text, CampaignCreditsSource.TextStyle style,
            int[] widths, Glyph[] glyphs)
        {
            FontModelSha256 = font.ModelSha256; FontMetricsSha256 = font.MetricsSha256;
            TextSha256 = Hash(Encoding.Latin1.GetBytes(text)); _style = style;
            LineWidths = Array.AsReadOnly(widths); Glyphs = Array.AsReadOnly(glyphs);
        }
        public Vector4 ColorAt(byte stateCode, long frame)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            double t = 0;
            int duration = _style.DurationFx;
            if (duration > 0)
            {
                if (stateCode >= 8)
                    t = frame >= (duration + 4095L) / 4096 ? 1 : frame * 4096d / duration;
                else
                {
                    // Reduce before multiplication: even long.MaxValue cannot overflow,
                    // and fractional fixed-point pulse periods retain their source phase.
                    long period = duration * 2L, phase = (frame % period) * 4096 % period;
                    t = (phase <= duration ? phase : period - phase) / (double)duration;
                }
            }
            float Channel(int shift)
            {
                int a = (int)(_style.StartColor >> shift) & 31, b = (int)(_style.EndColor >> shift) & 31;
                return (float)((a + (b - a) * t) / 31d);
            }
            return new(Channel(0), Channel(8), Channel(16), Channel(24));
        }
    }
    private readonly record struct Line(int Start, int Length, int Width);

    public static Snapshot Build(CampaignCreditsFontSource.Snapshot font, string text,
        CampaignCreditsSource.TextStyle style, int xFx, int yFx, int depthFx)
    {
        ArgumentNullException.ThrowIfNull(font); ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        Require(text.Length <= MaxTextBytes && style.Alignment <= 2 && style.DurationFx >= 0,
            "Credits text length or layout style is unsupported.");
        foreach (char ch in text) Require(ch is > '\0' and <= '\xff', "Credits text requires NUL-free native Latin1 content.");
        var lines = new List<Line>(); int work = 0;
        // Store source slices instead of repeatedly allocating candidate strings.
        // A wrapped builder is always a contiguous slice after leading empty words.
        int rawStart = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', rawStart), rawEnd = newline < 0 ? text.Length : newline;
            int rawWidth = Measure(rawStart, rawEnd - rawStart);
            if (style.WrapWidth == 0 || rawWidth <= style.WrapWidth) Add(rawStart, rawEnd - rawStart, rawWidth);
            else
            {
                int wordStart = rawStart, builderStart = rawStart, builderLength = 0, builderWidth = 0;
                while (true)
                {
                    int space = text.IndexOf(' ', wordStart, rawEnd - wordStart), wordEnd = space < 0 ? rawEnd : space;
                    if (builderLength == 0)
                    {
                        builderStart = wordStart; builderLength = wordEnd - wordStart; builderWidth = Measure(builderStart, builderLength);
                    }
                    else
                    {
                        int candidateLength = wordEnd - builderStart, candidateWidth = Measure(builderStart, candidateLength);
                        if (candidateWidth > style.WrapWidth)
                        {
                            Add(builderStart, builderLength, builderWidth);
                            builderStart = wordStart; builderLength = wordEnd - wordStart; builderWidth = Measure(builderStart, builderLength);
                        }
                        else { builderLength = candidateLength; builderWidth = candidateWidth; }
                    }
                    if (space < 0) break;
                    wordStart = space + 1;
                }
                Add(builderStart, builderLength, builderWidth);
            }
            if (newline < 0) break;
            rawStart = newline + 1;
        }
        var widths = new int[lines.Count]; var glyphs = new List<Glyph>();
        float x = xFx / 4096f, top = yFx / 4096f - 192 + (style.Size == 0 ? 8 : style.Size);
        float depth = depthFx / 4096f;
        float z = depth < 0 ? 28 + depth / 5 : 1000 + depth;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            Line line = lines[lineIndex]; widths[lineIndex] = line.Width;
            float pen = x - (style.Alignment == 1 ? line.Width : style.Alignment == 2 ? line.Width / 2 : 0);
            int cursor = line.Start, end = line.Start + line.Length;
            while (cursor < end)
            {
                bool space = text[cursor] == ' ';
                int glyph = NextGlyph(text, ref cursor, end);
                if ((uint)glyph >= font.GlyphCount) { pen += 8; continue; }
                if (!space)
                {
                    float u0 = ((glyph % 16) * 8 + .02f) / 128, v0 = ((glyph / 16) * 8 + .02f) / 256;
                    glyphs.Add(new(glyph, pen, top - font.VerticalOffset(glyph), z, u0, v0, u0 + 7.96f / 128, v0 + 7.96f / 256));
                }
                pen += font.Width(glyph);
            }
            top -= style.Size == 0 ? 8 : style.Size;
        }
        return new(font, text, style, widths, glyphs.ToArray());

        void Add(int start, int length, int width)
        {
            Require(lines.Count < MaxLines, "Credits text exceeds its line bound."); lines.Add(new(start, length, width));
        }
        int Measure(int start, int length)
        {
            int cursor = start, end = start + length, width = 0;
            while (cursor < end)
            {
                int before = cursor, glyph = NextGlyph(text, ref cursor, end); work += cursor - before;
                Require(work <= MaxMeasurementBytes, "Credits text exceeds its measurement work bound.");
                width += (uint)glyph < font.GlyphCount ? font.Width(glyph) : 8;
            }
            return width;
        }
    }
    private static int NextGlyph(string text, ref int cursor, int end)
    {
        int code = text[cursor++];
        if ((code & 0x80) != 0 && cursor < end) code = (text[cursor++] & 0x3f) | ((code & 0x1f) << 6);
        return code - 32;
    }
}
