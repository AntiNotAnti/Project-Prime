using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public enum NativeCreatorAction { Archive, Restore }
public sealed record NativeCreatorEntry(Guid MapId, string Name, string LatestHash,
    string CurrentHash, int RevisionCount, bool Archived, bool Deleted, bool CanManage);
public sealed record NativeCreatorPage(NativeCreatorEntry[] Items, int Total, int PageNumber);

public interface INativeCommunityCreatorAuthority
{
    Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token);
    Task<NativeCreatorEntry?> ReadOwnedAsync(Guid mapId, CancellationToken token);
    Task<bool> ExecuteAsync(NativeCreatorAction action, Guid mapId, CancellationToken token);
}

public sealed record NativeCreatorView(long Revision, bool Busy, bool Connected,
    string Status, int Page, int Total, NativeCreatorEntry[] Rows,
    int SelectedIndex, NativeCreatorEntry? Selected, string Confirmation)
{
    public bool CanPrevious => Page > 1;
    public bool CanNext => (long)Page * 8 < Total;
}

/// <summary>
/// Authenticated owned-project projection; all destructive actions require
/// a second click and an exact fresh server preflight. No optimistic lifecycle
/// changes, no token or service error text in native UI.
/// </summary>
public sealed class NativeCommunityCreatorController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityCreatorAuthority _service;
    private readonly ConcurrentQueue<(long Epoch, NativeCreatorPage? Page, bool Failed)> _finished = new();
    private CancellationTokenSource? _active;
    private long _epoch, _revision = 1;
    private bool _busy, _disposed, _connected;
    private string _status = "OPEN MY UPLOADS TO CONNECT YOUR HUNTER LICENSE";
    private NativeCreatorEntry[] _rows = Array.Empty<NativeCreatorEntry>();
    private int _page = 1, _total, _selected = -1;
    private (NativeCreatorAction Action, NativeCreatorEntry Expected, long Epoch)? _confirm;

    public NativeCommunityCreatorController(INativeCommunityCreatorAuthority service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    private void Owner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeCommunityCreatorController));
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Creator changes belong to the UI owner thread.");
    }
    private void Retire()
    {
        _epoch++;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        _active = null;
    }

    private void Begin(Func<CancellationToken, Task<NativeCreatorPage>> action, string status)
    {
        Retire();
        long epoch = _epoch;
        var cancellation = new CancellationTokenSource();
        _active = cancellation;
        _busy = true;
        _status = status;
        _revision++;
        _ = Task.Run(async () =>
        {
            try
            {
                NativeCreatorPage page = await action(cancellation.Token).ConfigureAwait(false);
                if (!cancellation.IsCancellationRequested)
                    _finished.Enqueue((epoch, page, false));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                if (!cancellation.IsCancellationRequested)
                    _finished.Enqueue((epoch, null, true));
            }
            finally { cancellation.Dispose(); }
        });
    }

    public void Refresh()
    {
        Owner();
        _confirm = null;
        _connected = false;
        _rows = Array.Empty<NativeCreatorEntry>();
        _selected = -1;
        _total = 0;
        int page = _page;
        Begin(token => _service.BrowseMineAsync(page, token), "VERIFYING OWNED COMMUNITY PROJECTS");
    }

    public bool MovePage(int direction)
    {
        Owner();
        if (_busy || direction is not (-1 or 1)) return false;
        int next = _page + direction;
        if (next < 1 || next > 2000 || (direction == 1 && (long)_page * 8 >= _total))
            return false;
        _page = next;
        Refresh();
        return true;
    }

    public bool Select(int index)
    {
        Owner();
        if (_busy || index < 0 || index >= _rows.Length) return false;
        _selected = index;
        _confirm = null;
        _revision++;
        return true;
    }

    public bool Can(NativeCreatorAction action)
    {
        Owner();
        if (_busy || !_connected || _selected < 0 || _selected >= _rows.Length)
            return false;
        NativeCreatorEntry value = _rows[_selected];
        return value.CanManage && (action switch
        {
            NativeCreatorAction.Archive => !value.Archived && !value.Deleted,
            NativeCreatorAction.Restore => value.Archived || value.Deleted,
            _ => false
        });
    }

    public bool Propose(NativeCreatorAction action)
    {
        Owner();
        if (!Can(action)) return false;
        _confirm = (action, _rows[_selected], _epoch);
        _revision++;
        return true;
    }

    public bool Dismiss()
    {
        Owner();
        if (_confirm == null) return false;
        _confirm = null;
        _revision++;
        return true;
    }

    public bool Confirm()
    {
        Owner();
        if (_confirm is not { } pending || pending.Epoch != _epoch
            || !Can(pending.Action) || _rows[_selected] != pending.Expected)
        {
            _confirm = null;
            _revision++;
            return false;
        }
        _confirm = null;
        NativeCreatorEntry expected = pending.Expected;
        NativeCreatorAction kind = pending.Action;
        int page = _page;
        // Retire private rows before a network mutation so a changed auth
        // session cannot show another account's projects while the request runs.
        _rows = Array.Empty<NativeCreatorEntry>();
        _selected = -1;
        _connected = false;
        Begin(async token =>
        {
            NativeCreatorEntry? current = await _service.ReadOwnedAsync(expected.MapId, token)
                .ConfigureAwait(false);
            if (current == null || current != expected || !current.CanManage)
                throw new InvalidOperationException("Owned project changed after confirmation.");
            if (!await _service.ExecuteAsync(kind, expected.MapId, token).ConfigureAwait(false))
                throw new InvalidOperationException("Creator lifecycle action rejected.");
            // Only a fresh authenticated read can publish changed archive state.
            return await _service.BrowseMineAsync(page, token).ConfigureAwait(false);
        }, "VERIFYING CREATOR LIFECYCLE CHANGE");
        return true;
    }

    public NativeCreatorView Tick()
    {
        Owner();
        while (_finished.TryDequeue(out var done))
        {
            if (done.Epoch != _epoch) continue;
            _busy = false;
            _active = null;
            NativeCreatorPage? result = done.Page;
            if (done.Failed || result == null || result.Items == null || result.PageNumber < 1)
            {
                _connected = false;
                _rows = Array.Empty<NativeCreatorEntry>();
                _total = 0;
                _selected = -1;
                _confirm = null;
                _status = "MY UPLOADS UNAVAILABLE // CHECK ACCOUNT OR COMMUNITY SERVICE";
            }
            else
            {
                _connected = true;
                _page = Math.Clamp(result.PageNumber, 1, 2000);
                _total = Math.Max(0, result.Total);
                _rows = result.Items.Where(x => x != null && x.MapId != Guid.Empty
                    && x.LatestHash is { Length: 64 }
                    && x.LatestHash.All(Uri.IsHexDigit)
                    && !String.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= 128)
                    .DistinctBy(x => x.MapId).Take(8).ToArray();
                _selected = _rows.Length > 0 ? 0 : -1;
                _status = _rows.Length == 0 ? "NO OWNED COMMUNITY MAPS"
                    : "SERVER-VERIFIED OWNED MAPS // " + _total + " TOTAL";
            }
            _revision++;
        }
        NativeCreatorEntry? selected = _selected >= 0 && _selected < _rows.Length
            ? _rows[_selected] : null;
        string confirmation = _confirm is { } current
            ? current.Action == NativeCreatorAction.Archive
                ? "ARCHIVE THIS OWNED MAP? CONFIRM OR CANCEL"
                : "RESTORE THIS OWNED MAP? CONFIRM OR CANCEL"
            : "";
        return new(_revision, _busy, _connected, _status, _page, _total,
            _rows.ToArray(), _selected, selected, confirmation);
    }

    public void Cancel()
    {
        Owner();
        Retire();
        _confirm = null;
        _connected = false;
        _rows = Array.Empty<NativeCreatorEntry>();
        _total = 0;
        _selected = -1;
        _busy = false;
        _status = "MY UPLOADS CLOSED";
        _revision++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner();
        Cancel();
        _disposed = true;
        while (_finished.TryDequeue(out _)) { }
    }
}
