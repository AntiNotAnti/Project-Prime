using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

public enum CommunityTransferKind { Upload, Download }
public enum CommunityTransferState
{
    Queued, Preparing, Uploading, Downloading, Verifying, Retryable,
    Paused, NeedsReview, Completed, Failed, Cancelled
}

/// <summary>
/// A safe-to-persist transfer receipt. Contains neither tickets nor bearer tokens.
/// Upload SnapshotPath is a frozen private archive; downloads use exact SHA-256
/// paths owned by the manager, never a creator-supplied archive destination.
/// </summary>
public sealed record CommunityTransferJob(
    Guid Id,
    CommunityTransferKind Kind,
    CommunityTransferState State,
    string Endpoint,
    string PackageHash,
    long TotalBytes,
    long AcknowledgedBytes,
    DateTimeOffset UpdatedAt,
    string? SnapshotPath = null,
    CommunityMap? Download = null,
    CommunityPublishRequest? Publish = null,
    bool Listed = true,
    bool Draft = false,
    string? Error = null);

internal sealed record CommunityTransferIndex(int Schema, CommunityTransferJob[] Jobs);

/// <summary>
/// Persistent bounded Community upload/download jobs. Downloaded archives are
/// verified and held privately; the gameplay owner still performs installation,
/// registration and the existing authoritative map Ready barrier.
///
/// A process restart marks interrupted jobs Retryable; StartPending resumes them.
/// Uploads reuse the service's authoritative offset; downloads reuse a stable
/// .part file and HTTP Range. No credential is written to disk.
/// </summary>
public sealed class CommunityTransferManager : IAsyncDisposable
{
    private const int SchemaVersion = 1;
    private const int MaximumRetainedJobs = 32;
    private readonly string _root;
    private readonly string _indexPath;
    private readonly CommunityTransferCoordination _coordination;
    private readonly IDisposable _writerVersion;
    private readonly Task _watcher;
    private readonly CommunityArchiveCache _archiveCache;
    private readonly ICommunityCredentialSource? _credentials;
    private readonly SemaphoreSlim _workers;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, CommunityTransferJob> _jobs = new();
    private readonly Dictionary<Guid, (CancellationTokenSource Cancellation, Task Running)> _running = new();
    private readonly object _gate = new();
    private bool _disposed;

    public event Action<CommunityTransferJob>? Changed;

    public CommunityTransferManager(string root, ICommunityCredentialSource? credentials = null,
        int maxConcurrent = 2)
    {
        if (maxConcurrent is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent));
        _root = Path.GetFullPath(root);
        _indexPath = Path.Combine(_root, "jobs-v1.json");
        _coordination = new CommunityTransferCoordination(_root);
        _archiveCache = new CommunityArchiveCache(_root);
        _credentials = credentials;
        _workers = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "uploads"));
        Directory.CreateDirectory(Path.Combine(_root, "partials"));
        Directory.CreateDirectory(Path.Combine(_root, "archives"));

        _writerVersion = _coordination.AcquireWriterVersion();
        try
        {

        // Before any process can inspect and restart a job, acquire the
        // short-lived index transaction lock. A different live process must
        // retain its running-job state; only ownerless work can be recovered.
        lock (_gate)
        {
            using var indexLease = LockAndReloadLocked();
            bool recovered = false;
            foreach (var job in _jobs.Values.ToArray())
            {
                if (job.State is CommunityTransferState.Preparing
                    or CommunityTransferState.Uploading
                    or CommunityTransferState.Downloading
                    or CommunityTransferState.Verifying
                    or CommunityTransferState.Queued)
                {
                    if (_coordination.IsJobOwned(job.Id)) continue;
                    _jobs[job.Id] = job with
                    {
                        State = CommunityTransferState.Retryable,
                        Error = "Previous owner exited; immutable bytes retained."
                    };
                    recovered = true;
                }
            }
            if (recovered) PersistLocked();
        }
        // A read-only process can observe and control another owner's jobs
        // without keeping its own stale copy of jobs-v1.json.
        _watcher = Task.Run(WatchOtherProcessesAsync);
        }
        catch { _writerVersion.Dispose(); _workers.Dispose(); _lifetime.Dispose(); throw; }
    }

    public IReadOnlyList<CommunityTransferJob> ListJobs()
    {
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            return _jobs.Values.OrderByDescending(j => j.UpdatedAt).ToArray();
        }
    }

    public CommunityTransferJob GetJob(Guid id)
    {
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            return _jobs.TryGetValue(id, out var job) ? job
                : throw new KeyNotFoundException("Unknown Community transfer job.");
        }
    }

    /// <summary>Acquire a read lease before consuming an exact archive.</summary>
    public CommunityArchiveCache.ArchiveLease? LeaseVerifiedArchive(Guid id)
    {
        CommunityTransferJob job = GetJob(id);
        if (job.Kind != CommunityTransferKind.Download
            || job.State != CommunityTransferState.Completed
            || job.Download == null) return null;
        CommunityMap map = job.Download;
        var identity = new MapContentIdentity(map.MapId, map.Name,
            MapHash256.Parse(map.ContentHash), MapHash256.Parse(map.Hash), true);
        return _archiveCache.TryAcquire(identity);
    }

    // Compatibility for older UI/tests. Consumers performing longer reads must
    // keep LeaseVerifiedArchive alive, or LRU eviction can race their use.
    public string? VerifiedArchivePath(Guid id)
    {
        using var lease = LeaseVerifiedArchive(id);
        return lease?.Path;
    }

    public async Task<Guid> EnqueueUploadAsync(string endpoint, string packagePath,
        CommunityPublishRequest? publish, bool listed, bool draft,
        CancellationToken cancellation = default)
    {
        ThrowIfDisposed();
        cancellation.ThrowIfCancellationRequested();
        ValidateEndpoint(endpoint);
        Guid id = Guid.NewGuid();
        string snapshot = UploadPath(id);
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            RequireCapacityLocked();
        }

        // Freeze before queue admission: subsequent editor changes or close may
        // not mutate an in-flight upload. Copy and SHA-256 work off the UI thread.
        try
        {
            await Task.Run(async () =>
            {
                await using var input = new FileStream(packagePath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (input.Length is <= 0 or > MapPackageReader.MaxArchiveBytes)
                    throw new InvalidDataException("Map package exceeds upload limits.");
                await using var output = new FileStream(snapshot, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await input.CopyToAsync(output, 65536, cancellation).ConfigureAwait(false);
                await output.FlushAsync(cancellation).ConfigureAwait(false);
            }, cancellation).ConfigureAwait(false);

            string hash = await Task.Run(() =>
            {
                using var reader = new MapPackageReader(snapshot);
                if (reader.Manifest == null)
                    throw new InvalidDataException("Only current immutable .ppmap archives can be queued.");
                return MapBuildFingerprint.HashFile(snapshot);
            }, cancellation).ConfigureAwait(false);
            var job = new CommunityTransferJob(id, CommunityTransferKind.Upload,
                CommunityTransferState.Queued, endpoint, hash, new FileInfo(snapshot).Length,
                0, DateTimeOffset.UtcNow, SnapshotPath: snapshot, Publish: publish,
                Listed: listed, Draft: draft);
            AddJob(job);
            Schedule(id);
            return id;
        }
        catch
        {
            if (File.Exists(snapshot)) File.Delete(snapshot);
            throw;
        }
    }

    public Guid EnqueueDownload(string endpoint, CommunityMap map)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(map);
        ValidateEndpoint(endpoint);
        ValidateMap(map);
        CommunityTransferJob? existing;
        CommunityTransferJob? added = null;
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            // Deduplicate against every live process, never a stale per-process
            // dictionary. The exact archive hash and service remain the key.
            existing = _jobs.Values.FirstOrDefault(j =>
                j.Kind == CommunityTransferKind.Download
                && j.PackageHash == map.Hash
                && j.Endpoint == endpoint && j.State != CommunityTransferState.Cancelled);
            if (existing == null)
            {
                RequireCapacityLocked();
                added = new CommunityTransferJob(Guid.NewGuid(),
                    CommunityTransferKind.Download, CommunityTransferState.Queued,
                    endpoint, map.Hash, map.Bytes, 0, DateTimeOffset.UtcNow,
                    Download: map);
                _jobs.Add(added.Id, added);
                PersistLocked();
            }
        }
        if (added != null)
        {
            Changed?.Invoke(added);
            Schedule(added.Id);
            return added.Id;
        }
        if (existing == null)
            throw new InvalidOperationException("Community download admission failed.");

        if (existing.Download == null || existing.Download.MapId != map.MapId
            || existing.Download.ContentHash != map.ContentHash
            || !StringComparer.OrdinalIgnoreCase.Equals(existing.Download.Name, map.Name)
            || existing.TotalBytes != map.Bytes)
            throw new InvalidDataException(
                "Stored transfer conflicts with the selected exact map revision.");

        if (existing.State == CommunityTransferState.Completed)
        {
            // A terminal receipt is not an archive lease. If CAS was evicted or
            // corrupted, one process claims the retry under the index gate.
            var exact = new MapContentIdentity(map.MapId, map.Name,
                MapHash256.Parse(map.ContentHash), MapHash256.Parse(map.Hash), true);
            using var cached = _archiveCache.TryAcquire(exact);
            if (cached == null)
            {
                Update(existing.Id, job => job with
                {
                    State = CommunityTransferState.Retryable,
                    Download = map,
                    AcknowledgedBytes = 0,
                    Error = "Cached archive missing or invalid; recovering exact revision."
                });
                Schedule(existing.Id);
            }
        }
        else if (existing.State is CommunityTransferState.Retryable
            or CommunityTransferState.Failed or CommunityTransferState.Queued)
            Schedule(existing.Id);
        return existing.Id;
    }

    public void Resume(Guid id)
    {
        ThrowIfDisposed();
        Task? retiring = null;
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            if (!_jobs.TryGetValue(id, out var job))
                throw new KeyNotFoundException("Unknown transfer.");
            if (job.State is CommunityTransferState.Completed
                or CommunityTransferState.Cancelled)
                throw new InvalidOperationException(
                    "Completed or cancelled transfers cannot resume.");
            if (job.State == CommunityTransferState.NeedsReview)
                throw new InvalidOperationException(
                    "Resolve the revision conflict explicitly before resuming.");
            // A queued state survives if a worker in another process is
            // currently retiring from Pause. Either observer can claim its
            // lifetime lease when the previous job owner exits.
            if (job.State is CommunityTransferState.Paused
                or CommunityTransferState.Retryable
                or CommunityTransferState.Failed)
            {
                _jobs[id] = job with { State = CommunityTransferState.Queued,
                    UpdatedAt = DateTimeOffset.UtcNow, Error = null };
                PersistLocked();
            }
            if (_running.TryGetValue(id, out var active))
            {
                if (!active.Cancellation.IsCancellationRequested) return;
                retiring = active.Running;
            }
        }
        if (retiring == null)
        {
            Schedule(id);
            return;
        }
        _ = retiring.ContinueWith(_ =>
        {
            try
            {
                if (!_disposed && GetJob(id).State == CommunityTransferState.Queued)
                    Schedule(id);
            }
            catch (Exception ex) when (ex is ObjectDisposedException
                or KeyNotFoundException) { }
        }, TaskScheduler.Default);
    }

    public void StartPending()
    {
        ThrowIfDisposed();
        foreach (var job in ListJobs().Where(j => j.State == CommunityTransferState.Retryable))
            Schedule(job.Id);
    }

    public void Pause(Guid id) => Stop(id, cancel: false);
    public void Cancel(Guid id) => Stop(id, cancel: true);

    /// <summary>
    /// Remove an inactive transfer receipt without ever evicting an installed
    /// map or verified download archive. PR-04 owns durable archive eviction.
    /// </summary>
    public void Forget(Guid id)
    {
        ThrowIfDisposed();
        CommunityTransferJob job;
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            if (!_jobs.TryGetValue(id, out job!))
                throw new KeyNotFoundException("Unknown Community transfer.");
            if (_running.ContainsKey(id) || _coordination.IsJobOwned(id)
                || (job.State is CommunityTransferState.Queued
                    or CommunityTransferState.Preparing or CommunityTransferState.Uploading
                    or CommunityTransferState.Downloading or CommunityTransferState.Verifying))
                throw new InvalidOperationException("Pause or finish the transfer before removing it.");
            _jobs.Remove(id);
            PersistLocked();
        }
        if (job.Kind == CommunityTransferKind.Upload)
        {
            string snapshot = UploadPath(id);
            if (File.Exists(snapshot)) File.Delete(snapshot);
        }
    }

    public async Task<CommunityTransferJob> WaitAsync(Guid id)
    {
        Task? running;
        lock (_gate)
            running = _running.TryGetValue(id, out var value)
                ? value.Running : null;
        if (running != null)
        {
            await running.ConfigureAwait(false);
            return GetJob(id);
        }
        // Wait for a different process's lifetime lease instead of returning
        // the first stale local snapshot. The transfer retains a bounded HTTP
        // timeout; paused, failed and review-needed jobs return immediately.
        while (true)
        {
            CommunityTransferJob job = GetJob(id);
            if (job.State is CommunityTransferState.Completed
                or CommunityTransferState.Failed
                or CommunityTransferState.Cancelled
                or CommunityTransferState.Paused
                or CommunityTransferState.NeedsReview
                or CommunityTransferState.Retryable)
                return job;
            if (job.State == CommunityTransferState.Queued)
                Schedule(id);
            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    public void ResolveConflict(Guid id, CommunityPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        CommunityTransferJob job = GetJob(id);
        if (job.Kind != CommunityTransferKind.Upload
            || job.State != CommunityTransferState.NeedsReview)
            throw new InvalidOperationException("Only an upload conflict can be resolved.");
        Update(id, latest =>
        {
            if (latest.State != CommunityTransferState.NeedsReview)
                throw new InvalidOperationException("Revision conflict was already handled.");
            return latest with { Publish = request,
                State = CommunityTransferState.Paused, Error = null };
        }, force: true);
    }

    private void AddJob(CommunityTransferJob job)
    {
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            RequireCapacityLocked();
            _jobs.Add(job.Id, job);
            PersistLocked();
        }
        Changed?.Invoke(job);
    }

    private void RequireCapacityLocked()
    {
        if (_jobs.Count < MaximumRetainedJobs) return;
        // Only terminal, unowned history may be aged out automatically. An
        // in-progress or paused receipt is never silently discarded.
        CommunityTransferJob? oldest = _jobs.Values
            .Where(j => !_running.ContainsKey(j.Id)
                && !_coordination.IsJobOwned(j.Id)
                && (j.State is CommunityTransferState.Completed
                    or CommunityTransferState.Cancelled))
            .OrderBy(j => j.UpdatedAt).FirstOrDefault();
        if (oldest == null)
            throw new IOException("Community transfer queue is full (32 active/paused jobs).");
        _jobs.Remove(oldest.Id);
        if (oldest.Kind == CommunityTransferKind.Upload)
        {
            string snapshot = UploadPath(oldest.Id);
            if (File.Exists(snapshot)) File.Delete(snapshot);
        }
    }

    private void Schedule(Guid id)
    {
        lock (_gate)
        {
            if (_disposed || _running.ContainsKey(id)) return;
            using var index = LockAndReloadLocked();
            if (!_jobs.TryGetValue(id, out var job))
                throw new KeyNotFoundException("Unknown Community job.");
            if (job.State is CommunityTransferState.Completed
                or CommunityTransferState.Cancelled
                or CommunityTransferState.Paused
                or CommunityTransferState.NeedsReview) return;

            // Lifetime exclusive across separate Game/Studio processes.
            // A second scheduler leaves the first owner untouched.
            FileStream? jobLease = _coordination.TryLeaseJob(id);
            if (jobLease == null) return;

            CancellationTokenSource? cancel = null;
            try
            {
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _jobs[id] = job with { State = CommunityTransferState.Queued,
                    UpdatedAt = DateTimeOffset.UtcNow, Error = null };
                PersistLocked();
                CancellationTokenSource ownerCancel = cancel;
                Task running = Task.Run(async () =>
                {
                    try { await RunAsync(id, ownerCancel.Token).ConfigureAwait(false); }
                    finally
                    {
                        // Only dispose the job lease after the HTTP worker has
                        // stopped writing offsets and persisted terminal state.
                        lock (_gate) _running.Remove(id);
                        jobLease.Dispose();
                        ownerCancel.Dispose();
                    }
                });
                _running.Add(id, (ownerCancel, running));
            }
            catch
            {
                jobLease.Dispose();
                cancel?.Dispose();
                throw;
            }
        }
    }

    private async Task RunAsync(Guid id, CancellationToken cancellation)
    {
        try
        {
            await _workers.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    CommunityTransferJob job = GetJob(id);
                    try
                    {
                        if (job.Kind == CommunityTransferKind.Upload)
                            await UploadAsync(job, cancellation).ConfigureAwait(false);
                        else
                            await DownloadAsync(job, cancellation).ConfigureAwait(false);
                        return;
                    }
                    catch (CommunityRevisionConflictException conflict)
                    {
                        Update(id, j => j with
                        {
                            State = CommunityTransferState.NeedsReview,
                            Error = conflict.Conflict.Message
                        });
                        return;
                    }
                    catch (HttpRequestException ex) when (Retryable(ex) && attempt < 3
                        && !cancellation.IsCancellationRequested)
                    {
                        Update(id, j => j with
                        {
                            State = CommunityTransferState.Retryable,
                            Error = "Network unavailable or service busy. Retrying."
                        });
                        // Bound exponential retry, jitter and delayed admission.
                        await Task.Delay(TimeSpan.FromMilliseconds(
                            300 * (1 << attempt) + Random.Shared.Next(0, 201)),
                            cancellation).ConfigureAwait(false);
                    }
                }
            }
            finally { _workers.Release(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Pause/cancel state was written before cancellation, or Dispose
            // has cancelled an in-progress worker. Do not discard partial bytes.
            Update(id, j => j.State is CommunityTransferState.Cancelled
                or CommunityTransferState.Paused or CommunityTransferState.Queued ? j
                : j with { State = CommunityTransferState.Retryable,
                    Error = "Interrupted; transfer bytes were retained." }, force: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or HttpRequestException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Update(id, j => j with
            {
                State = CommunityTransferState.Failed, Error = ex.GetBaseException().Message
            });
        }
    }

    private async Task UploadAsync(CommunityTransferJob job, CancellationToken cancellation)
    {
        string snapshot = UploadPath(job.Id);
        if (!File.Exists(snapshot) || new FileInfo(snapshot).Length != job.TotalBytes
            || await Task.Run(() => MapBuildFingerprint.HashFile(snapshot), cancellation)
                .ConfigureAwait(false) != job.PackageHash)
            throw new InvalidDataException("The frozen upload archive changed or is missing.");
        Update(job.Id, j => j with { State = CommunityTransferState.Uploading });
        var operations = new CommunityMapOperations(job.Endpoint, _credentials);
        Action<long, long> progress = (offset, total) => Progress(job.Id, offset, total);
        if (job.Publish is { } publication)
            await operations.PublishPackageAsync(snapshot, publication, cancellation,
                job.Listed, job.Draft, progress).ConfigureAwait(false);
        else
            await operations.UploadPackageAsync(snapshot, cancellation,
                job.Listed, job.Draft, progress).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        Update(job.Id, j => j with { State = CommunityTransferState.Completed,
            AcknowledgedBytes = j.TotalBytes, Error = null });
        if (File.Exists(snapshot)) File.Delete(snapshot);
    }

    private async Task DownloadAsync(CommunityTransferJob job, CancellationToken cancellation)
    {
        CommunityMap map = job.Download
            ?? throw new InvalidDataException("Missing exact Community map selection.");
        var exact = new MapContentIdentity(map.MapId, map.Name,
            MapHash256.Parse(map.ContentHash), MapHash256.Parse(map.Hash), true);

        // Strict cache validation occurs under a cross-process read lease.
        // An earlier completed download cannot bypass package inspection.
        using (var cached = _archiveCache.TryAcquire(exact))
        {
            if (cached != null)
            {
                Update(job.Id, j => j with { State = CommunityTransferState.Completed,
                    AcknowledgedBytes = map.Bytes, Error = null });
                return;
            }
        }

        // Serialize this hash across Game and Studio processes. Recheck after
        // acquiring the gate because another client may have finished it.
        using var gate = await _archiveCache.AcquireDownloadGateAsync(map.Hash, cancellation)
            .ConfigureAwait(false);
        using (var cached = _archiveCache.TryAcquire(exact))
        {
            if (cached != null)
            {
                Update(job.Id, j => j with { State = CommunityTransferState.Completed,
                    AcknowledgedBytes = map.Bytes, Error = null });
                return;
            }
        }

        string partial = PartialPath(map.Hash);
        Update(job.Id, j => j with { State = CommunityTransferState.Downloading });
        var operations = new CommunityMapOperations(job.Endpoint, _credentials);
        await operations.UseClientAsync(map.Draft,
            async client =>
            {
                string[] trustedHosts = CommunityCutoverPolicy.TrustedCdnHosts;
                if (trustedHosts.Length > 0)
                    await client.DownloadViaTrustedCdnOrOriginAsync(
                        map, partial, new CommunityCdnPolicy(trustedHosts),
                        cancellation,
                        (received, total) => Progress(job.Id, received, total))
                        .ConfigureAwait(false);
                else
                    await client.DownloadResumableAsync(map, partial, cancellation,
                        (received, total) => Progress(job.Id, received, total))
                        .ConfigureAwait(false);
                return true;
            }, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        Update(job.Id, j => j with { State = CommunityTransferState.Verifying });

        // Atomic cache promotion owns its exclusive lease and rechecks the
        // full immutable identity after copying across any volume boundary.
        _archiveCache.Promote(partial, exact);
        if (File.Exists(partial)) File.Delete(partial);
        // Pin the just-finished download while trimming older objects so it
        // cannot be evicted before this job becomes Completed.
        using var verified = _archiveCache.TryAcquire(exact)
            ?? throw new IOException("Verified archive disappeared before completion.");
        _archiveCache.Prune();
        Update(job.Id, j => j with { State = CommunityTransferState.Completed,
            AcknowledgedBytes = map.Bytes, Error = null });
    }

    private void Progress(Guid id, long received, long total)
    {
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            if (!_jobs.TryGetValue(id, out var job)
                || job.State is CommunityTransferState.Paused
                    or CommunityTransferState.Cancelled) return;
            long bounded = Math.Clamp(received, 0, total);
            if (bounded <= job.AcknowledgedBytes
                || bounded < job.TotalBytes
                    && bounded - job.AcknowledgedBytes < 1024 * 1024
                    && DateTimeOffset.UtcNow - job.UpdatedAt < TimeSpan.FromSeconds(2))
                return;
            CommunityTransferJob next = job with
            {
                AcknowledgedBytes = bounded,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _jobs[id] = next;
            PersistLocked();
        }
        // Event notifications must not run inside the durable-index lock.
        Changed?.Invoke(GetJob(id));
    }

    private void Stop(Guid id, bool cancel)
    {
        ThrowIfDisposed();
        CancellationTokenSource? signal = null;
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            if (!_jobs.TryGetValue(id, out var job))
                throw new KeyNotFoundException("Unknown transfer.");
            if (job.State is CommunityTransferState.Completed
                or CommunityTransferState.Cancelled) return;
            _jobs[id] = job with
            {
                State = cancel ? CommunityTransferState.Cancelled : CommunityTransferState.Paused,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PersistLocked();
            if (_running.TryGetValue(id, out var running)) signal = running.Cancellation;
        }
        try { signal?.Cancel(); }
        catch (ObjectDisposedException) { } // Worker may have just finished.
        Changed?.Invoke(GetJob(id));
    }

    private void Update(Guid id, Func<CommunityTransferJob, CommunityTransferJob> change,
        bool force = false)
    {
        CommunityTransferJob result;
        lock (_gate)
        {
            using var gate = LockAndReloadLocked();
            var job = _jobs[id];
            if (!force && job.State is CommunityTransferState.Paused
                or CommunityTransferState.Cancelled) return;
            result = change(job) with { UpdatedAt = DateTimeOffset.UtcNow };
            _jobs[id] = result;
            PersistLocked();
        }
        Changed?.Invoke(result);
    }

    /// <summary>
    /// Always call under _gate, and keep the returned cross-process lease
    /// alive through any transaction's PersistLocked call. Refreshing first
    /// prevents stale in-memory copies from rewriting another process's jobs.
    /// </summary>
    private FileStream LockAndReloadLocked()
    {
        // A retiring worker must still flush cancellation state after the
        // lifetime token has been cancelled during DisposeAsync.
        FileStream gate = _coordination.LockIndex();
        try
        {
            ReloadIndexLocked();
            return gate;
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

    private void ReloadIndexLocked()
    {
        if (!File.Exists(_indexPath))
        {
            _jobs.Clear();
            return;
        }
        if (new FileInfo(_indexPath).Length > 256 * 1024)
            throw new InvalidDataException("Community transfer index exceeds its budget.");
        CommunityTransferIndex persisted = JsonSerializer.Deserialize<CommunityTransferIndex>(
            File.ReadAllText(_indexPath), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("Community transfer index is missing.");
        if (persisted.Schema != SchemaVersion || persisted.Jobs == null
            || persisted.Jobs.Length > MaximumRetainedJobs)
            throw new InvalidDataException("Unsupported Community transfer index schema or size.");
        var copy = new Dictionary<Guid, CommunityTransferJob>();
        foreach (CommunityTransferJob job in persisted.Jobs)
        {
            ValidateRecord(job);
            if (!copy.TryAdd(job.Id, job))
                throw new InvalidDataException("Duplicate Community transfer job ID.");
        }
        _jobs.Clear();
        foreach (var pair in copy) _jobs.Add(pair.Key, pair.Value);
    }

    /// <summary>
    /// Poll the tiny atomic index for changes from other processes. This is a
    /// bounded 500ms control plane, never an HTTP polling loop. Cross-process
    /// pause/cancel requests cancel only the current job owner. A queued
    /// unowned job may be claimed by exactly one process via its job lease.
    /// </summary>
    private async Task WatchOtherProcessesAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(500, _lifetime.Token).ConfigureAwait(false);
                var changed = new List<CommunityTransferJob>();
                var cancel = new List<CancellationTokenSource>();
                var queued = new List<Guid>();
                lock (_gate)
                {
                    var previous = _jobs.ToDictionary(pair => pair.Key, pair => pair.Value);
                    using var gate = LockAndReloadLocked();
                    var orphaned = new List<Guid>();
                    foreach (var job in _jobs.Values.ToArray())
                    {
                        if ((job.State is CommunityTransferState.Preparing
                            or CommunityTransferState.Uploading
                            or CommunityTransferState.Downloading
                            or CommunityTransferState.Verifying)
                            && !_running.ContainsKey(job.Id)
                            && !_coordination.IsJobOwned(job.Id))
                        {
                            // Previous owner crashed or exited after another
                            // manager was already opened. No duplicate HTTP
                            // transfer can survive once the OS released its
                            // exclusive per-job file lease.
                            _jobs[job.Id] = job with
                            {
                                State = CommunityTransferState.Retryable,
                                Error = "Previous transfer owner exited; recovering."
                            };
                            orphaned.Add(job.Id);
                        }
                    }
                    if (orphaned.Count > 0) PersistLocked();
                    foreach (var job in _jobs.Values)
                    {
                        // JSON creates fresh array instances for nested
                        // CommunityMap metadata. Record equality alone would
                        // therefore emit a false change on every poll.
                        if (!previous.TryGetValue(job.Id, out var before)
                            || before.UpdatedAt != job.UpdatedAt
                            || before.State != job.State
                            || before.AcknowledgedBytes != job.AcknowledgedBytes
                            || before.Error != job.Error) changed.Add(job);
                        if (_running.TryGetValue(job.Id, out var active)
                            && job.State is CommunityTransferState.Paused
                                or CommunityTransferState.Cancelled)
                            cancel.Add(active.Cancellation);
                        if ((job.State == CommunityTransferState.Queued
                            || orphaned.Contains(job.Id))
                            && !_running.ContainsKey(job.Id))
                            queued.Add(job.Id);
                    }
                }
                foreach (var signal in cancel)
                {
                    try { signal.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                foreach (var job in changed) Changed?.Invoke(job);
                foreach (Guid id in queued)
                {
                    if (!_lifetime.IsCancellationRequested)
                        Schedule(id);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void PersistLocked()
        => AtomicFile.Write(_indexPath,
            JsonSerializer.SerializeToUtf8Bytes(new CommunityTransferIndex(
                SchemaVersion, _jobs.Values.OrderBy(j => j.Id).ToArray()),
                MapPackageReader.JsonOptions));

    private void ValidateRecord(CommunityTransferJob job)
    {
        if (job.Id == Guid.Empty || !Enum.IsDefined(job.Kind)
            || !Enum.IsDefined(job.State)
            || string.IsNullOrWhiteSpace(job.Endpoint)
            || !MapCommunityClient.ValidHash(job.PackageHash)
            || job.TotalBytes is <= 0 or > MapPackageReader.MaxArchiveBytes
            || job.AcknowledgedBytes < 0 || job.AcknowledgedBytes > job.TotalBytes)
            throw new InvalidDataException("Invalid stored transfer identity or byte limits.");
        ValidateEndpoint(job.Endpoint);
        if (job.Kind == CommunityTransferKind.Download)
        {
            CommunityMap map = job.Download
                ?? throw new InvalidDataException("Download job has no map metadata.");
            ValidateMap(map);
            if (map.Hash != job.PackageHash || map.Bytes != job.TotalBytes)
                throw new InvalidDataException("Stored Community job and map identities disagree.");
        }
        else if (!StringComparer.Ordinal.Equals(job.SnapshotPath, UploadPath(job.Id)))
            throw new InvalidDataException("Upload staging path is outside its private job.");
    }

    private static void ValidateEndpoint(string endpoint)
    {
        using var client = new MapCommunityClient(endpoint);
    }

    private static void ValidateMap(CommunityMap map)
    {
        if (!MapCommunityClient.ValidHash(map.Hash)
            || !MapCommunityClient.ValidHash(map.ContentHash)
            || map.Bytes is <= 0 or > MapPackageReader.MaxArchiveBytes
            || map.MapId == Guid.Empty || string.IsNullOrWhiteSpace(map.Name))
            throw new InvalidDataException("Invalid exact Community map identity.");
    }

    private static bool Retryable(HttpRequestException ex)
        => ex.StatusCode == null || ex.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.InternalServerError;

    private string UploadPath(Guid id)
        => Path.Combine(_root, "uploads", id.ToString("N") + ".ppmap");
    private string PartialPath(string hash)
        => Path.Combine(_root, "partials", hash + ".part");
    private string ArchivePath(string hash)
        => Path.Combine(_root, "archives", hash + ".ppmap");

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CommunityTransferManager));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Task[] running;
        lock (_gate) running = _running.Values.Select(v => v.Running).ToArray();
        try { await Task.WhenAll(running.Append(_watcher)).ConfigureAwait(false); }
        finally
        {
            _lifetime.Dispose();
            _workers.Dispose();
            _writerVersion.Dispose();
        }
    }
}
