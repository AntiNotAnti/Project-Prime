#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.Linq;
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
    private readonly HudEditorOption[] _originalOptions;
    private readonly Func<HudProfile, HudEditorOption[], bool>? _autoSave;
    private readonly Func<string>? _autoSaveError;
    private readonly record struct AutoSaveEditKey(string ProfileJson, string Fields, string Options);
    private string _savedKey;
    private AutoSaveEditKey? _observedKey, _failedKey, _settledKey;
    private long _lastEdit;
    private bool _saveFailed, _previewRequested, _returnedFromPreview;
    private readonly bool _canPreview;
    private string _previewFocus="";
    private RmlUiDocumentToken _confirm;
    private bool _disposed, _retired;
    public NativeHudEditorPage(RmlUiHost host, RmlUiPageManager pages, HudProfile original,bool asModal=false,HudEditorOption[]? options=null,
        Func<HudProfile, HudEditorOption[], bool>? autoSave=null,Func<string>? autoSaveError=null,bool canPreview=false)
    {
        _host = host; _pages = pages;
        _controller = new(original.DeepClone(), options: options);
        _originalOptions = _controller.CopyOptions();
        _original = _controller.Snapshot().ProfileJson; _originalName = original.Name;
        _autoSave = autoSave;
        _autoSaveError = autoSaveError;_canPreview=canPreview;
        _savedKey = SaveKey(_controller.CopyDraft(), _originalOptions);
        _presenter = new(host, pages, _controller,asModal:asModal) { AutoApply = autoSave != null };
    }
    public RmlUiDocumentToken Document => _presenter.Document;
    public bool Closed => _disposed || _retired || _controller.Snapshot().Closed;
    public bool HasLivePage => !_disposed && !_retired && _presenter.IsOpen;
    public bool OwnsForeground => !Closed && Foreground(_confirm != default ? _confirm : _presenter.ForegroundDocument);
    public bool CanNavigateAway => Closed || !Dirty;
    internal HudEditorOption[] CopyOptions() => _controller.CopyOptions();
    private bool Dirty => !System.Linq.Enumerable.SequenceEqual(_originalOptions, _controller.CopyOptions()) || _controller.Snapshot().ProfileJson != _original
        || (HasLivePage && _host.ReadField(Document, "hud_name") != _originalName);
    private bool Foreground(RmlUiDocumentToken document) => !_disposed && document != default
        && _pages.Top == document && _host.CurrentInputDocument == document
        && _host.IsAlive(document) && _host.IsVisible(document);
    public void Open() { _host.VerifyOwnerThread(); _presenter.Open();PresentPreviewAvailability(); }
    private void PresentPreviewAvailability()
    {
        _host.SetBool(Document,"disabled:hud_live_preview",!_canPreview);
        _host.SetText(Document,"hud_live_preview_help",_canPreview?_returnedFromPreview?"Preview ended. Continue editing or use Back to save changes.":"Try this HUD in the current match for 60 seconds. Escape returns here; automatic saving pauses during preview and resumes when you continue editing or go Back.":"Live preview is available from Settings during a match.");
    }
    internal bool TryTakePreview(out HudProfile profile,out HudEditorOption[] options)
    {
        profile=null!;options=Array.Empty<HudEditorOption>();
        if(!_previewRequested||!OwnsForeground)return false;
        _previewRequested=false;
        if(!_presenter.CapturePendingFields())return false;
        profile=_controller.CopyDraft();profile.Name=_host.ReadField(Document,"hud_name",64);profile.Validate();
        options=_controller.CopyOptions();return true;
    }
    internal bool SetPreviewVisible(bool visible)
    {
        if(!visible)_previewFocus=_host.FocusedElement();
        bool shown=_host.ShowDocument(Document,visible);
        if(shown&&visible)
        {
            _lastEdit=Stopwatch.GetTimestamp();_observedKey=EditKey();_settledKey=_observedKey;_returnedFromPreview=true;
            if(_previewFocus.Length>0)_host.FocusDocument(Document,_previewFocus);
        }
        return shown;
    }
    public void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _retired) return;
        if (!_presenter.IsOpen) { _retired = true; _controller.Dispose(); return; }
        if (_confirm != default && !_host.IsAlive(_confirm)) _confirm = default;
        _presenter.Present();PresentPreviewAvailability();
        TickAutoSave();
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!OwnsForeground || intent.Document != (_confirm != default ? _confirm : _presenter.ForegroundDocument)) return false;
        if(_presenter.PopupOpen)return _presenter.HandleIntent(intent);
        if(intent.Kind==RmlUiIntentKind.SettingsAction&&intent.Argument==246)
        {
            if(_confirm!=default||!_canPreview||!_pages.Accept(intent))return false;
            _controller.ReleaseInput();
            if(_presenter.CapturePendingFields())_previewRequested=true;
            _presenter.Present();return true;
        }
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
        if (intent.Kind == RmlUiIntentKind.HudAction && (intent.Argument == (int)HudEditorAction.Cancel
            || (_autoSave != null && intent.Argument == (int)HudEditorAction.Use)))
        {
            if (!_pages.Accept(intent)) return false;
            return RequestBack();
        }
        return _presenter.HandleIntent(intent);
    }
    public bool Back()
    {
        if (!OwnsForeground) return false;
        if(_presenter.DismissPopup())return true;
        if (_confirm != default) { if (!_pages.CloseModal()) return false; _confirm = default; _host.FocusDocument(Document, "hud_cancel"); return true; }
        if (_controller.ControllerMoveMode) return _presenter.Back();
        return RequestBack();
    }
    private bool RequestBack()
    {
        _controller.ReleaseInput();
        if (_autoSave != null)
        {
            if (FlushAutoSave(discardInvalid:true)) _controller.Dispatch(HudEditorAction.Cancel);
            _presenter.Present();
            return true;
        }
        if (Dirty)
            _confirm = _pages.OpenModal(new("native-hud-unsaved", "pages/hud/native-unsaved.rml", "native_hud_keep"));
        else _controller.Dispatch(HudEditorAction.Cancel);
        return true;
    }
    private AutoSaveEditKey EditKey() => new(_controller.Snapshot().ProfileJson, _presenter.PendingFieldsKey(),
        string.Join("\n", _controller.CopyOptions().Select(option => option.Id + "=" + option.Value)));
    private static string SaveKey(HudProfile profile, HudEditorOption[] options) => HudProfileStore.Serialize(profile)
        + "\n" + string.Join("\n", options.Select(option => option.Id + "=" + option.Value));
    private void TickAutoSave()
    {
        if (_autoSave == null || SettingsPreview.Active || _previewRequested || !OwnsForeground || _confirm != default || _presenter.PopupOpen) return;
        long now = Stopwatch.GetTimestamp();
        if (_controller.GestureActive) { _lastEdit = now; return; }
        var key = EditKey();
        if(_returnedFromPreview&&key!=_settledKey)_returnedFromPreview=false;
        if (key != _observedKey) { _observedKey = key; _lastEdit = now; _failedKey = null; }
        if (_failedKey == key || _settledKey == key || Stopwatch.GetElapsedTime(_lastEdit, now).TotalMilliseconds < 400) return;
        if (!FlushAutoSave()) _failedKey = _observedKey = EditKey();
        else _settledKey = EditKey();
    }
    private bool FlushAutoSave(bool discardInvalid=false)
    {
        if (_autoSave == null) return true;
        if (!_presenter.CapturePendingFields(discardInvalid)) { _presenter.Present(); return false; }
        var profile = _controller.CopyDraft();
        profile.Name = _host.ReadField(Document, "hud_name", 64);
        if (profile.Mode == HudMode.Custom && HudProfileDefaults.Presets.Contains(profile.Name)) profile.Name += " (Custom)";
        profile.Validate();
        var options = _controller.CopyOptions();
        string key = SaveKey(profile, options);
        // A failed outer transaction retains its draft. Even undoing to the last
        // saved profile must hand that value back so the parent cannot retry stale edits.
        if (key == _savedKey && !_saveFailed) return true;
        if (!_autoSave(profile, options))
        {
            _saveFailed = true;
            string? detail=_autoSaveError?.Invoke();
            _controller.ReportFailure((string.IsNullOrWhiteSpace(detail)?"Could not save HUD settings.":detail)
                +" Your edits are kept here; Back retries saving.");
            _presenter.Present();
            _failedKey = _observedKey = EditKey();
            return false;
        }
        if (_saveFailed) { _controller.ReportFailure(""); _saveFailed = false; }
        _savedKey = key;
        _presenter.Present();
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
