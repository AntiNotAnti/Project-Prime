using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class NativeTexturePixelBufferCheck
{
    private static readonly TextureFormat[] Formats =
    {
        TextureFormat.DirectRgb,
        TextureFormat.Palette2Bit,
        TextureFormat.Palette4Bit,
        TextureFormat.Palette8Bit,
        TextureFormat.PaletteA5I3,
        TextureFormat.PaletteA3I5
    };

    internal static int Run()
    {
        int failures = 0;
        void Check(bool pass, string name)
        {
            Console.WriteLine($"[nativepixelbuffercheck] {(pass ? "PASS" : "FAIL")}: {name}");
            if (!pass) failures++;
        }

        Recolor recolor = CreateFixture(Formats, 4);
        for (int textureId = 0; textureId < Formats.Length; textureId++)
        {
            IReadOnlyList<ColorRgba> expected = LegacyDecode(recolor, textureId, 0);
            ColorRgba[] actual = NativeTexturePixelBuffer.Decode(recolor, textureId, 0);
            string label = Formats[textureId].ToString();
            Check(actual.SequenceEqual(expected), $"{label} decode matches the previous per-texel result");

            NativeTexturePixelBuffer.Summary summary = NativeTexturePixelBuffer.Summarize(actual);
            var expectedSummary = LegacySummary(expected);
            Check(summary.OnlyOpaque == expectedSummary.OnlyOpaque
                && SameBits(summary.FlatColor, expectedSummary.FlatColor),
                $"{label} alpha classification and flat color retain their exact result");

            uint[] packed = NativeTexturePixelBuffer.Pack(actual);
            uint[] legacyPacked = LegacyPack(expected);
            bool packedMatches = packed.Length == expected.Count;
            for (int i = 0; packedMatches && i < packed.Length; i++)
            {
                ColorRgba pixel = expected[i];
                uint value = packed[i];
                packedMatches = value == pixel.ToUint()
                    && (byte)(value >> 0) == pixel.Red
                    && (byte)(value >> 8) == pixel.Green
                    && (byte)(value >> 16) == pixel.Blue
                    && (byte)(value >> 24) == pixel.Alpha;
            }
            Check(packedMatches, $"{label} packed RGBA bytes preserve source order and alpha");
            Check(SameNativeBytes(actual, legacyPacked),
                $"{label} direct RGBA array bytes match the previous packed uint upload bytes");

            foreach (int factor in new[] { 2, 4 })
            {
                uint[] legacyScaled = TextureUpscaler.Scale(legacyPacked, 4, 1, factor,
                    out int legacyWidth, out int legacyHeight);
                uint[] candidateScaled = TextureUpscaler.Scale(packed, 4, 1, factor,
                    out int candidateWidth, out int candidateHeight);
                Check(legacyWidth == candidateWidth && legacyHeight == candidateHeight
                    && legacyScaled.SequenceEqual(candidateScaled),
                    $"{label} {factor}x upscaling matches the previous packed upload");
            }
        }

        IReadOnlyList<ColorRgba> directWithIgnoredPalette = LegacyDecode(recolor, 0, int.MaxValue);
        Check(NativeTexturePixelBuffer.Decode(recolor, 0, int.MaxValue).SequenceEqual(directWithIgnoredPalette),
            "direct RGB continues to ignore palette IDs");

        Check(SameException(() => NativeTexturePixelBuffer.Decode(recolor, -1, 0),
                () => LegacyDecode(recolor, -1, 0))
            && SameException(() => NativeTexturePixelBuffer.Decode(recolor, Formats.Length, 0),
                () => LegacyDecode(recolor, Formats.Length, 0)),
            "invalid texture IDs retain the previous exception type and parameter");
        Check(SameException(() => NativeTexturePixelBuffer.Decode(recolor, 1, -1),
                () => LegacyDecode(recolor, 1, -1))
            && SameException(() => NativeTexturePixelBuffer.Decode(recolor, 1, 1),
                () => LegacyDecode(recolor, 1, 1)),
            "invalid indexed palette IDs retain the previous exception type and parameter");

        Recolor invalidIndex = CreateFixture(new[] { TextureFormat.Palette8Bit }, 1,
            new[] { new TextureData(4, 255) });
        Check(SameException(() => NativeTexturePixelBuffer.Decode(invalidIndex, 0, 0),
                () => LegacyDecode(invalidIndex, 0, 0)),
            "out-of-range texel palette indices retain the existing list-index failure");

        Recolor transparent = CreateFixture(new[] { TextureFormat.DirectRgb }, 4,
            new[]
            {
                new TextureData(0x001F, 0), new TextureData(0x03E0, 0),
                new TextureData(0x7C00, 0), new TextureData(0x4210, 0)
            });
        NativeTexturePixelBuffer.Summary transparentSummary = NativeTexturePixelBuffer.Summarize(
            NativeTexturePixelBuffer.Decode(transparent, 0, 0));
        var transparentExpected = LegacySummary(LegacyDecode(transparent, 0, 0));
        Check(!transparentSummary.OnlyOpaque
            && SameBits(transparentSummary.FlatColor, transparentExpected.FlatColor),
            "fully transparent pixels keep the previous unweighted color fallback");

        Recolor empty = CreateFixture(new[] { TextureFormat.DirectRgb }, 0,
            Array.Empty<TextureData>());
        NativeTexturePixelBuffer.Summary emptySummary = NativeTexturePixelBuffer.Summarize(
            NativeTexturePixelBuffer.Decode(empty, 0, 0));
        Check(emptySummary.OnlyOpaque && SameBits(emptySummary.FlatColor, Vector3.One),
            "empty texture keeps opaque classification and white flat-color fallback");

        var allocationFixture = CreateFixture(new[] { TextureFormat.Palette8Bit }, 4096);
        // Warm both paths before measuring per-thread managed allocations.
        _ = MeasureLegacy(allocationFixture, 2);
        _ = MeasureCandidate(allocationFixture, 2);
        long legacyBytes = MeasureLegacy(allocationFixture, 8);
        long candidateBytes = MeasureCandidate(allocationFixture, 8);
        Check(candidateBytes < legacyBytes,
            $"native array preparation allocates less than list decode plus packed upload ({candidateBytes} < {legacyBytes} bytes)");
        Console.WriteLine($"[nativepixelbuffer-allocation-json] {{\"schema\":1,\"pixelsPerTexture\":4096,\"iterations\":8,\"legacyAllocatedBytes\":{legacyBytes},\"candidateAllocatedBytes\":{candidateBytes},\"scope\":\"synthetic CPU texture preparation only; excludes OpenGL, upscaling and gameplay\"}}");

        return failures;
    }

    private static Recolor CreateFixture(IReadOnlyList<TextureFormat> formats, int pixelCount,
        IReadOnlyList<TextureData>? firstTextureData = null)
    {
        var textures = new Texture[formats.Count];
        var textureData = new IReadOnlyList<TextureData>[formats.Count];
        for (int texture = 0; texture < formats.Count; texture++)
        {
            textures[texture] = new Texture(formats[texture], (ushort)pixelCount, 1);
            if (texture == 0 && firstTextureData != null)
            {
                textureData[texture] = firstTextureData;
                continue;
            }
            var texels = new TextureData[pixelCount];
            for (int i = 0; i < texels.Length; i++)
            {
                uint data = formats[texture] == TextureFormat.DirectRgb
                    ? (uint)((i * 977 + 0x001F) & 0x7FFF)
                    : (uint)(i & 3);
                byte alpha = (byte)(i % 4 switch
                {
                    0 => 255,
                    1 => 113,
                    2 => 0,
                    _ => 37
                });
                texels[i] = new TextureData(data, alpha);
            }
            textureData[texture] = texels;
        }

        var palette = new IReadOnlyList<PaletteData>[]
        {
            new PaletteData[]
            {
                new(0x001F), new(0x03E0), new(0x7C00), new(0x4210)
            }
        };
        return new Recolor("native-pixel-buffer-check", textures,
            new Palette[] { default }, textureData, palette);
    }

    // Reference copy of the former Model.GetPixels implementation. Keep this
    // intentionally list-backed so the allocation check includes both old
    // intermediate pixel and packed-upload buffers.
    private static IReadOnlyList<ColorRgba> LegacyDecode(Recolor recolor, int textureId, int paletteId)
    {
        if (textureId < 0 || textureId >= recolor.TextureData.Count)
            throw new ArgumentException(nameof(textureId));
        var pixels = new List<ColorRgba>();
        TextureFormat format = recolor.Textures[textureId].Format;
        if (format == TextureFormat.DirectRgb)
        {
            for (int i = 0; i < recolor.TextureData[textureId].Count; i++)
            {
                uint color = recolor.TextureData[textureId][i].Data;
                byte alpha = recolor.TextureData[textureId][i].Alpha;
                pixels.Add(new ColorRgba(color, alpha));
            }
        }
        else
        {
            if (paletteId < 0 || paletteId >= recolor.PaletteData.Count)
                throw new ArgumentException(nameof(paletteId));
            for (int i = 0; i < recolor.TextureData[textureId].Count; i++)
            {
                int index = (int)recolor.TextureData[textureId][i].Data;
                ushort color = recolor.PaletteData[paletteId][index].Data;
                byte alpha = recolor.TextureData[textureId][i].Alpha;
                pixels.Add(new ColorRgba(color, alpha));
            }
        }
        return pixels;
    }

    private static (bool OnlyOpaque, Vector3 FlatColor) LegacySummary(IReadOnlyList<ColorRgba> pixels)
    {
        bool onlyOpaque = true;
        float red = 0, green = 0, blue = 0, weight = 0;
        float plainRed = 0, plainGreen = 0, plainBlue = 0, count = 0;
        foreach (ColorRgba pixel in pixels)
        {
            onlyOpaque &= pixel.Alpha == 255;
            float alpha = pixel.Alpha / 255f;
            red += pixel.Red * alpha;
            green += pixel.Green * alpha;
            blue += pixel.Blue * alpha;
            weight += alpha;
            plainRed += pixel.Red;
            plainGreen += pixel.Green;
            plainBlue += pixel.Blue;
            count++;
        }
        Vector3 flatColor = weight > 0.01f
            ? new Vector3(red, green, blue) / weight / 255f
            : count > 0
                ? new Vector3(plainRed, plainGreen, plainBlue) / count / 255f
                : Vector3.One;
        return (onlyOpaque, flatColor);
    }

    private static bool SameBits(Vector3 left, Vector3 right)
        => BitConverter.SingleToInt32Bits(left.X) == BitConverter.SingleToInt32Bits(right.X)
            && BitConverter.SingleToInt32Bits(left.Y) == BitConverter.SingleToInt32Bits(right.Y)
            && BitConverter.SingleToInt32Bits(left.Z) == BitConverter.SingleToInt32Bits(right.Z);

    private static uint[] LegacyPack(IReadOnlyList<ColorRgba> pixels)
    {
        var packed = new List<uint>();
        foreach (ColorRgba pixel in pixels) packed.Add(pixel.ToUint());
        return packed.ToArray();
    }

    private static bool SameNativeBytes(ColorRgba[] pixels, uint[] legacyPacked)
        => MemoryMarshal.AsBytes(pixels.AsSpan()).SequenceEqual(
            MemoryMarshal.AsBytes(legacyPacked.AsSpan()));

    private static (Type? ExceptionType, string? Parameter) Capture(Action action)
    {
        try { action(); return (null, null); }
        catch (Exception exception)
        {
            return (exception.GetType(), (exception as ArgumentException)?.ParamName);
        }
    }

    private static bool SameException(Action candidate, Action legacy)
        => Capture(candidate) == Capture(legacy);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureLegacy(Recolor recolor, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            IReadOnlyList<ColorRgba> pixels = LegacyDecode(recolor, 0, 0);
            var packed = new List<uint>();
            bool onlyOpaque = true;
            float red = 0, green = 0, blue = 0, weight = 0;
            float plainRed = 0, plainGreen = 0, plainBlue = 0, count = 0;
            foreach (ColorRgba pixel in pixels)
            {
                packed.Add(pixel.ToUint());
                onlyOpaque &= pixel.Alpha == 255;
                float alpha = pixel.Alpha / 255f;
                red += pixel.Red * alpha;
                green += pixel.Green * alpha;
                blue += pixel.Blue * alpha;
                weight += alpha;
                plainRed += pixel.Red;
                plainGreen += pixel.Green;
                plainBlue += pixel.Blue;
                count++;
            }
            uint[] upload = packed.ToArray();
            checksum += upload[iteration % upload.Length];
            checksum += onlyOpaque ? 1 : 0;
            checksum += BitConverter.SingleToInt32Bits(
                red + green + blue + weight + plainRed + plainGreen + plainBlue + count);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return allocated;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureCandidate(Recolor recolor, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            ColorRgba[] pixels = NativeTexturePixelBuffer.Decode(recolor, 0, 0);
            _ = NativeTexturePixelBuffer.Summarize(pixels);
            checksum += pixels[iteration % pixels.Length].ToUint();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return allocated;
    }
}
