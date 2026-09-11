using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Editor.App;

public sealed class EditorBuildService : IDisposable
{
    private readonly IMapBuildScheduler _builds;
    private readonly bool _ownsBuilds;

    public string CacheDirectory { get; }
    public string BaseContentIdentity { get; private set; } = "unconfigured";
    public bool HasContent { get; private set; }

    public EditorBuildService(string? contentDirectory, string contentVersion,
        string? cacheDirectory = null, IMapBuildScheduler? builds = null)
    {
        _builds = builds ?? new MapBuildScheduler();
        _ownsBuilds = builds == null;
        CacheDirectory = Path.GetFullPath(cacheDirectory ?? MapStoragePaths.MapCache);
        if (!string.IsNullOrWhiteSpace(contentDirectory))
        {
            ContentEnvironment.Open(Path.GetFullPath(contentDirectory), contentVersion);
            BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash;
            HasContent = true;
        }
    }

    public Task<MapBuildResult> BuildAsync(MapProject project, bool force,
        CancellationToken cancellationToken, IProgress<MapBuildProgress>? progress = null)
    {
        MapDependencyAnalysis dependencies = MapDependencyAnalyzer.Analyze(project);
        if (dependencies.RequiresBaseContent && !HasContent)
        {
            return Task.FromResult(new MapBuildResult(false, false, "", null, null,
                ImmutableArray.Create(new MapDiagnostic("MAP-DEP-010", MapDiagnosticSeverity.Error,
                    "Base game content is required to compile this map.",
                    SuggestedAction: "Start the editor with --content-dir <extracted AMHE1>.")),
                null, []));
        }
        return _builds.BuildAsync(project, new MapBuildOptions
        {
            CacheDirectory = CacheDirectory,
            BaseContentIdentity = BaseContentIdentity,
            Force = force,
            Progress = progress
        }, cancellationToken);
    }

    public async Task<MapBundleWriteResult> ExportAsync(MapProject project, string projectPath,
        string destination, CancellationToken cancellationToken)
    {
        MapBuildResult build = await BuildAsync(project, force: false, cancellationToken)
            .ConfigureAwait(false);
        if (!build.Success)
            throw new MapCompilationException("Map must compile before export.", build.Diagnostics);
        return MapPackageBuilder.Cook(project, projectPath, destination,
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsBuilds && _builds is IDisposable disposable) disposable.Dispose();
    }
}
