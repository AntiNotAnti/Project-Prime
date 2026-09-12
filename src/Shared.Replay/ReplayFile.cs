using System;
using System.IO;
using System.IO.Compression;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// FPDM format 2: frame deltas and length-prefixed records in a deflate
    /// stream. The uncompressed protocol byte selects the record decoder:
    /// legacy protocol-4 packets or authoritative server presentation facts.
    /// </summary>
    internal static class ReplayFile
    {
        // "FPDM" -- legacy Project Prime replay format identifier.
        public static readonly byte[] Magic = { (byte)'F', (byte)'P', (byte)'D', (byte)'M' };
        /// <summary>
        /// 2: frame-stamped records over a deflate stream. Version 1 files
        /// are refused rather than read -- their timestamps mean something
        /// else and their body is not compressed, so there is nothing here
        /// that could read one by accident.
        /// </summary>
        public const byte FormatVersion = 2;
        public const byte IndexedFormatVersion = 3;
        // Authoritative protocol 5 was checkpointed before the live wire moved
        // to 6. These formats contain server facts, not joins or input commands;
        // both remain readable through replay-only adapters after version 7 without enabling either old wire on a socket.
        // Protocol 9 changes live JOIN routing only. Recorded snapshots, rosters,
        // world facts and checkpoints retain the protocol-8 layout.
        public static bool IsAuthoritativeProtocol(byte protocol)
            => protocol >= 5 && protocol <= NetHeader.Version;
        public static bool IsSupportedProtocol(byte protocol) => protocol == 4 || IsAuthoritativeProtocol(protocol);
        public const string Extension = ".fpreplay";

        /// <summary>Magic, format version, protocol version. Never compressed: it says how to read the rest.</summary>
        public const int HeaderSize = 4 + 1 + 1;

        /// <summary>
        /// A frame delta of 0xFF means "not a delta": a 32-bit one follows.
        /// One byte covers 254 frames, which is every record in a running
        /// match; the escape is for the gaps, where the recorder spent four
        /// seconds loading a room.
        /// </summary>
        public const byte LongGap = 0xFF;
    }

    /// <summary>Appends recorded packets to a file as they arrive. Not thread-safe -- called from the net-update thread only.</summary>
    internal sealed class ReplayWriter : IDisposable
    {
        /// <summary>
        /// Frames between flushes. A replay that dies with the game stays
        /// watchable to within this much of the crash.
        /// </summary>
        private const uint FlushIntervalFrames = 15;

        private readonly FileStream _stream;
        private readonly DeflateStream? _deflate;
        private readonly ReplayArchive? _archive;
        private uint _lastFrame;
        private uint _lastFlushFrame;
        private bool _hasFlushed;
        private bool _disposed;
        private readonly byte[] _header = new byte[7];
        public byte ProtocolVersion { get; }

        public ReplayWriter(string path, byte protocolVersion = NetHeader.Version, bool indexed = false)
        {
            ProtocolVersion = protocolVersion;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _stream.Write(ReplayFile.Magic);
            _stream.WriteByte(indexed ? ReplayFile.IndexedFormatVersion : ReplayFile.FormatVersion);
            _stream.WriteByte(protocolVersion);
            _stream.Flush();
            if (indexed) _archive = new ReplayArchive(_stream, scan: false);
            else _deflate = new DeflateStream(_stream, CompressionLevel.Fastest, leaveOpen: true);
        }

        /// <param name="frame">Simulation frames since this writer was created.</param>
        public void WriteRecord(uint frame, ReadOnlySpan<byte> data, ReplayMarker marker = ReplayMarker.None)
        {
            if (data.Length is < 1 or > NetConfig.MaxPacketSize)
            { throw new ArgumentOutOfRangeException(nameof(data)); }
            if (_archive != null)
            {
                _archive.Write(frame, data, marker: marker);
                FlushIfDue(frame, marker);
                return;
            }
            if (frame < _lastFrame)
            {
                // Only reachable if the frame counter were ever wound back.
                // Cheaper to pin than to encode a negative delta nothing
                // would know what to do with.
                frame = _lastFrame;
            }
            uint delta = frame - _lastFrame;
            _lastFrame = frame;
            int at = 0;
            if (delta < ReplayFile.LongGap)
            {
                _header[at++] = (byte)delta;
            }
            else
            {
                _header[at++] = ReplayFile.LongGap;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                    _header.AsSpan(at), delta);
                at += 4;
            }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                _header.AsSpan(at), (ushort)data.Length);
            at += 2;
            _deflate!.Write(_header.AsSpan(0, at));
            _deflate!.Write(data);
            FlushIfDue(frame, marker);
        }

        internal void WriteKeyframe(uint frame, System.Collections.Generic.IReadOnlyList<byte[]> records)
        {
            if (_archive == null) throw new InvalidOperationException("Keyframes require replay format 3.");
            _archive.Write(frame, ReplayArchive.Pack(records), keyframe: true);
            FlushIfDue(frame, ReplayMarker.None);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_archive != null)
            {
                _archive.Dispose();
                return;
            }
            try
            {
                _deflate?.Dispose();
                _stream.Flush(flushToDisk: true);
            }
            finally { _stream.Dispose(); }
        }

        private void FlushIfDue(uint frame, ReplayMarker marker)
        {
            bool terminal = (marker & ReplayMarker.MatchEnd) != 0;
            // Publish the initial complete baseline immediately. Subsequent
            // ordinary frames use the legacy 15-frame crash-recovery cadence.
            if (_hasFlushed && !terminal && frame - _lastFlushFrame < FlushIntervalFrames) return;
            _lastFlushFrame = frame;
            _hasFlushed = true;
            // For indexed replays the archive has already closed this record's
            // independent deflate stream. For linear replays, flush the
            // deflate stream first so the file contains a readable prefix.
            _deflate?.Flush();
            _archive?.Flush(flushToDisk: terminal);
            if (_archive == null) _stream.Flush(flushToDisk: terminal);
        }
    }

    /// <summary>One recorded packet, read back from a replay file.</summary>
    internal readonly struct ReplayRecord
    {
        /// <summary>Simulation frames after the recording started.</summary>
        public readonly uint Frame;
        public readonly byte[] Data;

        public ReplayRecord(uint frame, byte[] data)
        {
            Frame = frame;
            Data = data;
        }
    }

    /// <summary>Reads a replay file's header, then its records in order.</summary>
    internal sealed class ReplayReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly DeflateStream? _deflate;
        private readonly ReplayArchive? _archive;
        private readonly byte[] _header = new byte[7];
        private uint _frame;

        public byte ProtocolVersion { get; }
        public byte FormatVersion { get; }
        internal System.Collections.Generic.IReadOnlyList<ReplayIndexEntry> Index => _archive?.Index ?? Array.Empty<ReplayIndexEntry>();
        internal uint LastFrame => _archive?.LastFrame ?? _frame;
        internal bool CanSeek => _archive != null;
        internal bool RecoveredTail => _archive?.RecoveredTail ?? false;
        internal ReplayRecord[]? Seek(uint frame, out uint restoredFrame)
        {
            restoredFrame = 0;
            return _archive?.Seek(frame, out restoredFrame);
        }

        /// <summary>Null if the file doesn't look like a replay at all (bad magic, wrong version, truncated header).</summary>
        public static ReplayReader? Open(string path)
        {
            FileStream? stream = null;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> header = stackalloc byte[ReplayFile.HeaderSize];
                if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false)
                    < header.Length)
                {
                    stream.Dispose();
                    return null;
                }
                if (!header[..ReplayFile.Magic.Length].SequenceEqual(ReplayFile.Magic)
                    || header[4] is not (ReplayFile.FormatVersion or ReplayFile.IndexedFormatVersion))
                {
                    stream.Dispose();
                    return null;
                }
                return new ReplayReader(stream, header[5], header[4]);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                stream?.Dispose();
                return null;
            }
        }

        private ReplayReader(FileStream stream, byte protocolVersion, byte formatVersion)
        {
            _stream = stream;
            FormatVersion = formatVersion;
            if (formatVersion == ReplayFile.IndexedFormatVersion) _archive = new ReplayArchive(stream, scan: true);
            else _deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
            ProtocolVersion = protocolVersion;
        }

        /// <summary>The next record, or null at end of file.</summary>
        public ReplayRecord? ReadNext()
        {
            if (_archive != null)
            {
                try { return _archive.ReadNext(); }
                catch (IOException) { return null; }
            }
            try
            {
                if (!Fill(_header.AsSpan(0, 1)))
                {
                    return null;
                }
                uint delta = _header[0];
                if (delta == ReplayFile.LongGap)
                {
                    if (!Fill(_header.AsSpan(0, 4)))
                    {
                        return null;
                    }
                    delta = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_header);
                }
                if (!Fill(_header.AsSpan(0, 2)))
                {
                    return null;
                }
                int length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_header);
                if (length is < 1 or > NetConfig.MaxPacketSize || uint.MaxValue - _frame < delta) { return null; }
                byte[] data = new byte[length];
                if (!Fill(data))
                {
                    return null;
                }
                _frame += delta;
                return new ReplayRecord(_frame, data);
            }
            catch (InvalidDataException)
            {
                // The deflate stream stops mid-block: the process that wrote
                // it did not get to close it. Everything up to the last flush
                // has already been handed over; treat the rest as the end.
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>True when the whole span was read; false at a clean or ragged end of file.</summary>
        private bool Fill(Span<byte> destination)
        {
            return _deflate!.ReadAtLeast(destination, destination.Length,
                throwOnEndOfStream: false) == destination.Length;
        }

        public void Dispose()
        {
            try
            {
                _archive?.Dispose();
                _deflate?.Dispose();
            }
            finally { _stream.Dispose(); }
        }
    }
}
