#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static RmlUiWindowsIme? _windowsIme;
    private static RmlUiCocoaIme? _cocoaIme;
    private static RmlUiLinuxIme? _linuxIme;
    private static GamepadUiRouter? _gamepad;
    private static RmlUiDocumentToken _gamepadDocument;
    private static RenderWindow? _inputWindow;
    private static bool NativeInputVisible => CapturesInput && _inputWindow is { IsFocused:true }
        && (!GameplayOpen || GameplayLeaseCurrent);

    private static void AttachNativeInput(RmlUiHost host, RenderWindow window)
    {
        _inputWindow = window;
        _gamepad = new GamepadUiRouter();
        _gamepad.Action += NativeGamepadAction;
        try { _windowsIme = RmlUiDesktopInput.AttachIme(host,window,() => NativeInputVisible); }
        catch (Exception error) when (Recoverable(error)) { }
        try { _cocoaIme = RmlUiDesktopInput.AttachCocoaIme(host,window,() => NativeInputVisible); }
        catch (Exception error) when (Recoverable(error)) { }
        try { _linuxIme = RmlUiLinuxDesktopInput.Attach(host,window,() => NativeInputVisible); }
        catch (Exception error) when (Recoverable(error)) { }
    }

    private static void PumpNativeInput(RenderWindow window, RmlUiHost host)
    {
        if (!ReferenceEquals(window,_inputWindow) || !window.IsFocused || !CapturesInput
            || GameplayOpen && !GameplayLeaseCurrent)
        { ReleaseNativeInput(host); return; }
        if (_gamepadDocument != host.CurrentInputDocument)
        { _gamepad?.Reset(); _gamepadDocument = host.CurrentInputDocument; }
        GamepadDesktop.Poll();
        _gamepad?.Update(GamepadManager.Snapshot,GamepadContext.Menu,Environment.TickCount64);
        _hudEditor?.HandleControllerAxes(GamepadManager.Snapshot, Environment.TickCount64);
        _linuxIme?.Pump();
    }

    private static void RefreshNativeCandidate()
    {
        if (_playback?.Viewport.Focused == true && _host?.FocusedElement() != "replay_viewport")
            _playback.Viewport.Release();
        _windowsIme?.RefreshCandidatePosition();
        _cocoaIme?.RefreshCandidatePosition();
        _linuxIme?.Pump();
    }

    private static void NativeGamepadAction(UiAction action)
    {
        if (!CapturesInput || _host is not { Active:true } host
            || _inputWindow is not { IsFocused:true } || GameplayOpen && !GameplayLeaseCurrent) return;
        if (MphRead.Mods.Launcher.Gui.Shell.RmlUiMatchStartVisible || _fullSettings?.ControllerBindingCaptureActive == true) return;
        if (action == UiAction.Back) { BackNativeMenu(); return; }
        if (host.TryGetTextInputState(out var composition) && composition.Composing) return;
        if (_hudEditor?.HandleController(action, host.FocusedElement() == "hud_canvas") == true) return;
        bool text = host.TextInputActive;
        int key = action switch
        {
            UiAction.Accept => 2,
            UiAction.Left when text => 7, UiAction.Right when text => 8,
            UiAction.PageUp => 11, UiAction.PageDown => 12,
            _ => 1
        };
        RmlUiInputModifiers modifiers = action is UiAction.Up or UiAction.PreviousTab
            || action == UiAction.Left && !text ? RmlUiInputModifiers.Shift : default;
        var scope = host.CurrentInputDocument;
        host.Input.Dispatch(new(scope,RmlUiPlatformInputKind.KeyDown,RmlUiInputDevice.Gamepad,Code:key,Modifiers:modifiers));
        host.Input.Dispatch(new(scope,RmlUiPlatformInputKind.KeyUp,RmlUiInputDevice.Gamepad,Code:key,Modifiers:modifiers));
    }

    private static void CancelNativeComposition()
    {
        _windowsIme?.Cancel(); _cocoaIme?.Cancel(); _linuxIme?.Cancel();
        if (_host is { Active:true } host && host.TryGetTextInputState(out var state) && state.Composing)
            host.Input.Dispatch(new(state.Document,RmlUiPlatformInputKind.CompositionCancel,
                RmlUiInputDevice.InputMethod,state.FocusEpoch));
    }

    private static void ReleaseNativeInput(RmlUiHost? host = null, bool revertDisplayTrial = true)
    {
        _hunterChoice?.CancelPreviewGesture();
        _pendingReplayTimeline=null;
        _playback?.ReleaseTimelineInput();
        _hudEditor?.ReleaseInput(); _hudPointerHeld = false;
        _fullSettings?.CancelInputCapture(revertDisplayTrial);
        _playback?.Viewport.Release();
        _studioEditor?.ReleaseInput();
        CancelNativeComposition();
        _gamepad?.Reset(); _gamepadDocument = default;
        (host ?? _host)?.ReleaseInput();
    }

    internal static void NativeFocusChanged(bool focused)
    {
        _gamepad?.Reset(); _gamepadDocument = default;
        if (!focused) _campaignEnding?.Suspend();
        if (!focused && _host is { Active:true } host) ReleaseNativeInput(host);
    }

    internal static void NativeViewportChanged()
    {
        // A held gesture belongs to the old window coordinates and native layout.
        // Retire it before another event or camera poll can use those coordinates.
        if (_host is { Active:true } host)
        {
            // A display proposal changes its own viewport. Keep the trial alive
            // across usable resizes while retiring gestures in old coordinates.
            // Focus loss and a zero-sized viewport still revoke the proposal.
            bool unusable = _inputWindow == null || _inputWindow.FramebufferSize.X <= 0
                || _inputWindow.FramebufferSize.Y <= 0;
            if (unusable) _campaignEnding?.Suspend();
            ReleaseNativeInput(host, revertDisplayTrial: unusable);
        }
    }

    private static void DisposeNativeInput()
    {
        var windows = _windowsIme; var cocoa = _cocoaIme; var linux = _linuxIme;
        _windowsIme = null; _cocoaIme = null; _linuxIme = null;
        _gamepad = null; _gamepadDocument = default; _inputWindow = null;
        try { windows?.Dispose(); } catch (Exception error) when (Recoverable(error)) { }
        try { cocoa?.Dispose(); } catch (Exception error) when (Recoverable(error)) { }
        try { linux?.Dispose(); } catch (Exception error) when (Recoverable(error)) { }
    }

    private static void BackNativeMenu()
    {
        if (NativeStartupOpen) return;
        if (_quitDocument != default) { CloseNativeQuit(); return; }
        if (BackNativeVersions()) return;
        if (BackFullSettings()) return;
        if (NativeUpdateOpen) { _updatePrompt?.Back(); CompleteNativeUpdate(); return; }
        if (_host is not { Active:true } host) return;
        if (host.TryGetTextInputState(out var state) && state.Composing)
        { CancelNativeComposition(); return; }
        if (_license != null) { _license.Back(); if (_license.Closed) CloseNativeLicense(); return; }
        if (BackNativeGameFiles()) return;
        if (GameplayOpen) { BackGameplayOverlay(); return; }
        if (_campaignEnding != null) { _campaignEnding.Back(); ApplyCampaignEndingReturn(); return; }
        if (_campaignShip != null) { _campaignShip.Back(); ApplyCampaignShipEffect(); return; }
        if (BackNativeStudioRoute()) return;
        if (_party != null)
        {
            _party.Back();
            if (_party.Closed) { CloseNativeSocial(); OpenNativeSocial(host); }
            return;
        }
        if (_social != null) { _social.Back(); if (_social.Closed) CloseNativeSocial(); return; }
        if (_quickSettings != null) { _quickSettings.Back(); if (_quickSettings.Closed) CloseNativeQuickSettings(); return; }
        if (_hunterChoice != null) { _hunterChoice.Back(); if (_hunterChoice.Closed) CloseNativeHunter(); return; }
        if (_news != null) { if (!_news.Back()) CloseNativeNews(); return; }
        if (_community != null) { if (!_community.Back()) CloseNativeCommunity(); return; }
        if (_studio != null) { CloseNativeStudio(); return; }
        if (_launcherPages is { Suspended:false } launcher && launcher.Back(out var intent)
            && intent.Kind != 0)
        {
            if (intent.Kind == RmlUiIntentKind.Quit) OpenNativeQuit();
            else _controller?.Dispatch(intent.Kind,intent.Argument);
        }
    }
}
#endif
