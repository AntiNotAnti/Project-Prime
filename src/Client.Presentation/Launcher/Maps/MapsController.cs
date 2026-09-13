using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Owns the Maps route's catalog and asynchronous content operations. The
/// shell supplies only platform-bound picker/process callbacks; all map
/// mutation and compilation remains behind this controller.
/// </summary>
internal sealed class MapsController : IDisposable
{
    private readonly MapCatalog _catalog;
    private readonly bool _ownsCatalog;
    private readonly IMapBuildScheduler _builds;
    private readonly bool _ownsBuilds;
    private readonly MapsPreviewCache _previews;
    private readonly bool _ownsPreviews;
    private readonly CancellationTokenSource _lifetime = new();
    private MapsState _state = MapsState.Initial;
    private long _operationGeneration;
    private int _disposed;

    internal MapsController(MapCatalog? catalog = null, MapsPreviewCache? previews = null)
    {
        _catalog = catalog ?? MapPlatformService.Shared.Catalog;
        _ownsCatalog = false;
        _builds = catalog == null ? MapPlatformService.Shared.Builds : new MapBuildScheduler();
        _ownsBuilds = catalog != null;
        _previews = previews ?? new MapsPreviewCache();
        _ownsPreviews = previews == null;
        _catalog.Changed += CatalogChanged;
        CatalogChanged(_catalog.Snapshot);
    }

    internal MapsState State => Volatile.Read(ref _state);
    internal MapCatalog Catalog => _catalog;
    internal CancellationToken LifetimeToken => _lifetime.Token;
    internal long OperationGeneration => Interlocked.Read(ref _operationGeneration);
    internal event EventHandler? Changed;

    internal void SelectTab(MapsTab tab)
    {
        if (Volatile.Read(ref _disposed) != 0 || State.Tab == tab) return;
        Publish(State with { Tab = tab, Error = null, Status = null });
    }

    internal void Select(InstalledMap? map)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Publish(State with { SelectedIdentity = map?.ContentIdentity });
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        long generation = Interlocked.Increment(ref _operationGeneration);
        Publish(State with { Loading = true, Error = null, Status = "Refreshing maps…" });
        using CancellationTokenSource linked = Link(cancellationToken);
        try
        {
            await _catalog.RefreshAsync(linked.Token).ConfigureAwait(false);
            if (!IsCurrent(generation)) return;
            Publish(State with
            {
                Catalog = _catalog.Snapshot,
                Loading = false,
                Error = null,
                Status = $"{_catalog.Snapshot.Maps.Length} maps available"
            });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (IsCurrent(generation))
                Publish(State with { Loading = false, Status = null });
        }
        catch (Exception exception)
        {
            if (IsCurrent(generation))
                Publish(State with { Loading = false, Error = exception.Message,
                    Status = "Map catalog refresh failed." });
        }
    }

    internal async Task<InstalledMap?> InstallAsync(string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (Volatile.Read(ref _disposed) != 0) return null;
        long generation = Interlocked.Increment(ref _operationGeneration);
        Publish(State with { Loading = true, Error = null, Status = "Installing map…" });
        using CancellationTokenSource linked = Link(cancellationToken);
        try
        {
            InstalledMap map = await _catalog.InstallAsync(packagePath, linked.Token)
                .ConfigureAwait(false);
            if (IsCurrent(generation))
                Publish(State with { Catalog = _catalog.Snapshot, Loading = false,
                    Error = null, Status = $"Installed {map.DisplayName}." });
            return map;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (IsCurrent(generation)) Publish(State with { Loading = false, Status = null });
            return null;
        }
        catch (Exception exception)
        {
            if (IsCurrent(generation))
                Publish(State with { Loading = false, Error = exception.Message,
                    Status = "Map installation failed." });
            return null;
        }
    }

    internal async Task<MapBuildResult> BuildAsync(InstalledMap map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (Volatile.Read(ref _disposed) != 0) return FailedBuild("Maps route is closed.");
        long generation = Interlocked.Increment(ref _operationGeneration);
        Publish(State with { Loading = true, Error = null,
            Status = $"Building {map.DisplayName}…" });
        using CancellationTokenSource linked = Link(cancellationToken);
        try
        {
            _catalog.PublishBuildState(map.ContentIdentity, MapBuildState.Building, null, []);
            MapBuildResult result = await _builds.BuildAsync(map.Project,
                new MapBuildOptions
                {
                    CacheDirectory = MapStoragePaths.MapCache,
                    BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash
                }, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            _catalog.PublishBuildState(map.ContentIdentity,
                result.Success ? MapBuildState.Ready : result.FailureKind.ToBuildState(),
                result.Statistics, result.Diagnostics);
            if (IsCurrent(generation))
                Publish(State with { Catalog = _catalog.Snapshot, Loading = false,
                    Error = result.Success ? null : BuildError(result),
                    Status = result.Success ? $"{map.DisplayName} is ready."
                        : $"{map.DisplayName} failed validation." });
            return result;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (IsCurrent(generation)) Publish(State with { Loading = false, Status = null });
            return FailedBuild("Map build was canceled.");
        }
        catch (Exception exception)
        {
            if (IsCurrent(generation))
            {
                _catalog.PublishBuildState(map.ContentIdentity, MapBuildState.NeedsBuild, null,
                    [new("MAP-CMP-999", MapDiagnosticSeverity.Error, exception.Message)]);
                Publish(State with { Catalog = _catalog.Snapshot, Loading = false,
                    Error = exception.Message, Status = "Map build failed." });
            }
            return FailedBuild(exception.Message);
        }
    }

    internal async Task<MapBundleWriteResult?> ExportAsync(InstalledMap map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (Volatile.Read(ref _disposed) != 0) return null;
        MapBuildResult result = await BuildAsync(map, cancellationToken).ConfigureAwait(false);
        if (!result.Success || Volatile.Read(ref _disposed) != 0) return null;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            MapBundleWriteResult package = await Task.Run(() => MapPackageBuilder.Cook(
                map.Project, map.SourcePath,
                Path.ChangeExtension(map.SourcePath, MapBundle.Extension),
                cancellationToken: linked.Token), linked.Token).ConfigureAwait(false);
            Publish(State with { Loading = false, Error = null,
                Status = $"Exported {Path.GetFileName(package.Path)}." });
            return package;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || _lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            Publish(State with { Loading = false, Error = exception.Message,
                Status = "Map export failed." });
            return null;
        }
    }

    internal async Task<bool> RemoveAsync(InstalledMap map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (Volatile.Read(ref _disposed) != 0) return false;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            await _catalog.RemoveAsync(map.ContentIdentity, linked.Token)
                .ConfigureAwait(false);
            Publish(State with { Catalog = _catalog.Snapshot, Error = null,
                Status = $"Removed {map.DisplayName}." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || _lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            Publish(State with { Error = exception.Message, Status = "Map removal failed." });
            return false;
        }
    }

    internal async Task<string?> CreateProjectAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return null;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            string projectPath = await Task.Run(CreateProjectCore, linked.Token)
                .ConfigureAwait(false);
            await RefreshAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            Publish(State with { Error = null, Status = "Map project created." });
            return projectPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || _lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            Publish(State with { Error = exception.Message, Status = "Map creation failed." });
            return null;
        }
    }

    internal async Task<string?> ImportQ3Async(string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (Volatile.Read(ref _disposed) != 0) return null;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            string projectPath = await Task.Run(() => ImportQ3Core(sourcePath), linked.Token)
                .ConfigureAwait(false);
            await RefreshAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            Publish(State with { Error = null, Status = "Q3 map imported." });
            return projectPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || _lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            Publish(State with { Error = exception.Message, Status = "Q3 import failed." });
            return null;
        }
    }

    internal async Task<bool> AddTextureAsync(InstalledMap map, string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (Volatile.Read(ref _disposed) != 0) return false;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            await Task.Run(() => AddTextureCore(map, sourcePath, linked.Token), linked.Token)
                .ConfigureAwait(false);
            await RefreshAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            Publish(State with { Error = null, Status = "Map texture added." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || _lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            Publish(State with { Error = exception.Message, Status = "Texture import failed." });
            return false;
        }
    }

    internal async Task<MapsPreviewLease?> LoadPreviewAsync(InstalledMap map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.PreviewPath is not { Length: > 0 } previewPath
            || Volatile.Read(ref _disposed) != 0) return null;
        string key = $"{map.ContentIdentity.ContentHash}:{previewPath}";
        using CancellationTokenSource linked = Link(cancellationToken);
        return await _previews.LoadAsync(key,
            token => ReadPreviewBytesAsync(previewPath, token), linked.Token)
            .ConfigureAwait(false);
    }

    internal void SetError(string message)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        string value = String.IsNullOrWhiteSpace(message) ? "The map operation failed." : message.Trim();
        Publish(State with { Loading = false, Error = value, Status = value });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _catalog.Changed -= CatalogChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_ownsPreviews) _previews.Dispose();
        if (_ownsBuilds && _builds is IDisposable disposableBuilds) disposableBuilds.Dispose();
        if (_ownsCatalog) _catalog.Dispose();
    }

    private void CatalogChanged(MapCatalogSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        MapsState state = State;
        MapContentIdentity? selected = state.SelectedIdentity != null
            && snapshot.Find(state.SelectedIdentity) != null ? state.SelectedIdentity : null;
        Publish(state with { Catalog = snapshot, SelectedIdentity = selected });
    }

    private string CreateProjectCore()
    {
        string id;
        string directory;
        do
        {
            string suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            id = "community.map-" + suffix.ToLowerInvariant() + "-"
                + Guid.NewGuid().ToString("N")[..8];
            directory = Path.Combine(MapStoragePaths.Projects, id);
        }
        while (Directory.Exists(directory));

        string projectPath = Path.Combine(directory, "map.project.json");
        Directory.CreateDirectory(directory);
        MapProject project = NewProject(id, "New Map");
        MapProjectIO.Save(project, projectPath);
        return projectPath;
    }

    private static string ImportQ3Core(string sourcePath)
    {
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException(
            "The selected Quake 3 map was not found.", source);
        string name = Path.GetFileNameWithoutExtension(source);
        string canonicalName = MapIdentity.FromLegacyName(name)["legacy.".Length..];
        string stableId = "community." + canonicalName;
        string directory = Path.Combine(MapStoragePaths.Projects, stableId);
        if (Directory.Exists(directory))
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            int maxBase = Math.Max(3, 63 - suffix.Length - 1 - "community.".Length);
            string baseName = canonicalName.Length > maxBase
                ? canonicalName[..maxBase].TrimEnd('-', '.', '_') : canonicalName;
            stableId = "community." + baseName + "-" + suffix;
            directory = Path.Combine(MapStoragePaths.Projects, stableId);
        }
        string projectPath = Path.Combine(directory, "map.project.json");
        Directory.CreateDirectory(directory);
        MapProject project = Q3MapProjectFactory.Create(source, projectPath, stableId, name);
        MapProjectIO.Save(project, projectPath);
        return projectPath;
    }

    private static void AddTextureCore(InstalledMap map, string sourcePath,
        CancellationToken cancellationToken)
    {
        if (map.Project.Authoring == null)
            throw new InvalidOperationException("This map does not support authoring textures.");
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException(
            "The selected texture was not found.", source);
        string projectDirectory = Path.GetDirectoryName(map.SourcePath)
            ?? throw new InvalidOperationException("Map project directory is unavailable.");
        string textures = Path.Combine(projectDirectory, "textures");
        Directory.CreateDirectory(textures);
        string extension = Path.GetExtension(source).ToLowerInvariant();
        string stem = MapIdentity.FromLegacyName(Path.GetFileNameWithoutExtension(source))
            ["legacy.".Length..];
        if (stem.Length > 50) stem = stem[..50].TrimEnd('-', '.', '_');
        string materialId = "material." + stem;
        for (int suffix = 2; map.Project.Authoring.Materials.Any(value => value.Id == materialId); suffix++)
            materialId = $"material.{stem}-{suffix}";
        string destination = Path.Combine(textures,
            materialId["material.".Length..] + extension);
        string temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan))
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            map.Project.Authoring.Materials.Add(new MapAuthoringMaterial
            {
                Id = materialId,
                Name = Path.GetFileNameWithoutExtension(source),
                CustomImage = Path.GetRelativePath(projectDirectory, destination).Replace('\\', '/'),
                Tiling = 16,
                Terrain = "Metal"
            });
            MapProjectIO.Save(map.Project, map.SourcePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<byte[]?> ReadPreviewBytesAsync(string previewPath,
        CancellationToken cancellationToken)
    {
        int separator = previewPath.IndexOf("::", StringComparison.Ordinal);
        if (separator >= 0)
        {
            string package = previewPath[..separator];
            string entry = previewPath[(separator + 2)..];
            return await Task.Run(() => new MapBundleReader().Read(package)
                .ReadDeclaredFile(entry), cancellationToken).ConfigureAwait(false);
        }
        try
        {
            return await File.ReadAllBytesAsync(previewPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static MapProject NewProject(string id, string name)
    {
        var project = new MapProject
        {
            StableId = id,
            Metadata = new MapProjectMetadata { Name = name, Author = Environment.UserName },
            Environment = new MapEnvironment { KillHeight = -10 },
            Map = new MapDefinition { Name = name.ToUpperInvariant(), InGameName = name,
                TextureSource = "MP3 PROVING GROUND", KillHeight = -10 },
            Authoring = new MapAuthoringScene()
        };
        project.Authoring.Materials.Add(new MapAuthoringMaterial { Id = "material.default",
            Name = "Default", SourceRoom = project.Map.TextureSource, SourceMaterial = 1 });
        project.Authoring.Brushes.Add(ConvexBrushFactory.Box("brush.floor", "material.default",
            new OpenTK.Mathematics.Vector3(16, 1, 16)));
        project.Authoring.Brushes[0].Transform.Position = [0, -0.5f, 0];
        for (int index = 0; index < 4; index++)
            project.Authoring.Entities.Add(new MapEntityDefinition { Id = $"spawn.{index + 1}",
                Kind = MapEntityKind.PlayerSpawn,
                Transform = new MapTransform { Position = [index % 2 == 0 ? -3 : 3, 0.1f,
                    index < 2 ? -3 : 3] } });
        return project;
    }

    private CancellationTokenSource Link(CancellationToken cancellationToken)
        => cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

    private bool IsCurrent(long generation)
        => Volatile.Read(ref _disposed) == 0
            && generation == Interlocked.Read(ref _operationGeneration);

    private void Publish(MapsState state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _state, state);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string BuildError(MapBuildResult result)
        => result.Diagnostics.IsDefaultOrEmpty ? "Map build failed validation."
            : result.Diagnostics.FirstOrDefault(value => value.Severity == MapDiagnosticSeverity.Error)
                ?.Message ?? "Map build failed validation.";

    private static MapBuildResult FailedBuild(string message)
        => new(false, false, "", null, null,
            [new("MAP-CMP-999", MapDiagnosticSeverity.Error, message)], null, []);
}
