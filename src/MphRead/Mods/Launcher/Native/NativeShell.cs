#if MPHREAD_RMLUI && !MPHREAD_AVALONIA && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Desktop engine-window adapter for the opt-in toolkit-free client. The native
/// page partials and Core controllers are shared with the migration presenter;
/// this compile alternative owns no hidden toolkit screens or dispatcher.
/// </summary>
internal static partial class Shell
{
    public static bool Active { get; private set; }
    public static bool UiVisible => RmlUiPrototype.Visible;
    internal static RenderWindow? Window => _window;
    private static RenderWindow? _window;
    private static RmlMultiplayerController? _rmlMultiplayer;
    private static RmlLobbyRulesEditor? _rmlLobbyRules;
    private static MenuSettings _settings = new();
    private static IReadOnlyList<string> _rooms = Array.Empty<string>();
    private static LaunchPlan? _pending, _played;
    private static bool _endMatch, _quit, _matchLoading, _focusTrainingOnReturn;
    private static CancellationTokenSource? _startupWorkCancel;
    private static int _firstFrameStarted;
    private static readonly ConcurrentQueue<(IReadOnlyList<MapGen.MapDefinition> Definitions, CancellationToken Token)> _startupCatalog = new();
    private static IReadOnlyList<MapGen.MapDefinition>? _deferredCustomRoomsPending;
    private static CancellationToken _deferredCustomRoomsToken;

    public static bool Run()
    {
        bool result = RunSession();
        if (GraphicsBackendPolicy.Requested == GraphicsBackend.Auto
            && ModernGraphicsCompat.RecoveryFailure is Exception failure)
            return RendererCompatibilityRestart.Start(failure.Message);
        return result;
    }

    private static bool RunSession()
    {
        LifecycleTiming.Startup("native client session begin");
        LauncherPrefs.Load();
        _nativeCapturePageApplied = false;
        _nativeCommandLineRouteApplied = false;
        _nativeInitialSetupApplied = false;
        if (!RmlUiPrototype.CaptureRequested && !LauncherUiPerformance.Enabled) SocialRuntime.Start();
        _settings = GameState.LoadSettings();
        Mods.GameSettings.Apply(_settings);
        Interlocked.Exchange(ref _firstFrameStarted, 0);
        LauncherPhoto.Enabled = true;
        if (GameFiles.Ready) GameFiles.ApplyPaths();
        if (!WindowMode.StartupForced) WindowMode.Startup = LauncherPrefs.WindowMode;
        RenderWindow.LogCreatingWindow();
        RenderWindow? window = null;
        bool completed = false;
        try
        {
            window = RenderWindow.Create(shell: true);
            _window = window;
            window.FileDrop += OnFilesDropped;
            PublishNativeHandle(window);
            Active = true;
            OfflineRematch.StartNext = PlayAnother;
            if (!RmlUiPrototype.TryActivate(window))
                throw new InvalidOperationException("The native RmlUi runtime could not initialize. Check the matched native library, page assets and fonts in this installation.");
            WireNativePages();
            LifecycleTiming.Startup("native client front screen ready");
            window.Run();
            completed = true;
            return true;
        }
        catch (Exception error)
        {
            if (!RmlUiPrototype.Failed)
                LauncherUiRuntime.RecordNativeFailure(RmlUiPrototype.Active
                    ? LauncherUiFailure.Runtime : LauncherUiFailure.Initialization);
            Console.Error.WriteLine("[launcher] " + error.Message);
            DebugLog.Exception("native-launcher", error);
            return _quit;
        }
        finally
        {
            bool processEnding = completed || _quit;
            if (processEnding)
            {
                LifecycleTiming.BeginShutdown("native client session ending");
                ReplayWritePump.BeginProcessShutdown();
                MphRead.Sound.AudioLifetime.BeginShutdown();
            }
            var startup = _startupWorkCancel;
            _startupWorkCancel = null;
            startup?.Cancel(); startup?.Dispose();
            while (_startupCatalog.TryDequeue(out _)) { }
            _deferredCustomRoomsPending = null;
            _deferredCustomRoomsToken = default;
            Active = false;
            OfflineRematch.StartNext = null;
            _pending = null;
            _endMatch = _quit = _matchLoading = false;
            NetSession.Stop(); NetHostSession.Stop();
            if (window != null)
            {
                window.FileDrop -= OnFilesDropped;
                if (!GraphicsBackendPolicy.ModernGameplayRequested) window.Context.MakeCurrent();
            }
            ReleaseRmlLobby();
            _nativeReturnTheatre?.Dispose(); _nativeReturnTheatre = null;
            RmlUiPrototype.Shutdown();
            _rmlMultiplayer?.Dispose(); _rmlMultiplayer = null;
            _rmlLobbyRules = null;
            SocialRuntime.Stop();
            ReleaseApplicationRouter();
            _window = null;
            try { window?.Dispose(); }
            finally { ModernGraphicsCompat.Shutdown(); }
            if (processEnding) LifecycleTiming.Shutdown("native client window disposed");
        }
    }

    private static void OnFilesDropped(FileDropEventArgs e)
    {
        foreach (string path in e.FileNames?.ToArray() ?? Array.Empty<string>()) QueueNativeDroppedDocument(path);
    }
    private static unsafe void PublishNativeHandle(RenderWindow window)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { NativeFilePicker.Owner = GLFW.GetWin32Window(window.WindowPtr); }
        catch (Exception error) { DebugLog.Line("native-picker", error.Message); }
    }

    internal static void BeforeFrame(RenderWindow window)
    {
        if (!Active) return;
        StudioIntegration.GameStudioIntegration.PumpOwnerThread(window);
        while (_startupCatalog.TryDequeue(out var catalog)) PublishDeferredCustomRooms(catalog.Definitions, catalog.Token);
        if (!window.HasScene && _deferredCustomRoomsPending is { } pendingCatalog)
            PublishDeferredCustomRooms(pendingCatalog, _deferredCustomRoomsToken);
        ApplyNativeCapturePage(); EnsureNativeStartupSetup(); PumpNativeRoutes();
        if (RmlUiPrototype.Failed)
        {
            DebugLog.Line("native-launcher", "Native presentation failed; closing the client window.");
            RequestQuit();
        }
        while (RmlUiPrototype.Visible && RmlUiPrototype.TryTakeIntent(out var intent))
        {
            if (HandleNativePageIntent(intent)) continue;
            string command = RmlUiIntentRegistry.ToLegacy(intent);
            if (command.StartsWith("lobby:rules-", StringComparison.Ordinal)) HandleRmlLobbyRulesCommand(command);
            else if (command.StartsWith("lobby:", StringComparison.Ordinal))
            {
                switch (command)
                {
                    case "lobby:ready": DispatchRmlLobby(LobbyIntentKind.ToggleReady); break;
                    case "lobby:start": DispatchRmlLobby(LobbyIntentKind.StartMatch); break;
                    case "lobby:leave": _rmlLobbyRules?.ResetSession(); DispatchRmlLobby(LobbyIntentKind.Leave); break;
                    case "lobby:next-hunter": DispatchRmlLobby(LobbyIntentKind.NextHunter); break;
                    case "lobby:next-suit": DispatchRmlLobby(LobbyIntentKind.NextSuit); break;
                    case "lobby:classic": OpenLegacyRmlLobby(); break;
                }
            }
            else if (command.StartsWith("play:", StringComparison.Ordinal)) HandleRmlPlayCommand(command);
            else if (command == "quit") RequestQuit();
            else if (command == "studio:open") OpenNativePage(LauncherPage.StudioLaunch);
        }
        if (window.HasScene && window.Scene.AimTrainer is { Completed: true, ResultsShown: false } training)
            OpenNativeAimResults(training);
        if (_quit) { window.Close(); return; }
        if (window.HasScene && NetSession.PersistentLobby && NetSession.IsInLobby && !_endMatch) EndNetworkMatchToLobby(window);
        if (window.HasScene && (NetSession.Refused || NetSession.SessionTimedOut)) _endMatch = true;
        if (_endMatch) { _endMatch = false; EndMatch(window); }
        if (_pending is LaunchPlan plan)
        {
            if (!ValidateRmlPendingPlan()) { _pending = null; return; }
            bool roomLaunch = plan.Kind is LaunchKind.Online or LaunchKind.Offline or LaunchKind.Host or LaunchKind.AimTrainer;
            if (roomLaunch && !String.IsNullOrWhiteSpace(plan.RoomKey) && RoomPrewarm.Begin(plan.RoomKey)
                && !RoomPrewarm.TryGetPreparationResult(plan.RoomKey, out _)) return;
            _pending = null;
            StartMatch(window, plan);
        }
        if (window.HasScene && _played?.Kind == LaunchKind.Demo && DemoPlayback.IsActive
            && DemoPlayback.LastResult != ReplayOpenResult.Success && _nativePlayback == null)
            OpenNativePlayback();
        if (_matchLoading && window.HasScene)
        {
            if (!NetSession.FreezeGameplay) { _matchLoading = false; RmlUiPrototype.Hide(); }
            else if (NetSession.Refused || NetSession.SessionTimedOut) { _matchLoading = false; _endMatch = true; }
        }
    }

    internal static void TickUi(RenderWindow window)
    {
        if (!RmlUiPrototype.Visible) { UiOverlay.Visible = false; return; }
        if (RmlUiPrototype.LobbyMode) { PumpRmlLobby(); _rmlLobbyRules?.Tick(); }
        else _rmlLobbyRules?.ResetSession();
        UiOverlay.Visible = false;
        RmlUiPrototype.Tick(window);
        _rmlMultiplayer?.Tick(); TickNativePages();
    }
    public static bool EndPanelUp => _nativeResults?.Active == true;
    internal static void TickEndPanel() => TickNativeEndPanel();
    internal static void AfterDraw(RenderWindow window)
    {
        if (RmlUiPrototype.Visible && HasNativeHunterPreview)
            DrawNativeHunterPreview(window, window.FramebufferSize.X, window.FramebufferSize.Y);
        if (Interlocked.Exchange(ref _firstFrameStarted, 1) == 0) { LifecycleTiming.FirstFrame(); StartBackgroundStartupWork(); }
        RmlUiPrototype.AfterDraw(window);
    }

    private static RmlLobbyRulesEditor EnsureRmlLobbyRules()
    {
        _rmlLobbyRules ??= new(_rooms.Count != 0 ? _rooms : GameFiles.Ready ? ThumbnailGenerator.MultiplayerRooms() : Array.Empty<string>());
        _rmlLobbyRules.BindSession(_rmlLobby); return _rmlLobbyRules;
    }
    private static void HandleRmlLobbyRulesCommand(string command)
    {
        if (!RmlUiPrototype.LobbyMode) return;
        var editor = EnsureRmlLobbyRules();
        switch (command)
        {
            case "lobby:rules-open": editor.Open(); return;
            case "lobby:rules-close": editor.Close(); return;
            case "lobby:rules-map": editor.CycleMap(); return;
            case "lobby:rules-mode": editor.CycleMode(); return;
            case "lobby:rules-format": editor.CycleFormat(); return;
            case "lobby:rules-apply": editor.Apply(RmlUiPrototype.ReadFieldValue("rules_time"), RmlUiPrototype.ReadFieldValue("rules_goal")); return;
        }
        const string prefix = "lobby:rules-toggle:";
        if (command.StartsWith(prefix, StringComparison.Ordinal) && Int32.TryParse(command[prefix.Length..], out int index)) editor.Toggle(index);
    }
    private static RmlMultiplayerController EnsureRmlMultiplayer()
    {
        if (_rmlMultiplayer != null) return _rmlMultiplayer;
        var controller = new RmlMultiplayerController(_rooms.Count != 0 ? _rooms : GameFiles.Ready ? ThumbnailGenerator.MultiplayerRooms() : Array.Empty<string>());
        controller.Connected += AcceptRmlMultiplayerPlan;
        return _rmlMultiplayer = controller;
    }
    private static void HandleRmlPlayCommand(string command)
    {
        if (command == "play:cancel") { _rmlMultiplayer?.Cancel(); ApplicationRouter.Back(); return; }
        if (!RmlUiPrototype.LobbyMode) ApplicationRouter.Navigate(new(LauncherPage.Play));
        var controller = EnsureRmlMultiplayer();
        switch (command)
        {
            case "play:quick": if (!controller.Visible) controller.Open(quickPlay: true); else controller.QuickPlay(); break;
            case "play:browse": if (!controller.Visible) controller.Open(quickPlay: false); else controller.Browse(); break;
            case "play:create-open": controller.OpenCreate(); break;
            case "play:next-map": controller.NextMap(); break;
            case "play:next-mode": controller.NextMode(); break;
            case "play:toggle-host": controller.ToggleHost(); break;
            case "play:create": controller.Create(RmlUiPrototype.ReadFieldValue("play_create_name"), RmlUiPrototype.ReadFieldValue("play_create_player_name")); break;
            case "play:join": controller.JoinEndpoint(RmlUiPrototype.ReadFieldValue("play_join_address"), spectate: false); break;
            default:
                if (command.StartsWith("play:server:", StringComparison.Ordinal) && Int32.TryParse(command["play:server:".Length..], out int index)) controller.JoinSelected(index);
                break;
        }
    }
    private static void AcceptRmlMultiplayerPlan(LaunchPlan plan)
    {
        _nativeSocialJoin = false; _rmlMultiplayer?.Cancel();
        if (NetSession.Active && NetSession.PersistentLobby) { OpenRmlLobby(plan); return; }
        RmlUiPrototype.Hide(); Decided(plan);
    }
    private static void Decided(LaunchPlan plan)
    {
        if (plan.Kind == LaunchKind.None) { RequestQuit(); return; }
        _pendingLobbyController = _rmlLobby;
        if (plan.Kind == LaunchKind.Online && _pendingLobbyController is { } lobby)
        {
            var state = lobby.Snapshot();
            _rmlPendingStart = (state.Lifetime, state.MatchId, state.AuthorityEpoch, state.StartGeneration);
        }
        else { _pendingLobbyController = null; _rmlPendingStart = null; }
        _pending = plan;
    }

    public static bool CanPlayAnother => Active && _pending == null && !NetSession.Active && _played is { Kind: LaunchKind.Offline };
    public static bool PlayAnother(string room)
    {
        if (!CanPlayAnother || _played is not LaunchPlan plan || !OfflineRematch.TryPlan(plan, room, out var next)) return false;
        _endMatch = true; _pending = next; return true;
    }
    private static void StartMatch(RenderWindow window, LaunchPlan plan)
    {
        _played = plan; _rmlLobby?.Suspend(); _matchLoading = false;
        try
        {
            if (!MatchStart.Begin(window, _settings, plan)) { FailNativeMatch(window, plan, MatchStart.LastError ?? "The map could not be loaded."); return; }
            _rmlLobby?.YieldPumpToGameplay(); CompleteNativePageLaunch(plan);
            if (NetSession.PersistentLobby && NetSession.IsStarting) _matchLoading = true;
            else if (plan.Kind == LaunchKind.Demo && _nativePlayback?.IsOpen == true) RmlUiPrototype.ShowGameplayMenu();
            else RmlUiPrototype.Hide();
        }
        catch (Exception error) { DebugLog.Exception("native-match", error); FailNativeMatch(window, plan, error.Message); }
    }
    private static void FailNativeMatch(RenderWindow window, LaunchPlan plan, string error)
    {
        NetSession.ReportMatchLoadFailed(error);
        if (RecoverNativeLaunchFailure(window, error)
            || RecoverNativeTrainingLaunchFailure(window, plan, error)) return;
        EndMatch(window);
        RmlUiPrototype.SetMenuText("system_status", error);
    }
    private static void FullscreenReplay(RenderWindow window)
    {
        RmlUiPrototype.Hide();
        try { window.Focus(); } catch (Exception error) { DebugLog.Line("replay-focus", error.Message); }
    }
    private static void EndNetworkMatchToLobby(RenderWindow window)
    {
        _matchLoading = false; CloseMenu(); window.EndScene(); MatchStart.AfterMatch();
        NetSession.ResetMatchState(); PauseMenu.Reset();
        if (_rmlLobby is not { } lobby) { EndMatch(window); return; }
        lobby.Resume(); lobby.TransferPumpOwnership(LobbyPumpOwner.Native);
        RmlUiPrototype.PresentLobby(lobby.Snapshot());
        if (!RmlUiPrototype.EnterLobby(window, lobby.Snapshot().Context?.ServerName, lobby.Snapshot().Context?.Endpoint)) { RequestQuit(); return; }
        RmlUiPrototype.Show();
    }
    private static void EndMatch(RenderWindow window)
    {
        _matchLoading = false; CloseMenu(); window.EndScene();
        NetSession.Stop(); NetHostSession.Stop(); MatchStart.AfterMatch(); ReleaseRmlLobby();
        if (!TryRestoreRmlHome(window)) { RequestQuit(); return; }
        if (_nativeReturnTheatre != null) { _nativeReturnTheatre.ResumeAfterPlayback(); OpenNativePage(LauncherPage.Theatre); }
        else if (_played?.Kind == LaunchKind.AimTrainer && _focusTrainingOnReturn)
        { OpenNativePage(LauncherPage.Offline); _nativeOffline?.FocusTraining(); }
        _focusTrainingOnReturn = false;
    }
    public static void RequestEndMatch() { if (Active) _endMatch = true; }
    public static void RequestQuit()
    {
        LifecycleTiming.BeginShutdown("quit requested"); _startupWorkCancel?.Cancel();
        ReplayWritePump.BeginProcessShutdown(); MphRead.Sound.AudioLifetime.BeginShutdown(); _quit = true;
    }
    internal static void LeaveMatch(GameWindow window) { if (Active) RequestEndMatch(); else window.Close(); }
    internal static void Quit(GameWindow window) { if (Active) RequestQuit(); else window.Close(); }
    internal static bool OpenPauseMenu() => OpenNativePause();
    internal static void CloseMenu() { if (CloseNativePause()) return; PauseMenu.MarkClosed(); }

    // The authored "Other options" action uses the native administration page
    // in this client, with the existing lobby controller and authority witnesses.
    private static void OpenLegacyRmlLobby()
    {
        if (!RmlUiPrototype.Active || RmlUiPrototype.Failed) { RequestQuit(); return; }
        if (_rmlLobby == null) return;
        _rmlLobbyRules?.Close();
        _nativeHunters?.Dispose(); _nativeHunters = null;
        _nativeAdmin?.Dispose();
        _nativeAdmin = new(RmlUiPrototype.Runtime, _rmlLobby);
        _nativeAdmin.Open(); WireNativePages();
    }
    private static bool RestoreLegacyRmlPresentation()
    {
        RequestQuit(); return false;
    }

    public static void PointerMoved(double x, double y)
    {
        if (NativeHudMove(x, y)) return;
        _nativePlayback?.Viewport.PointerMoveInWindow(x, y); RmlUiPrototype.PointerMoved(x, y);
    }
    public static void PointerButton(MouseButton button, double x, double y, bool down)
    {
        if (down && _nativeSettings?.TryCaptureMouse(button) == true) return;
        if (NativeHudPointer(button, x, y, down) || NativeReplayPointer(button, x, y, down)) return;
        RmlUiPrototype.PointerButton(button, x, y, down);
    }
    public static void PointerWheel(double x, double y)
    {
        if (_nativeSettings?.TryCaptureWheel((float)y) == true) return;
        RmlUiPrototype.PointerWheel(x, y);
    }
    public static void KeyDown(KeyboardKeyEventArgs e)
    {
        if (_nativeSettings?.TryCaptureKey(e.Key) == true) return;
        var modifiers = RmlUiDesktopInput.Modifiers(e);
        if (_nativeHud?.KeyDown(e.Key, modifiers) == true || _nativePlayback?.Viewport.KeyDown(e.Key) == true || NativeReplayShortcut(e.Key, modifiers)) return;
        RmlUiPrototype.KeyDown(e);
    }
    public static void KeyUp(KeyboardKeyEventArgs e)
    {
        if (_nativePlayback?.Viewport.KeyUp(e.Key) == true) return;
        RmlUiPrototype.KeyUp(e);
    }
    public static void TextInput(string text) => RmlUiPrototype.TextInput(text);

    private static void StartBackgroundStartupWork()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _startupWorkCancel, cancellation);
        previous?.Cancel(); previous?.Dispose();
        var token = cancellation.Token;
        _ = Task.Run(() =>
        {
            try
            {
                string[] builtInRooms = Metadata.RoomList.Select(room => room.Name).ToArray();
                MapGen.MapDefinition[] definitions = MapGen.CustomRooms.DeferInitialRegistration
                    ? MapGen.CustomRooms.DeferredDefinitions(builtInRooms).ToArray() : Array.Empty<MapGen.MapDefinition>();
                token.ThrowIfCancellationRequested();
                if (definitions.Length != 0) _startupCatalog.Enqueue((definitions, token));
                Maintenance.RunStartup(); token.ThrowIfCancellationRequested();
                ThumbnailGenerator.EnsureCustomPreviews(line => DebugLog.Line("thumbnails", line), token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { DebugLog.Exception("startup-background", error); }
        }, token);
    }
    private static void PublishDeferredCustomRooms(IReadOnlyList<MapGen.MapDefinition> definitions, CancellationToken token)
    {
        if (!Active || token.IsCancellationRequested) return;
        if (_window?.HasScene == true) { _deferredCustomRoomsPending = definitions; _deferredCustomRoomsToken = token; return; }
        _deferredCustomRoomsPending = null;
        try
        {
            foreach (var definition in definitions) { token.ThrowIfCancellationRequested(); Metadata.RegisterDownloadedMap(definition); }
            _rooms = ThumbnailGenerator.MultiplayerRooms();
            // Recreate browser/rules presenters before the next operation so
            // newly registered rooms are offered by the same service catalog.
            if (_rmlMultiplayer?.Visible != true) { _rmlMultiplayer?.Dispose(); _rmlMultiplayer = null; }
            if (_rmlLobbyRules?.IsOpen != true) _rmlLobbyRules = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { DebugLog.Exception("startup-map-registration", error); }
    }
}
#endif
