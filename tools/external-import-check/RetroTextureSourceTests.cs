using System.Buffers.Binary;
using MphRead.Mods.Import.Retro;

// Own-generated numeric fixtures only. No commercial TXTR, filesystem output,
// material activation, sampler policy, GL or image-library oracle is involved.
internal static class RetroTextureSourceTests
{
    private static int checks;

    internal static void Run()
    {
        checks = 0;
        Check(RetroTextureSource.MaxDimension == 4096 && RetroTextureSource.MaxMipCount == 13
            && RetroTextureSource.MaxSourceBytes == 64 * 1024 * 1024
            && RetroTextureSource.MaxOutputBytes == 64 * 1024 * 1024
            && RetroTextureSource.PixelContract == "gx-expanded-cmpr-3-5-rgb5a3-v1",
            "versioned expanded-channel contract and finite source/output bounds");
        CmprSelectorsAndInterpolation();
        CmprTransparency();
        CmprTilesAndCropping();
        Rgb5A3ChannelsAndTiles();
        MipsAndBudget();
        Padding();
        MalformedAndAllocation();
        DetachedReadOnlyOutput();
        Console.WriteLine($"TXTR SOURCE PASS {checks} synthetic checks; bounded CMPR/RGB5A3 source conversion only, no source media/material/GL/runtime acceptance.");
    }

    private static void CmprSelectorsAndInterpolation()
    {
        byte[] source = Texture(10, 8, 8);
        Block(source, 12, 0xf800, 0x001f, 0x1b, 0xe4, 0xaa, 0xff);
        SolidBlock(source, 20, 0x07e0); // top-right
        SolidBlock(source, 28, 0xffff); // bottom-left
        SolidBlock(source, 36, 0xffe0); // bottom-right
        var decoded = RetroTextureSource.DecodeTop(source);
        Pixel(decoded, 0, 0, 255, 0, 0, 255, "BE first endpoint");
        Pixel(decoded, 1, 0, 0, 0, 255, 255, "BE second endpoint");
        Pixel(decoded, 2, 0, 159, 0, 95, 255, "GX 5:3 interpolation differs from DXT1 two-thirds");
        Pixel(decoded, 3, 0, 95, 0, 159, 255, "GX 3:5 interpolation");
        Pixel(decoded, 0, 1, 95, 0, 159, 255, "selectors are MSB first within their row");
        Pixel(decoded, 3, 1, 255, 0, 0, 255, "row selector order is not reversed");
        Pixel(decoded, 0, 2, 159, 0, 95, 255, "selector rows follow source order");
        Pixel(decoded, 0, 3, 95, 0, 159, 255, "top-first fourth row");
        Pixel(decoded, 4, 0, 0, 255, 0, 255, "8x8 tile top-right 4x4 block");
        Pixel(decoded, 0, 4, 255, 255, 255, 255, "8x8 tile bottom-left 4x4 block");
        Pixel(decoded, 4, 4, 255, 255, 0, 255, "8x8 tile bottom-right 4x4 block");

        byte[] expanded = Texture(10, 1, 1);
        SolidBlock(expanded, 12, 0x2207); // R5=4, G6=16, B5=7
        Pixel(RetroTextureSource.DecodeTop(expanded), 0, 0, 33, 65, 57, 255,
            "565 endpoints use bit replication rather than floor scaling");
        var tiny = Texture(10, 4, 4);
        source.AsSpan(12, 32).CopyTo(tiny.AsSpan(12));
        var cropped = RetroTextureSource.DecodeTop(tiny);
        Check(cropped.Pixels.Length == 64 && cropped.Layout.Mips[0].Length == 32,
            "tiny CMPR output crops a complete 8x8 encoded tile");
        Pixel(cropped, 3, 3, 95, 0, 159, 255, "tiny 4x4 retains only top-left block in top-first order");
    }

    private static void CmprTransparency()
    {
        byte[] source = Texture(10, 4, 4);
        Block(source, 12, 0x001f, 0xf800, 0x1b, 0x1b, 0x1b, 0x1b);
        var decoded = RetroTextureSource.DecodeTop(source);
        Pixel(decoded, 0, 0, 0, 0, 255, 255, "three-color first endpoint stays opaque");
        Pixel(decoded, 1, 0, 255, 0, 0, 255, "three-color second endpoint stays opaque");
        Pixel(decoded, 2, 0, 127, 0, 127, 255, "three-color average stays opaque");
        Pixel(decoded, 3, 0, 127, 0, 127, 0, "transparent selector retains averaged RGB without premultiplication");
        Block(source, 12, 0x2207, 0x2207, 0x1b, 0x1b, 0x1b, 0x1b);
        decoded = RetroTextureSource.DecodeTop(source);
        Pixel(decoded, 2, 0, 33, 65, 57, 255, "equal endpoints take three-color branch");
        Pixel(decoded, 3, 0, 33, 65, 57, 0, "equal endpoints keep transparent average RGB");
    }

    private static void CmprTilesAndCropping()
    {
        foreach (var size in new[] { (9, 5), (5, 9) })
        {
            byte[] source = Texture(10, size.Item1, size.Item2);
            ushort[] colors = { 0xf800, 0x07e0, 0x001f, 0xffff, 0xffe0, 0x07ff, 0xf81f, 0 };
            for (int block = 0; block < colors.Length; block++) SolidBlock(source, 12 + block * 8, colors[block]);
            var decoded = RetroTextureSource.DecodeTop(source);
            Check(decoded.Pixels.Length == size.Item1 * size.Item2 * 4 && decoded.Layout.PayloadEnd == 76,
                "odd non-square CMPR crops output but consumes complete source tiles");
            Pixel(decoded, 0, 0, 255, 0, 0, 255, "first cropped tile starts top-left");
            Pixel(decoded, 4, 0, 0, 255, 0, 255, "first cropped tile top-right");
            Pixel(decoded, 0, 4, 0, 0, 255, 255, "first cropped tile bottom-left");
            Pixel(decoded, 4, 4, 255, 255, 255, 255, "first cropped tile bottom-right");
            if (size.Item1 == 9)
            {
                Pixel(decoded, 8, 0, 255, 255, 0, 255, "next horizontal tile starts after all four previous blocks");
                Pixel(decoded, 8, 4, 255, 0, 255, 255, "odd-width cropped last tile lower row");
            }
            else
            {
                Pixel(decoded, 0, 8, 255, 255, 0, 255, "next vertical tile starts after all four previous blocks");
                Pixel(decoded, 4, 8, 0, 255, 255, 255, "odd-height cropped last tile right block");
            }
        }
    }

    private static void Rgb5A3ChannelsAndTiles()
    {
        byte[] source = Texture(8, 4, 4);
        Word(source, 12, 0x921f); // opaque R5=4, G5=16, B5=31
        Word(source, 14, 0x3a5f); // alpha3=3, RGB4=10/5/15
        Word(source, 16, 0x0234); // zero alpha still keeps nonzero straight RGB
        Word(source, 18, 0x7fff);
        var decoded = RetroTextureSource.DecodeTop(source);
        Pixel(decoded, 0, 0, 33, 132, 255, 255, "opaque RGB5A3 bit-expanded5 channels");
        Pixel(decoded, 1, 0, 170, 85, 255, 109, "RGB5A3 four-bit RGB and replicated three-bit alpha");
        Pixel(decoded, 2, 0, 34, 51, 68, 0, "RGB5A3 transparent color stays straight RGBA");
        Pixel(decoded, 3, 0, 255, 255, 255, 255, "RGB5A3 maximum alpha branch");
        int[] alpha = { 0, 36, 73, 109, 146, 182, 219, 255 };
        for (int i = 0; i < alpha.Length; i++)
        {
            byte[] one = Texture(8, 1, 1);
            Word(one, 12, (ushort)((i << 12) | 0x0234));
            var pixel = RetroTextureSource.DecodeTop(one);
            Pixel(pixel, 0, 0, 34, 51, 68, alpha[i], "literal alpha expansion vector " + i);
            Check(pixel.Pixels.Length == 4 && pixel.Layout.Mips[0].Length == 32,
                "1x1 RGB5A3 consumes a complete 4x4 source tile");
        }
        byte[] tiles = Texture(8, 8, 8);
        ushort[] tileColors = { 0xfc00, 0x83e0, 0x801f, 0xffff };
        for (int tile = 0; tile < 4; tile++)
            for (int i = 0; i < 16; i++) Word(tiles, 12 + tile * 32 + i * 2, tileColors[tile]);
        decoded = RetroTextureSource.DecodeTop(tiles);
        Pixel(decoded, 0, 0, 255, 0, 0, 255, "RGB5A3 tile top-left");
        Pixel(decoded, 4, 0, 0, 255, 0, 255, "RGB5A3 tile top-right");
        Pixel(decoded, 0, 4, 0, 0, 255, 255, "RGB5A3 tile bottom-left, no row flip");
        Pixel(decoded, 4, 4, 255, 255, 255, 255, "RGB5A3 tile bottom-right");
        byte[] odd = Texture(8, 5, 3);
        for (int i = 0; i < 16; i++)
        { Word(odd, 12 + i * 2, 0xfc00); Word(odd, 44 + i * 2, 0x801f); }
        decoded = RetroTextureSource.DecodeTop(odd);
        Check(decoded.Pixels.Length == 60 && decoded.Layout.PayloadEnd == 76, "odd RGB5A3 crops only output, not tiled source size");
        Pixel(decoded, 3, 2, 255, 0, 0, 255, "odd RGB5A3 final first-tile column");
        Pixel(decoded, 4, 2, 0, 0, 255, 255, "odd RGB5A3 final cropped second-tile column");
    }

    private static void MipsAndBudget()
    {
        byte[] source = Texture(10, 9, 5, 4);
        var layout = RetroTextureSource.Inspect(source, 180);
        Check(layout.Format == 10 && layout.Width == 9 && layout.Height == 5 && layout.MipCount == 4
            && layout.PayloadEnd == 172 && layout.PaddingBytes == 0 && layout.TopRgbaBytes == 180,
            "complete non-square mip chain metadata with exact top-only output budget");
        var expected = new[] { (9, 5, 12, 64), (4, 2, 76, 32), (2, 1, 108, 32), (1, 1, 140, 32) };
        for (int i = 0; i < expected.Length; i++)
        {
            var mip = layout.Mips[i]; var e = expected[i];
            Check((mip.Width, mip.Height, mip.Offset, mip.Length) == e, "literal tiled mip dimensions, offsets and extents " + i);
        }
        var top = RetroTextureSource.DecodeTop(source, 180);
        Check(top.Pixels.Length == 180, "lower mips validate without charging aggregate RGBA against top-only budget");
        Expect<InvalidDataException>(() => RetroTextureSource.Inspect(source, 179), "one byte below top output budget");
        foreach (int length in new[] { 76, 108, 140, 171 })
        {
            byte[] truncated = source[..length];
            Reject(truncated, "lower mip chain truncation at extent " + length);
        }
        foreach (uint format in new uint[] { 8, 10 })
        {
            var maximum = RetroTextureSource.Inspect(Texture(format, 1, 4096, 13), 16384);
            Check(maximum.MipCount == 13 && maximum.Mips[^1].Width == 1 && maximum.Mips[^1].Height == 1,
                "maximum dimension and 13-level non-square chain accepted for format " + format);
        }
    }

    private static void Padding()
    {
        foreach (uint format in new uint[] { 8, 10 })
        {
            byte[] exact = Texture(format, 1, 1);
            byte[] aligned = exact.Concat(Enumerable.Repeat((byte)0xff, 20)).ToArray();
            var layout = RetroTextureSource.Inspect(aligned);
            Check(layout.PayloadEnd == 44 && layout.PaddingBytes == 20 && aligned.Length == 64,
                "only next 32-byte-aligned uniform FF suffix accepted for format " + format);
            Check(RetroTextureSource.DecodeTop(aligned).Pixels.SequenceEqual(RetroTextureSource.DecodeTop(exact).Pixels),
                "alignment padding does not change top pixels");
            byte[] zeros = exact.Concat(new byte[20]).ToArray(); Reject(zeros, "zero alignment padding");
            byte[] mixed = (byte[])aligned.Clone(); mixed[^1] = 0; Reject(mixed, "mixed FF/zero alignment padding");
            Reject(exact.Concat(Enumerable.Repeat((byte)0xff, 19)).ToArray(), "short FF suffix");
            Reject(exact.Concat(Enumerable.Repeat((byte)0xff, 21)).ToArray(), "long FF suffix");
            Reject(aligned.Concat(Enumerable.Repeat((byte)0xff, 32)).ToArray(), "extra aligned FF block");
        }
    }

    private static void MalformedAndAllocation()
    {
        for (int length = 0; length < 12; length++) Reject(new byte[length], "short header " + length);
        foreach (uint format in new uint[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 12, uint.MaxValue })
        {
            byte[] unsupported = Texture(10, 1, 1); BinaryPrimitives.WriteUInt32BigEndian(unsupported, format);
            Expect<NotSupportedException>(() => RetroTextureSource.Inspect(unsupported), "unsupported Inspect format " + format);
            Expect<NotSupportedException>(() => RetroTextureSource.DecodeTop(unsupported), "unsupported DecodeTop format " + format);
        }
        foreach (var size in new[] { (0, 1), (1, 0), (4097, 1), (1, 4097), (65535, 65535) })
            Reject(Header(10, size.Item1, size.Item2, 1), "invalid dimensions " + size);
        foreach (uint count in new uint[] { 0, 14, uint.MaxValue })
        {
            byte[] invalid = Texture(10, 8, 8); BinaryPrimitives.WriteUInt32BigEndian(invalid.AsSpan(8), count);
            Reject(invalid, "invalid mip count " + count);
        }
        byte[] excessive = Texture(10, 1, 1); BinaryPrimitives.WriteUInt32BigEndian(excessive.AsSpan(8), 2);
        Reject(excessive, "mip count cannot repeat 1x1 beyond its natural chain");
        byte[] ordinary = Texture(10, 1, 1);
        foreach (int budget in new[] { -1, 0, RetroTextureSource.MaxOutputBytes + 1 })
        {
            Expect<ArgumentOutOfRangeException>(() => RetroTextureSource.Inspect(ordinary, budget), "invalid Inspect budget " + budget);
            Expect<ArgumentOutOfRangeException>(() => RetroTextureSource.DecodeTop(ordinary, budget), "invalid DecodeTop budget " + budget);
        }
        Expect<InvalidDataException>(() => RetroTextureSource.DecodeTop(ordinary, 3), "DecodeTop exact four-byte minimum budget");
        Reject(new byte[RetroTextureSource.MaxSourceBytes + 1], "source beyond 64MiB cap");
        byte[] hugeHeader = Header(10, 4096, 4096, 1);
        Expect<InvalidDataException>(() => RetroTextureSource.DecodeTop(hugeHeader), "warm malformed large-output request");
        long before = GC.GetAllocatedBytesForCurrentThread();
        Expect<InvalidDataException>(() => RetroTextureSource.DecodeTop(hugeHeader), "large top output rejected before allocation");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024 * 1024, "truncated 4096x4096 source does not allocate its 64MiB pixel output (actual bytes " + allocated + ")");
        Console.WriteLine($"TXTR SOURCE ALLOCATION PASS: truncated 4096x4096 request allocated {allocated} bytes, below 1MiB metadata/exception allowance; no 64MiB pixel allocation.");
    }

    private static void DetachedReadOnlyOutput()
    {
        byte[] source = Texture(8, 1, 1); Word(source, 12, 0xffff);
        var first = RetroTextureSource.DecodeTop(source); var second = RetroTextureSource.DecodeTop(source);
        source.AsSpan().Clear();
        Pixel(first, 0, 0, 255, 255, 255, 255, "decoded pixels detached from source bytes");
        Pixel(second, 0, 0, 255, 255, 255, 255, "independent decode retains its own snapshot");
        Check(first.Layout.Format == 8 && first.Layout.Width == 1 && first.Layout.Mips[0].Offset == 12,
            "layout metadata detached from later header mutation");
        byte[] copy = first.Pixels.ToArray(); copy[0] = 0;
        Pixel(first, 0, 0, 255, 255, 255, 255, "caller pixel copy cannot mutate decoded output");
        Check(typeof(RetroTextureSource.Decoded).GetProperty(nameof(RetroTextureSource.Decoded.Pixels))!.PropertyType == typeof(ReadOnlySpan<byte>),
            "public pixel view is ReadOnlySpan rather than mutable array");
        Check(first.Layout.Mips is IList<RetroTextureSource.Mip> { IsReadOnly: true }, "mip metadata collection is read-only");
        var list = (IList<RetroTextureSource.Mip>)first.Layout.Mips;
        Expect<NotSupportedException>(() => list[0] = new(9, 9, 9, 9), "mip list rejects replacement");
        Expect<NotSupportedException>(() => list.Add(new(1, 1, 1, 1)), "mip list rejects additions");
        Expect<NotSupportedException>(() => list.Clear(), "mip list rejects clearing");
        Check(list.Count == 1 && list[0].Width == 1, "rejected mutation preserves mip metadata");
    }

    private static byte[] Header(uint format, int width, int height, uint mips)
    {
        byte[] bytes = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, format);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), checked((ushort)width));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), checked((ushort)height));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), mips);
        return bytes;
    }
    private static byte[] Texture(uint format, int width, int height, int mips = 1)
    {
        int tile = format == 10 ? 8 : 4, payload = 0, w = width, h = height;
        for (int i = 0; i < mips; i++)
        { payload += ((w + tile - 1) / tile) * ((h + tile - 1) / tile) * 32; w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
        byte[] bytes = new byte[12 + payload]; Header(format, width, height, (uint)mips).CopyTo(bytes, 0); return bytes;
    }
    private static void Word(byte[] source, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(source.AsSpan(offset), value);
    private static void Block(byte[] source, int offset, ushort a, ushort b, byte row0, byte row1, byte row2, byte row3)
    { Word(source, offset, a); Word(source, offset + 2, b); source[offset + 4] = row0; source[offset + 5] = row1; source[offset + 6] = row2; source[offset + 7] = row3; }
    private static void SolidBlock(byte[] source, int offset, ushort value) => Block(source, offset, value, value, 0, 0, 0, 0);
    private static void Pixel(RetroTextureSource.Decoded decoded, int x, int y, int r, int g, int b, int a, string why)
    { var pixel = decoded.Pixels.Slice((y * decoded.Layout.Width + x) * 4, 4); Check(pixel[0] == r && pixel[1] == g && pixel[2] == b && pixel[3] == a, why); }
    private static void Reject(byte[] source, string why)
    { Expect<InvalidDataException>(() => RetroTextureSource.Inspect(source), "Inspect " + why); Expect<InvalidDataException>(() => RetroTextureSource.DecodeTop(source), "DecodeTop " + why); }
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("TXTR source: " + why); }
    private static void Expect<T>(Action action, string why) where T : Exception
    { checks++; try { action(); } catch (T) { return; } throw new InvalidOperationException("TXTR source expected " + typeof(T).Name + ": " + why); }
}
