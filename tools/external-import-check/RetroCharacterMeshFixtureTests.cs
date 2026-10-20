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
        Expect<InvalidDataException>(() => Cskr.Load(skin), "CSKR overflowing vertex count");
        var overflowingGroups = (byte[])skin.Clone();
        Set(overflowingGroups, 0, uint.MaxValue);
        Expect<InvalidDataException>(() => Cskr.Load(overflowingGroups), "CSKR oversized groups");
        Expect<InvalidDataException>(() => Cskr.Load(new byte[3]), "CSKR truncated header");
        Check(Be.U32(new byte[]{0,0,0,2}, 0) == 2, "big endian resource IDs");
        Expect<InvalidDataException>(() => Be.Slice(new byte[4], -1, 2), "negative resource offset");
        Expect<InvalidDataException>(() => Cmdl.Load(_ => new byte[64], 0x10), "invalid CMDL");
        PositionPadding();
    }

    private static void PositionPadding()
    {
        var resolve=typeof(Cmdl).GetMethod("ResolveSkinnedPositionPadding",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        Cmdl Model(int vertices=5) => new()
        {
            Positions=Enumerable.Range(0,vertices*3).Select(v=>v<12 ? (float)v : 0f).ToArray(),
            Skin=Enumerable.Range(0,4).Select(v=>new[]{(Bone:(uint)(10+v),Weight:1f)}).ToList(),
            Triangles=new[]{new Cmdl.Corner{P=0},new Cmdl.Corner{P=1},new Cmdl.Corner{P=3}},TriangleMaterial=new[]{0}
        };
        void Apply(Cmdl model,byte[] bytes,int end) => resolve.Invoke(null,new object[]{model,bytes,(0,end)});
        var good=Model(); var corners=good.Triangles;var skin=good.Skin;var original=good.Positions.ToArray();
        Apply(good,new byte[64],64);
        Check(good.PositionCount==4 && good.Positions.SequenceEqual(original.Take(12)) && ReferenceEquals(corners,good.Triangles)
            && ReferenceEquals(skin,good.Skin) && good.Skin!.All(g=>g.Single().Weight==1f),
            "exact zero alignment tail removes no referenced geometry or source influences");
        var referenced=Model();referenced.Triangles[2].P=4;Apply(referenced,new byte[64],64);
        Check(referenced.PositionCount==5,"referenced unweighted position remains a fail-closed mismatch");
        var nonzero=Model();var bad=new byte[64];bad[63]=1;Apply(nonzero,bad,64);
        Check(nonzero.PositionCount==5,"nonzero attribute tail cannot be discarded");
        var oversized=Model(8);Apply(oversized,new byte[96],96);
        Check(oversized.PositionCount==8,"oversized tail cannot be treated as alignment");
        var unaligned=Model();Apply(unaligned,new byte[60],60);
        Check(unaligned.PositionCount==5,"unaligned section cannot be treated as padding");
        var missing=Model();missing.Skin!.RemoveAt(3);Apply(missing,new byte[64],64);
        Check(missing.PositionCount==5,"missing real CSKR extent cannot be replaced with guessed influences");
        var exact=Model(4);var exactPositions=exact.Positions;Apply(exact,new byte[64],64);
        Check(ReferenceEquals(exact.Positions,exactPositions),"already exact historical model positions stay byte-identical");
        Console.WriteLine("CMDL PADDING PASS: bounded zero alignment, real corner coverage and exact influence preservation");
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
