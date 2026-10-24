using System;
using System.IO;

namespace MphRead.Mods.Render.Hud;

// Shared by the renderers and the published-assembly check. A wildcard build
// item silently omits missing inputs; neither null nor a partial atlas is valid.
internal static class HudAssetReader
{
    internal const string DsFontName = "MphRead.Assets.DsHud.font.bin";
    internal const string DsBackgroundName = "MphRead.Assets.DsHud.background.rgb";
    private static readonly Lazy<HudFonts.Atlas> DsFontData = new(() => ReadAtlas(DsFontName, 768, 384));
    private static readonly Lazy<byte[]> DsBackgroundData = new(() => ReadBytes(DsBackgroundName, 768 * 576 * 3));
    internal static HudFonts.Atlas DsFont => DsFontData.Value;
    internal static byte[] DsBackground => DsBackgroundData.Value;

    internal static HudFonts.Atlas ReadAtlas(string name, int width, int height)
    {
        using Stream? stream = typeof(HudAssetReader).Assembly.GetManifestResourceStream(name);
        return ReadAtlas(stream, name, width, height);
    }

    internal static HudFonts.Atlas ReadAtlas(Stream? stream, string name, int width, int height)
    {
        if (stream == null)
            throw new InvalidDataException($"Required embedded HUD asset is missing: {name}. Rebuild this client with its HUD resources.");
        int count = checked(width * height);
        if (width <= 0 || height <= 0 || count > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(width));
        int expected = checked(96 * sizeof(float) + count);
        if (stream.CanSeek && stream.Length - stream.Position != expected)
            throw new InvalidDataException($"Embedded HUD asset {name} must contain exactly {expected} bytes.");
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var advances = new float[96];
        try
        {
            for (int i = 0; i < advances.Length; i++)
            {
                advances[i] = reader.ReadSingle();
                if (!float.IsFinite(advances[i]) || advances[i] < 0 || advances[i] > 512)
                    throw new InvalidDataException($"Embedded HUD asset {name} has an invalid glyph advance at {i}.");
            }
            var alpha = new byte[count];
            stream.ReadExactly(alpha);
            if (stream.ReadByte() != -1)
                throw new InvalidDataException($"Embedded HUD asset {name} contains trailing data.");
            return new HudFonts.Atlas(advances, alpha);
        }
        catch (EndOfStreamException e) { throw new InvalidDataException($"Embedded HUD asset {name} is truncated.", e); }
    }

    internal static byte[] ReadBytes(string name, int expected)
    {
        using Stream? stream = typeof(HudAssetReader).Assembly.GetManifestResourceStream(name);
        if (stream == null || (stream.CanSeek && stream.Length != expected))
            throw new InvalidDataException($"Missing or incomplete embedded HUD asset: {name} ({expected} bytes required).");
        var bytes = new byte[expected];
        try { stream.ReadExactly(bytes); }
        catch (EndOfStreamException e) { throw new InvalidDataException($"Embedded HUD asset {name} is truncated.", e); }
        if (stream.ReadByte() != -1) throw new InvalidDataException($"Embedded HUD asset {name} contains trailing data.");
        return bytes;
    }
}
