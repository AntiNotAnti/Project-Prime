using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Native Echoes MREA render surface reader. Keeps world *visual* meshes
/// independent from the already-imported MREA authoritative collision.
/// Based on Second Hunt's MIT-licensed Mrea.ReadGeometry layout research.
/// Extracted commercial geometry is never embedded in the application.
/// </summary>
public static class EchoesWorldGeometrySource
{
    public const int MaxModels = 4096;
    public const int MaxSurfaces = 100000;
    public const int MaxPositions = 2_000_000;
    public const int MaxTriangles = 400000;
    public const int MaxMaterials = 4096;

    public static EchoesWorldGeometry Read(EchoesMreaSections area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.GeometrySection < 0 || area.ModelCount is < 1 or > MaxModels)
            throw new InvalidDataException("Unsupported Echoes world-model count.");

        byte[] set = area.Section(area.GeometrySection).ToArray();
        int pos = 0;
        int textures = ReadCount(set, ref pos, 16384);
        Skip(set, ref pos, 4L * textures);
        int materialCount = ReadCount(set, ref pos, MaxMaterials);
        if (materialCount == 0)
            throw new InvalidDataException("Echoes world model has no materials.");
        var offsets = new int[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            offsets[i] = ReadCount(set, ref pos, set.Length);
            if (i > 0 && offsets[i] < offsets[i - 1])
                throw new InvalidDataException("Retro material table is not ordered.");
        }
        int materialStart = pos;
        var descriptors = new uint[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            int first = checked(materialStart + (i == 0 ? 0 : offsets[i - 1]));
            int last = checked(materialStart + offsets[i]);
            if (first >= last || last > set.Length)
                throw new InvalidDataException("Retro material descriptor extends outside section.");
            try
            {
                descriptors[i] = Cmdl.ReadMaterial(set, first, last, echoes: true).VertexAttributes;
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException
                or OverflowException or InvalidDataException)
            {
                throw new InvalidDataException("Invalid Echoes material descriptor.", ex);
            }
        }

        var vertices = new List<float>();
        var triangles = new List<int>();
        int section = area.GeometrySection + 1;
        int seen = 0;
        for (int model = 0; model < area.ModelCount; model++)
        {
            if (section + 6 >= area.SectionCount)
                throw new InvalidDataException("Echoes world model sections are incomplete.");
            int vertexOffset = vertices.Count / 3;
            ReadPositions(area.Section(section + 1).Span, vertices);
            int countOffset = 0;
            int surfaceCount = ReadCount(area.Section(section + 6).Span,
                ref countOffset, MaxSurfaces - seen);
            seen += surfaceCount;
            section += 7;
            if (section + surfaceCount + 2 > area.SectionCount)
                throw new InvalidDataException("Echoes world surfaces run past the section table.");
            for (int i = 0; i < surfaceCount; i++)
            {
                ReadOnlySpan<byte> surface = area.Section(section + i).Span;
                int material = checked((int)Read32(surface, 12));
                if (material < 0 || material >= materialCount)
                    throw new InvalidDataException("Echoes render surface references a missing material.");
                DecodeSurface(surface, descriptors[material], vertexOffset,
                    vertices.Count / 3, triangles);
            }
            section += surfaceCount + 2;
        }
        if (triangles.Count == 0)
            throw new InvalidDataException("Echoes world model contains no display-list triangles.");
        return new EchoesWorldGeometry(vertices.ToArray(), triangles.ToArray());
    }

    /// <summary>Decode indexed GX triangles, strips and fans for one surface.
    /// Vertex descriptor controls the number of ancillary attributes to skip;
    /// none of those attributes changes authoritative collision.</summary>
    public static void DecodeSurface(ReadOnlySpan<byte> surface, uint descriptor,
        int vertexOffset, int positionsAvailable, List<int> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        if (surface.Length < 0x30 || (descriptor & 3) == 0)
            throw new InvalidDataException("GX surface is incomplete or lacks indexed positions.");
        long start = 0x30L + Read32(surface, 0x1c);
        if (start > int.MaxValue - 31)
            throw new InvalidDataException("GX surface offset exceeds limits.");
        int at = ((int)start + 31) & ~31;
        if (at > surface.Length)
            throw new InvalidDataException("GX surface data lies outside the source section.");

        while (at < surface.Length)
        {
            byte opcode = surface[at++];
            if (opcode == 0) break;
            int primitive = opcode & 0xf8;
            int count = Read16(surface, ref at);
            var indices = new int[count];
            for (int vertex = 0; vertex < count; vertex++)
            {
                for (int bit = 0; bit < 8; bit++)
                    if ((descriptor & (0x01000000u << bit)) != 0)
                        Skip(surface, ref at, 1);
                int index = vertexOffset + Read16(surface, ref at);
                if (index < vertexOffset || index >= positionsAvailable)
                    throw new InvalidDataException("GX vertex index exceeds source model range.");
                indices[vertex] = index;
                if ((descriptor & 0xc) != 0) Skip(surface, ref at, 2);
                if ((descriptor & 0x30) != 0) Skip(surface, ref at, 2);
                if ((descriptor & 0xc0) != 0) Skip(surface, ref at, 2);
                for (int tex = 0; tex < 7; tex++)
                    if ((descriptor & (0x300u << (2 * tex))) != 0)
                        Skip(surface, ref at, 2);
            }
            void Add(int a, int b, int c)
            {
                int x = indices[a], y = indices[b], z = indices[c];
                if (x == y || y == z || x == z) return;
                if (triangles.Count / 3 >= MaxTriangles)
                    throw new InvalidDataException("Echoes world triangles exceed limit.");
                triangles.Add(x); triangles.Add(y); triangles.Add(z);
            }
            if (primitive == 0x90)
            {
                if (count % 3 != 0)
                    throw new InvalidDataException("Malformed GX triangle-list length.");
                for (int i = 0; i < count; i += 3) Add(i, i + 1, i + 2);
            }
            else if (primitive == 0x98)
            {
                for (int i = 0; i + 2 < count; i++)
                    if (i % 2 == 0) Add(i, i + 1, i + 2);
                    else Add(i + 1, i, i + 2);
            }
            else if (primitive == 0xa0)
            {
                for (int i = 1; i + 1 < count; i++) Add(0, i, i + 1);
            }
            else throw new InvalidDataException("Unsupported Echoes GX primitive " +
                opcode.ToString("x2") + ".");
        }
    }

    private static void ReadPositions(ReadOnlySpan<byte> section, List<float> vertices)
    {
        int scalars = section.Length / 12 * 3;
        if (scalars > MaxPositions * 3L - vertices.Count)
            throw new InvalidDataException("Echoes world positions exceed limits.");
        for (int i = 0; i < scalars; i++)
        {
            float value = BinaryPrimitives.ReadSingleBigEndian(section.Slice(i * 4, 4));
            if (!float.IsFinite(value) || Math.Abs(value) > 100000f)
                throw new InvalidDataException("Invalid Echoes world vertex.");
            vertices.Add(value);
        }
    }
    private static int ReadCount(ReadOnlySpan<byte> data, ref int offset, int max)
    {
        uint count = Read32(data, offset);
        offset += 4;
        if (count > max)
            throw new InvalidDataException("Echoes geometry count exceeds budget.");
        return (int)count;
    }
    private static uint Read32(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || data.Length - offset < 4)
            throw new InvalidDataException("Truncated Echoes geometry source.");
        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }
    private static int Read16(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset < 0 || data.Length - offset < 2)
            throw new InvalidDataException("Truncated indexed GX source.");
        int value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }
    private static void Skip(ReadOnlySpan<byte> data, ref int offset, long bytes)
    {
        if (offset < 0 || bytes < 0 || bytes > data.Length - offset)
            throw new InvalidDataException("Echoes source pointer extends beyond section.");
        offset += (int)bytes;
    }
}

public sealed record EchoesWorldGeometry(float[] Positions, int[] TriangleVertices)
{
    public int PositionCount => Positions.Length / 3;
    public int TriangleCount => TriangleVertices.Length / 3;
}
