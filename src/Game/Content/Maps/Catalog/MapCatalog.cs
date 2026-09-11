using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MapBuildState>))]
public enum MapBuildState
{
    Ready,
    NeedsBuild,
    Building,
    Invalid,
    Unsupported,
    MissingDependency
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MapInstallSource>))]
public enum MapInstallSource
{
    LocalProject,
    InstalledPackage,
    LegacyRecipe,
    LegacyPackage,
    BundledPackage
}

public sealed record InstalledMap(
    MapContentIdentity ContentIdentity,
    string DisplayName,
    string Author,
    string Description,
    MapInstallSource Source,
    ImmutableArray<MapMode> SupportedModes,
    MapBuildState BuildState,
    string? PreviewPath,
    string SourcePath,
    string? ArtifactHash,
    long PackageSize,
    DateTimeOffset? InstalledAt,
    MapBuildStatistics? Statistics,
    ImmutableArray<MapDiagnostic> Diagnostics,
    MapProject Project);

public sealed record MapCatalogSnapshot
{
    public static MapCatalogSnapshot Empty { get; } = new(0, [], []);
    public long Revision { get; init; }
    public ImmutableArray<InstalledMap> Maps { get; }
    public ImmutableArray<MapDiagnostic> Diagnostics { get; }
    public FrozenDictionary<MapContentIdentity, InstalledMap> ByIdentity { get; }
    public FrozenDictionary<MapIdentity, ImmutableArray<InstalledMap>> ByStableIdVersion { get; }
    public FrozenDictionary<string, InstalledMap> ByRoomName { get; }

    public MapCatalogSnapshot(long revision, ImmutableArray<InstalledMap> maps,
        ImmutableArray<MapDiagnostic> diagnostics)
    {
        Revision = revision;
        Maps = maps;
        Diagnostics = diagnostics;
        ByIdentity = maps.GroupBy(map => map.ContentIdentity)
            .ToFrozenDictionary(group => group.Key, group => Preferred(group));
        ByStableIdVersion = maps.GroupBy(map => map.ContentIdentity.Identity)
            .ToFrozenDictionary(group => group.Key,
                group => group.OrderByDescending(LookupPriority)
                    .ThenBy(map => map.SourcePath, StringComparer.Ordinal).ToImmutableArray());
        ByRoomName = maps.Where(map => !string.IsNullOrWhiteSpace(map.Project.Map.Name))
            .GroupBy(map => map.Project.Map.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key, group => Preferred(group),
                StringComparer.OrdinalIgnoreCase);
    }

    public InstalledMap? Find(MapContentIdentity identity)
        => ByIdentity.GetValueOrDefault(identity);

    public InstalledMap? Find(string stableId, MapVersion version, string contentHash)
        => Find(new MapContentIdentity(new MapIdentity(stableId, version), contentHash));

    public InstalledMap? FindRoom(string roomName)
        => string.IsNullOrWhiteSpace(roomName) ? null : ByRoomName.GetValueOrDefault(roomName);

    private static InstalledMap Preferred(IEnumerable<InstalledMap> maps)
        => maps.OrderByDescending(LookupPriority)
            .ThenBy(map => map.SourcePath, StringComparer.Ordinal).First();

    private static int LookupPriority(InstalledMap map) => map.Source switch
    {
        MapInstallSource.InstalledPackage => 4,
        MapInstallSource.BundledPackage => 4,
        MapInstallSource.LocalProject => 3,
        MapInstallSource.LegacyPackage => 2,
        _ => 1
    };
}

public interface IMapCatalog
{
    MapCatalogSnapshot Snapshot { get; }
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
    ValueTask<InstalledMap> InstallAsync(string packagePath, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(MapContentIdentity identity, CancellationToken cancellationToken = default);
}

public sealed class MapCatalogOptions
{
    public required string InstalledDirectory { get; init; }
    public ImmutableArray<string> ProjectDirectories { get; init; } = [];
    public string? CacheDirectory { get; init; }
}

public sealed class MapCatalog : IMapCatalog, IDisposable
{
    private static readonly string[] RuntimeFiles =
        ["Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin"];
    private readonly MapCatalogOptions _options;
    private readonly MapBundleReader _reader;
    private readonly MapBundleValidator _validator;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private MapCatalogSnapshot _snapshot = MapCatalogSnapshot.Empty;
    private bool _disposed;

    public MapCatalogSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public event Action<MapCatalogSnapshot>? Changed;

    public MapCatalog(MapCatalogOptions options, MapBundleReadOptions? bundleOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _reader = new MapBundleReader(bundleOptions);
        _validator = new MapBundleValidator(bundleOptions);
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            MapCatalogSnapshot next = await Task.Run(Scan, cancellationToken).ConfigureAwait(false);
            Publish(next with { Revision = Snapshot.Revision + 1 });
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<InstalledMap> InstallAsync(string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        MapBundleValidationResult validation = await Task.Run(
            () => _validator.Validate(packagePath), cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new MapValidationException("Map package failed semantic validation.",
                validation.Diagnostics);
        MapBundleReadResult bundle = validation.Bundle!;
        if (bundle.IsLegacy)
            throw new MapPackageException("MAP-PKG-005",
                "Legacy bundles can be loaded for migration but must be re-exported as v2 before installation.");

        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Directory.CreateDirectory(_options.InstalledDirectory);
            string filename = $"{bundle.Manifest.StableId}-{bundle.Manifest.Version}-{bundle.Manifest.ContentHash[..12]}{MapBundle.Extension}";
            string destination = Path.Combine(Path.GetFullPath(_options.InstalledDirectory), filename);
            string temporary = destination + ".install-" + Guid.NewGuid().ToString("N");
            try
            {
                // Dispose the exclusive output handle before moving and
                // rescanning the package. Keeping the declaration alive for
                // the entire try made Scan silently classify the newly
                // installed file as unreadable on platforms that enforce
                // FileShare.None.
                await using (FileStream source = new(packagePath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (FileStream output = new(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(output, cancellationToken)
                        .ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                MapBundleReadResult copied = _reader.Read(temporary);
                if (copied.ArtifactHash != bundle.ArtifactHash)
                    throw new MapPackageException("MAP-PKG-009",
                        "Copied map artifact changed before atomic installation.");
                File.Move(temporary, destination, overwrite: true);
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }

            MapCatalogSnapshot next = Scan() with { Revision = Snapshot.Revision + 1 };
            Publish(next);
            // Identity is the catalog contract; path spelling is not. macOS
            // can surface the same temporary directory through canonical and
            // non-canonical prefixes, making an exact source-path comparison
            // reject a successfully scanned package.
            var identity = new MapContentIdentity(bundle.Manifest.Identity,
                bundle.Manifest.ContentHash);
            return next.Find(identity) ?? throw new MapPackageException(
                "MAP-CAT-004", "The installed map could not be rediscovered by its exact content identity.");
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask RemoveAsync(MapContentIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            InstalledMap[] matches = Snapshot.Maps.Where(map => map.Source == MapInstallSource.InstalledPackage
                && map.ContentIdentity == identity).ToArray();
            foreach (InstalledMap map in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string full = Path.GetFullPath(map.SourcePath);
                string root = Path.GetFullPath(_options.InstalledDirectory) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidOperationException("Catalog refused to remove a file outside its installed-map directory.");
                File.Delete(full);
            }
            MapCatalogSnapshot next = Scan() with { Revision = Snapshot.Revision + 1 };
            RemoveUnreferencedCache(identity, next.Maps);
            Publish(next);
        }
        finally
        {
            _writer.Release();
        }
    }

    public void PublishBuildState(MapContentIdentity identity, MapBuildState state,
        MapBuildStatistics? statistics, IEnumerable<MapDiagnostic> diagnostics)
    {
        _writer.Wait();
        try
        {
            ThrowIfDisposed();
            MapCatalogSnapshot current = Snapshot;
            ImmutableArray<InstalledMap> maps = current.Maps
                .Select(map => map.ContentIdentity == identity
                    ? map with { BuildState = state, Statistics = statistics, Diagnostics = [.. diagnostics] }
                    : map)
                .ToImmutableArray();
            Publish(new MapCatalogSnapshot(current.Revision + 1, maps, current.Diagnostics));
        }
        finally
        {
            _writer.Release();
        }
    }

    private MapCatalogSnapshot Scan()
    {
        var maps = new List<InstalledMap>();
        var diagnostics = new List<MapDiagnostic>();
        ScanDirectory(_options.InstalledDirectory, installed: true, maps, diagnostics);
        foreach (string directory in _options.ProjectDirectories.Distinct(StringComparer.Ordinal))
            ScanDirectory(directory, installed: false, maps, diagnostics);

        maps = maps.GroupBy(map => map.SourcePath, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(map => map.InstalledAt.HasValue).First()).ToList();

        var accepted = new List<InstalledMap>();
        // Editable source and cooked packages deliberately use different hash
        // domains. A creator must be able to keep a project beside an exported
        // or installed copy with the same stable ID/version. Conflicts are only
        // meaningful within the same ownership domain.
        foreach (IGrouping<(string StableId, MapVersion Version, bool Package), InstalledMap> group in maps
            .GroupBy(map => (map.ContentIdentity.Identity.StableId,
                map.ContentIdentity.Identity.Version, IsPackageSource(map.Source))))
        {
            string[] hashes = group.Select(map => map.ContentIdentity.ContentHash)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (hashes.Length > 1)
            {
                foreach (InstalledMap conflict in group)
                {
                    MapDiagnostic diagnostic = new("MAP-CAT-003", MapDiagnosticSeverity.Error,
                        $"{conflict.ContentIdentity.Identity.StableId} {conflict.ContentIdentity.Identity.Version} has conflicting "
                            + $"{(group.Key.Package ? "package" : "project")} content hashes.",
                        SourcePath: conflict.SourcePath,
                        SuggestedAction: "Remove or version one of the conflicting maps.");
                    accepted.Add(conflict with { BuildState = MapBuildState.Invalid,
                        Diagnostics = conflict.Diagnostics.Add(diagnostic) });
                    diagnostics.Add(diagnostic);
                }
                continue;
            }
            InstalledMap winner = group.OrderByDescending(map => SourcePriority(map.Source))
                .ThenBy(map => map.SourcePath, StringComparer.Ordinal).First();
            accepted.Add(winner);
        }
        ApplyCacheState(accepted);
        HashSet<MapContentIdentity> building = Snapshot.Maps
            .Where(map => map.BuildState == MapBuildState.Building)
            .Select(map => map.ContentIdentity).ToHashSet();
        for (int index = 0; index < accepted.Count; index++)
        {
            InstalledMap map = accepted[index];
            if (building.Contains(map.ContentIdentity)
                && map.BuildState is MapBuildState.NeedsBuild or MapBuildState.Ready)
                accepted[index] = map with { BuildState = MapBuildState.Building };
        }
        return new MapCatalogSnapshot(0,
            accepted.OrderBy(map => map.ContentIdentity.Identity.StableId, StringComparer.Ordinal)
                .ThenByDescending(map => map.ContentIdentity.Identity.Version).ToImmutableArray(),
            [.. diagnostics]);
    }

    private void ScanDirectory(string directory, bool installed, List<InstalledMap> maps,
        List<MapDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal))
            .Where(path => Path.GetExtension(path).Equals(MapBundle.Extension, StringComparison.OrdinalIgnoreCase)
                || !installed && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                InstalledMap? map = Path.GetExtension(path).Equals(MapBundle.Extension, StringComparison.OrdinalIgnoreCase)
                    ? ReadPackage(path, installed)
                    : ReadProject(path);
                if (map != null) maps.Add(map);
            }
            catch (Exception exception) when (exception is MapPackageException or JsonException
                or IOException or UnauthorizedAccessException or ProgramException or ArgumentException)
            {
                diagnostics.Add(new("MAP-CAT-001", MapDiagnosticSeverity.Error,
                    exception.Message, SourcePath: path));
            }
        }
    }

    private InstalledMap ReadPackage(string path, bool installed)
    {
        MapBundleReadResult bundle = _reader.Read(path);
        MapProject project = MapProjectIO.Load(path);
        MapDefinition definition = project.Map;
        definition.Name = definition.Name.ToUpperInvariant();
        definition.SourcePath = Path.GetFullPath(path);
        definition.BaseDirectory = Path.GetDirectoryName(definition.SourcePath);
        definition.BundlePath = definition.SourcePath;
        if (definition.Import != null)
        {
            definition.Import.BaseDirectory = definition.BaseDirectory;
            definition.Import.BundlePath = definition.BundlePath;
        }
        project.StableId = bundle.Manifest.StableId;
        project.Version = bundle.Manifest.Version;
        project.Metadata = new MapProjectMetadata
        {
            Name = bundle.Manifest.Name,
            Author = bundle.Manifest.Author,
            Description = bundle.Manifest.Description,
            Redistribution = bundle.Manifest.Redistribution
        };
        project.SupportedModes = [.. bundle.Manifest.SupportedModes];
        project.DeclaredContentIdentity = new MapContentIdentity(bundle.Manifest.Identity, bundle.Manifest.ContentHash);
        ImmutableArray<MapDiagnostic> projectDiagnostics = new MapValidator().ValidateProject(project);
        return new InstalledMap(new MapContentIdentity(bundle.Manifest.Identity, bundle.Manifest.ContentHash),
            bundle.Manifest.Name, bundle.Manifest.Author, bundle.Manifest.Description,
            bundle.IsLegacy ? MapInstallSource.LegacyPackage
                : installed ? MapInstallSource.InstalledPackage : MapInstallSource.BundledPackage,
            [.. bundle.Manifest.SupportedModes], StateFromDiagnostics(projectDiagnostics),
            bundle.Manifest.Preview == null ? null : $"{Path.GetFullPath(path)}::{bundle.Manifest.Preview}",
            Path.GetFullPath(path), bundle.ArtifactHash, bundle.PackageSize,
            installed ? File.GetCreationTimeUtc(path) : null, null,
            bundle.IsLegacy
                ? projectDiagnostics.Add(new("MAP-PKG-001", MapDiagnosticSeverity.Warning,
                    "Legacy package is available through the migration adapter.", SourcePath: path))
                : projectDiagnostics, project);
    }

    private static InstalledMap? ReadProject(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        MapProject? project = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("stableId", out _)
                && document.RootElement.TryGetProperty("map", out _))
                project = JsonSerializer.Deserialize(bytes, MapJsonContext.Default.MapProject);
        }
        catch (JsonException)
        {
            // Legacy loader below retains its comments/trailing-comma compatibility.
        }
        MapInstallSource source;
        if (project == null)
        {
            MapDefinition definition = MapDefinition.Load(path);
            if (definition.Import == null && definition.Brushes.Count == 0) return null;
            definition.Name = definition.Name.ToUpperInvariant();
            project = MapProject.FromLegacy(definition);
            source = MapInstallSource.LegacyRecipe;
        }
        else
        {
            project.SourcePath = Path.GetFullPath(path);
            project.Map.SourcePath = project.SourcePath;
            project.Map.BaseDirectory = Path.GetDirectoryName(project.SourcePath);
            if (project.Map.Import != null) project.Map.Import.BaseDirectory = project.Map.BaseDirectory;
            source = MapInstallSource.LocalProject;
        }
        ImmutableArray<MapDiagnostic> projectDiagnostics = new MapValidator().ValidateProject(project);
        string sourceHash;
        try { sourceHash = MapProjectContentHasher.Compute(project); }
        catch (MapDependencyException)
        {
            // Keep a stable catalog record so the launcher/editor can report
            // and repair the missing dependency. It cannot be Ready until a
            // refresh computes the complete dependency-aware identity.
            sourceHash = MapProjectContentHasher.ComputeCanonicalProject(project);
        }
        return new InstalledMap(new MapContentIdentity(project.Identity, sourceHash), project.Metadata.Name,
            project.Metadata.Author, project.Metadata.Description, source, [.. project.SupportedModes],
            StateFromDiagnostics(projectDiagnostics),
            project.PreviewImage == null ? null : Path.GetFullPath(project.PreviewImage,
                Path.GetDirectoryName(Path.GetFullPath(path))!),
            Path.GetFullPath(path), null, bytes.LongLength, null, null, projectDiagnostics, project);
    }

    private void ApplyCacheState(List<InstalledMap> maps)
    {
        if (string.IsNullOrWhiteSpace(_options.CacheDirectory)
            || !Directory.Exists(_options.CacheDirectory)) return;
        var builds = new Dictionary<MapContentIdentity, MapBuildMetadata>();
        foreach (string directory in Directory.EnumerateDirectories(_options.CacheDirectory)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!TryReadCacheMetadata(directory, out MapBuildMetadata? metadata)) continue;
            builds[metadata.SourceIdentity] = metadata;
        }
        for (int index = 0; index < maps.Count; index++)
        {
            InstalledMap map = maps[index];
            if (map.BuildState is MapBuildState.Invalid or MapBuildState.Unsupported
                or MapBuildState.MissingDependency) continue;
            if (!builds.TryGetValue(map.ContentIdentity, out MapBuildMetadata? metadata)) continue;
            maps[index] = map with { BuildState = MapBuildState.Ready,
                Statistics = metadata.Statistics, Diagnostics = [.. metadata.Diagnostics] };
        }
    }

    private void RemoveUnreferencedCache(MapContentIdentity removed,
        ImmutableArray<InstalledMap> remaining)
    {
        if (string.IsNullOrWhiteSpace(_options.CacheDirectory)
            || !Directory.Exists(_options.CacheDirectory)
            || remaining.Any(map => map.ContentIdentity == removed)) return;
        string root = Path.GetFullPath(_options.CacheDirectory) + Path.DirectorySeparatorChar;
        foreach (string directory in Directory.EnumerateDirectories(_options.CacheDirectory))
        {
            if (!TryReadCacheMetadata(directory, out MapBuildMetadata? metadata)
                || metadata.SourceIdentity != removed) continue;
            string full = Path.GetFullPath(directory);
            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException("Catalog refused to remove a cache outside its root.");
            Directory.Delete(full, recursive: true);
        }
    }

    private static bool TryReadCacheMetadata(string directory, out MapBuildMetadata metadata)
    {
        metadata = null!;
        try
        {
            string path = Path.Combine(directory, "build.json");
            if (!File.Exists(path)) return false;
            metadata = JsonSerializer.Deserialize(File.ReadAllBytes(path),
                MapJsonContext.Default.MapBuildMetadata)!;
            if (metadata == null || metadata.CompilerSchemaVersion <= 0
                || metadata.GeneratedFiles.Count != RuntimeFiles.Length) return false;
            foreach (MapGeneratedFile file in metadata.GeneratedFiles)
            {
                if (!RuntimeFiles.Contains(file.Path, StringComparer.Ordinal)) return false;
                string generatedPath = Path.Combine(directory, file.Path);
                if (!File.Exists(generatedPath)) return false;
                var info = new FileInfo(generatedPath);
                if (info.Length != file.Size
                    || MapJson.Sha256(File.ReadAllBytes(generatedPath)) != file.Sha256) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException
            or UnauthorizedAccessException or ArgumentException)
        {
            metadata = null!;
            return false;
        }
    }

    private static int SourcePriority(MapInstallSource source) => source switch
    {
        MapInstallSource.LocalProject => 4,
        MapInstallSource.InstalledPackage => 3,
        MapInstallSource.BundledPackage => 3,
        MapInstallSource.LegacyPackage => 2,
        _ => 1
    };

    private static bool IsPackageSource(MapInstallSource source)
        => source is MapInstallSource.InstalledPackage
            or MapInstallSource.BundledPackage
            or MapInstallSource.LegacyPackage;

    private static MapBuildState StateFromDiagnostics(ImmutableArray<MapDiagnostic> diagnostics)
    {
        MapDiagnostic[] errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error).ToArray();
        if (errors.Length == 0) return MapBuildState.NeedsBuild;
        return CompilationFailureKinds.FromDiagnostics(errors).ToBuildState();
    }

    private void Publish(MapCatalogSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
        Changed?.Invoke(snapshot);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        _disposed = true;
        _writer.Dispose();
    }
}
