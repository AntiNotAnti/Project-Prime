using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;

internal static class EchoesMreaFixtureTests
{
    public static void Run()
    {
        byte[] plain = Fixture(compressed: false);
        var info = EchoesMreaSections.Parse(plain);
        Check(info.SectionCount == 2 && info.ClusterCount == 1, "section index");
        Check(info.GeometrySection == 0 && info.CollisionSection == 1, "section block IDs");
        Check(info.Section(0).Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "geometry section");
        Check(info.Section(1).Span.SequenceEqual(new byte[] { 5, 6, 7, 8 }), "collision section");
        Check(info.Transform.Length == 12 && info.LayerCount == 0, "MREA metadata");

        var packed = EchoesMreaSections.Parse(Fixture(compressed: true));
        Check(packed.Section(0).Span.SequenceEqual(info.Section(0).Span) &&
            packed.Section(1).Span.SequenceEqual(info.Section(1).Span),
            "segmented MREA cluster");

        byte[] bad = Fixture(false);
        Put(bad, 0, 0);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "magic");
        bad = Fixture(false); Put(bad, 0x40, uint.MaxValue);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "sections budget");
        bad = Fixture(false); Put(bad, 0x70, uint.MaxValue);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "cluster count");
        bad = Fixture(false); Put(bad, 0x80, uint.MaxValue);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "declared decoded bytes");
        bad = Fixture(false); Put(bad, 0xa0 + 4, 1000);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "truncated raw cluster");
        bad = Fixture(true); Put(bad, 0xa0 + 8, 1000);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "truncated compressed cluster");
        bad = Fixture(false); Put(bad, 0x44 + 3 * 4, 500);
        Expect<InvalidDataException>(() => EchoesMreaSections.Parse(bad), "bad collision section index");
        Check(EchoesArenaSources.All.Count == 6 &&
            EchoesArenaSources.All.Select(x => x.ResourceId).Distinct().Count() == 6,
            "six distinct source arena IDs");
    }

    private static byte[] Fixture(bool compressed)
    {
        // Two four-byte sections, one eight-byte cluster. Header size table
        // occupies 0x80..0x87; cluster table 0xA0..0xAF, data starts 0xC0.
        var b = new byte[512];
        Put(b, 0, EchoesMreaSections.Magic);
        Put(b, 4, EchoesMreaSections.Version);
        Put(b, 0x38, 1); Put(b, 0x3c, 0);
        Put(b, 0x40, 2);
        for (int i = 0; i < 11; i++) Put(b, 0x44 + i * 4, uint.MaxValue);
        Put(b, 0x44, 0); Put(b, 0x44 + 12, 1);
        Put(b, 0x70, 1);
        Put(b, 0x80, 4); Put(b, 0x84, 4);
        Put(b, 0xa4, 8);
        if (compressed)
        {
            Put(b, 0xa8, 10);
            // 10 bytes compressed, plus 22 bytes front padding.
            int off = 0xc0 + 22;
            b[off] = 0xff; b[off + 1] = 0xf8; // signed -8: stored segment
            for (int i = 0; i < 8; i++) b[off + 2 + i] = (byte)(i + 1);
        }
        else
        {
            Put(b, 0xa8, 0);
            for (int i = 0; i < 8; i++) b[0xc0 + i] = (byte)(i + 1);
        }
        return b;
    }
    private static void Put(byte[] b, int offset, uint number) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset, 4), number);
    private static void Check(bool okay, string name)
    {
        if (!okay) throw new InvalidOperationException(name);
    }
    private static void Expect<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + name);
    }
}
