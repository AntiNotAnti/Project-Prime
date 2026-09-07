using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MphRead.Mods.Network
{
    [Flags]
    public enum ReplayMarker : ushort
    {
        None = 0, Kill = 1, Headshot = 2, MultiKill = 4, FlagCapture = 8,
        NodeCapture = 16, PrimeChange = 32, MatchPoint = 64, Overtime = 128, MatchEnd = 256
    }
    public readonly record struct ReplayIndexEntry(uint Frame, long Offset, bool Keyframe, ReplayMarker Marker);

    /// <summary>Independent compressed chunks: a torn final write cannot invalidate earlier chunks.</summary>
    internal sealed class ReplayArchive : IDisposable
    {
        internal const int HeaderBytes = 24, MaximumRawBytes = 2 * 1024 * 1024;
        internal const uint MaximumFrame = 60 * 60 * 12 * 60;
        private const uint ChunkMagic = 0x33435046; // FPC3
        private const int MaximumIndexEntries = 131072;
        internal const long MaximumFileBytes = 2L * 1024 * 1024 * 1024;
        private const int MaximumChunks = 4_000_000;
        private int _chunks;
        private long _decodedBytes;
        private const long MaximumDecodedBytes = 8L * 1024 * 1024 * 1024;
        private readonly FileStream _file;
        private readonly List<ReplayIndexEntry> _index = new();
        private long _validEnd = DemoFile.HeaderSize;
        private uint _lastFrame;
        internal IReadOnlyList<ReplayIndexEntry> Index => _index;
        internal uint LastFrame => _lastFrame;
        internal bool RecoveredTail { get; private set; }
        internal ReplayArchive(FileStream file, bool scan)
        {
            _file = file;
            if (scan) Scan();
        }

        internal void Write(uint frame, ReadOnlySpan<byte> payload, bool keyframe = false, ReplayMarker marker = ReplayMarker.None)
        {
            _decodedBytes += payload.Length;
            if (_decodedBytes > MaximumDecodedBytes || ++_chunks > MaximumChunks || _file.Position > MaximumFileBytes - MaximumRawBytes - 4096 - HeaderBytes
                || frame > MaximumFrame || frame < _lastFrame || payload.Length is < 1 or > MaximumRawBytes
                || (!keyframe && payload.Length > NetConfig.MaxPacketSize))
                throw new InvalidDataException("Replay chunk exceeds its frame or payload bounds.");
            using var buffer = new MemoryStream();
            using (var compressor = new DeflateStream(buffer, CompressionLevel.Fastest, true)) compressor.Write(payload);
            byte[] compressed = buffer.ToArray();
            if (compressed.Length > MaximumRawBytes + 4096) throw new InvalidDataException("Replay compressed chunk too large.");
            Span<byte> header = stackalloc byte[HeaderBytes];
            header.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(header, ChunkMagic);
            header[4] = keyframe ? (byte)2 : (byte)1;
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)marker);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], compressed.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[12..], payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], frame);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], Checksum(compressed, header[..20]));
            _file.Write(header); _file.Write(compressed);
            _lastFrame = frame;
            // Each record is a closed deflate stream. Flush exposes complete chunks to crash recovery.
            _file.Flush();
        }

        private void Scan()
        {
            if (_file.Length > MaximumFileBytes) throw new InvalidDataException("Replay file exceeds 2 GiB.");
            _file.Position = DemoFile.HeaderSize;
            while (_file.Position < _file.Length)
            {
                if (++_chunks > MaximumChunks) { RecoveredTail = true; break; }
                long offset = _file.Position;
                if (!ReadChunk(out uint frame, out bool keyframe, out ReplayMarker marker, out byte[] data)
                    || frame < _lastFrame || frame > MaximumFrame)
                { RecoveredTail = true; break; }
                _decodedBytes += data.Length;
                if (_decodedBytes > MaximumDecodedBytes) { RecoveredTail = true; break; }
                if (keyframe || marker != ReplayMarker.None)
                {
                    if (_index.Count == MaximumIndexEntries) { RecoveredTail = true; break; }
                    _index.Add(new(frame, offset, keyframe, marker));
                }
                _lastFrame = frame;
                _validEnd = _file.Position;
            }
            _file.Position = DemoFile.HeaderSize;
        }

        internal DemoRecord? ReadNext()
        {
            while (_file.Position < _validEnd)
            {
                if (!ReadChunk(out uint frame, out bool keyframe, out _, out byte[] data)) return null;
                if (!keyframe) return new DemoRecord(frame, data);
            }
            return null;
        }

        internal DemoRecord[]? Seek(uint target, out uint restoredFrame)
        {
            restoredFrame = 0;
            ReplayIndexEntry? selected = null;
            foreach (ReplayIndexEntry entry in _index)
            {
                if (entry.Frame > target) break;
                if (entry.Keyframe) selected = entry;
            }
            if (selected is not ReplayIndexEntry checkpoint)
            { _file.Position = DemoFile.HeaderSize; return Array.Empty<DemoRecord>(); }
            _file.Position = checkpoint.Offset;
            if (!ReadChunk(out uint frame, out bool keyframe, out _, out byte[] data) || !keyframe) return null;
            restoredFrame = frame;
            return Unpack(frame, data);
        }

        internal static byte[] Pack(IReadOnlyList<byte[]> records)
        {
            if (records.Count is < 1 or > 2048) throw new InvalidDataException("Replay keyframe record count invalid.");
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(records.Count);
            foreach (byte[] record in records)
            {
                if (record.Length is < 1 or > NetConfig.MaxPacketSize) throw new InvalidDataException("Replay keyframe record too large.");
                writer.Write((ushort)record.Length); writer.Write(record);
                if (stream.Length > MaximumRawBytes) throw new InvalidDataException("Replay keyframe too large.");
            }
            return stream.ToArray();
        }

        internal static DemoRecord[]? Unpack(uint frame, ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 4) return null;
            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes); bytes = bytes[4..];
            if (count is < 1 or > 2048) return null;
            var records = new DemoRecord[count];
            for (int i = 0; i < count; i++)
            {
                if (bytes.Length < 2) return null;
                int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes); bytes = bytes[2..];
                if (length is < 1 or > NetConfig.MaxPacketSize || bytes.Length < length) return null;
                records[i] = new(frame, bytes[..length].ToArray()); bytes = bytes[length..];
            }
            return bytes.IsEmpty ? records : null;
        }

        private bool ReadChunk(out uint frame, out bool keyframe, out ReplayMarker marker, out byte[] data)
        {
            frame = 0; keyframe = false; marker = ReplayMarker.None; data = Array.Empty<byte>();
            Span<byte> header = stackalloc byte[HeaderBytes];
            if (_file.ReadAtLeast(header, HeaderBytes, false) != HeaderBytes
                || BinaryPrimitives.ReadUInt32LittleEndian(header) != ChunkMagic
                || header[4] is not (1 or 2) || header[5] != 0) return false;
            marker = (ReplayMarker)BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            int rawLength = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
            frame = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]); keyframe = header[4] == 2;
            if (((ushort)marker & ~511) != 0 || frame > MaximumFrame
                || compressedLength is < 1 or > MaximumRawBytes + 4096 || rawLength is < 1 or > MaximumRawBytes
                || (!keyframe && rawLength > NetConfig.MaxPacketSize) || _file.Length - _file.Position < compressedLength) return false;
            byte[] compressed = new byte[compressedLength];
            _file.ReadExactly(compressed);
            if (Checksum(compressed, header[..20]) != BinaryPrimitives.ReadUInt32LittleEndian(header[20..])) return false;
            try
            {
                using var input = new MemoryStream(compressed, false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                data = new byte[rawLength];
                if (deflate.ReadAtLeast(data, rawLength, false) != rawLength || deflate.ReadByte() != -1) return false;
                return !keyframe || Unpack(frame, data) != null;
            }
            catch (InvalidDataException) { return false; }
        }

        private static uint Checksum(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in prefix)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            return ~crc;
        }
        public void Dispose() => _file.Dispose();
    }
}
