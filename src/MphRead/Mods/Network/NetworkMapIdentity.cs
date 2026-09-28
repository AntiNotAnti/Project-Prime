using System;
using System.IO;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

[Flags]
public enum NetworkMapFlags : byte { None = 0, Custom = 1, Downloadable = 2 }

public readonly record struct NetworkMapIdentity(Guid MapId, MapHash256 ContentHash, MapHash256 PackageHash, NetworkMapFlags Flags)
{
    public const int Size = 81;
    public bool IsCustom => Flags.HasFlag(NetworkMapFlags.Custom);
    public MapContentIdentity Content(string room) => new(MapId, room, ContentHash, PackageHash, IsCustom);
    public bool IsValid => IsCustom
        ? MapId != Guid.Empty && !ContentHash.IsZero && !PackageHash.IsZero && (Flags & ~(NetworkMapFlags.Custom | NetworkMapFlags.Downloadable)) == 0
        : this == default;
    public void Write(Span<byte> destination)
    {
        MapId.TryWriteBytes(destination[..16]); ContentHash.Write(destination[16..]); PackageHash.Write(destination[48..]); destination[80] = (byte)Flags;
    }
    public static bool TryRead(ReadOnlySpan<byte> source, out NetworkMapIdentity identity)
    {
        identity = default;
        if (source.Length != Size) return false;
        identity = new(new Guid(source[..16]), MapHash256.Read(source.Slice(16,32)), MapHash256.Read(source.Slice(48,32)), (NetworkMapFlags)source[80]);
        return identity.IsValid;
    }
    public static string ConfiguredDownloadSource()
    {
        string? address = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY");
        string path = Path.Combine(Launcher.LauncherPrefs.Directory, "map-community.txt");
        if (string.IsNullOrWhiteSpace(address) && File.Exists(path) && new FileInfo(path).Length <= 4096) address = File.ReadAllText(path).Trim();
        address = string.IsNullOrWhiteSpace(address) ? MapCommunityClient.DefaultAddress : address.Trim();
        using var validation = new MapCommunityClient(address);
        if (System.Text.Encoding.UTF8.GetByteCount(address) >= SessionStatePacket.MaxDownloadSourceBytes)
            throw new InvalidDataException("Community address is too long to advertise.");
        return address;
    }

    public static void StageRoom(string room)
    {
        if (Metadata.IsBuiltInRoom(room)) return;
        if (CustomRooms.Installed.TryGet(room, out var installed))
        {
            if (!CustomRooms.Installed.HasExact(installed.Identity))
                throw new InvalidDataException("Installed map package has changed: " + room);
            return;
        }
        var definition = System.Linq.Enumerable.FirstOrDefault(CustomRooms.Definitions, d => d.Name.Equals(room, StringComparison.OrdinalIgnoreCase));
        if (definition == null) throw new InvalidDataException("Unknown map: " + room);
        Guid id = definition.MapId == Guid.Empty ? MapPackageBuilder.LegacyId(room) : definition.MapId;
        string path = Path.Combine(CustomRooms.UserMapDirectory, id.ToString("N") + ".ppmap");
        MapPackageBuilder.Build(definition, path);
        Metadata.RegisterDownloadedMap(MapDefinition.Load(path));
    }

    public static NetworkMapIdentity ForRoom(string room)
    {
        if (Metadata.IsBuiltInRoom(room)) return default;
        if (!CustomRooms.Installed.TryGet(room, out var installed))
            throw new InvalidDataException("Install an immutable .ppmap package before hosting this custom map: " + room);
        var value = installed.Identity;
        return new(value.MapId, value.ContentHash, value.PackageHash, NetworkMapFlags.Custom);
    }
}

public enum MapAvailabilityState : byte { Unknown, Missing, Downloading, Verifying, Installing, Building, Prewarming, Ready, Failed }

public readonly record struct MapAvailabilityPacket(ushort MatchId, ulong AuthorityEpoch, NetworkMapIdentity Map, MapAvailabilityState State, ushort Generation = 1, uint Sequence = 1)
{
    public const int Size = 17 + NetworkMapIdentity.Size;
    public void Write(Span<byte> destination)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination, MatchId);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(destination[2..], AuthorityEpoch);
        destination[10] = (byte)State; Map.Write(destination[11..]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination[(Size-6)..],Generation);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination[(Size-4)..],Sequence);
    }
    public static bool TryRead(ReadOnlySpan<byte> source, out MapAvailabilityPacket packet)
    {
        packet = default;
        if (source.Length != Size || source[10] > (byte)MapAvailabilityState.Failed || !NetworkMapIdentity.TryRead(source.Slice(11,NetworkMapIdentity.Size), out var identity)) return false;
        packet = new(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(source),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(source[2..]), identity, (MapAvailabilityState)source[10],
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(source[(Size-6)..]),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source[(Size-4)..]));
        return packet.MatchId != 0 && packet.AuthorityEpoch != 0 && packet.Generation != 0 && packet.Sequence != 0;
    }
}
