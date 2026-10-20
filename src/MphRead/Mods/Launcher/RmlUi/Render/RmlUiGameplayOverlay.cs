#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Campaign;
using MphRead.Formats;
using MphRead.Text;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static InGameController? _pauseController;
    private static InGamePagePresenter? _pause;
    private static MatchResultsController? _matchResultsController;
    private static MatchResultsPagePresenter? _matchResults;
    private static AimResultsController? _aimResultsController;
    private static AimResultsPagePresenter? _aimResults;
    private static Action<AimResultsAction>? _aimResultAction;
    private static Scene? _gameplayScene;
    private static RenderWindow? _gameplayWindow;
    private static bool GameplayLeaseCurrent => _gameplayWindow is { HasScene: true } window
        && (_playback != null ? NativeReplayLeaseCurrent(window) : ReferenceEquals(_gameplayScene, window.Scene));
    internal static bool CampaignMenuAudio => _campaignEnding != null || _campaignShip != null;
    internal static bool GameplayOpen => _settingsReturnToPause && _gameplayWindow is { HasScene: true } && (_fullSettings != null || _hudEditor != null) || _pause != null || _matchResults != null || _aimResults != null || _playback != null;
    internal static bool NativePauseOpen => _pause != null;
    internal static bool NativeMatchResultsOpen => _matchResults != null;
    internal static bool NativeAimResultsOpen => _aimResults != null;

    internal static bool TryOpenGameplayPause(RenderWindow window)
    {
        if (!Requested || _disabled || !window.HasScene || !Shell.Active) return false;
        if (_gameplayScene != null && !ReferenceEquals(_gameplayScene, window.Scene)) CloseGameplayOverlays();
        if (_pause != null) return true;
        if (!TryStart(window) || _host == null || _pages == null) return false;
        try
        {
            CloseNativeMatchResults();
            CloseNativeAimResults();
            CloseNativePlayback();
            CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense();
            CloseNativeCommunity(); CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
            _launcherPages?.Suspend();
            RmlUiHunterStageRenderer.Hide();
            var controller = new InGameController(InGameController.CreateEngineBackend(window.Scene));
            _pauseController = controller;
            CampaignJournal? journal = null;
            if (CampaignJournalView.Available(window.Scene.Players))
            {
                System.Collections.Generic.IReadOnlyList<StringTableEntry>? entries = null;
                try { entries = Strings.ReadStringTable(StringTables.ScanLog); }
                catch (Exception error) when (Recoverable(error))
                { Mods.DebugLog.Line("rmlui", "scan logbook text unavailable: " + error.Message); }
                journal = CampaignJournal.Capture(window.Scene.GameState.StorySave, entries);
            }
            _pause = new InGamePagePresenter(_host, _pages, controller, journal);
            _pause.Open();
            _gameplayScene = window.Scene;
            _gameplayWindow = window;
            _shown = true;
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
            return false;
        }
    }

    internal static bool TryOpenNativeMatchResults(RenderWindow window)
    {
        // A recorded post-match world cannot retire the active replay page or acquire interactive results.
        if (MphRead.Mods.Network.DemoPlayback.IsActive) return false;
        if (window.HasScene && _gameplayScene != null && !ReferenceEquals(_gameplayScene, window.Scene)) CloseGameplayOverlays();
        if (!Requested || _disabled || !window.HasScene || !Shell.Active || NativePauseOpen || NativeAimResultsOpen) return false;
        if (_matchResults != null) return true;
        if (!TryStart(window) || _host == null || _pages == null) return false;
        try
        {
            _launcherPages?.Suspend();
            _matchResultsController = new MatchResultsController();
            _matchResults = new MatchResultsPagePresenter(_host, _pages, _matchResultsController);
            _matchResults.Open();
            _gameplayScene = window.Scene;
            _gameplayWindow = window;
            _shown = true;
            EndScreen.PanelUp = true;
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); return false; }
    }

    internal static void CloseNativeMatchResults()
    {
        var page = _matchResults; var controller = _matchResultsController;
        _matchResults = null; _matchResultsController = null;
        try { page?.Dispose(); }
        finally
        {
            try { controller?.Dispose(); }
            finally { if (page != null) EndScreen.PanelUp = false; ReleaseGameplayLeaseIfIdle(); }
        }
    }

    internal static bool TryOpenNativeAimResults(RenderWindow window, AimResultsReport report, Action<AimResultsAction> action)
    {
        if (window.HasScene && _gameplayScene != null && !ReferenceEquals(_gameplayScene, window.Scene)) CloseGameplayOverlays();
        if (!Requested || _disabled || !window.HasScene || !Shell.Active) return false;
        if (_aimResults != null) return true;
        if (!TryStart(window) || _host == null || _pages == null) return false;
        try
        {
            CloseGameplayPause(); CloseNativeMatchResults(); CloseNativePlayback();
            _launcherPages?.Suspend();
            _aimResultsController = new AimResultsController(report);
            _aimResults = new AimResultsPagePresenter(_host, _pages, _aimResultsController);
            _aimResultAction = action;
            _aimResults.Open();
            _gameplayScene = window.Scene;
            _gameplayWindow = window;
            _shown = true;
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); return false; }
    }

    private static void CloseNativeAimResults()
    {
        var page = _aimResults; var controller = _aimResultsController;
        _aimResults = null; _aimResultsController = null; _aimResultAction = null;
        try { page?.Dispose(); }
        finally
        {
            try { controller?.Dispose(); }
            finally { ReleaseGameplayLeaseIfIdle(); }
        }
    }

    private static void CloseGameplayOverlays()
    {
        // Native retirement can fail while handling an earlier native error.
        // Retire every owner independently so failure recovery still reaches
        // the existing toolkit fallback and releases the native host.
        Cleanup(CloseFullSettings);
        Cleanup(() => CloseGameplayPause());
        Cleanup(CloseNativeMatchResults);
        Cleanup(CloseNativeAimResults);
        Cleanup(CloseNativePlayback);
        _gameplayScene = null; _gameplayWindow = null;

        static void Cleanup(Action close)
        {
            try { close(); }
            catch (Exception error) when (Recoverable(error))
            {
                try { Mods.DebugLog.Line("rmlui", "gameplay overlay teardown: " + error.Message); }
                catch { }
            }
        }
    }

    private static void ReleaseGameplayLeaseIfIdle()
    {
        if (!GameplayOpen) { _gameplayScene = null; _gameplayWindow = null; }
    }

    internal static bool CloseGameplayPause(bool markClosed = true)
    {
        var page = _pause; var controller = _pauseController;
        _pause = null; _pauseController = null;
        if (page == null && controller == null) return false;
        try { page?.Dispose(); }
        finally
        {
            try { controller?.Dispose(); }
            finally
            {
                try { if (_host is { Active: true } host) ReleaseNativeInput(host); }
                finally
                {
                    _shown = false;
                    if (markClosed) PauseMenu.MarkClosed();
                    ReleaseGameplayLeaseIfIdle();
                }
            }
        }
        return true;
    }

    private static void DrawGameplayOverlay(RenderWindow window)
    {
        RmlUiHunterStageRenderer.Hide();
        if (_playback != null && !ReferenceEquals(_gameplayWindow, window)) return;
        if (_gameplayScene != null && (_playback != null ? !NativeReplayLeaseCurrent(window)
            : !ReferenceEquals(_gameplayScene, window.Scene))) CloseGameplayOverlays();
        if (_disabled || !GameplayOpen || _host is not { Active: true } host)
        {
            if (_shown && _host is { Active: true } previous) ReleaseNativeInput(previous);
            _shown = false;
            return;
        }
        try
        {
            CompleteNativeReplayEditor(window);
            int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
            if (width <= 0 || height <= 0) { ReleaseNativeInput(host); _shown = false; return; }
            float density = Density(window);
            if (_width != width || _height != height || _density != density)
            {
                NativeViewportChanged();
                host.Resize(width, height, density);
                _width = width; _height = height; _density = density;
            }
            host.Input.SetFramebufferScale((float)width / Math.Max(1, window.ClientSize.X),
                (float)height / Math.Max(1, window.ClientSize.Y));
            _shown = true;
            PumpNativeInput(window,host);
            for (int i = 0; i < 64 && host.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                if (HandleFullSettings(intent)) continue;
                _pause?.Handle(intent);
                _matchResults?.Handle(intent);
                _aimResults?.Handle(intent);
                _playback?.HandleIntent(intent);
                ApplyGameplayEffects();
                if (!GameplayOpen) return;
            }
            TickFullSettings();
            _pause?.Refresh();
            _matchResults?.Refresh();
            _playback?.Present();
            ApplyGameplayEffects();
            if (!GameplayOpen) return;
            PresentNativeFeedback(host);
            host.Update();
            RefreshNativeCandidate();
            var timelinePage=_playback;
            var timelineMetrics=ReplayTimelineMetrics(window);
            var timelineTicket=timelinePage?.PrepareTimeline(timelineMetrics,window.IsFocused);
            RmlUiOpenGlFrame.Draw(host, width, height);
            if (window.IsFocused && width > 0 && height > 0)
                _fullSettings?.AcknowledgeUsableFrame();
            QueueReplayTimeline(window,timelinePage,timelineTicket,timelineMetrics);
        }
        catch (Exception error) when (Recoverable(error))
        {
            RecoverGameplayPresentation(error);
        }
    }

    private static void RecoverGameplayPresentation(Exception error)
    {
        // Shutdown retires the native controller and callback. Capture the
        // exact immutable report and its original scene owner first.
        var window = _gameplayWindow;
        bool current = GameplayLeaseCurrent;
        // Recovery's existing Shell boundary expects the exact displayed scene.
        // Capture it while the canonical window/request receipt is still current.
        var scene = _playback != null && current ? window?.Scene : _gameplayScene;
        string? replay = _playback != null ? MphRead.Mods.Network.DemoPlayback.LogicalPath : null;
        AimResultsReport? report = _aimResultsController?.Report;
        Action<AimResultsAction>? action = _aimResultAction;
        Fail(error);
        if (current && window != null && scene != null && report != null && action != null
            && Shell.OpenNativeAimResultsHandoff(window, scene, report, action)) return;
        if (current && window != null && scene != null && replay != null
            && Shell.RecoverReplayPresentation(window,scene,replay)) return;
        Shell.OpenNativePauseHandoff(InGameEffect.None);
    }

    private static void BackGameplayOverlay()
    {
        if (BackFullSettings()) return;
        if (_pause != null) _pause.Back();
        else if (_aimResults != null) _aimResults.Back();
        else if (_playback != null)
        {
            if (!_playback.Back() && !ReleaseNativeReplayCamera()) _playbackController?.Dispatch(TheatrePlaybackAction.Back);
        }
        else _matchResults?.Back();
        ApplyGameplayEffects();
    }

    private static void ApplyGameplayEffects()
    {
        ApplyPlaybackEffects();
        ApplyPauseEffect();
        if (_aimResults is { } aim && aim.TryTakeAction(out AimResultsAction action))
        {
            var callback = _aimResultAction;
            CloseNativeAimResults();
            _shown = false;
            callback?.Invoke(action);
        }
        if (_matchResults is { } results && results.TryTakeEffect(out MatchResultsEffect effect))
        {
            CloseNativeMatchResults();
            _shown = false;
            if (effect == MatchResultsEffect.Close) Shell.OpenPauseMenu();
        }
    }

    private static void ApplyPauseEffect()
    {
        if (_pause == null) return;
        if (_pause.TryTakeEffect(out InGameEffect effect))
        {
            bool handoff = effect is InGameEffect.Settings or InGameEffect.Replay;
            CloseGameplayPause(markClosed: !handoff);
            if (handoff && !(effect == InGameEffect.Replay && Shell.Window is { } window
                && TryOpenNativePlayback(window))) Shell.OpenNativePauseHandoff(effect);
        }
        else if (!_pause.Active) CloseGameplayPause();
    }
}
#endif
