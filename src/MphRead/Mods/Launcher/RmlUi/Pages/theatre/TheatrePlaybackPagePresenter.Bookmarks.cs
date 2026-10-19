#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private readonly ITheatreBookmarkBackend? _bookmarkBackend;
    private TheatreBookmarkController? _bookmarks;
    private RmlUiDocumentToken _bookmarkModal, _bookmarkConfirmation;
    private string _bookmarkProblem = "";

    /// <summary>Dismiss annotation confirmation/panel before the shell leaves playback.</summary>
    public bool Back()
    {
        if (_disposed) return false;
        if (BackStudioTools()) return true;
        if (BackSpatial()) return true;
        if (BackTimeline()) return true;
        if (BackCameraTools()) return true;
        if (BackEditor()) return true;
        if (BackExports()) return true;
        if (BackCamera()) return true;
        if (BackRanges()) return true;
        if (_bookmarkConfirmation != default)
        {
            if (!_pages.CloseModal()) return true;
            _bookmarkConfirmation = default;
            _bookmarks?.CancelRemove();
            PresentBookmarks(); return true;
        }
        if (_bookmarkModal == default || WorkspaceMode&&!_workspaceClosing) return false;
        CloseBookmarks(); return true;
    }
    private bool HandleBookmarkIntent(in RmlUiIntent intent)
    {
        if (!IsOpen || _bookmarkBackend == null || !Enum.IsDefined((TheatrePlaybackAction)intent.Argument)) return false;
        var action = (TheatrePlaybackAction)intent.Argument;
        bool opening = action == TheatrePlaybackAction.Bookmarks && _bookmarkModal == default && intent.Document == _document;
        bool confirming = _bookmarkConfirmation != default && intent.Document == _bookmarkConfirmation
            && action is TheatrePlaybackAction.BookmarkConfirmRemove or TheatrePlaybackAction.BookmarkCancel;
        bool editing = _bookmarkConfirmation == default && _bookmarkModal != default && intent.Document == _bookmarkModal
            && action is TheatrePlaybackAction.BookmarkPrevious or TheatrePlaybackAction.BookmarkNext
                or TheatrePlaybackAction.BookmarkAdd or TheatrePlaybackAction.BookmarkJump
                or TheatrePlaybackAction.BookmarkRemove or TheatrePlaybackAction.BookmarkCancel or TheatrePlaybackAction.BookmarkRefresh;
        if ((!opening && !confirming && !editing) || !_pages.Accept(intent)) return false;
        try
        {
            _bookmarkProblem = "";
            if (opening)
            {
                Viewport.Release();
                _bookmarks?.Dispose();
                _bookmarks = new(_bookmarkBackend);
                _bookmarkModal = _pages.OpenModal(new("replay-bookmarks", "pages/theatre/bookmarks.rml", "replay_bookmark_name"));
                _host.SetField(_bookmarkModal, "replay_bookmark_name", "");
            }
            else switch (action)
            {
                case TheatrePlaybackAction.BookmarkPrevious: _bookmarks?.Move(-1); break;
                case TheatrePlaybackAction.BookmarkNext: _bookmarks?.Move(1); break;
                case TheatrePlaybackAction.BookmarkAdd:
                    if (_bookmarks?.Add(_host.ReadField(_bookmarkModal, "replay_bookmark_name")) == true)
                        _host.SetField(_bookmarkModal, "replay_bookmark_name", "");
                    break;
                case TheatrePlaybackAction.BookmarkJump: _bookmarks?.Jump(); break;
                case TheatrePlaybackAction.BookmarkRefresh: _bookmarks?.Refresh(); break;
                case TheatrePlaybackAction.BookmarkRemove:
                    if (_bookmarks?.RequestRemove() == true)
                        _bookmarkConfirmation = _pages.OpenModal(new("replay-bookmark-remove",
                            "pages/theatre/bookmark-remove.rml", "replay_bookmark_remove_cancel"));
                    break;
                case TheatrePlaybackAction.BookmarkConfirmRemove:
                    if (_bookmarks?.ConfirmRemove() == true && _pages.CloseModal()) _bookmarkConfirmation = default;
                    break;
                case TheatrePlaybackAction.BookmarkCancel: Back(); break;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _bookmarkProblem = error.Message; }
        PresentBookmarks(); return true;
    }
    private void PresentBookmarks()
    {
        if (_bookmarkConfirmation != default && !_host.IsAlive(_bookmarkConfirmation))
        { _bookmarkConfirmation = default; _bookmarks?.CancelRemove(); }
        if (_bookmarkModal != default && !_host.IsAlive(_bookmarkModal))
        { _bookmarkModal = default; _bookmarks?.Dispose(); _bookmarks = null; }
        if (_bookmarkModal == default || _bookmarks == null) return;
        var state = _bookmarks.Snapshot();
        _host.SetText(_bookmarkModal, "replay_bookmark_count", state.Entries.Length == 0 ? "NO SAVED BOOKMARKS"
            : $"BOOKMARK {state.SelectedIndex + 1} / {state.Entries.Length}");
        _host.SetText(_bookmarkModal, "replay_bookmark_selected", state.Selected is { } selected
            ? Time(selected.Frame) + " // " + selected.Name : "Add a bookmark at the current playhead.");
        _host.SetText(_bookmarkModal, "replay_bookmark_status", state.Status);
        _host.SetText(_bookmarkModal, "replay_bookmark_error", _bookmarkProblem.Length > 0 ? _bookmarkProblem
            : !state.Current ? "The replay owner changed. Close this panel." : state.Error);
        foreach (string id in new[] { "previous", "next", "jump", "remove" })
            _host.SetBool(_bookmarkModal, "disabled:replay_bookmark_" + id, !state.Current || state.Selected == null);
        _host.SetBool(_bookmarkModal, "disabled:replay_bookmark_add", !state.Current);
        _host.SetBool(_bookmarkModal, "disabled:replay_bookmark_refresh", !state.Current);
        _host.SetBool(_bookmarkModal, "disabled:replay_bookmark_name", !state.Current);
        if (_bookmarkConfirmation == default) return;
        _host.SetText(_bookmarkConfirmation, "replay_bookmark_remove_replay", state.ReplayName);
        _host.SetText(_bookmarkConfirmation, "replay_bookmark_remove_name", state.PendingRemoval is { } exact
            ? Time(exact.Frame) + " // " + exact.Name : "The selected bookmark is no longer pending.");
        _host.SetText(_bookmarkConfirmation, "replay_bookmark_remove_error", _bookmarkProblem.Length > 0 ? _bookmarkProblem : state.Error);
        _host.SetBool(_bookmarkConfirmation, "disabled:replay_bookmark_remove_confirm", !state.Current || state.PendingRemoval == null);
    }
    private bool CloseBookmarks()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if (_bookmarkConfirmation != default)
        {
            if (_host.IsAlive(_bookmarkConfirmation) && !_pages.CloseModal()) return false;
            _bookmarkConfirmation = default;
        }
        if (_bookmarkModal != default)
        {
            if (_bookmarkModal!=_document && _host.IsAlive(_bookmarkModal) && !_pages.CloseModal()) return false;
            _bookmarkModal = default;
        }
        _bookmarks?.Dispose(); _bookmarks = null; _bookmarkProblem = ""; return true;
    }
}
#endif
