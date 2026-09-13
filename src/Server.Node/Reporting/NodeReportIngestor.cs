using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Reporting;

namespace ProjectPrime.Server.Node.Reporting;

/// <summary>Node-owned bounded artifact ingestion. HTTP and durable retry ownership remain in MatchReportOutbox.</summary>
public sealed class NodeReportIngestor : IAsyncDisposable
{
    private sealed record Intake(MatchSpec Spec, WorkerId Worker, Guid Incarnation, uint WireId, string Root,
        MatchReportReady Ready, TaskCompletionSource<bool> Durability);
    private sealed record Binding(Guid MatchId, string Hash, MatchReportV1? Pending, string SpecHash, WorkerId Worker, Guid Incarnation, uint WireId, string ArtifactRoot);
    private readonly MatchReportOutbox _outbox;
    private readonly string _bindings;
    private readonly Channel<Intake> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Dictionary<MatchId, MatchReportOutbox.Reservation> _reservations = new();
    private readonly HashSet<TaskCompletionSource<bool>> _durabilityTasks = [];
    private readonly Task _reader;
    private volatile bool _ready;
    private bool _stopping;
    private string? _error;
    public string? LastError => Volatile.Read(ref _error);
    private int _bindingCount;
    private long _bindingBytes;
    public bool CanAcceptOfficial
    {
        get { lock (_gate) return _ready && !_stopping && _error == null && _outbox.CanAccept
            && _bindingCount + _reservations.Count < MaximumBindings
            && _bindingBytes + (_reservations.Count + 1L) * MatchReportReady.MaximumPayloadBytes * 2L <= MaximumBindingBytes; }
    }
    public long Ingested => Interlocked.Read(ref _ingested);
    private long _ingested;
    private long _pending;
    private const int MaximumBindings = 4096;
    private const long MaximumBindingBytes = 64 * 1024 * 1024;

    public NodeReportIngestor(MatchReportOutbox outbox, string receiptDirectory, int queueCapacity = 32)
    {
        if (!Path.IsPathFullyQualified(receiptDirectory) || queueCapacity is < 1 or > 1024) throw new ArgumentException("Invalid ingestion storage or capacity.");
        _outbox = outbox; _bindings = receiptDirectory; Directory.CreateDirectory(_bindings);
        RejectLink(_bindings);
        _queue = Channel.CreateBounded<Intake>(new BoundedChannelOptions(queueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _reader = RunAsync();
    }
    public bool TryReserve(MatchId matchId)
    {
        lock (_gate)
        {
            if (_reservations.ContainsKey(matchId)) return true;
            if (!CanAcceptOfficial || !_outbox.TryReserve(out var reservation)) return false;
            _reservations.Add(matchId, reservation!); return true;
        }
    }
    public void CancelReservation(MatchId matchId)
    { lock (_gate) if (_reservations.Remove(matchId, out var reservation)) reservation.Dispose(); }

    public bool TryQueue(MatchSpec spec, WorkerId workerId, Guid workerIncarnation, uint wireMatchId,
        string artifactDirectory, MatchReportReady ready)
        => TryQueue(spec, workerId, workerIncarnation, wireMatchId, artifactDirectory, ready, out _);

    /// <summary>Queues one report and returns the Node-owned durability edge.
    /// A successful return means only that bounded intake accepted the notice;
    /// callers must await the returned task before retiring the match.</summary>
    public bool TryQueue(MatchSpec spec, WorkerId workerId, Guid workerIncarnation, uint wireMatchId,
        string artifactDirectory, MatchReportReady ready, out Task durability)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        durability = completion.Task;
        lock (_gate)
        {
            // Admission and publication are one gate-owned transaction. The
            // reader can fail/close the channel concurrently, so never leave a
            // task registered after a rejected write and never publish after a
            // failure has been observed.
            if (_error != null || !_ready || _stopping || !Path.IsPathFullyQualified(artifactDirectory))
            {
                completion.TrySetException(new IOException("Report ingestion is unavailable."));
                return false;
            }
            Interlocked.Increment(ref _pending);
            _durabilityTasks.Add(completion);
            if (_queue.Writer.TryWrite(new(spec, workerId, workerIncarnation, wireMatchId, artifactDirectory, ready, completion)))
                return true;
            _durabilityTasks.Remove(completion);
            Interlocked.Decrement(ref _pending);
            completion.TrySetException(new IOException("Report ingestion queue is closed."));
            return false;
        }
    }

    /// <summary>Validate and remove a report produced for a guest match without
    /// transferring it to the Backend outbox. This keeps local completion handling
    /// intact while bounding worker artifact retention.</summary>
    public bool TryDiscard(MatchSpec spec, WorkerId workerId, Guid workerIncarnation, uint wireMatchId,
        string artifactDirectory, MatchReportReady ready)
        => TryDiscardArtifact(spec, workerId, workerIncarnation, wireMatchId, artifactDirectory, ready);

    public static bool TryDiscardArtifact(MatchSpec spec, WorkerId workerId, Guid workerIncarnation, uint wireMatchId,
        string artifactDirectory, MatchReportReady ready)
    {
        try
        {
            spec.Validate(); ready.Validate();
            if (ready.MatchId != spec.MatchId || ready.ReportId != spec.MatchId.Value
                || ready.WorkerId != workerId || ready.WorkerIncarnation != workerIncarnation
                || !Path.IsPathFullyQualified(artifactDirectory)) return false;
            string root = Path.GetFullPath(artifactDirectory);
            string reports = Path.Combine(root, "reports");
            string path = Path.Combine(reports, ready.ReportId.ToString("N") + ".json");
            if (!Directory.Exists(root) || !Directory.Exists(reports)) return false;
            RejectLink(root); RejectLink(reports);
            if (!File.Exists(path)) return false;
            RejectLink(path);
            var info = new FileInfo(path);
            if (info.Length != ready.PayloadBytes || info.Length > MatchReportReady.MaximumPayloadBytes) return false;
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != info.Length || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), ready.PayloadHash, StringComparison.OrdinalIgnoreCase))
                return false;
            MatchReportV1 report = JsonSerializer.Deserialize<MatchReportV1>(bytes)
                ?? throw new InvalidDataException("Missing report body.");
            MatchReportBinding.Validate(spec, wireMatchId, report);
            File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException
            or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return false; }
    }
    public async Task WaitForDurabilityAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (LastError != null) throw new IOException(LastError);
            lock (_gate) if (_ready && Interlocked.Read(ref _pending) == 0 && _reservations.Count == 0) return;
            await Task.Delay(10, cancellationToken);
        }
    }
    private async Task RunAsync()
    {
        try
        {
            while (!_outbox.Status.Ready) await Task.Delay(10, _stop.Token);
            var files = Directory.GetFiles(_bindings, "*.json");
            if (files.Length >= MaximumBindings || files.Sum(p => new FileInfo(p).Length) > MaximumBindingBytes) throw new IOException("Report receipt storage is full.");
            _bindingCount = files.Length; _bindingBytes = files.Sum(p => new FileInfo(p).Length);
            foreach (string path in files)
            {
                Binding binding = ReadBinding(path);
                if (binding.Pending is { } pending) await StoreAsync(pending, binding.Hash, path);
                CleanupArtifact(binding);
            }
            lock (_gate) if (!_stopping) _ready = true;
            await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                bool durable = false;
                try
                {
                    item.Ready.Validate();
                    if (item.Ready.MatchId != item.Spec.MatchId || item.Ready.ReportId != item.Spec.MatchId.Value || item.Ready.WorkerId != item.Worker || item.Ready.WorkerIncarnation != item.Incarnation)
                        throw new InvalidDataException("Report artifact sender does not own the frozen placement.");
                    string bindingPath = Path.Combine(_bindings, item.Spec.MatchId.Value.ToString("N") + ".json");
                    string specHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item.Spec)));
                    if (File.Exists(bindingPath))
                    {
                        Binding prior = ReadBinding(bindingPath);
                        if (prior.Hash != item.Ready.PayloadHash || prior.SpecHash != specHash || prior.Worker != item.Worker
                            || prior.Incarnation != item.Incarnation || prior.WireId != item.WireId
                            || Path.GetFullPath(prior.ArtifactRoot) != Path.GetFullPath(item.Root))
                            throw new InvalidDataException("Conflicting immutable report placement.");
                        if (prior.Pending == null)
                        {
                            CleanupArtifact(prior); CancelReservation(item.Spec.MatchId);
                            Interlocked.Increment(ref _ingested); durable = true; continue;
                        }
                    }
                    string directory = Path.Combine(item.Root, "reports"); RejectLink(item.Root); RejectLink(directory);
                    string path = Path.Combine(directory, item.Ready.ReportId.ToString("N") + ".json"); RejectLink(path);
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (file.Length != item.Ready.PayloadBytes) throw new InvalidDataException("Report artifact length mismatch.");
                    byte[] bytes = new byte[item.Ready.PayloadBytes]; await file.ReadExactlyAsync(bytes, _stop.Token);
                    if (file.Length != bytes.Length) throw new InvalidDataException("Report artifact changed during read.");
                    string hash = Convert.ToHexString(SHA256.HashData(bytes));
                    if (!string.Equals(hash, item.Ready.PayloadHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Report artifact hash mismatch.");
                    var report = JsonSerializer.Deserialize<MatchReportV1>(bytes) ?? throw new InvalidDataException("Missing report body.");
                    MatchReportBinding.Validate(item.Spec, item.WireId, report);
                    if (File.Exists(bindingPath))
                    {
                        var existing = ReadBinding(bindingPath);
                        if (existing.MatchId != report.MatchId || existing.Hash != hash) throw new InvalidDataException("Conflicting payload under immutable MatchId.");
                        if (existing.Pending == null)
                        {
                            CancelReservation(item.Spec.MatchId); Interlocked.Increment(ref _ingested);
                            durable = true; continue;
                        }
                    }
                    else
                    {
                        var entries = Directory.GetFiles(_bindings, "*.json");
                        if (entries.Length >= MaximumBindings || entries.Sum(p => new FileInfo(p).Length) + bytes.Length * 2L > MaximumBindingBytes)
                            throw new IOException("Report receipt storage is full.");
                        DurableSpool.Write(bindingPath, JsonSerializer.SerializeToUtf8Bytes(new Binding(report.MatchId, hash, report, specHash, item.Worker, item.Incarnation, item.WireId, item.Root)));
                        lock (_gate) { _bindingCount++; _bindingBytes += new FileInfo(bindingPath).Length; }
                    }
                    await StoreAsync(report, hash, bindingPath);
                    file.Dispose(); CleanupArtifact(ReadBinding(bindingPath)); // Node durable ownership acknowledges this exact generated artifact.
                    Interlocked.Increment(ref _ingested);
                    durable = true;
                }
                catch (Exception error)
                {
                    item.Durability.TrySetException(error);
                    throw;
                }
                finally
                {
                    // A report becomes durable only after StoreAsync and
                    // artifact cleanup succeed.  Never convert a malformed or
                    // missing artifact into a successful disposition while
                    // unwinding the queue item.
                    if (durable) item.Durability.TrySetResult(true);
                    else
                        item.Durability.TrySetException(new IOException("Report durability was not established."));
                    lock (_gate) _durabilityTasks.Remove(item.Durability);
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException error) when (_stop.IsCancellationRequested)
        {
            lock (_gate) { _ready = false; _queue.Writer.TryComplete(error); }
            FailDurability(error);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                Volatile.Write(ref _error, error.GetType().Name + ": report ingestion failed closed.");
                _ready = false;
                _queue.Writer.TryComplete(error);
            }
            FailDurability(error);
        }
        finally
        {
            lock (_gate)
            {
                _ready = false;
                _queue.Writer.TryComplete();
            }
        }
    }

    private void FailDurability(Exception error)
    {
        TaskCompletionSource<bool>[] pending;
        lock (_gate) { pending = _durabilityTasks.ToArray(); _durabilityTasks.Clear(); }
        foreach (TaskCompletionSource<bool> completion in pending)
            completion.TrySetException(error);
    }
    private async Task StoreAsync(MatchReportV1 report, string hash, string bindingPath)
    {
        if (!report.IsValid || Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(report))) != hash)
            throw new InvalidDataException("Invalid durable report binding.");
        MatchReportOutbox.Reservation? reservation;
        lock (_gate) _reservations.Remove(new MatchId(report.MatchId), out reservation);
        if (reservation == null && !_outbox.TryReserve(out reservation)) throw new IOException("Report outbox unavailable or full.");
        using (reservation)
        {
            if (!_outbox.TryEnqueue(report, reservation!, out var receipt)) throw new IOException("Report outbox queue is full.");
            while (receipt!.State == ReportSubmissionState.Queued) await Task.Delay(10, _stop.Token);
            if (receipt.State is not (ReportSubmissionState.DurablyStored or ReportSubmissionState.BackendAccepted)) throw new IOException("Report durability rejected.");
        }
        // Once the outbox owns a durable body, retain only its immutable identity binding.
        string temporary = bindingPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            long previousBytes = new FileInfo(bindingPath).Length;
            DurableSpool.Write(temporary, JsonSerializer.SerializeToUtf8Bytes(ReadBinding(bindingPath) with { Pending = null }));
            File.Move(temporary, bindingPath, true); DurableSpool.SyncDirectory(_bindings);
            lock (_gate) _bindingBytes += new FileInfo(bindingPath).Length - previousBytes;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static Binding ReadBinding(string path)
    {
        RejectLink(path);
        if (new FileInfo(path).Length > MatchReportReady.MaximumPayloadBytes * 2L) throw new InvalidDataException("Oversized receipt.");
        var value = JsonSerializer.Deserialize<Binding>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Invalid receipt.");
        if (value.MatchId == Guid.Empty || Path.GetFileName(path) != value.MatchId.ToString("N") + ".json"
            || !Path.IsPathFullyQualified(value.ArtifactRoot) || value.SpecHash is not { Length: 64 } || value.SpecHash.Any(c => !char.IsAsciiHexDigit(c))
            || value.Pending is { } pending && pending.MatchId != value.MatchId || value.Worker.Value == Guid.Empty || value.Incarnation == Guid.Empty || value.WireId == 0
            || value.Hash is not { Length: 64 } || value.Hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid receipt identity.");
        return value;
    }
    private static void CleanupArtifact(Binding binding)
    {
        string directory = Path.Combine(binding.ArtifactRoot, "reports");
        string path = Path.Combine(directory, binding.MatchId.ToString("N") + ".json");
        if (!File.Exists(path)) return;
        RejectLink(binding.ArtifactRoot); RejectLink(directory); RejectLink(path);
        if (new FileInfo(path).Length > MatchReportReady.MaximumPayloadBytes
            || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != binding.Hash)
            throw new IOException("Report cleanup found a conflicting artifact.");
        File.Delete(path);
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Artifact links are not allowed."); }
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            _ready = false;
            _queue.Writer.TryComplete();
        }
        try { await _reader.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { _stop.Cancel(); await _reader; }
        lock (_gate)
        {
            foreach (var reservation in _reservations.Values) reservation.Dispose();
            _reservations.Clear();
        }
        FailDurability(new ObjectDisposedException(nameof(NodeReportIngestor)));
        _stop.Dispose();
    }
}
