using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Launcher;

public readonly record struct NativeCommunityHistoryRequest(Guid MapId, bool Owned, string AnchorHash)
{
    [JsonIgnore] internal object? AccountLease { get; init; }
    [JsonIgnore] internal bool ReconcileAfterWrite { get; init; }
}
public sealed record NativeCommunityRevision(int Number, string Hash, string ParentHash, string Notes,
    DateTimeOffset CreatedAt, bool Listed, bool Draft, bool Deleted, NativeCommunityEntry Package)
{
    public DateTimeOffset? DeleteAfter { get; init; }
}
public enum NativeCommunityRevisionAction { Promote, Unlist, Draft, Delete, Restore, Publish }
public sealed record NativeCommunityRevisionCatalog(Guid MapId, string Name, string CurrentHash, string LatestHash,
    bool CanManage, bool Archived, bool Deleted, NativeCommunityRevision[] Revisions)
{
    [JsonIgnore] internal Guid Account { get; init; }
    [JsonIgnore] internal object? AccountLease { get; init; }
    [JsonIgnore] internal string OwnerId { get; init; } = "";
}
public sealed record NativeCommunityPromotion(Guid MapId, string CurrentHash, string LatestHash, int Number, string Hash)
{
    public NativeCommunityRevisionAction Action { get; init; }
    [JsonIgnore] internal Guid Account { get; init; }
    [JsonIgnore] internal object? AccountLease { get; init; }
    [JsonIgnore] internal string OwnerId { get; init; } = "";
    [JsonIgnore] internal IReadOnlyList<NativeCommunityRevision> ExpectedHistory { get; init; } = Array.Empty<NativeCommunityRevision>();
}
public interface INativeCommunityRevisionsAuthority
{
    Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request, CancellationToken token);
    Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion request, CancellationToken token);
    Task<NativeCommunityRevisionCatalog> ChangeAsync(NativeCommunityPromotion request, CancellationToken token)
        => request.Action == NativeCommunityRevisionAction.Promote ? PromoteAsync(request, token)
            : throw new NotSupportedException("Community revision action is unavailable.");
    bool IsCurrentAccountLease(object? lease);
}
public interface INativeCommunityCreatorAccountLease
{
    bool IsCurrentAccountLease(object? lease);
}

/// <summary>Only copied presentation values cross this snapshot; account UUID/lease stay in the controller.</summary>
public sealed record NativeCommunityRevisionsView(long Revision, bool Busy, bool Connected, string Status,
    string Name, string CurrentHash, string LatestHash, int Page, int Total,
    IReadOnlyList<NativeCommunityRevision> Rows, int SelectedIndex, NativeCommunityRevision? Selected,
    bool CanPromote, bool Confirming)
{
    public bool CanPublish { get; init; }
    public bool CanUnlist { get; init; }
    public bool CanDraft { get; init; }
    public bool CanDelete { get; init; }
    public bool CanRestore { get; init; }
    public NativeCommunityRevisionAction? PendingAction { get; init; }
    public string Confirmation { get; init; } = "";
    public bool Owned { get; init; }
    public bool CanPrevious => Page > 0;
    public bool CanNext => (Page + 1) * 8 < Total;
}

public sealed class NativeCommunityRevisionsController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityRevisionsAuthority _service;
    private readonly ConcurrentQueue<(long Epoch, NativeCommunityRevisionCatalog? Catalog)> _done = new();
    private readonly object _publication = new();
    private CancellationTokenSource? _active;
    private NativeCommunityHistoryRequest _request;
    private NativeCommunityRevisionCatalog? _catalog;
    private NativeCommunityRevision[] _rows = Array.Empty<NativeCommunityRevision>();
    private NativeCommunityPromotion? _confirmation;
    private long _epoch, _revision = 1;
    private int _page, _selected = -1;
    private bool _busy, _disposed, _uncertain;
    private string _status = "SELECT A PROJECT TO LOAD ITS IMMUTABLE HISTORY";

    public NativeCommunityRevisionsController(INativeCommunityRevisionsAuthority service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    public bool Open(NativeCommunityHistoryRequest request)
    {
        Owner();
        if (request.MapId == Guid.Empty || !Hash(request.AnchorHash) || request.Owned && request.AccountLease == null) return false;
        if (!request.ReconcileAfterWrite) _uncertain = false;
        _request = request; _page = 0;
        Start(token => _service.ReadAsync(request, token), "VERIFYING IMMUTABLE REVISION HISTORY");
        return true;
    }

    public bool Refresh()
    {
        Owner();
        if (_request.MapId == Guid.Empty) return false;
        if (_request.Owned && !_service.IsCurrentAccountLease(_request.AccountLease)) { Tick(); return false; }
        if (_uncertain) _request = _request with { ReconcileAfterWrite = true };
        return Open(_request);
    }

    private void Start(Func<CancellationToken, Task<NativeCommunityRevisionCatalog>> operation, string status)
    {
        Retire(); Clear(); _busy = true; _status = status; _revision++;
        long epoch = _epoch; var source = new CancellationTokenSource(); _active = source;
        _ = Task.Run(async () =>
        {
            NativeCommunityRevisionCatalog? result = null;
            try { result = await operation(source.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
            finally
            {
                lock (_publication)
                    if (!source.IsCancellationRequested && epoch == _epoch)
                        _done.Enqueue((epoch, result));
                source.Dispose();
            }
        });
    }

    public NativeCommunityRevisionsView Tick()
    {
        Owner();
        if (_request.Owned && _request.AccountLease != null && !_service.IsCurrentAccountLease(_request.AccountLease))
        {
            if (_busy || _catalog != null || _status != "ACCOUNT CHANGED // CLOSE AND REOPEN MY UPLOADS")
            { Retire(); Clear(); _busy = false; _status = "ACCOUNT CHANGED // CLOSE AND REOPEN MY UPLOADS"; _revision++; }
        }
        while (_done.TryDequeue(out var done))
        {
            if (done.Epoch != _epoch) continue;
            _active = null; _busy = false;
            if (!Valid(done.Catalog, _request))
            {
                Clear(); _status = _uncertain
                    ? "REVISION HISTORY UNAVAILABLE // CHANGE OUTCOME UNVERIFIED; REFRESH HISTORY OR REOPEN THE PROJECT"
                    : "REVISION HISTORY UNAVAILABLE // REFRESH THE PROJECT OR ACCOUNT";
            }
            else
            {
                _catalog = done.Catalog! with { Revisions = done.Catalog!.Revisions.ToArray() };
                if (_request.Owned) _request = _request with { AnchorHash = _catalog.LatestHash, ReconcileAfterWrite = false };
                _uncertain = false;
                _rows = _catalog.Revisions.OrderByDescending(row => row.Number).ToArray();
                _selected = Array.FindIndex(_rows, row => row.Hash.Equals(_catalog.CurrentHash, StringComparison.OrdinalIgnoreCase));
                if (_selected < 0 && _rows.Length > 0) _selected = 0;
                _page = Math.Max(0, _selected / 8);
                _status = _rows.Length == 0 ? "NO IMMUTABLE REVISIONS" : "VERIFIED IMMUTABLE HISTORY // " + _rows.Length + " REVISIONS";
            }
            _revision++;
        }
        NativeCommunityRevision? selected = _selected >= 0 && _selected < _rows.Length ? _rows[_selected] : null;
        return new(_revision, _busy, _catalog != null, _status, _catalog?.Name ?? "REVISION HISTORY",
            _catalog?.CurrentHash ?? "", _catalog?.LatestHash ?? "", _page, _rows.Length,
            Array.AsReadOnly(_rows.Skip(_page * 8).Take(8).ToArray()), _selected < 0 ? -1 : _selected - _page * 8,
            selected, CanChange(NativeCommunityRevisionAction.Promote, selected), _confirmation != null)
        {
            CanPublish = CanChange(NativeCommunityRevisionAction.Publish, selected),
            CanUnlist = CanChange(NativeCommunityRevisionAction.Unlist, selected),
            CanDraft = CanChange(NativeCommunityRevisionAction.Draft, selected),
            CanDelete = CanChange(NativeCommunityRevisionAction.Delete, selected),
            CanRestore = CanChange(NativeCommunityRevisionAction.Restore, selected),
            PendingAction = _confirmation?.Action,
            Owned = _request.Owned && _catalog != null,
            Confirmation = _confirmation is { } pending ? NativeCommunityRevisionPolicy.Confirmation(pending) : ""
        };
    }

    public bool Page(int direction)
    {
        Owner(); if (_busy || _catalog == null || _confirmation != null || direction is not (-1 or 1)) return false;
        int next = _page + direction;
        if (next < 0 || next * 8 >= _rows.Length) return false;
        _page = next; _selected = next * 8; _revision++; return true;
    }
    public bool Select(int index)
    {
        Owner(); int next = _page * 8 + index;
        if (_busy || _catalog == null || _confirmation != null || index is < 0 or > 7 || next >= _rows.Length) return false;
        _selected = next; _revision++; return true;
    }
    public bool ProposePromotion() => Propose(NativeCommunityRevisionAction.Promote);
    public bool Propose(NativeCommunityRevisionAction action)
    {
        Owner(); var view = Tick();
        if (_confirmation != null || !CanChange(action, view.Selected) || _catalog == null || view.Selected == null) return false;
        _confirmation = new(_catalog.MapId, _catalog.CurrentHash, _catalog.LatestHash, view.Selected.Number, view.Selected.Hash)
        { Action = action, Account = _catalog.Account, AccountLease = _catalog.AccountLease, OwnerId = _catalog.OwnerId,
            ExpectedHistory = Array.AsReadOnly(_rows.ToArray()) };
        _revision++; return true;
    }
    public bool Dismiss()
    { Owner(); if (_confirmation == null) return false; _confirmation = null; _revision++; return true; }
    public bool ConfirmPromotion() => ConfirmChange();
    public bool ConfirmChange()
    {
        Owner(); var view = Tick(); var expected = _confirmation;
        if (expected == null || !CanChange(expected.Action, view.Selected) || view.Selected?.Hash != expected.Hash) return false;
        _uncertain = true;
        Start(async token =>
        {
            var result = await _service.ChangeAsync(expected, token).ConfigureAwait(false);
            if (!NativeCommunityRevisionPolicy.VerifiedReadback(expected, result))
                throw new InvalidOperationException("Community did not verify the confirmed revision change.");
            return result;
        }, "VERIFYING IMMUTABLE REVISION CATALOG CHANGE");
        return true;
    }
    public bool TryGetInstallSelection(out NativeCommunityEntry entry)
    {
        Owner(); var view = Tick(); entry = default;
        // The current shared installer is anonymous exact-archive acquisition. Private/unlisted
        // packages retain their service visibility; publishing is an explicit separate action.
        if (view.Busy || !view.Connected || view.Confirming || view.Selected is not { Deleted: false, Draft: false, Listed: true } selected) return false;
        entry = selected.Package; return true;
    }
    private bool CanChange(NativeCommunityRevisionAction action, NativeCommunityRevision? selected) => !_busy && _request.Owned && _catalog is
        { CanManage: true, Archived: false, Deleted: false, Account: var account, AccountLease: not null }
        && account != Guid.Empty && _service.IsCurrentAccountLease(_catalog.AccountLease) && selected != null
        && NativeCommunityRevisionPolicy.Can(action, _catalog, selected);

    private bool Valid(NativeCommunityRevisionCatalog? data, NativeCommunityHistoryRequest expected)
    {
        if (data == null || data.MapId != expected.MapId || String.IsNullOrWhiteSpace(data.Name) || data.Name.Length > 128
            || !Hash(data.LatestHash) || data.CurrentHash != "" && !Hash(data.CurrentHash)
            || data.Revisions == null || data.Revisions.Length > 4096
            || expected.Owned && (data.Account == Guid.Empty || !ReferenceEquals(data.AccountLease, expected.AccountLease)
                || !_service.IsCurrentAccountLease(data.AccountLease))
            || expected.ReconcileAfterWrite && (!data.CanManage || data.Archived || data.Deleted)
            || !expected.Owned && (data.Account != Guid.Empty || data.AccountLease != null || data.CanManage)) return false;
        var numbers = new HashSet<int>(); var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in data.Revisions)
            if (row == null || row.Number < 1 || !numbers.Add(row.Number) || !Hash(row.Hash) || !hashes.Add(row.Hash)
                || row.ParentHash != "" && !Hash(row.ParentHash) || row.Notes == null || row.Notes.Length > 4000
                || row.Package.MapId != data.MapId || row.Package.Revision != row.Number
                || !row.Hash.Equals(row.Package.LatestHash, StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrWhiteSpace(row.Package.Name) || row.Package.Name.Length > 128
                || row.Package.MinPlayers < 1 || row.Package.MaxPlayers < row.Package.MinPlayers || row.Package.MaxPlayers > 8
                || !expected.Owned && (row.Deleted || row.Draft || !row.Listed)) return false;
        return hashes.Contains(expected.AnchorHash) && data.Revisions.Any(row => row.Hash == data.LatestHash && !row.Deleted)
            && (data.CurrentHash.Length == 0 || data.Revisions.Any(row => row.Hash == data.CurrentHash && !row.Deleted && row.Listed && !row.Draft));
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private void Clear() { _catalog = null; _rows = Array.Empty<NativeCommunityRevision>(); _selected = -1; _confirmation = null; }
    private void Retire()
    {
        CancellationTokenSource? active;
        lock (_publication)
        {
            _epoch++; active = _active; _active = null;
            while (_done.TryDequeue(out _)) { }
        }
        // Cancellation callbacks must not execute while the publication gate is held.
        try { active?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public void Cancel() { Owner(); Retire(); Clear(); _request = default; _busy = false; _uncertain = false; _status = "REVISION HISTORY CLOSED"; _revision++; }
    public void Dispose() { if (_disposed) return; Owner(); Cancel(); _disposed = true; while (_done.TryDequeue(out _)) { } }
    private void Owner()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Community history belongs to its UI owner."); ObjectDisposedException.ThrowIf(_disposed, this); }
}
