#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render.Hud;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Hud;

/// <summary>Lifecycle and unsaved-change boundary around the existing detached HUD authoring owner.</summary>
internal sealed class NativeHudEditorPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly HudEditorController _controller;
    private readonly HudEditorPagePresenter _presenter;
    private readonly string _original;
    private readonly string _originalName;
    private RmlUiDocumentToken _confirm;
    private bool _disposed, _retired;
    public NativeHudEditorPage(RmlUiHost host, RmlUiPageManager pages, HudProfile original,bool asModal=false)
    {
        _host = host; _pages = pages;
        _controller = new(original.DeepClone());
        _original = _controller.Snapshot().ProfileJson; _originalName = original.Name;
        _presenter = new(host, pages, _controller,asModal:asModal);
    }
    public RmlUiDocumentToken Document => _presenter.Document;
    public bool Closed => _disposed || _retired || _controller.Snapshot().Closed;
    public bool HasLivePage => !_disposed && !_retired && _presenter.IsOpen;
    public bool OwnsForeground => !Closed && Foreground(_confirm != default ? _confirm : Document);
    public bool CanNavigateAway => Closed || !Dirty;
    private bool Dirty => _controller.Snapshot().ProfileJson != _original
        || (HasLivePage && _host.ReadField(Document, "hud_name") != _originalName);
    private bool Foreground(RmlUiDocumentToken document) => !_disposed && document != default
        && _pages.Top == document && _host.CurrentInputDocument == document
        && _host.IsAlive(document) && _host.IsVisible(document);
    public void Open() { _host.VerifyOwnerThread(); _presenter.Open(); }
    public void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _retired) return;
        if (!_presenter.IsOpen) { _retired = true; _controller.Dispose(); return; }
        if (_confirm != default && !_host.IsAlive(_confirm)) _confirm = default;
        _presenter.Present();
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!OwnsForeground || intent.Document != (_confirm != default ? _confirm : Document)) return false;
        if (_confirm != default)
        {
            if (intent.Kind != RmlUiIntentKind.HudAction || (intent.Argument != (int)HudEditorAction.Use && intent.Argument != (int)HudEditorAction.Cancel && intent.Argument != (int)HudEditorAction.Undo)
                || !_pages.Accept(intent)) return false;
            var action = (HudEditorAction)intent.Argument;
            if (!_pages.CloseModal()) return false;
            _confirm = default;
            if (action != HudEditorAction.Undo)
                _controller.Dispatch(action, name: _host.ReadField(Document, "hud_name"));
            _presenter.Present(); _host.FocusDocument(Document, "hud_cancel"); return true;
        }
        if (intent.Kind == RmlUiIntentKind.HudAction && intent.Argument == (int)HudEditorAction.Cancel)
        {
            if (!_pages.Accept(intent)) return false;
            return RequestBack();
        }
        return _presenter.HandleIntent(intent);
    }
    public bool Back()
    {
        if (!OwnsForeground) return false;
        if (_confirm != default) { if (!_pages.CloseModal()) return false; _confirm = default; _host.FocusDocument(Document, "hud_cancel"); return true; }
        if (_controller.ControllerMoveMode) return _presenter.Back();
        return RequestBack();
    }
    private bool RequestBack()
    {
        _controller.ReleaseInput();
        if (Dirty)
            _confirm = _pages.OpenModal(new("native-hud-unsaved", "pages/hud/native-unsaved.rml", "native_hud_keep"));
        else _controller.Dispatch(HudEditorAction.Cancel);
        return true;
    }
    public bool TryTakeAccepted(out HudProfile profile) => _controller.TryTakeAccepted(out profile);
    public bool TryTakeCancelled() => _controller.TryTakeCancelled();
    public void ReportFailure(string error) { _controller.ReportFailure(error); _presenter.Present(); }
    public void SetCanvasSize(float width, float height) { if (HasLivePage) _presenter.SetCanvasSize(width, height); }
    public bool PointerDown(long pointer, float x, float y, bool shift = false, bool alt = false, bool touch = false)
        => OwnsForeground && _confirm == default && _presenter.PointerDown(pointer, x, y, shift, alt, touch);
    public bool PointerMove(long pointer, float x, float y, bool shift = false, bool control = false)
        => OwnsForeground && _confirm == default && _presenter.PointerMove(pointer, x, y, shift, control);
    public void PointerUp(long pointer) { if (HasLivePage) _presenter.PointerUp(pointer); }
    public void ReleaseInput() { if (HasLivePage) _presenter.ReleaseInput(); }
    public bool KeyDown(Keys key, RmlUiInputModifiers modifiers)
        => OwnsForeground && _confirm == default && _presenter.KeyDown(key, modifiers);
    public bool HandleController(UiAction action, bool canvasFocused)
        => action == UiAction.Back ? Back() : OwnsForeground && _confirm == default && _presenter.HandleController(action, canvasFocused);
    public void HandleControllerAxes(GamepadSnapshot snapshot, long now)
    { if (OwnsForeground && _confirm == default) _presenter.HandleControllerAxes(snapshot, now); }
    public void Dispose()
    {
        if (_disposed) return;
        if (_confirm != default && _pages.Top == _confirm && !_pages.CloseModal())
            throw new InvalidOperationException("HUD editor confirmation could not close.");
        _confirm = default; _presenter.Dispose(); _controller.Dispose(); _disposed = true;
    }
}
#endif
