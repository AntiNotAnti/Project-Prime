using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Network.Server;

/// <summary>
/// Projects a model onto the CPU sections used by simulation. The original
/// header/node layout is retained so the existing animation and attachment
/// code reads the same transforms. Texture/palette payloads and display-list
/// commands are not copied. This output must only be loaded in Read.ServerMode.
/// </summary>
public static class ServerModelData
{
    public static byte[] Project(ReadOnlySpan<byte> source)
    {
        if (source.Length < Sizes.Header)
        {
            throw new InvalidDataException("Truncated model header.");
        }
        Header header = Read.ReadStruct<Header>(source[..Sizes.Header]);
        using var output = new MemoryStream();
        output.Write(source[..Sizes.Header]);
        // The upstream zero-weight fallback infers count from the table's
        // offset. Preserve that offset using zero padding, not original bytes.
        bool implicitCount = header.NodePosCounts != 0 && header.NodeWeightCount == 0;
        if (implicitCount)
        {
            if (header.NodePosCounts < Sizes.Header || header.NodePosCounts > source.Length
                || (header.NodePosCounts - Sizes.Header) % 4 != 0)
            {
                throw new InvalidDataException("Invalid model node-position count table.");
            }
            int count = checked(((int)header.NodePosCounts - Sizes.Header) / 4);
            output.SetLength(header.NodePosCounts);
            CopySection(source, output, nameof(Header.NodePosCounts), header.NodePosCounts, count, sizeof(int));
        }
        CopySection(source, output, nameof(Header.MaterialOffset), header.MaterialOffset, header.MaterialCount, Sizes.Material);
        int dlistOffset = CopySection(source, output, nameof(Header.DlistOffset), header.DlistOffset, header.MeshCount, Sizes.Dlist);
        CopySection(source, output, nameof(Header.NodeOffset), header.NodeOffset, header.NodeCount, Sizes.Node);
        CopySection(source, output, nameof(Header.NodeWeightOffset), header.NodeWeightOffset, header.NodeWeightCount, sizeof(int));
        CopySection(source, output, nameof(Header.MeshOffset), header.MeshOffset, header.MeshCount, Sizes.Mesh);
        CopySection(source, output, nameof(Header.NodePosition), header.NodePosition, header.NodeCount, 12);
        CopySection(source, output, nameof(Header.NodeInitialPosition), header.NodeInitialPosition, header.NodeCount, 12);
        if (!implicitCount)
        {
            CopySection(source, output, nameof(Header.NodePosCounts), header.NodePosCounts, header.NodeWeightCount, sizeof(int));
        }
        int maxIndex = -1;
        if (header.NodePosCounts != 0 && header.NodeWeightCount != 0)
        {
            for (int i = 0; i < header.NodeWeightCount; i++)
            {
                int positions = ReadInt(source, header.NodePosCounts, i);
                int first = ReadInt(source, header.NodeWeightOffset, i);
                if (positions < 0 || first < 0)
                {
                    throw new InvalidDataException("Invalid model node-position range.");
                }
                if (positions != 0)
                {
                    maxIndex = Math.Max(maxIndex, checked(first + positions - 1));
                }
            }
        }
        CopySection(source, output, nameof(Header.NodePosScales), header.NodePosScales, checked(maxIndex + 1), sizeof(int));
        byte[] result = output.ToArray();
        ClearHeader(result, nameof(Header.TextureOffset));
        ClearHeader(result, nameof(Header.PaletteOffset));
        ClearHeader(result, nameof(Header.PrimitiveCount));
        ClearHeader(result, nameof(Header.VertexCount));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(Offset(nameof(Header.TextureCount))), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(Offset(nameof(Header.PaletteCount))), 0);
        if (dlistOffset != 0)
        {
            for (int i = 0; i < header.MeshCount; i++)
            {
                // Bounding boxes remain necessary for entity setup; commands do not.
                result.AsSpan(dlistOffset + i * Sizes.Dlist, 8).Clear();
            }
        }
        return result;
    }

    private static int CopySection(ReadOnlySpan<byte> source, MemoryStream output,
        string field, uint originalOffset, int count, int elementSize)
    {
        int offset = 0;
        if (originalOffset != 0 && count != 0)
        {
            int size = checked(count * elementSize);
            if (count < 0 || originalOffset < Sizes.Header || originalOffset > source.Length
                || size > source.Length - (long)originalOffset)
            {
                throw new InvalidDataException($"Model section {field} exceeds file bounds.");
            }
            offset = checked((int)output.Length);
            output.Position = offset;
            output.Write(source.Slice(checked((int)originalOffset), size));
        }
        long end = output.Length;
        output.Position = Offset(field);
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(value, offset);
        output.Write(value);
        output.Position = end;
        return offset;
    }

    private static int ReadInt(ReadOnlySpan<byte> source, uint offset, int index)
    {
        long start = offset + (long)index * sizeof(int);
        if (offset < Sizes.Header || start > source.Length - sizeof(int))
        {
            throw new InvalidDataException("Model node-position data exceeds file bounds.");
        }
        return BinaryPrimitives.ReadInt32LittleEndian(source.Slice((int)start, sizeof(int)));
    }

    private static int Offset(string field) => checked((int)Marshal.OffsetOf<Header>(field));
    private static void ClearHeader(Span<byte> bytes, string field) => bytes.Slice(Offset(field), 4).Clear();
}
