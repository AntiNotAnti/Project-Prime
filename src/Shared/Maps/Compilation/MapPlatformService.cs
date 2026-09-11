namespace MphRead.Mods.MapGen;

/// <summary>
/// One process-scoped ownership boundary for catalog mutation and map-build
/// scheduling. Client composition uses <see cref="Shared"/>; tests, tools,
/// Workers, and the editor may create an explicitly scoped instance.
/// </summary>
public sealed class MapPlatformService : IDisposable
{
    private static readonly Lazy<MapPlatformService> Process = new(() =>
        new MapPlatformService(null, new MapBuildScheduler(),
            ownsCatalog: false, ownsBuilds: true));
    private readonly MapCatalog? _catalog;
    private readonly bool _ownsCatalog;
    private readonly bool _ownsBuilds;
    private int _disposed;

    public static MapPlatformService Shared => Process.Value;
    public MapCatalog Catalog => _catalog ?? (MapCatalog)CustomRooms.Catalog;
    public IMapBuildScheduler Builds { get; }

    public MapPlatformService(MapCatalogOptions catalogOptions, int maxConcurrentBuilds = 2)
        : this(new MapCatalog(catalogOptions), new MapBuildScheduler(maxConcurrentBuilds),
            ownsCatalog: true, ownsBuilds: true) { }

    public MapPlatformService(MapCatalog? catalog, IMapBuildScheduler builds,
        bool ownsCatalog = false, bool ownsBuilds = false)
    {
        _catalog = catalog;
        Builds = builds ?? throw new ArgumentNullException(nameof(builds));
        _ownsCatalog = ownsCatalog;
        _ownsBuilds = ownsBuilds;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsBuilds && Builds is IDisposable disposableBuilds) disposableBuilds.Dispose();
        if (_ownsCatalog) _catalog!.Dispose();
    }
}
