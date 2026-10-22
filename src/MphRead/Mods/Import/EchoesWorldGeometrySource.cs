using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Native Echoes MREA render surface reader. Keeps world *visual* meshes
/// independent from the already-imported MREA authoritative collision.
/// Based on Second Hunt's MIT-licensed Mrea.ReadGeometry layout research.
/// Extracted commercial geometry is never embedded in the application.
/// </summary>
public static partial class EchoesWorldGeometrySource
{
    public const int MaxModels = 4096;
    public const int MaxSurfaces = 100000;
    public const int MaxPositions = 2_000_000;
    public const int MaxTriangles = 400000;
    public const int MaxMaterials = 4096;

    public static EchoesWorldGeometry Read(EchoesMreaSections area) => ReadCore(area, null);

    /// <summary>Opt-in source-preservation evidence. This does not register a
    /// map, produce a native UV companion or qualify material rendering.</summary>
    public static EchoesWorldSourceGeometry ReadWithSourceCoordinates(EchoesMreaSections area)
    {
        var coordinates = new CoordinateCapture();
        EchoesWorldGeometry geometry = ReadCore(area, coordinates);
        return new(geometry, coordinates.Materials, coordinates.Triangles.ToArray(),
            coordinates.CornerVisits, coordinates.CandidateCornerVisits);
    }

    private static EchoesWorldGeometry ReadCore(EchoesMreaSections area, CoordinateCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.GeometrySection < 0 || area.ModelCount is < 1 or > MaxModels)
            throw new InvalidDataException("Unsupported Echoes world-model count.");

        byte[] set = area.Section(area.GeometrySection).ToArray();
        int pos = 0;
        int textures = ReadCount(set, ref pos, 16384);
        var resources = capture == null ? Array.Empty<uint>() : new uint[textures];
        for (int i = 0; i < resources.Length; i++) resources[i] = Read32(set, pos + 4 * i);
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
        if (capture != null) capture.Materials = new EchoesSourceMaterial[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            int first = checked(materialStart + (i == 0 ? 0 : offsets[i - 1]));
            int last = checked(materialStart + offsets[i]);
            if (first >= last || last > set.Length)
                throw new InvalidDataException("Retro material descriptor extends outside section.");
            try
            {
                Cmdl.Material material = Cmdl.ReadMaterial(set, first, last, echoes: true);
                descriptors[i] = material.VertexAttributes;
                if (capture != null)
                {
                    foreach (int index in material.Tex)
                        if (index < 0 || index >= resources.Length)
                            throw new InvalidDataException("Echoes material texture index is outside its set.");
                    bool candidate = IsCommonOpaqueCandidate(material);
                    capture.Materials[i] = new(i,
                        Convert.ToHexStringLower(SHA256.HashData(set.AsSpan(first, last - first))), candidate,
                        candidate ? resources[material.Tex[1]] : null,
                        candidate ? resources[material.Tex[0]] : null);
                }
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
            SourceUvPools? uv = capture == null ? null : capture.AdmitPools(
                area.Section(section + 4), area.Section(section + 5));
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
                DecodeSurfaceCore(surface, descriptors[material], vertexOffset,
                    vertices.Count / 3, triangles, material, uv,
                    capture?.Materials[material].CommonOpaqueCandidate == true, capture);
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
        => DecodeSurfaceCore(surface, descriptor, vertexOffset, positionsAvailable, triangles,
            -1, null, false, null);

    private static void DecodeSurfaceCore(ReadOnlySpan<byte> surface, uint descriptor,
        int vertexOffset, int positionsAvailable, List<int> triangles, int material,
        SourceUvPools? uv, bool commonCandidate, CoordinateCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        if (vertexOffset < 0 || positionsAvailable < vertexOffset)
            throw new InvalidDataException("Invalid Echoes model-local position range.");
        // The inherited source layout admits T0..T6. Do not silently treat
        // an unhandled eighth coordinate attribute as the next primitive.
        if (capture != null && (descriptor & 0x00c00000) != 0)
            throw new InvalidDataException("Echoes source T7 attributes are outside the coordinate-preservation profile.");
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
            if (primitive is not (0x90 or 0x98 or 0xa0) || (primitive == 0x90 && count % 3 != 0))
                throw new InvalidDataException("Unsupported or malformed Echoes GX primitive.");
            int stride = 2;
            for (int bit = 0; bit < 8; bit++)
                if ((descriptor & (0x01000000u << bit)) != 0) stride++;
            if ((descriptor & 0xc) != 0) stride += 2;
            if ((descriptor & 0x30) != 0) stride += 2;
            if ((descriptor & 0xc0) != 0) stride += 2;
            for (int tex = 0; tex < 7; tex++)
                if ((descriptor & (0x300u << (2 * tex))) != 0) stride += 2;
            if ((long)stride * count > surface.Length - at)
                throw new InvalidDataException("Truncated Echoes GX primitive attributes.");
            capture?.AdmitCorners(count);
            if (commonCandidate && capture != null) capture.CandidateCornerVisits += count;
            var indices = new int[count];
            var pairs = commonCandidate ? new EchoesSourceCoordinates[count] : null;
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
                int t0 = -1, t1 = -1;
                for (int tex = 0; tex < 7; tex++)
                    if ((descriptor & (0x300u << (2 * tex))) != 0)
                    {
                        if (pairs == null) { Skip(surface, ref at, 2); continue; }
                        int indexUv = Read16(surface, ref at);
                        if (tex == 0) t0 = indexUv;
                        if (tex == 1) t1 = indexUv;
                    }
                if (pairs != null)
                {
                    if (uv == null || t0 < 0 || t1 < 0)
                        throw new InvalidDataException("Common Echoes material lacks its paired source UV attributes.");
                    pairs[vertex] = uv.Read(t0, t1);
                }
            }
            void Add(int a, int b, int c)
            {
                int x = indices[a], y = indices[b], z = indices[c];
                if (x == y || y == z || x == z) return;
                if (triangles.Count / 3 >= MaxTriangles)
                    throw new InvalidDataException("Echoes world triangles exceed limit.");
                triangles.Add(x); triangles.Add(y); triangles.Add(z);
                if (capture != null)
                {
                    if ((capture.Triangles.Count + 1L) * 56 > MaxRetainedSourceCoordinateBytes)
                        throw new InvalidDataException("Retained Echoes source coordinates exceed their byte budget.");
                    capture.Triangles.Add(new(material, pairs == null ? null
                        : new EchoesSourceUvTriangle(pairs[a], pairs[b], pairs[c])));
                }
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
