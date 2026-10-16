using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MphRead.Mods.Import;

internal static class RetroPakFixtureTests
{
    private const uint Raw = 0x11223344, Zipped = 0x22334455, Stored = 0x33445566, Lzo = 0x44556677;
    internal static void Run()
    {
        byte[] fixture = Build();
        using (var reader = new RetroPakReader(new MemoryStream(fixture, writable: false), leaveOpen: false))
        {
            Check(reader.Resources.Count == 4 && reader.Names[Raw].Name == "test", "PAK resource index");
            Check(reader.Entry(Zipped).Type == "CMDL", "resource type preserved");
            Check(reader.ReadResource(Raw).SequenceEqual(new byte[] { 1, 2, 3 }), "raw resource");
            Check(reader.ReadResource(Zipped).SequenceEqual(new byte[] { 8, 7, 6, 5 }), "MP1 zlib resource");
            Check(reader.ReadResource(Stored).SequenceEqual(new byte[] { 5, 6, 7 }), "MP2 stored segment");
            Check(reader.ReadResource(Lzo).SequenceEqual(new byte[] { 65, 66, 67 }), "MP2 LZO segment");
            Expect<KeyNotFoundException>(() => reader.ReadResource(0xff), "unknown resource");
        }

        var bad = Build();
        Put(bad, 0, 0);
        Expect<InvalidDataException>(() => Open(bad), "unknown PAK version");
        bad = Build(); Put(bad, 8, uint.MaxValue);
        Expect<InvalidDataException>(() => Open(bad), "name count ceiling");
        bad = Build(); Put(bad, 28, uint.MaxValue);
        Expect<InvalidDataException>(() => Open(bad), "resource count ceiling");
        bad = Build(); Put(bad, 32 + 12, uint.MaxValue);
        Expect<InvalidDataException>(() => Open(bad), "raw length outside archive");
        bad = Build(); Put(bad, 32, 2);
        Expect<InvalidDataException>(() => Open(bad), "invalid compression tag");
        bad = Build(); Put(bad, 200, uint.MaxValue);
        using (var reader = Open(bad))
            Expect<InvalidDataException>(() => reader.ReadResource(Zipped), "zlib expanded length ceiling");
        bad = Build(); bad[304] = 0; bad[305] = 0;
        using (var reader = Open(bad))
            Expect<InvalidDataException>(() => reader.ReadResource(Stored), "zero-length segment");
        try { _ = Lzo1x.Decompress(new byte[] { 1 }, 1024); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Truncated LZO block accepted.");
    }

    private static RetroPakReader Open(byte[] b) =>
        new(new MemoryStream(b, writable: false), leaveOpen: false);

    private static byte[] Build()
    {
        var b = new byte[512];
        Put(b, 0, RetroPakReader.Version);
        Put(b, 8, 1);
        Encoding.ASCII.GetBytes("MREA").CopyTo(b, 12);
        Put(b, 16, Raw);
        Put(b, 20, 4);
        Encoding.ASCII.GetBytes("test").CopyTo(b, 24);
        Put(b, 28, 4);
        // Four 20-byte table entries at 32, 52, 72 and 92.
        Set(b, 32, 0, "MREA", Raw, 3, 160);
        byte[] zlib;
        using (var mem = new MemoryStream())
        {
            using (var zip = new ZLibStream(mem, CompressionLevel.Optimal, leaveOpen: true))
                zip.Write(new byte[] { 8, 7, 6, 5 });
            zlib = mem.ToArray();
        }
        Set(b, 52, 1, "CMDL", Zipped, (uint)(4 + zlib.Length), 200);
        Set(b, 72, 1, "ANIM", Stored, 9, 300);
        Set(b, 92, 1, "ANCS", Lzo, 13, 350);
        b[160] = 1; b[161] = 2; b[162] = 3;
        Put(b, 200, 4);
        zlib.CopyTo(b, 204);
        Put(b, 300, 3);
        b[304] = 0xff; b[305] = 0xfd; // signed -3, 3 stored bytes
        b[306] = 5; b[307] = 6; b[308] = 7;
        Put(b, 350, 3);
        b[354] = 0; b[355] = 7; // LZO segment compressed length
        new byte[] { 0x14, 65, 66, 67, 0x11, 0, 0 }.CopyTo(b, 356);
        return b;
    }

    private static void Set(byte[] b, int o, uint c, string type, uint id, uint length, uint offset)
    {
        Put(b, o, c);
        Encoding.ASCII.GetBytes(type).CopyTo(b, o + 4);
        Put(b, o + 8, id); Put(b, o + 12, length); Put(b, o + 16, offset);
    }
    private static void Put(byte[] b, int o, uint x) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), x);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + message);
    }
}
