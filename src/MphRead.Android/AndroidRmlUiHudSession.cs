#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Settings;

namespace MphRead.Droid;

/// <summary>Owner-thread HUD draft handoff; Android supplies framebuffer pointer coordinates.</summary>
internal sealed class AndroidRmlUiHudSession : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly SettingsPagePresenter _settings;
    private readonly HashSet<int> _pointers = new();
    private HudEditorController? _controller;
    private HudEditorPagePresenter? _presenter;
    private RmlUiTextInputBounds? _gestureBounds;
    private RmlUiDocumentToken _gestureDocument;
    private bool _disposed;

    internal AndroidRmlUiHudSession(RmlUiHost host, RmlUiPageManager pages, SettingsPagePresenter settings)
        => (_host, _pages, _settings) = (host, pages, settings);

    internal bool Active => !_disposed && _presenter?.IsOpen == true;
    internal RmlUiDocumentToken Document => _presenter?.Document ?? default;
#if MPHREAD_RMLUI_ANDROID_CHECK
    private string? _acceptedProfileForCheck;
    internal string AcceptedProfileForCheck()
    {
        VerifyOwner();
        return _acceptedProfileForCheck ?? throw new InvalidOperationException("The actual HUD draft has not been accepted.");
    }
    internal HudEditorSnapshot SnapshotForCheck()
    {
        VerifyOwner();
        return _controller?.Snapshot() ?? throw new InvalidOperationException("The actual HUD editor is not open.");
    }
    internal string PointerStateForCheck()
    {
        VerifyOwner();
        return $"captured={_pointers.Count}, ids={String.Join(',', _pointers)}, bounds={_gestureBounds}, {_controller?.GestureSummaryForCheck()}";
    }
#endif

    internal bool Open()
    {
        VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (Active) return true;
#if MPHREAD_RMLUI_ANDROID_CHECK
        _acceptedProfileForCheck = null;
#endif
        if (_presenter != null || _controller != null) CloseEditor();
        var draft = _settings.CaptureHudDraft();
        if (!_settings.SuspendForHud()) return false;
        try
        {
            _controller = new(draft);
            _presenter = new(_host, _pages, _controller);
            _presenter.Open();
            Present();
            return true;
        }
        catch
        {
            CloseEditor();
            _settings.ResumeFromHud();
            throw;
        }
    }

    internal void Present()
    {
        VerifyOwner();
        if (!Active) return;
        if (_pointers.Count == 0 && CaptureCanvasBounds() is { } bounds)
            _presenter!.SetCanvasSize(bounds.Width, bounds.Height);
        _presenter!.Present();
        if (_presenter.TryTakeAccepted(out var profile))
        {
            if (!_settings.AcceptHudDraft(profile))
            {
                _presenter.ReportFailure("The HUD draft could not be staged. Correct the settings and retry.");
                return;
            }
#if MPHREAD_RMLUI_ANDROID_CHECK
            _acceptedProfileForCheck = MphRead.Mods.Render.Hud.HudProfileStore.Serialize(profile);
#endif
            ReturnToSettings();
        }
        else if (_presenter.TryTakeCancelled()) ReturnToSettings();
    }

    internal bool HandleIntent(in RmlUiIntent intent)
    {
        VerifyOwner();
        if (!Active || !_presenter!.HandleIntent(intent)) return false;
        Present();
        return true;
    }

    internal bool Back()
    {
        VerifyOwner();
        if (!Active) return false;
        _presenter!.Back(); Present();
        return true;
    }

    internal RmlUiTextInputBounds? CaptureCanvasBounds()
    {
        VerifyOwner();
        return Active && _host.TryGetElementBounds(Document, "hud_canvas", out float x, out float y,
            out float width, out float height) ? new(x, y, width, height) : null;
    }

    internal bool DispatchPointer(int id, RmlUiPlatformInputKind kind, float x, float y, bool touch,
        RmlUiInputModifiers modifiers = default)
    {
        VerifyOwner();
        if (kind == RmlUiPlatformInputKind.FocusLost) { ReleaseInput(); return Active; }
        if (!Active || !float.IsFinite(x) || !float.IsFinite(y)) return false;
        if (_pointers.Count != 0 && _gestureDocument != Document) { ReleaseInput(); return false; }
        bool shift = (modifiers & RmlUiInputModifiers.Shift) != 0;
        bool control = (modifiers & (RmlUiInputModifiers.Control | RmlUiInputModifiers.Command)) != 0;
        if (kind == RmlUiPlatformInputKind.PointerDown)
        {
            var bounds = _gestureBounds ?? CaptureCanvasBounds();
            if (bounds is not { } canvas || x < canvas.X || y < canvas.Y
                || x >= canvas.X + canvas.Width || y >= canvas.Y + canvas.Height) return false;
            if (!_pointers.Add(id)) return true;
            if (_pointers.Count == 1)
            {
                _gestureBounds = canvas; _gestureDocument = Document;
                _presenter!.SetCanvasSize(canvas.Width, canvas.Height);
            }
            _host.FocusDocument(Document, "hud_canvas");
            _presenter!.PointerDown(id, x - canvas.X, y - canvas.Y, shift,
                (modifiers & RmlUiInputModifiers.Alt) != 0, touch);
            return true;
        }
        if (!_pointers.Contains(id) || _gestureBounds is not { } captured) return false;
        if (kind == RmlUiPlatformInputKind.PointerMove)
        {
            _presenter!.PointerMove(id, x - captured.X, y - captured.Y, shift, control);
            return true;
        }
        if (kind == RmlUiPlatformInputKind.PointerUp)
        {
            _presenter!.PointerUp(id); _pointers.Remove(id);
            if (_pointers.Count == 0) { _gestureBounds = null; _gestureDocument = default; }
            return true;
        }
        return false;
    }

    internal void ReleaseInput()
    {
        VerifyOwner();
        _pointers.Clear(); _gestureBounds = null; _gestureDocument = default;
        _presenter?.ReleaseInput();
    }

    private void ReturnToSettings()
    {
        CloseEditor();
        _settings.ResumeFromHud();
    }

    private void CloseEditor()
    {
        ReleaseInput();
        _presenter?.Dispose(); _presenter = null;
        _controller?.Dispose(); _controller = null;
    }

    public void Dispose()
    {
        VerifyOwner();
        if (_disposed) return;
        CloseEditor(); _disposed = true;
    }

    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("The Android HUD session belongs to its native UI owner thread.");
    }
}
#endif
