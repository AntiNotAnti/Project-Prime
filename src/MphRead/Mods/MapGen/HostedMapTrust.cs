using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Trusted handoff from the allocator that verified an immutable hosted package
/// to the child process that receives a hard link/copy of it. This fast path is
/// enabled only by -hostedchild and only for the private child library.
/// Ordinary user/downloaded packages retain full package hashing.
/// </summary>
internal static class HostedMapTrust
{
    private const string FileName = ".hosted-identities";
    private static readonly object Gate = new();
    private static string _directory = "";
    private static Dictionary<Guid, MapContentIdentity> _identities = new();

    internal static void Enable(string directory)
    {
        directory = Path.GetFullPath(directory);
        var identities = new Dictionary<Guid, MapContentIdentity>();
        string path = Path.Combine(directory, FileName);
        try
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 4
                        || !Guid.TryParseExact(parts[0], "N", out Guid id)
                        || !MapHash256.TryParse(parts[2], out MapHash256 content)
                        || !MapHash256.TryParse(parts[3], out MapHash256 package))
                        continue;
                    string room;
                    try { room = Uri.UnescapeDataString(parts[1]); }
                    catch (UriFormatException) { continue; }
                    if (room.Length == 0 || id == Guid.Empty
                        || content.IsZero || package.IsZero) continue;
                    identities[id] = new(id, room, content, package, true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            identities.Clear();
        }
        lock (Gate)
        {
            _directory = directory.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            _identities = identities;
        }
    }

    internal static bool TryGet(string packagePath, MapDefinition definition,
        out MapContentIdentity identity)
    {
        identity = default;
        string full;
        try { full = Path.GetFullPath(packagePath); }
        catch { return false; }

        lock (Gate)
        {
            if (_directory.Length == 0 || !full.StartsWith(_directory,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
                || !_identities.TryGetValue(definition.MapId, out identity))
                return false;
        }
        return identity.IsCustom
            && StringComparer.Ordinal.Equals(identity.RoomKey, definition.Name);
    }

    internal static void Write(string directory,
        IEnumerable<MphRead.Mods.Network.HostedMapArchive> archives)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string[] lines = archives
            .GroupBy(a => a.Identity.MapId)
            .Select(group => group.First().Identity)
            .OrderBy(identity => identity.MapId)
            .Select(identity => String.Join('\t',
                identity.MapId.ToString("N"),
                Uri.EscapeDataString(identity.RoomKey),
                identity.ContentHash.ToString(),
                identity.PackageHash.ToString()))
            .ToArray();
        File.WriteAllLines(temporary, lines);
        File.Move(temporary, path, overwrite: true);
    }
}
