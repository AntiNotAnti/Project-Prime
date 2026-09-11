using System.Collections.Concurrent;
using MphRead;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Worker.Maps;

/// <summary>Single-flight, asynchronous preparation of exact custom map content.</summary>
internal sealed class WorkerMapBuildService : IDisposable
{
    internal const int MaximumPreparedEntries = 64;
    internal sealed record PreparedMapContent(
        MapContentIdentity Identity,
        string BuildFingerprint,
        string CachePath,
        MapDefinition RuntimeDefinition);

    private readonly WorkerOptions _options;
    private readonly WorkerContent _baseContent;
    private readonly IMapBuildScheduler _scheduler;
    private readonly bool _ownsScheduler;
    private readonly ConcurrentDictionary<MapContentIdentity, Lazy<Task<PreparedMapContent>>> _builds = new();
    private readonly ConcurrentQueue<MapContentIdentity> _preparedOrder = new();

    public WorkerMapBuildService(WorkerOptions options, WorkerContent baseContent,
        IMapBuildScheduler? scheduler = null)
    {
        _options = options;
        _baseContent = baseContent;
        _scheduler = scheduler ?? new MapBuildScheduler();
        _ownsScheduler = scheduler == null;
    }

    internal int PreparedCount => _builds.Count;

    public async Task<MatchContentSnapshot?> PrepareAsync(ContentIdentity content,
        CancellationToken cancellationToken)
    {
        MapRequirement? required = content.RequiredMap;
        if (required == null) return null;
        required.Validate();
        var identity = new MapContentIdentity(
            new MapIdentity(required.StableId, MapVersion.Parse(required.Version)), required.ContentHash);
        Lazy<Task<PreparedMapContent>> build = _builds.GetOrAdd(identity, _ => new(
            () => BuildAsync(content.MapKey, required, identity), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            PreparedMapContent prepared = await build.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            MatchContentSnapshot snapshot = ContentEnvironment.CreateMapSnapshot(prepared.Identity,
                prepared.BuildFingerprint, prepared.CachePath, prepared.RuntimeDefinition,
                _options.BuildVersion + ":" + _options.ProtocolVersion);
            if (snapshot.MatchContentIdentity != required.MatchContentHash)
                throw new MapCompilationException("Compiled map content identity does not match admission.");
            return snapshot;
        }
        catch
        {
            if (build.IsValueCreated && build.Value.IsCompleted)
                _builds.TryRemove(new(identity, build));
            throw;
        }
    }

    private async Task<PreparedMapContent> BuildAsync(string mapKey, MapRequirement required,
        MapContentIdentity identity)
    {
        InstalledMap map = CustomRooms.Find(mapKey)
            ?? throw new MapDependencyException("Required map is not installed.");
        if (map.Source is not (MapInstallSource.InstalledPackage or MapInstallSource.BundledPackage)
            || map.ContentIdentity.Identity.StableId != required.StableId
            || map.ContentIdentity.Identity.Version.ToString() != required.Version
            || map.ContentIdentity.ContentHash != required.ContentHash
            || map.ArtifactHash != required.ArtifactHash
            || map.PackageSize != required.PackageSize)
            throw new MapDependencyException("Installed map does not match the required acquisition identity.");
        string expectedMatchHash = MapRequirement.ComputeMatchContentHash(_baseContent.ContentHash,
            required.StableId, required.Version, required.ContentHash,
            _options.BuildVersion, _options.ProtocolVersion);
        if (expectedMatchHash != required.MatchContentHash)
            throw new MapDependencyException("Required map does not match this Worker content identity.");

        MapBuildResult result = await _scheduler.BuildAsync(map.Project,
            new MapBuildOptions
            {
                CacheDirectory = MapStoragePaths.MapCache,
                BaseContentIdentity = _baseContent.ContentHash
            }, CancellationToken.None).ConfigureAwait(false);
        if (!result.Success || result.CachePath == null || result.ContentIdentity != map.ContentIdentity)
            throw new MapCompilationException("Required map compilation failed.", result.Diagnostics);
        var prepared = new PreparedMapContent(map.ContentIdentity, result.BuildFingerprint,
            result.CachePath, map.Project.Map);
        _preparedOrder.Enqueue(identity);
        while (_builds.Count > MaximumPreparedEntries
            && _preparedOrder.TryDequeue(out MapContentIdentity? oldest))
            _builds.TryRemove(oldest, out _);
        return prepared;
    }

    public void Dispose()
    {
        if (_ownsScheduler && _scheduler is IDisposable disposable) disposable.Dispose();
        _builds.Clear();
    }
}
