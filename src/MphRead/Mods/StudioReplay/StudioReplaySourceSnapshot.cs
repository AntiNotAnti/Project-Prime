#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using MphRead.Mods.Replay;

namespace MphRead.Mods.StudioReplay;

public sealed partial class StudioReplayPlayer
{
    private string? _playbackContentHash;
    private string? _preparedLogicalContentHash, _preparedSourceContentHash;
    /// <summary>Exact immutable recording or clip descriptor identity captured by preparation.</summary>
    public string? PresentationSourceHash => Volatile.Read(ref _preparedLogicalContentHash);
    private ReplayVirtualClipDocument? _preparedClipDescriptor;
    private ReplayCameraTrackIdentity? _preparedCameraIdentity;
    private byte[]? _preparedCameraState;
    private string? _cameraLoadError;
    private (long Length, long LastWriteTicks)? _preparedLogicalStamp;
    private readonly object _snapshotGate = new();
    private IDisposable? _sourcePin;
    private bool _snapshotDisposed;
    /// <summary>The existing detached preparation worker captures one stable private source.</summary>
    private PreparedReplaySource PrepareImmutablePlaybackSource(CancellationToken cancellation)
    {
        if (_playbackPath is { } prepared) return PreparedReplaySource.File(prepared, cancellation: cancellation);
        PruneIdleReplayCache(cancellation);
        string snapshots = Path.Combine(_cacheRoot, "sources");
        var information = new FileInfo(LogicalPath);
        var stamp = (information.Length, information.LastWriteTimeUtc.Ticks);
        _preparedLogicalStamp = stamp;
        if (!LogicalPath.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
        {
            using var captured = StudioReplaySnapshotCache.CapturePinned(LogicalPath, snapshots, cancellation);
            _preparedLogicalContentHash = _preparedSourceContentHash = SnapshotHash(captured.Path);
            PrepareCamera(stamp);
            return PrepareSnapshot(captured, cancellation);
        }

        if (new FileInfo(LogicalPath).Length > 512 * 1024) throw new InvalidDataException("Virtual replay clip descriptor exceeds its size limit.");
        using var descriptor = StudioReplaySnapshotCache.CapturePinned(LogicalPath, Path.Combine(_cacheRoot, "clip-descriptors"), cancellation, "source.ppclip");
        if (!ReplayVirtualClips.TryLoad(descriptor.Path, out var clip) || clip == null)
            throw new InvalidDataException("Virtual replay clip descriptor is invalid.");
        using var original = StudioReplaySnapshotCache.CapturePinned(clip.SourceReplay, snapshots, cancellation);
        if (clip.SourceContentHash != null && !StringComparer.OrdinalIgnoreCase.Equals(clip.SourceContentHash, SnapshotHash(original.Path)))
            throw new InvalidDataException("The source recording was replaced or changed. This replay project requires its original exact recording.");
        _preparedClipDescriptor = clip;
        _preparedLogicalContentHash = SnapshotHash(descriptor.Path); _preparedSourceContentHash = SnapshotHash(original.Path);
        PrepareCamera(stamp);
        string directory = Path.Combine(_cacheRoot, "clip-extraction", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "source.ppdemo");
            var result = ReplayArchive.Extract(original.Path, clip.StartFrame, clip.EndFrame, output, cancellation);
            if (result != ReplayOpenResult.Success) throw new IOException("Cannot materialize replay clip: " + result);
            using var immutable = StudioReplaySnapshotCache.CapturePinned(output, snapshots, cancellation);
            return PrepareSnapshot(immutable, cancellation);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private void PrepareCamera((long Length, long LastWriteTicks) stamp)
    {
        _preparedCameraIdentity = new ReplayCameraTrackIdentity(LogicalPath, Convert.FromHexString(_preparedSourceContentHash!),
            _preparedClipDescriptor?.StartFrame ?? 0, _preparedClipDescriptor?.EndFrame ?? 0);
        var camera = new ReplayCameraTrack();
        if (!camera.LoadBound(LogicalPath, _preparedCameraIdentity.Value, stamp)) _cameraLoadError = camera.LastError;
        _preparedCameraState = camera.ExportState();
    }
    private PreparedReplaySource PrepareSnapshot(StudioReplaySnapshotCache.Snapshot captured, CancellationToken cancellation)
    {
        IDisposable? pin = captured.TakePin(); PreparedReplaySource? source = null;
        try
        {
            source = PreparedReplaySource.File(captured.Path, cancellation: cancellation);
            cancellation.ThrowIfCancellationRequested();
            lock (_snapshotGate)
            {
                ObjectDisposedException.ThrowIf(_snapshotDisposed, this);
                _playbackContentHash = SnapshotHash(captured.Path); _sourcePin = pin; pin = null;
            }
            return source;
        }
        catch { source?.Dispose(); throw; }
        finally { pin?.Dispose(); }
    }
    private void PruneIdleReplayCache(CancellationToken cancellation)
    {
        var queued = StudioReplayCachePins.ProtectedDirectories(_cacheRoot);
        bool protectedDirectory(string directory) => queued(directory)
            || LogicalPath.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        MapDiskCache.Prune(Path.Combine(_cacheRoot, "sources"), 512L * 1024 * 1024, TimeSpan.FromDays(7),
            cancellation: cancellation, protectedDirectory: protectedDirectory);
        MapDiskCache.Prune(Path.Combine(_cacheRoot, "packages"), 2L * 1024 * 1024 * 1024, TimeSpan.FromDays(30),
            cancellation: cancellation, protectedDirectory: protectedDirectory);
        MapDiskCache.Prune(Path.Combine(_cacheRoot, "clip-descriptors"), 64L * 1024 * 1024, TimeSpan.FromDays(7),
            cancellation: cancellation, protectedDirectory: protectedDirectory);
    }
    private static string SnapshotHash(string path) => Path.GetFileName(Path.GetDirectoryName(path)!);
    /// <summary>Background jobs retain the already-owned kernel pins before
    /// admission. This short managed operation cannot wait on filesystem owners.</summary>
    private IDisposable RetainJobResources()
    {
        RequireOwner(); IDisposable source;
        lock (_snapshotGate)
        {
            ObjectDisposedException.ThrowIf(_snapshotDisposed, this);
            source = MapDiskCache.Retain(_sourcePin ?? throw new InvalidOperationException("Wait for immutable replay preparation before starting this job."));
        }
        try { return new ReplayJobResources(source, _resources.RetainPackagePin()); }
        catch { source.Dispose(); throw; }
    }
    private sealed class ReplayJobResources(IDisposable source, IDisposable? package) : IDisposable
    {
        private IDisposable? _source = source, _package = package;
        public void Dispose()
        { Interlocked.Exchange(ref _package, null)?.Dispose(); Interlocked.Exchange(ref _source, null)?.Dispose(); }
    }
    private IDisposable? StopSnapshotOwnership()
    { lock (_snapshotGate) { _snapshotDisposed = true; var pin = _sourcePin; _sourcePin = null; return pin; } }
}
#endif
