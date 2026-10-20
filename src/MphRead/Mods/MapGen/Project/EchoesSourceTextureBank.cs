using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Private, source-bound top-level RGBA storage. This owns no files, receipt,
/// renderer or source qualification. Surrounding import/asset owners supply
/// read-only streams and immutable-generation identities.
/// </summary>
public static class EchoesSourceTextureBank
{
    public const int HeaderBytes = 160;
    public const int EntryBytes = 112;
    public const int MaxEntries = 512;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxBankBytes = 64 * 1024 * 1024;
    private static readonly byte[] ContractHash = SHA256.HashData(Encoding.UTF8.GetBytes(RetroTextureSource.PixelContract));
    private static readonly byte[] GameId = "G2ME01"u8.ToArray();
    private static string ArchiveSha1 => ExternalSourceRequirements.For(ExternalGameKind.Echoes)
        .Single(r => r.Path == "Metroid6.pak").ExpectedSha1;

    public sealed record Source(uint MreaId, string MreaSha256);
    public sealed record Input(uint ResourceId, string SourceSha256, RetroTextureSource.Decoded Image);
    public sealed record Entry(uint ResourceId, uint Format, int Width, int Height,
        int SourceMipCount, int Offset, int Length, string SourceSha256, string RgbaSha256);
    public sealed class Bank
    {
        public Source Source { get; }
        public IReadOnlyList<Entry> Entries { get; }
        public int PayloadOffset { get; }
        public int Length { get; }
        public string Sha256 { get; }
        public string HeaderSha256 { get; }
        public string DirectorySha256 { get; }
        internal Bank(Source source, Entry[] entries, int payloadOffset, int length,
            string hash, string headerHash, string directoryHash)
        {
            Source = source; Entries = Array.AsReadOnly(entries); PayloadOffset = payloadOffset;
            Length = length; Sha256 = hash; HeaderSha256 = headerHash; DirectorySha256 = directoryHash;
        }
        public Entry RequireEntry(uint resource) => Entries.FirstOrDefault(e => e.ResourceId == resource)
            ?? throw new InvalidDataException("Texture is absent from the private source bank.");
    }

    /// <summary>Deterministic encoding; caller inputs and source pixels stay untouched.</summary>
    public static byte[] Encode(Source source, IReadOnlyList<Input> images)
    {
        CheckSource(source);
        ArgumentNullException.ThrowIfNull(images);
        int imageCount = images.Count;
        Require(imageCount is >= 1 and <= MaxEntries, "Source bank entry count exceeds its limit.");
        var sorted = new Input[imageCount];
        for (int i = 0; i < sorted.Length; i++) sorted[i] = images[i]
            ?? throw new InvalidDataException("Source bank image is missing.");
        Array.Sort(sorted, (a, b) => a.ResourceId.CompareTo(b.ResourceId));
        int payloadOffset = checked(HeaderBytes + sorted.Length * EntryBytes);
        long total = payloadOffset;
        var entries = new Entry[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            Input image = sorted[i];
            Require(image.ResourceId != 0 && (i == 0 || sorted[i - 1].ResourceId < image.ResourceId)
                && Hex(image.SourceSha256, 64), "Invalid or duplicate source bank identity.");
            RetroTextureSource.Decoded decoded = image.Image
                ?? throw new InvalidDataException("Source bank image is missing.");
            var layout = decoded.Layout;
            CheckImage(layout.Format, layout.Width, layout.Height, layout.MipCount, decoded.Pixels.Length);
            entries[i] = new(image.ResourceId, layout.Format, layout.Width, layout.Height,
                layout.MipCount, checked((int)(total - payloadOffset)), decoded.Pixels.Length,
                image.SourceSha256, Hash(decoded.Pixels));
            total = checked(total + decoded.Pixels.Length);
            Require(total <= MaxBankBytes, "Source bank exceeds the private component budget.");
        }
        var result = new byte[(int)total];
        Span<byte> header = result.AsSpan(0, HeaderBytes);
        "PPSB"u8.CopyTo(header);
        Put(header, 4, 1); Put(header, 8, HeaderBytes); Put(header, 12, entries.Length);
        Put(header, 16, EntryBytes); Put(header, 20, payloadOffset); Put(header, 24, result.Length);
        ContractHash.CopyTo(header[28..]); GameId.CopyTo(header[60..]);
        // Revision/disc bytes 66/67 and all reserved fields remain zero.
        Convert.FromHexString(ArchiveSha1).CopyTo(header[68..]);
        Put(header, 88, source.MreaId);
        Convert.FromHexString(source.MreaSha256).CopyTo(header[92..]);
        Span<byte> directory = result.AsSpan(HeaderBytes, entries.Length * EntryBytes);
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            Span<byte> row = directory.Slice(i * EntryBytes, EntryBytes);
            Put(row, 0, entry.ResourceId); Put(row, 4, entry.Format); Put(row, 8, entry.Width);
            Put(row, 12, entry.Height); Put(row, 16, entry.SourceMipCount); Put(row, 20, entry.Offset);
            Put(row, 24, entry.Length);
            Convert.FromHexString(entry.SourceSha256).CopyTo(row[32..]);
            Convert.FromHexString(entry.RgbaSha256).CopyTo(row[64..]);
            sorted[i].Image.Pixels.CopyTo(result.AsSpan(payloadOffset + entry.Offset, entry.Length));
        }
        SHA256.HashData(directory).CopyTo(header[124..]);
        return result;
    }

    /// <summary>
    /// Validates directory/ranges before reading the complete bounded hash.
    /// The returned identity owns no stream; the caller keeps its stream open.
    /// </summary>
    public static Bank Inspect(Stream input)
    {
        int length = CheckStream(input);
        input.Position = 0;
        byte[] header = new byte[HeaderBytes];
        input.ReadExactly(header);
        Require(header.AsSpan(0, 4).SequenceEqual("PPSB"u8) && U32(header, 4) == 1
            && U32(header, 8) == HeaderBytes && U32(header, 16) == EntryBytes
            && U32(header, 24) == length && Zero(header.AsSpan(156, 4)), "Invalid private source bank header.");
        Require(header.AsSpan(28, 32).SequenceEqual(ContractHash)
            && header.AsSpan(60, 6).SequenceEqual(GameId) && header[66] == 0 && header[67] == 0
            && header.AsSpan(68, 20).SequenceEqual(Convert.FromHexString(ArchiveSha1)),
            "Unsupported source bank pixel contract or source edition.");
        var source = new Source(U32(header, 88), Convert.ToHexStringLower(header.AsSpan(92, 32)));
        CheckSource(source);
        uint count = U32(header, 12);
        Require(count is >= 1 and <= MaxEntries, "Source bank entry count exceeds its limit.");
        int directoryBytes = checked((int)count * EntryBytes);
        int payloadOffset = HeaderBytes + directoryBytes;
        Require(U32(header, 20) == payloadOffset && payloadOffset < length, "Invalid source bank directory extent.");
        byte[] directory = new byte[directoryBytes];
        input.ReadExactly(directory);
        string directoryHash = Hash(directory);
        Require(SHA256.HashData(directory).AsSpan().SequenceEqual(header.AsSpan(124, 32)),
            "Source bank directory integrity failed.");
        var entries = new Entry[(int)count];
        long next = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            ReadOnlySpan<byte> row = directory.AsSpan(i * EntryBytes, EntryBytes);
            uint id = U32(row, 0), format = U32(row, 4);
            Require(id != 0 && (i == 0 || entries[i - 1].ResourceId < id)
                && Zero(row.Slice(28, 4)) && Zero(row[96..]), "Invalid source bank entry identity or flags.");
            int width = Int(row, 8), height = Int(row, 12), mips = Int(row, 16);
            int offset = Int(row, 20), bytes = Int(row, 24);
            CheckImage(format, width, height, mips, bytes);
            Require(offset == next && checked(payloadOffset + next + bytes) <= length,
                "Source bank image range overlaps, has a gap or is truncated.");
            next = checked(next + bytes);
            entries[i] = new(id, format, width, height, mips, offset, bytes,
                Convert.ToHexStringLower(row.Slice(32, 32)), Convert.ToHexStringLower(row.Slice(64, 32)));
        }
        Require(payloadOffset + next == length, "Unexpected source bank trailing data.");
        string completeHash = HashStream(input, length);
        return new(source, entries, payloadOffset, length, completeHash, Hash(header), directoryHash);
    }

    /// <summary>
    /// Reads only a selected image from the captured bank generation. Header,
    /// directory and selected pixels reverify on every read; unrelated image
    /// payloads need not be rehashed for each material sharing this bank.
    /// </summary>
    public static byte[] ReadImage(Stream input, Bank bank, uint resource)
    {
        ArgumentNullException.ThrowIfNull(bank);
        Require(CheckStream(input) == bank.Length, "Source bank changed length.");
        input.Position = 0;
        byte[] header = new byte[HeaderBytes]; input.ReadExactly(header);
        Require(Hash(header) == bank.HeaderSha256, "Source bank header changed.");
        byte[] directory = new byte[bank.PayloadOffset - HeaderBytes]; input.ReadExactly(directory);
        Require(Hash(directory) == bank.DirectorySha256, "Source bank directory changed.");
        Entry entry = bank.RequireEntry(resource);
        input.Position = checked(bank.PayloadOffset + entry.Offset);
        byte[] pixels = new byte[entry.Length]; input.ReadExactly(pixels);
        Require(Hash(pixels) == entry.RgbaSha256, "Source bank image integrity failed.");
        Require(input.Length == bank.Length, "Source bank changed during image verification.");
        return pixels;
    }

    private static int CheckStream(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek) throw new NotSupportedException("Source bank input must be readable and seekable.");
        long length = input.Length;
        Require(length is > HeaderBytes and <= MaxBankBytes, "Source bank exceeds its size bounds.");
        return (int)length;
    }
    private static void CheckSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(EchoesArenaSources.All.Any(a => a.ResourceId == source.MreaId)
            && Hex(source.MreaSha256, 64), "Unknown or malformed source bank area binding.");
    }
    private static void CheckImage(uint format, int width, int height, int mips, int bytes)
    {
        Require(format is 8 or 10 && width is >= 1 and <= RetroTextureSource.MaxDimension
            && height is >= 1 and <= RetroTextureSource.MaxDimension, "Unsupported source bank image dimensions or format.");
        long expected = checked((long)width * height * 4);
        Require(bytes is >= 4 and <= MaxImageBytes && expected == bytes, "Invalid source bank image byte count.");
        int maximumMips = 1;
        for (int size = Math.Max(width, height); size > 1; size >>= 1) maximumMips++;
        Require(mips >= 1 && mips <= RetroTextureSource.MaxMipCount && mips <= maximumMips,
            "Invalid source bank original mip count.");
    }
    private static string HashStream(Stream input, int length)
    {
        input.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[16384];
        int remaining = length;
        while (remaining > 0)
        {
            int read = input.Read(buffer[..Math.Min(remaining, buffer.Length)]);
            if (read == 0) throw new EndOfStreamException("Source bank changed during hashing.");
            hash.AppendData(buffer[..read]); remaining -= read;
        }
        Require(input.ReadByte() == -1 && input.Length == length, "Source bank grew during hashing.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);
    private static bool Zero(ReadOnlySpan<byte> value) { foreach (byte b in value) if (b != 0) return false; return true; }
    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    private static int Int(ReadOnlySpan<byte> data, int at)
    { uint value = U32(data, at); Require(value <= int.MaxValue, "Source bank integer exceeds its range."); return (int)value; }
    private static void Put(Span<byte> data, int at, int value) => Put(data, at, checked((uint)value));
    private static void Put(Span<byte> data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[at..], value);
    private static void Require(bool condition, string why) { if (!condition) throw new InvalidDataException(why); }
}
