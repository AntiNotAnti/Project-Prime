#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Setup;

internal sealed class NativeGameFilesPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private NativeGameFilesController? _controller;
    private readonly INativeGameFilesAuthority _authority;
    private readonly Func<bool> _scope;
    private bool _restoreChoose;
    private RmlUiDocumentToken _document, _confirm;
    private long _revision;
    private bool _disposed, _retired;
    public bool Closed => _retired || _disposed || _controller?.Snapshot().Closed == true;
    internal bool HasLivePage => !_disposed && !_retired && _document != default
        && _host.IsAlive(_document) && _pages.Page == _document;
    public RmlUiDocumentToken Document => _document;
    internal bool OwnsForeground => Foreground(_confirm != default ? _confirm : _document);
    internal NativeGameFilesPage(RmlUiHost host, RmlUiPageManager pages, INativeGameFilesAuthority authority,
        Func<bool> scope)
    { _host = host; _pages = pages; _authority = authority; _scope = scope; }
    public void Open()
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_document != default) return;
        _document = _pages.OpenPage(new("native-game-files", "pages/setup/native-game-files.rml", "native_setup_choose"));
        _controller = new(_authority, () => _scope() && _host.IsAlive(_document)
            && _host.IsVisible(_document) && _pages.Page == _document, _pages.Lifetime(_document),
            canStartImport: () => Foreground(_document));
        Refresh();
    }
    private bool Foreground(RmlUiDocumentToken document) => !_disposed && document != default
        && _pages.Top == document && _host.CurrentInputDocument == document
        && _host.IsAlive(document) && _host.IsVisible(document);
    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread(); var top = _confirm != default ? _confirm : _document;
        if (_controller == null || !Foreground(top) || intent.Document != top || intent.Kind != RmlUiIntentKind.SetupAction) return false;
        bool recognized = _confirm != default ? intent.Argument is 2 or 11 : intent.Argument is 0 or 1 or 3 or 5 or 11;
        if (!recognized || !_pages.Accept(intent)) return false;
        bool changed = intent.Argument switch
        {
            0 => _controller.Back(), 1 => _controller.Choose(), 2 => _controller.Confirm(),
            3 => _controller.Refresh(), 5 => _controller.Previews(), 11 => _controller.Cancel(), _ => false
        };
        Refresh(); return changed;
    }
    public bool Back()
    {
        _host.VerifyOwnerThread(); if (_controller == null || !Foreground(_confirm != default ? _confirm : _document)) return false;
        bool handled = _confirm != default ? _controller.Cancel() : _controller.Back(); Refresh(); return handled;
    }
    public bool TryTakeInstalled() { _host.VerifyOwnerThread(); return _confirm == default && Foreground(_document) && _controller?.TryTakeInstalled() == true; }
    public void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _document == default || _controller == null) return;
        if (!HasLivePage) { _retired = true; _controller.Dispose(); _confirm = default; return; }
        if (_confirm != default && !_host.IsAlive(_confirm))
        {
            // External modal retirement also retires its pending proposal.
            _confirm = default; _controller.Cancel(); _restoreChoose = true;
        }
        var state = _controller.Tick();
        if (state.Confirming && _confirm == default && Foreground(_document))
            _confirm = _pages.OpenModal(new("native-game-files-confirm", "pages/setup/native-game-files-confirm.rml", "native_setup_decline"));
        if (!state.Confirming && _confirm != default && _pages.Top == _confirm)
        { _pages.CloseModal(); _confirm = default; _restoreChoose = true; }
        var fields = new Dictionary<string, RmlUiBindingValue>();
        void Text(string key, string value) => fields[key] = RmlUiBindingValue.FromText(value);
        void Bool(string key, bool value) => fields[key] = RmlUiBindingValue.FromBoolean(value);
        Text("native_setup_files", state.Files.Description); Text("native_setup_root", state.Files.Root);
        Text("native_setup_status", state.Status); Text("native_setup_log", state.Log);
        Text("native_setup_progress", state.Stage + (state.Busy && state.Stage.Length > 0 ? " // milestone " + Math.Round(state.Fraction * 100).ToString(CultureInfo.InvariantCulture) + "%" : ""));
        Bool("disabled:native_setup_choose", state.Busy || state.Confirming || state.Files.SetupBusy);
        Bool("disabled:native_setup_refresh", state.Busy || state.Confirming);
        Bool("disabled:native_setup_back", !state.CanBack);
        Bool("visible:native_setup_previews", state.Files.Ready && state.Files.CanRenderPreviews && state.Files.MissingPreviews > 0);
        Bool("disabled:native_setup_previews", state.Busy || state.Confirming || state.Files.SetupBusy);
        Bool("visible:native_setup_cancel", state.Busy);
        _pages.Present(_document, ++_revision, fields);
        if (_confirm != default && _host.IsAlive(_confirm))
            _pages.Present(_confirm, ++_revision, new Dictionary<string, RmlUiBindingValue>
                { ["native_setup_confirmation"] = RmlUiBindingValue.FromText("Install " + state.SelectedName + " into your game-files folder?\nExisting game files may be updated.") });
    }
    // Called after the existing host.Update. Deferred field/button bindings
    // must be applied before restoring the formerly-disabled Choose button.
    public void AfterUpdate()
    {
        _host.VerifyOwnerThread(); if (!_restoreChoose || !Foreground(_document) || _controller == null) return;
        if (_controller.Snapshot().Busy) return;
        _restoreChoose = !_host.FocusDocument(_document, "native_setup_choose");
    }
    internal void RetireWork() { _host.VerifyOwnerThread(); _controller?.Dispose(); }
    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return; _disposed = true; _controller?.Dispose();
        if (_confirm != default && _pages.Top == _confirm) _pages.CloseModal();
        if (_document != default && _pages.Page == _document && _pages.Top == _document) _pages.ClosePage();
        _document = default; _confirm = default;
    }
}
#endif
