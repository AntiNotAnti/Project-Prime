using System.Collections.Concurrent;
using MphRead;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Worker.Maps;

/// <summary>Single-flight, asynchronous preparation of exact custom map content.</summary>
internal sealed class WorkerMapBuildService
{
    private readonly WorkerOptions _options;
    private readonly WorkerContent _baseContent;
    private readonly ConcurrentDictionary<MapContentIdentity, Lazy<Task<MatchContentSnapshot>>> _builds = new();

    public WorkerMapBuildService(WorkerOptions options, WorkerContent baseContent)
    {
        _options = options;
        _baseContent = baseContent;
    }

    public async Task<MatchContentSnapshot?> PrepareAsync(ContentIdentity content,
        CancellationToken cancellationToken)
    {
        MapRequirement? required = content.RequiredMap;
        if (required == null) return null;
        required.Validate();
        var identity = new MapContentIdentity(
            new MapIdentity(required.StableId, MapVersion.Parse(required.Version)), required.ContentHash);
        Lazy<Task<MatchContentSnapshot>> build = _builds.GetOrAdd(identity, _ => new(
            () => BuildAsync(content.MapKey, required), LazyThreadSafetyMode.ExecutionAndPublication));
        try { return await build.Value.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (build.IsValueCreated && build.Value.IsCompleted)
                _builds.TryRemove(new(identity, build));
            throw;
        }
    }

    private async Task<MatchContentSnapshot> BuildAsync(string mapKey, MapRequirement required)
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

        MapBuildResult result = await new MapCompiler().CompileAsync(map.Project,
            new MapBuildOptions
            {
                CacheDirectory = MapStoragePaths.MapCache,
                BaseContentIdentity = _baseContent.ContentHash
            }, CancellationToken.None).ConfigureAwait(false);
        if (!result.Success || result.CachePath == null || result.ContentIdentity != map.ContentIdentity)
            throw new MapCompilationException("Required map compilation failed.", result.Diagnostics);
        MatchContentSnapshot snapshot = ContentEnvironment.CreateMapSnapshot(map.ContentIdentity,
            result.BuildFingerprint, result.CachePath, map.Project.Map,
            _options.BuildVersion + ":" + _options.ProtocolVersion);
        if (snapshot.MatchContentIdentity != required.MatchContentHash)
            throw new MapCompilationException("Compiled map content identity does not match admission.");
        return snapshot;
    }
}
