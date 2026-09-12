using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.MapGen;

public interface IContentResolver
{
    ReadOnlyMemory<byte> Read(string logicalPath);
    bool Exists(string logicalPath);
}

public sealed class BaseContentMount : IContentResolver
{
    private readonly string _root;

    public BaseContentMount(string root)
        => _root = Path.GetFullPath(root);

    public ReadOnlyMemory<byte> Read(string logicalPath)
        => File.ReadAllBytes(Resolve(logicalPath));

    public bool Exists(string logicalPath)
    {
        try { return File.Exists(Resolve(logicalPath)); }
        catch (ArgumentException) { return false; }
    }

    private string Resolve(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        string path = Path.IsPathRooted(logicalPath)
            ? Path.GetFullPath(logicalPath)
            : Path.GetFullPath(Path.Combine(_root, logicalPath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (path != _root && !path.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("Content path escapes the base mount.", nameof(logicalPath));
        return path;
    }
}

public sealed class MapContentMount : IContentResolver
{
    private readonly FrozenDictionary<string, byte[]> _files;
    public MapContentIdentity Identity { get; }
    public string BuildFingerprint { get; }

    public MapContentMount(MapContentIdentity identity, string buildFingerprint,
        string cacheDirectory, MapDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(definition);
        Identity = identity;
        BuildFingerprint = MapHash.Validate(buildFingerprint, nameof(buildFingerprint));
        string prefix = definition.Name.ToLowerInvariant();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Normalize(CustomRooms.ArchiveDirectory(definition), $"{prefix}_Model.bin")] = "Model.bin",
            [Normalize(CustomRooms.ArchiveDirectory(definition), $"{prefix}_Anim.bin")] = "Anim.bin",
            [Normalize(CustomRooms.ArchiveDirectory(definition), $"{prefix}_Collision.bin")] = "Collision.bin",
            [Normalize(CustomRooms.EntityDirectory(), $"{prefix}_Ent.bin")] = "Ent.bin",
            [Normalize(CustomRooms.NodeDirectory(), $"{prefix}_Node.bin")] = "Node.bin"
        };
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string logicalPath, string cacheName) in paths)
        {
            string source = Path.Combine(cacheDirectory, cacheName);
            if (!File.Exists(source))
                throw new MapCompilationException($"Compiled map cache is missing {cacheName}.");
            files.Add(logicalPath, File.ReadAllBytes(source));
        }
        _files = files.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public ReadOnlyMemory<byte> Read(string logicalPath)
    {
        string normalized = Normalize(logicalPath);
        if (!_files.TryGetValue(normalized, out byte[]? bytes))
            throw new FileNotFoundException("Resource is not in the selected map mount.", logicalPath);
        return (byte[])bytes.Clone();
    }

    public bool Exists(string logicalPath) => _files.ContainsKey(Normalize(logicalPath));

    private static string Normalize(string path)
        => Path.GetFullPath(path).Replace('\\', '/');

    private static string Normalize(string directory, string file)
        => Normalize(Path.Combine(directory, file));
}

public sealed class CompositeContentResolver(params IContentResolver[] mounts) : IContentResolver
{
    private readonly IContentResolver[] _mounts = mounts.Length == 0
        ? throw new ArgumentException("At least one content mount is required.", nameof(mounts))
        : (IContentResolver[])mounts.Clone();

    public ReadOnlyMemory<byte> Read(string logicalPath)
    {
        foreach (IContentResolver mount in _mounts)
        {
            if (mount.Exists(logicalPath)) return mount.Read(logicalPath);
        }
        throw new FileNotFoundException("No content mount contains the requested resource.", logicalPath);
    }

    public bool Exists(string logicalPath)
    {
        foreach (IContentResolver mount in _mounts)
        {
            if (mount.Exists(logicalPath)) return true;
        }
        return false;
    }
}

public sealed record MatchContentSnapshot(
    string BaseContentIdentity,
    MapContentIdentity MapIdentity,
    string GameplayIdentity,
    string MatchContentIdentity,
    MapContentMount MapMount)
{
    public static MatchContentSnapshot Create(string baseContentIdentity, MapContentIdentity mapIdentity,
        string gameplayIdentity, MapContentMount mapMount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameplayIdentity);
        string hash = MatchContentHash.Compute(baseContentIdentity,
            mapIdentity, gameplayIdentity);
        return new MatchContentSnapshot(baseContentIdentity, mapIdentity, gameplayIdentity, hash, mapMount);
    }
}

/// <summary>
/// The one compatibility formula shared by Node admission, Worker preparation,
/// clients, replays, and immutable runtime snapshots.
/// </summary>
public static class MatchContentHash
{
    public static string Compute(string baseContentIdentity,
        MapContentIdentity mapIdentity, string gameplayIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentIdentity);
        ArgumentNullException.ThrowIfNull(mapIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameplayIdentity);
        string value = string.Join('\0', "ProjectPrime.MatchContent.v1",
            baseContentIdentity, mapIdentity.Identity.StableId,
            mapIdentity.Identity.Version.ToString(), mapIdentity.ContentHash,
            gameplayIdentity);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
