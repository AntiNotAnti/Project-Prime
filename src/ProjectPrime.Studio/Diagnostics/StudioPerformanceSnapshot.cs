using System.Diagnostics;
using ProjectPrime.Studio.Jobs;

namespace ProjectPrime.Studio.Diagnostics;

/// <summary>Null denotes an unmeasured value, rather than a fabricated zero.</summary>
public sealed record StudioRenderPerformance(double? CpuMilliseconds, double? GpuMilliseconds,
    int? DrawCalls, int? BatchCount, long? VisiblePrimitives, long? GeometryUploadBytes,
    long? GeometryResidentBytes, int? ResidentMeshes, int? ResidentTextures, long? TextureBytes,
    long? FrameReadbackBytes, long? PickReadbackBytes, int? DeviceGeneration);

public sealed record StudioReplayPerformance(double? SeekMilliseconds, double? AdvanceMilliseconds,
    double? RenderMilliseconds, double? CheckpointCaptureMilliseconds, uint? SeekRestoreFrame,
    int? SeekSimulationSteps, int CheckpointCount, long CheckpointBytes,
    int? RejectedCheckpoints, string? CheckpointSource);

public sealed record StudioCachePerformance(int Count, long Bytes, int PendingJobs,
    long CompilationCount, int PreparationPeak, double PreparationMilliseconds);

public sealed record StudioGraphicsResources(bool DeviceCreated, int Worlds, int NativeSurfaces, int ViewportSurfaces);

public sealed record StudioPerformanceSourceSnapshot(string Name, StudioRenderPerformance? Render = null,
    StudioReplayPerformance? Replay = null, StudioCachePerformance? Cache = null,
    StudioGraphicsResources? Graphics = null, string? Detail = null);

public sealed record StudioPerformanceJob(Guid Id, string Title, StudioJobState State,
    double Fraction, TimeSpan Elapsed, string? Error);

public sealed record StudioPerformanceSnapshot(DateTimeOffset CapturedAt, long ProcessWorkingSetBytes,
    long ManagedBytes, IReadOnlyList<StudioPerformanceJob> Jobs,
    IReadOnlyList<StudioPerformanceSourceSnapshot> Sources)
{
    public int RunningJobs => Jobs.Count(job => job.State == StudioJobState.Running);
}

/// <summary>Captures immutable counters on the caller's thread; it never creates a graphics device.</summary>
public sealed class StudioPerformanceCollector(StudioJobManager jobs) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, (string Name, Func<StudioPerformanceSourceSnapshot?> Capture)> _sources = [];
    private long _nextId;
    private bool _disposed;
    public int SourceCount { get { lock (_gate) return _sources.Count; } }

    public IDisposable Register(string name, Func<StudioPerformanceSourceSnapshot?> capture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(capture);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long id = ++_nextId;
            _sources.Add(id, (name, capture));
            return new Registration(this, id);
        }
    }

    public StudioPerformanceSnapshot Capture()
    {
        (string Name, Func<StudioPerformanceSourceSnapshot?> Capture)[] providers;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            providers = _sources.Values.ToArray();
        }
        var values = new List<StudioPerformanceSourceSnapshot>(providers.Length);
        foreach (var provider in providers)
        {
            try
            {
                if (provider.Capture() is { } value) values.Add(value);
            }
            catch (Exception ex)
            {
                // A closing document can lose its native resources between UI updates.
                // Preserve the reason and leave counters unknown instead of retaining stale values.
                values.Add(new(provider.Name, Detail: $"Unavailable: {ex.Message}"));
            }
        }
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var jobValues = jobs.Jobs.Select(job => new StudioPerformanceJob(job.Id, job.Title,
            job.State, job.Progress.Fraction, job.Elapsed, job.Error)).ToArray();
        return new(DateTimeOffset.UtcNow, process.WorkingSet64, GC.GetTotalMemory(false), jobValues, values);
    }

    private void Remove(long id) { lock (_gate) _sources.Remove(id); }
    public void Dispose() { lock (_gate) { _disposed = true; _sources.Clear(); } }
    private sealed class Registration(StudioPerformanceCollector owner, long id) : IDisposable
    {
        private StudioPerformanceCollector? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Remove(id);
    }
}
