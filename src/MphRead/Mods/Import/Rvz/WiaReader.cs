using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.Import.Rvz
{
    internal enum WiaCompression { None = 0, Purge = 1, Bzip2 = 2, Lzma = 3, Lzma2 = 4, Zstd = 5 }

    // A Wii hash that differs from the one recomputed from the data (a WIA/RVZ "exception"): Sector counts from the
    // partition's first data sector, Offset is inside that sector's 0x400-byte hash block.
    internal readonly record struct WiaHashException(long Sector, int Offset, byte[] Hash);

    // A WIA or RVZ disc image (WIA is wit's format by Dirk Clemens; RVZ is Dolphin's extension of it), read in place
    // over a seekable stream. Clean-room: written only from the format description docs/WiaAndRvz.md in the
    // dolphin-emu repository (github.com/dolphin-emu/dolphin; the description was read, no code was taken).
    //
    // The disc is split into raw data regions (everything outside Wii partition data; all of a GameCube disc) and Wii
    // partitions, whose data is stored DECRYPTED and without the 0x400-byte hash block of each 0x8000-byte sector (so
    // no key is needed). Both are cut into groups of chunk_size disc bytes, each compressed on its own (NONE, PURGE,
    // BZIP2, LZMA, LZMA2; RVZ adds Zstandard); RVZ can also replace junk padding with a generator seed (RvzPacking).
    // As an IDiscBlob this is the plain disc image, except that Wii partition data is read through Partitions[i]
    // (decrypted) instead: re-encrypting it into the original image is not implemented (nothing needs it).
    // CPP08 private GC-only derivative: GameCubeRvzStream serializes access; no parallel
    // decode path. The cache keys every decode input, including initialized length,
    // logical offset/junk phase and exception context. Wii entry points reject.
    internal sealed class WiaReader : IDiscBlob, IDisposable
    {
        public const uint WiaMagic = 0x57494101;   // "WIA\x01"
        public const uint RvzMagic = 0x52565A01;   // "RVZ\x01"
        const uint WiaVersion = 0x01000000, WiaReadCompatible = 0x00080000;
        const uint RvzVersion = 0x01000000, RvzReadCompatible = 0x00030000;
        internal const int SectorSize = 0x8000, SectorData = 0x7C00, HashBlock = 0x400;
        const int SectorsPerExceptionList = 64;   // one list per 2 MiB of Wii sectors
        const int CacheSlots = 16;

        struct RawRegion
        {
            public long Offset, End, AlignedStart;
            public int GroupIndex, GroupCount;
        }

        internal struct PartRange
        {
            public long FirstSector, SectorCount;   // FirstSector relative to the partition's first data sector
            public int GroupIndex, GroupCount;
        }

        struct GroupEntry
        {
            public long Offset;
            public int Size;
            public bool Compressed;
            public int PackedSize;
        }

        sealed class CacheEntry
        {
            public int Group = -1;
            public byte[] Data = Array.Empty<byte>();
            public int Length, Lists;
            public long DataOffset, FirstSector;
            public WiaHashException[] Exceptions = Array.Empty<WiaHashException>();
            public long Stamp;
        }

        // CPP08 private GC-only bounds; donor snapshot and MIT notice beside this file.
        internal const long MaxDiscBytes = 1459978240;
        internal const int MaxChunkBytes = 2 * 1024 * 1024, MaxStoredBytes = 4 * 1024 * 1024;
        internal const int MaxRawRegions = 1024, MaxGroups = 65536;
        readonly long _physicalLength;
        bool _disposed;
        readonly Stream _s;
        readonly bool _leaveOpen;
        readonly object _fileLock = new();
        readonly object _cacheLock = new();
        readonly CacheEntry[] _cache = new CacheEntry[CacheSlots];
        long _clock;
        readonly byte[] _dhead;
        readonly byte[] _comprData;
        readonly RawRegion[] _raw;
        readonly GroupEntry[] _groups;
        readonly int[] _decodedLengths;
        readonly WiaPartition[] _parts;

        [ThreadStatic] static ZstdDecoder? t_zstd;
        [ThreadStatic] static JunkGenerator? t_junk;

        public bool IsRvz { get; }
        public uint Version { get; }
        public uint VersionCompatible { get; }
        public DiscPlatform Platform { get; }
        public WiaCompression Compression { get; }
        public int CompressionLevel { get; }
        public int ChunkSize { get; }
        public long Size { get; }                       // the original disc image's size
        public long FileSize { get; }
        public string GameId => Encoding.ASCII.GetString(_dhead, 0, 6);
        public IReadOnlyList<WiaPartition> Partitions => _parts;
        public int GroupCount => _groups.Length;

        // true when the stream starts like a WIA or RVZ file (the position is restored)
        public static bool Detect(Stream s)
        {
            if (!s.CanSeek || s.Length < 0x48) return false;
            long pos = s.Position;
            try
            {
                Span<byte> b = stackalloc byte[4];
                s.Seek(0, SeekOrigin.Begin);
                s.ReadExactly(b);
                uint m = BinaryPrimitives.ReadUInt32BigEndian(b);
                return m == WiaMagic || m == RvzMagic;
            }
            finally { s.Position = pos; }
        }

        public WiaReader(Stream s, bool leaveOpen = true)
        {
            if (!s.CanSeek) throw new ArgumentException("a disc image needs a seekable stream", nameof(s));
            if (!s.CanRead || s.Length < 0x124 || s.Length > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("GC RVZ physical source size exceeds bounds.");
            _physicalLength = s.Length;
            _s = s; _leaveOpen = leaveOpen;
            for (int i = 0; i < CacheSlots; i++) _cache[i] = new CacheEntry();

            // ---- wia_file_head_t (0x48 bytes)
            byte[] head = ReadFile(0, 0x48);
            uint magic = Be.U32(head, 0);
            if (magic != WiaMagic && magic != RvzMagic) throw new InvalidDataException("not a WIA or RVZ file");
            IsRvz = magic == RvzMagic;
            if (!IsRvz) throw new NotSupportedException("This adapter accepts GameCube RVZ only, not WIA.");
            Version = Be.U32(head, 4);
            VersionCompatible = Be.U32(head, 8);
            uint version = IsRvz ? RvzVersion : WiaVersion, readCompatible = IsRvz ? RvzReadCompatible : WiaReadCompatible;
            if (VersionCompatible > version || Version < readCompatible)
            {
                throw new NotSupportedException($"{(IsRvz ? "RVZ" : "WIA")} version 0x{Version:X8} (compatible 0x{VersionCompatible:X8}) is not supported");
            }
            if (!SHA1.HashData(head.AsSpan(0, 0x34)).AsSpan().SequenceEqual(head.AsSpan(0x34, 20)))
            {
                throw new InvalidDataException("WIA/RVZ header hash mismatch (damaged file)");
            }
            int discSize = checked((int)Be.U32(head, 0x0C));
            if (discSize < 0x90 || discSize > 0x1000) throw new InvalidDataException("bad wia_disc_t size");
            Size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(0x24)));
            FileSize = checked((long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(0x2C)));
            if (FileSize != _physicalLength || Size < 0x440 || Size > MaxDiscBytes)
                throw new InvalidDataException("GC RVZ declared physical/logical size mismatch or budget exceeded.");

            // ---- wia_disc_t (right after; zero-padded when an older writer stored less)
            byte[] discRaw = ReadFile(0x48, discSize);
            if (!SHA1.HashData(discRaw).AsSpan().SequenceEqual(head.AsSpan(0x10, 20)))
            {
                throw new InvalidDataException("WIA/RVZ disc header hash mismatch (damaged file)");
            }
            var disc = new byte[Math.Max(discSize, 0xDC)];
            discRaw.CopyTo(disc, 0);
            uint discType = Be.U32(disc, 0x00);
            Platform = discType switch
            {
                1 => DiscPlatform.GameCube,
                2 => DiscPlatform.Wii,
                _ => throw new InvalidDataException($"WIA/RVZ: unknown disc type {discType}"),
            };
            if (Platform != DiscPlatform.GameCube)
                throw new NotSupportedException("Wii/Trilogy/Corruption RVZ editions need their existing separate edition owner; not GameCube.");
            uint comp = Be.U32(disc, 0x04);
            if (comp > 5 || (comp == 5 && !IsRvz) || (comp == 1 && IsRvz)) throw new InvalidDataException($"WIA/RVZ: bad compression {comp}");
            Compression = (WiaCompression)comp;
            if (Compression is not WiaCompression.None and not WiaCompression.Zstd)
                throw new NotSupportedException("This bounded GC RVZ slice supports None/Zstandard only.");
            CompressionLevel = Be.I32(disc, 0x08);
            ChunkSize = Be.I32(disc, 0x0C);
            bool chunkOk = IsRvz
                ? ChunkSize >= 0x8000 && (ChunkSize < 0x200000 ? (ChunkSize & (ChunkSize - 1)) == 0 : ChunkSize % 0x200000 == 0)
                : ChunkSize >= 0x200000 && ChunkSize % 0x200000 == 0;
            if (!chunkOk || ChunkSize > MaxChunkBytes) throw new InvalidDataException($"WIA/RVZ: bad chunk size 0x{ChunkSize:X}");
            _dhead = disc.AsSpan(0x10, 0x80).ToArray();
            int nPart = checked((int)Be.U32(disc, 0x90));
            int partTSize = checked((int)Be.U32(disc, 0x94));
            long partOff = checked((long)BinaryPrimitives.ReadUInt64BigEndian(disc.AsSpan(0x98)));
            int nRaw = checked((int)Be.U32(disc, 0xB4));
            long rawOff = checked((long)BinaryPrimitives.ReadUInt64BigEndian(disc.AsSpan(0xB8)));
            int rawSize = checked((int)Be.U32(disc, 0xC0));
            int nGroups = checked((int)Be.U32(disc, 0xC4));
            long groupOff = checked((long)BinaryPrimitives.ReadUInt64BigEndian(disc.AsSpan(0xC8)));
            int groupSize = checked((int)Be.U32(disc, 0xD0));
            int comprLen = Math.Min((int)disc[0xD4], 7);
            _comprData = disc.AsSpan(0xD5, comprLen).ToArray();
            if (Compression == WiaCompression.Lzma && _comprData.Length < 5) throw new InvalidDataException("WIA: missing LZMA properties");
            if (nPart != 0 || nRaw < 1 || nRaw > MaxRawRegions || nGroups < 1 || nGroups > MaxGroups)
            {
                throw new InvalidDataException("WIA/RVZ: implausible table sizes");
            }

            // ---- wia_part_t[] (stored uncompressed, hashed)
            _parts = new WiaPartition[nPart];
            if (nPart > 0)
            {
                if (partTSize < 16 || partTSize > 0x1000) throw new InvalidDataException("WIA/RVZ: bad partition entry size");
                byte[] pt = ReadFile(partOff, nPart * partTSize);
                if (!SHA1.HashData(pt).AsSpan().SequenceEqual(disc.AsSpan(0xA0, 20)))
                {
                    throw new InvalidDataException("WIA/RVZ partition table hash mismatch (damaged file)");
                }
                for (int i = 0; i < nPart; i++)
                {
                    var e = new byte[Math.Max(partTSize, 0x30)];
                    Buffer.BlockCopy(pt, i * partTSize, e, 0, partTSize);
                    long first0 = Be.U32(e, 0x10);
                    var ranges = new PartRange[2];
                    for (int k = 0; k < 2; k++)
                    {
                        int o = 0x10 + k * 16;
                        ranges[k] = new PartRange
                        {
                            FirstSector = Be.U32(e, o) - first0,
                            SectorCount = Be.U32(e, o + 4),
                            GroupIndex = (int)Be.U32(e, o + 8),
                            GroupCount = (int)Be.U32(e, o + 12),
                        };
                    }
                    _parts[i] = new WiaPartition(this, i, e.AsSpan(0, 16).ToArray(), first0, ranges);
                }
            }

            // ---- wia_raw_data_t[] (compressed)
            byte[] rawTable = ReadTable(rawOff, rawSize, checked(nRaw * 24));
            _raw = new RawRegion[nRaw];
            for (int i = 0; i < nRaw; i++)
            {
                int o = i * 24;
                long off = checked((long)BinaryPrimitives.ReadUInt64BigEndian(rawTable.AsSpan(o)));
                long size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(rawTable.AsSpan(o + 8)));
                // groups start at the sector boundary at or before the region (the first region starts at 0x80)
                _raw[i] = new RawRegion
                {
                    Offset = off,
                    End = checked(off + size),
                    AlignedStart = off - off % SectorSize,
                    GroupIndex = (int)Be.U32(rawTable, o + 16),
                    GroupCount = (int)Be.U32(rawTable, o + 20),
                };
            }
            Array.Sort(_raw, (a, b) => a.Offset.CompareTo(b.Offset));

            // ---- wia_group_t[] / rvz_group_t[] (compressed)
            int entrySize = IsRvz ? 12 : 8;
            byte[] groupTable = ReadTable(groupOff, groupSize, checked(nGroups * entrySize));
            _groups = new GroupEntry[nGroups];
            for (int i = 0; i < nGroups; i++)
            {
                int o = i * entrySize;
                uint size = Be.U32(groupTable, o + 4);
                _groups[i] = new GroupEntry
                {
                    Offset = (long)Be.U32(groupTable, o) << 2,
                    Size = (int)(size & 0x7FFFFFFF),
                    Compressed = IsRvz ? (size & 0x80000000) != 0 : Compression > WiaCompression.Purge,
                    PackedSize = IsRvz ? (int)Be.U32(groupTable, o + 8) : 0,
                };
            }
            _decodedLengths = new int[_groups.Length];
            long priorEnd = 0x80;
            foreach (RawRegion r in _raw)
            {
                CheckGroups(r.GroupIndex, r.GroupCount);
                if (r.Offset < priorEnd || r.Offset < 0x80 || r.End <= r.Offset || r.End > Size
                    || r.GroupCount != (r.End - r.AlignedStart + ChunkSize - 1) / ChunkSize)
                    throw new InvalidDataException("GC RVZ raw region outside logical disc, overlapping, or wrong group coverage.");
                for (int i = 0; i < r.GroupCount; i++)
                {
                    int length = checked((int)Math.Min(ChunkSize, r.End - (r.AlignedStart + (long)i * ChunkSize)));
                    _decodedLengths[r.GroupIndex + i] = Math.Max(_decodedLengths[r.GroupIndex + i], length);
                }
                priorEnd = r.End;
            }
            foreach (GroupEntry g in _groups)
            {
                if (g.Offset < 0 || g.Size < 0 || g.Size > MaxStoredBytes || g.Offset > _physicalLength
                    || g.Size > _physicalLength - g.Offset || g.PackedSize < 0 || g.PackedSize > MaxStoredBytes)
                    throw new InvalidDataException("GC RVZ stored or packed group exceeds source/buffer budget.");
            }
            foreach (WiaPartition p in _parts)
            {
                foreach (PartRange r in p.Ranges) CheckGroups(r.GroupIndex, r.GroupCount);
            }
        }

        void CheckGroups(int index, int count)
        {
            if (index < 0 || count < 0 || (long)index + count > _groups.Length) throw new InvalidDataException("WIA/RVZ: group index out of range");
        }

        byte[] ReadFile(long offset, int size)
        {
            ValidateFileRange(offset, size);
            var b = new byte[size];
            ReadFile(offset, b);
            return b;
        }

        void ValidateFileRange(long offset, int length)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_s.Length != _physicalLength || length < 0 || length > MaxStoredBytes
                || offset < 0 || offset > _physicalLength || length > _physicalLength - offset)
                throw new InvalidDataException("GC RVZ opened source changed length or read exceeds bounds.");
        }

        void ReadFile(long offset, Span<byte> dst)
        {
            lock (_fileLock)
            {
                ValidateFileRange(offset, dst.Length);
                _s.Seek(offset, SeekOrigin.Begin);
                _s.ReadExactly(dst);
                ValidateFileRange(offset, dst.Length);
            }
        }

        // the raw-data and group tables: compressed with the file's method (PURGE: its segment format)
        byte[] ReadTable(long offset, int storedSize, int size)
        {
            if (size < 0 || size > MaxGroups * 12 || storedSize < 0 || storedSize > MaxStoredBytes)
                throw new InvalidDataException("GC RVZ table exceeds budget before allocation.");
            byte[] stored = ReadFile(offset, storedSize);
            if (Compression == WiaCompression.None) return stored.Length == size ? stored : throw new InvalidDataException("WIA/RVZ: table truncated");
            var result = new byte[size];
            if (Compression == WiaCompression.Purge)
            {
                Unpurge(stored, result);
                return result;
            }
            var tmp = new byte[size + 64];
            int n = Decompress(stored, tmp);
            if (n != size) throw new InvalidDataException("WIA/RVZ: table decompressed short");
            Buffer.BlockCopy(tmp, 0, result, 0, size);
            return result;
        }

        int Decompress(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            switch (Compression)
            {
                case WiaCompression.Zstd: return (t_zstd ??= new ZstdDecoder()).Decompress(src, dst);
                default: throw new InvalidOperationException();
            }
        }

        // PURGE: segments (u32 offset, u32 size, data) over a zeroed output, then a SHA-1 (not checked here)
        static void Unpurge(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            dst.Clear();
            int p = 0, end = src.Length - 20;
            if (end < 0) throw new InvalidDataException("WIA purge data too short");
            while (p < end)
            {
                if (p + 8 > end) throw new InvalidDataException("WIA purge segment truncated");
                uint off = BinaryPrimitives.ReadUInt32BigEndian(src.Slice(p));
                uint size = BinaryPrimitives.ReadUInt32BigEndian(src.Slice(p + 4));
                p += 8;
                if (p + (long)size > end || off + (long)size > dst.Length) throw new InvalidDataException("WIA purge segment out of range");
                src.Slice(p, (int)size).CopyTo(dst.Slice((int)off));
                p += (int)size;
            }
        }

        // ---- reads: cut logical requests into bounded serial group pieces

        // one group's share of a read
        readonly struct Piece
        {
            public readonly int Group, DataSize, Lists, Within, Length, DstOffset;
            public readonly long DataOffset, FirstSector;

            public Piece(int group, long dataOffset, int dataSize, int lists, long firstSector, int within, int length, int dstOffset)
            {
                Group = group; DataOffset = dataOffset; DataSize = dataSize; Lists = lists; FirstSector = firstSector;
                Within = within; Length = length; DstOffset = dstOffset;
            }
        }


        // ---- the disc view (IDiscBlob): header, raw regions; Wii partition data is not readable here
        public void Read(long offset, Span<byte> dst)
        {
            ValidateFileRange(0, 0);
            if (offset < 0 || offset > Size || dst.Length > Size - offset)
                throw new InvalidDataException("GC RVZ logical read outside disc.");
            var pieces = new List<Piece>();
            int pos = 0;
            while (pos < dst.Length)
            {
                long at = offset + pos;
                int n;
                if (at >= Size)
                {
                    dst.Slice(pos).Clear();
                    break;
                }
                if (at < 0x80)
                {
                    n = (int)Math.Min(dst.Length - pos, 0x80 - at);
                    _dhead.AsSpan((int)at, n).CopyTo(dst.Slice(pos));
                }
                else if (FindRaw(at, out RawRegion r))
                {
                    long rel = at - r.AlignedStart;
                    int g = (int)(rel / ChunkSize);
                    int within = (int)(rel % ChunkSize);
                    if (g >= r.GroupCount) throw new InvalidDataException("WIA/RVZ: raw data offset past its groups");
                    long groupStart = r.AlignedStart + (long)g * ChunkSize;
                    int groupLen = (int)Math.Min(ChunkSize, r.End - groupStart);
                    n = (int)Math.Min(dst.Length - pos, Math.Min(groupLen - within, r.End - at));
                    pieces.Add(new Piece(r.GroupIndex + g, groupStart, groupLen, 0, 0, within, n, pos));
                }
                else
                {
                    long next = Size;
                    foreach (WiaPartition p in _parts)
                    {
                        if (at >= p.DiscOffset && at < p.DiscOffset + p.SectorCount * SectorSize)
                        {
                            throw new NotSupportedException("WIA/RVZ: Wii partition data is stored decrypted; read it through Partitions[i]");
                        }
                        if (p.DiscOffset > at) next = Math.Min(next, p.DiscOffset);
                    }
                    foreach (RawRegion q in _raw)
                    {
                        if (q.Offset > at) next = Math.Min(next, q.Offset);
                    }
                    n = (int)Math.Min(dst.Length - pos, next - at);   // a gap no region covers reads as zeros
                    dst.Slice(pos, n).Clear();
                }
                pos += n;
            }
            Fetch(pieces, dst);
        }

        bool FindRaw(long offset, out RawRegion region)
        {
            int lo = 0, hi = _raw.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_raw[mid].End <= offset) lo = mid + 1;
                else if (_raw[mid].Offset > offset) hi = mid - 1;
                else
                {
                    region = _raw[mid];
                    return true;
                }
            }
            region = default;
            return false;
        }

        // ---- Wii partition data (decrypted, 0x7C00 bytes per disc sector)
        internal void ReadPartition(WiaPartition p, long offset, Span<byte> dst)
            => throw new NotSupportedException("GameCube-only RVZ adapter has no Wii partition reader.");

        void Fetch(List<Piece> pieces, Span<byte> dst)
        {
            // GC-only adapter deliberately has no parallel decode/task/run buffer path.
            foreach (Piece pc in pieces)
                CopyFromGroup(pc.Group, pc.DataOffset, pc.DataSize, pc.Lists, pc.FirstSector,
                    pc.Within, dst.Slice(pc.DstOffset, pc.Length));
        }

        // the hash exceptions of the partition's sectors [first, first + count)
        internal List<WiaHashException> PartitionExceptions(WiaPartition p, long first, long count)
            => throw new NotSupportedException("GameCube-only RVZ adapter has no Wii hash/key path.");

        // Cached groups are only read or replaced under _cacheLock, so a buffer is never recycled mid-copy.
        void CopyFromGroup(int group, long dataOffset, int dataSize, int lists, long firstSector, int within, Span<byte> dst)
        {
            if (within < 0 || within > dataSize || dst.Length > dataSize - within)
                throw new InvalidDataException("GC RVZ copy exceeds initialized decoded group length.");
            lock (_cacheLock)
            {
                CacheEntry? hit = FindCached(group, dataOffset, dataSize, lists, firstSector);
                if (hit != null)
                {
                    hit.Data.AsSpan(within, dst.Length).CopyTo(dst);
                    return;
                }
            }
            byte[] data = DecodeToBuffer(group, dataOffset, dataSize, lists, firstSector, out WiaHashException[] exc);
            lock (_cacheLock)
            {
                CacheEntry e = AddCached(group, dataOffset, data, dataSize, lists, firstSector, exc);
                e.Data.AsSpan(within, dst.Length).CopyTo(dst);
            }
        }

        CacheEntry? FindCached(int group, long dataOffset, int length, int lists, long firstSector)
        {
            foreach (CacheEntry e in _cache)
            {
                if (e.Group == group && e.DataOffset == dataOffset && e.Length == length
                    && e.Lists == lists && e.FirstSector == firstSector)
                {
                    e.Stamp = ++_clock;
                    return e;
                }
            }
            return null;
        }

        CacheEntry AddCached(int group, long dataOffset, byte[] data, int length, int lists, long firstSector, WiaHashException[] exc)
        {
            CacheEntry victim = _cache[0];
            foreach (CacheEntry e in _cache)
            {
                if (e.Group == group && e.DataOffset == dataOffset && e.Length == length
                    && e.Lists == lists && e.FirstSector == firstSector)
                {
                    // another thread decoded it meanwhile
                    ArrayPool<byte>.Shared.Return(data);
                    e.Stamp = ++_clock;
                    return e;
                }
                if (e.Stamp < victim.Stamp) victim = e;
            }
            if (victim.Data.Length > 0) ArrayPool<byte>.Shared.Return(victim.Data);
            victim.Group = group;
            victim.Data = data;
            victim.Length = length; victim.DataOffset = dataOffset;
            victim.Lists = lists; victim.FirstSector = firstSector;
            victim.Exceptions = exc;
            victim.Stamp = ++_clock;
            return victim;
        }

        // storedBuf/storedOff: the group's stored bytes when already read (else they are read here)
        byte[] DecodeToBuffer(int group, long dataOffset, int dataSize, int lists, long firstSector, out WiaHashException[] exc,
            byte[]? storedBuf = null, int storedOff = 0)
        {
            byte[] data = ArrayPool<byte>.Shared.Rent(dataSize);
            try
            {
                exc = DecodeGroup(group, dataOffset, dataSize, lists, firstSector, data, storedBuf, storedOff);
                return data;
            }
            catch (InvalidDataException e)
            {
                ArrayPool<byte>.Shared.Return(data);
                GroupEntry g = _groups[group];
                throw new InvalidDataException($"WIA/RVZ group {group} (file offset 0x{g.Offset:X}, {g.Size} bytes): {e.Message}", e);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(data);
                throw;
            }
        }

        WiaHashException[] DecodeGroup(int group, long dataOffset, int dataSize, int lists, long firstSector, byte[] outData,
            byte[]? storedBuf, int storedOff)
        {
            GroupEntry g = _groups[group];
            int expected = _decodedLengths[group];
            if (lists != 0 || firstSector != 0 || dataSize <= 0 || dataSize > expected || expected > ChunkSize)
                throw new InvalidDataException("GC RVZ decode context exceeds validated raw group coverage.");
            Span<byte> output = outData.AsSpan(0, dataSize);
            if (g.Size == 0)
            {
                output.Clear();
                return Array.Empty<WiaHashException>();
            }
            byte[]? stored = null;
            byte[]? inflated = null;
            byte[]? restored = null;
            try
            {
                ReadOnlySpan<byte> payload;
                if (storedBuf != null)
                {
                    payload = storedBuf.AsSpan(storedOff, g.Size);
                }
                else
                {
                    stored = ArrayPool<byte>.Shared.Rent(g.Size);
                    ReadFile(g.Offset, stored.AsSpan(0, g.Size));
                    payload = stored.AsSpan(0, g.Size);
                }
                bool padded = true;
                if (g.Compressed)
                {
                    // Aliased raw regions can request different initialized prefixes.
                    // Decode the complete validated physical group's logical/packed payload.
                    int expectedStored = g.PackedSize == 0 ? expected : g.PackedSize;
                    int cap = checked(expectedStored + 64); // donor Zstd scratch allowance
                    inflated = ArrayPool<byte>.Shared.Rent(cap);
                    int n = Decompress(payload, inflated.AsSpan(0, cap));
                    if (n != expectedStored) throw new InvalidDataException("GC RVZ inflated group length differs from validated expected length.");
                    payload = inflated.AsSpan(0, n);
                    padded = false;
                }
                int p = 0;
                WiaHashException[] exc = Array.Empty<WiaHashException>();
                if (lists > 0)
                {
                    // wia_except_list_t[lists]: u16 count, then (u16 offset among the hash blocks, SHA-1) each
                    List<WiaHashException>? list = null;
                    for (int j = 0; j < lists; j++)
                    {
                        if (p + 2 > payload.Length) throw new InvalidDataException("WIA/RVZ: exception list truncated");
                        int count = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(p));
                        p += 2;
                        for (int k = 0; k < count; k++)
                        {
                            if (p + 22 > payload.Length) throw new InvalidDataException("WIA/RVZ: exception list truncated");
                            int off = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(p));
                            (list ??= new List<WiaHashException>()).Add(new WiaHashException(
                                firstSector + (long)j * SectorsPerExceptionList + off / HashBlock, off % HashBlock, payload.Slice(p + 2, 20).ToArray()));
                            p += 22;
                        }
                    }
                    if (list != null) exc = list.ToArray();
                    if (padded) p = (p + 3) & ~3;   // NONE / PURGE / uncompressed RVZ groups align the data to 4
                }
                ReadOnlySpan<byte> data = payload.Slice(Math.Min(p, payload.Length));
                if (!IsRvz && Compression == WiaCompression.Purge)
                {
                    Unpurge(data, output);
                }
                else if (g.PackedSize != 0)
                {
                    if (data.Length < g.PackedSize || data.Length - g.PackedSize > 3)
                        throw new InvalidDataException("GC RVZ packed payload size differs from declaration.");
                    data = data.Slice(0, g.PackedSize);
                    int restoredLength = RvzPacking.Measure(data, ChunkSize);
                    if (restoredLength != expected)
                        throw new InvalidDataException("GC RVZ packed restoration length differs from validated raw group coverage.");
                    Span<byte> complete = output;
                    if (expected != dataSize)
                    {
                        restored = ArrayPool<byte>.Shared.Rent(expected);
                        complete = restored.AsSpan(0, expected);
                    }
                    int used = RvzPacking.Unpack(data, complete, dataOffset, t_junk ??= new JunkGenerator());
                    if (used != data.Length) throw new InvalidDataException("GC RVZ packed payload not fully consumed.");
                    if (restored != null) complete.Slice(0, dataSize).CopyTo(output);
                }
                else
                {
                    if (data.Length < expected || data.Length - expected > 3)
                        throw new InvalidDataException($"GC RVZ group {group} length {data.Length} differs from validated expected {expected}");
                    data.Slice(0, dataSize).CopyTo(output);
                }
                return exc;
            }
            finally
            {
                if (stored != null) ArrayPool<byte>.Shared.Return(stored);
                if (inflated != null) ArrayPool<byte>.Shared.Return(inflated);
                if (restored != null) ArrayPool<byte>.Shared.Return(restored);
            }
        }


        // diagnostics: where group i is stored
        internal (long Offset, int Size, bool Compressed, int PackedSize) StoredGroup(int i)
            => (_groups[i].Offset, _groups[i].Size, _groups[i].Compressed, _groups[i].PackedSize);

        public void Dispose()
        {
            lock (_cacheLock)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (CacheEntry entry in _cache)
                {
                    if (entry.Data.Length != 0) ArrayPool<byte>.Shared.Return(entry.Data);
                    entry.Data = Array.Empty<byte>(); entry.Group = -1;
                }
            }
            if (!_leaveOpen) _s.Dispose();
        }
    }

    // One Wii partition of a WIA/RVZ file: its data, decrypted and without hashes (0x7C00 bytes per disc sector),
    // offset 0 = the partition's first data sector.
    internal sealed class WiaPartition : IDiscBlob
    {
        readonly WiaReader _r;
        readonly byte[] _key;
        internal readonly WiaReader.PartRange[] Ranges;

        internal WiaPartition(WiaReader r, int index, byte[] key, long firstSector, WiaReader.PartRange[] ranges)
        {
            _r = r; Index = index; _key = key; FirstSector = firstSector; Ranges = ranges;
            long end = 0;
            foreach (WiaReader.PartRange x in ranges)
            {
                if (x.SectorCount > 0) end = Math.Max(end, x.FirstSector + x.SectorCount);
            }
            SectorCount = end;
        }

        public int Index { get; }
        public long FirstSector { get; }                                 // disc sector of the first data sector
        public long DiscOffset => FirstSector * WiaReader.SectorSize;    // = partition offset + its data offset
        public long SectorCount { get; }
        public long Size => SectorCount * WiaReader.SectorData;

        // the partition's title key, decrypted (WIA/RVZ store it so a reader can re-encrypt)
        public byte[] TitleKey => throw new NotSupportedException("GameCube-only RVZ adapter has no Wii key path.");

        public void Read(long offset, Span<byte> dst) => _r.ReadPartition(this, offset, dst);

        // the stored hash exceptions for sectors [first, first + count)
        public IReadOnlyList<WiaHashException> HashExceptions(long first, long count) => _r.PartitionExceptions(this, first, count);
    }
}
