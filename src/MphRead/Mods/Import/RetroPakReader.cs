using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MphRead.Mods.Import;

/// <summary>
/// Read-only Retro Studios MP1/MP2 PAK v3.5 index over a seekable source.
/// Unlike upstream's whole-PAK byte[] representation, this reads only the
/// resource index, then the requested resource. All extraction is in memory,
/// bounded and never written/advertised as a Community asset.
/// </summary>
public sealed class RetroPakReader : IDisposable
{
    public const uint Version = 0x00030005;
    public const int MaxNamedEntries = 100_000;
    public const int MaxResources = 200_000;
    public const int MaxResourceBytes = 256 * 1024 * 1024;
    private const int MaxNameBytes = 4096;
    private const int SegmentUnpackedBytes = 0x4000;

    private readonly Stream _archive;
    private readonly bool _leaveOpen;
    private readonly object _gate = new();
    private readonly Dictionary<uint, RetroPakEntry> _resources = new();
    private readonly Dictionary<uint, (string Type, string Name)> _names = new();
    private bool _disposed;

    public IReadOnlyDictionary<uint, RetroPakEntry> Resources => _resources;
    public IReadOnlyDictionary<uint, (string Type, string Name)> Names => _names;

    public RetroPakReader(Stream archive, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (!archive.CanRead || !archive.CanSeek)
            throw new ArgumentException("Retro PAK requires a readable seekable archive stream.", nameof(archive));
        _archive = archive;
        _leaveOpen = leaveOpen;
        if (archive.Length < 12 || Read32(0) != Version)
            throw new InvalidDataException("Unsupported Retro PAK version/header.");

        uint namedCount = Read32(8);
        if (namedCount > MaxNamedEntries)
            throw new InvalidDataException("PAK name table exceeds limit.");
        long cursor = 12;
        for (int i = 0; i < namedCount; i++)
        {
            byte[] header = ReadAt(cursor, 12);
            string type = TypeAt(header, 0);
            uint id = Be32(header, 4);
            uint nameBytes = Be32(header, 8);
            cursor += 12;
            if (nameBytes > MaxNameBytes)
                throw new InvalidDataException("PAK resource name exceeds limit.");
            byte[] name = ReadAt(cursor, (int)nameBytes);
            cursor += nameBytes;
            _names.TryAdd(id, (type, Encoding.Latin1.GetString(name)));
        }

        uint resourceCount = Read32(cursor);
        cursor += 4;
        if (resourceCount > MaxResources ||
            cursor > archive.Length || resourceCount * 20L > archive.Length - cursor)
            throw new InvalidDataException("PAK resource index exceeds source bounds.");

        for (int i = 0; i < resourceCount; i++)
        {
            byte[] entry = ReadAt(cursor, 20);
            cursor += 20;
            uint compressed = Be32(entry, 0);
            if (compressed > 1)
                throw new InvalidDataException("Invalid PAK compression flag.");
            string type = TypeAt(entry, 4);
            uint id = Be32(entry, 8);
            long length = Be32(entry, 12);
            long offset = Be32(entry, 16);
            if (offset > archive.Length || length > archive.Length - offset ||
                length > MaxResourceBytes)
                throw new InvalidDataException("PAK resource outside archive or exceeding budget.");
            _resources.TryAdd(id, new RetroPakEntry(id, type, compressed == 1, offset, length));
        }
    }

    private static uint Be32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private uint Read32(long offset) => Be32(ReadAt(offset, 4), 0);

    private static string TypeAt(byte[] data, int offset)
    {
        for (int i = 0; i < 4; i++)
            if (data[offset + i] is < 0x20 or > 0x7e)
                throw new InvalidDataException("Invalid PAK resource type.");
        return Encoding.ASCII.GetString(data, offset, 4);
    }

    private byte[] ReadAt(long offset, int length)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (length < 0 || offset < 0 || offset > _archive.Length || length > _archive.Length - offset)
                throw new InvalidDataException("Retro PAK read outside archive bounds.");
            byte[] bytes = new byte[length];
            _archive.Position = offset;
            _archive.ReadExactly(bytes);
            return bytes;
        }
    }

    public bool Contains(uint id) => _resources.ContainsKey(id);

    public RetroPakEntry Entry(uint id) =>
        _resources.TryGetValue(id, out var entry) ? entry :
            throw new KeyNotFoundException("Resource absent from local PAK: " + id.ToString("x8"));

    /// <summary>
    /// Returns only one explicitly requested resource, never an entire PAK.
    /// Supports MP1 zlib and MP2 segmented stored/zlib/LZO1X resources.
    /// The caller owns the byte[] and must not publish it as a Community asset.
    /// </summary>
    public byte[] ReadResource(uint id)
    {
        var entry = Entry(id);
        byte[] raw = ReadAt(entry.Offset, (int)entry.Length);
        if (!entry.Compressed) return raw;
        if (raw.Length < 5)
            throw new InvalidDataException("Compressed PAK resource is truncated.");
        long uncompressed = Be32(raw, 0);
        if (uncompressed > MaxResourceBytes)
            throw new InvalidDataException("PAK decompressed size exceeds budget.");
        int size = (int)uncompressed;
        if (IsZlib(raw.AsSpan(4)))
            return Inflate(raw.AsSpan(4), size);
        return UnpackSegments(raw.AsSpan(4), size);
    }

    private static bool IsZlib(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] == 0x78 &&
        data[1] is 0x01 or 0x9c or 0xda;

    private static byte[] Inflate(ReadOnlySpan<byte> data, int count)
    {
        using var input = new MemoryStream(data.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var output = new byte[count];
        try { zlib.ReadExactly(output); }
        catch (EndOfStreamException ex)
        { throw new InvalidDataException("Zlib PAK resource ends before declared size.", ex); }
        if (zlib.ReadByte() != -1)
            throw new InvalidDataException("Zlib PAK resource exceeds declared size.");
        return output;
    }

    internal static byte[] UnpackSegments(ReadOnlySpan<byte> bytes, int expected)
    {
        var output = new byte[expected];
        int read = 0;
        int written = 0;
        while (written < expected)
        {
            if (read > bytes.Length - 2)
                throw new InvalidDataException("Truncated segmented PAK resource.");
            short header = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(read, 2));
            read += 2;
            if (header == 0)
                throw new InvalidDataException("Zero-length PAK segment.");
            int length = Math.Abs((int)header);
            if (length > bytes.Length - read)
                throw new InvalidDataException("PAK segment crosses source boundary.");
            ReadOnlySpan<byte> part = bytes.Slice(read, length);
            read += length;
            int remaining = expected - written;
            if (header < 0)
            {
                if (length > remaining)
                    throw new InvalidDataException("Stored PAK segment exceeds declared output.");
                part.CopyTo(output.AsSpan(written));
                written += length;
            }
            else
            {
                int blockSize = Math.Min(SegmentUnpackedBytes, remaining);
                byte[] decoded = IsZlib(part)
                    ? Inflate(part, blockSize)
                    : Lzo1x.Decompress(part.ToArray(), blockSize);
                if (decoded.Length != blockSize)
                    throw new InvalidDataException("Compressed PAK segment has the wrong decoded size.");
                decoded.CopyTo(output, written);
                written += decoded.Length;
            }
        }
        return output;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_leaveOpen) _archive.Dispose();
        }
    }
}

public readonly record struct RetroPakEntry(
    uint Id, string Type, bool Compressed, long Offset, long Length);
