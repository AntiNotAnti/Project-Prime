using System.Collections.Concurrent;

namespace MphRead.Mods.MapGen;

public interface IMapBuildScheduler
{
    Task<MapBuildResult> BuildAsync(MapProject project, MapBuildOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-scoped owner for background map compilation. Identical concurrent
/// requests share one build; a caller cancellation stops only that caller's
/// wait, while scheduler disposal cancels the shared work.
/// </summary>
public sealed class MapBuildScheduler : IMapBuildScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<MapBuildResult>>> _builds = new();
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _baseContentConcurrency = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<MapProject, MapBuildOptions, CancellationToken, MapBuildResult> _compile;
    private int _disposed;
    private int _activeWaiters;

    internal int ActiveWaiters => Volatile.Read(ref _activeWaiters);

    public MapBuildScheduler(int maxConcurrentBuilds = 2)
        : this(maxConcurrentBuilds, static (project, options, token) =>
            new MapCompiler().Compile(project, options, token)) { }

    internal MapBuildScheduler(int maxConcurrentBuilds,
        Func<MapProject, MapBuildOptions, CancellationToken, MapBuildResult> compile)
    {
        if (maxConcurrentBuilds <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentBuilds));
        _compile = compile ?? throw new ArgumentNullException(nameof(compile));
        _concurrency = new SemaphoreSlim(maxConcurrentBuilds, maxConcurrentBuilds);
    }

    public async Task<MapBuildResult> BuildAsync(MapProject project, MapBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Capture synchronously before the first await. Editor mutations after
        // admission must not change either the fingerprint or the operation
        // that is eventually dispatched for that key.
        MapBuildSnapshot snapshot = MapBuildSnapshot.Capture(project);
        MapProject compilerProject = snapshot.Materialize();
        string fingerprint;
        try
        {
            fingerprint = await Task.Run(() => MapBuildFingerprint.Compute(compilerProject,
                options.BaseContentIdentity), cancellationToken).ConfigureAwait(false);
        }
        catch (MapDependencyException)
        {
            // Let the compiler convert dependency failures into its structured
            // result. The unique key prevents an invalid request from being
            // mistaken for another source while preserving the same API.
            fingerprint = "invalid-" + Guid.NewGuid().ToString("N");
        }
        string key = Path.GetFullPath(options.CacheDirectory) + "\0" + fingerprint
            + (options.Force ? "\0force" : "\0cached");
        var buildOptions = CopyOptions(options);
        Lazy<Task<MapBuildResult>> operation = _builds.GetOrAdd(key, _ => new(
            () => RunAsync(compilerProject, buildOptions), LazyThreadSafetyMode.ExecutionAndPublication));
        Interlocked.Increment(ref _activeWaiters);
        try
        {
            return await operation.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeWaiters);
            if (operation.IsValueCreated && operation.Value.IsCompleted)
                _builds.TryRemove(new(key, operation));
        }
    }

    private async Task<MapBuildResult> RunAsync(MapProject project, MapBuildOptions options)
    {
        await _concurrency.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        bool baseContentAcquired = false;
        try
        {
            if (MapDependencyAnalyzer.Analyze(project).RequiresBaseContent)
            {
                await _baseContentConcurrency.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                baseContentAcquired = true;
            }
            return await Task.Run(() => _compile(project, options, _lifetime.Token),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (baseContentAcquired) _baseContentConcurrency.Release();
            _concurrency.Release();
        }
    }

    private static MapBuildOptions CopyOptions(MapBuildOptions options) => new()
    {
        CacheDirectory = options.CacheDirectory,
        BaseContentIdentity = options.BaseContentIdentity,
        Force = options.Force,
        Verbose = options.Verbose,
        Progress = options.Progress
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
