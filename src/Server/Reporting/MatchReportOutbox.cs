using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MphRead.Identity;

namespace MphRead.Reporting;

public enum ReportSubmissionState { Queued, DurablyStored, BackendAccepted, Quarantined, Failed }
public sealed class ReportSubmission
{
    private int _state;
    private ReportSubmission? _alias;
    public ReportSubmissionState State => Volatile.Read(ref _alias)?.State ?? (ReportSubmissionState)Volatile.Read(ref _state);
    internal void Alias(ReportSubmission receipt) => Volatile.Write(ref _alias, receipt);
    public string? Error { get; internal set; }
    public string? PayloadHash { get; internal set; }
    internal void Set(ReportSubmissionState state) => Volatile.Write(ref _state, (int)state);
}
public sealed record MatchReportOutboxOptions(string Directory, int MaximumReports = 128,
    long MaximumBytes = 64 * 1024 * 1024, int MaximumReportBytes = 512 * 1024, int QueueCapacity = 16);
public readonly record struct OutboxStatus(bool Ready, int ReservedReports, long ReservedBytes, int Quarantined,
    int DurablePending, int QueuedPending, double OldestAgeSeconds, string? LastError);

/// <summary>Two background owners: spool writes never wait for HTTP. FIFO submission survives rotation/restart.</summary>
public sealed class MatchReportOutbox : IAsyncDisposable, IDisposable
{
    private sealed record Pending(MatchReportV1 Report, ReportSubmission Receipt);
    private sealed record Envelope(int Version, long Sequence, Guid MatchId, string PayloadHash, byte[] Payload);
    private sealed record Entry(Envelope Envelope, string Path, ReportSubmission Receipt, int Bytes, DateTimeOffset StoredAt);
    private readonly MatchReportOutboxOptions _options;
    private readonly IMatchReportTransport _transport;
    private readonly Action<string, byte[]> _persist;
    private readonly Channel<Pending> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly SortedDictionary<long, Entry> _entries = new();
    private readonly Task _writer, _sender;
    private readonly TaskCompletionSource _recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FileStream _ownership;
    private int _reserved, _quarantined, _disposed;
    private long _bytes, _sequence;
    private bool _ready, _storageFault, _permanentFault, _accepting = true;
    private string? _error;
    public MatchReportOutbox(MatchReportOutboxOptions options, IMatchReportTransport transport) : this(options, transport, DurableSpool.Write) { }
    internal MatchReportOutbox(MatchReportOutboxOptions options, IMatchReportTransport transport, Action<string, byte[]> persist)
    {
        if (options.MaximumReports < 1 || options.QueueCapacity < 1 || options.MaximumReportBytes < 1024
            || options.MaximumBytes < options.MaximumReportBytes * 2L) throw new ArgumentOutOfRangeException(nameof(options));
        _options = options; _transport = transport; _persist = persist;
        System.IO.Directory.CreateDirectory(options.Directory);
        _ownership = new FileStream(Path.Combine(options.Directory, ".owner"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(options.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _writer = Task.Run(WriteLoop); _sender = Task.Run(SendLoop);
    }
    public OutboxStatus Status { get { lock (_gate) return new(_ready, _reserved, _bytes, _quarantined, _entries.Count,
        Math.Max(0, _reserved - _quarantined - _entries.Count), _entries.Count == 0 ? 0
            : Math.Max(0, (DateTimeOffset.UtcNow - _entries.Values.First().StoredAt).TotalSeconds), _error); } }
    // Reserve worst-case encoded envelope space before accepting RAM ownership.
    private long ReservationBytes => _options.MaximumReportBytes * 2L;
    public bool Healthy { get { lock (_gate) return _ready && _accepting && !_storageFault && !_permanentFault; } }
    public bool CanAccept { get { lock (_gate) return _ready && _accepting && !_storageFault && !_permanentFault && _reserved < _options.MaximumReports && _bytes + ReservationBytes <= _options.MaximumBytes; } }
    public sealed class Reservation : IDisposable
    {
        internal readonly MatchReportOutbox Owner;
        internal int State;
        internal Reservation(MatchReportOutbox owner) => Owner = owner;
        public void Dispose()
        {
            lock (Owner._gate)
            {
                if (State != 0) return;
                State = 2; Owner._reserved--; Owner._bytes -= Owner.ReservationBytes;
            }
        }
    }
    public bool TryReserve(out Reservation? reservation)
    {
        lock (_gate)
        {
            reservation = null;
            if (!CanAccept) return false;
            _reserved++; _bytes += ReservationBytes; reservation = new(this); return true;
        }
    }
    public bool TryEnqueue(MatchReportV1 report, out ReportSubmission? receipt)
    {
        receipt = null;
        if (!TryReserve(out Reservation? reservation)) return false;
        using (reservation) return TryEnqueue(report, reservation!, out receipt);
    }
    public bool TryEnqueue(MatchReportV1 report, Reservation reservation, out ReportSubmission? receipt)
    {
        receipt = null;
        lock (_gate)
        {
            if (reservation.Owner != this || reservation.State != 0 || !_accepting) return false;
            receipt = new();
            if (_queue.Writer.TryWrite(new(report, receipt))) { reservation.State = 1; return true; }
            receipt = null; return false;
        }
    }
    private void Error(string message) { lock (_gate) _error = message; Console.Error.WriteLine("[match-outbox] " + message); }
    private async Task WriteLoop()
    {
        try
        {
            Recover(); lock (_gate) _ready = true; _recovered.SetResult();
            await foreach (Pending pending in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                byte[] payload;
                try
                {
                    if (!pending.Report.IsValid) throw new InvalidDataException("Invalid report envelope");
                    payload = JsonSerializer.SerializeToUtf8Bytes(pending.Report);
                    if (payload.Length > _options.MaximumReportBytes) throw new InvalidDataException("Report exceeds configured payload limit");
                }
                catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
                { RejectPending(pending, e.Message); continue; }
                string hash = Convert.ToHexString(SHA256.HashData(payload));
                pending.Receipt.PayloadHash = hash;
                Entry? duplicate;
                lock (_gate) duplicate = _entries.Values.FirstOrDefault(e => e.Envelope.MatchId == pending.Report.MatchId);
                if (duplicate != null)
                {
                    if (duplicate.Envelope.PayloadHash != hash) RejectPending(pending, "Conflicting payload under existing MatchId");
                    else { pending.Receipt.Alias(duplicate.Receipt); ReleaseReservation(); }
                    continue;
                }
                var envelope = new Envelope(1, ++_sequence, pending.Report.MatchId, hash, payload);
                byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(envelope);
                if (encoded.Length > ReservationBytes) { RejectPending(pending, "Encoded spool envelope exceeds reservation"); continue; }
                string path = Path.Combine(_options.Directory, $"{envelope.Sequence:D20}-{envelope.MatchId:D}.json");
                while (true)
                {
                    try
                    {
                        _persist(path, encoded);
                        lock (_gate)
                        {
                            _bytes += encoded.Length - ReservationBytes;
                            _entries.Add(envelope.Sequence, new(envelope, path, pending.Receipt, encoded.Length, DateTimeOffset.UtcNow));
                            if (!_permanentFault) _error = null; _storageFault = false;
                        }
                        pending.Receipt.Set(ReportSubmissionState.DurablyStored); break;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                    { lock (_gate) _storageFault = true; pending.Receipt.Error = "Durable write failed"; Error("Durable write failed: " + e.GetType().Name); await Task.Delay(1000, _stop.Token); }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { lock (_gate) _permanentFault = true; Error("Outbox writer stopped: " + e.GetType().Name); _recovered.TrySetException(e); }
    }
    private void ReleaseReservation() { lock (_gate) { _reserved--; _bytes -= ReservationBytes; } }
    private void RejectPending(Pending pending, string reason)
    { pending.Receipt.Error = reason; pending.Receipt.Set(ReportSubmissionState.Failed); ReleaseReservation(); lock (_gate) _permanentFault = true; Error(reason); }
    private void Recover()
    {
        foreach (string path in System.IO.Directory.EnumerateFiles(_options.Directory))
        {
            string extension = Path.GetExtension(path);
            if (Path.GetFileName(path) == ".owner") continue;
            long length = new FileInfo(path).Length;
            lock (_gate) { _reserved++; _bytes += length; }
            if (_reserved > _options.MaximumReports || _bytes > _options.MaximumBytes) throw new IOException("Existing spool exceeds configured bounds");
            if (extension == ".quarantine") { lock (_gate) { _quarantined++; _permanentFault = true; } continue; }
            try
            {
                if (extension != ".json" || length > ReservationBytes) throw new InvalidDataException();
                Envelope envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path)) ?? throw new InvalidDataException();
                if (envelope.Version != 1 || envelope.Sequence <= 0 || envelope.MatchId == Guid.Empty
                    || envelope.Payload.Length > _options.MaximumReportBytes
                    || envelope.PayloadHash != Convert.ToHexString(SHA256.HashData(envelope.Payload))) throw new InvalidDataException();
                MatchReportV1 report = JsonSerializer.Deserialize<MatchReportV1>(envelope.Payload) ?? throw new InvalidDataException();
                if (!report.IsValid || report.MatchId != envelope.MatchId || _entries.ContainsKey(envelope.Sequence)
                    || _entries.Values.Any(e => e.Envelope.MatchId == envelope.MatchId)) throw new InvalidDataException();
                var receipt = new ReportSubmission { PayloadHash = envelope.PayloadHash }; receipt.Set(ReportSubmissionState.DurablyStored);
                lock (_gate) _entries.Add(envelope.Sequence, new(envelope, path, receipt, checked((int)length), File.GetLastWriteTimeUtc(path)));
                _sequence = Math.Max(_sequence, envelope.Sequence);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException or NullReferenceException)
            { Quarantine(path); }
        }
    }
    private void Quarantine(string path)
    {
        File.Move(path, path + ".quarantine", overwrite: false); DurableSpool.SyncDirectory(_options.Directory);
        lock (_gate) { _quarantined++; _permanentFault = true; }
        Error("Quarantined invalid or rejected report: " + Path.GetFileName(path));
    }
    private async Task SendLoop()
    {
        try
        {
            await _recovered.Task.WaitAsync(_stop.Token);
            int attempts = 0;
            while (!_stop.IsCancellationRequested)
            {
                Entry? entry; lock (_gate) entry = _entries.Values.FirstOrDefault();
                if (entry == null) { await Task.Delay(100, _stop.Token); continue; }
                ReportDelivery delivery;
                try { delivery = await _transport.SubmitAsync(entry.Envelope.MatchId, entry.Envelope.PayloadHash, entry.Envelope.Payload, _stop.Token); }
                catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !_stop.IsCancellationRequested)
                { delivery = new(ReportDeliveryKind.Retry, Detail: "Transport unavailable"); }
                if (delivery.Kind == ReportDeliveryKind.Accepted)
                {
                    File.Delete(entry.Path); DurableSpool.SyncDirectory(_options.Directory);
                    lock (_gate) { _entries.Remove(entry.Envelope.Sequence); _reserved--; _bytes -= entry.Bytes; }
                    entry.Receipt.Set(ReportSubmissionState.BackendAccepted); attempts = 0;
                }
                else if (delivery.Kind == ReportDeliveryKind.Rejected)
                {
                    Quarantine(entry.Path); lock (_gate) _entries.Remove(entry.Envelope.Sequence);
                    entry.Receipt.Error = delivery.Detail; entry.Receipt.Set(ReportSubmissionState.Quarantined); attempts = 0;
                }
                else
                {
                    if (delivery.Kind == ReportDeliveryKind.Unauthorized) lock (_gate) _permanentFault = true;
                    Error(delivery.Detail ?? "Report submission deferred");
                    if (delivery.Kind == ReportDeliveryKind.Unauthorized)
                    {
                        // Explicit operator credential repair/restart is required; do not hammer authentication.
                        await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                    }
                    // FIFO is deliberate: current-RP transactions must retain per-server submission order.
                    double seconds = delivery.Kind == ReportDeliveryKind.Unauthorized ? 60
                        : Math.Min(60, Math.Pow(2, Math.Min(++attempts, 6))) + Random.Shared.NextDouble();
                    if (delivery.RetryAfter is { } requested) seconds = Math.Max(seconds, Math.Clamp(requested.TotalSeconds, 0, 300));
                    await Task.Delay(TimeSpan.FromSeconds(seconds), _stop.Token);
                    if (delivery.Kind != ReportDeliveryKind.Unauthorized) lock (_gate)
                        if (!_permanentFault && !_storageFault) _error = null;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { lock (_gate) _permanentFault = true; Error("Outbox sender stopped: " + e.GetType().Name); }
    }
    public async Task<bool> StopAsync(TimeSpan deadline)
    {
        lock (_gate) _accepting = false;
        _queue.Writer.TryComplete();
        bool drained = false;
        try { await _writer.WaitAsync(deadline); drained = Status.QueuedPending == 0; }
        catch (TimeoutException) { Error("Shutdown deadline expired before all queued reports were durable"); }
        _stop.Cancel();
        try { await Task.WhenAll(_writer, _sender).WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
        return drained;
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync(TimeSpan.FromSeconds(10));
        Task workers = Task.WhenAll(_writer, _sender);
        if (workers.IsCompleted) { _ownership.Dispose(); _stop.Dispose(); }
        else
        {
            // Synchronous storage or an uncooperative injected transport may still own the spool.
            // Keep the exclusive lock and token source alive until both workers actually exit.
            _ = workers.ContinueWith(_ => { _ownership.Dispose(); _stop.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
