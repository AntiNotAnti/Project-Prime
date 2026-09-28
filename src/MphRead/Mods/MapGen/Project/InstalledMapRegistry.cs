using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.MapGen;

public sealed record InstalledMapIdentity(MapContentIdentity Identity, string? Version, string PackagePath);

/// <summary>Only validated immutable archives enter this index; loose editor sources are not installed versions.</summary>
public sealed class InstalledMapRegistry
{
    private readonly Dictionary<Guid, InstalledMapIdentity> _byId = new();
    private readonly Dictionary<string, InstalledMapIdentity> _byRoom = new(StringComparer.OrdinalIgnoreCase);
    public bool TryGet(Guid id, out InstalledMapIdentity identity) => _byId.TryGetValue(id, out identity!);
    public bool TryGet(string room, out InstalledMapIdentity identity) => _byRoom.TryGetValue(room, out identity!);
    public bool HasExact(MapContentIdentity required) => required.IsCustom && TryGet(required.MapId, out var installed)
        && installed.Identity.Matches(required) && ArchiveMatches(installed);
    private static bool ArchiveMatches(InstalledMapIdentity installed)
    {
        try { return MapHash256.HashFile(installed.PackagePath) == installed.Identity.PackageHash; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static InstalledMapRegistry Create(IEnumerable<MapDefinition> definitions)
    {
        var registry = new InstalledMapRegistry();
        foreach (var definition in definitions)
        {
            if (definition.BundlePath is not { } path) continue;
            try
            {
                var identity = MapContentIdentity.FromPackage(path);
                if (identity.MapId != definition.MapId || !identity.RoomKey.Equals(definition.Name, StringComparison.Ordinal)) continue;
                var entry = new InstalledMapIdentity(identity, definition.Version, Path.GetFullPath(path));
                if (registry._byId.ContainsKey(identity.MapId) || registry._byRoom.ContainsKey(identity.RoomKey))
                    throw new InvalidDataException("Duplicate installed map identity.");
                registry._byId.Add(identity.MapId, entry); registry._byRoom.Add(identity.RoomKey, entry);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                Console.WriteLine($"[map] Cannot index {path}: {ex.Message}");
            }
        }
        return registry;
    }
}
