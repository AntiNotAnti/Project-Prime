using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Launcher;

public readonly record struct NativeCommunityEntry(
    Guid MapId, string Name, string Creator, string LatestHash,
    int Revision, string Modes, int MinPlayers, int MaxPlayers)
{
    public string Description { get; init; } = "";
    public string Version { get; init; } = "";
    public string RoomKey { get; init; } = "";
    public bool Installed { get; init; }
    public bool Favorited { get; init; }
    public int FavoriteCount { get; init; }
    public int MinimumProtocol { get; init; }
    public long ArchiveBytes { get; init; }
}
public readonly record struct NativeCommunityPage(
    NativeCommunityEntry[] Rows, int Total, int PageNumber, int PageSize)
{
    // Opaque account/configuration witness; never a credential or DOM value.
    [JsonIgnore]
    public object? Witness { get; init; }
}
public enum NativeCommunityCatalog { Discover, Favorites, Installed }

public interface INativeCommunityPreviewSource
{
    Task<byte[]?> ReadPreviewAsync(string exactHash, CancellationToken cancellation);
}

public interface INativeCommunityLibraryReader : INativeCommunityReader
{
    Task<NativeCommunityPage> BrowseLibraryAsync(NativeCommunityCatalog catalog,
        string query, string sort, int page, CancellationToken cancellation);
    bool IsCurrentWitness(object? witness);
    Task<object> SetFavoriteAsync(NativeCommunityEntry selected, bool favorite,
        object? expectedWitness, CancellationToken cancellation);
}

public interface INativeCommunityReader
{
    Task<NativeCommunityPage> BrowseAsync(
        string query, string sort, int page, CancellationToken cancellation);
}

/// <summary>
/// An owner-thread Community catalog and account-fenced favorite projection. Background HTTP never edits
/// any RmlUi object, active map, CommunityTransferManager, or gameplay session.
/// The modern current catalog API remains the sole source of map identity.
/// </summary>
public sealed class NativeCommunityDiscoverController : IDisposable
{
    private readonly record struct Completion(long Epoch, NativeCommunityPage? Page, string? Error);
    private readonly record struct FavoriteCompletion(long Epoch, object? Witness, bool Success);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityReader _reader;
    private readonly ConcurrentQueue<Completion> _done = new();
    private readonly ConcurrentQueue<FavoriteCompletion> _favorites = new();
    private NativeCommunityCatalog _catalog;
    private object? _witness;
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
        public NativeCommunityCatalog Catalog { get; init; }
        [JsonIgnore]
        public object? Witness { get; init; }
        public bool CanPrevious => Page > 1;
        public bool CanNext => (long)Page * 8 < Total;
    }

    public Snapshot Current()
    {
        Owner();
        NativeCommunityEntry[] copy = _visible.Take(_count).ToArray();
        return new(_revision, _busy, _search, _sort, _page, _total,
            Array.AsReadOnly(copy), _selection, _selection >= 0 && _selection < _count
                ? copy[_selection] : null, _status) { Catalog = _catalog, Witness = _witness };
    }

    public bool Catalog(NativeCommunityCatalog catalog)
    {
        Owner();
        if (!Enum.IsDefined(catalog) || catalog != NativeCommunityCatalog.Discover
            && _reader is not INativeCommunityLibraryReader) return false;
        if (_catalog == catalog) return true;
        _catalog = catalog; _page = 1; _witness = null;
        _count = 0; _selection = -1; _total = 0; Array.Clear(_visible);
        Refresh();
        return true;
    }

    public bool SetFavorite(bool favorite)
    {
        Owner();
        if (_busy || _catalog == NativeCommunityCatalog.Installed
            || _reader is not INativeCommunityLibraryReader library
            || Current().SelectedEntry is not { } selected
            || !NativeCommunityInstallController.IsVerifiedSelection(selected)
            || _witness != null && !library.IsCurrentWitness(_witness)) return false;
        Cancel(); _busy = true; _revision++;
        _status = "UPDATING FAVORITE // WAITING FOR ACCOUNT SERVICE";
        long epoch = _epoch; object? witness = _witness;
        var cancel = _work = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                object result = await library.SetFavoriteAsync(selected, favorite, witness, cancel.Token).ConfigureAwait(false);
                if (!cancel.IsCancellationRequested) _favorites.Enqueue(new(epoch, result, true));
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { if (!cancel.IsCancellationRequested) _favorites.Enqueue(new(epoch, null, false)); }
            finally { cancel.Dispose(); }
        });
        return true;
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
        if (choice is < 0 or > 2 || _catalog == NativeCommunityCatalog.Installed && choice == 2) return false;
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
        _ = DownloadAsync(epoch, search, sort, page, _catalog, cts);
    }

    private async Task DownloadAsync(long epoch, string search, string sort,
        int page, NativeCommunityCatalog catalog, CancellationTokenSource cts)
    {
        try
        {
            NativeCommunityPage found = _reader is INativeCommunityLibraryReader library
                ? await library.BrowseLibraryAsync(catalog, search, sort, page, cts.Token).ConfigureAwait(false)
                : await _reader.BrowseAsync(search, sort, page, cts.Token).ConfigureAwait(false);
            _done.Enqueue(new(epoch, found, null));
        }
        catch (OperationCanceledException)
        {
            _done.Enqueue(new(epoch, null, "CANCELLED"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // Never project untrusted service/HTTP error text into native RmlUi.
            _done.Enqueue(new(epoch, null, "COMMUNITY UNAVAILABLE // CHECK CONNECTION OR SERVICE"));
        }
        finally { cts.Dispose(); }
    }

    public Snapshot Tick()
    {
        Owner();
        if (_witness != null && _reader is INativeCommunityLibraryReader current
            && !current.IsCurrentWitness(_witness))
        {
            Cancel(); _witness = null; _count = 0; _selection = -1; _total = 0;
            Array.Clear(_visible); _status = "ACCOUNT OR GAME FILES CHANGED // REFRESH TO VERIFY"; _revision++;
        }
        while (_favorites.TryDequeue(out var favorite))
        {
            if (favorite.Epoch != _epoch) continue;
            _busy = false; _work = null;
            if (favorite.Success && _reader is INativeCommunityLibraryReader library
                && favorite.Witness != null && library.IsCurrentWitness(favorite.Witness))
                Refresh(); // Read back the service; never optimistically edit favorite state/counts.
            else { _status = "FAVORITE UPDATE UNAVAILABLE // VERIFY ACCOUNT AND RETRY"; _revision++; }
        }
        while (_done.TryDequeue(out var finish))
        {
            if (finish.Epoch != _epoch) continue;
            _busy = false;
            _work = null;
            _count = 0;
            _selection = -1;
            Array.Clear(_visible);
            _witness = null;
            if (finish.Page is { } result && (_reader is not INativeCommunityLibraryReader library
                || (_catalog == NativeCommunityCatalog.Discover || result.Witness != null)
                    && library.IsCurrentWitness(result.Witness)))
            {
                _witness = result.Witness;
                _page = Math.Clamp(result.PageNumber, 1, 2000);
                _total = Math.Max(0, result.Total);
                foreach (var candidate in result.Rows.Take(8))
                {
                    if (!NativeCommunityInstallController.IsVerifiedSelection(candidate))
                        continue;
                    _visible[_count++] = candidate;
                }
                _selection = _count > 0 ? 0 : -1;
                int first = _count == 0 ? 0 : (_page - 1) * 8 + 1;
                _status = _count == 0 ? _catalog switch
                    { NativeCommunityCatalog.Installed => "NO INSTALLED MAPS FOUND",
                        NativeCommunityCatalog.Favorites => "NO FAVORITES FOUND", _ => "NO PUBLISHED MAPS FOUND" }
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
        while (_favorites.TryDequeue(out _)) { }
    }
}
