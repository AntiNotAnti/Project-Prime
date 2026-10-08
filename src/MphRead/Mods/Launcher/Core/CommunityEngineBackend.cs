using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Reuses the map service, narrow Hunter License tickets, package validator and publication fences.</summary>
public sealed class CommunityEngineBackend : ICommunityBackend
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private string _address;
    private readonly string _settingsPath;
    public string DefaultAddress => _address;

    public CommunityEngineBackend()
    {
        _settingsPath = Path.Combine(LauncherPrefs.Directory, "map-community.txt");
        _address = NetworkMapIdentity.ConfiguredDownloadSource();
        try { if (File.Exists(_settingsPath)) { string configured = File.ReadAllText(_settingsPath).Trim(); using var validate = new MapCommunityClient(configured); _address = configured; } }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }
    }
    public void SetAddress(string address)
    {
        RequireOwner();
        address = string.IsNullOrWhiteSpace(address) ? MapCommunityClient.DefaultAddress : address.Trim();
        using var validate = new MapCommunityClient(address);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, address);
        _address = address;
    }
    public MapContentIdentity? Installed(Guid mapId) => CustomRooms.Installed.TryGet(mapId, out var installed) ? installed.Identity : null;
    public void ValidateInstallation(CommunityMap? package)
    {
        RequireOwner();
        if (!GameFiles.Ready) throw new IOException("Set up game files before installing playable maps.");
        if (package != null && package.MinimumProtocol > NetConfig.ProtocolVersion) throw new InvalidDataException("Update Project Prime before installing this map.");
        MapRuntimeUsage.RequireInstallationAllowed(package?.Name);
        GameFiles.ApplyPaths();
    }
    private static readonly object CatalogGate = new();
    private static string _catalogAddress = "";
    private static Task<CommunityMapProject[]>? _catalog;
    private static long _catalogUntil;
    private static Task? _installedWarm;
    private static Task WarmInstalled()
    {
        lock (CatalogGate)
            return _installedWarm is { IsFaulted: false, IsCanceled: false } ? _installedWarm
                : _installedWarm = Task.Run(() => { _ = CustomRooms.Installed; });
    }
    public void InvalidateCatalog() { lock (CatalogGate) { _catalogUntil = 0; _catalog = null; } }
    public static void WarmPublicCatalog()
    {
        if (LauncherUiPerformance.Enabled) return;
        var backend = new CommunityEngineBackend();
        _ = WarmInstalled().ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        _ = backend.PublicCatalog(CancellationToken.None).ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
    }
    private Task<CommunityMapProject[]> PublicCatalog(CancellationToken cancellation)
    {
        Task<CommunityMapProject[]> task;
        lock (CatalogGate)
        {
            long now = Environment.TickCount64;
            if (_catalog == null || _catalogAddress != _address || _catalog.IsFaulted || _catalog.IsCanceled
                || _catalog.IsCompleted && now >= _catalogUntil)
            {
                string address = _address;
                _catalogAddress = address; _catalogUntil = now + 30000;
                _catalog = Task.Run(async () => {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var client = new MapCommunityClient(address);
                    return await client.BrowseProjectsAsync(timeout.Token, sort: "name").ConfigureAwait(false);
                });
            }
            task = _catalog;
        }
        return task.WaitAsync(cancellation);
    }
    public async Task<CommunityMapProject[]> BrowseAsync(CommunityTab tab, CancellationToken cancellation)
    {
        var catalog = tab == CommunityTab.Discover ? PublicCatalog(cancellation) :
            Client(true, cancellation, client => client.BrowseProjectsAsync(cancellation,
                mine: tab == CommunityTab.MyMaps, favorites: tab == CommunityTab.Favorites, sort: "name"));
        // Package validation/hashing must finish off-thread before the first
        // Snapshot asks for installed badges on the render/input thread.
        await Task.WhenAll(catalog, WarmInstalled().WaitAsync(cancellation)).ConfigureAwait(false);
        return await catalog.ConfigureAwait(false);
    }
    public Task<CommunityMapRevision[]> RevisionsAsync(Guid map, bool authenticated, CancellationToken cancellation) =>
        Client(authenticated, cancellation, client => client.GetRevisionsAsync(map, cancellation));
    public async Task<ICommunityInstallation> PrepareInstallAsync(CommunityMap package, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    {
        var prepared = await Client(package.Draft, cancellation, client => client.PrepareAsync(package, cancellation,
            (done, total) => progress(new(done, total, "Downloading and validating")), CustomRooms.UserMapDirectory)).ConfigureAwait(false);
        return new Installation(prepared, _owner);
    }
    public async Task<ICommunityInstallation> PrepareImportAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    {
        var identity = await Task.Run(() => MapContentIdentity.FromPackage(Path.GetFullPath(path)), cancellation).ConfigureAwait(false);
        progress(new(0, 0, "Validating and building"));
        return new Installation(await MapPackageInstaller.PrepareAsync(path, identity, cancellation, CustomRooms.UserMapDirectory).ConfigureAwait(false), _owner);
    }
    public Task FavoriteAsync(Guid map, bool favorite, CancellationToken cancellation) => Client(true, cancellation,
        async client => { await client.SetFavoriteAsync(map, favorite, cancellation).ConfigureAwait(false); return true; });
    public Task ReportAsync(Guid map, MapReportRequest report, CancellationToken cancellation) => Client(true, cancellation,
        async client => { await client.ReportAsync(map, report, cancellation).ConfigureAwait(false); return true; });
    public Task ModifyAsync(Guid map, string hash, int revision, CommunityCreatorAction action, CancellationToken cancellation) => Client(true, cancellation,
        async client =>
        {
            switch (action)
            {
                case CommunityCreatorAction.Promote: await client.PromoteRevisionAsync(map, revision, cancellation).ConfigureAwait(false); break;
                case CommunityCreatorAction.Unlist: await client.SetVisibilityAsync(hash, "Unlisted", cancellation).ConfigureAwait(false); break;
                case CommunityCreatorAction.Draft: await client.SetVisibilityAsync(hash, "Draft", cancellation).ConfigureAwait(false); break;
                case CommunityCreatorAction.DeleteRevision: await client.DeleteRevisionAsync(map, revision, cancellation).ConfigureAwait(false); break;
                case CommunityCreatorAction.RestoreRevision: await client.RestoreRevisionAsync(map, revision, cancellation).ConfigureAwait(false); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
            return true;
        });
    public Task ModifyMapAsync(Guid map, CommunityDetailAction action, CancellationToken cancellation) => Client(true, cancellation,
        async client =>
        {
            switch (action)
            {
                case CommunityDetailAction.Archive: await client.ArchiveMapAsync(map, cancellation).ConfigureAwait(false); break;
                case CommunityDetailAction.RestoreMap: await client.RestoreMapAsync(map, cancellation).ConfigureAwait(false); break;
                case CommunityDetailAction.DeleteMap: await client.DeleteMapAsync(map, cancellation).ConfigureAwait(false); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
            return true;
        });
    public async Task<ICommunityPublication> PreparePublicationAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    {
        path = Path.GetFullPath(path);
        string snapshot = Path.Combine(Path.GetTempPath(), "prime-community-" + Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            progress(new(0, 0, "Validating and building immutable package"));
            if (MapBundle.Is(path))
            {
                await using var source = File.OpenRead(path); await using var output = File.Create(snapshot);
                await MapCommunityClient.CopyBoundedAsync(source, output, MapPackageReader.MaxArchiveBytes, cancellation).ConfigureAwait(false);
            }
            else
            {
                MapProject project = await Task.Run(() => MapProjectSerializer.Load(path), cancellation).ConfigureAwait(false);
                if (project.Definition.MapId == Guid.Empty) throw new InvalidDataException("Save or upgrade this Map Studio project before publishing it.");
                await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(project), snapshot, cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            var identity = MapContentIdentity.FromPackage(snapshot);
            if (identity.MapId == Guid.Empty) throw new InvalidDataException("The prepared package has no stable map identity.");
            var definition = MapDefinition.Load(snapshot);
            var validation = await MapBuildScheduler.Shared.BuildAsync(MapBuildSnapshot.Capture(definition), cancellation).ConfigureAwait(false);
            MapCompiler.ThrowIfInvalid(validation.Validation());
            CommunityMapProject? existing = await Client(true, cancellation, client => client.GetProjectAsync(identity.MapId, cancellation)).ConfigureAwait(false);
            if (existing != null && existing.MapId != identity.MapId) throw new InvalidDataException("Community returned a different map identity.");
            return new Publication(snapshot, identity, definition.InGameName ?? definition.Name, existing);
        }
        catch { if (File.Exists(snapshot)) File.Delete(snapshot); throw; }
    }
    public Task<CommunityPublishResult> PublishAsync(ICommunityPublication publication, CommunityPublishRequest request,
        CommunityVisibility visibility, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    {
        if (publication is not Publication prepared) throw new InvalidOperationException("Use an exact package prepared by this Community backend.");
        return Client(true, cancellation, client => client.PublishAsync(prepared.Path, request, cancellation,
            listed: visibility == CommunityVisibility.Published, draft: visibility == CommunityVisibility.Draft,
            progress: (done, total) => progress(new(done, total, "Uploading immutable revision"))));
    }
    public Task DiscardPublicationAsync(ICommunityPublication publication, CancellationToken cancellation) => Client(true, cancellation,
        async client => { await client.DiscardPendingUploadAsync(publication.Identity.PackageHash.ToString(), cancellation).ConfigureAwait(false); return true; });

    private async Task<T> Client<T>(bool authenticated, CancellationToken cancellation, Func<MapCommunityClient, Task<T>> action)
    {
        string address = _address;
        if (!authenticated) { using var publicClient = new MapCommunityClient(address); return await action(publicClient).ConfigureAwait(false); }
#if !MPHREAD_SERVER
        // This is the existing narrow ticket authority. Supabase sessions never enter snapshots or the map service.
        string ticket = await HunterLicenseClient.GetCommunityMapTicketAsync(cancellation).ConfigureAwait(false);
        using (var client = new MapCommunityClient(address, ticket))
        {
            try { return await action(client).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) { }
        }
        ticket = await HunterLicenseClient.RefreshCommunityMapTicketAsync(cancellation).ConfigureAwait(false);
        using var retry = new MapCommunityClient(address, ticket);
        return await action(retry).ConfigureAwait(false);
#else
        throw new NotSupportedException("Community account operations are unavailable in the dedicated server.");
#endif
    }
    private void RequireOwner() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Map publication must run on the engine thread."); }

    private sealed class Installation(PreparedMapInstallation prepared, int owner) : ICommunityInstallation
    {
        public MapContentIdentity Identity => prepared.Identity;
        public string Commit(CancellationToken cancellation)
        {
            if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Install commit must run on the engine thread.");
            MapDefinition definition = prepared.Commit(CustomRooms.UserMapDirectory, cancellation: cancellation);
            Metadata.RegisterDownloadedMap(definition);
            return definition.Name;
        }
        public void Dispose() => prepared.Dispose();
    }
    private sealed class Publication(string path, MapContentIdentity identity, string displayName, CommunityMapProject? existing) : ICommunityPublication
    {
        private int _disposed;
        internal string Path => path;
        public MapContentIdentity Identity => identity;
        public string DisplayName => displayName;
        public CommunityMapProject? Existing => existing;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0 && File.Exists(path)) File.Delete(path); }
    }
}
