using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.MapGen;

/// <summary>An immutable SHA-256 value. Default represents the all-zero built-in identity.</summary>
public readonly record struct MapHash256(ulong A, ulong B, ulong C, ulong D)
{
    public const int Size = 32;
    public bool IsZero => this == default;
    public static MapHash256 Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size) throw new ArgumentException("A map hash requires exactly 32 bytes.");
        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
    }
    public void Write(Span<byte> bytes)
    {
        if (bytes.Length < Size) throw new ArgumentException("A map hash requires 32 bytes.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, A); BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], C); BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], D);
    }
    public static bool TryParse(string? value, out MapHash256 hash)
    {
        hash = default;
        if (value is not { Length: 64 }) return false;
        Span<byte> bytes = stackalloc byte[Size];
        try { Convert.FromHexString(value).CopyTo(bytes); }
        catch (FormatException) { return false; }
        hash = Read(bytes); return true;
    }
    public static MapHash256 Parse(string value) => TryParse(value, out var hash) ? hash
        : throw new InvalidDataException("Invalid SHA-256 map hash.");
    public static MapHash256 HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(SHA256.HashData(stream));
    }
    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Size]; Write(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public readonly record struct MapContentIdentity(Guid MapId, string RoomKey,
    MapHash256 ContentHash, MapHash256 PackageHash, bool IsCustom)
{
    public static MapContentIdentity BuiltIn(string roomKey) => new(Guid.Empty, roomKey, default, default, false);
    public static MapContentIdentity FromPackage(string path)
    {
        using var package = new MapPackageReader(path);
        var manifest = package.Manifest ?? throw new InvalidDataException("Rebuild legacy maps before online sharing.");
        return new(manifest.MapId, manifest.Name, MapHash256.Parse(manifest.ContentHash), MapHash256.HashFile(path), true);
    }
    public bool Matches(MapContentIdentity other) => IsCustom == other.IsCustom
        && StringComparer.OrdinalIgnoreCase.Equals(RoomKey, other.RoomKey)
        && MapId == other.MapId && ContentHash == other.ContentHash && PackageHash == other.PackageHash;
}
