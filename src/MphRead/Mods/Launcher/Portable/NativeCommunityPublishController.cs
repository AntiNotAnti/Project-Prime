using System;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

internal enum NativeCommunityPublishVisibility { Published, Unlisted, Draft }
internal sealed record NativeCommunityPublishSource(Guid MapId, string RuntimeName);
internal sealed record NativeCommunityPublishDraft(Guid MapId, string RuntimeName, bool IsRevision, string ParentHash)
{
    [JsonIgnore] internal object? AccountLease { get; init; }
    [JsonIgnore] internal object? Account { get; init; }
}
internal sealed record NativeCommunityPublishReceipt(Guid JobId, Guid MapId, string RuntimeName, string Hash,
    bool IsRevision, string ParentHash, NativeCommunityPublishVisibility Visibility)
{
    [JsonIgnore] internal object? AccountLease { get; init; }
    [JsonIgnore] internal object? Account { get; init; }
}
internal sealed record NativeCommunityPublishProgress(Guid JobId, string Hash, string State, long Received, long Total,
    bool Complete, bool CanPause, bool CanResume, bool NeedsReview);
internal interface INativeCommunityPublishAuthority
{
    bool IsCurrentAccountLease(object? lease);
    Task<NativeCommunityPublishDraft> ReadAsync(NativeCommunityPublishSource source, CancellationToken token);
    Task<NativeCommunityPublishReceipt> QueueAsync(NativeCommunityPublishDraft draft, NativeCommunityPublishVisibility visibility, string notes, CancellationToken token);
    Task<NativeCommunityPublishProgress> ReadTransferAsync(NativeCommunityPublishReceipt receipt, CancellationToken token);
    Task<NativeCommunityPublishProgress> ControlTransferAsync(NativeCommunityPublishReceipt receipt, bool pause, CancellationToken token);
}
internal sealed record NativeCommunityPublishView(long Revision, bool Busy, bool Ready, string Status,
    string Name, string ParentHash, NativeCommunityPublishVisibility Visibility, string Confirmation,
    string PackageHash, string TransferState, string Progress, bool CanPause, bool CanResume, bool NeedsReview)
{
    internal bool AccountCurrent { get; init; }
}

/// <summary>Owner-thread state for one frozen authoring handoff; no editor, credentials or package writer.</summary>
internal sealed class NativeCommunityPublishController : IDisposable
{
    private readonly INativeCommunityPublishAuthority _authority;
    private readonly TimeProvider _time;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<(int Epoch, object? Value, bool Failed)> _done = new();
    private readonly object _publication = new();
    private CancellationTokenSource? _active;
    private NativeCommunityPublishSource? _source;
    private NativeCommunityPublishDraft? _draft;
    private NativeCommunityPublishReceipt? _receipt;
    private NativeCommunityPublishProgress? _progress;
    private NativeCommunityPublishVisibility _visibility = NativeCommunityPublishVisibility.Draft;
    private string _status = "OPEN A CURRENT STUDIO PROJECT", _confirmation = "", _notes = "";
    private long _revision;
    private int _epoch;
    private bool _busy, _disposed;
    private DateTimeOffset _poll;
    internal NativeCommunityPublishController(INativeCommunityPublishAuthority authority, TimeProvider? time = null)
    { _authority = authority ?? throw new ArgumentNullException(nameof(authority)); _time = time ?? TimeProvider.System; }
    internal bool Open(NativeCommunityPublishSource source)
    {
        Verify(); CancelCore();
        if (source.MapId == Guid.Empty || string.IsNullOrWhiteSpace(source.RuntimeName) || source.RuntimeName.Length > 128)
        { _status = "CURRENT PROJECT IDENTITY IS INVALID"; Touch(); return false; }
        _source = source; _visibility = NativeCommunityPublishVisibility.Draft;
        Read(); return true;
    }
    internal bool Refresh()
    {
        Verify(); if (_source == null || _busy || _receipt != null) return false;
        _confirmation = ""; _notes = ""; Read(); return true;
    }
    private void Read()
    {
        var source = _source!; _draft = null;
        _status = "VERIFYING THE CURRENT COMMUNITY ACCOUNT AND PROJECT PARENT";
        Start(token => _authority.ReadAsync(source, token));
    }
    internal bool SetVisibility(int visibility)
    {
        Verify(); if (_busy || _draft == null || _receipt != null || _confirmation.Length != 0 || !Enum.IsDefined((NativeCommunityPublishVisibility)visibility)) return false;
        _visibility = (NativeCommunityPublishVisibility)visibility; Touch(); return true;
    }
    internal bool Propose(string notes)
    {
        Verify(); Tick();
        if (_busy || _draft == null || _receipt != null || _confirmation.Length != 0) return false;
        if (notes == null || notes.Length > 4000 || notes.Contains('\0'))
        { _status = "RELEASE NOTES MUST BE AT MOST 4,000 CHARACTERS"; Touch(); return false; }
        _notes = notes;
        _confirmation = "QUEUE " + _visibility.ToString().ToUpperInvariant() + " // " + _draft.RuntimeName
            + (_draft.IsRevision ? "\nEXPECTED PARENT SHA-256 // " + _draft.ParentHash : "\nNEW COMMUNITY PROJECT")
            + "\nThe captured authoring snapshot will be validated and packaged. Referenced asset files are checked during the build."
            + "\nOnce admitted, the durable upload continues when this page closes. A changed parent stops for review; it is never silently overridden.";
        Touch(); return true;
    }
    internal bool Dismiss()
    { Verify(); if (_busy || _confirmation.Length == 0) return false; _confirmation = ""; _notes = ""; Touch(); return true; }
    internal bool Confirm()
    {
        Verify(); Tick(); if (_busy || _draft == null || _confirmation.Length == 0 || _receipt != null) return false;
        var draft = _draft; var visibility = _visibility; string notes = _notes;
        _confirmation = ""; _status = "PREPARING A VALIDATED IMMUTABLE PACKAGE // NO PUBLICATION CONFIRMED YET";
        Start(token => _authority.QueueAsync(draft, visibility, notes, token)); return true;
    }
    internal bool ControlTransfer(bool pause)
    {
        Verify(); Tick(); if (_busy || _receipt == null || _progress == null || (pause ? !_progress.CanPause : !_progress.CanResume)) return false;
        var receipt = _receipt;
        Start(token => _authority.ControlTransferAsync(receipt, pause, token)); return true;
    }
    internal void CancelPreparation()
    {
        Verify(); if (!_busy || _receipt != null) return;
        Retire(); _busy = false; _draft = null; _confirmation = ""; _notes = "";
        _status = "PREPARATION WAIT CANCELLED // AN ALREADY ADMITTED DURABLE UPLOAD MAY REMAIN; REFRESH BEFORE RETRY"; Touch();
    }
    internal NativeCommunityPublishView Tick()
    {
        Verify();
        if (_draft != null && !_authority.IsCurrentAccountLease(_draft.AccountLease))
        {
            Retire(); _busy = false; _draft = null; _receipt = null; _progress = null; _confirmation = ""; _notes = "";
            _status = "ACCOUNT CHANGED // CLOSE AND REOPEN PUBLISH"; Touch();
        }
        while (_done.TryDequeue(out var item))
        {
            if (item.Epoch != _epoch) continue;
            _active?.Dispose(); _active = null; _busy = false;
            if (item.Failed)
            {
                if (_receipt == null) _draft = null;
                _status = "PUBLICATION OUTCOME UNVERIFIED // REFRESH OR CHECK TRANSFERS; NO CHANGE IS ASSUMED";
                _poll = _time.GetUtcNow().AddMilliseconds(500); Touch(); continue;
            }
            switch (item.Value)
            {
                case NativeCommunityPublishDraft draft when _source != null && draft.MapId == _source.MapId
                    && draft.RuntimeName == _source.RuntimeName && draft.Account != null && draft.AccountLease != null
                    && _authority.IsCurrentAccountLease(draft.AccountLease)
                    && (draft.IsRevision ? Hash(draft.ParentHash) : draft.ParentHash == ""):
                    _draft = draft; _status = "FROZEN STUDIO PROJECT // CHOOSE VISIBILITY AND REVIEW BEFORE QUEUEING"; break;
                case NativeCommunityPublishReceipt receipt when _draft != null && Valid(receipt, _draft, _visibility):
                    _receipt = receipt; _status = "EXACT PACKAGE QUEUED // PUBLICATION IS NOT YET VERIFIED"; _poll = DateTimeOffset.MinValue; break;
                case NativeCommunityPublishProgress progress when _receipt != null && progress.JobId == _receipt.JobId
                    && progress.Hash == _receipt.Hash && progress.Total > 0 && progress.Received >= 0 && progress.Received <= progress.Total
                    && KnownState(progress.State) && progress.Complete == (progress.State == "COMPLETED")
                    && progress.NeedsReview == (progress.State == "NEEDSREVIEW")
                    && (!progress.Complete || progress.Received == progress.Total && !progress.CanPause && !progress.CanResume):
                    _progress = progress; _status = progress.Complete ? "PUBLICATION VERIFIED // EXACT PACKAGE RETAINED IN COMMUNITY HISTORY"
                        : progress.NeedsReview ? "PARENT CONFLICT // NOTHING IS ASSUMED PUBLISHED; REVIEW CURRENT HISTORY"
                        : "DURABLE UPLOAD // " + progress.State; _poll = _time.GetUtcNow().AddMilliseconds(500); break;
                default:
                    _draft = null; _receipt = null; _progress = null; _status = "INVALID COMMUNITY RESPONSE // CLOSE AND REOPEN PUBLISH"; break;
            }
            Touch();
        }
        if (!_busy && _receipt != null && _progress?.Complete != true && _time.GetUtcNow() >= _poll)
        {
            var receipt = _receipt; _poll = _time.GetUtcNow().AddMilliseconds(500);
            Start(token => _authority.ReadTransferAsync(receipt, token));
        }
        return new(_revision, _busy, !_busy && _draft != null && _receipt == null && _confirmation.Length == 0,
            _status, _source?.RuntimeName ?? "", _draft?.ParentHash ?? "", _visibility, _confirmation,
            _receipt?.Hash ?? "", _progress?.State ?? "", _progress == null ? "" : $"{_progress.Received:N0} / {_progress.Total:N0} BYTES",
            !_busy && _progress?.CanPause == true, !_busy && _progress?.CanResume == true, _progress?.NeedsReview == true)
            { AccountCurrent = _draft != null };
    }
    private static bool Valid(NativeCommunityPublishReceipt receipt, NativeCommunityPublishDraft draft, NativeCommunityPublishVisibility visibility)
        => receipt.JobId != Guid.Empty && receipt.MapId == draft.MapId && receipt.RuntimeName == draft.RuntimeName
        && Hash(receipt.Hash) && receipt.IsRevision == draft.IsRevision && receipt.ParentHash == draft.ParentHash && receipt.Visibility == visibility
        && ReferenceEquals(receipt.AccountLease, draft.AccountLease) && ReferenceEquals(receipt.Account, draft.Account);
    private static bool Hash(string value) => value != null && value.Length == 64 && System.Linq.Enumerable.All(value, Uri.IsHexDigit);
    private static bool KnownState(string state) => state is "QUEUED" or "PREPARING" or "UPLOADING" or "DOWNLOADING" or "VERIFYING"
        or "RETRYABLE" or "PAUSED" or "NEEDSREVIEW" or "COMPLETED" or "FAILED" or "CANCELLED" or "NEEDSAUTHORIZATION";
    private void Start<T>(Func<CancellationToken, Task<T>> action)
    {
        Retire(); _busy = true; var cancel = _active = new(); int epoch = _epoch; Touch();
        _ = Task.Run(async () =>
        {
            object? value = null; bool failed = false;
            try { value = await action(cancel.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { failed = true; }
            lock (_publication) if (!cancel.IsCancellationRequested && epoch == _epoch) _done.Enqueue((epoch, value, failed));
        });
    }
    private void Retire()
    {
        CancellationTokenSource? active;
        lock (_publication) { _epoch++; active = _active; _active = null; while (_done.TryDequeue(out _)) { } }
        active?.Cancel(); active?.Dispose();
    }
    private void CancelCore()
    { Retire(); _source = null; _draft = null; _receipt = null; _progress = null; _busy = false; _confirmation = ""; _notes = ""; }
    private void Verify()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Community publishing belongs to its UI owner."); ObjectDisposedException.ThrowIf(_disposed, this); }
    private void Touch() => _revision++;
    public void Dispose() { if (_disposed) return; Verify(); CancelCore(); _disposed = true; }
}
