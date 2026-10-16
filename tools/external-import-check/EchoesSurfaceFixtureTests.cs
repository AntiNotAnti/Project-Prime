using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.Import;

internal static class EchoesSurfaceFixtureTests
{
    public static void Run()
    {
        byte[] collision = Collision();
        EchoesCollisionData read = EchoesCollisionSource.Parse(collision);
        Check(read.VertexCount == 3 && read.TriangleCount == 1, "three collision vertices and one surface");
        Check(read.TriangleVertices.SequenceEqual(new[] { 0, 1, 2 }), "edge reconstructed triangle");
        Check((read.SurfaceFlags[0] & (1UL << 4)) != 0, "terrain flags");
        byte[] reverse = (byte[])collision.Clone();
        Put64(reverse, 52, (1UL << 24) | (1UL << 4));
        Check(EchoesCollisionSource.Parse(reverse).TriangleVertices.SequenceEqual(new[] { 1, 0, 2 }),
            "flipped triangle winding");
        byte[] bad = (byte[])collision.Clone();
        Put32(bad, 8, 0);
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "collision marker");
        bad = (byte[])collision.Clone(); Put32(bad, 40 + 4, uint.MaxValue);
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "octree overrun");
        bad = (byte[])collision.Clone(); Put32(bad, 48, uint.MaxValue);
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "materials budget");
        bad = (byte[])collision.Clone(); Put32(bad, 73, 65536);
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "edges budget");
        bad = (byte[])collision.Clone(); Put16(bad, 93, 9);
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "invalid triangle edge");
        bad = (byte[])collision.Clone(); bad[72] = 2;
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "invalid surface");
        bad = (byte[])collision.Clone();
        BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(107, 4), 0x7f800000);
        Expect<InvalidDataException>(() => EchoesCollisionSource.Parse(bad), "nonfinite vertex");

        byte[] layer = ScriptLayer();
        var objects = EchoesScriptSource.ParseLayer(layer);
        Check(objects.Count == 1 && objects[0].Type == "SPWN" && objects[0].Active, "SPWN object");
        Check(objects[0].HasTransform && objects[0].Transform[0] == 4 &&
            objects[0].Transform[2] == 12, "position transform");
        Check(EchoesScriptSource.ParseLayer(layer).Count == 1, "deterministic parse");
        bad = (byte[])layer.Clone(); bad[0] = (byte)'X';
        Expect<InvalidDataException>(() => EchoesScriptSource.ParseLayer(bad), "layer magic");
        bad = (byte[])layer.Clone(); Put32(bad, 10, uint.MaxValue);
        Expect<InvalidDataException>(() => EchoesScriptSource.ParseLayer(bad), "object count");
        bad = (byte[])layer.Clone(); Put16(bad, 18, ushort.MaxValue);
        Expect<InvalidDataException>(() => EchoesScriptSource.ParseLayer(bad), "object outside layer");
        bad = (byte[])layer.Clone(); Put16(bad, 24, 5000);
        Expect<InvalidDataException>(() => EchoesScriptSource.ParseLayer(bad), "connections budget");
    }

    private static byte[] Collision()
    {
        var data = new byte[143];
        // Echoes area collision magic lives at section offset 8; octree at 40.
        Put32(data, 8, EchoesCollisionSource.Magic);
        Put32(data, 44, 0);
        Put32(data, 48, 1);     // one material at 52, size 8
        Put64(data, 52, 1UL << 4);
        Put32(data, 60, 0);     // vertex material bytes
        Put32(data, 64, 0);     // edge material bytes
        Put32(data, 68, 1);     // triangle material table count
        data[72] = 0;
        Put32(data, 73, 3);     // edge count
        Put16(data, 77, 0); Put16(data, 79, 1);
        Put16(data, 81, 1); Put16(data, 83, 2);
        Put16(data, 85, 2); Put16(data, 87, 0);
        Put32(data, 89, 3);     // edge indices per triangle
        Put16(data, 93, 0); Put16(data, 95, 1); Put16(data, 97, 2);
        Put32(data, 99, 0);     // unknown u16 count
        Put32(data, 103, 3);    // vertex count
        PutFloat(data, 107, 0); PutFloat(data, 111, 0); PutFloat(data, 115, 0);
        PutFloat(data, 119, 5); PutFloat(data, 123, 0); PutFloat(data, 127, 0);
        PutFloat(data, 131, 0); PutFloat(data, 135, 5); PutFloat(data, 139, 0);
        return data;
    }

    private static byte[] ScriptLayer()
    {
        var editor = new byte[51];
        Put16(editor, 0, 2);
        Put32(editor, 2, EchoesScriptSource.TransformId);
        Put16(editor, 6, 36);
        PutFloat(editor, 8, 4); PutFloat(editor, 12, 5); PutFloat(editor, 16, 12);
        Put32(editor, 44, EchoesScriptSource.ActiveId);
        Put16(editor, 48, 1);
        editor[50] = 1;

        var root = new byte[59];
        Put16(root, 0, 1);
        Put32(root, 2, EchoesScriptSource.EditorId);
        Put16(root, 6, (ushort)editor.Length);
        editor.CopyTo(root, 8);

        var layer = new byte[14 + 6 + 6 + 6 + root.Length];
        Encoding.ASCII.GetBytes("SCLY").CopyTo(layer, 0);
        Put32(layer, 5, 0);
        Put32(layer, 10, 1);
        Encoding.ASCII.GetBytes("SPWN").CopyTo(layer, 14);
        Put16(layer, 18, (ushort)(6 + 6 + root.Length));
        Put32(layer, 20, 0x1234);
        Put16(layer, 24, 0);
        Put32(layer, 26, 0x11111111);
        Put16(layer, 30, (ushort)root.Length);
        root.CopyTo(layer, 32);
        return layer;
    }

    private static void PutFloat(byte[] dst, int at, float value) =>
        BinaryPrimitives.WriteSingleBigEndian(dst.AsSpan(at, 4), value);
    private static void Put32(byte[] dst, int at, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(at, 4), value);
    private static void Put16(byte[] dst, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(at, 2), value);
    private static void Put64(byte[] dst, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(dst.AsSpan(at, 8), value);
    private static void Check(bool okay, string what)
    {
        if (!okay) throw new InvalidOperationException("Expected: " + what);
    }
    private static void Expect<T>(Action invoke, string name) where T : Exception
    {
        try { invoke(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " for " + name);
    }
}
