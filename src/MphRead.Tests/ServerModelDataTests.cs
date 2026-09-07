using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using MphRead.Mods.Network.Server;
using Xunit;

namespace MphRead.Tests;

public sealed class ServerModelDataTests
{
    [Fact]
    public void ProjectionRetainsBoundsAndRemovesTextureAndDrawPayloads()
    {
        byte[] model = new byte[512];
        Set(model, nameof(Header.DlistOffset), 128);
        Set16(model, nameof(Header.MeshCount), 1);
        Set(model, nameof(Header.TextureOffset), 256);
        Set16(model, nameof(Header.TextureCount), 1);
        Set(model, nameof(Header.PaletteOffset), 384);
        Set16(model, nameof(Header.PaletteCount), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(128), 224);
        BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(132), 32);
        model.AsSpan(136, 24).Fill(7); // min/max bounds
        model.AsSpan(224).Fill(0xFA); // presentation payloads
        byte[] projected = ServerModelData.Project(model);
        Header header = Read.ReadStruct<Header>(projected.AsSpan(0, Sizes.Header));
        Assert.Equal(0, header.TextureCount);
        Assert.Equal(0, header.PaletteCount);
        Assert.Equal(0u, header.TextureOffset);
        Assert.Equal(0u, header.PaletteOffset);
        Assert.Equal(Sizes.Header + Sizes.Dlist, projected.Length);
        Assert.Equal(new byte[8], projected.AsSpan((int)header.DlistOffset, 8).ToArray());
        foreach (byte b in projected.AsSpan((int)header.DlistOffset + 8, 24)) { Assert.Equal(7, b); }
        Assert.DoesNotContain((byte)0xFA, projected);
    }

    [Fact]
    public void ProjectionPreservesImplicitNodeCountOffsetWithoutCopyingPadding()
    {
        byte[] model = new byte[256];
        int table = Sizes.Header + 8;
        Set(model, nameof(Header.NodePosCounts), (uint)table);
        model.AsSpan(Sizes.Header, 8).Fill(0xFA);
        BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(table), 3);
        BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(table + 4), 5);
        byte[] projected = ServerModelData.Project(model);
        Header header = Read.ReadStruct<Header>(projected.AsSpan(0, Sizes.Header));
        Assert.Equal((uint)table, header.NodePosCounts);
        Assert.Equal(new byte[8], projected.AsSpan(Sizes.Header, 8).ToArray());
        Assert.Equal(new[] { 3, 5 }, Read.DoOffsets<int>(projected, header.NodePosCounts, 2));
    }

    [Fact]
    public void ProjectionRejectsTruncatedAndOutOfBoundsSections()
    {
        Assert.Throws<InvalidDataException>(() => ServerModelData.Project(new byte[Sizes.Header - 1]));
        byte[] model = new byte[Sizes.Header];
        Set(model, nameof(Header.NodeOffset), uint.MaxValue);
        Set16(model, nameof(Header.NodeCount), 1);
        Assert.Throws<InvalidDataException>(() => ServerModelData.Project(model));
    }

    private static void Set(byte[] bytes, string field, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)Marshal.OffsetOf<Header>(field)), value);
    private static void Set16(byte[] bytes, string field, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)Marshal.OffsetOf<Header>(field)), value);
}
