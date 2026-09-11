using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Editor.App;

public sealed class EditorBuildService
{
    private readonly MapCompiler _compiler = new();

    public string CacheDirectory { get; }
    public string BaseContentIdentity { get; private set; } = "unconfigured";
    public bool HasContent { get; private set; }

    public EditorBuildService(string? contentDirectory, string contentVersion,
        string? cacheDirectory = null)
    {
        CacheDirectory = Path.GetFullPath(cacheDirectory ?? MapStoragePaths.MapCache);
        if (!string.IsNullOrWhiteSpace(contentDirectory))
        {
            ContentEnvironment.Open(Path.GetFullPath(contentDirectory), contentVersion);
            BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash;
            HasContent = true;
        }
    }

    public Task<MapBuildResult> BuildAsync(MapProject project, bool force,
        CancellationToken cancellationToken)
    {
        if (!HasContent)
        {
            return Task.FromResult(new MapBuildResult(false, false, "", null, null,
                ImmutableArray.Create(new MapDiagnostic("MAP-DEP-010", MapDiagnosticSeverity.Error,
                    "Base game content is required to compile this map.",
                    SuggestedAction: "Start the editor with --content-dir <extracted AMHE1>.")),
                null, []));
        }
        return _compiler.CompileAsync(project, new MapBuildOptions
        {
            CacheDirectory = CacheDirectory,
            BaseContentIdentity = BaseContentIdentity,
            Force = force
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
}
