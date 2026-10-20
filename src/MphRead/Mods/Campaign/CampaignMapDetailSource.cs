using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MphRead.Formats.Collision;

namespace MphRead.Mods.Campaign;

/// <summary>
/// Bounded, detached reads of the same local Hunters collision used by the
/// campaign. Never enters Collision.GetCollision's shared simulation cache.
/// Unsupported formats retain the native NAV room.
/// </summary>
public static class CampaignMapDetailSource
{
    public const int MaxFileBytes = 32 * 1024 * 1024;
    public const int MaxGridReferences = 1048576;

    public static MphCollisionInfo ReadCollision(string path, int layerMask, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < Marshal.SizeOf<CollisionHeader>() || stream.Length > MaxFileBytes)
            throw new InvalidDataException("Pause-map collision size is unsupported.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        cancellation.ThrowIfCancellationRequested();
        return ReadCollision(bytes, layerMask);
    }

    public static MphCollisionInfo ReadCollision(ReadOnlySpan<byte> bytes, int layerMask)
    {
        int headerSize = Marshal.SizeOf<CollisionHeader>();
        if (bytes.Length < headerSize || bytes.Length > MaxFileBytes
            || !bytes[..4].SequenceEqual("wc01"u8))
            throw new InvalidDataException("Pause-map detail requires native wc01 collision.");
        CollisionHeader header = Read.ReadStruct<CollisionHeader>(bytes[..headerSize]);
        int byteLength = bytes.Length;
        Section<global::MphRead.Vector3Fx>(header.PointOffset, header.PointCount, 65536);
        Section<global::MphRead.Vector4Fx>(header.PlaneOffset, header.PlaneCount, 65536);
        Section<ushort>(header.PointIndexOffset, header.PointIndexCount, 1048576);
        Section<CollisionData>(header.DataOffset, header.DataCount, 65535);
        Section<ushort>(header.DataIndexOffset, header.DataIndexCount, 1048576);
        Section<CollisionEntry>(header.EntryOffset, header.EntryCount, 1048576);
        Section<RawCollisionPortal>(header.PortalOffset, header.PortalCount, 4096);

        // Check every index before the existing reader filters layer/grid entries.
        // It allocates only after the section counts and spans above are bounded.
        var indices = Read.DoOffsets<ushort>(bytes, header.PointIndexOffset, header.PointIndexCount);
        foreach (ushort index in indices)
            if (index >= header.PointCount) throw new InvalidDataException("Pause-map point index is invalid.");
        var faces = Read.DoOffsets<CollisionData>(bytes, header.DataOffset, header.DataCount);
        foreach (CollisionData face in faces)
            if (face.PlaneIndex >= header.PlaneCount
                || (ulong)face.PointStartIndex + face.PointIndexCount > header.PointIndexCount)
                throw new InvalidDataException("Pause-map face span is invalid.");
        var dataIndices = Read.DoOffsets<ushort>(bytes, header.DataIndexOffset, header.DataIndexCount);
        foreach (ushort index in dataIndices)
            if (index >= header.DataCount) throw new InvalidDataException("Pause-map grid index is invalid.");
        ulong gridReferences = 0;
        foreach (CollisionEntry entry in Read.DoOffsets<CollisionEntry>(bytes, header.EntryOffset, header.EntryCount))
        {
            if ((ulong)entry.DataStartIndex + entry.DataCount > header.DataIndexCount)
                throw new InvalidDataException("Pause-map grid span is invalid.");
            // The existing layer filter expands every cell span, even when
            // many valid cells refer to the same small source index table.
            gridReferences += entry.DataCount;
            if (gridReferences > MaxGridReferences)
                throw new InvalidDataException("Pause-map grid expansion exceeds its budget.");
        }
        return Collision.ReadMphCollision(header, bytes, layerMask);

        void Section<T>(uint offset, uint count, uint maxCount) where T : struct
        {
            if (count > maxCount || (count != 0 && offset < headerSize)
                || (ulong)offset + (ulong)count * (uint)Marshal.SizeOf<T>() > (ulong)byteLength)
                throw new InvalidDataException("Pause-map collision section is invalid or exceeds its budget.");
        }
    }
}
