using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;

namespace MphRead.Mods.CustomWeapons;

/// <summary>Fixed storage for a SHA-256 value. Packet readers never allocate a hex string.</summary>
internal readonly record struct WeaponDigest(ulong A, ulong B, ulong C, ulong D)
{
    internal const int Size = 32;
    internal bool IsValid => (A | B | C | D) != 0;
    internal static WeaponDigest Read(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
    internal void Write(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, A); BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], C); BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], D);
    }
    internal static WeaponDigest FromHex(string hex)
    {
        if (!WeaponIdentity.IsDigest(hex)) throw new ArgumentException("Canonical SHA-256 required.", nameof(hex));
        var value = Read(Convert.FromHexString(hex));
        if (!value.IsValid) throw new ArgumentException("Zero is the invalid digest sentinel.", nameof(hex));
        return value;
    }
    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Size]; Write(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}

internal readonly record struct CustomManifestRow(WeaponRuntimeId RuntimeId, Guid ContentId, WeaponDigest Mechanics)
{
    internal const int Size = 50;
    internal bool IsValid => RuntimeId.Value is >= 10 and <= 73 && ContentId != Guid.Empty && Mechanics.IsValid;
    internal WeaponIdentity Identity => WeaponIdentity.Custom(ContentId, Mechanics.ToString());
    internal void Write(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, RuntimeId.Value);
        ContentId.TryWriteBytes(bytes[2..18], bigEndian: true, out _);
        Mechanics.Write(bytes[18..50]);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out CustomManifestRow row)
    {
        row = default;
        if (bytes.Length != Size) return false;
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        if (id is < 10 or > 73) return false;
        row = new(new(id), new Guid(bytes[2..18], bigEndian: true), WeaponDigest.Read(bytes[18..50]));
        if (row.IsValid) return true;
        row = default; return false;
    }
}

/// <summary>Mechanics-only v1 manifest. Assets/packages are a separately versioned CW-12 contract.
/// Canonical bytes: CWM1, schema u16, behavior u16, count u16, sorted 50-byte rows.
/// Runtime IDs agree with the scene registry (nine native entries precede custom entries).</summary>
internal sealed class CustomWeaponManifest
{
    internal const ushort Version = 1;
    internal const int MaxEntries = WeaponRegistry.MaxCustomWeapons;
    internal ReadOnlyCollection<CustomManifestRow> Rows { get; }
    internal WeaponDigest Digest { get; }
    internal CustomWeaponManifest(IEnumerable<WeaponDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var items = new List<WeaponDefinition>();
        foreach (var definition in definitions)
        {
            if (definition == null || items.Count == MaxEntries)
                throw new ArgumentException("Invalid or excessive custom manifest entries.", nameof(definitions));
            items.Add(definition);
        }
        if (items.Count == 0 || items.Select(x => x.Identity.ContentId).Distinct().Count() != items.Count)
            throw new ArgumentException("A manifest needs 1–64 distinct content IDs.", nameof(definitions));
        var rows = items.OrderBy(x => x.Identity.ToString(), StringComparer.Ordinal)
            .Select((x, i) => new CustomManifestRow(new((ushort)(10+i)), x.Identity.ContentId,
                WeaponDigest.FromHex(x.Identity.DefinitionHash!))).ToArray();
        Rows = Array.AsReadOnly(rows);
        Digest = ComputeDigest(rows);
    }
    internal static WeaponDigest ComputeDigest(ReadOnlySpan<CustomManifestRow> rows)
    {
        if (rows.Length is < 1 or > MaxEntries) throw new ArgumentException("Invalid manifest size.", nameof(rows));
        Span<byte> bytes = stackalloc byte[10 + MaxEntries*CustomManifestRow.Size];
        "CWM1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[4..], WeaponMechanics.SchemaVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[6..], WeaponMechanics.BehaviorVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], (ushort)rows.Length);
        for (int i = 0; i < rows.Length; i++) rows[i].Write(bytes[(10+i*CustomManifestRow.Size)..]);
        Span<byte> digest = stackalloc byte[WeaponDigest.Size];
        SHA256.HashData(bytes[..(10+rows.Length*CustomManifestRow.Size)], digest);
        return WeaponDigest.Read(digest);
    }
}
