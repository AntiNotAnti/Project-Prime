#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

internal sealed class NativeCommunityRevisionsPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityRevisionsController _controller;
    private readonly NativeCommunityInstallController _install;
    private RmlUiDocumentToken _document;
    private long _shown, _shownInstall, _revision;
    private bool _installOwned, _disposed;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document);
    public NativeCommunityRevisionsPage(RmlUiHost host, RmlUiPageManager pages,
        INativeCommunityRevisionsAuthority authority, NativeCommunityInstallController install)
        => (_host, _pages, _controller, _install) = (host, pages, new(authority), install);

    public bool Open(NativeCommunityHistoryRequest request)
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen || !_controller.Open(request)) return false;
        _install.Cancel();
        try { _document = _pages.OpenModal(new("community-revisions", "pages/community/native-revisions.rml", "community_history_close")); }
        catch { _controller.Cancel(); throw; }
        Refresh(); return true;
    }
    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!Foreground() || intent.Document != _document) return false;
        bool recognized = intent.Kind is RmlUiIntentKind.CommunitySelectRevision or RmlUiIntentKind.CommunityRevisionPage
            or RmlUiIntentKind.CommunityRefresh or RmlUiIntentKind.CommunityConfirm or RmlUiIntentKind.CommunityCancel
            or RmlUiIntentKind.CommunityCancelWork
            || intent.Kind == RmlUiIntentKind.CommunityCreatorAction && intent.Argument is >= 0 and < 5
            || intent.Kind == RmlUiIntentKind.CommunityVisibility && intent.Argument is >= 0 and < 3
            || intent.Kind == RmlUiIntentKind.CommunityDetailAction && intent.Argument == 0;
        if (!recognized || !_pages.Accept(intent)) return false;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.CommunitySelectRevision: _install.Cancel(); _installOwned = false; _controller.Select(intent.Argument); break;
            case RmlUiIntentKind.CommunityRevisionPage: _install.Cancel(); _installOwned = false; _controller.Page(intent.Argument); break;
            case RmlUiIntentKind.CommunityRefresh: _install.Cancel(); _installOwned = false; _controller.Refresh(); break;
            case RmlUiIntentKind.CommunityCreatorAction:
                _install.Cancel(); _installOwned = false; _controller.Propose((NativeCommunityRevisionAction)intent.Argument); break;
            case RmlUiIntentKind.CommunityVisibility:
                _install.Cancel(); _installOwned = false;
                _controller.Propose(intent.Argument switch { 0 => NativeCommunityRevisionAction.Publish, 1 => NativeCommunityRevisionAction.Unlist, _ => NativeCommunityRevisionAction.Draft }); break;
            case RmlUiIntentKind.CommunityConfirm: _controller.ConfirmChange(); break;
            case RmlUiIntentKind.CommunityCancel: return Back();
            case RmlUiIntentKind.CommunityCancelWork: _install.Cancel(); break;
            case RmlUiIntentKind.CommunityDetailAction:
                if (!_controller.TryGetInstallSelection(out var selected)) return false;
                _installOwned = true;
                try
                {
                    if (!GameFiles.Ready) { _install.FailPreflight(); break; }
                    MapRuntimeUsage.RequireInstallationAllowed(); GameFiles.ApplyPaths(); _install.Begin(selected);
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
                { _install.FailPreflight(); }
                break;
        }
        Refresh(); return true;
    }
    public bool Back()
    {
        _host.VerifyOwnerThread(); if (!Foreground()) return false;
        if (_controller.Dismiss()) { Refresh(); return true; }
        if (!_pages.CloseModal()) return false;
        _install.Cancel(); _installOwned = false; _controller.Cancel(); _document = default;
        return true;
    }
    private bool Foreground() => IsOpen && _pages.Top == _document && _host.IsVisible(_document);
    public void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document)) { _install.Cancel(); _controller.Cancel(); _document = default; return; }
        var state = _controller.Tick();
        // Retire installation before its owner-thread registration step when account/history is lost.
        if (!state.Connected) { _install.Cancel(); _installOwned = false; }
        var install = _install.Tick();
        if (state.Revision == _shown && install.Revision == _shownInstall) return;
        var data = new Dictionary<string, RmlUiBindingValue>();
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        Text("community_history_title", state.Name);
        Text("community_history_status", _installOwned && install.Status.Length != 0 ? install.Status : state.Status);
        Text("community_history_page", state.Total == 0 ? "0 / 0" : $"{state.Page + 1} / {(state.Total + 7) / 8}");
        Text("community_history_detail", state.Selected is { } selected
            ? $"REVISION {selected.Number}" + (selected.Hash.Equals(state.CurrentHash, StringComparison.OrdinalIgnoreCase) ? " // CURRENT PUBLIC REVISION" : "")
                + "\n" + (selected.Deleted ? "DELETED" : selected.Draft ? "PRIVATE DRAFT" : selected.Listed ? "PUBLISHED" : "UNLISTED")
                + "\nPACKAGE SHA-256 // " + selected.Hash + "\nPARENT // " + (selected.ParentHash.Length == 0 ? "INITIAL REVISION" : selected.ParentHash)
                + (selected.Deleted && selected.DeleteAfter is { } deadline ? "\nSERVICE RESTORE DEADLINE // " + deadline.UtcDateTime.ToString("u") : "")
                + "\n" + (selected.Notes.Length == 0 ? "No release notes." : selected.Notes)
            : "Select an immutable revision. Private drafts must be published before using the public installation owner.");
        Text("community_history_confirmation", state.Confirmation);
        Text("community_history_confirm", state.PendingAction == NativeCommunityRevisionAction.Promote
            ? "CONFIRM PUBLIC REVISION" : "CONFIRM " + state.PendingAction?.ToString().ToUpperInvariant());
        Bool("visible:community_history_confirmation", state.Confirming);
        Bool("visible:community_history_confirm", state.Confirming);
        Bool("visible:community_history_dismiss", state.Confirming);
        Bool("disabled:community_history_confirm", state.Busy);
        Bool("disabled:community_history_install", !_controller.TryGetInstallSelection(out _) || install.Busy || !GameFiles.Ready);
        Bool("disabled:community_history_promote", !state.CanPromote || state.Confirming || install.Busy);
        Bool("visible:community_history_owner_actions", state.Owned);
        Bool("disabled:community_history_publish", !state.CanPublish || state.Confirming || install.Busy);
        Bool("disabled:community_history_unlist", !state.CanUnlist || state.Confirming || install.Busy);
        Bool("disabled:community_history_draft", !state.CanDraft || state.Confirming || install.Busy);
        Bool("disabled:community_history_delete", !state.CanDelete || state.Confirming || install.Busy);
        Bool("disabled:community_history_restore", !state.CanRestore || state.Confirming || install.Busy);
        Bool("disabled:community_history_previous", state.Busy || state.Confirming || !state.CanPrevious);
        Bool("disabled:community_history_next", state.Busy || state.Confirming || !state.CanNext);
        Bool("disabled:community_history_refresh", state.Busy || state.Confirming);
        Bool("visible:community_history_cancel_install", _installOwned && install.Busy);
        for (int i = 0; i < 8; i++)
        {
            var row = i < state.Rows.Count ? state.Rows[i] : null;
            Bool("visible:community_history_row" + i, row != null);
            Bool("disabled:community_history_row" + i, state.Busy || state.Confirming);
            Bool("class:community_history_row" + i + ":selected", row != null && i == state.SelectedIndex);
            Text("community_history_label" + i, row == null ? "" : $"REV {row.Number} // "
                + (row.Deleted ? "DELETED" : row.Draft ? "DRAFT" : row.Listed ? "PUBLISHED" : "UNLISTED")
                + (row.Hash.Equals(state.CurrentHash, StringComparison.OrdinalIgnoreCase) ? " // CURRENT" : ""));
        }
        _pages.Present(_document, ++_revision, data); _shown = state.Revision; _shownInstall = install.Revision;
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return;
        _disposed = true; _install.Cancel(); _controller.Dispose();
        if (_document != default && _pages.Top == _document) _pages.CloseModal();
        _document = default;
    }
}
#endif
