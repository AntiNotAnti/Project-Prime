#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Replay;

namespace MphRead.Mods.StudioReplay;

public sealed partial class StudioReplayPlayer
{
    private readonly ReplayCameraSidecarQueue _cameraWrites;
    private bool _cameraAuthoringClosed;
    public bool CameraEditsPending => _cameraWrites.Dirty;
    public bool CameraEditsWriting => _cameraWrites.Writing;
    public string? CameraSaveError => _cameraWrites.Error ?? _cameraLoadError;
    /// <summary>Path-free bounded canonical state, including exact cropped curves.</summary>
    public byte[] ExportCameraSidecarState() { RequireOwner(); return _camera.ExportState(); }
    public void ImportCameraSidecarState(byte[] state, bool persist = true)
    {
        RequireCameraAuthoring();
        if (!_camera.Edit(track =>
        {
            if (!track.ImportState(state)) return false;
            if (track.WindowDuration is { } duration && duration != Status.DurationFrames)
                throw new InvalidDataException("The camera sample window differs from the recording duration.");
            return true;
        })) throw new InvalidDataException(_camera.LastError);
        if (persist) QueueCameraSave(); Changed?.Invoke();
    }
    private void RequireCameraAuthoring()
    {
        RequireOwner();
        if (_cameraAuthoringClosed) throw new InvalidOperationException("Camera authoring is saving or closing.");
        if (!Status.Ready || _preparedCameraIdentity == null)
            throw new InvalidOperationException("Wait for replay preparation before editing cameras.");
    }
    private void QueueCameraSave() { _cameraLoadError = null; _cameraWrites.Enqueue(_camera.ExportState()); }
    private void PersistCameraState(byte[] state, Func<Action, bool> publish)
    {
        // Identity was hashed on the preparation worker. Editing/serialization never
        // scans the recording. The sidecar binds to those exact original bytes.
        ReplayCameraTrackIdentity identity; (long Length, long LastWriteTicks) stamp;
        lock (_snapshotGate)
        {
            identity = _preparedCameraIdentity ?? throw new InvalidOperationException("The prepared camera identity is unavailable.");
            stamp = _preparedLogicalStamp ?? throw new InvalidOperationException("The prepared recording stamp is unavailable.");
        }
        var current = new FileInfo(LogicalPath);
        if (!current.Exists || current.Length != stamp.Length || current.LastWriteTimeUtc.Ticks != stamp.LastWriteTicks)
            throw new IOException("The recording or clip descriptor changed. Restore its original source before saving these camera edits.");
        var snapshot = new ReplayCameraTrack();
        if (!snapshot.ImportState(state) || !snapshot.SaveBound(LogicalPath, identity, publish)) throw new IOException(snapshot.LastError);
    }
    public async Task FlushCameraEditsAsync(CancellationToken cancellation = default)
    {
        if (_cameraWrites.Dirty) await ValidateCameraRetrySourceAsync(cancellation).ConfigureAwait(false);
        _cameraWrites.Retry();
        await _cameraWrites.FlushAsync(cancellation).ConfigureAwait(false);
    }
    private Task ValidateCameraRetrySourceAsync(CancellationToken cancellation) => Task.Run(() =>
    {
        string hash; (long Length, long LastWriteTicks) stamp;
        lock (_snapshotGate)
        {
            hash = _preparedLogicalContentHash ?? throw new InvalidOperationException("The prepared recording identity is unavailable.");
            stamp = _preparedLogicalStamp ?? throw new InvalidOperationException("The prepared recording stamp is unavailable.");
        }
        cancellation.ThrowIfCancellationRequested();
        var before = new FileInfo(LogicalPath);
        if (!before.Exists) throw new IOException("The recording or clip descriptor is missing. Restore its original source before saving these camera edits.");
        var observed = (before.Length, before.LastWriteTimeUtc.Ticks);
        if (observed == stamp) return;
        // Only an explicit save/close retry may verify changed metadata. Ordinary
        // camera edits never scan the recording, and this verification stays detached.
        ReplaySourceHash.Verify(LogicalPath, hash, cancellation);
        var after = new FileInfo(LogicalPath);
        if (!after.Exists || (after.Length, after.LastWriteTimeUtc.Ticks) != observed)
            throw new IOException("The recording changed while its original identity was being verified. Retry when its source is stable.");
        cancellation.ThrowIfCancellationRequested();
        lock (_snapshotGate) _preparedLogicalStamp = observed;
    }, cancellation);
    /// <summary>Closing seals new edits. Cancellation affects only the wait and
    /// reopens authoring; snapshots already accepted by the writer remain owned.</summary>
    public async Task DiscardCameraEditsAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _cameraAuthoringClosed = true;
        try
        {
            await _cameraWrites.DiscardAsync(cancellation);
            var loaded = await Task.Run(() =>
            {
                var track = new ReplayCameraTrack();
                if (_preparedCameraIdentity is { } identity && !track.LoadBound(LogicalPath, identity, _preparedLogicalStamp))
                    track.Clear();
                return track.ExportState();
            });
            RequireOwner(); _camera.ImportState(loaded); _cameraLoadError = null; Changed?.Invoke();
        }
        catch { _cameraAuthoringClosed = false; throw; }
    }
    public void ResumeCameraAuthoring() { ObjectDisposedException.ThrowIf(_disposed, this); _cameraAuthoringClosed = false; }
    public async Task CloseCameraAuthoringAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner.HasValue && _owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Camera authoring requires its owner thread.");
        _cameraAuthoringClosed = true;
        try { await FlushCameraEditsAsync(cancellation).ConfigureAwait(false); }
        catch { _cameraAuthoringClosed = false; throw; }
    }
}
#endif
