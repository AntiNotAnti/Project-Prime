using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Owner-thread library choices; disk completions are queued and retired with this lifetime.</summary>
public sealed class TheatreController : IDisposable
{
    public const int PageSize = 8;
    private readonly ITheatreBackend _backend;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ConcurrentQueue<Action> _completions = new();
    private CancellationTokenSource? _scanCancellation, _jobCancellation;
    private ImmutableArray<TheatreEntry> _entries = ImmutableArray<TheatreEntry>.Empty;
    private ImmutableArray<TheatreEntry> _filtered = ImmutableArray<TheatreEntry>.Empty;
    private TheatreSnapshot? _snapshot;
    private TheatreLibraryState _state = TheatreLibraryState.Loading;
    private TheatreFilter _filter;
    private TheatreSort _sort;
    private string _search = "", _status = "", _error = "", _launchProblem = "";
    private string? _selected, _deletePath;
    private int _page, _thumbnail;
    private long _revision, _scanVersion, _jobVersion, _selectionVersion;
    private bool _disposed, _busy, _recovering, _launchPending, _storagePolicy;
    private LaunchPlan? _pendingLaunch;

    public TheatreController(ITheatreBackend backend, bool manageStorage = true)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _storagePolicy = manageStorage;
        Refresh();
    }

    public void Pump()
    {
        VerifyOwner();
        if (_disposed) return;
        for (int i = 0; i < 64 && _completions.TryDequeue(out Action? completion); i++) completion();
    }

    public TheatreSnapshot Snapshot()
    {
        VerifyOwner();
        string launchProblem = _backend.CanLaunch(out string reason) ? "" : reason;
        if (_launchProblem != launchProblem) { _launchProblem = launchProblem; Touch(); }
        if (_snapshot != null && _snapshot.Revision == _revision) return _snapshot;
        var visible = _filtered.Skip(_page * PageSize).Take(PageSize).ToImmutableArray();
        TheatreEntry? selected = Selected;
        int favorites = _filtered.Count(e => !e.Favorite && !e.Recoverable);
        int validations = _filtered.Count(e => !e.Recoverable);
        long frames = _filtered.Sum(e => (long)e.DurationFrames);
        int clips = _entries.Count(e => e.IsClip), recovery = _entries.Count(e => e.Recoverable);
        string insights = $"ARCHIVE // {_entries.Length - clips} REPLAYS / {clips} CLIPS / {_entries.Count(e => e.Favorite)} FAVORITES / "
            + $"{_entries.Sum(e => e.HighlightCount)} HIGHLIGHTS / {_entries.Sum(e => e.BookmarkCount)} BOOKMARKS"
            + (recovery > 0 ? $" / {recovery} RECOVERY" : "")
            + $"\nCURRENT VIEW // {_filtered.Length} ITEMS / {DurationSummary(frames)}";
        return _snapshot = new()
        {
            Lifetime = _lifetime, Revision = _revision, State = _disposed ? TheatreLibraryState.Closed : _state,
            Entries = _entries, VisibleEntries = visible, Selected = selected,
            Filter = _filter, Sort = _sort, Search = _search, Page = _page,
            PageCount = Math.Max(1, (_filtered.Length + PageSize - 1) / PageSize),
            TotalCount = _entries.Length, FilteredCount = _filtered.Length, Thumbnail = _thumbnail,
            Busy = _busy, Recovering = _recovering, LaunchPending = _launchPending,
            ConfirmDelete = _deletePath != null, DeletePath = _deletePath ?? "",
            Summary = _state == TheatreLibraryState.Loading ? "SCANNING LIBRARY..."
                : _entries.IsEmpty ? "EMPTY LIBRARY" : $"{_filtered.Length} OF {_entries.Length} ITEMS / {_filtered.Count(e => e.Favorite)} FAVORITES",
            Insights = insights, Status = _status, Error = _error, LaunchProblem = _launchProblem,
            DesktopActions = _backend.DesktopActions, FavoriteTargets = favorites, ValidateTargets = validations
        };
    }

    public void Refresh()
    {
        Verify();
        if (_busy || _launchPending || _deletePath != null) return;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        CancellationToken cancellation = _scanCancellation.Token;
        long version = ++_scanVersion;
        bool policy = _storagePolicy;
        _storagePolicy = false;
        _state = TheatreLibraryState.Loading;
        Touch();
        _ = FinishScan(version, policy, cancellation);
    }

    private async Task FinishScan(long version, bool policy, CancellationToken cancellation)
    {
        try
        {
            var entries = await _backend.Scan(policy, cancellation).ConfigureAwait(false);
            Post(() =>
            {
                if (version != _scanVersion || cancellation.IsCancellationRequested) return;
                _entries = entries;
                _state = entries.IsEmpty ? TheatreLibraryState.Empty : TheatreLibraryState.Ready;
                Refilter();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            string message = "Could not load replay library: " + ex.Message;
            Post(() =>
            {
                if (version != _scanVersion || cancellation.IsCancellationRequested) return;
                _state = TheatreLibraryState.Failed;
                Fail(message);
            });
        }
    }

    public bool Dispatch(TheatreAction action, string value = "", string tags = "", string collections = "")
    {
        Verify();
        if (!Enum.IsDefined(action)) return false;
        if (_deletePath != null && action is not (TheatreAction.ConfirmDelete or TheatreAction.CancelDelete)) return false;
        if ((_busy || _launchPending) && action is not (TheatreAction.CancelJob or TheatreAction.CancelLaunch)) return false;
        switch (action)
        {
            case TheatreAction.Search: _search = value.Trim(); _page = 0; Refilter(); return true;
            case TheatreAction.ClearSearch: _search = ""; _page = 0; Refilter(); return true;
            case TheatreAction.NextFilter: _filter = (TheatreFilter)(((int)_filter + 1) % 14); _page = 0; Refilter(); return true;
            case TheatreAction.NextSort: _sort = (TheatreSort)(((int)_sort + 1) % 4); _page = 0; Refilter(); return true;
            case TheatreAction.PreviousPage: if (_page > 0) { _page--; Touch(); } return true;
            case TheatreAction.NextPage: if ((_page + 1) * PageSize < _filtered.Length) { _page++; Touch(); } return true;
            case TheatreAction.Refresh: _error = ""; Refresh(); return true;
            case TheatreAction.Watch: if (_selected != null) PrepareLaunch(_selected); return true;
            case TheatreAction.ImportPath:
                if (String.IsNullOrWhiteSpace(value)) return Fail("Choose a replay file to import.");
                PrepareLaunch(value.Trim()); return true;
            case TheatreAction.Import: StartImport(); return true;
            case TheatreAction.CancelJob:
            case TheatreAction.CancelLaunch: CancelJob(); return true;
            case TheatreAction.Delete:
                if (Selected == null) return false;
                _deletePath = _selected; _error = ""; Touch(); return true;
            case TheatreAction.CancelDelete: _deletePath = null; Touch(); return true;
            case TheatreAction.ConfirmDelete:
                if (_deletePath == null || Selected == null || !SamePath(_deletePath, _selected)) return false;
                _deletePath = null; StartOperation(TheatreOperation.Delete); return true;
            case TheatreAction.Studio:
                if (Selected == null) return Fail("Select a replay or clip first.");
                if (Selected.Interrupted) return Fail("Recover the interrupted recording before opening it in Studio.");
                if (!_backend.OpenStudio(Selected.Path, out string? error)) return Fail(error ?? "Studio could not start.");
                _status = "OPENING PROJECT PRIME STUDIO"; _error = ""; Touch(); return true;
            case TheatreAction.Reveal:
                if (Selected == null) return false;
                try { _backend.Reveal(Selected.Path); }
                catch (Exception ex) { return Fail(ex.Message); }
                return true;
            default:
                TheatreOperation? operation = action switch
                {
                    TheatreAction.Favorite => TheatreOperation.Favorite,
                    TheatreAction.Validate => TheatreOperation.Validate,
                    TheatreAction.Recover => TheatreOperation.Recover,
                    TheatreAction.Export => TheatreOperation.Export,
                    TheatreAction.Rename => TheatreOperation.Rename,
                    TheatreAction.Organize => TheatreOperation.Organize,
                    TheatreAction.FavoriteFiltered => TheatreOperation.FavoriteFiltered,
                    TheatreAction.ValidateFiltered => TheatreOperation.ValidateFiltered,
                    _ => null
                };
                if (operation.HasValue) { StartOperation(operation.Value, value, tags, collections); return true; }
                return false;
        }
    }

    public bool SelectVisible(int index)
    {
        Verify();
        if (_busy || _launchPending || _deletePath != null || index is < 0 or >= PageSize) return false;
        int at = _page * PageSize + index;
        if (at >= _filtered.Length) return false;
        Select(_filtered[at].Path);
        return true;
    }

    public bool SelectThumbnail(int index)
    {
        Verify();
        if (Selected == null || index < 0 || index >= Math.Min(3, Selected.PreviewPaths.Length)) return false;
        _thumbnail = index; Touch(); return true;
    }

    private void StartOperation(TheatreOperation operation, string name = "", string tags = "", string collections = "")
    {
        bool batch = operation is TheatreOperation.FavoriteFiltered or TheatreOperation.ValidateFiltered;
        var targets = batch ? _filtered.Where(e => !e.Recoverable
            && (operation != TheatreOperation.FavoriteFiltered || !e.Favorite)).ToImmutableArray()
            : Selected == null ? ImmutableArray<TheatreEntry>.Empty : ImmutableArray.Create(Selected);
        if (targets.IsEmpty) return;
        int limit = operation == TheatreOperation.FavoriteFiltered ? 200 : operation == TheatreOperation.ValidateFiltered ? 50 : 1;
        if (targets.Length > limit)
        {
            Fail(operation == TheatreOperation.FavoriteFiltered
                ? "NARROW FILTER TO 200 OR FEWER ITEMS FOR BATCH FAVORITE"
                : "NARROW FILTER TO 50 OR FEWER ITEMS FOR BATCH INTEGRITY");
            return;
        }
        if (!batch && targets[0].Interrupted && operation is not (TheatreOperation.Recover or TheatreOperation.Delete or TheatreOperation.Validate))
        { Fail("Recover the interrupted recording before managing it."); return; }
        if (operation == TheatreOperation.Recover && !targets[0].Interrupted) return;
        CancellationToken cancellation = BeginJob(operation == TheatreOperation.Recover ? "RECOVERING..." : operation.ToString().ToUpperInvariant() + "...");
        long version = _jobVersion;
        _recovering = operation == TheatreOperation.Recover;
        _ = FinishOperation(operation, targets, name, tags, collections, version, cancellation);
    }

    private async Task FinishOperation(TheatreOperation operation, ImmutableArray<TheatreEntry> targets,
        string name, string tags, string collections, long version, CancellationToken cancellation)
    {
        try
        {
            var result = await _backend.Execute(operation, targets, name, tags, collections, cancellation).ConfigureAwait(false);
            Post(() =>
            {
                if (version != _jobVersion || cancellation.IsCancellationRequested) return;
                _busy = _recovering = false;
                _status = result.Status; _error = "";
                if (result.Selection != null) _selected = result.Selection;
                else if (operation == TheatreOperation.Delete) _selected = null;
                Touch();
                if (result.Reload) Refresh();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            string prefix = operation switch
            {
                TheatreOperation.Validate => "Integrity check failed: ", TheatreOperation.Recover => "Recovery failed: ",
                TheatreOperation.Export => "Export failed: ", TheatreOperation.FavoriteFiltered => "Batch favorite failed: ",
                TheatreOperation.ValidateFiltered => "Batch integrity failed: ", _ => ""
            };
            Post(() =>
            {
                if (version != _jobVersion || cancellation.IsCancellationRequested) return;
                _busy = _recovering = false; Fail(prefix + ex.Message);
            });
        }
    }

    private void StartImport()
    {
        CancellationToken cancellation = BeginJob("CHOOSING REPLAY...");
        long version = _jobVersion;
        _ = FinishImport(version, cancellation);
    }

    private async Task FinishImport(long version, CancellationToken cancellation)
    {
        try
        {
            string? path = await _backend.PickImport(cancellation).ConfigureAwait(false);
            Post(() =>
            {
                if (version != _jobVersion || cancellation.IsCancellationRequested) return;
                _busy = false; Touch();
                if (path != null) PrepareLaunch(path); else _status = "IMPORT CANCELLED";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Post(() => { if (version == _jobVersion && !cancellation.IsCancellationRequested) { _busy = false; Fail("Import failed: " + ex.Message); } });
        }
    }

    private void PrepareLaunch(string path)
    {
        if (!_backend.CanLaunch(out string reason)) { Fail(reason); return; }
        if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
        { Fail("Recover the interrupted recording before watching it."); return; }
        CancellationToken cancellation = BeginJob("CHECKING REPLAY...");
        long version = _jobVersion, selection = _selectionVersion;
        _launchPending = true;
        _ = FinishLaunch(path, version, selection, cancellation);
    }

    private async Task FinishLaunch(string path, long version, long selection, CancellationToken cancellation)
    {
        try
        {
            var result = await _backend.PreparePlayback(path, cancellation).ConfigureAwait(false);
            Post(() =>
            {
                if (version != _jobVersion || selection != _selectionVersion || cancellation.IsCancellationRequested) return;
                _busy = false;
                if (result.Plan is not { } plan) { _launchPending = false; Fail(result.Error); return; }
                if (!_backend.CanLaunch(out string reason)) { _launchPending = false; Fail(reason); return; }
                _pendingLaunch = plan; _status = "OPENING REPLAY"; Touch();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Post(() =>
            {
                if (version != _jobVersion || cancellation.IsCancellationRequested) return;
                _busy = _launchPending = false; Fail("Could not open replay: " + ex.Message);
            });
        }
    }

    private CancellationToken BeginJob(string status)
    {
        _jobCancellation?.Cancel(); _jobCancellation?.Dispose();
        _jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _jobVersion++; _busy = true; _status = status; _error = ""; Touch();
        return _jobCancellation.Token;
    }

    public void CancelJob()
    {
        Verify();
        _jobCancellation?.Cancel(); _jobVersion++;
        _pendingLaunch = null; _launchPending = false; _busy = false;
        _status = _recovering ? "RECOVERY CANCELLED" : "OPERATION CANCELLED";
        _recovering = false; Touch();
    }

    public bool TryTakeLaunch(out LaunchPlan plan)
    {
        VerifyOwner();
        plan = default;
        if (_disposed || _pendingLaunch is not { } ready) return false;
        _pendingLaunch = null; plan = ready; return true;
    }

    public void ReportLaunchFailure(string message)
    {
        Verify();
        _pendingLaunch = null; _launchPending = _busy = false; Fail(message);
    }

    /// <summary>Returns to the same archive selection after the engine closes playback.</summary>
    public void ResumeAfterPlayback()
    {
        Verify();
        _jobCancellation?.Cancel(); _jobVersion++;
        _pendingLaunch = null; _launchPending = _busy = _recovering = false;
        _deletePath = null; _status = _error = "";
        Refresh();
    }

    private void Refilter()
    {
        TheatreEntry? anchor = Selected;
        IEnumerable<TheatreEntry> items = _entries;
        if (_search.Length > 0) items = items.Where(e => e.SearchText.Contains(_search, StringComparison.OrdinalIgnoreCase));
        DateTime recent = DateTime.Now.AddDays(-7);
        items = _filter switch
        {
            TheatreFilter.FullReplays => items.Where(e => !e.IsClip), TheatreFilter.Clips => items.Where(e => e.IsClip),
            TheatreFilter.Favorites => items.Where(e => e.Favorite), TheatreFilter.RecentSevenDays => items.Where(e => e.Recorded >= recent),
            TheatreFilter.SameMap => String.IsNullOrEmpty(anchor?.Room) ? items : items.Where(e => String.Equals(e.Room, anchor.Room, StringComparison.OrdinalIgnoreCase)),
            TheatreFilter.SamePlayers => String.IsNullOrEmpty(anchor?.Players) ? items : items.Where(e => SharesPlayer(e.Players, anchor.Players)),
            TheatreFilter.Annotated => items.Where(e => e.Annotated), TheatreFilter.Highlights => items.Where(e => e.HighlightCount > 0),
            TheatreFilter.Bookmarks => items.Where(e => e.BookmarkCount > 0), TheatreFilter.Organized => items.Where(e => e.Organized),
            TheatreFilter.LongSessions => items.Where(e => e.DurationFrames >= 10 * 60 * 60),
            TheatreFilter.ShortClips => items.Where(e => e.IsClip && e.DurationFrames < 60 * 60),
            TheatreFilter.NeedsRecovery => items.Where(e => e.Recoverable), _ => items
        };
        _filtered = (_sort switch
        {
            TheatreSort.Oldest => items.OrderBy(e => e.Recorded), TheatreSort.Name => items.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
            TheatreSort.Longest => items.OrderByDescending(e => e.DurationFrames), _ => items.OrderByDescending(e => e.Recorded)
        }).ToImmutableArray();
        _page = Math.Clamp(_page, 0, Math.Max(0, (_filtered.Length - 1) / PageSize));
        string? selected = _filtered.Any(e => SamePath(e.Path, _selected)) ? _selected : _filtered.FirstOrDefault()?.Path;
        Select(selected);
        if (selected != null)
            for (int i = 0; i < _filtered.Length; i++)
                if (SamePath(_filtered[i].Path, selected)) { _page = i / PageSize; break; }
        Touch();
    }

    private void Select(string? path)
    {
        if (!SamePath(_selected, path)) { _selectionVersion++; _thumbnail = 0; }
        _selected = path; _deletePath = null; Touch();
    }
    private TheatreEntry? Selected => _entries.FirstOrDefault(e => SamePath(e.Path, _selected));
    private static bool SamePath(string? left, string? right) => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool SharesPlayer(string left, string right) => !String.IsNullOrWhiteSpace(left)
        && right.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(name => left.Contains(name, StringComparison.OrdinalIgnoreCase));
    private static string DurationSummary(long frames)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, frames) / 60d);
        return duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}H {duration.Minutes:00}M"
            : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}M {duration.Seconds:00}S" : $"{duration.Seconds}S";
    }
    private bool Fail(string message) { _error = message; _status = ""; Touch(); return false; }
    private void Touch() => _revision++;
    private void Post(Action completion) { if (!_lifetimeCancellation.IsCancellationRequested) _completions.Enqueue(() => { if (!_disposed) completion(); }); }
    private void Verify() { VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private void VerifyOwner() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Theatre choices belong to the engine thread."); }

    public void Dispose()
    {
        VerifyOwner();
        if (_disposed) return;
        _disposed = true; _pendingLaunch = null; _busy = _recovering = _launchPending = false;
        _scanCancellation?.Cancel(); _jobCancellation?.Cancel(); _lifetimeCancellation.Cancel();
        _scanCancellation?.Dispose(); _jobCancellation?.Dispose();
        Touch();
    }
}
