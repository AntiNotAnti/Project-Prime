#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

/// <summary>Native library documents. All disk and launch decisions belong to TheatreController.</summary>
public sealed class TheatrePagePresenter : IDisposable
{
    private static readonly RmlUiPageSpec Page = new("theatre", "pages/theatre/library.rml", "theatre_search");
    private static readonly RmlUiPageSpec Confirm = new("theatre-delete", "pages/theatre/confirm-delete.rml", "theatre_cancel_delete");
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly TheatreController _controller;
    private readonly TheatreImageCache _images;
    private readonly ConcurrentQueue<Action> _imageCompletions = new();
    private readonly Dictionary<string, string> _previews = new(StringComparer.Ordinal);
    private CancellationTokenSource? _previewCancellation;
    private RmlUiDocumentToken _document, _confirmation;
    private string? _draftPath, _savedTitle, _savedTags, _savedCollections;
    private string _previewKey = "", _previewError = "";
    private long _previewVersion;
    private bool _disposed;

    public TheatrePagePresenter(RmlUiHost host, RmlUiPageManager pages, TheatreController controller,
        TheatreImageCache? images = null)
        => (_host, _pages, _controller, _images) = (host, pages, controller, images ?? new TheatreImageCache());
    public RmlUiDocumentToken Document => _document;
    public RmlUiDocumentToken ConfirmationDocument => _confirmation;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document) && _pages.Page == _document;

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _document = _pages.OpenPage(Page);
        _host.SetField(_document, "theatre_search", _controller.Snapshot().Search);
        _draftPath = null; _previewKey = ""; Present();
    }
    public bool Present()
    {
        if (!IsOpen) return false;
        _controller.Pump();
        for (int i = 0; i < 16 && _imageCompletions.TryDequeue(out Action? completion); i++) completion();
        TheatreSnapshot state = _controller.Snapshot();
        if (_confirmation != default && !_host.IsAlive(_confirmation))
        {
            _confirmation = default;
            if (state.ConfirmDelete) _controller.Dispatch(TheatreAction.CancelDelete);
            state = _controller.Snapshot();
        }
        _pages.Present(_document, state.Revision, Bindings(state));
        if (state.Selected is { } selected && (_draftPath != selected.Path || _savedTitle != selected.Title
            || _savedTags != selected.Tags || _savedCollections != selected.Collections))
        {
            _draftPath = selected.Path; _savedTitle = selected.Title; _savedTags = selected.Tags; _savedCollections = selected.Collections;
            _host.SetField(_document, "theatre_name", selected.Title);
            _host.SetField(_document, "theatre_tags", selected.Tags);
            _host.SetField(_document, "theatre_collections", selected.Collections);
        }
        else if (state.Selected == null && _draftPath != null)
        {
            _draftPath = _savedTitle = _savedTags = _savedCollections = null;
            foreach (string id in new[] { "theatre_name", "theatre_tags", "theatre_collections" }) _host.SetField(_document, id, "");
        }
        if (state.ConfirmDelete)
        {
            if (_confirmation == default) _confirmation = _pages.OpenModal(Confirm);
            _pages.Present(_confirmation, state.Revision, new[] { Text("theatre_delete_title", "DELETE " + (state.Selected?.Title ?? "REPLAY") + "?"),
                Text("theatre_delete_path", state.DeletePath) });
        }
        else if (_confirmation != default)
        {
            if (!_pages.CloseModal()) return false;
            _confirmation = default;
        }
        PresentPreviews(state);
        return true;
    }
    public bool HandleIntent(in RmlUiIntent intent)
    {
        if (!IsOpen || intent.Kind is not (RmlUiIntentKind.TheatreAction or RmlUiIntentKind.TheatreEntry or RmlUiIntentKind.TheatreThumbnail)) return false;
        bool modal = intent.Kind == RmlUiIntentKind.TheatreAction && intent.Argument is 15 or 16;
        if (intent.Document != (modal ? _confirmation : _document) || !_pages.Accept(intent)) return false;
        if (intent.Kind == RmlUiIntentKind.TheatreEntry) _controller.SelectVisible(intent.Argument);
        else if (intent.Kind == RmlUiIntentKind.TheatreThumbnail) _controller.SelectThumbnail(intent.Argument);
        else if (Enum.IsDefined((TheatreAction)intent.Argument))
        {
            var action = (TheatreAction)intent.Argument;
            string value = action switch
            {
                TheatreAction.Search => _host.ReadField(_document, "theatre_search"),
                TheatreAction.ImportPath => _host.ReadField(_document, "theatre_import_path"),
                TheatreAction.Rename => _host.ReadField(_document, "theatre_name"), _ => ""
            };
            _controller.Dispatch(action, value, _host.ReadField(_document, "theatre_tags"), _host.ReadField(_document, "theatre_collections"));
            if (action == TheatreAction.ClearSearch) _host.SetField(_document, "theatre_search", "");
        }
        Present(); return true;
    }
    public bool Back()
    {
        if (!IsOpen) return false;
        TheatreSnapshot state = _controller.Snapshot();
        if (state.ConfirmDelete) { _controller.Dispatch(TheatreAction.CancelDelete); Present(); return true; }
        if (state.Busy || state.LaunchPending) { _controller.CancelJob(); Present(); return true; }
        return false;
    }
    public bool TryTakeLaunch(out LaunchPlan plan) => _controller.TryTakeLaunch(out plan);
    public void ReportLaunchFailure(string message) { _controller.ReportLaunchFailure(message); Present(); }
    public bool Close()
    {
        _previewCancellation?.Cancel(); _previewVersion++;
        if (_controller.Snapshot().ConfirmDelete) _controller.Dispatch(TheatreAction.CancelDelete);
        _controller.CancelJob();
        if (IsOpen && !_pages.ClosePage()) return false;
        _document = _confirmation = default; return true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (!Close()) throw new InvalidOperationException("The Theatre library could not be closed.");
        _previewCancellation?.Dispose(); _disposed = true;
    }

    private void PresentPreviews(TheatreSnapshot state)
    {
        string key = state.Selected?.Path + "|" + String.Join("|", state.Selected?.PreviewPaths ?? System.Collections.Immutable.ImmutableArray<string>.Empty);
        if (key != _previewKey)
        {
            _previewKey = key; _previewError = ""; _previews.Clear();
            _previewCancellation?.Cancel(); _previewCancellation?.Dispose();
            _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_pages.Lifetime(_document));
            long version = ++_previewVersion;
            CancellationToken cancellation = _previewCancellation.Token;
            foreach (string source in state.Selected?.PreviewPaths ?? System.Collections.Immutable.ImmutableArray<string>.Empty)
                _ = LoadPreview(source, version, cancellation);
        }
        string? selectedSource = state.Selected?.PreviewPaths.ElementAtOrDefault(state.Thumbnail);
        bool available = selectedSource != null && _previews.TryGetValue(selectedSource, out _);
        _host.SetBool(_document, "theatre_preview", available);
        _host.SetBool(_document, "theatre_no_preview", !available);
        _host.SetText(_document, "theatre_no_preview", _previewError.Length > 0 ? _previewError
            : selectedSource == null ? "NO PREVIEW YET" : "LOADING PREVIEW...");
        if (available) _host.SetText(_document, "image:theatre_preview", _previews[selectedSource!]);
        for (int i = 0; i < 3; i++)
        {
            string? source = state.Selected?.PreviewPaths.ElementAtOrDefault(i);
            bool ready = source != null && _previews.TryGetValue(source, out _);
            _host.SetBool(_document, "theatre_thumbnail" + i, source != null);
            _host.SetBool(_document, "theatre_thumbnail_image" + i, ready);
            _host.SetBool(_document, "class:theatre_thumbnail" + i + ":selected", i == state.Thumbnail);
            if (ready) _host.SetText(_document, "image:theatre_thumbnail_image" + i, _previews[source!]);
        }
    }
    private async Task LoadPreview(string source, long version, CancellationToken cancellation)
    {
        try
        {
            string path = await Task.Run(() => _images.Load(source, cancellation), cancellation).ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested) _imageCompletions.Enqueue(() => { if (IsOpen && version == _previewVersion) _previews[source] = path; });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { string error = "PREVIEW UNAVAILABLE: " + ex.Message; if (!cancellation.IsCancellationRequested) _imageCompletions.Enqueue(() => { if (IsOpen && version == _previewVersion) _previewError = error; }); }
    }
    private static IEnumerable<KeyValuePair<string, RmlUiBindingValue>> Bindings(TheatreSnapshot state)
    {
        yield return Text("theatre_summary", state.Summary); yield return Text("theatre_insights", state.Insights);
        yield return Text("theatre_filter_label", FilterLabel(state.Filter));
        yield return Text("theatre_sort_label", state.Sort.ToString().ToUpperInvariant());
        yield return Text("theatre_page_number", $"{state.Page + 1} / {state.PageCount}");
        yield return Text("theatre_empty", state.State == TheatreLibraryState.Loading ? "SCANNING REPLAY LIBRARY..."
            : state.State == TheatreLibraryState.Failed ? "LIBRARY UNAVAILABLE" : state.TotalCount == 0 ? "Record a match to add your first replay." : "No items match this view.");
        yield return Bool("theatre_empty", state.VisibleEntries.IsEmpty);
        for (int i = 0; i < TheatreController.PageSize; i++)
        {
            TheatreEntry? entry = state.VisibleEntries.ElementAtOrDefault(i);
            yield return Bool("theatre_entry" + i, entry != null);
            yield return Bool("disabled:theatre_entry" + i, !state.CanManage);
            yield return Bool("class:theatre_entry" + i + ":selected", entry != null && entry.Path == state.Selected?.Path);
            yield return Text("theatre_entry" + i + "_title", entry == null ? "" : (entry.Favorite ? "★ " : "") + entry.Title);
            yield return Text("theatre_entry" + i + "_detail", entry?.Detail ?? "");
        }
        yield return Text("theatre_selected_title", state.Selected?.Title ?? "SELECT A REPLAY");
        yield return Text("theatre_hero", state.Selected?.Hero ?? "");
        yield return Text("theatre_metadata", state.Selected?.Metadata ?? "");
        yield return Text("theatre_status", state.Status); yield return Bool("theatre_status", state.Status.Length > 0);
        yield return Text("theatre_error", state.Error); yield return Bool("theatre_error", state.Error.Length > 0);
        yield return Text("theatre_launch_problem", state.LaunchProblem); yield return Bool("theatre_launch_problem", state.LaunchProblem.Length > 0);
        yield return Bool("disabled:theatre_watch", !state.CanWatch);
        yield return Text("theatre_favorite", state.Selected?.Favorite == true ? "UNFAVORITE" : "FAVORITE");
        yield return Bool("theatre_cancel_job", state.Busy || state.LaunchPending);
        yield return Bool("theatre_recover", state.Selected?.Interrupted == true);
        foreach (string id in new[] { "favorite", "export", "rename", "organize", "studio" })
            yield return Bool("disabled:theatre_" + id, !state.CanManage || state.Selected == null || state.Selected.Interrupted);
        foreach (string id in new[] { "validate", "delete", "reveal", "recover" })
            yield return Bool("disabled:theatre_" + id, !state.CanManage || state.Selected == null);
        yield return Bool("theatre_reveal", state.DesktopActions); yield return Bool("theatre_studio", state.DesktopActions);
        foreach (string id in new[] { "name", "tags", "collections" })
            yield return Bool("disabled:theatre_" + id, !state.CanManage || state.Selected == null || state.Selected.Interrupted);
        foreach (string id in new[] { "search", "search_submit", "clear_search", "next_filter", "next_sort", "refresh", "import", "import_path", "import_submit" })
            yield return Bool("disabled:theatre_" + id, !state.CanManage);
        yield return Bool("disabled:theatre_previous_page", !state.CanManage || state.Page == 0);
        yield return Bool("disabled:theatre_next_page", !state.CanManage || state.Page + 1 >= state.PageCount);
        yield return Bool("disabled:theatre_favorite_filtered", !state.CanManage || state.FavoriteTargets is <= 0 or > 200);
        yield return Bool("disabled:theatre_validate_filtered", !state.CanManage || state.ValidateTargets is <= 0 or > 50);
        yield return Text("theatre_favorite_filtered", state.FavoriteTargets == 0 ? "FILTERED FAVORITES COMPLETE" : $"FAVORITE FILTERED // {state.FavoriteTargets}");
        yield return Text("theatre_validate_filtered", $"CHECK FILTERED // {state.ValidateTargets}");
        yield return Text("theatre_batch_hint", "BATCH LIMITS // FAVORITES 200 / INTEGRITY 50");
    }
    private static KeyValuePair<string, RmlUiBindingValue> Text(string id, string value) => new(id, RmlUiBindingValue.FromText(value));
    private static KeyValuePair<string, RmlUiBindingValue> Bool(string id, bool value)
        => new(id.Contains(':') ? id : "visible:" + id, RmlUiBindingValue.FromBoolean(value));
    private static string FilterLabel(TheatreFilter filter) => filter switch
    {
        TheatreFilter.All => "ALL ITEMS", TheatreFilter.FullReplays => "FULL REPLAYS", TheatreFilter.Clips => "CLIPS",
        TheatreFilter.Favorites => "FAVORITES", TheatreFilter.RecentSevenDays => "RECENT // 7 DAYS", TheatreFilter.SameMap => "SAME MAP",
        TheatreFilter.SamePlayers => "SAME PLAYERS", TheatreFilter.Annotated => "ANNOTATED", TheatreFilter.Highlights => "HIGHLIGHTS",
        TheatreFilter.Bookmarks => "BOOKMARKS", TheatreFilter.Organized => "ORGANIZED", TheatreFilter.LongSessions => "LONG SESSIONS",
        TheatreFilter.ShortClips => "SHORT CLIPS", TheatreFilter.NeedsRecovery => "NEEDS RECOVERY", _ => "ALL ITEMS"
    };
}
#endif
