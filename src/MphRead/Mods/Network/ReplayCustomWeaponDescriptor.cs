using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

// File-only historical authored layout. Exact package bytes are resolved by the
// bootstrap's historical binding before Bind; no stock-table lookup or install.
internal sealed class ReplayCustomWeaponDescriptor
{
    internal const ushort Version = 1, MetadataVersion = 2;
    internal const int MaximumBytes = 20 * 1024, HeaderSize = 12, PlacementSize = 28;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] _bytes;
    private readonly CustomPickupPlacement[] _placements;
    internal ReplayCustomWeaponBootstrap Bootstrap { get; }
    internal MapContentIdentity Map { get; }
    internal WeaponDefinition? MetadataDefinition { get; }
    internal int Count => _placements.Length;
    internal ReplayCustomWeaponDescriptor(ReplayCustomWeaponContext context)
        : this(context.Bootstrap, context.Layout.MapIdentity, context.Layout.Placements.ToArray()) { }
    internal static ReplayCustomWeaponDescriptor ForInspection(ReplayCustomWeaponContext context)
        => new(context.Bootstrap, context.Layout.MapIdentity, context.Layout.Placements.ToArray(), context.Definition);
    private ReplayCustomWeaponDescriptor(ReplayCustomWeaponBootstrap bootstrap, MapContentIdentity map, CustomPickupPlacement[] placements, WeaponDefinition? metadata = null)
    {
        Bootstrap = bootstrap; Map = map; _placements = placements; MetadataDefinition = metadata;
        byte[] name = Utf8.GetBytes(map.RoomKey), boot = bootstrap.CopyBytes();
        byte[] definitionName = metadata == null ? Array.Empty<byte>() : Utf8.GetBytes(metadata.Name);
        byte[] mechanics = metadata?.Mechanics.ToCanonicalBytes() ?? Array.Empty<byte>();
        _bytes = new byte[HeaderSize + boot.Length + 81 + name.Length + 2 + placements.Length * PlacementSize
            + (metadata == null ? 0 : 4 + definitionName.Length + mechanics.Length)];
        if (_bytes.Length > MaximumBytes || name.Length is 0 or > 320) throw new InvalidDataException("Excessive custom replay layout.");
        "CWRD"u8.CopyTo(_bytes); BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(4), metadata == null ? Version : MetadataVersion);
        // Reserved flags at6 must remain zero.
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(8), checked((ushort)boot.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(10), checked((ushort)name.Length));
        int at = HeaderSize; boot.CopyTo(_bytes, at); at += boot.Length;
        _bytes[at++] = map.IsCustom ? (byte)1 : (byte)0;
        map.MapId.TryWriteBytes(_bytes.AsSpan(at, 16), bigEndian: true, out _); at += 16;
        map.ContentHash.Write(_bytes.AsSpan(at, 32)); at += 32; map.PackageHash.Write(_bytes.AsSpan(at, 32)); at += 32;
        name.CopyTo(_bytes, at); at += name.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(at), checked((ushort)placements.Length)); at += 2;
        foreach (var row in placements) { WritePlacement(_bytes.AsSpan(at, PlacementSize), row); at += PlacementSize; }
        if (metadata != null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(at), checked((ushort)definitionName.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(at + 2), checked((ushort)mechanics.Length)); at += 4;
            definitionName.CopyTo(_bytes, at); at += definitionName.Length; mechanics.CopyTo(_bytes, at);
        }
    }
    internal byte[] CopyBytes() => (byte[])_bytes.Clone();
    internal ReplayCustomWeaponContext Bind(WeaponDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var manifest = new CustomWeaponManifest(new[] { definition });
        using var registry = new WeaponRegistry(true, new[] { definition });
        var layout = new CustomPickupLayout(Map, manifest, registry, _placements);
        return new(Bootstrap, definition, layout); // Requires exact recorded mechanics/layout digests.
    }
    // Pure metadata consistency, never proof of archive/resource provenance.
    internal ReplayCustomWeaponContext InspectContext()
        => Bind(MetadataDefinition ?? throw new InvalidDataException("Historical descriptor has no inspection mechanics metadata."));
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ReplayCustomWeaponDescriptor? descriptor)
    {
        descriptor = null;
        try
        {
            if (bytes.Length is < HeaderSize or > MaximumBytes || !bytes[..4].SequenceEqual("CWRD"u8)
                || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) is not (Version or MetadataVersion) || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != 0) return false;
            bool metadata = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) == MetadataVersion;
            int bootLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]);
            if (nameLength is 0 or > 320 || bootLength < ReplayCustomWeaponBootstrap.HeaderSize
                || bytes.Length < HeaderSize + bootLength + 81 + nameLength + 2
                || !ReplayCustomWeaponBootstrap.TryRead(bytes.Slice(HeaderSize, bootLength), out var bootstrap)) return false;
            int at = HeaderSize + bootLength; byte custom = bytes[at++]; if (custom > 1) return false;
            var id = new Guid(bytes.Slice(at, 16), bigEndian: true); at += 16;
            var content = MapHash256.Read(bytes.Slice(at, 32)); at += 32; var archive = MapHash256.Read(bytes.Slice(at, 32)); at += 32;
            string name = Utf8.GetString(bytes.Slice(at, nameLength)); at += nameLength;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || custom == 0 && (id != Guid.Empty || !content.IsZero || !archive.IsZero)
                || custom == 1 && (id == Guid.Empty || content.IsZero || archive.IsZero)) return false;
            foreach (char character in name) if (char.IsControl(character)) return false;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]); at += 2;
            if (count is < 1 or > CustomPickupLayout.Capacity || bytes.Length < at + count * PlacementSize
                || !metadata && bytes.Length != at + count * PlacementSize) return false;
            var rows = new CustomPickupPlacement[count];
            for (int i = 0; i < count; i++)
            {
                var row = ReadPlacement(bytes.Slice(at, PlacementSize)); at += PlacementSize;
                const int limit = 8192 * 4096;
                if (row.SpawnId == 0 || i > 0 && row.SpawnId <= rows[i - 1].SpawnId || row.Weapon.Value != 10
                    || row.X4096 is < -limit or > limit || row.Y4096 is < -limit or > limit || row.Z4096 is < -limit or > limit
                    || row.InitialDelayTicks > CustomPickupLayout.MaximumDelayTicks || row.RespawnTicks is 0 or > CustomPickupLayout.MaximumDelayTicks
                    || row.ReserveRefill < 0) return false;
                rows[i] = row;
            }
            WeaponDefinition? definition = null;
            if (metadata)
            {
                if (bytes.Length < at + 4) return false;
                int definitionNameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]), mechanicsLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(at + 2)..]); at += 4;
                if (definitionNameLength is 0 or > 320 || mechanicsLength is 0 or > 16384 || bytes.Length != at + definitionNameLength + mechanicsLength) return false;
                string definitionName = Utf8.GetString(bytes.Slice(at, definitionNameLength)); at += definitionNameLength;
                var mechanics = WeaponMechanics.Parse(bytes.Slice(at, mechanicsLength));
                if (!mechanics.ToCanonicalBytes().AsSpan().SequenceEqual(bytes[at..])) return false;
                definition = WeaponDefinition.Create(bootstrap!.Packages.Rows[0].Weapon.ContentId, definitionName, mechanics);
            }
            var value = new ReplayCustomWeaponDescriptor(bootstrap!, new(id, name, content, archive, custom == 1), rows, definition);
            if (!bytes.SequenceEqual(value._bytes)) return false;
            if (metadata) value.InspectContext();
            descriptor = value; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or FormatException) { return false; }
    }
    private static void WritePlacement(Span<byte> bytes, in CustomPickupPlacement row)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, row.SpawnId); BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..], row.Weapon.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], row.X4096); BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], row.Y4096); BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], row.Z4096);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], row.InitialDelayTicks); BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..], row.RespawnTicks); BinaryPrimitives.WriteInt32LittleEndian(bytes[24..], row.ReserveRefill);
    }
    private static CustomPickupPlacement ReadPlacement(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadUInt16LittleEndian(bytes), new(BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..])),
        BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]));
}
