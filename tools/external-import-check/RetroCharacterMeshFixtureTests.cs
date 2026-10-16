using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Import.Retro;

internal static class RetroCharacterMeshFixtureTests
{
    public static void Run()
    {
        var skin = new byte[24];
        Set(skin, 0, 1);   // one group
        Set(skin, 4, 1);   // one weight
        Set(skin, 8, 10);  // skeleton bone
        BinaryPrimitives.WriteSingleBigEndian(skin.AsSpan(12, 4), 1.0f);
        Set(skin, 16, 2);  // repeated for two vertices
        var weights = Cskr.Load(skin);
        Check(weights.Count == 2 && weights[0][0].Bone == 10 &&
            weights[1][0].Weight == 1f, "native CSKR binding");
        Set(skin, 4, uint.MaxValue);
        Expect<InvalidDataException>(() => Cskr.Load(skin), "CSKR weight count budget");
        Set(skin, 4, 1);
        Set(skin, 16, uint.MaxValue);
        Expect<OverflowException>(() => Cskr.Load(skin), "CSKR overflowing vertex count");
        Check(Be.U32(new byte[]{0,0,0,2}, 0) == 2, "big endian resource IDs");
        Expect<InvalidDataException>(() => Be.Slice(new byte[4], -1, 2), "negative resource offset");
        Expect<InvalidDataException>(() => Cmdl.Load(_ => new byte[64], 0x10), "invalid CMDL");
    }

    private static void Set(byte[] bytes, int at, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at,4),value);
    private static void Check(bool good, string what)
    {
        if(!good) throw new InvalidOperationException("FAILED: "+what);
    }
    private static void Expect<T>(Action action, string what) where T:Exception
    {
        try { action(); } catch(T) { return; }
        throw new InvalidOperationException("Expected "+typeof(T).Name+": "+what);
    }
}
