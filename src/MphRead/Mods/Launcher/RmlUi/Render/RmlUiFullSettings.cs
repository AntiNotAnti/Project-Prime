#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Render.Hud;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeFullSettingsPage? _fullSettings;
    private static NativeHudEditorPage? _hudEditor;
    private static bool _settingsReturnToPause, _hudPointerHeld;

    internal static bool OpenNativeCreateLobby()
    {
        if (_controller == null || _controller.Busy || _controller.Joining || _pages?.ModalCount != 0) return false;
        if (!PrepareFullSettingsNavigation()) return false;
        CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense(); CloseNativeCommunity();
        CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
        _controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Play);
        return _controller.Dispatch(RmlUiIntentKind.PlayCreateOpen);
    }

    internal static bool TryOpenNativeFullSettings(RenderWindow window, bool returnToPause = false)
    {
        if (!Requested || _disabled || !Shell.Active || !ReferenceEquals(Shell.Window, window)
            || _pages?.ModalCount > 0 || !TryStart(window) || _host == null || _pages == null) return false;
        if (_fullSettings != null) return true;
        CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense(); CloseNativeCommunity();
        CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
        _launcherPages?.Suspend();
        bool playbackOverlay=Shell.StudioWindow && _playback is { IsOpen:true } playback
            && _pages.Page==playback.Document && _pages.Top==playback.Document;
        var next = new NativeFullSettingsPage(_host, _pages, Shell.RmlUiSettings,
            mode => { if (ReferenceEquals(Shell.Window, window)) { Mods.WindowGeometry.DisplayTrialActive = true; Mods.WindowMode.Set(window, mode); } }, allowGameFiles: !window.HasScene, asModal:playbackOverlay);
        try
        {
            next.Open(); _fullSettings = next; _settingsReturnToPause = returnToPause;
            if (window.HasScene && !playbackOverlay) { _gameplayWindow = window; _gameplayScene = window.Scene; }
            _shown = true; return true;
        }
        catch (Exception error) when (Recoverable(error)) { next.Dispose(); Fail(error); return false; }
    }

    private static bool HandleFullSettings(in RmlUiIntent intent)
    {
        if (_hudEditor is { } hud)
        {
            hud.Handle(intent); TickFullSettings(); return true;
        }
        if (_fullSettings is not { } settings) return false;
        bool setting = intent.Kind is RmlUiIntentKind.SettingsAction or RmlUiIntentKind.SettingsCategory
            or RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsDiscard or RmlUiIntentKind.SettingsClose
            or RmlUiIntentKind.SettingsResetCategory or RmlUiIntentKind.SettingsKeepVideo or RmlUiIntentKind.SettingsRevertVideo;
        if (!setting) return false;
        settings.Handle(intent); TickFullSettings(); return true;
    }

    private static bool PrepareFullSettingsNavigation()
    {
        if (_hudEditor != null) { _hudEditor.Back(); return false; }
        return _fullSettings?.PrepareNavigation() ?? true;
    }

    private static void TickFullSettings()
    {
        if (_fullSettings is not { } settings) return;
        if (_hudEditor is { } hud)
        {
            RefreshHudCanvas(hud);
            hud.Refresh();
            bool accepted = hud.TryTakeAccepted(out HudProfile profile);
            if (accepted || hud.TryTakeCancelled() || hud.Closed)
            {
                _hudEditor = null; _hudPointerHeld = false; hud.Dispose();
                if (accepted) settings.AcceptHud(profile);
                settings.ResumeView();
            }
            return;
        }
        PumpNativeArenaPicker(settings);
        settings.Refresh();
        Mods.WindowGeometry.DisplayTrialActive = settings.ConfirmingVideo;
        if (settings.TryTakeOpenHud(out HudProfile draft))
        {
            settings.SuspendView();
            var next = new NativeHudEditorPage(_host!, _pages!, draft, asModal:settings.AsModal);
            try { next.Open(); _hudEditor = next; }
            catch { next.Dispose(); settings.ResumeView(); throw; }
        }
        else if (settings.TryTakeArenaImport()) StartNativeArenaPicker(settings);
        else if (settings.TryTakeGameFiles())
        {
            CloseFullSettings(); Shell.RmlUiConfigureGameFiles();
        }
        else if (settings.Closed)
        {
            bool pause = _settingsReturnToPause;
            CloseFullSettings();
            if (pause && Shell.Window is { HasScene: true } window) TryOpenGameplayPause(window);
        }
    }

    private static void CloseFullSettings()
    {
        var hud = _hudEditor; _hudEditor = null; _hudPointerHeld = false;
        var settings = _fullSettings; _fullSettings = null; _settingsReturnToPause = false;
        RetireNativeArenaPicker();
        try { hud?.Dispose(); }
        finally { try { settings?.Dispose(); } finally { Mods.WindowGeometry.DisplayTrialActive = false; } }
        if (settings != null) { Shell.RmlUiSettingsClosed(); ReleaseGameplayLeaseIfIdle(); }
    }

    private static bool BackFullSettings()
    {
        if (_hudEditor is { OwnsForeground: true }) { _hudEditor.Back(); TickFullSettings(); return true; }
        if (_fullSettings is not { } settings || !(settings.OwnsForeground || settings.VisualEditorOpen)) return false;
        settings.Back(); TickFullSettings(); return true;
    }

    private static void RefreshHudCanvas(NativeHudEditorPage page)
    {
        if (_host is { } host && page.OwnsForeground
            && host.TryGetElementBounds(page.Document, "hud_canvas", out _, out _, out float width, out float height))
            page.SetCanvasSize(width, height);
    }
    private static bool HudModifier(Keys left, Keys right)
        => _inputWindow is { } window && (window.KeyboardState.IsKeyDown(left) || window.KeyboardState.IsKeyDown(right));
    private static bool HudCanvas(double clientX, double clientY, out float x, out float y)
    {
        x = y = 0;
        if (_hudEditor is not { OwnsForeground: true } page || _host == null || _inputWindow is not { } window
            || !_host.TryGetElementBounds(page.Document, "hud_canvas", out float left, out float top, out float width, out float height)) return false;
        page.SetCanvasSize(width, height);
        x = (float)(clientX * window.FramebufferSize.X / Math.Max(1, window.ClientSize.X)) - left;
        y = (float)(clientY * window.FramebufferSize.Y / Math.Max(1, window.ClientSize.Y)) - top;
        return x >= 0 && y >= 0 && x < width && y < height;
    }
    private static bool HudPointerButton(int button, double x, double y, bool down)
    {
        if (_hudEditor == null || button != 0) return false;
        if (!down && _hudPointerHeld) { _hudEditor.PointerUp(1); _hudPointerHeld = false; return true; }
        if (!down || !HudCanvas(x, y, out float px, out float py)) return false;
        _host!.FocusDocument(_hudEditor.Document, "hud_canvas");
        _hudPointerHeld = _hudEditor.PointerDown(1, px, py, HudModifier(Keys.LeftShift, Keys.RightShift), HudModifier(Keys.LeftAlt, Keys.RightAlt));
        return _hudPointerHeld;
    }
    private static void HudPointerMove(double x, double y)
    {
        if (_hudEditor == null || !_hudPointerHeld) return;
        HudCanvas(x, y, out float px, out float py);
        _hudEditor.PointerMove(1, px, py, HudModifier(Keys.LeftShift, Keys.RightShift), HudModifier(Keys.LeftControl, Keys.RightControl));
    }
}
#endif
