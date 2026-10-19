using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Import;

/// <summary>
/// Bounds-checked import of Echoes MREA collision surfaces. Based on Second Hunt's
/// MIT-licensed collision layout research, without bundling any game data.
/// Raw vertices use metres, Z-up; the caller performs explicit world conversion.
/// </summary>
public static class EchoesCollisionSource
{
    public const uint Magic = 0xDEAFBABE;
    public const int MaxVertices = 65535;
    public const int MaxEdges = 65535;
    public const int MaxTriangles = 300000;
    public const ulong FlippedTri = 1UL << 24;
    public const ulong ShootThrough = 1UL << 20;

    public static EchoesCollisionData Read(EchoesMreaSections area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.CollisionSection < 0)
            throw new InvalidDataException("Echoes area has no collision section.");
        return Parse(area.Section(area.CollisionSection).Span);
    }

    public static EchoesCollisionData Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 48 || U32(data, 8) != Magic)
            throw new InvalidDataException("Not a supported Echoes collision section.");

        // Original Echoes collision structure: 0x28 -> octree header/payload,
        // materials, two byte-index tables, triangle material indices, edges,
        // triangle edge references, auxiliary u16s and vertex positions.
        long offset = 8 + 0x20;
        long octreeBytes = U32(data, offset + 4);
        offset = Advance(data, offset, 8 + octreeBytes);

        int materialCount = Count(data, ref offset, 256);
        long materialTable = Slice(data, ref offset, (long)materialCount * 8);
        var materials = new ulong[materialCount];
        for (int i = 0; i < materialCount; i++)
            materials[i] = ((ulong)U32(data, materialTable + 8L * i) << 32)
                | U32(data, materialTable + 8L * i + 4);
        if (materialCount == 0)
            throw new InvalidDataException("Echoes collision has no surface flags.");

        SkipBytes(data, ref offset);
        SkipBytes(data, ref offset);
        int materialIndexCount = Count(data, ref offset, MaxTriangles);
        long materialIndices = Slice(data, ref offset, materialIndexCount);

        int edgeCount = Count(data, ref offset, MaxEdges);
        long edges = Slice(data, ref offset, (long)edgeCount * 4);
        int triangleEdgeIndexCount = Count(data, ref offset, MaxTriangles * 3);
        if (triangleEdgeIndexCount % 3 != 0)
            throw new InvalidDataException("Echoes triangle edge references are not triples.");
        int triangleCount = triangleEdgeIndexCount / 3;
        if (materialIndexCount < triangleCount)
            throw new InvalidDataException("Echoes triangle material table is incomplete.");
        long triangleEdges = Slice(data, ref offset, (long)triangleEdgeIndexCount * 2);

        int unknownCount = Count(data, ref offset, MaxTriangles * 3);
        Slice(data, ref offset, (long)unknownCount * 2);
        int vertexCount = Count(data, ref offset, MaxVertices);
        long points = Slice(data, ref offset, (long)vertexCount * 3 * 4);

        var vertices = new float[vertexCount * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            float value = BinaryPrimitives.ReadSingleBigEndian(data.Slice(checked((int)(points + i * 4L)), 4));
            if (!float.IsFinite(value) || MathF.Abs(value) > 100000f)
                throw new InvalidDataException("Echoes collision has non-finite or oversized coordinates.");
            vertices[i] = value;
        }

        var indices = new int[triangleCount * 3];
        var flags = new ulong[triangleCount];
        for (int tri = 0; tri < triangleCount; tri++)
        {
            int e0 = U16(data, triangleEdges + tri * 6L);
            int e1 = U16(data, triangleEdges + tri * 6L + 2);
            int e2 = U16(data, triangleEdges + tri * 6L + 4);
            if (e0 >= edgeCount || e1 >= edgeCount || e2 >= edgeCount)
                throw new InvalidDataException("Echoes triangle references an invalid edge.");
            int v0 = U16(data, edges + 4L * e0);
            int v1 = U16(data, edges + 4L * e0 + 2);
            int w0 = U16(data, edges + 4L * e1);
            int w1 = U16(data, edges + 4L * e1 + 2);
            int x0 = U16(data, edges + 4L * e2);
            int x1 = U16(data, edges + 4L * e2 + 2);
            if (e0 == e1 || e0 == e2 || e1 == e2
                || v0 >= vertexCount || v1 >= vertexCount
                || w0 >= vertexCount || w1 >= vertexCount
                || x0 >= vertexCount || x1 >= vertexCount
                || v0 == v1 || w0 == w1 || x0 == x1)
                throw new InvalidDataException("Echoes collision triangle contains bad or repeated edges or vertices.");
            // Three distinct undirected edges must form exactly one closed
            // triangle. Preserve the original first-edge winding and FlippedTri
            // handling; edge storage orientation itself is not winding proof.
            bool w0Shared = w0 == v0 || w0 == v1;
            bool w1Shared = w1 == v0 || w1 == v1;
            if (w0Shared == w1Shared)
                throw new InvalidDataException("Echoes collision triangle edges do not share one vertex.");
            int v2 = w0Shared ? w1 : w0;
            int remaining = (w0Shared ? w0 : w1) == v0 ? v1 : v0;
            if (!((x0 == remaining && x1 == v2) || (x1 == remaining && x0 == v2)))
                throw new InvalidDataException("Echoes collision triangle edges do not close.");
            int surface = data[checked((int)(materialIndices + tri))];
            if (surface >= materialCount)
                throw new InvalidDataException("Echoes collision triangle has invalid material index.");
            ulong bits = materials[surface];
            if ((bits & FlippedTri) != 0) (v0, v1) = (v1, v0);
            indices[3 * tri] = v0;
            indices[3 * tri + 1] = v1;
            indices[3 * tri + 2] = v2;
            flags[tri] = bits;
        }
        return new EchoesCollisionData(vertices, indices, flags);
    }

    private static uint U32(ReadOnlySpan<byte> data, long at)
    {
        Require(data, at, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice((int)at, 4));
    }

    private static ushort U16(ReadOnlySpan<byte> data, long at)
    {
        Require(data, at, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(data.Slice((int)at, 2));
    }

    private static void Require(ReadOnlySpan<byte> data, long at, long length)
    {
        if (at < 0 || length < 0 || at > data.Length || length > data.Length - at)
            throw new InvalidDataException("Echoes collision index exceeds section bounds.");
    }

    private static long Advance(ReadOnlySpan<byte> data, long at, long length)
    {
        Require(data, at, length);
        return at + length;
    }

    private static long Slice(ReadOnlySpan<byte> data, ref long at, long length)
    {
        long start = at;
        at = Advance(data, at, length);
        return start;
    }

    private static int Count(ReadOnlySpan<byte> data, ref long at, int max)
    {
        uint value = U32(data, at);
        at += 4;
        if (value > max)
            throw new InvalidDataException("Echoes collision count exceeds import budget.");
        return (int)value;
    }

    private static void SkipBytes(ReadOnlySpan<byte> data, ref long at)
    {
        uint length = U32(data, at);
        at = Advance(data, at, 4 + (long)length);
    }
}

public sealed record EchoesCollisionData(float[] Vertices, int[] TriangleVertices, ulong[] SurfaceFlags)
{
    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => TriangleVertices.Length / 3;
}
