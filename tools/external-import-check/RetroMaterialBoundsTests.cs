using System.Buffers.Binary;
using MphRead.Mods.Import.Retro;

// Literal invented material records only: no CMDL/TXTR/media copied from a game.
// This verifies the material slice boundary, not model admission or rendering.
internal static class RetroMaterialBoundsTests
{
    private static int checks;

    // Big-endian Echoes record, independently specified rather than generated
    // by the production serializer: 2 texture refs, one channel, 2 TEV records
    // followed by 2 binding rows, 2 texgens and an empty 4-byte UV block.
    private static byte[] Literal(bool echoes = true)
    {
        byte[] bytes = Convert.FromHexString(
            "0000008000000002000000000000000500000f0f112233445566778810203040" +
            "000400050000000100aabbcc0000000200020c4100041cc500000773000001a2" +
            "99112233000e77df00018820000001000000070088445566aabb0100ccddff01" +
            "0000000200065275000000010000000400000000");
        return echoes ? bytes : bytes[..20].Concat(bytes[28..]).ToArray();
    }

    internal static void Run()
    {
        checks = 0;
        KnownValues();
        SliceBoundaries();
        OptionalFieldsAndCounts();
        UvParameters();
        AllocationBound();
        Console.WriteLine($"RETRO MATERIAL BOUNDS PASS {checks} synthetic checks; bounded source records only, no source media/model/GPU authority.");
    }

    private static void KnownValues()
    {
        foreach (bool echoes in new[] { false, true })
        {
            byte[] bytes = Literal(echoes);
            var m = Cmdl.ReadMaterial(bytes, 0, bytes.Length, echoes);
            Check(bytes.Length == (echoes ? 116 : 108), "literal variant has independently known exact byte extent");
            Check(m.Flags == 0x80 && m.Tex.SequenceEqual(new[] { 0, 5 })
                && m.VertexAttributes == 0xf0f && m.Group == 0x10203040,
                "exact descriptor, texture references, flags and group in " + Variant(echoes));
            Check(echoes ? m.EchoesUnknown!.SequenceEqual(new uint[] { 0x11223344, 0x55667788 })
                : m.EchoesUnknown == null, "only Echoes consumes its two unknown words");
            Check(m.Konst.Length == 0 && m.IndTex == null && m.BlendDst == 4 && m.BlendSrc == 5
                && m.Channels.SequenceEqual(new uint[] { 0x00aabbcc }), "blend/channel values and omitted optional fields");
            Check(m.Stages.Length == 2 && m.Stages[0].ColorIn.SequenceEqual(new[] { 1, 2, 3, 4 })
                && m.Stages[0].AlphaIn.SequenceEqual(new[] { 5, 6, 7, 8 })
                && m.Stages[1].ColorIn.SequenceEqual(new[] { 31, 30, 29, 28 })
                && m.Stages[1].AlphaIn.SequenceEqual(new[] { 0, 1, 2, 3 }), "TEV packed input selectors preserve each stage");
            var first = m.Stages[0];
            Check(first.RawColorOp == 0x773 && first.RawAlphaOp == 0x1a2
                && first.ColorOp.Clamp == 1 && first.ColorOp.Op == 3 && first.ColorOp.Bias == 3
                && first.ColorOp.Scale == 1 && first.ColorOp.Out == 3
                && first.AlphaOp.Clamp == 1 && first.AlphaOp.Op == 2 && first.AlphaOp.Bias == 2
                && first.AlphaOp.Scale == 2 && first.AlphaOp.Out == 0, "TEV raw words and decoded operation bits stay exact");
            Check(first.KAlpha == 0x11 && first.KColor == 0x22 && first.Ras == 0x33
                && m.Stages[1].KAlpha == 0x44 && m.Stages[1].KColor == 0x55 && m.Stages[1].Ras == 0x66,
                "TEV byte selectors follow their own 20-byte records");
            Check(first.Tex == 1 && first.TexCoord == 0 && m.Stages[1].Tex == 255 && m.Stages[1].TexCoord == 1,
                "TEV bindings follow the entire record table and preserve no-texture sentinel255");
            Check(m.TexGens.Length == 2 && m.TexGens[0].Type == 5 && m.TexGens[0].Src == 7
                && m.TexGens[0].Mtx == 9 && m.TexGens[0].Normalize && m.TexGens[0].Post == 12
                && m.TexGens[1].Type == 1 && m.TexGens[1].Src == 0 && !m.TexGens[1].Normalize,
                "texgen bit fields retain source coordinate and matrix metadata");
            Check(m.UvAnims.Length == 0 && m.TexIds.Length == 0, "empty UV block and unresolved texture IDs remain empty");
            Array.Fill(bytes, (byte)0);
            Check(m.Tex[1] == 5 && m.Stages[0].ColorIn[0] == 1 && m.Channels[0] == 0x00aabbcc,
                "parsed fields detach from caller source bytes");
            m.Stages[0].ColorIn[0] = 30;
            byte[] fresh = Literal(echoes);
            Check(Cmdl.ReadMaterial(fresh, 0, fresh.Length, echoes).Stages[0].ColorIn[0] == 1,
                "mutable returned fields are privately owned by each parse");
        }
    }

    private static void SliceBoundaries()
    {
        foreach (bool echoes in new[] { false, true })
        {
            byte[] bytes = Literal(echoes);
            // Every missing byte must fail against end, even though the same
            // backing array still contains a complete following record.
            for (int end = 0; end < bytes.Length; end++)
                Reject(() => Cmdl.ReadMaterial(bytes, 0, end, echoes), "declared end cuts " + Variant(echoes) + " at byte " + end);
            byte[] framed = Enumerable.Repeat((byte)0xd7, 11).Concat(bytes)
                .Concat(Enumerable.Repeat((byte)0xe9, 32)).ToArray();
            var m = Cmdl.ReadMaterial(framed, 11, 11 + bytes.Length, echoes);
            Check(m.Tex.SequenceEqual(new[] { 0, 5 }) && m.Group == 0x10203040,
                "nonzero start and following data do not change slice interpretation");
            Reject(() => Cmdl.ReadMaterial(framed, 11, 11 + bytes.Length - 1, echoes), "following data cannot satisfy truncated final field");
            byte[] trailer = bytes.Concat(new byte[] { 0xfa, 0xce, 0xb0, 0x0c, 0x55 }).ToArray();
            Check(Cmdl.ReadMaterial(trailer, 0, trailer.Length, echoes).TexGens.Length == 2,
                "opaque outer trailer remains permitted by historical material policy");
            foreach (var range in new[] { (-1, bytes.Length), (0, -1), (2, 1), (bytes.Length, bytes.Length),
                (bytes.Length + 1, bytes.Length), (0, bytes.Length + 1), (int.MaxValue, int.MaxValue),
                (0, int.MaxValue), (int.MinValue, int.MaxValue) })
                Reject(() => Cmdl.ReadMaterial(bytes, range.Item1, range.Item2, echoes), "invalid/overflowing material range " + range);
        }
        byte[] valid = Literal();
        byte[] atLimit = new byte[1024 * 1024]; valid.CopyTo(atLimit, 0);
        Check(Cmdl.ReadMaterial(atLimit, 0, atLimit.Length, true).Group == 0x10203040,
            "exact1MiB material including an opaque trailer remains admitted");
        byte[] tooLarge = new byte[1024 * 1024 + 1]; valid.CopyTo(tooLarge, 0);
        Reject(() => Cmdl.ReadMaterial(tooLarge, 0, tooLarge.Length, true), "material byte budget rejects valid-looking oversized slice");
        Check(Cmdl.ReadMaterial(tooLarge, 0, valid.Length, true).Tex.Length == 2,
            "byte budget belongs to the declared slice rather than unrelated bytes in its containing CMDL");
    }

    private static void OptionalFieldsAndCounts()
    {
        foreach (bool echoes in new[] { false, true })
        {
            byte[] all = Make(echoes, textures: 8, konst: 4, channels: 2, stages: 16, texgens: 8,
                animations: Enumerable.Repeat((0u, Array.Empty<float>()), 8).ToArray());
            var m = Cmdl.ReadMaterial(all, 0, all.Length, echoes);
            Check(m.Tex.Length == 8 && m.Konst.Length == 4 && m.Channels.Length == 2
                && m.Stages.Length == 16 && m.TexGens.Length == 8 && m.UvAnims.Length == 8,
                "all independent hardware-compatible count limits are admitted together");
            Check(m.IndTex == 0x12345678 && m.Konst[0].SequenceEqual(new byte[] { 1, 2, 3, 4 })
                && m.Konst[3].SequenceEqual(new byte[] { 1, 2, 3, 4 }), "optional indirect reference and raw RGBA constants preserve exact bytes");
            // Full in-range payloads prove these refusals are count bounds,
            // rather than incidental truncation of the small literal fixture.
            var excessive = new[] {
                Make(echoes, textures: 9), Make(echoes, konst: 5), Make(echoes, channels: 3),
                Make(echoes, stages: 17), Make(echoes, texgens: 9),
                Make(echoes, animations: Enumerable.Repeat((0u, Array.Empty<float>()), 9).ToArray()) };
            for (int i = 0; i < excessive.Length; i++)
            { byte[] bad = excessive[i]; Reject(() => Cmdl.ReadMaterial(bad, 0, bad.Length, echoes), "fully present count exceeds component limit " + i); }
            // Traverse every optional field byte with a complete backing array.
            byte[] optional = Make(echoes, konst: 1);
            for (int end = 0; end < optional.Length; end++)
                Reject(() => Cmdl.ReadMaterial(optional, 0, end, echoes), "optional record cannot read beyond end " + end);
        }
        foreach (int offset in new[] { 4, 36, 44, 96 })
        foreach (uint count in new uint[] { 0x7fffffff, 0x80000000, 0xffffffff })
        {
            byte[] bad = Literal(); Put(bad, offset, count);
            Reject(() => Cmdl.ReadMaterial(bad, 0, bad.Length, true), "oversized declared count at " + offset);
        }
        byte[] largeIndex = Literal(); Put(largeIndex, 8, uint.MaxValue);
        Reject(() => Cmdl.ReadMaterial(largeIndex, 0, largeIndex.Length, true), "texture reference cannot overflow signed downstream index");
        foreach (uint count in new uint[] { 0x7fffffff, 0x80000000, 0xffffffff })
        {
            byte[] bad = Make(true, konst: 1); Put(bad, 32, count);
            Reject(() => Cmdl.ReadMaterial(bad, 0, bad.Length, true), "optional Konst count bounded before allocation");
        }
    }

    private static void UvParameters()
    {
        var modes = new (uint Mode, int Count)[] { (0, 0), (1, 0), (2, 4), (3, 2), (4, 4), (5, 4), (6, 0), (7, 2), (8, 9), (0xfeedbeef, 0) };
        foreach (var mode in modes)
        {
            float[] parameters = Enumerable.Range(0, mode.Count).Select(i => (i - 3) * .25f).ToArray();
            byte[] bytes = Make(true, animations: new[] { (mode.Mode, parameters) });
            var material = Cmdl.ReadMaterial(bytes, 0, bytes.Length, true);
            Check(material.UvAnims.Length == 1 && material.UvAnims[0].Mode == mode.Mode
                && material.UvAnims[0].Params.SequenceEqual(parameters.Select(v => (double)v)),
                "UV mode preserves exact finite parameter arity/value, including unexecuted zero-param modes: " + mode.Mode);
            // The UV header is the literal's final8 bytes; Make otherwise uses
            // the same fixed fixture layout, except it also adds IndTex4.
            int sizeOffset = bytes.Length - 8 - 4 - 4 * mode.Count;
            for (int end = sizeOffset; end < bytes.Length; end++)
                Reject(() => Cmdl.ReadMaterial(bytes, 0, end, true), "UV mode/parameter bytes cannot borrow following data " + mode.Mode + "/" + end);
            uint blockSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizeOffset));
            foreach (uint declared in new uint[] { 0, 1, 2, 3, blockSize - 1, blockSize + 1, uint.MaxValue })
            {
                byte[] bad = (byte[])bytes.Clone(); Put(bad, sizeOffset, declared);
                Reject(() => Cmdl.ReadMaterial(bad, 0, bad.Length, true), "partial/overflowing UV block span for mode " + mode.Mode);
            }
            byte[] hugeCount = (byte[])bytes.Clone(); Put(hugeCount, sizeOffset + 4, uint.MaxValue);
            Reject(() => Cmdl.ReadMaterial(hugeCount, 0, hugeCount.Length, true), "UV declared count bounded before array allocation");
            for (int i = 0; i < mode.Count; i++)
            foreach (float nonfinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                byte[] bad = (byte[])bytes.Clone(); Put(bad, sizeOffset + 12 + 4 * i, BitConverter.SingleToUInt32Bits(nonfinite));
                Reject(() => Cmdl.ReadMaterial(bad, 0, bad.Length, true), "nonfinite UV parameter is refused at slot " + i);
            }
        }
        // Outer end contains a legitimate following record, but the declared
        // animation block ends before the required mode2 parameter array.
        byte[] shortBlock = Make(true, animations: new[] { (2u, new[] { 1f, 2f, 3f, 4f }) });
        Put(shortBlock, shortBlock.Length - 28, 8);
        Reject(() => Cmdl.ReadMaterial(shortBlock, 0, shortBlock.Length, true), "inner UV block end fences parameter reads independently of outer end");
    }

    private static void AllocationBound()
    {
        byte[] tiny = new byte[8]; Put(tiny, 4, uint.MaxValue);
        for (int i = 0; i < 8; i++) Reject(() => Cmdl.ReadMaterial(tiny, 0, tiny.Length, true), "warm huge count refusal");
        long before = GC.GetAllocatedBytesForCurrentThread();
        Reject(() => Cmdl.ReadMaterial(tiny, 0, tiny.Length, true), "tiny source refuses huge count before large arrays");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024 * 1024, "huge texture count allocates less than1MiB warmed refusal allowance");
        byte[] onlyFlags = { 0xff, 0xff, 0xff, 0xff };
        Reject(() => Cmdl.ReadMaterial(onlyFlags, 0, 4, true), "four-byte fake material cannot reach absent count arrays");
        Console.WriteLine($"RETRO MATERIAL ALLOCATION PASS: huge count in8-byte input allocated {allocated} bytes; no declared-count arrays admitted.");
    }

    private static byte[] Make(bool echoes, int textures = 2, int konst = -1, int channels = 1,
        int stages = 2, int texgens = 2, (uint Mode, float[] Params)[]? animations = null)
    {
        animations ??= Array.Empty<(uint, float[])>();
        var output = new List<byte>();
        void Word(uint v) { byte[] b = new byte[4]; Put(b, 0, v); output.AddRange(b); }
        Word(0x80 | Cmdl.IndStage | (konst >= 0 ? Cmdl.Konst : 0));
        Word((uint)textures); for (int i = 0; i < textures; i++) Word((uint)i);
        Word(0xf0f); if (echoes) { Word(0x11223344); Word(0x55667788); }
        Word(0x10203040);
        if (konst >= 0) { Word((uint)konst); for (int i = 0; i < konst; i++) Word(0x01020304); }
        Word(0x00040005); Word(0x12345678);
        Word((uint)channels); for (int i = 0; i < channels; i++) Word(0x00aabbcc);
        Word((uint)stages);
        for (int i = 0; i < stages; i++) output.AddRange(Literal().AsSpan(48, 20).ToArray());
        for (int i = 0; i < stages; i++) Word(0xaabb0100);
        Word((uint)texgens); for (int i = 0; i < texgens; i++) Word(0x65275);
        Word((uint)(4 + animations.Sum(a => 4 + a.Params.Length * 4))); Word((uint)animations.Length);
        foreach (var anim in animations) { Word(anim.Mode); foreach (float p in anim.Params) Word(BitConverter.SingleToUInt32Bits(p)); }
        return output.ToArray();
    }

    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static string Variant(bool echoes) => echoes ? "Echoes" : "Prime1";
    private static void Check(bool value, string why)
    { checks++; if (!value) throw new InvalidOperationException("Retro material bounds: " + why); }
    private static void Reject(Action action, string why)
    {
        checks++;
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Retro material expected InvalidDataException: " + why);
    }
}
