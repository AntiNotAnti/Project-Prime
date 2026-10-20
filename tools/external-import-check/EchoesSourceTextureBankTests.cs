using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.MapGen;

// Own-generated TXTR/RGBA fixtures only. This suite creates no files or images
// and does not qualify source media, materials, samplers, rendering or gameplay.
internal static class EchoesSourceTextureBankTests
{
    private static int checks;
    private static readonly EchoesSourceTextureBank.Source Area = new(0x02690de1, new string('b', 64));

    internal static void Run()
    {
        checks = 0;
        Check(EchoesSourceTextureBank.HeaderBytes == 160 && EchoesSourceTextureBank.EntryBytes == 112
            && EchoesSourceTextureBank.MaxEntries == 512 && EchoesSourceTextureBank.MaxImageBytes == 2 * 1024 * 1024
            && EchoesSourceTextureBank.MaxBankBytes == 64 * 1024 * 1024, "finite versioned bank limits");
        DeterministicLayoutAndOwnership();
        HeaderRefusal();
        DirectoryRefusal();
        SelectedIntegrityAndGeneration();
        EncoderBounds();
        BoundedStreams();
        Console.WriteLine($"SOURCE TEXTURE BANK PASS {checks} synthetic checks; bounded private bytes/metadata only, no media/files/material/GPU authority.");
    }

    private static void DeterministicLayoutAndOwnership()
    {
        var a = Tiny(0x20, 8); var b = Tiny(0x90, 10);
        var inputs = new List<EchoesSourceTextureBank.Input> { b, a };
        byte[] bytes = EchoesSourceTextureBank.Encode(Area, inputs);
        Check(inputs[0] == b && inputs[1] == a, "encoding never sorts caller inventory in place");
        Check(bytes.SequenceEqual(EchoesSourceTextureBank.Encode(Area, inputs.AsEnumerable().Reverse().ToArray())),
            "unsorted/reversed inputs encode deterministically by unsigned resource identity");
        Check(bytes.Length == 396 && Encoding.ASCII.GetString(bytes, 0, 4) == "PPSB"
            && U32(bytes, 4) == 1 && U32(bytes, 8) == 160 && U32(bytes, 12) == 2
            && U32(bytes, 16) == 112 && U32(bytes, 20) == 384 && U32(bytes, 24) == 396,
            "literal little-endian version 1 header and exact payload extent");
        Check(bytes.AsSpan(28, 32).SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes("gx-expanded-cmpr-3-5-rgb5a3-v1")))
            && bytes.AsSpan(60, 6).SequenceEqual("G2ME01"u8) && bytes[66] == 0 && bytes[67] == 0
            && bytes.AsSpan(68, 20).SequenceEqual(Convert.FromHexString("b8b92af37dc772ecd8234fb1ce664be0493188f6"))
            && U32(bytes, 88) == 0x02690de1 && bytes.AsSpan(92, 32).SequenceEqual(Convert.FromHexString(Area.MreaSha256)),
            "header binds exact pixel contract, pinned edition/archive and supplied MREA fingerprint");
        Check(U32(bytes, 160) == 0x20 && U32(bytes, 164) == 8 && U32(bytes, 168) == 2
            && U32(bytes, 172) == 1 && U32(bytes, 176) == 1 && U32(bytes, 180) == 0 && U32(bytes, 184) == 8
            && U32(bytes, 272) == 0x90 && U32(bytes, 276) == 10 && U32(bytes, 292) == 8 && U32(bytes, 296) == 4,
            "literal sorted row fields use contiguous relative pixel offsets");
        Check(bytes.AsSpan(124, 32).SequenceEqual(SHA256.HashData(bytes.AsSpan(160, 224)))
            && bytes.AsSpan(156, 4).IndexOfAnyExcept((byte)0) == -1
            && bytes.AsSpan(188, 4).IndexOfAnyExcept((byte)0) == -1 && bytes.AsSpan(256, 16).IndexOfAnyExcept((byte)0) == -1,
            "directory hash and reserved header/row flags encoded exactly");
        byte[] redGreen = { 255, 0, 0, 255, 0, 255, 0, 255 }; byte[] blue = { 0, 0, 255, 255 };
        Check(bytes.AsSpan(384, 8).SequenceEqual(redGreen) && bytes.AsSpan(392, 4).SequenceEqual(blue),
            "bank stores exact top-first straight RGBA pixels, not TXTR bytes");
        using var stream = new MemoryStream(bytes, writable: true); stream.Position = 7;
        var bank = EchoesSourceTextureBank.Inspect(stream);
        Check(stream.CanRead && bank.Length == 396 && bank.PayloadOffset == 384 && bank.Source == Area
            && bank.Sha256 == Hash(bytes) && bank.HeaderSha256 == Hash(bytes.AsSpan(0, 160))
            && bank.DirectorySha256 == Hash(bytes.AsSpan(160, 224)), "inspection computes detached whole/header/directory hashes and leaves stream open");
        Check(bank.Entries.Select(e => e.ResourceId).SequenceEqual(new uint[] { 0x20, 0x90 })
            && bank.RequireEntry(0x20).SourceSha256 == a.SourceSha256
            && bank.RequireEntry(0x20).RgbaSha256 == Hash(redGreen), "immutable metadata retains canonical source/pixel fingerprints");
        Check(bank.Entries is IList<EchoesSourceTextureBank.Entry> { IsReadOnly: true }, "entry metadata is read-only through collection interface");
        var list = (IList<EchoesSourceTextureBank.Entry>)bank.Entries;
        Expect<NotSupportedException>(() => list[0] = list[0] with { ResourceId = 1 }, "entry replacement refused");
        Expect<NotSupportedException>(() => list.Clear(), "entry clearing refused");
        Expect<NotSupportedException>(() => list.Add(list[0]), "entry additions refused");
        Expect<InvalidDataException>(() => bank.RequireEntry(0x30), "absent resource is not substituted");
        inputs.Clear();
        byte[] first = EchoesSourceTextureBank.ReadImage(stream, bank, 0x20);
        Check(first.SequenceEqual(redGreen) && stream.CanRead, "selected image validates and caller stream remains open");
        first[0] = 0;
        Check(EchoesSourceTextureBank.ReadImage(stream, bank, 0x20).SequenceEqual(redGreen), "each selected read returns fresh detached bytes");
        var upper = Area with { MreaSha256 = Area.MreaSha256.ToUpperInvariant() };
        Check(EchoesSourceTextureBank.Encode(upper, new[] { a with { SourceSha256 = a.SourceSha256.ToUpperInvariant() }, b }).SequenceEqual(bytes),
            "hexadecimal fingerprint casing cannot change binary representation");
        var unsigned = Inspect(EchoesSourceTextureBank.Encode(Area, new[] { a with { ResourceId = uint.MaxValue }, a with { ResourceId = 1 }, a with { ResourceId = 0x80000000 } }));
        Check(unsigned.Entries.Select(e => e.ResourceId).SequenceEqual(new uint[] { 1, 0x80000000, uint.MaxValue }), "resource sorting is unsigned across high-bit IDs");
        bytes[92] ^= 1;
        Check(bank.Source == Area && bank.Entries[0].ResourceId == 0x20 && bank.Sha256 != Hash(bytes), "caller bank bytes cannot mutate captured metadata or hash");
    }

    private static void HeaderRefusal()
    {
        byte[] good = Good();
        foreach (var change in new (int Offset, uint Value)[] { (4, 0), (4, 2), (8, 159), (12, 0), (12, 513),
            (12, uint.MaxValue), (16, 111), (20, 385), (20, uint.MaxValue), (24, uint.MaxValue), (88, 0) })
        { byte[] bad = (byte[])good.Clone(); Put(bad, change.Offset, change.Value); Reject(bad, "header scalar " + change); }
        foreach (int offset in new[] { 0, 28, 60, 65, 66, 67, 68, 87, 124, 155, 156, 159 })
        { byte[] bad = (byte[])good.Clone(); bad[offset] ^= 1; Reject(bad, "header identity/hash/reserved byte " + offset); }
        foreach (int length in new[] { 0, 159, 160, 161, 200, 383, 395 }) Reject(good[..length], "truncated header/directory/payload " + length);
        byte[] truncated = good[..^1]; Put(truncated, 24, (uint)truncated.Length);
        Reject(truncated, "payload truncation with honest total length");
        byte[] trailing = good.Concat(new byte[] { 0 }).ToArray(); Reject(trailing, "trailing byte with old header");
        Put(trailing, 24, (uint)trailing.Length); Reject(trailing, "trailing byte with honest total length");
    }

    private static void DirectoryRefusal()
    {
        byte[] good = Good();
        foreach (var change in new (int Row, int Field, uint Value)[] {
            (0, 0, 0), (0, 0, 0x90), (0, 0, 0x91), (0, 4, 9), (0, 8, 0), (0, 8, 4097),
            (0, 8, uint.MaxValue), (0, 12, 0), (0, 12, 4097), (0, 16, 0), (0, 16, 3), (1, 16, 2),
            (0, 20, 1), (1, 20, 0), (1, 20, int.MaxValue), (1, 20, uint.MaxValue),
            (0, 24, 0), (0, 24, 7), (0, 24, 2 * 1024 * 1024 + 1), (0, 24, uint.MaxValue),
            (0, 28, 1), (0, 96, 1), (1, 108, 1) })
        {
            byte[] bad = (byte[])good.Clone(); Put(bad, 160 + change.Row * 112 + change.Field, change.Value);
            RehashDirectory(bad); Reject(bad, "rehash-valid structural row refusal " + change);
        }
        byte[] unhashed = (byte[])good.Clone(); unhashed[160 + 32] ^= 1;
        Reject(unhashed, "directory content must match captured hash");
    }

    private static void SelectedIntegrityAndGeneration()
    {
        byte[] good = Good(); var captured = Inspect(good);
        byte[] corruptA = (byte[])good.Clone(); corruptA[captured.PayloadOffset] ^= 1;
        var corruptMetadata = Inspect(corruptA);
        Check(corruptMetadata.Sha256 == Hash(corruptA) && corruptMetadata.Sha256 != captured.Sha256,
            "Inspect computes whole-bank hash without claiming an external expected hash or all pixel verification");
        using (var changed = new MemoryStream(corruptA))
        {
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.ReadImage(changed, captured, 0x20), "selected pixel corruption refused");
            Check(changed.CanRead, "failed selected verification does not dispose caller stream");
            Check(EchoesSourceTextureBank.ReadImage(changed, captured, 0x90).SequenceEqual(new byte[] { 0, 0, 255, 255 }),
                "unrelated corrupted payload is not falsely claimed revalidated by selected read");
        }
        byte[] headerSwap = (byte[])good.Clone(); headerSwap[92] ^= 1;
        Check(Inspect(headerSwap).Length == captured.Length, "alternate valid header generation retains the same bank length");
        using (var changed = new MemoryStream(headerSwap))
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.ReadImage(changed, captured, 0x20), "same-size header generation swap refused");
        byte[] indexSwap = (byte[])good.Clone(); indexSwap[160 + 32] ^= 1;
        using (var changed = new MemoryStream(indexSwap))
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.ReadImage(changed, captured, 0x20), "same-size directory swap refused even with captured header unchanged");
        RehashDirectory(indexSwap); var newBank = Inspect(indexSwap);
        using (var changed = new MemoryStream(indexSwap))
        {
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.ReadImage(changed, captured, 0x20), "rehash-valid new index generation cannot reuse an old captured bank");
            Check(EchoesSourceTextureBank.ReadImage(changed, newBank, 0x20).Length == 8, "newly captured valid directory owns its own selected reads");
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.ReadImage(changed, newBank, 0x33), "missing selected identity fails closed");
        }
        using var stream = new MemoryStream(good);
        Expect<ArgumentNullException>(() => EchoesSourceTextureBank.ReadImage(stream, null!, 0x20), "null captured bank");
        Expect<ArgumentNullException>(() => EchoesSourceTextureBank.ReadImage(null!, captured, 0x20), "null selected stream");
        Check(stream.CanRead, "argument refusal never disposes caller stream");
    }

    private static void EncoderBounds()
    {
        var a = Tiny(0x20, 8);
        Expect<ArgumentNullException>(() => EchoesSourceTextureBank.Encode(null!, new[] { a }), "null source binding");
        Expect<ArgumentNullException>(() => EchoesSourceTextureBank.Encode(Area, null!), "null inventory");
        foreach (var bad in new[] { Area with { MreaId = 0 }, Area with { MreaSha256 = "" }, Area with { MreaSha256 = null! }, Area with { MreaSha256 = new string('a', 63) }, Area with { MreaSha256 = new string('g', 64) } })
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(bad, new[] { a }), "invalid source binding " + bad.MreaId);
        foreach (var arena in EchoesArenaSources.All)
            Check(Inspect(EchoesSourceTextureBank.Encode(Area with { MreaId = arena.ResourceId }, new[] { a })).Source.MreaId == arena.ResourceId,
                "canonical six-area identity accepted " + arena.Key);
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, Array.Empty<EchoesSourceTextureBank.Input>()), "empty inventory");
        var excessive = new UnreadableList();
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, excessive), "oversized inventory rejected before indexing");
        Check(excessive.Reads == 0, "entry count bound precedes allocation/index access");
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, new[] { a, a }), "duplicate texture identity");
        foreach (var bad in new[] { a with { ResourceId = 0 }, a with { SourceSha256 = "bad" }, a with { SourceSha256 = null! }, a with { SourceSha256 = new string('g', 64) }, a with { Image = null! } })
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, new[] { bad }), "invalid input metadata");
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, new EchoesSourceTextureBank.Input[] { null! }), "null inventory member");
        foreach (var invalid in new[] { (9u, 1, 1, 1, 4), (8u, 0, 1, 1, 4), (8u, 4097, 1, 1, 4),
            (8u, 1, 1, 0, 4), (8u, 1, 1, 2, 4), (8u, 1, 1, 1, 3), (8u, 1, 1, 1, 8) })
            Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, new[] { a with { Image = InvalidDecoded(invalid) } }),
                "Encode independently bounds forged internal decoded DTO " + invalid);
        var maximum = Enumerable.Range(1, 512).Select(id => a with { ResourceId = (uint)id }).ToArray();
        Check(Inspect(EchoesSourceTextureBank.Encode(Area, maximum)).Entries.Count == 512, "maximum 512-entry inventory remains valid");

        var oversizedImage = Large(1024);
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, new[] { oversizedImage }),
            "valid decoder output above bank-specific 2MiB image limit is refused");
        var large = Large();
        Check(Inspect(EchoesSourceTextureBank.Encode(Area, new[] { large })).Entries[0].Length == 2 * 1024 * 1024,
            "exact 2MiB selected-image boundary accepted");
        var aggregate = Enumerable.Range(1, 32).Select(id => large with { ResourceId = (uint)id }).ToArray();
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, aggregate), "warm aggregate budget refusal");
        long before = GC.GetAllocatedBytesForCurrentThread();
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Encode(Area, aggregate), "32 reused 2MiB images exceed bank cap including metadata");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024 * 1024, "aggregate admission refuses 64MiB output allocation, actual managed bytes " + allocated);
        Console.WriteLine($"SOURCE BANK ALLOCATION PASS: oversized aggregate allocated {allocated} managed bytes before refusal, below 1MiB allowance.");
    }

    private static void BoundedStreams()
    {
        byte[] good = Good();
        Expect<ArgumentNullException>(() => EchoesSourceTextureBank.Inspect(null!), "null stream");
        using var tooLarge = new GuardStream(good, declaredLength: EchoesSourceTextureBank.MaxBankBytes + 1L);
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Inspect(tooLarge), "fake length over 64MiB rejected");
        Check(tooLarge.ReadCalls == 0 && !tooLarge.Disposed, "oversized fake stream rejected before reads and not disposed");
        using var unreadable = new GuardStream(good, canRead: false);
        Expect<NotSupportedException>(() => EchoesSourceTextureBank.Inspect(unreadable), "unreadable stream");
        using var unseekable = new GuardStream(good, canSeek: false);
        Expect<NotSupportedException>(() => EchoesSourceTextureBank.Inspect(unseekable), "unseekable stream");
        using var growing = new GuardStream(good, extraHashByte: true);
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Inspect(growing), "readable growth beyond declared full-bank length");
        Check(growing.HashBytes == good.Length + 1 && !growing.Disposed,
            "full hash consumes declared length plus one sentinel only on growing stream");
        using var shrinking = new GuardStream(good, shortHash: true);
        Expect<EndOfStreamException>(() => EchoesSourceTextureBank.Inspect(shrinking), "early EOF during whole-bank hash");
        Check(shrinking.HashBytes == good.Length - 1 && !shrinking.Disposed, "shrinking hash fails finitely without disposing stream");
        using var changedLength = new GuardStream(good, growReportedLength: true);
        Expect<InvalidDataException>(() => EchoesSourceTextureBank.Inspect(changedLength), "length growth after full hash");
        using var normal = new GuardStream(good);
        var bank = EchoesSourceTextureBank.Inspect(normal);
        Check(bank.Sha256 == Hash(good) && normal.HashBytes == good.Length && !normal.Disposed, "bounded hash scans every declared byte of stable stream");
    }

    private sealed class UnreadableList : IReadOnlyList<EchoesSourceTextureBank.Input>
    {
        public int Reads; public int Count => 513;
        public EchoesSourceTextureBank.Input this[int index] { get { Reads++; throw new InvalidOperationException("Count admission must precede indexing."); } }
        public IEnumerator<EchoesSourceTextureBank.Input> GetEnumerator() => throw new InvalidOperationException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // A finite guard converts an accidentally unbounded growing-stream scan
    // into a test failure rather than hanging the verification process.
    private sealed class GuardStream(byte[] data, long? declaredLength = null, bool canRead = true,
        bool canSeek = true, bool extraHashByte = false, bool shortHash = false, bool growReportedLength = false) : Stream
    {
        private long position; private int resets;
        public int ReadCalls { get; private set; } public int HashBytes { get; private set; } public bool Disposed { get; private set; }
        public override bool CanRead => canRead && !Disposed; public override bool CanSeek => canSeek && !Disposed; public override bool CanWrite => false;
        public override long Length => declaredLength ?? (data.Length + (growReportedLength && resets >= 2 ? 1L : 0L));
        public override long Position { get => position; set { position = value; if (value == 0) resets++; } }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (++ReadCalls > 32) throw new InvalidOperationException("Unbounded stream read loop.");
            bool hashing = resets >= 2;
            int limit = data.Length - (hashing && shortHash ? 1 : 0);
            int count = (int)Math.Min(buffer.Length, Math.Max(0, limit - position));
            if (count > 0) { data.AsSpan((int)position, count).CopyTo(buffer); position += count; }
            else if (hashing && extraHashByte && position >= data.Length && buffer.Length > 0)
            {
                if (HashBytes >= data.Length + 1) throw new InvalidOperationException("Hash read beyond declared length plus one.");
                buffer[0] = 0; count = 1; position++;
            }
            if (hashing) HashBytes += count;
            return count;
        }
        public override int ReadByte() { Span<byte> one = stackalloc byte[1]; return Read(one) == 0 ? -1 : one[0]; }
        public override long Seek(long offset, SeekOrigin origin) { Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => position + offset, _ => Length + offset }; return position; }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private static EchoesSourceTextureBank.Input Tiny(uint id, uint format)
    {
        byte[] txtr = new byte[44]; BinaryPrimitives.WriteUInt32BigEndian(txtr, format);
        BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(4), (ushort)(format == 8 ? 2 : 1));
        BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(6), 1); BinaryPrimitives.WriteUInt32BigEndian(txtr.AsSpan(8), 1);
        if (format == 8) { BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(12), 0xfc00); BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(14), 0x83e0); }
        else { BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(12), 0x001f); BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(14), 0x001f); }
        return new(id, Hash(txtr), RetroTextureSource.DecodeTop(txtr));
    }
    private static EchoesSourceTextureBank.Input Large(int width = 512)
    {
        byte[] txtr = new byte[12 + width * 1024 * 2]; BinaryPrimitives.WriteUInt32BigEndian(txtr, 8);
        BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(4), (ushort)width); BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(6), 1024);
        BinaryPrimitives.WriteUInt32BigEndian(txtr.AsSpan(8), 1); return new(1, Hash(txtr), RetroTextureSource.DecodeTop(txtr));
    }
    private static RetroTextureSource.Decoded InvalidDecoded((uint Format, int Width, int Height, int Mips, int Bytes) value)
    {
        var layout = new RetroTextureSource.Layout(value.Format, value.Width, value.Height, value.Mips, 0, 0, value.Bytes, Array.Empty<RetroTextureSource.Mip>());
        var ctor = typeof(RetroTextureSource.Decoded).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(RetroTextureSource.Layout), typeof(byte[]) }, null)!;
        return (RetroTextureSource.Decoded)ctor.Invoke(new object[] { layout, new byte[value.Bytes] });
    }
    private static byte[] Good() => EchoesSourceTextureBank.Encode(Area, new[] { Tiny(0x90, 10), Tiny(0x20, 8) });
    private static EchoesSourceTextureBank.Bank Inspect(byte[] bytes) { using var stream = new MemoryStream(bytes); return EchoesSourceTextureBank.Inspect(stream); }
    private static void RehashDirectory(byte[] bytes) => SHA256.HashData(bytes.AsSpan(160, checked((int)U32(bytes, 12) * 112))).CopyTo(bytes.AsSpan(124, 32));
    private static void Reject(byte[] bytes, string why) => Expect<InvalidDataException>(() => Inspect(bytes), why);
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("Source texture bank: " + why); }
    private static void Expect<T>(Action action, string why) where T : Exception
    { checks++; try { action(); } catch (T) { return; } throw new InvalidOperationException("Source texture bank expected " + typeof(T).Name + ": " + why); }
}
