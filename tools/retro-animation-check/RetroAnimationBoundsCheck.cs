using System.Buffers.Binary;
using MphRead.Mods.Import.Retro;

// Invented, complete ANIM resources. No extracted animation or source fixture.
internal static class RetroAnimationBoundsCheck
{
    private static int _checks;

    internal static void Run()
    {
        foreach (bool echoes in new[] { false, true })
        {
            byte[] plain = Uncompressed(echoes);
            Anim decoded = Valid(plain, echoes);
            Check(decoded.Keys == 3 && decoded.Duration == 1 && decoded.Tick == .5,
                "uncompressed literal timing");
            Check(decoded.Rot.Keys.Order().SequenceEqual(new uint[] { 0, 2 })
                && decoded.Trans[0][2].SequenceEqual(new double[] { 12, 22, 32 })
                && decoded.Trans[2][1].SequenceEqual(new double[] { 41, 51, 61 }),
                "uncompressed sparse bone routing and complete keys");
            Check(decoded.Rot[0][0].SequenceEqual(new double[] { 0, 0, 0, 1 }),
                "source WXYZ quaternion becomes XYZW");
            if (echoes)
                Check(decoded.Scale.Count == 2 && decoded.Scale[0][2].SequenceEqual(new double[] { 3, 4, 5 })
                    && decoded.Scale[2][0].SequenceEqual(new double[] { 4, 5, 6 }),
                    "all Echoes scale channels and keys survive admission");
            else Check(decoded.Scale.Count == 0, "Prime has no invented scale channel");

            byte[] packed = Compressed(echoes);
            Anim compressed = Valid(packed, echoes);
            Check(compressed.Trans[7][0][0] == 1 && compressed.Trans[7][1][0] == 1.5
                && compressed.Trans[7][2][0] == 2, "compressed skipped middle key uses neighboring stored keys");
            Check(compressed.Rot[7][0].SequenceEqual(new double[] { 0, 0, 0, 1 })
                && Math.Abs(compressed.Rot[7][2][2] - Math.Sin(Math.PI / 32768)) < 1e-15,
                "compressed rotation seed and literal signed delta");
            if (echoes)
                Check(compressed.Scale[7][0][1] == .75 && compressed.Scale[7][1][1] == .625
                    && compressed.Scale[7][2][1] == .5, "compressed full scale and signed negative delta");
            Anim wide = Valid(Compressed(echoes, wide: true), echoes);
            Check(wide.Trans[7][2][0] == 1.5, "signed 32-bit delta crosses a word boundary and wraps to S16");
            TinyTick(echoes);
            QuaternionAdmission(echoes);
            foreach (uint keys in new uint[] { 1, 31, 32, 33 })
            {
                var empty = Header(echoes, keys, 0);
                for (uint word = 0; word < (keys + 31) / 32; word++) empty.U32(0);
                empty.U32(0); if (!echoes) empty.U32(0);
                Check(Valid(empty.Array(), echoes).Keys == keys, "flag table rounds to complete big-endian words");
            }

            foreach (byte[] valid in new[] { plain, packed })
                for (int length = 0; length < valid.Length; length++)
                    Refuse(valid.AsSpan(0, length).ToArray(), echoes, "every truncated encoded prefix");

            int duration = echoes ? 10 : 16, tick = duration + 4;
            int divisor = echoes ? 26 : 32, factor = divisor + 4;
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                BadFloat(packed, echoes, duration, invalid, "compressed duration finite");
                BadFloat(packed, echoes, tick, invalid, "compressed tick finite");
                BadFloat(packed, echoes, factor, invalid, "compressed translation factor finite");
                if (echoes) BadFloat(packed, echoes, factor + 4, invalid, "compressed scale factor finite");
                BadFloat(plain, echoes, 4, invalid, "uncompressed duration finite");
                BadFloat(plain, echoes, 12, invalid, "uncompressed tick finite");
                BadFloat(plain, echoes, plain.Length - 4, invalid, "uncompressed final full key finite");
            }
            foreach (float invalid in new[] { 0f, -1f })
            {
                BadFloat(packed, echoes, tick, invalid, "compressed nonpositive tick");
                BadFloat(plain, echoes, 12, invalid, "uncompressed nonpositive tick");
                BadFloat(packed, echoes, duration, invalid, "compressed nonpositive duration");
                BadFloat(plain, echoes, 4, invalid, "uncompressed nonpositive duration");
            }
            BadFloat(plain, echoes, 4, 121, "duration budget");
            BadFloat(plain, echoes, plain.Length - 4, 100001, "decoded scalar magnitude bound");

            int channelCount = echoes ? 38 : 40, keyCount = echoes ? 46 : 48;
            foreach (uint count in new uint[] { 0, Anim.MaximumKeys + 1, int.MaxValue, uint.MaxValue })
            {
                Refuse(Patch(packed, keyCount, count), echoes, "compressed key count");
                Refuse(Patch(plain, 20, count), echoes, "uncompressed key count");
            }
            foreach (uint count in new uint[] { Anim.MaximumChannels + 1, int.MaxValue, uint.MaxValue })
            {
                Refuse(Patch(packed, channelCount, count), echoes, "compressed channel count");
                Refuse(Patch(plain, 28, count), echoes, "uncompressed bone count");
            }
            Refuse(Patch(packed, divisor, 0), echoes, "zero active rotation divisor");
            byte[] invalidBits = (byte[])packed.Clone();
            // Header, rounded flag word, opaque trailer, bone, channel count, signed seed.
            int firstWidth = (echoes ? 50 + 4 + 4 + 1 : 52 + 4 + 8 + 4) + 2 + 2;
            foreach (byte width in new byte[] { 33, 64, 255 })
            { invalidBits[firstWidth] = width; Refuse(invalidBits, echoes, "delta width exceeds supported 32 bits"); }

            byte[] duplicate = Compressed(echoes, duplicate: true);
            Refuse(duplicate, echoes, "duplicate source bone cannot overwrite and append another channel");
            byte[] badIndex = (byte[])plain.Clone();
            // Prime uses bone-map values as rotation indices; Echoes has its own table.
            badIndex[echoes ? 39 : 32] = 2;
            Refuse(badIndex, echoes, "index must name an encoded channel");
            Refuse(Patch(plain, 35, uint.MaxValue), echoes, "declared index byte span cannot truncate silently");
            Refuse(ScalarBomb(echoes, compressed: true), echoes, "compressed scalar product before decode", "scalar");
            Refuse(ScalarBomb(echoes, compressed: false), echoes, "uncompressed scalar product before payload", "scalar");

            // Successful admission owns decoded values; changing caller bytes cannot retarget them.
            double[] captured = decoded.Trans[0][0].ToArray();
            Array.Fill(plain, (byte)255);
            Check(decoded.Trans[0][0].SequenceEqual(captured), "decoded full channels detached from source bytes");
        }
        Refuse(new byte[] { 0, 0, 0, 3 }, false, "unknown compression type");
        Refuse(new byte[Anim.MaximumResourceBytes + 1], false, "resource byte budget");
        AllocationFence();
        Console.WriteLine($"PASS: bounded ANIM literal fixtures and malformed resource admission ({_checks} checks)");
    }

    private static Anim Valid(byte[] bytes, bool echoes)
    {
        Anim bounded = Anim.LoadBounded(bytes, echoes), legacy = Anim.Load(bytes, echoes);
        Check(bounded.Keys == legacy.Keys && bounded.Tick == legacy.Tick && bounded.Duration == legacy.Duration,
            "bounded decoder retains original timing");
        foreach (var pair in new[] { (bounded.Rot, legacy.Rot), (bounded.Trans, legacy.Trans), (bounded.Scale, legacy.Scale) })
            Check(pair.Item1.Keys.Order().SequenceEqual(pair.Item2.Keys.Order())
                && pair.Item1.All(kv => kv.Value.Count == pair.Item2[kv.Key].Count
                    && kv.Value.Zip(pair.Item2[kv.Key]).All(keys => keys.First.SequenceEqual(keys.Second))),
                "bounded full decode equals unchanged raw loader");
        return bounded;
    }

    private static void TinyTick(bool echoes)
    {
        byte[] source=Patch(Uncompressed(echoes),12,1); // exact binary32 float.Epsilon, positive and finite
        Anim decoded=Valid(source,echoes);
        Check(decoded.Tick==(double)float.Epsilon,"smallest positive finite source tick remains an admitted exact fact");
        decoded.Sample(0,out var firstRot,out var first);
        decoded.Sample((double)float.Epsilon/2,out var halfRot,out var half);
        Check(first[0].SequenceEqual(new double[]{10,20,30})&&half[0].SequenceEqual(new double[]{10.5,20.5,30.5})
            &&firstRot[0].SequenceEqual(new double[]{0,0,0,1})&&halfRot[0].All(double.IsFinite),
            "subnormal tick still samples a finite interior half-key before any endpoint clamp");
        foreach(double time in new[]{(double)float.Epsilon*2,1e-6,.25,1,double.MaxValue})
        {
            decoded.Sample(time,out var rotations,out var translations);
            Check(translations[0].SequenceEqual(new double[]{12,22,32})&&translations[2].SequenceEqual(new double[]{42,52,62})
                &&rotations.Values.All(q=>q.All(double.IsFinite)),
                "huge finite frame quotient clamps to final key before narrowing to an integer index");
        }
    }
    private static void QuaternionAdmission(bool echoes)
    {
        byte[] source=Uncompressed(echoes);
        // The literal has six rotation keys followed by a count word and six
        // translation keys; locating from that independent fixed tail avoids
        // depending on the different Prime/Echoes index-table prefixes.
        int rotation=source.Length-6*3*4-4-6*4*4;
        foreach(int key in new[]{0,5})foreach(float w in new[]{0f,2f})
        {
            byte[] invalid=Patch(source,rotation+key*16,unchecked((uint)BitConverter.SingleToInt32Bits(w)));
            Refuse(invalid,echoes,"zero or nonunit quaternion on first or last full key","quaternion");
        }
        foreach(float squareNorm in new[]{.9995f,1.0005f})
        {
            float w=MathF.Sqrt(squareNorm);byte[] quantized=Patch(source,rotation,unchecked((uint)BitConverter.SingleToInt32Bits(w)));
            Check(Valid(quantized,echoes).Rot[0][0][3]==(double)w,
                "small source quaternion quantization error is accepted without rewriting or normalizing source keys");
        }
        foreach(float squareNorm in new[]{.998f,1.002f})
            BadFloat(source,echoes,rotation,MathF.Sqrt(squareNorm),"quaternion norm outside explicit source quantization tolerance");
        byte[] compressed=Compressed(echoes);int seed=echoes?61:70;
        for(int axis=0;axis<3;axis++)BinaryPrimitives.WriteInt16BigEndian(compressed.AsSpan(seed+axis*3,2),16384);
        Refuse(compressed,echoes,"compressed seed with three unit sine components cannot yield a unit quaternion","quaternion");
    }

    private static byte[] Uncompressed(bool echoes)
    {
        var w = new Writer(); w.U32(0); w.F32(1); w.U32(0); w.F32(.5f); w.U32(0);
        w.U32(3); w.U32(0); w.U32(3); w.Bytes(0, 255, 1);
        if (echoes) { w.U32(2); w.Bytes(0, 1); }
        w.U32(2); w.Bytes(0, 1);
        if (echoes)
        {
            w.U32(2); w.Bytes(0, 1); w.U32(6);
            for (int channel = 0; channel < 2; channel++)
                for (int key = 0; key < 3; key++)
                    w.Vec(1 + channel * 3 + key, 2 + channel * 3 + key, 3 + channel * 3 + key);
        }
        w.U32(6);
        for (int channel = 0; channel < 2; channel++)
            for (int key = 0; key < 3; key++) { w.F32(1); w.F32(0); w.F32(0); w.F32(0); }
        w.U32(6);
        for (int channel = 0; channel < 2; channel++)
            for (int key = 0; key < 3; key++)
                w.Vec(10 + channel * 30 + key, 20 + channel * 30 + key, 30 + channel * 30 + key);
        return w.Array();
    }

    private static byte[] Compressed(bool echoes, bool wide = false, bool duplicate = false)
    {
        var w = Header(echoes, 3, duplicate ? 2u : 1u); w.U32(5); // flags: keys 0 and 2 stored, key 1 interpolated
        w.U32(0); if (!echoes) w.U32(0);
        for (int channel = 0; channel < (duplicate ? 2 : 1); channel++)
        {
            if (echoes) w.Bytes(7); else w.U32(7);
            w.Channel(0, 0, 0, 0, 0, 2);
            w.Channel(2, 4, 6, (byte)(wide ? 32 : 3), 0, 0);
            if (echoes) w.Channel(2, 3, 4, 0, 2, 0);
        }
        // Independently packed low-bit-first stream, big-endian 32-bit storage.
        var bits = new List<bool>();
        void Put(uint value, int width) { for (int i = 0; i < width; i++) bits.Add((value & (1u << i)) != 0); }
        Put(0, 1); Put(1, 2); Put(wide ? unchecked((uint)-65535) : 2, wide ? 32 : 3);
        if (echoes) Put(3, 2); // signed two-bit -1
        for (int at = 0; at < bits.Count; at += 32)
        { uint word = 0; for (int bit = 0; bit < 32 && at + bit < bits.Count; bit++) if (bits[at + bit]) word |= 1u << bit; w.U32(word); }
        return w.Array();
    }

    private static Writer Header(bool echoes, uint keys, uint channels)
    {
        var w = new Writer(); w.U32(2); w.U32(0);
        if (echoes) w.U16(0); else { w.U32(0); w.U32(0); }
        w.F32(1); w.F32(.5f); w.U32(0); w.U32(0); w.U32(16384); w.F32(.5f);
        if (echoes) w.F32(.25f);
        w.U32(channels); w.U32(0); w.U32(keys); return w;
    }

    private static byte[] ScalarBomb(bool echoes, bool compressed)
    {
        // Each individually bounded field is valid, but the decoded product exceeds 4M scalars.
        const int channels = 40, keys = 20000;
        if (compressed)
        {
            var w = Header(echoes, keys, channels);
            for (int i = 0; i < (keys + 31) / 32; i++) w.U32(0);
            w.U32(0); if (!echoes) w.U32(0);
            for (int i = 0; i < channels; i++)
            { if (echoes) w.Bytes((byte)i); else w.U32((uint)i); w.Channel(0, 0, 0, 0, 0, 0); w.Channel(0, 0, 0, 0, 0, 0); if (echoes) w.Channel(0, 0, 0, 0, 0, 0); }
            return w.Array();
        }
        var u = new Writer(); u.U32(0); u.F32(1); u.U32(0); u.F32(.5f); u.U32(0); u.U32(keys); u.U32(0);
        u.U32(channels); for (int i = 0; i < channels; i++) u.Bytes((byte)i);
        if (echoes) { u.U32(channels); for (int i = 0; i < channels; i++) u.Bytes((byte)i); }
        u.U32(channels); for (int i = 0; i < channels; i++) u.Bytes((byte)i);
        if (echoes) { u.U32(channels); for (int i = 0; i < channels; i++) u.Bytes((byte)i); }
        return u.Array();
    }

    private static void AllocationFence()
    {
        byte[] bomb = Patch(Compressed(true), 46, int.MaxValue);
        for (int i = 0; i < 4; i++) Refuse(bomb, true, "warm count refusal");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++) Refuse(bomb, true, "count refusal before bool/key allocation");
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 1024 * 1024,
            "small malformed count inputs do not allocate declared key arrays");
    }
    private static byte[] Patch(byte[] source, int offset, uint value)
    { byte[] copy = (byte[])source.Clone(); BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(offset, 4), value); return copy; }
    private static void BadFloat(byte[] source, bool echoes, int offset, float value, string why)
        => Refuse(Patch(source, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value))), echoes, why);
    private static void Refuse(byte[] source, bool echoes, string why, string? message = null)
    {
        try { Anim.LoadBounded(source, echoes); }
        catch (InvalidDataException e) { Check(message == null || e.Message.Contains(message, StringComparison.OrdinalIgnoreCase), why); return; }
        throw new InvalidOperationException("Expected bounded ANIM refusal: " + why);
    }
    private static void Check(bool value, string why)
    { if (!value) throw new InvalidOperationException(why); _checks++; }
    private sealed class Writer
    {
        private readonly MemoryStream _bytes = new();
        internal void U32(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); _bytes.Write(b); }
        internal void U16(ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, value); _bytes.Write(b); }
        internal void F32(float value) => U32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        internal void Vec(float x, float y, float z) { F32(x); F32(y); F32(z); }
        internal void Bytes(params byte[] values) => _bytes.Write(values);
        internal void Channel(short x, short y, short z, byte bx, byte by, byte bz)
        { U16(3); U16(unchecked((ushort)x)); Bytes(bx); U16(unchecked((ushort)y)); Bytes(by); U16(unchecked((ushort)z)); Bytes(bz); }
        internal byte[] Array() => _bytes.ToArray();
    }
}
