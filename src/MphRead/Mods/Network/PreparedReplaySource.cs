using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

/// <summary>Detached reader/decoder/capsule ownership. No Scene or native resource
/// is created here. Only the scene owner can adopt a prepared source.</summary>
internal sealed class PreparedReplaySource : IDisposable
{
    private ReplayPlaybackSession? _session;
    private ReplayWorldCheckpoint? _checkpoint;
    private ReplayTimelineClip? _clip;
    private PreparedReplaySource? _requiredOrigin;
    private PreparedMapInstallation? _preparedMap;
    private readonly ReplayMetadata? _mapMetadata;
    private bool _mapValidated;
    private readonly CancellationToken _cancellation;
    internal string? Path { get; }
    internal ReplayPlaybackSession Session => _session ?? throw new ObjectDisposedException(nameof(PreparedReplaySource));
    internal uint? PlaybackFrame { get; }
    internal ReplayCheckpointIndex? Durable { get; }
    internal string? CheckpointError { get; }
    internal ReplayWorldCheckpoint? Checkpoint => _checkpoint;
    internal ReplayTimelineClip? FrozenClip => _clip;
    [ThreadStatic] internal static bool IsWorker;

    private PreparedReplaySource(ReplayPlaybackSession session, ReplayWorldCheckpoint? checkpoint,
        ReplayTimelineClip? clip, string? path, uint? playbackFrame, ReplayCheckpointIndex? durable,
        string? checkpointError, CancellationToken cancellation, PreparedReplaySource? requiredOrigin = null,
        PreparedMapInstallation? preparedMap = null, ReplayMetadata? mapMetadata = null)
    { _session = session; _checkpoint = checkpoint; _clip = clip; Path = path; PlaybackFrame = playbackFrame;
      Durable = durable; CheckpointError = checkpointError; _cancellation = cancellation; _requiredOrigin = requiredOrigin; _preparedMap = preparedMap; _mapMetadata = mapMetadata; }

    internal static PreparedReplaySource File(string path, ReplayCheckpointIndex? durable = null,
        CancellationToken cancellation = default, uint? memoryFrame = null, bool useLeadInCheckpoint = true,
        byte[]? verifiedOrigin = null, bool prepareMap = true)
    {
        var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        ReplayWorldCheckpoint? checkpoint = null;
        PreparedReplaySource? requiredOrigin = null;
        PreparedMapInstallation? preparedMap = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!session.JoinDetached(path, cancellation))
                throw new ReplayPreparationException(session.LastResult, session.LastError ?? "Cannot prepare replay.");
            if (useLeadInCheckpoint && durable == null && memoryFrame == null && session.IsWarming)
            {
                var beforeVisibleStart = session.DurableCheckpoints.LastOrDefault(index => session.CheckpointVisibleFrame(index) == 0);
                if (beforeVisibleStart.RawLength > 0) durable = beforeVisibleStart;
            }
            if (session.Metadata?.WorldCheckpoint is { Length: > 0 } origin)
                checkpoint = ReplayWorldCheckpoint.FromBytes(origin, session.Metadata.BuildId);
            string? error = null;
            if (durable is { } disk)
            {
                try
                {
                    var selected = session.LoadCheckpoint(disk);
                    try
                    {
                        var construction = selected.ConstructionState();
                        if (session.Metadata is { } metadata) ReplayMapIdentity.ValidateOriginConstruction(metadata, construction);
                        ((PassiveReplaySessionHost)session.Host).State.RestoreCheckpoint(construction);
                    }
                    catch { selected.Dispose(); throw; }
                    checkpoint?.Dispose(); checkpoint = selected;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                { error = ex.Message; durable = null; }
            }
            if (checkpoint != null)
            {
                var construction = checkpoint.ConstructionState();
                if (session.Metadata is { } metadata) ReplayMapIdentity.ValidateOriginConstruction(metadata, construction);
                session.PrepareReposition(memoryFrame ?? durable?.Frame ?? 0, sourceClock: !memoryFrame.HasValue);
                // Cursor preparation restores playback diagnostics/clocks. Scene
                // construction instead needs the capsule's immutable baseline;
                // the owner's world restore adopts its current decoder afterwards.
                ((PassiveReplaySessionHost)session.Host).State.RestoreCheckpoint(construction);
            }
            if (durable != null && session.Metadata?.WorldCheckpoint is { Length: > 0 } required
                && (verifiedOrigin == null || !required.AsSpan().SequenceEqual(verifiedOrigin)))
            {
                // A required origin cannot be replaced by an optional seek
                // capsule before its complete graph/resource contract is bound.
                requiredOrigin = File(path, cancellation: cancellation, useLeadInCheckpoint: false, prepareMap: false);
                var originMetadata = requiredOrigin.Session.Metadata!;
                if (!required.AsSpan().SequenceEqual(originMetadata.WorldCheckpoint)
                    || originMetadata.RoomKey != session.Metadata.RoomKey || originMetadata.Mode != session.Metadata.Mode
                    || originMetadata.MapHash != session.Metadata.MapHash || originMetadata.ProtocolVersion != session.Metadata.ProtocolVersion)
                    throw new InvalidDataException("Required replay origin changed during detached preparation.");
            }
            if (prepareMap && session.Metadata is { } mapMetadata)
                preparedMap = PrepareMap(mapMetadata, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new(session, checkpoint, null, path, durable?.Frame ?? 0, durable, error, cancellation, requiredOrigin, preparedMap);
        }
        catch { try { checkpoint?.Dispose(); } finally { try { session.Dispose(); } finally
            { try { requiredOrigin?.Dispose(); } finally { preparedMap?.Dispose(); } } } throw; }
    }

    internal static PreparedReplaySource Clip(ReplayTimelineClip clip, CancellationToken cancellation = default)
    {
        // Retain the frozen bytes independently of the controller/timeline lease.
        var retained = Retain(clip);
        var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        ReplayWorldCheckpoint? checkpoint = null;
        PreparedMapInstallation? preparedMap = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            checkpoint = PassiveReplayScene.Checkpoint(retained);
            session.Join(retained, checkpoint.ConstructionState());
            var mapMetadata = checkpoint.ClipMetadata(0, Array.Empty<ReplayPlayerInfo>());
            preparedMap = PrepareMap(mapMetadata, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new(session, checkpoint, retained, null, null, null, null, cancellation, preparedMap: preparedMap, mapMetadata: mapMetadata);
        }
        catch { try { checkpoint?.Dispose(); } finally { try { session.Dispose(); }
            finally { try { retained.Dispose(); } finally { preparedMap?.Dispose(); } } } throw; }
    }
    private static PreparedMapInstallation? PrepareMap(ReplayMetadata metadata, CancellationToken cancellation)
    {
        try { return ReplayMapIdentity.PrepareExactPackageDetached(metadata, cancellation); }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or UnauthorizedAccessException
            || ex is OperationCanceledException && !cancellation.IsCancellationRequested)
        { throw new ReplayPreparationException(ReplayOpenResult.MapMissing, "Cannot prepare replay map: " + ex.Message); }
    }

    internal static ReplayTimelineClip Retain(ReplayTimelineClip clip)
        => new(clip.RestorePoint, clip.Records.ToArray(), clip.StartRecordingFrame, clip.EndRecordingFrame);

    internal ReplayPlaybackSession AdoptSession(int ownerThreadId)
    {
        RequireOwner(ownerThreadId);
        _cancellation.ThrowIfCancellationRequested();
        CommitPreparedMap(ownerThreadId);
        _cancellation.ThrowIfCancellationRequested();
        return Interlocked.Exchange(ref _session, null)
            ?? throw new InvalidOperationException("Prepared replay has already been adopted.");
    }
    internal void ValidateRequiredOrigin(Vector2i size, int ownerThreadId)
    {
        RequireOwner(ownerThreadId); _cancellation.ThrowIfCancellationRequested();
        using var origin = Interlocked.Exchange(ref _requiredOrigin, null);
        if (origin != null)
        {
            CommitPreparedMap(ownerThreadId);
            origin._mapValidated = true; // Equal required-origin/map metadata was checked on the worker.
            using (new PassiveReplayScene(origin, size, ownerThreadId)) { }
        }
    }
    internal void CommitPreparedMap(int ownerThreadId)
    {
        RequireOwner(ownerThreadId); _cancellation.ThrowIfCancellationRequested();
        if (_mapValidated) return;
        if (_preparedMap is { } prepared)
        {
            using var publicationPerf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.MapPublication);
            try { ReplayMapIdentity.CommitExactPackage(prepared, _cancellation); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException)
            { throw new ReplayPreparationException(ReplayOpenResult.MapMissing, "Cannot publish replay map: " + ex.Message); }
            _preparedMap = null; prepared.Dispose();
        }
        Session.ValidatePreparedMap(preparePackage: false, _mapMetadata); _mapValidated = true;
    }
    internal ReplayWorldCheckpoint? TakeCheckpoint() => Interlocked.Exchange(ref _checkpoint, null);
    internal ReplayTimelineClip? TakeClip() => Interlocked.Exchange(ref _clip, null);
    internal static void RequireOwner(int ownerThreadId)
    {
        if (IsWorker || Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("Replay scene commit requires its owner thread.");
    }
    public void Dispose()
    {
        try { Interlocked.Exchange(ref _session, null)?.Dispose(); }
        finally { try { TakeCheckpoint()?.Dispose(); } finally { try { TakeClip()?.Dispose(); }
            finally { try { Interlocked.Exchange(ref _requiredOrigin, null)?.Dispose(); }
                finally { Interlocked.Exchange(ref _preparedMap, null)?.Dispose(); } } } }
    }
}

internal sealed class ReplayPreparationException(ReplayOpenResult result, string message) : IOException(message)
{
    internal ReplayOpenResult Result { get; } = result;
}

/// <summary>Two bounded detached workers. Abandoned results are observed and
/// disposed without a dispatcher or any scene/native-resource access.</summary>
internal sealed class ReplayPreparationJob : IDisposable
{
    private static readonly SemaphoreSlim Workers = new(2, 2);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<PreparedReplaySource> _task;
    private bool _taken, _disposed, _cleaned;
    internal bool Completed => _task.IsCompleted;
    internal Task Completion => _task;

    private ReplayPreparationJob(Func<CancellationToken, PreparedReplaySource> prepare, Action? release = null)
    {
        var privateResources = StudioReplay.StudioReplayResources.Current;
        _task = Task.Run(async () =>
        {
            bool admitted = false;
            try
            {
                await Workers.WaitAsync(_cancellation.Token).ConfigureAwait(false); admitted = true;
                _cancellation.Token.ThrowIfCancellationRequested();
                IsWorker(true);
                try { using var scope = privateResources?.Enter(); return prepare(_cancellation.Token); }
                finally { IsWorker(false); }
            }
            finally { if (admitted) Workers.Release(); release?.Invoke(); }
        });
        _ = _task.ContinueWith(completed =>
        {
            lock (_gate)
            {
                if (_disposed && !_taken) Cleanup(completed);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private static void IsWorker(bool value) => PreparedReplaySource.IsWorker = value;
    private void Cleanup(Task<PreparedReplaySource> completed)
    {
        if (_cleaned) return;
        _cleaned = true;
        try
        {
            if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
            else _ = completed.Exception;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { MphRead.Mods.DebugLog.Exception("replay-preparation-cleanup", ex); }
        finally { _cancellation.Dispose(); }
    }
    internal static ReplayPreparationJob File(string path, ReplayCheckpointIndex? durable = null, uint? memoryFrame = null,
        bool useLeadInCheckpoint = true, byte[]? verifiedOrigin = null)
        => new(token => PreparedReplaySource.File(path, durable, token, memoryFrame, useLeadInCheckpoint, verifiedOrigin));
    internal static ReplayPreparationJob Clip(ReplayTimelineClip clip)
    {
        var retained = PreparedReplaySource.Retain(clip);
        try { return new(token => PreparedReplaySource.Clip(retained, token), retained.Dispose); }
        catch { retained.Dispose(); throw; }
    }
    // A deterministic preparation seam for cancellation/ownership regressions.
    internal static ReplayPreparationJob Start(Func<CancellationToken, PreparedReplaySource> prepare)
        => new(prepare);
    internal PreparedReplaySource TakeCompleted()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_taken || !_task.IsCompleted) throw new InvalidOperationException("Replay preparation is not available.");
            _taken = true;
            try { return _task.GetAwaiter().GetResult(); }
            finally { _cancellation.Dispose(); }
        }
    }
    internal PreparedReplaySource WaitCompleted()
    { _task.GetAwaiter().GetResult(); return TakeCompleted(); }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_taken) _cancellation.Cancel();
            if (!_taken && _task.IsCompleted) Cleanup(_task);
            // Completion owns cleanup even when cancellation loses its race.
        }
    }
}
