using MphRead;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Maps;

[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter<NodeMapPackageState>))]
public enum NodeMapPackageState
{
    Ready,
    Invalid
}

public sealed record NodeMapReadiness(string MapKey, NodeMapPackageState State, string Code);

/// <summary>Validated, explicitly configured data-only packages served by this Node.</summary>
public sealed class NodeMapPackageStore
{
    private sealed record Entry(MapRequirement Requirement, string Path, long Length, long LastWriteTimeUtcTicks);
    private readonly IReadOnlyDictionary<(string StableId, string Version, string ArtifactHash), Entry> _entries;
    public IReadOnlyList<NodeMapConfiguration> ReadyConfigurations { get; }
    public IReadOnlyList<NodeMapReadiness> States { get; }

    public NodeMapPackageStore(IEnumerable<NodeMapConfiguration> configurations, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        string root = Path.GetFullPath(contentRoot);
        var entries = new Dictionary<(string, string, string), Entry>();
        var ready = new List<NodeMapConfiguration>();
        var states = new List<NodeMapReadiness>();
        var mapKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (NodeMapConfiguration configuration in configurations)
        {
            try
            {
                if (configuration == null || String.IsNullOrWhiteSpace(configuration.MapKey)
                    || !mapKeys.Add(configuration.MapKey))
                    throw new ArgumentException("Node map package configuration has an invalid or duplicate map key.");
                MapRequirement? requirement = configuration.RequiredMap;
                ((string StableId, string Version, string ArtifactHash) Key, Entry Value)? package = null;
                if (requirement != null)
                {
                    requirement.Validate();
                    if (String.IsNullOrWhiteSpace(configuration.PackagePath))
                        throw new ArgumentException("Custom map has no configured package path.");
                    string path = Path.IsPathRooted(configuration.PackagePath)
                        ? Path.GetFullPath(configuration.PackagePath)
                        : Path.GetFullPath(configuration.PackagePath, root);
                    MapBundleValidationResult validation = new MapBundleValidator(
                        new MapBundleReadOptions { AllowLegacyV1 = false }).Validate(path);
                    if (!validation.IsValid)
                        throw new InvalidDataException("Configured package failed semantic validation.");
                    MapBundleReadResult bundle = validation.Bundle!;
                    if (!bundle.Manifest.Redistribution
                        || bundle.Manifest.StableId != requirement.StableId
                        || bundle.Manifest.Version.ToString() != requirement.Version
                        || bundle.Manifest.ContentHash != requirement.ContentHash
                        || bundle.ArtifactHash != requirement.ArtifactHash
                        || bundle.PackageSize != requirement.PackageSize)
                        throw new InvalidDataException(
                            "Configured package does not match its exact acquisition identity.");
                    var key = (requirement.StableId, requirement.Version, requirement.ArtifactHash);
                    if (entries.ContainsKey(key))
                        throw new ArgumentException(
                            "Node map package configuration contains a duplicate acquisition identity.");
                    var file = new FileInfo(path);
                    if (!file.Exists || file.Length != requirement.PackageSize)
                        throw new InvalidDataException("Configured package disappeared or has an unexpected size.");
                    // The deployment contract is immutable package bytes. Cache
                    // file metadata once at startup so every request can detect
                    // an accidental replacement without hashing the whole file.
                    package = (key, new(requirement, path, file.Length, file.LastWriteTimeUtc.Ticks));
                }
                // Validate the complete control-plane configuration and the
                // aggregate catalog bounds before publishing this individual
                // map. A bad community entry is isolated rather than failing
                // the Node's entire service graph.
                _ = NodeContentCatalog.FromConfiguration(ready.Append(configuration));
                if (package.HasValue) entries.Add(package.Value.Key, package.Value.Value);
                ready.Add(configuration);
                states.Add(new(configuration.MapKey, NodeMapPackageState.Ready, "NODE-MAP-READY"));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException
                or IOException or UnauthorizedAccessException or ProgramException)
            {
                states.Add(new(configuration?.MapKey ?? "", NodeMapPackageState.Invalid,
                    "NODE-MAP-PKG-INVALID"));
            }
        }
        _entries = entries;
        ReadyConfigurations = ready.AsReadOnly();
        States = states.AsReadOnly();
    }

    public bool TryOpen(string stableId, string version, string artifactHash,
        out FileStream? stream, out MapRequirement? requirement)
    {
        stream = null;
        requirement = null;
        if (!_entries.TryGetValue((stableId, version, artifactHash.ToLowerInvariant()), out Entry? entry))
            return false;
        var opened = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (opened.Length != entry.Length
                || File.GetLastWriteTimeUtc(entry.Path).Ticks != entry.LastWriteTimeUtcTicks)
                throw new InvalidDataException("Map package changed after Node startup.");
            opened.Position = 0;
            stream = opened;
            requirement = entry.Requirement;
            return true;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }
}
