using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public readonly record struct NativeCommunityEntry(
    Guid MapId, string Name, string Creator, string LatestHash,
    int Revision, string Modes, int MinPlayers, int MaxPlayers);
public readonly record struct NativeCommunityPage(
    NativeCommunityEntry[] Rows, int Total, int PageNumber, int PageSize);

public interface INativeCommunityReader
{
    Task<NativeCommunityPage> BrowseAsync(
        string query, string sort, int page, CancellationToken cancellation);
}

/// <summary>
/// An owner-thread, read-only Community catalog. Background HTTP never edits
/// any RmlUi object, active map, CommunityTransferManager, or gameplay session.
/// The modern current catalog API remains the sole source of map identity.
/// </summary>
public sealed class NativeCommunityDiscoverController : IDisposable
{
    private readonly record struct Completion(long Epoch, NativeCommunityPage? Page, string? Error);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityReader _reader;
    private readonly ConcurrentQueue<Completion> _done = new();
    private CancellationTokenSource? _work;
    private readonly NativeCommunityEntry[] _visible = new NativeCommunityEntry[8];
    private bool _disposed, _busy;
    private string _search = "", _sort = "updated", _status = "REFRESH TO BROWSE MAPS";
    private int _page = 1, _total, _count, _selection = -1;
    private long _epoch, _revision = 1;

    public NativeCommunityDiscoverController(INativeCommunityReader reader)
        => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public sealed record Snapshot(long Revision, bool Busy, string Query, string Sort,
        int Page, int Total, IReadOnlyList<NativeCommunityEntry> Rows,
        int Selected, NativeCommunityEntry? SelectedEntry, string Status)
    {
        public bool CanPrevious => Page > 1;
        public bool CanNext => (long)Page * 8 < Total;
    }

    public Snapshot Current()
    {
        Owner();
        NativeCommunityEntry[] copy = _visible.Take(_count).ToArray();
        return new(_revision, _busy, _search, _sort, _page, _total,
            Array.AsReadOnly(copy), _selection, _selection >= 0 && _selection < _count
                ? copy[_selection] : null, _status);
    }

    public bool Search(string text)
    {
        Owner();
        if (text == null || text.Trim().Length > 128) return false;
        _search = text.Trim();
        _page = 1;
        Refresh();
        return true;
    }

    public bool Sort(int choice)
    {
        Owner();
        if (choice is < 0 or > 2) return false;
        string sort = choice switch { 0 => "name", 1 => "updated", _ => "favorites" };
        if (_sort == sort) return true;
        _sort = sort;
        _page = 1;
        Refresh();
        return true;
    }

    public bool Page(int direction)
    {
        Owner();
        if (direction is not (-1 or 1) || _busy) return false;
        int next = _page + direction;
        if (next < 1 || next > 2000 || (direction > 0 && (long)_page * 8 >= _total))
            return false;
        _page = next;
        Refresh();
        return true;
    }

    public bool Select(int index)
    {
        Owner();
        if (index < 0 || index >= _count) return false;
        _selection = index;
        _revision++;
        return true;
    }

    public void Refresh()
    {
        Owner();
        Cancel();
        _busy = true;
        _status = "CONTACTING COMMUNITY CATALOG";
        _revision++;
        long epoch = _epoch;
        var cts = new CancellationTokenSource();
        _work = cts;
        string search = _search, sort = _sort;
        int page = _page;
        _ = DownloadAsync(epoch, search, sort, page, cts);
    }

    private async Task DownloadAsync(long epoch, string search, string sort,
        int page, CancellationTokenSource cts)
    {
        try
        {
            NativeCommunityPage found = await _reader.BrowseAsync(search, sort, page, cts.Token)
                .ConfigureAwait(false);
            _done.Enqueue(new(epoch, found, null));
        }
        catch (OperationCanceledException)
        {
            _done.Enqueue(new(epoch, null, "CANCELLED"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            _done.Enqueue(new(epoch, null, "COMMUNITY UNAVAILABLE: " + error.Message));
        }
        finally { cts.Dispose(); }
    }

    public Snapshot Tick()
    {
        Owner();
        while (_done.TryDequeue(out var finish))
        {
            if (finish.Epoch != _epoch) continue;
            _busy = false;
            _work = null;
            _count = 0;
            _selection = -1;
            Array.Clear(_visible);
            if (finish.Page is { } result)
            {
                _page = Math.Clamp(result.PageNumber, 1, 2000);
                _total = Math.Max(0, result.Total);
                foreach (var candidate in result.Rows.Take(8))
                {
                    if (candidate.MapId == Guid.Empty || candidate.LatestHash.Length != 64)
                        continue;
                    _visible[_count++] = candidate;
                }
                _selection = _count > 0 ? 0 : -1;
                int first = _count == 0 ? 0 : (_page - 1) * 8 + 1;
                _status = _count == 0 ? "NO PUBLISHED MAPS FOUND"
                    : $"SHOWING {first}–{first + _count - 1} OF {_total} MAPS";
            }
            else _status = finish.Error ?? "COMMUNITY CATALOG UNAVAILABLE";
            _revision++;
        }
        return Current();
    }

    public void Cancel()
    {
        Owner();
        _epoch++;
        try { _work?.Cancel(); }
        catch (ObjectDisposedException) { /* Worker finalized its source. */ }
        _work = null;
        _busy = false;
    }

    private void Owner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Community snapshot changes require the launcher owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner();
        Cancel();
        _disposed = true;
        while (_done.TryDequeue(out _)) { }
    }
}
