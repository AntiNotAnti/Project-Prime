using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

public enum NativeCommunityTransferAction { Pause, Resume, Cancel, Forget }

/// <summary>A display receipt, with an opaque local queue witness; no path, endpoint or credential.</summary>
public sealed record NativeCommunityTransferReceipt(Guid Id, CommunityTransferKind Kind,
    CommunityTransferState State, string PackageHash, string Title, long Received, long Total,
    DateTimeOffset UpdatedAt)
{
    [JsonIgnore] public object? QueueWitness { get; init; }
}

public interface INativeCommunityTransfersAuthority
{
    Task<NativeCommunityTransferReceipt[]> ReadAsync(CancellationToken token);
    Task<NativeCommunityTransferReceipt[]> ControlAsync(NativeCommunityTransferReceipt expected,
        NativeCommunityTransferAction action, CancellationToken token);
}

public sealed record NativeCommunityTransfersView(long Revision, bool Busy, string Status,
    IReadOnlyList<NativeCommunityTransferReceipt> Rows, int Page, int Total,
    NativeCommunityTransferReceipt? Selected, NativeCommunityTransferAction? Confirmation)
{
    public int Pages => Math.Max(1, (Total + 7) / 8);
}

/// <summary>One owner-thread view over the shared durable queue; closing cancels observation only.</summary>
public sealed class NativeCommunityTransfersController : IDisposable
{
    private readonly INativeCommunityTransfersAuthority _authority;
    private readonly TimeProvider _time;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<(int Epoch, NativeCommunityTransferReceipt[]? Rows, bool Failed)> _done = new();
    private readonly object _publication = new();
    private NativeCommunityTransferReceipt[] _rows = Array.Empty<NativeCommunityTransferReceipt>();
    private NativeCommunityTransferReceipt? _confirmed;
    private NativeCommunityTransferAction? _confirmation;
    private CancellationTokenSource? _active;
    private Guid _selected;
    private int _epoch, _page;
    private long _revision;
    private bool _busy, _disposed, _open;
    private DateTimeOffset _poll;
    private string _status = "TRANSFER HISTORY IS NOT OPEN";

    public NativeCommunityTransfersController(INativeCommunityTransfersAuthority authority, TimeProvider? time = null)
    { _authority = authority ?? throw new ArgumentNullException(nameof(authority)); _time = time ?? TimeProvider.System; }

    public void Open()
    { Verify(); _open = true; Refresh(); }

    public bool Refresh()
    {
        Verify(); if (!_open || _busy || _confirmation.HasValue) return false;
        _status = "READING LOCAL TRANSFER HISTORY";
        Start(_authority.ReadAsync, visibleBusy: true); return true;
    }

    public NativeCommunityTransfersView Tick()
    {
        Verify();
        while (_done.TryDequeue(out var item))
        {
            if (item.Epoch != _epoch || !_open) continue;
            _active?.Dispose(); _active = null; _busy = false;
            if (item.Failed || item.Rows == null)
                _status = "TRANSFER HISTORY COULD NOT BE VERIFIED // REFRESH TO RETRY";
            else
            {
                _rows = item.Rows.Take(32).Where(Valid).GroupBy(x => x.Id).Select(x => x.First())
                    .Select(x => x with { Title = CleanTitle(x.Title), PackageHash = x.PackageHash.ToLowerInvariant() })
                    .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToArray();
                _page = Math.Clamp(_page, 0, Math.Max(0, (_rows.Length - 1) / 8));
                if (!_rows.Skip(_page * 8).Take(8).Any(x => x.Id == _selected))
                    _selected = _rows.Skip(_page * 8).FirstOrDefault()?.Id ?? Guid.Empty;
                _status = _rows.Length == 0 ? "NO TRANSFER HISTORY ON THIS DEVICE" : "LOCAL DURABLE TRANSFERS // CLOSING THIS PAGE KEEPS JOBS RUNNING";
            }
            _poll = _time.GetUtcNow().AddSeconds(1); _revision++;
        }
        if (_open && _active == null && !_confirmation.HasValue && _time.GetUtcNow() >= _poll)
            Start(_authority.ReadAsync, visibleBusy: false);
        return new(_revision, _busy, _status, _rows.Skip(_page * 8).Take(8).ToArray(), _page,
            _rows.Length, Selected(), _confirmation);
    }

    public bool Select(int slot)
    {
        Verify(); if (!_open || _busy || _confirmation.HasValue || slot is < 0 or >= 8) return false;
        var row = _rows.Skip(_page * 8 + slot).FirstOrDefault(); if (row == null) return false;
        _selected = row.Id; _revision++; return true;
    }

    public bool MovePage(int direction)
    {
        Verify(); if (!_open || _busy || _confirmation.HasValue || direction is not (-1 or 1)) return false;
        int next = _page + direction;
        if (next < 0 || next >= Math.Max(1, (_rows.Length + 7) / 8)) return false;
        _page = next; _selected = _rows[_page * 8].Id; _revision++; return true;
    }

    public static bool Allows(NativeCommunityTransferReceipt row, NativeCommunityTransferAction action)
        => action switch
        {
            NativeCommunityTransferAction.Pause => row.State is CommunityTransferState.Queued or CommunityTransferState.Preparing
                or CommunityTransferState.Uploading or CommunityTransferState.Downloading or CommunityTransferState.Verifying or CommunityTransferState.Retryable,
            NativeCommunityTransferAction.Resume => row.State is CommunityTransferState.Paused or CommunityTransferState.Retryable or CommunityTransferState.Failed,
            NativeCommunityTransferAction.Cancel => row.State is not (CommunityTransferState.Completed or CommunityTransferState.Cancelled),
            NativeCommunityTransferAction.Forget => row.State is CommunityTransferState.Completed or CommunityTransferState.Cancelled
                or CommunityTransferState.Failed or CommunityTransferState.Paused or CommunityTransferState.NeedsReview or CommunityTransferState.NeedsAuthorization,
            _ => false
        };

    public bool Control(NativeCommunityTransferAction action)
    {
        Verify(); var row = Selected();
        if (!_open || _busy || _confirmation.HasValue || row == null || !Allows(row, action)) return false;
        if (action is NativeCommunityTransferAction.Cancel or NativeCommunityTransferAction.Forget)
        {
            Retire(); _confirmed = row; _confirmation = action;
            _status = action == NativeCommunityTransferAction.Cancel
                ? "CANCEL THIS TRANSFER? CANCELLED JOBS CANNOT RESUME."
                : "REMOVE THIS RECEIPT? RETAINED UPLOAD BYTES WILL BE DELETED; INSTALLED MAPS AND VERIFIED DOWNLOADS ARE KEPT.";
            _revision++; return true;
        }
        BeginControl(row, action); return true;
    }

    public bool Confirm()
    {
        Verify();
        if (!_open || _busy || _confirmation is not { } action || _confirmed is not { } row || Selected()?.Id != row.Id) return false;
        _confirmation = null; _confirmed = null; BeginControl(row, action); return true;
    }

    public bool Dismiss()
    {
        Verify(); if (!_confirmation.HasValue) return false;
        _confirmation = null; _confirmed = null; _status = "TRANSFER CHANGE DISMISSED"; _poll = DateTimeOffset.MinValue; _revision++; return true;
    }

    private void BeginControl(NativeCommunityTransferReceipt row, NativeCommunityTransferAction action)
    {
        _status = "UPDATING TRANSFER // " + action.ToString().ToUpperInvariant();
        Start(token => _authority.ControlAsync(row, action, token), visibleBusy: true);
    }

    public void Close()
    {
        Verify(); Retire(); _open = _busy = false; _rows = Array.Empty<NativeCommunityTransferReceipt>();
        _selected = Guid.Empty; _confirmation = null; _confirmed = null; _page = 0; _revision++;
    }

    private NativeCommunityTransferReceipt? Selected() => _rows.Skip(_page * 8).Take(8).FirstOrDefault(x => x.Id == _selected);
    private static bool Valid(NativeCommunityTransferReceipt row) => row != null && row.Id != Guid.Empty
        && Enum.IsDefined(row.Kind) && Enum.IsDefined(row.State) && row.PackageHash is { Length: 64 }
        && row.PackageHash.All(Uri.IsHexDigit) && row.Total > 0 && row.Received >= 0 && row.Received <= row.Total
        && (row.State != CommunityTransferState.Completed || row.Received == row.Total) && row.Title != null && row.QueueWitness != null;
    private static string CleanTitle(string title) => new(title.Where(c => !Char.IsControl(c)).Take(96).ToArray());

    private void Start(Func<CancellationToken, Task<NativeCommunityTransferReceipt[]>> operation, bool visibleBusy)
    {
        Retire(); _busy = visibleBusy; var cancel = _active = new(); int epoch = _epoch; _revision++;
        _ = Task.Run(async () =>
        {
            NativeCommunityTransferReceipt[]? rows = null; bool failed = false;
            try { rows = await operation(cancel.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { failed = true; }
            lock (_publication)
                if (!cancel.IsCancellationRequested && epoch == _epoch) _done.Enqueue((epoch, rows, failed));
        });
    }

    private void Retire()
    {
        CancellationTokenSource? active;
        lock (_publication) { _epoch++; active = _active; _active = null; while (_done.TryDequeue(out _)) { } }
        active?.Cancel(); active?.Dispose();
    }
    private void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Transfer history belongs to its UI owner.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose() { if (_disposed) return; Verify(); Close(); _disposed = true; }
}
