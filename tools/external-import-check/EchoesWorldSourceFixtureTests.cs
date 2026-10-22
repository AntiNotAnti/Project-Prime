using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;

internal static class EchoesWorldSourceFixtureTests
{
    public static void Run()
    {
        var surface = new byte[96];
        Put32(surface, 0x1c, 0);
        surface[64] = 0x90; // GX triangles
        Put16(surface, 65, 3);
        Put16(surface, 67, 0); Put16(surface, 69, 1); Put16(surface, 71, 2);
        var list = new List<int>();
        EchoesWorldGeometrySource.DecodeSurface(surface, 1, 0, 3, list);
        Check(list.SequenceEqual(new[] { 0, 1, 2 }), "triangle display list");
        surface[64] = 0x98; // strip
        list.Clear();
        EchoesWorldGeometrySource.DecodeSurface(surface, 1, 0, 3, list);
        Check(list.Count == 3, "GX strip triangulation");
        surface[64] = 0xa0; // fan
        list.Clear();
        EchoesWorldGeometrySource.DecodeSurface(surface, 1, 0, 3, list);
        Check(list.Count == 3, "GX fan triangulation");
        surface[64] = 0xff;
        Expect<InvalidDataException>(() =>
            EchoesWorldGeometrySource.DecodeSurface(surface, 1, 0, 3, new List<int>()),
            "unsupported GX primitive");
        surface[64] = 0x90;
        Put16(surface, 67, 50);
        Expect<InvalidDataException>(() =>
            EchoesWorldGeometrySource.DecodeSurface(surface, 1, 0, 3, new List<int>()),
            "vertex index out of range");
        Put16(surface, 67, 0);
        Put32(surface, 0x1c, uint.MaxValue);
        Expect<InvalidDataException>(() =>
            EchoesWorldGeometrySource.DecodeSurface(surface, 1, 0, 3, new List<int>()),
            "display list offset overflow");
        Put32(surface, 0x1c, 0);
        Expect<InvalidDataException>(() =>
            EchoesWorldGeometrySource.DecodeSurface(surface, 0, 0, 3, new List<int>()),
            "missing position descriptor");
    }
    private static void Put32(byte[] bytes,int at,uint n) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at,4),n);
    private static void Put16(byte[] bytes,int at,ushort n) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at,2),n);
    private static void Check(bool good,string name)
    { if(!good)throw new InvalidOperationException(name); }
    private static void Expect<T>(Action action,string name) where T:Exception
    {
        try{action();}
        catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name+": "+name);
    }
}
