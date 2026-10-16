using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Import;

/// <summary>
/// Echoes MREA v0x19 section/block reader for local source imports.
/// Based on the Second Hunt Mrea reader format mapping, with explicit bounds,
/// section and cluster limits. No renderer, map registration or asset export.
/// </summary>
public sealed class EchoesMreaSections
{
    public const uint Magic = 0xDEADBEEF;
    public const uint Version = 0x19;
    public const int MaxSections = 4096;
    public const int MaxClusters = 4096;
    public const int MaxDecodedBytes = 128 * 1024 * 1024;

    private readonly byte[] _data;
    private readonly (int Start, int Length)[] _slices;

    public int ModelCount { get; }
    public int LayerCount { get; }
    public int SectionCount => _slices.Length;
    public int ClusterCount { get; }
    public int GeometrySection { get; }
    public int ScriptSection { get; }
    public int GeneratedSection { get; }
    public int CollisionSection { get; }
    public int LightingSection { get; }
    public float[] Transform { get; }

    private EchoesMreaSections(byte[] data, (int Start, int Length)[] slices,
        int models, int layers, int clusters, int[] block, float[] transform)
    {
        _data = data;
        _slices = slices;
        ModelCount = models;
        LayerCount = layers;
        ClusterCount = clusters;
        GeometrySection = block[0];
        ScriptSection = block[1];
        GeneratedSection = block[2];
        CollisionSection = block[3];
        LightingSection = block[5];
        Transform = transform;
    }

    public ReadOnlyMemory<byte> Section(int index)
    {
        if ((uint)index >= _slices.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var span = _slices[index];
        return new ReadOnlyMemory<byte>(_data, span.Start, span.Length);
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    private static int I32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
    private static int Align32(int size) => checked((size + 31) & ~31);

    public static EchoesMreaSections Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 0x80 || U32(raw, 0) != Magic || U32(raw, 4) != Version)
            throw new InvalidDataException("Not a supported Echoes MREA v0x19 resource.");
        uint sectionsRaw = U32(raw, 0x40), clustersRaw = U32(raw, 0x70);
        uint modelsRaw = U32(raw, 0x38), layersRaw = U32(raw, 0x3c);
        if (sectionsRaw == 0 || sectionsRaw > MaxSections || clustersRaw > MaxClusters ||
            modelsRaw > 100_000 || layersRaw > MaxSections)
            throw new InvalidDataException("MREA section/model/cluster count exceeds budget.");

        int count = (int)sectionsRaw;
        int clusters = (int)clustersRaw;
        int layers = (int)layersRaw;
        var blocks = new int[11];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = I32(raw, 0x44 + 4 * i);
            if (blocks[i] < -1 || blocks[i] >= count)
                throw new InvalidDataException("MREA block points outside section table.");
        }
        if (blocks[1] >= 0 && layers > count - blocks[1])
            throw new InvalidDataException("MREA script layers cross section table.");

        int sizesAt = 0x80;
        int tableAt = Align32(checked(sizesAt + count * 4));
        int payloadAt = Align32(checked(tableAt + clusters * 16));
        if (payloadAt > raw.Length)
            throw new InvalidDataException("MREA header tables extend beyond resource.");

        var sectionSizes = new int[count];
        long required = 0;
        for (int i = 0; i < count; i++)
        {
            uint size = U32(raw, sizesAt + 4 * i);
            required += size;
            if (size > MaxDecodedBytes || required > MaxDecodedBytes)
                throw new InvalidDataException("MREA sections exceed decoded size budget.");
            sectionSizes[i] = (int)size;
        }
        var transform = new float[12];
        for (int i = 0; i < 12; i++)
            transform[i] = BinaryPrimitives.ReadSingleBigEndian(raw.Slice(8 + 4 * i, 4));

        using var payload = new MemoryStream((int)required);
        int cursor = payloadAt;
        long decodedTotal = 0;
        for (int i = 0; i < clusters; i++)
        {
            int at = tableAt + i * 16;
            uint expanded = U32(raw, at + 4), encoded = U32(raw, at + 8);
            if (expanded > MaxDecodedBytes || decodedTotal + expanded > MaxDecodedBytes)
                throw new InvalidDataException("MREA cluster exceeds decoded size budget.");
            if (encoded == 0)
            {
                if (expanded > (long)raw.Length - cursor)
                    throw new InvalidDataException("Uncompressed MREA cluster is truncated.");
                payload.Write(raw.Slice(cursor, (int)expanded));
                cursor += (int)expanded;
            }
            else
            {
                // Echoes cluster streams are front-padded to 32-byte alignment,
                // calculated from the compressed length (Second Hunt's parser).
                int pad = (32 - ((int)encoded & 31)) & 31;
                cursor = checked(cursor + pad);
                if (cursor > raw.Length || encoded > (long)raw.Length - cursor)
                    throw new InvalidDataException("Compressed MREA cluster is truncated.");
                byte[] decoded = RetroPakReader.UnpackSegments(
                    raw.Slice(cursor, (int)encoded), (int)expanded);
                if (decoded.Length != expanded)
                    throw new InvalidDataException("Incorrect MREA cluster decoded size.");
                payload.Write(decoded);
                cursor += (int)encoded;
            }
            decodedTotal += expanded;
        }
        if (decodedTotal < required)
            throw new InvalidDataException("MREA clusters do not contain every declared section.");

        byte[] data = payload.ToArray();
        var slices = new (int Start, int Length)[count];
        int pos = 0;
        for (int i = 0; i < count; i++)
        {
            slices[i] = (pos, sectionSizes[i]);
            pos += sectionSizes[i];
        }
        return new EchoesMreaSections(data, slices, (int)modelsRaw, layers, clusters, blocks, transform);
    }
}

/// <summary>
/// The six source MREA resource IDs from Second Hunt's EchoesArena definitions.
/// They resolve inside the user's locally verified Metroid6.pak.
/// </summary>
public static class EchoesArenaSources
{
    public readonly record struct Arena(string Key, string Name, uint ResourceId);

    public static IReadOnlyList<Arena> All { get; } = Array.AsReadOnly(new[]
    {
        new Arena("M01_SidehopperStation", "Sidehopper Station", 0x02690de1),
        new Arena("M02_Spires", "Spires", 0x09749b2f),
        new Arena("M03_CrossfireChaos", "Crossfire Chaos", 0x44d34a2e),
        new Arena("M04_Pipeline", "Pipeline", 0x845dcf93),
        new Arena("M05_SpiderComplex", "Spider Complex", 0xe7095052),
        new Arena("M06_ShootingGallery", "Shooting Gallery", 0xd97a600b),
    });
}
