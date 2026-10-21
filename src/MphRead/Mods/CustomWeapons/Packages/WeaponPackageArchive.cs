using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MphRead.Mods.CustomWeapons.Packages;

// Narrow ZIP32 profile, deliberately stored only. No general-purpose inflater,
// extraction, data descriptors, symlinks, comments, extras or ZIP64 parser.
// Explicit headers also make identical input bytes portable across ZIP writers.
internal static class WeaponPackageArchive
{
    private const uint Local = 0x04034b50, Central = 0x02014b50, End = 0x06054b50;
    private const ushort Version = 20, Date1980 = 33;
    private static readonly uint[] CrcTable = CreateTable();
    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        { uint v = i; for (int b = 0; b < 8; b++) v = (v >> 1) ^ ((v & 1) == 0 ? 0 : 0xedb88320u); table[i] = v; }
        return table;
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    { uint crc = uint.MaxValue; foreach (byte b in bytes) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8); return ~crc; }
    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static int Offset(ReadOnlySpan<byte> b, int at)
    { uint value = U32(b, at); if (value > WeaponPackageLimits.MaximumArchiveBytes) throw WeaponPackageJson.Bad("ZIP offset/size budget."); return (int)value; }
    private static void Require(bool condition)
    { if (!condition) throw WeaponPackageJson.Bad("Unsupported, corrupt or noncanonical stored ZIP32 container."); }

    internal static SortedDictionary<string, byte[]> Read(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length is >= 22 and <= WeaponPackageLimits.MaximumArchiveBytes);
        int end = bytes.Length - 22;
        Require(U32(bytes, end) == End && U16(bytes, end + 4) == 0 && U16(bytes, end + 6) == 0 && U16(bytes, end + 20) == 0);
        int count = U16(bytes, end + 10);
        Require(count is > 0 and <= WeaponPackageLimits.MaximumEntries && U16(bytes, end + 8) == count);
        int central = Offset(bytes, end + 16), centralBytes = Offset(bytes, end + 12);
        Require(central <= end && (long)central + centralBytes == end && centralBytes <= count * (46 + WeaponPackageLimits.MaximumPathBytes));
        int local = 0, cursor = central, total = 0;
        string? previous = null;
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            Require(cursor <= end - 46 && U32(bytes, cursor) == Central);
            Require(U16(bytes, cursor + 4) == Version && U16(bytes, cursor + 6) == Version
                && U16(bytes, cursor + 8) == 0 && U16(bytes, cursor + 10) == 0 && U16(bytes, cursor + 12) == 0
                && U16(bytes, cursor + 14) == Date1980 && U16(bytes, cursor + 30) == 0 && U16(bytes, cursor + 32) == 0
                && U16(bytes, cursor + 34) == 0 && U16(bytes, cursor + 36) == 0 && U32(bytes, cursor + 38) == 0);
            int length = Offset(bytes, cursor + 24), packed = Offset(bytes, cursor + 20), nameLength = U16(bytes, cursor + 28);
            uint crc = U32(bytes, cursor + 16);
            Require(length > 0 && length == packed && nameLength is > 0 and <= WeaponPackageLimits.MaximumPathBytes
                && cursor + 46 + nameLength <= end && Offset(bytes, cursor + 42) == local);
            var nameBytes = bytes.Slice(cursor + 46, nameLength);
            foreach (byte b in nameBytes) Require(b < 128);
            string name = WeaponPackageJson.Path(Encoding.ASCII.GetString(nameBytes));
            Require((previous == null || StringComparer.Ordinal.Compare(previous, name) < 0) && aliases.Add(name)); previous = name;
            Require(local <= central - 30 && U32(bytes, local) == Local && U16(bytes, local + 4) == Version
                && U16(bytes, local + 6) == 0 && U16(bytes, local + 8) == 0 && U16(bytes, local + 10) == 0
                && U16(bytes, local + 12) == Date1980 && U32(bytes, local + 14) == crc
                && U32(bytes, local + 18) == length && U32(bytes, local + 22) == length
                && U16(bytes, local + 26) == nameLength && U16(bytes, local + 28) == 0);
            long next = (long)local + 30 + nameLength + length;
            Require(next <= central && bytes.Slice(local + 30, nameLength).SequenceEqual(nameBytes));
            var payload = bytes.Slice(local + 30 + nameLength, length);
            Require(Crc(payload) == crc);
            total = checked(total + length); Require(total <= WeaponPackageLimits.MaximumExpandedBytes);
            result.Add(name, payload.ToArray()); local = (int)next; cursor += 46 + nameLength;
        }
        Require(local == central && cursor == end);
        return result;
    }

    internal static byte[] Write(SortedDictionary<string, byte[]> entries)
    {
        if (entries.Count is < 1 or > WeaponPackageLimits.MaximumEntries) throw WeaponPackageJson.Bad("ZIP entry budget.");
        long estimate = 22;
        foreach (var entry in entries)
        {
            WeaponPackageJson.Path(entry.Key);
            if (entry.Value.Length == 0) throw WeaponPackageJson.Bad("Empty resource.");
            estimate += 76L + entry.Key.Length * 2 + entry.Value.Length;
        }
        if (estimate > WeaponPackageLimits.MaximumArchiveBytes) throw WeaponPackageJson.Bad("ZIP byte budget.");
        using var stream = new MemoryStream((int)estimate);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var rows = new List<(string Name, uint Crc, int Length, uint Offset)>(entries.Count);
        foreach (var entry in entries)
        {
            byte[] name = Encoding.ASCII.GetBytes(entry.Key); uint crc = Crc(entry.Value), offset = (uint)stream.Position;
            writer.Write(Local); writer.Write(Version); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write(Date1980); writer.Write(crc); writer.Write((uint)entry.Value.Length); writer.Write((uint)entry.Value.Length);
            writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write(name); writer.Write(entry.Value);
            rows.Add((entry.Key, crc, entry.Value.Length, offset));
        }
        uint start = (uint)stream.Position;
        foreach (var row in rows)
        {
            byte[] name = Encoding.ASCII.GetBytes(row.Name);
            writer.Write(Central); writer.Write(Version); writer.Write(Version); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write(Date1980); writer.Write(row.Crc); writer.Write((uint)row.Length); writer.Write((uint)row.Length);
            writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(0u); writer.Write(row.Offset); writer.Write(name);
        }
        uint size = (uint)stream.Position - start;
        writer.Write(End); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)rows.Count); writer.Write((ushort)rows.Count);
        writer.Write(size); writer.Write(start); writer.Write((ushort)0); writer.Flush();
        return stream.ToArray();
    }
}
