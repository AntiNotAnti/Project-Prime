using System;
using System.IO;
using System.Security.Cryptography;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;

namespace MphRead.Mods.Campaign;

/// <summary>Detached two-dimensional native frontend bitmap, independent of the model texture decoder.</summary>
public static class CampaignCreditsBackgroundSource
{
    public const int EncodedBytes = 512 + 256 * 192;
    public sealed class Snapshot
    {
        private readonly byte[] _rgba;
        public string SourceSha256 { get; }
        public int ByteLength => EncodedBytes;
        public int Width => 256;
        public int Height => 192;
        public int PixelCount => 256 * 192;
        internal ReadOnlyMemory<byte> Rgba => _rgba;
        internal Snapshot(string hash, byte[] rgba) { SourceSha256 = hash; _rgba = rgba; }
        public ColorRgba PixelAt(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(x), "Bitmap coordinate is outside its extent.");
            int at = (y * Width + x) * 4;
            return new(_rgba[at], _rgba[at + 1], _rgba[at + 2], _rgba[at + 3]);
        }
    }
    public static Snapshot Read(ReadOnlySpan<byte> source)
    {
        Require(source.Length == EncodedBytes, "Credits background must contain one palette and one 256x192 index plane.");
        byte[] owned = source.ToArray();
        byte[] rgba = new byte[256 * 192 * 4];
        for (int pixel = 0; pixel < 256 * 192; pixel++)
        {
            ushort color = U16(owned, owned[512 + pixel] * 2);
            int at = pixel * 4;
            // Original frontend bitmap conversion truncates. Native model textures
            // intentionally retain their separate rounded RGB5 conversion.
            rgba[at] = (byte)((color & 31) * 255 / 31);
            rgba[at + 1] = (byte)(((color >> 5) & 31) * 255 / 31);
            rgba[at + 2] = (byte)(((color >> 10) & 31) * 255 / 31);
            rgba[at + 3] = 255;
        }
        return new(Hash(owned), rgba);
    }
}

/// <summary>Immutable glyph metrics bound to one admitted native font atlas.</summary>
public static class CampaignCreditsFontSource
{
    public const int GlyphCount = 480, CellSize = 8, Columns = 16, Rows = 32;
    public sealed class Snapshot
    {
        private readonly byte[] _metrics;
        public string ModelSha256 { get; }
        public string MetricsSha256 { get; }
        public int GlyphCount => CampaignCreditsFontSource.GlyphCount;
        internal CampaignCreditsModelSource.Snapshot Model { get; }
        internal Snapshot(CampaignCreditsModelSource.Snapshot model, byte[] metrics)
        { Model = model; ModelSha256 = model.SourceSha256; _metrics = metrics; MetricsSha256 = Hash(metrics); }
        public int Width(int glyph) { CheckGlyph(glyph); return _metrics[glyph]; }
        public int VerticalOffset(int glyph) { CheckGlyph(glyph); return unchecked((sbyte)_metrics[GlyphCount + glyph]); }
        private static void CheckGlyph(int glyph)
        { if ((uint)glyph >= CampaignCreditsFontSource.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph)); }
    }
    public static Snapshot Read(CampaignCreditsModelSource.Snapshot font, ReadOnlySpan<byte> widths, ReadOnlySpan<byte> verticalOffsets)
    {
        ArgumentNullException.ThrowIfNull(font);
        Require(widths.Length == GlyphCount && verticalOffsets.Length == GlyphCount, "Credits font metric extents differ from the supported atlas.");
        Require(font.NodeCount == 1 && font.MaterialCount == 1 && font.MeshCount == 1 && font.TextureCount == 1 && font.MatrixCount == 1,
            "Credits font model profile is unsupported.");
        var bytes = font.Memory.Span;
        int texture = (int)U32(bytes, 44), material = (int)U32(bytes, 16);
        Require(bytes[texture] == 6 && U16(bytes, texture + 2) == 128 && U16(bytes, texture + 4) == 256
            && I16(bytes, material + 68) == 0 && I16(bytes, material + 70) == 0,
            "Credits font requires the native 128x256 A3I5 atlas and its first palette.");
        byte[] metrics = new byte[GlyphCount * 2]; widths.CopyTo(metrics); verticalOffsets.CopyTo(metrics.AsSpan(GlyphCount));
        for (int glyph = 0; glyph < GlyphCount; glyph++)
            Require(metrics[glyph] is >= 1 and <= 9 && unchecked((sbyte)metrics[GlyphCount + glyph]) is >= -2 and <= 1,
                "Credits font metric value is unsupported.");
        return new(font, metrics);
    }
}
