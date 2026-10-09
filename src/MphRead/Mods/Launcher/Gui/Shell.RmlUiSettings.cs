#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Settings;
using MphRead.Mods.Launcher.RmlUi.Setup;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static SettingsPagePresenter? _nativeSettings;
    private static HudEditorPagePresenter? _nativeHud;
    private static HudEditorController? _nativeHudController;
    private static bool _nativeSettingsInGame, _nativeHudPointer;
    private static SetupPagePresenter? _nativeSetup;
    private static bool _nativeInitialSetupApplied, _nativeSplashShown;
    private static NativeUpdateMonitor? _nativeUpdates;
    private static Task? _nativePostSetupPreviews;
    internal static bool NativeKeyCaptureActive => _nativeSettings?.IsCapturingInput == true;
    private static bool NativeSetupBlocksNavigation => _nativeSetup != null && !_nativeSetup.Controller.CanLeave;
    private static bool NativeSettingsForeground => _nativeSettings != null
        && _nativeSettings.Document != default && RmlUiPrototype.Pages?.Manager.Page == _nativeSettings.Document;

    private static void OpenNativeSettings(bool inGame)
    {
        if (RmlUiPrototype.Pages is not { } composition) return;
        _nativeSettingsInGame = inGame;
        _nativeSettings = new(RmlUiPrototype.Runtime, composition.Manager, GameState.LoadSettings(),
            _window?.HasScene == true ? _window.Scene.GameState : GameState.Current,
            CloseNativeSettings, OpenNativeSetup, inGame, _window?.HasScene == true ? _window.Scene.Players : null,
            OpenNativeHud);
        _nativeSettings.Open();
        if (inGame) RmlUiPrototype.ShowGameplayMenu();
    }

    private static void CloseNativeSettings()
    {
        bool inGame = _nativeSettingsInGame;
        RetireNativePages();
        _settings = GameState.LoadSettings();
        if (inGame) OpenNativePause();
        else ReturnNativePage();
    }

    private static void OpenNativeSetup()
    {
        OpenNativeSetupPage(required: !GameFiles.Ready);
    }
    private static bool OpenNativeSetupPage(bool required = false, bool navigate = true)
    {
        if (!RmlUiPrototype.Active || RmlUiPrototype.Pages is not { } composition) return false;
        if (NativeSetupBlocksNavigation) return true;
        if (NativeSettingsForeground)
        { _nativeSettings!.RequestLeave(() => OpenNativeSetupPage(required, navigate)); return true; }
        if (navigate && ApplicationRouter.Navigate(new(LauncherPage.Settings, "setup")) == LauncherNavigationOutcome.Blocked) return true;
        RetireNativePages(); composition.Suspend();
        _nativeSetup = new(RmlUiPrototype.Runtime, composition.Manager, CloseNativeSetup, RequestQuit,
            inGame: _window?.HasScene == true || Network.NetSession.Active, required: required, beforeInstall: () =>
            {
                ReleaseRmlLobby(retirePages: false);
                _rmlMultiplayer?.Cancel();
                Network.NetSession.Stop(); Network.NetHostSession.Stop();
            }, reportFailure: message => RmlUiPrototype.Pages?.ReportSystemNotice(message));
        _nativeSetup.Open();
        WireNativePages();
        if (_window?.HasScene == true) RmlUiPrototype.ShowGameplayMenu();
        else RmlUiPrototype.Show();
        return true;
    }
    private static void CloseNativeSetup()
    {
        bool inGame = _window?.HasScene == true;
        RetireNativePages();
        if (GameFiles.Ready)
        {
            GameFiles.ApplyPaths();
            if (!inGame && !Network.NetSession.Active)
            {
                _rooms = ThumbnailGenerator.MultiplayerRooms();
                _rmlMultiplayer?.Dispose(); _rmlMultiplayer = null;
                _rmlLobbyRules = null;
#if MPHREAD_AVALONIA
                _front?.RefreshDeferredRooms(_rooms);
#endif
                if (ThumbnailHost.CanRender && _nativePostSetupPreviews?.IsCompleted != false)
                {
                    var cancel = _startupWorkCancel?.Token ?? default;
                    _nativePostSetupPreviews = Task.Run(async () =>
                    {
                        try { await ThumbnailHost.RenderMissingAsync(line => DebugLog.Line("thumbnails", line), cancel); }
                        catch (OperationCanceledException) { }
                        catch (Exception error) { DebugLog.Exception("post-setup-previews", error); }
                    }, cancel);
                }
            }
        }
        if (inGame) OpenNativePause();
        else ReturnNativePage();
    }
    private static void EnsureNativeStartupSetup()
    {
        if (_nativeInitialSetupApplied || !RmlUiPrototype.Active) return;
        if (RmlUiPrototype.Pages is { } composition)
        {
            if (composition.Manager.PageKey == "splash") { RmlUi.Presenters.RmlSplashPage.Layout(RmlUiPrototype.Runtime,composition); return; }
            if (!_nativeSplashShown && !RmlUiPrototype.CaptureRequested && !LauncherUiPerformance.Enabled
                && !_nativeCommandLineRouteApplied)
            {
                _nativeSplashShown = true;
                RmlUi.Presenters.RmlSplashPage.Open(RmlUiPrototype.Runtime,composition);
                return;
            }
        }
        _nativeInitialSetupApplied = true;
        if (!GameFiles.Ready && !RmlUiPrototype.CaptureRequested) OpenNativeSetupPage(required: true);
    }

    private static void RetireNativeSettings()
    {
        _nativeHudPointer = false;
        RetireNativeOwner(ref _nativeHud);
        RetireNativeOwner(ref _nativeHudController);
        bool hadSettings = _nativeSettings != null;
        RetireNativeOwner(ref _nativeSettings);
        if (hadSettings) _settings = GameState.LoadSettings();
        _nativeSettingsInGame = false;
        RetireNativeOwner(ref _nativeSetup);
    }

    private static void OpenNativeHud()
    {
        if (_nativeSettings == null || RmlUiPrototype.Pages is not { } composition) return;
        var draft = _nativeSettings.CaptureHudDraft();
        if (!_nativeSettings.SuspendForHud()) return;
        _nativeHudController = new(draft);
        _nativeHud = new(RmlUiPrototype.Runtime, composition.Manager, _nativeHudController);
        _nativeHud.Open();
        WireNativePages();
    }

    private static void TickNativeSettings()
    {
        _nativeSettings?.Refresh();
        _nativeSetup?.Refresh();
        if (!RmlUiPrototype.CaptureRequested && !LauncherUiPerformance.Enabled)
        {
            _nativeUpdates ??= new();
            bool mayPrompt = _window?.HasScene != true && !HasNativePage && _rmlLobby == null
                && _rmlMultiplayer?.Visible != true && _nativeHunters?.Active != true && _nativeAdmin?.Active != true
                && RmlUiPrototype.Pages?.Manager.ModalCount == 0;
            _nativeUpdates.Tick(mayPrompt);
            RmlUiPrototype.Pages?.ObserveRelease(LauncherPrefs.AutoUpdate && !MphRead.Mods.Update.Updater.Disabled
                ? MphRead.Mods.Update.Updater.Available?.Tag : null);
            if (_nativeUpdates.TryTakeAvailable(out var update) && OpenNativeSetupPage())
                _nativeSetup?.OpenLatest(update, automatic: true);
        }
        if (_nativeHud?.IsOpen == true)
        {
            if (RmlUiPrototype.Runtime.TryGetElementBounds(_nativeHud.Document, "hud_canvas",
                out _, out _, out float width, out float height)) _nativeHud.SetCanvasSize(width, height);
            if (RmlUiPrototype.Runtime.FocusedElement() == "hud_canvas" && GamepadContexts.Focused)
                _nativeHud.HandleControllerAxes(GamepadManager.Snapshot, Environment.TickCount64);
            _nativeHud.Present();
        }
        if (_nativeHud?.TryTakeAccepted(out var accepted) == true)
        {
            if (_nativeSettings?.AcceptHudDraft(accepted) != true)
            { _nativeHud.ReportFailure("The HUD draft could not be staged. Correct the settings and retry."); return; }
            ReturnNativeHud();
        }
        else if (_nativeHud?.TryTakeCancelled() == true) ReturnNativeHud();
    }

    private static void ReturnNativeHud()
    {
        _nativeHudPointer = false;
        _nativeHud?.Dispose(); _nativeHud = null;
        _nativeHudController?.Dispose(); _nativeHudController = null;
        _nativeSettings?.ResumeFromHud();
    }

    private static bool NativeHudCoordinates(double x, double y, out float localX, out float localY, out bool inside)
    {
        localX = localY = 0; inside = false;
        if (_nativeHud?.IsOpen != true || _window == null
            || !RmlUiPrototype.Runtime.TryGetElementBounds(_nativeHud.Document, "hud_canvas",
                out float left, out float top, out float width, out float height)) return false;
        var logical = _window.ClientSize; var framebuffer = _window.FramebufferSize;
        localX = (float)(logical.X > 0 ? x * framebuffer.X / logical.X : x) - left;
        localY = (float)(logical.Y > 0 ? y * framebuffer.Y / logical.Y : y) - top;
        inside = localX >= 0 && localY >= 0 && localX < width && localY < height;
        return true;
    }

    private static bool NativeHudPointer(MouseButton button, double x, double y, bool down)
    {
        if (button != MouseButton.Button1 || _nativeHud?.IsOpen != true) return false;
        if (!down)
        {
            bool captured = _nativeHudPointer;
            _nativeHudPointer = false; _nativeHud.PointerUp(0);
            return captured;
        }
        if (!NativeHudCoordinates(x, y, out float localX, out float localY, out bool inside) || !inside) return false;
        RmlUiPrototype.Runtime.FocusDocument(_nativeHud.Document, "hud_canvas");
        _nativeHudPointer = _nativeHud.PointerDown(0, localX, localY,
            NativeModifier(Keys.LeftShift, Keys.RightShift), NativeModifier(Keys.LeftAlt, Keys.RightAlt));
        return true;
    }

    private static bool NativeHudMove(double x, double y)
    {
        if (!_nativeHudPointer || !NativeHudCoordinates(x, y, out float localX, out float localY, out _)) return false;
        _nativeHud!.PointerMove(0, localX, localY, NativeModifier(Keys.LeftShift, Keys.RightShift),
            NativeModifier(Keys.LeftControl, Keys.RightControl));
        return true;
    }
    private static bool NativeModifier(Keys left, Keys right) => _window != null
        && (_window.KeyboardState.IsKeyDown(left) || _window.KeyboardState.IsKeyDown(right));
    private static bool NativeGamepadAction(UiAction action)
    {
        if (_nativeHud?.IsOpen != true) return false;
        bool handled = _nativeHud.HandleController(action, RmlUiPrototype.Runtime.FocusedElement() == "hud_canvas");
        TickNativeSettings();
        return handled;
    }
    private static void ReleaseNativePageInput()
    {
        _nativeHudPointer = false;
        if (_nativeHud?.IsOpen == true) _nativeHud.ReleaseInput();
        _nativePlayback?.Viewport.Release();
    }

    private static void RetireNativePresentation()
    {
        RetireNativeOwner(ref _nativeUpdates);
        _nativePresentationPolicy.Clear();
        RetireNativePages();
    }
}
#endif
