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

        CheckCompactTextureData(Check);

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

        var hugeTexels = new HugeTexelDataList();
        Recolor invalidPaletteBeforePixels = CreateFixture(
            new[] { TextureFormat.Palette8Bit }, 1, hugeTexels);
        bool invalidPaletteMatches = SameException(
            () => NativeTexturePixelBuffer.Decode(invalidPaletteBeforePixels, 0, 1),
            () => LegacyDecode(invalidPaletteBeforePixels, 0, 1));
        Check(invalidPaletteMatches && hugeTexels.CountReads == 0
            && hugeTexels.IndexerReads == 0 && hugeTexels.EnumeratorReads == 0,
            "invalid indexed palette rejects before pixel allocation or texel traversal");

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

    private static void CheckCompactTextureData(Action<bool, string> check)
    {
        var cases = new (TextureFormat Format, int Width, int Height, uint Opaque, byte[] Payload)[]
        {
            (TextureFormat.DirectRgb, 4, 1, 1, new byte[] { 0x1F, 0x00, 0xE0, 0x83, 0x10, 0x42, 0xFF, 0xFF }),
            (TextureFormat.Palette2Bit, 4, 1, 0, new byte[] { 0xE4 }),
            (TextureFormat.Palette2Bit, 4, 1, 1, new byte[] { 0xE4 }),
            (TextureFormat.Palette4Bit, 4, 1, 0, new byte[] { 0x10, 0x32 }),
            (TextureFormat.Palette8Bit, 4, 1, 0, new byte[] { 0, 1, 255, 128 }),
            (TextureFormat.PaletteA5I3, 4, 1, 0, new byte[] { 0x00, 0xF8, 0x7F, 0xFF }),
            (TextureFormat.PaletteA3I5, 4, 1, 0, new byte[] { 0x00, 0xE0, 0xFF, 0x1F }),
            ((TextureFormat)3, 3, 1, 0, new byte[] { 0x00, 0x7F, 0xFF })
        };

        foreach ((TextureFormat format, int width, int height, uint opaque, byte[] payload) in cases)
        {
            const int imageOffset = 2;
            Texture texture = CreateRawTexture(format, width, height, imageOffset, opaque);
            byte[] source = new byte[imageOffset + payload.Length];
            source[0] = 0xA5;
            source[1] = 0x5A;
            payload.CopyTo(source, imageOffset);
            List<TextureData> expected = LegacyDecodeTextureData(texture, source);
            CompactTextureData actual = CompactTextureData.Decode(texture, source);
            check(SameTextureData(actual, expected),
                $"{format} compact storage preserves eager decoded texel values and order");
            check(actual.SequenceEqual(expected),
                $"{format} generic enumeration preserves texel values and order");
            check(EnumeratesSame((System.Collections.IEnumerable)actual, expected),
                $"{format} non-generic enumeration preserves texel values and order");
            if (format != (TextureFormat)3)
            {
                MphRead.Utility.Repack.TextureInfo expectedRepack
                    = MphRead.Utility.Repack.ConvertData(texture, expected);
                MphRead.Utility.Repack.TextureInfo actualRepack
                    = MphRead.Utility.Repack.ConvertData(texture, actual);
                check(expectedRepack.Data.SequenceEqual(actualRepack.Data),
                    $"{format} model repack bytes match the list-backed data");
            }
            source[imageOffset] ^= 0xFF;
            check(SameTextureData(actual, expected),
                $"{format} decoded texels own their data after the source buffer changes");
        }

        Texture nonDivisible = CreateRawTexture(TextureFormat.Palette2Bit, 3, 1, 0, 1);
        byte[] nonDivisibleSource = { 0xE4 };
        check(SameException(() => CompactTextureData.Decode(nonDivisible, nonDivisibleSource),
                () => LegacyDecodeTextureData(nonDivisible, nonDivisibleSource)),
            "packed palette dimensions retain the old non-divisible pixel-count exception");

        Texture truncatedPalette = CreateRawTexture(TextureFormat.Palette2Bit, 8, 1, 1, 1);
        byte[] shortPaletteSource = { 0xE4, 0x1B };
        check(SameException(() => CompactTextureData.Decode(truncatedPalette, shortPaletteSource),
                () => LegacyDecodeTextureData(truncatedPalette, shortPaletteSource)),
            "truncated packed palette bytes retain the old eager indexer exception");

        Texture truncatedDirect = CreateRawTexture(TextureFormat.DirectRgb, 2, 1, 1, 1);
        byte[] shortDirectSource = { 0x00, 0x1F };
        check(SameException(() => CompactTextureData.Decode(truncatedDirect, shortDirectSource),
                () => LegacyDecodeTextureData(truncatedDirect, shortDirectSource)),
            "truncated direct-color bytes retain the old eager span exception");

        Texture hugeOffsetPalette = CreateRawTexture(TextureFormat.Palette8Bit, 2, 1, uint.MaxValue, 1);
        Texture hugeOffsetDirect = CreateRawTexture(TextureFormat.DirectRgb, 2, 1, uint.MaxValue, 1);
        byte[] offsetSource = { 0, 1, 2, 3 };
        check(SameException(() => CompactTextureData.Decode(hugeOffsetPalette, offsetSource),
                () => LegacyDecodeTextureData(hugeOffsetPalette, offsetSource))
            && SameException(() => CompactTextureData.Decode(hugeOffsetDirect, offsetSource),
                () => LegacyDecodeTextureData(hugeOffsetDirect, offsetSource)),
            "large unsigned image offsets retain the old palette and direct-color exceptions");

        Texture emptyTexture = CreateRawTexture(TextureFormat.DirectRgb, 0, 0, 0, 1);
        Texture overflowedTexture = CreateRawTexture(TextureFormat.DirectRgb,
            ushort.MaxValue, ushort.MaxValue, 0, 1);
        Texture overflowedPackedTexture = CreateRawTexture(TextureFormat.Palette2Bit,
            ushort.MaxValue, ushort.MaxValue, 0, 1);
        byte[] emptySource = Array.Empty<byte>();
        check(SameTextureData(CompactTextureData.Decode(emptyTexture, emptySource),
                LegacyDecodeTextureData(emptyTexture, emptySource))
            && SameTextureData(CompactTextureData.Decode(overflowedTexture, emptySource),
                LegacyDecodeTextureData(overflowedTexture, emptySource))
            && SameException(() => CompactTextureData.Decode(overflowedPackedTexture, emptySource),
                () => LegacyDecodeTextureData(overflowedPackedTexture, emptySource)),
            "zero and overflowed dimensions preserve prior count and divisibility behavior");

        Texture rangeTexture = CreateRawTexture(TextureFormat.Palette8Bit, 2, 1, 0, 1);
        var rangeSource = new byte[] { 0, 1 };
        CompactTextureData rangeView = CompactTextureData.Decode(rangeTexture, rangeSource);
        List<TextureData> rangeList = LegacyDecodeTextureData(rangeTexture, rangeSource);
        check(SameException(() => _ = rangeView[-1], () => _ = rangeList[-1])
            && SameException(() => _ = rangeView[rangeView.Count], () => _ = rangeList[rangeList.Count]),
            "compact texture indexing retains List range exception types and parameter names");

        const int allocationPixels = 16384;
        Texture allocationTexture = CreateRawTexture(TextureFormat.Palette8Bit,
            allocationPixels, 1, 0, 1);
        var allocationSource = new byte[allocationPixels];
        for (int i = 0; i < allocationSource.Length; i++)
            allocationSource[i] = (byte)(i & 0xFF);
        List<TextureData> retainedLegacy = LegacyDecodeTextureData(allocationTexture, allocationSource);
        CompactTextureData retainedCompact = CompactTextureData.Decode(allocationTexture, allocationSource);
        long legacyRetainedBufferBytes = (long)retainedLegacy.Capacity * Marshal.SizeOf<TextureData>();
        long compactRetainedBufferBytes = retainedCompact.StorageByteCount;
        _ = MeasureLegacyTextureDecode(allocationTexture, allocationSource, 3);
        _ = MeasureCompactTextureDecode(allocationTexture, allocationSource, 3);
        long legacyAllocatedBytes = MeasureLegacyTextureDecode(allocationTexture, allocationSource, 8);
        long compactAllocatedBytes = MeasureCompactTextureDecode(allocationTexture, allocationSource, 8);
        check(compactRetainedBufferBytes < legacyRetainedBufferBytes
            && compactAllocatedBytes < legacyAllocatedBytes,
            $"compact decoded storage retains and allocates fewer bytes ({compactRetainedBufferBytes} < {legacyRetainedBufferBytes}; {compactAllocatedBytes} < {legacyAllocatedBytes})");
        Console.WriteLine($"[compacttexturedata-storage-json] {{\"schema\":1,\"pixelsPerTexture\":{allocationPixels},\"legacyRetainedBufferBytes\":{legacyRetainedBufferBytes},\"compactRetainedBufferBytes\":{compactRetainedBufferBytes},\"iterations\":8,\"legacyAllocatedBytes\":{legacyAllocatedBytes},\"compactAllocatedBytes\":{compactAllocatedBytes},\"scope\":\"synthetic eager texture decode and retained texel storage only\"}}");
        GC.KeepAlive(retainedLegacy);
        GC.KeepAlive(retainedCompact);
    }

    private static Texture CreateRawTexture(TextureFormat format, int width, int height,
        uint imageOffset, uint opaque)
    {
        Texture texture = new(format, (ushort)width, (ushort)height);
        byte[] bytes = new byte[Marshal.SizeOf<Texture>()];
        MemoryMarshal.Write(bytes.AsSpan(), in texture);
        int imageOffsetField = Marshal.OffsetOf<Texture>(nameof(Texture.ImageOffset)).ToInt32();
        int opaqueField = Marshal.OffsetOf<Texture>(nameof(Texture.Opaque)).ToInt32();
        MemoryMarshal.Write(bytes.AsSpan(imageOffsetField), in imageOffset);
        MemoryMarshal.Write(bytes.AsSpan(opaqueField), in opaque);
        return MemoryMarshal.Read<Texture>(bytes.AsSpan());
    }

    // Reference copy of Read.GetTextureData before compact storage. Keeping
    // this parser independent lets the check compare every decoded Data/Alpha
    // value rather than only comparing two views over the new representation.
    private static List<TextureData> LegacyDecodeTextureData(Texture texture, ReadOnlySpan<byte> textureBytes)
    {
        var data = new List<TextureData>();
        int pixelCount = texture.Width * texture.Height;
        int entriesPerByte = 1;
        if (texture.Format == TextureFormat.Palette2Bit) entriesPerByte = 4;
        else if (texture.Format == TextureFormat.Palette4Bit) entriesPerByte = 2;
        if (pixelCount % entriesPerByte != 0)
            throw new ProgramException($"Pixel count {pixelCount} is not divisible by {entriesPerByte}.");
        pixelCount /= entriesPerByte;
        if (texture.Format == TextureFormat.DirectRgb)
        {
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                ushort color = Read.SpanReadUshort(textureBytes, (int)(texture.ImageOffset + pixelIndex * 2));
                byte alpha = (color & 0x8000) == 0 ? (byte)0 : (byte)255;
                data.Add(new TextureData(color, alpha));
            }
        }
        else
        {
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                byte entry = textureBytes[(int)(texture.ImageOffset + pixelIndex)];
                for (int entryIndex = 0; entryIndex < entriesPerByte; entryIndex++)
                {
                    uint index = (uint)(entry >> ((pixelIndex * entriesPerByte + entryIndex) % entriesPerByte
                        * (8 / entriesPerByte)));
                    byte alpha = 255;
                    if (texture.Format == TextureFormat.Palette2Bit) index &= 0x3;
                    else if (texture.Format == TextureFormat.Palette4Bit) index &= 0xF;
                    else if (texture.Format == TextureFormat.PaletteA5I3)
                    {
                        index &= 0x7;
                        alpha = (byte)MathF.Round((entry >> 3) / 31.0f * 255.0f);
                    }
                    else if (texture.Format == TextureFormat.PaletteA3I5)
                    {
                        index &= 0x1F;
                        alpha = (byte)MathF.Round((entry >> 5) / 7.0f * 255.0f);
                    }
                    if ((texture.Format == TextureFormat.Palette2Bit
                            || texture.Format == TextureFormat.Palette4Bit
                            || texture.Format == TextureFormat.Palette8Bit)
                        && texture.Opaque == 0 && index == 0)
                    {
                        alpha = 0;
                    }
                    data.Add(new TextureData(index, alpha));
                }
            }
        }
        return data;
    }

    private static bool SameTextureData(IReadOnlyList<TextureData> left, IReadOnlyList<TextureData> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (left[index].Data != right[index].Data || left[index].Alpha != right[index].Alpha)
                return false;
        }
        return true;
    }

    private static bool EnumeratesSame(System.Collections.IEnumerable values,
        IReadOnlyList<TextureData> expected)
    {
        int index = 0;
        foreach (object? value in values)
        {
            if (index >= expected.Count || value is not TextureData texel
                || texel.Data != expected[index].Data || texel.Alpha != expected[index].Alpha)
            {
                return false;
            }
            index++;
        }
        return index == expected.Count;
    }

    private static long MeasureLegacyTextureDecode(Texture texture, byte[] textureBytes, int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        List<TextureData>? retained = null;
        for (int iteration = 0; iteration < iterations; iteration++)
            retained = LegacyDecodeTextureData(texture, textureBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(retained);
        return allocated;
    }

    private static long MeasureCompactTextureDecode(Texture texture, byte[] textureBytes, int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        CompactTextureData? retained = null;
        for (int iteration = 0; iteration < iterations; iteration++)
            retained = CompactTextureData.Decode(texture, textureBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(retained);
        return allocated;
    }

    private sealed class HugeTexelDataList : IReadOnlyList<TextureData>
    {
        public int CountReads { get; private set; }
        public int IndexerReads { get; private set; }
        public int EnumeratorReads { get; private set; }

        public int Count
        {
            get
            {
                CountReads++;
                return int.MaxValue;
            }
        }

        public TextureData this[int index]
        {
            get
            {
                IndexerReads++;
                throw new InvalidOperationException("Synthetic texel list must not be indexed.");
            }
        }

        public IEnumerator<TextureData> GetEnumerator()
        {
            EnumeratorReads++;
            throw new InvalidOperationException("Synthetic texel list must not be enumerated.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
