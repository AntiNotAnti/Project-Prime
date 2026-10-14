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
        _credentials = credentials;
        _workers = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "uploads"));
        Directory.CreateDirectory(Path.Combine(_root, "partials"));
        Directory.CreateDirectory(Path.Combine(_root, "archives"));

        if (!File.Exists(_indexPath)) return;
        if (new FileInfo(_indexPath).Length > 256 * 1024)
            throw new InvalidDataException("Community transfer index exceeds its budget.");
        var persisted = JsonSerializer.Deserialize<CommunityTransferIndex>(
            File.ReadAllText(_indexPath), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("Community transfer index is missing.");
        if (persisted.Schema != SchemaVersion || persisted.Jobs.Length > MaximumRetainedJobs)
            throw new InvalidDataException("Unsupported Community transfer index schema or size.");
        foreach (var job in persisted.Jobs)
        {
            ValidateRecord(job);
            if (!_jobs.TryAdd(job.Id, job with
                {
                    State = job.State is CommunityTransferState.Preparing
                        or CommunityTransferState.Uploading
                        or CommunityTransferState.Downloading
                        or CommunityTransferState.Verifying
                        or CommunityTransferState.Queued ? CommunityTransferState.Retryable : job.State
                }))
                throw new InvalidDataException("Duplicate Community transfer job ID.");
        }
        // An unexpected power loss must never silently publish a job as Complete.
        lock (_gate) PersistLocked();
    }

    public IReadOnlyList<CommunityTransferJob> ListJobs()
    {
        lock (_gate)
            return _jobs.Values.OrderByDescending(j => j.UpdatedAt).ToArray();
    }

    public CommunityTransferJob GetJob(Guid id)
    {
        lock (_gate)
            return _jobs.TryGetValue(id, out var job) ? job
                : throw new KeyNotFoundException("Unknown Community transfer job.");
    }

    public string? VerifiedArchivePath(Guid id)
    {
        CommunityTransferJob job = GetJob(id);
        if (job.Kind != CommunityTransferKind.Download
            || job.State != CommunityTransferState.Completed) return null;
        string path = ArchivePath(job.PackageHash);
        // Caller must run the map installer's strict identity validation before
        // using this archive, even if this transfer completed previously.
        return File.Exists(path) ? path : null;
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
        lock (_gate) RequireCapacityLocked();

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
        lock (_gate)
        {
            // An exact download is single-flight within this manager; reuse
            // even a paused or completed job instead of creating duplicates.
            var existing = _jobs.Values.FirstOrDefault(j =>
                j.Kind == CommunityTransferKind.Download
                && j.PackageHash == map.Hash
                && j.Endpoint == endpoint && j.State != CommunityTransferState.Cancelled);
            if (existing != null)
            {
                if (existing.State is CommunityTransferState.Retryable
                    or CommunityTransferState.Failed)
                    Schedule(existing.Id);
                return existing.Id;
            }
            RequireCapacityLocked();
        }
        Guid id = Guid.NewGuid();
        AddJob(new CommunityTransferJob(id, CommunityTransferKind.Download,
            CommunityTransferState.Queued, endpoint, map.Hash, map.Bytes, 0,
            DateTimeOffset.UtcNow, Download: map));
        Schedule(id);
        return id;
    }

    public void Resume(Guid id)
    {
        ThrowIfDisposed();
        CommunityTransferJob job = GetJob(id);
        if (job.State is CommunityTransferState.Completed or CommunityTransferState.Cancelled)
            throw new InvalidOperationException("Completed or cancelled transfers cannot resume.");
        Schedule(id);
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
            if (!_jobs.TryGetValue(id, out job!))
                throw new KeyNotFoundException("Unknown Community transfer.");
            if (_running.ContainsKey(id) || job.State is CommunityTransferState.Queued
                or CommunityTransferState.Preparing or CommunityTransferState.Uploading
                or CommunityTransferState.Downloading or CommunityTransferState.Verifying)
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
        lock (_gate) running = _running.TryGetValue(id, out var value) ? value.Running : null;
        if (running != null) await running.ConfigureAwait(false);
        return GetJob(id);
    }

    public void ResolveConflict(Guid id, CommunityPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        CommunityTransferJob job = GetJob(id);
        if (job.Kind != CommunityTransferKind.Upload
            || job.State != CommunityTransferState.NeedsReview)
            throw new InvalidOperationException("Only an upload conflict can be resolved.");
        Update(id, _ => job with { Publish = request, State = CommunityTransferState.Paused, Error = null },
            force: true);
    }

    private void AddJob(CommunityTransferJob job)
    {
        lock (_gate)
        {
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
                && j.State is CommunityTransferState.Completed
                    or CommunityTransferState.Cancelled)
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
            if (_running.ContainsKey(id)) return;
            if (!_jobs.TryGetValue(id, out var job))
                throw new KeyNotFoundException("Unknown Community job.");
            if (job.State is CommunityTransferState.Completed
                or CommunityTransferState.Cancelled) return;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _jobs[id] = job with { State = CommunityTransferState.Queued,
                UpdatedAt = DateTimeOffset.UtcNow, Error = null };
            PersistLocked();
            Task running = Task.Run(async () =>
            {
                try { await RunAsync(id, cancel.Token).ConfigureAwait(false); }
                finally
                {
                    lock (_gate) _running.Remove(id);
                    cancel.Dispose();
                }
            });
            _running.Add(id, (cancel, running));
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
                or CommunityTransferState.Paused ? j
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
        string destination = ArchivePath(map.Hash);
        string partial = PartialPath(map.Hash);
        if (File.Exists(destination))
        {
            var prior = await Task.Run(() => MapBuildFingerprint.HashFile(destination),
                cancellation).ConfigureAwait(false);
            if (prior == map.Hash && new FileInfo(destination).Length == map.Bytes
                && MapContentIdentity.FromPackage(destination) is { } cached
                && cached.MapId == map.MapId && cached.ContentHash.ToString() == map.ContentHash
                && cached.RoomKey == map.Name)
            {
                Update(job.Id, j => j with { State = CommunityTransferState.Completed,
                    AcknowledgedBytes = map.Bytes, Error = null });
                return;
            }
            // This is our private staging archive, not the installed package.
            File.Delete(destination);
        }

        Update(job.Id, j => j with { State = CommunityTransferState.Downloading });
        var operations = new CommunityMapOperations(job.Endpoint, _credentials);
        await operations.UseClientAsync(map.Draft,
            async client =>
            {
                await client.DownloadResumableAsync(map, partial, cancellation,
                    (received, total) => Progress(job.Id, received, total))
                    .ConfigureAwait(false);
                return true;
            }, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        Update(job.Id, j => j with { State = CommunityTransferState.Verifying });
        // A partial is never visible as a completed archive.
        File.Move(partial, destination, overwrite: true);
        Update(job.Id, j => j with { State = CommunityTransferState.Completed,
            AcknowledgedBytes = map.Bytes, Error = null });
    }

    private void Progress(Guid id, long received, long total)
    {
        lock (_gate)
        {
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
            var job = _jobs[id];
            if (!force && job.State is CommunityTransferState.Paused
                or CommunityTransferState.Cancelled) return;
            result = change(job) with { UpdatedAt = DateTimeOffset.UtcNow };
            _jobs[id] = result;
            PersistLocked();
        }
        Changed?.Invoke(result);
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
        if (job.Kind == CommunityTransferKind.Download) ValidateMap(job.Download
            ?? throw new InvalidDataException("Download job has no map metadata."));
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
        if (running.Length > 0) await Task.WhenAll(running).ConfigureAwait(false);
        _lifetime.Dispose();
        _workers.Dispose();
    }
}
