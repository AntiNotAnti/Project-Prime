#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Foreground publication dialog over the retained Studio page.</summary>
internal sealed class NativeCommunityPublishPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly INativeCommunityPublishAuthority? _injected;
    private NativeCommunityPublishController? _controller;
    private CancellationTokenRegistration _retirement;
    private RmlUiDocumentToken _document;
    private long _shown = -1;
    private bool _disposed;
    internal bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document);
    internal RmlUiDocumentToken Document => _document;
    internal NativeCommunityPublishPage(RmlUiHost host, RmlUiPageManager pages, INativeCommunityPublishAuthority? authority = null)
        => (_host, _pages, _injected) = (host, pages, authority);
    internal bool Open(CommunityStudioPublishIntent intent, string returnFocus)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return OpenCore(new(intent.MapId, intent.RuntimeName), _injected ?? new NativeCommunityPublishService(intent), returnFocus);
    }
    internal bool OpenSynthetic(NativeCommunityPublishSource source, string returnFocus)
        => OpenCore(source, _injected ?? throw new InvalidOperationException("A synthetic publication authority is required."), returnFocus);
    private bool OpenCore(NativeCommunityPublishSource source, INativeCommunityPublishAuthority authority, string returnFocus)
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen || _pages.Top == default) return false;
        _controller?.Dispose(); _controller = new(authority);
        if (!_controller.Open(source)) return false;
        if (!string.IsNullOrEmpty(returnFocus)) _host.FocusDocument(_pages.Top, returnFocus);
        try
        {
            _document = _pages.OpenModal(new("community-publish", "pages/community/native-publish.rml", "community_publish_close"));
            _retirement = _pages.Lifetime(_document).Register(() => { _controller?.Dispose(); _document = default; });
        }
        catch { _controller.Dispose(); throw; }
        _shown = -1; Refresh(); return true;
    }
    internal bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!Foreground() || intent.Document != _document) return false;
        bool known = intent.Kind is RmlUiIntentKind.CommunityConfirm or RmlUiIntentKind.CommunityCancel or RmlUiIntentKind.CommunityRefresh
            || intent.Kind == RmlUiIntentKind.CommunityVisibility && intent.Argument is >= 0 and < 3
            || intent.Kind == RmlUiIntentKind.CommunityUpload && intent.Argument is 0 or 1
            || intent.Kind == RmlUiIntentKind.CommunityDetailAction && intent.Argument is 0 or 1;
        if (!known || !_pages.Accept(intent)) return false;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.CommunityVisibility: _controller!.SetVisibility(intent.Argument); break;
            case RmlUiIntentKind.CommunityUpload:
                if (intent.Argument == 0) _controller!.Propose(_host.ReadField(_document, "community_publish_notes", 16004));
                else _controller!.CancelPreparation();
                break;
            case RmlUiIntentKind.CommunityConfirm: _controller!.Confirm(); break;
            case RmlUiIntentKind.CommunityRefresh: _controller!.Refresh(); break;
            case RmlUiIntentKind.CommunityDetailAction: _controller!.ControlTransfer(intent.Argument == 0); break;
            case RmlUiIntentKind.CommunityCancel: return Back();
        }
        Refresh(); return true;
    }
    internal bool Back()
    {
        _host.VerifyOwnerThread(); if (!Foreground()) return false;
        if (_controller!.Dismiss()) { Refresh(); return true; }
        if (!_pages.CloseModal()) return false;
        _retirement.Dispose(); _controller.Dispose(); _document = default;
        return true;
    }
    private bool Foreground() => IsOpen && _pages.Top == _document && _host.IsVisible(_document)
        && _host.CurrentInputDocument == _document;
    internal void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _controller == null || _document == default) return;
        if (!_host.IsAlive(_document)) { _controller.Dispose(); _document = default; return; }
        var state = _controller.Tick(); if (state.Revision == _shown) return;
        if (!state.AccountCurrent) _host.SetField(_document, "community_publish_notes", "");
        var data = new Dictionary<string, RmlUiBindingValue>();
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        Text("community_publish_title", state.Name);
        Text("community_publish_status", state.Status);
        Text("community_publish_parent", state.ParentHash.Length == 0 ? "NEW COMMUNITY PROJECT" : "EXPECTED PARENT SHA-256 // " + state.ParentHash);
        Text("community_publish_confirmation", state.Confirmation);
        Text("community_publish_hash", state.PackageHash.Length == 0 ? "PACKAGE SHA-256 IS ASSIGNED AFTER VALIDATED BUILD" : "EXACT PACKAGE SHA-256 // " + state.PackageHash);
        Text("community_publish_transfer", state.TransferState + (state.Progress.Length == 0 ? "" : " // " + state.Progress));
        Bool("visible:community_publish_confirmation", state.Confirmation.Length != 0);
        Bool("visible:community_publish_confirm", state.Confirmation.Length != 0);
        Bool("disabled:community_publish_confirm", state.Busy);
        Bool("disabled:community_publish_notes", !state.Ready);
        Bool("disabled:community_publish_prepare", !state.Ready);
        Bool("disabled:community_publish_refresh", state.Busy || state.PackageHash.Length != 0 || state.Confirmation.Length != 0);
        Bool("visible:community_publish_cancel_prepare", state.Busy && state.PackageHash.Length == 0);
        Bool("visible:community_publish_pause", state.CanPause);
        Bool("visible:community_publish_resume", state.CanResume);
        Bool("visible:community_publish_conflict", state.NeedsReview);
        foreach (var (id, index) in new[] { ("published", 0), ("unlisted", 1), ("draft", 2) })
        {
            Bool("disabled:community_publish_" + id, !state.Ready);
            Bool("class:community_publish_" + id + ":selected", (int)state.Visibility == index);
        }
        _pages.Present(_document, state.Revision, data); _shown = state.Revision;
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return;
        _disposed = true; _retirement.Dispose(); _controller?.Dispose();
        if (_document != default && _pages.Top == _document) _pages.CloseModal();
        _document = default;
    }
}
#endif
