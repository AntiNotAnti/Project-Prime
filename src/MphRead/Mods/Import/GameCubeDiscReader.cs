using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.Import;

/// <summary>
/// Bounded read-only GameCube ISO/GCM filesystem source. Adapts the MIT-licensed
/// FST parsing approach in Second Hunt with strict bounds, path and cycle checks.
/// Nintendo game data never enters this assembly or an outgoing asset package.
/// </summary>
public sealed class GameCubeDiscReader : IDisposable
{
    public const uint Magic = 0xC2339F3D;
    public const int MaxFstBytes = 32 * 1024 * 1024;
    public const int MaxEntries = 100_000;
    private const int MaxDepth = 64;
    private const int MaxNameBytes = 255;

    private readonly Stream _source;
    private readonly bool _leaveOpen;
    private readonly object _gate = new();
    private readonly Dictionary<string, GameCubeDiscEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string GameId { get; }
    public string Title { get; }
    public byte DiscNumber { get; }
    public byte Revision { get; }
    public ExternalGameKind Game { get; }
    public IReadOnlyDictionary<string, GameCubeDiscEntry> Files => _files;

    public static GameCubeDiscReader Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string ext = Path.GetExtension(path);
        if (!ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".gcm", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".rvz", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only GameCube .iso/.gcm or bounded GameCube RVZ sources are supported.");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        Stream source = stream;
        try
        {
            if (ext.Equals(".rvz", StringComparison.OrdinalIgnoreCase))
                source = GameCubeRvzStream.Open(stream, leaveOpen: false);
            return new GameCubeDiscReader(source, leaveOpen: false);
        }
        catch { source.Dispose(); throw; }
    }

    public GameCubeDiscReader(Stream source, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("GameCube source must be readable and seekable.", nameof(source));
        _source = source;
        _leaveOpen = leaveOpen;
        if (source.Length < 0x440)
            throw new InvalidDataException("Truncated GameCube disc header.");

        Span<byte> header = stackalloc byte[0x440];
        ReadAt(0, header);
        if (Big32(header, 0x1c) != Magic)
            throw new InvalidDataException("Not an uncompressed GameCube disc. Wii/RVZ/WIA are not supported.");
        GameId = Encoding.ASCII.GetString(header[..6]);
        Game = GameId switch
        {
            "GM8E01" => ExternalGameKind.MetroidPrime,
            "G2ME01" => ExternalGameKind.Echoes,
            _ => throw new NotSupportedException("Unsupported source edition: " + GameId)
        };
        DiscNumber = header[6];
        Revision = header[7];
        ReadOnlySpan<byte> title = header.Slice(0x20, 0x3e0);
        int titleEnd = title.IndexOf((byte)0);
        Title = Encoding.ASCII.GetString(title[..(titleEnd < 0 ? title.Length : titleEnd)]);

        long fstOffset = Big32(header, 0x424);
        long fstSize = Big32(header, 0x428);
        if (fstOffset < 0x440 || fstSize < 12 || fstSize > MaxFstBytes ||
            fstOffset > source.Length || fstSize > source.Length - fstOffset)
            throw new InvalidDataException("Filesystem table lies outside source image or exceeds budget.");
        var fst = new byte[(int)fstSize];
        ReadAt(fstOffset, fst);
        ParseFst(fst);
    }

    private static uint Big32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));

    private void ReadAt(long offset, Span<byte> buffer)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (offset < 0 || offset > _source.Length || buffer.Length > _source.Length - offset)
                throw new InvalidDataException("Disc read outside local image bounds.");
            _source.Position = offset;
            _source.ReadExactly(buffer);
        }
    }

    private void ParseFst(byte[] fst)
    {
        uint root = Big32(fst, 0);
        long countValue = Big32(fst, 8);
        if ((root >> 24) == 0 || Big32(fst, 4) != 0 ||
            countValue < 1 || countValue > MaxEntries || countValue * 12 > fst.Length)
            throw new InvalidDataException("Invalid GameCube FST root.");
        int count = (int)countValue;
        int strings = checked(count * 12);

        string Name(int index)
        {
            uint header = Big32(fst, index * 12);
            int start = checked(strings + (int)(header & 0xffffff));
            if (start < strings || start >= fst.Length)
                throw new InvalidDataException("Filename outside FST strings.");
            int end = Array.IndexOf(fst, (byte)0, start);
            if (end < 0 || end == start || end - start > MaxNameBytes)
                throw new InvalidDataException("Invalid or unterminated FST filename.");
            for (int i = start; i < end; i++)
            {
                byte value = fst[i];
                if (value < 0x20 || value > 0x7e ||
                    value == (byte)'/' || value == (byte)'\\' || value == (byte)':')
                    throw new InvalidDataException("Unsafe path component in FST.");
            }
            string name = Encoding.ASCII.GetString(fst, start, end - start);
            if (name is "." or "..")
                throw new InvalidDataException("FST path traversal component.");
            return name;
        }

        void Walk(int first, int last, int parent, string prefix, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidDataException("FST nesting limit exceeded.");
            for (int index = first; index < last;)
            {
                int at = checked(index * 12);
                uint info = Big32(fst, at);
                string name = Name(index);
                if ((info >> 24) != 0)
                {
                    long directoryParent = Big32(fst, at + 4);
                    long next = Big32(fst, at + 8);
                    if (directoryParent != parent || next <= index || next > last)
                        throw new InvalidDataException("FST directory parent or range invalid.");
                    Walk(index + 1, (int)next, index, prefix + name + "/", depth + 1);
                    index = (int)next;
                }
                else
                {
                    long offset = Big32(fst, at + 4);
                    long length = Big32(fst, at + 8);
                    if (offset > _source.Length || length > _source.Length - offset)
                        throw new InvalidDataException("FST file outside disc image.");
                    string path = prefix + name;
                    if (!_files.TryAdd(path, new GameCubeDiscEntry(path, offset, length)))
                        throw new InvalidDataException("Duplicate FST path: " + path);
                    index++;
                }
            }
        }
        Walk(1, count, 0, "", 0);
    }

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string value = path.Replace('\\', '/');
        if (value.StartsWith('/') || value.Split('/').Any(p => p is "" or "." or ".."))
            throw new ArgumentException("Relative, normalized disc file path required.", nameof(path));
        return value;
    }

    public bool TryGetFile(string path, out GameCubeDiscEntry entry) =>
        _files.TryGetValue(Normalize(path), out entry);

    public Stream OpenRead(string path)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!TryGetFile(path, out var entry))
                throw new FileNotFoundException("Missing file in local disc image: " + path);
            return new DiscFileView(this, entry);
        }
    }

    public byte[] ReadSmallFile(string path, int maxBytes = 16 * 1024 * 1024)
    {
        using Stream window = OpenRead(path);
        if (maxBytes < 0 || window.Length > maxBytes)
            throw new InvalidDataException("File exceeds bounded small-file read limit.");
        byte[] result = new byte[(int)window.Length];
        window.ReadExactly(result);
        return result;
    }

    public string Sha1(string path)
    {
        using Stream window = OpenRead(path);
        // SHA-1 is for matching an upstream source recipe, not cryptographic trust.
        return Convert.ToHexString(SHA1.HashData(window)).ToLowerInvariant();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_leaveOpen) _source.Dispose();
        }
    }

    private sealed class DiscFileView : Stream
    {
        private readonly GameCubeDiscReader _owner;
        private readonly GameCubeDiscEntry _entry;
        private long _position;

        public DiscFileView(GameCubeDiscReader owner, GameCubeDiscEntry entry)
        { _owner = owner; _entry = entry; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _entry.Length;
        public override long Position
        {
            get => _position;
            set => _position = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || _position >= Length) return 0;
            int take = (int)Math.Min(buffer.Length, Length - _position);
            _owner.ReadAt(_entry.Offset + _position, buffer[..take]);
            _position += take;
            return take;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return _position;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException("Read-only source.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read-only source.");
    }
}

public enum ExternalGameKind { MetroidPrime, Echoes }
public readonly record struct GameCubeDiscEntry(string Path, long Offset, long Length);
public readonly record struct ExternalArchiveRequirement(string Path, string ExpectedSha1);
public enum ExternalArchiveStatus { Verified, Missing, Incompatible }
public readonly record struct ExternalArchiveCheck(
    string Path, ExternalArchiveStatus Status, string ExpectedSha1, string? ActualSha1);
public enum ExternalSourceEdition { OriginalRecipe, PrimeUsaRevision2 }

/// <summary>Pinned original and independently qualified edition archives; never a network asset license.</summary>
public static class ExternalSourceRequirements
{
    private static readonly ExternalArchiveRequirement[] Prime =
    {
        new("Metroid1.pak", "1ae13e2325ed8b57462f7a39d9d7fac035745598"),
        new("AudioGrp.pak", "157e0841155cc7b699176dd7afc746c7048dbc28")
    };
    private static readonly ExternalArchiveRequirement[] Echoes =
    {
        new("Metroid1.pak", "6b9a47578f41b2c64a12f1a0bf4e5cd783692124"),
        new("Metroid5.pak", "b3375e83c5290d35d6c83f35b04b7f60f6fb8ea7"),
        new("Metroid6.pak", "b8b92af37dc772ecd8234fb1ce664be0493188f6")
    };
    // Independently measured from the user's USA revision 2 source. Do not
    // substitute this archive for the original recipe or accept arbitrary
    // revisions because one of their resources happens to match.
    private static readonly ExternalArchiveRequirement[] PrimeUsaRevision2 =
    {
        new("Metroid1.pak", "2147a5ea1ff654d6bbce749d5f7c1170f98a601e"),
        new("AudioGrp.pak", "157e0841155cc7b699176dd7afc746c7048dbc28")
    };

    public static ExternalSourceEdition EditionFor(GameCubeDiscReader disc)
    {
        ArgumentNullException.ThrowIfNull(disc);
        return disc.GameId == "GM8E01" && disc.Revision == 2 && disc.DiscNumber == 0
            ? ExternalSourceEdition.PrimeUsaRevision2 : ExternalSourceEdition.OriginalRecipe;
    }

    public static IReadOnlyList<ExternalArchiveRequirement> For(GameCubeDiscReader disc)
    {
        ExternalSourceEdition edition = EditionFor(disc);
        return For(disc.Game, edition);
    }

    public static IReadOnlyList<ExternalArchiveRequirement> For(ExternalGameKind game, ExternalSourceEdition edition)
        => edition switch
        {
            ExternalSourceEdition.OriginalRecipe => For(game),
            ExternalSourceEdition.PrimeUsaRevision2 when game == ExternalGameKind.MetroidPrime
                => Array.AsReadOnly(PrimeUsaRevision2),
            _ => throw new NotSupportedException("Unrecognized external source edition.")
        };

    public static IReadOnlyList<ExternalArchiveRequirement> For(ExternalGameKind game) => game switch
    {
        ExternalGameKind.MetroidPrime => Array.AsReadOnly(Prime),
        ExternalGameKind.Echoes => Array.AsReadOnly(Echoes),
        _ => throw new NotSupportedException("Unrecognized external game.")
    };

    public static ExternalArchiveCheck Verify(GameCubeDiscReader disc, ExternalArchiveRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(disc);
        if (requirement.ExpectedSha1 is not { Length: 40 } ||
            !requirement.ExpectedSha1.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected source SHA-1 must contain 40 hex characters.");
        if (!disc.TryGetFile(requirement.Path, out _))
            return new(requirement.Path, ExternalArchiveStatus.Missing, requirement.ExpectedSha1, null);
        string actual = disc.Sha1(requirement.Path);
        return new(requirement.Path, actual.Equals(requirement.ExpectedSha1, StringComparison.OrdinalIgnoreCase)
            ? ExternalArchiveStatus.Verified : ExternalArchiveStatus.Incompatible, requirement.ExpectedSha1, actual);
    }

    public static IReadOnlyList<ExternalArchiveCheck> VerifyKnown(GameCubeDiscReader disc) =>
        For(disc).Select(requirement => Verify(disc, requirement)).ToArray();
}
