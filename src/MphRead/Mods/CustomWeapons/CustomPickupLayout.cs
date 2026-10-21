using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Fixed-point authored positions make the layout hash and contact position exact.
// This sidecar never enters native ItemType, entity serialization or weapon arrays.
internal readonly record struct CustomPickupPlacement(ushort SpawnId, WeaponRuntimeId Weapon,
    int X4096, int Y4096, int Z4096, uint InitialDelayTicks, uint RespawnTicks, int ReserveRefill)
{
    internal Vector3 Position => new(X4096/4096f,Y4096/4096f,Z4096/4096f);
}

internal sealed class CustomPickupLayout
{
    internal const int Capacity = 64;
    internal const uint MaximumDelayTicks = 36000;
    private readonly CustomPickupPlacement[] _placements;
    private readonly WeaponDefinition[] _definitions;
    internal WeaponDigest Digest { get; }
    internal WeaponDigest Map { get; }
    internal MapContentIdentity MapIdentity { get; }
    internal WeaponDigest Manifest { get; }
    internal int Count => _placements.Length;
    internal ReadOnlySpan<CustomPickupPlacement> Placements => _placements;
    internal WeaponDefinition Definition(int index) => _definitions[index];

    internal CustomPickupLayout(MapContentIdentity map, CustomWeaponManifest manifest, WeaponRegistry registry,
        IEnumerable<CustomPickupPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(placements);
        if (registry.IsDisposed || registry.Count != 9+manifest.Rows.Count)
            throw new ArgumentException("Exact live custom registry required.",nameof(registry));
        foreach (var row in manifest.Rows)
            if (!registry.TryResolve(row.RuntimeId,out var entry) || entry?.Identity != row.Identity)
                throw new ArgumentException("Manifest and registry differ.",nameof(registry));
        Map = HashMap(map); MapIdentity = map; Manifest = manifest.Digest;
        var list = new List<CustomPickupPlacement>();
        foreach (var placement in placements)
        {
            const int limit = 8192*4096;
            if (list.Count == Capacity || placement.SpawnId == 0 || placement.Weapon.Value is < 10 or > 73
                || placement.X4096 < -limit || placement.X4096 > limit
                || placement.Y4096 < -limit || placement.Y4096 > limit
                || placement.Z4096 < -limit || placement.Z4096 > limit
                || placement.InitialDelayTicks > MaximumDelayTicks || placement.RespawnTicks is 0 or > MaximumDelayTicks
                || !registry.TryResolve(placement.Weapon,out var entry) || entry?.Custom is not { } definition
                || placement.ReserveRefill < 0 || placement.ReserveRefill > definition.Mechanics.MaximumReserve
                || placement.ReserveRefill == 0 && definition.Mechanics.MaximumReserve != 0)
                throw new ArgumentException("Invalid or excessive custom pickup placement.",nameof(placements));
            list.Add(placement);
        }
        if (list.Count == 0) throw new ArgumentException("At least one authored pickup is required.",nameof(placements));
        list.Sort((a,b)=>a.SpawnId.CompareTo(b.SpawnId));
        _placements = list.ToArray(); _definitions = new WeaponDefinition[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            if (i != 0 && list[i-1].SpawnId == list[i].SpawnId)
                throw new ArgumentException("Duplicate custom pickup spawn ID.",nameof(placements));
            registry.TryResolve(list[i].Weapon,out var entry); _definitions[i] = entry!.Custom!;
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("CWP1"u8);
        Span<byte> bytes = stackalloc byte[32];
        Map.Write(bytes); hash.AppendData(bytes); Manifest.Write(bytes); hash.AppendData(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes,(ushort)Count); hash.AppendData(bytes[..2]);
        foreach (var placement in _placements)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes,placement.SpawnId);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..],placement.Weapon.Value);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..],placement.X4096);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..],placement.Y4096);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..],placement.Z4096);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..],placement.InitialDelayTicks);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..],placement.RespawnTicks);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[24..],placement.ReserveRefill);
            hash.AppendData(bytes[..28]);
        }
        Digest = WeaponDigest.Read(hash.GetHashAndReset());
    }

    private static WeaponDigest HashMap(MapContentIdentity map)
    {
        if (string.IsNullOrWhiteSpace(map.RoomKey) || map.RoomKey.Length > 80)
            throw new ArgumentException("Stable room identity required.",nameof(map));
        foreach (char value in map.RoomKey)
            if (char.IsControl(value)) throw new ArgumentException("Invalid room identity.",nameof(map));
        if (map.IsCustom ? map.MapId == Guid.Empty || map.ContentHash.IsZero || map.PackageHash.IsZero
            : map.MapId != Guid.Empty || !map.ContentHash.IsZero || !map.PackageHash.IsZero)
            throw new ArgumentException("Exact built-in or custom map identity required.",nameof(map));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("CWPM1"u8);
        Span<byte> bytes = stackalloc byte[32];
        bytes[0] = map.IsCustom ? (byte)1 : (byte)0; hash.AppendData(bytes[..1]);
        byte[] name = new UTF8Encoding(false,true).GetBytes(map.RoomKey.ToUpperInvariant());
        BinaryPrimitives.WriteInt32LittleEndian(bytes,name.Length); hash.AppendData(bytes[..4]); hash.AppendData(name);
        map.MapId.TryWriteBytes(bytes,bigEndian:true,out _); hash.AppendData(bytes[..16]);
        map.ContentHash.Write(bytes); hash.AppendData(bytes); map.PackageHash.Write(bytes); hash.AppendData(bytes);
        return WeaponDigest.Read(hash.GetHashAndReset());
    }
}
