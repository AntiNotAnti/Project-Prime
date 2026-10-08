#if MPHREAD_RMLUI_POC || MPHREAD_RMLUI
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Transitional engine presenter. Native lifetime, input, document bindings
    /// and the versioned intent protocol belong to RmlUiHost; this adapter owns
    /// the existing engine-rendered deployment chamber and Hunter composition.
    /// </summary>
    internal static class RmlUiPrototype
    {
        private static readonly RmlUiHost _runtime = new();
        private static readonly Queue<RmlUiIntent> _commands = new();
        private static bool _visible;
        private static LobbySnapshot? _lobbySnapshot;
        private static string _restoreFocus = "";
        private static readonly GamepadUiRouter _gamepad = new();
        private static bool _active;
        private static bool _failed;
        private static int _width;
        private static int _height;
        private static float _density = 1;
        private static long _nextStateRefresh;
        private static int _renderSamples;
        private static double _renderMs;
        private static int _captureFrames;
        private static string? _captureDirectory;
        private static bool _diagnosticsVisible;
        private static bool _lobbyMode;
        private static bool _multiplayerMode;
        private static string _lobbyName = "MULTIPLAYER LOBBY";
        private static string _lobbyEndpoint = "";
        private static readonly LobbyDisplayPlayer[] _lobbyPlayers = new LobbyDisplayPlayer[8];
        private static int _lobbyPlayerCount;

        private readonly record struct LobbyDisplayPlayer(
            int NetSlot, Hunter Hunter, int Suit, string Name,
            bool Ready, bool Local, bool Spectator, bool Occupied);

        static RmlUiPrototype()
        {
            _gamepad.Action += HandleGamepad;
            _runtime.CleanupFailed += error => Mods.DebugLog.Exception("rmlui", error);
        }

        public static bool Requested => Array.Exists(Environment.GetCommandLineArgs(),
            value => value.Equals("-rmlui", StringComparison.OrdinalIgnoreCase)
                || value.Equals("-rmluipoc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("-rmluipocshot", StringComparison.OrdinalIgnoreCase));

        public static bool CaptureRequested => CaptureDirectory() != null;
        public static bool Active => _active;
        public static bool Visible => _active && _visible;
        internal static RmlUiHost Runtime => _runtime;
        public static bool Failed => _failed;
        public static bool LobbyMode => _active && _lobbyMode;

        public static bool TryActivate(RenderWindow window)
        {
            if (!Requested || _active || _failed)
                return _active;

            string root = Path.Combine(AppContext.BaseDirectory, "rmlui");
            string document = Path.Combine(root, "prime_home.rml");
            if (!File.Exists(document))
            {
                Mods.DebugLog.Line("rmlui", $"POC assets are missing at {document}; falling back to Avalonia");
                _failed = true;
                return false;
            }

            try
            {
                if (CaptureSize() is Vector2i requested)
                {
                    window.ClientSize = requested;
                    // The diagnostic needs the framebuffer size produced by
                    // the resize, not the previous frame's cached value.
                    NativeWindow.ProcessWindowEvents(false);
                }
                ReadSize(window, out int width, out int height, out float density);
                bool ok = _runtime.Initialize(width, height, density, root,
                    ModernGraphicsCompat.Active ? RmlUiRenderBackend.DrawList : RmlUiRenderBackend.OpenGl);
                if (!ok)
                {
                    Mods.DebugLog.Line("rmlui", "native RmlUi host refused initialization; falling back to Avalonia");
                    _failed = true;
                    return false;
                }

                _width = width;
                _height = height;
                _density = density;
                _active = true;
                _visible = true;
                ReadSize(window, out _, out _, out _);
                _multiplayerMode = false;
                _nextStateRefresh = 0;
                _captureFrames = 0;
                _captureDirectory = CaptureDirectory();
                _diagnosticsVisible = false;
                GamepadContexts.MenuVisible = Visible;
                _gamepad.Reset();

                LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
                HubSnapshot snapshot = HubState.Capture();
                ConfigureHunter(snapshot);
                RefreshState(snapshot, force: true);
                Mods.DebugLog.Line("rmlui",
                    $"RmlUi 6.3 POC active at {width}x{height} ({density:0.##}x density) "
                    + $"// {LauncherMenuVisuals.Style.Name} / {LauncherMenuVisuals.Activity.Name}");

                return true;
            }
            catch (DllNotFoundException ex)
            {
                Fail("native bridge not found", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                Fail("native bridge ABI mismatch", ex);
            }
            catch (Exception ex)
            {
                Fail("initialization failed", ex);
            }
            return false;
        }

        public static bool EnterLobby(RenderWindow window, string? lobbyName, string? endpoint)
        {
            if (!Requested)
                return false;
            if (!_active && !TryActivate(window))
                return false;

            _commands.Clear();
            _runtime.DiscardIntents();
            _lobbyMode = true;
            _multiplayerMode = false;
            _lobbyName = String.IsNullOrWhiteSpace(lobbyName)
                ? "MULTIPLAYER LOBBY"
                : lobbyName.Trim().ToUpperInvariant();
            _lobbyEndpoint = endpoint?.Trim() ?? "";
            LauncherLobbyVisuals.Active = true;
            PublishLobbyAnchors();
            LauncherBackdrop.Set(LauncherBackdropScene.Lobby,
                _lobbySnapshot?.Match?.RoomKey);
            SetBool("multiplayer_mode", false);
            SetBool("lobby_mode", true);
            Show();
            RefreshLobbyState(force: true);
            _nextStateRefresh = 0;
            Mods.DebugLog.Line("rmlui",
                $"live lobby chamber active // {_lobbyName} // {_lobbyPlayerCount}/8");
            return true;
        }

        public static void ExitLobby()
        {
            if (!_active || !_lobbyMode)
                return;

            _commands.Clear();
            _runtime.DiscardIntents();
            _lobbyMode = false;
            _multiplayerMode = false;
            _lobbyPlayerCount = 0;
            Array.Clear(_lobbyPlayers);
            LauncherLobbyVisuals.Reset();
            SetBool("lobby_mode", false);
            LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
            HubSnapshot snapshot = HubState.Capture();
            ConfigureHunter(snapshot);
            RefreshState(snapshot, force: true);
            _nextStateRefresh = 0;
            Mods.DebugLog.Line("rmlui", "live lobby chamber closed; home restored");
        }

        public static void DrawHunters(RenderWindow window, int width, int height)
        {
            if (!Visible || width <= 0 || height <= 0)
                return;

            if (!_lobbyMode)
            {
                // Browser/Create overlays use the central bay for real data.
                // Do not paint a giant Hunter through translucent server rows.
                if (_multiplayerMode) return;
                LauncherHunter.PreviewSlot = -1;
                LauncherHunter.Draw(window, width, height);
                return;
            }

            // Draw rear-to-front so a distant Hunter can never paint over
            // the local/front hero where their preview rectangles overlap.
            ReadOnlySpan<int> drawOrder = stackalloc int[] { 7, 5, 6, 3, 4, 1, 2, 0 };
            foreach (int i in drawOrder)
            {
                if (i >= _lobbyPlayerCount || i >= LauncherLobbyFormation.Capacity)
                    continue;
                LobbyDisplayPlayer player = _lobbyPlayers[i];
                if (!player.Occupied)
                    continue;

                LobbyFormationSlot placement = LauncherLobbyFormation.At(i);
                LauncherHunter.Wanted = true;
                LauncherHunter.CanPresent = () => Visible && _lobbyMode;
                LauncherHunter.PreviewSlot = i;
                LauncherHunter.Hunter = player.Hunter;
                LauncherHunter.Suit = player.Suit;
                LauncherHunter.Left = placement.HunterLeft;
                LauncherHunter.Top = placement.HunterTop;
                LauncherHunter.Right = placement.HunterRight;
                LauncherHunter.Bottom = placement.HunterBottom;
                LauncherHunter.DistanceScale = placement.HunterDistance;
                LauncherHunter.TransparentBackground = true;
                LauncherHunter.Draw(window, width, height);
            }

            // Restore the local/front configuration because the chamber theme
            // and the under/over atmosphere passes read this shared preview state.
            if (_lobbyPlayerCount > 0 && _lobbyPlayers[0].Occupied)
                ConfigureLobbyHunter(_lobbyPlayers[0], LauncherLobbyFormation.At(0));
        }

        public static void Tick(RenderWindow window)
        {
            if (!Visible)
                return;
            try
            {
                ReadSize(window, out int width, out int height, out float density);
                if (width != _width || height != _height || Math.Abs(density - _density) > 0.01f)
                {
                    _width = width;
                    _height = height;
                    _density = density;
                    _runtime.Resize(width, height, density);
                    if (_lobbyMode) PublishLobbyAnchors();
                }

                long now = Environment.TickCount64;
                if (now >= _nextStateRefresh)
                {
                    if (_lobbyMode)
                    {
                        RefreshLobbyState(force: false);
                        _nextStateRefresh = now + 100;
                    }
                    else
                    {
                        HubSnapshot snapshot = HubState.Capture();
                        ConfigureHunter(snapshot);
                        RefreshState(snapshot, force: false);
                        _nextStateRefresh = now + 1000;
                    }
                }

                Mods.Input.GamepadDesktop.Poll();
                if (!GamepadContexts.Focused)
                    _gamepad.Reset();
                else
                    _gamepad.Update(GamepadManager.Snapshot, GamepadContext.Menu, now);

                _runtime.Update();
                DrainActions();
            }
            catch (Exception ex)
            {
                Fail("update failed", ex);
            }
        }

        public static void Render(int width, int height)
        {
            if (!Visible || width <= 0 || height <= 0)
                return;
            try
            {
                long started = Stopwatch.GetTimestamp();
                _runtime.Render(width, height);
                if (ModernGraphicsCompat.Active) RmlUiGpuCompositor.DrawNativeFrame(width, height);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _renderMs += elapsed;
                _renderSamples++;
                if (_renderSamples >= 30)
                {
                    SetText("ui_cost", $"RMLUI DRAW {_renderMs / _renderSamples:0.00} MS AVG // NO UI BITMAP UPLOAD");
                    _renderSamples = 0;
                    _renderMs = 0;
                }
            }
            catch (Exception ex)
            {
                Fail("render failed", ex);
            }
        }

        public static void AfterDraw(RenderWindow window)
        {
            if (!Visible || _captureDirectory == null)
                return;

            _captureFrames++;
            if (_captureFrames < 30)
                return;

            string directory = Path.GetFullPath(_captureDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "rmlui-home.png");
            bool saved = Mods.ScreenCapture.SaveWindow(
                window.FramebufferSize.X, window.FramebufferSize.Y, path);
            if (saved)
            {
                Mods.Render.FinalCompositeCapture.WriteEvidence(path,
                    window.FramebufferSize.X, window.FramebufferSize.Y,
                    "RmlUi proof final composite: procedural deployment chamber + engine Hunter + direct RmlUi overlay");
                Mods.DebugLog.Line("rmlui", $"proof capture: {path}");
            }
            else
            {
                Mods.DebugLog.Line("rmlui", "proof capture failed");
                Environment.ExitCode = 1;
            }

            _captureDirectory = null;
            Shell.RequestQuit();
        }

        public static bool TryTakeIntent(out RmlUiIntent intent)
        {
            while (_commands.Count > 0)
            {
                intent = _commands.Dequeue();
                if (Visible && _runtime.IsAlive(intent.Document)) return true;
            }
            intent = default;
            return false;
        }

        // Compatibility for the legacy Shell adapter; new presenters consume typed intents.
        public static bool TryTakeCommand(out string command)
        {
            if (TryTakeIntent(out RmlUiIntent intent))
            {
                command = RmlUiIntentRegistry.ToLegacy(intent);
                return true;
            }
            command = string.Empty;
            return false;
        }

        public static void PresentLobby(LobbySnapshot snapshot)
        {
            _runtime.VerifyOwnerThread();
            if (_lobbySnapshot is { } previous && previous.Lifetime == snapshot.Lifetime
                && snapshot.Version < previous.Version) return;
            _lobbySnapshot = snapshot;
            if (Visible && _lobbyMode) RefreshLobbyState(force: false);
        }

        public static void ClearLobbySnapshot()
        {
            _runtime.VerifyOwnerThread();
            _lobbySnapshot = null;
            _commands.Clear();
        }

        public static void Hide()
        {
            if (!_active) return;
            _runtime.VerifyOwnerThread();
            _restoreFocus = _runtime.FocusedElement();
            _visible = false;
            _runtime.ReleaseInput();
            _runtime.ShowDocument(_runtime.HomeDocument, false);
            _commands.Clear();
            _runtime.DiscardIntents();
            _gamepad.Reset();
            LauncherHunter.Wanted = false;
            LauncherLobbyVisuals.Active = false;
            GamepadContexts.MenuVisible = false;
        }

        public static void Show()
        {
            if (!_active) return;
            _runtime.VerifyOwnerThread();
            _visible = true;
            _runtime.ShowDocument(_runtime.HomeDocument, true);
            _gamepad.Reset();
            GamepadContexts.MenuVisible = true;
            if (_lobbyMode)
            {
                PublishLobbyAnchors();
                RefreshLobbyState(force: true);
            }
            else
            {
                HubSnapshot snapshot = HubState.Capture();
                ConfigureHunter(snapshot);
                RefreshState(snapshot, force: true);
            }
            if (!_runtime.FocusDocument(_runtime.HomeDocument, _restoreFocus))
                _runtime.FocusDocument(_runtime.HomeDocument,
                    _lobbyMode ? "lobby_hunter" : _multiplayerMode ? "play_quick" : "deploy");
            _nextStateRefresh = 0;
        }

        public static void PointerMoved(double x, double y)
        {
            if (Visible) _runtime.Input.PointerMoved(x, y);
        }

        public static void PointerButton(MouseButton button, double x, double y, bool down)
        {
            if (!Visible) return;
            int translated = button switch
            {
                MouseButton.Button2 => 1,
                MouseButton.Button3 => 2,
                _ => 0
            };
            _runtime.Input.PointerButton(translated, x, y, down);
        }

        public static void PointerWheel(double deltaX, double deltaY)
        {
            if (Visible) _runtime.Input.PointerWheel(deltaY);
        }

        public static void KeyDown(KeyboardKeyEventArgs e)
        {
            if (!Visible) return;
#if DEBUG
            if (e.Key == Keys.F9)
            {
                try
                {
                    if (!_runtime.ReloadAssets())
                        throw new InvalidOperationException("Native RmlUi asset reload failed.");
                    _commands.Clear();
                    SetBool("lobby_mode", _lobbyMode);
                    SetBool("multiplayer_mode", _multiplayerMode);
                    Show();
                    if (_lobbyMode) PublishLobbyAnchors();
                }
                catch (Exception ex) { Fail("asset reload failed", ex); }
                return;
            }
#endif
            if (e.Key == Keys.F10)
            {
                _diagnosticsVisible = !_diagnosticsVisible;
                SetBool("diagnostics_visible", _diagnosticsVisible);
                return;
            }
            if (e.Key == Keys.Escape)
            {
                // The selector is a modal layer inside this proof, so Escape
                // closes it and restores the committed Menu Stage before
                // falling through to the production shell on a second press.
                if (!_runtime.Back())
                    _commands.Enqueue(_runtime.CreateIntent(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.News));
                return;
            }
            RmlUiDesktopInput.KeyDown(_runtime, e);
        }

        public static void KeyUp(KeyboardKeyEventArgs e)
        {
            if (!Visible) return;
            int key = RmlUiDesktopInput.TranslateKey(e.Key);
            if (key != 0) _runtime.Input.Key(key, false, RmlUiDesktopInput.Modifiers(e));
        }

        public static void TextInput(string text)
        {
            if (Visible) _runtime.Input.Text(text);
        }

        public static void Shutdown()
        {
            if (!_active)
                return;
            RmlUiGpuCompositor.ReleaseNativeFrame();
            try { _runtime.Shutdown(); }
            catch (Exception ex) { Mods.DebugLog.Exception("rmlui", ex); }
            _active = false;
            _visible = false;
            _commands.Clear();
            _gamepad.Reset();
            _captureDirectory = null;
            _captureFrames = 0;
            _lobbyMode = false;
            _lobbyPlayerCount = 0;
            _lobbySnapshot = null;
            _restoreFocus = "";
            Array.Clear(_lobbyPlayers);
            LauncherLobbyVisuals.Reset();
            LauncherHunter.Reset();
            GamepadContexts.MenuVisible = false;
            Mods.DebugLog.Line("rmlui", "RmlUi POC shut down");
        }

        private static void RefreshState(HubSnapshot snapshot, bool force)
        {
            SetText("player_name", snapshot.PlayerName.ToUpperInvariant());
            SetText("hunter_name", snapshot.DisplayHunter.ToString().ToUpperInvariant());
            SetText("profile_state", snapshot.GameFilesReady ? "LOCAL PROFILE // GAME DATA READY" : "LOCAL PROFILE // SETUP REQUIRED");
            SetText("game_data_state", snapshot.GameFilesReady ? "GAME DATA READY" : "GAME DATA NOT CONFIGURED");
            SetText("build_version", Update.BuildVersion.Display);
            SetBool("reduce_motion", LauncherPrefs.ReduceMotion);
            SetBool("diagnostics_visible", _diagnosticsVisible);
            if (force)
            {
                SetText("renderer_name", "OPENGL // RMLUI 6.3");
                SetText("ui_cost", "RMLUI DIRECT GPU OVERLAY // MEASURING");
            }
        }

        private static void RefreshLobbyState(bool force)
        {
            if (!_lobbyMode)
                return;

            LobbySnapshot? snapshot = _lobbySnapshot;
            if (snapshot == null) return;
            int localRosterIndex = -1;
            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                if (snapshot.Players[i].Slot == snapshot.LocalSlot)
                {
                    localRosterIndex = i;
                    break;
                }
            }

            int display = 0;
            void AddRosterPlayer(int rosterIndex)
            {
                if (display >= _lobbyPlayers.Length || rosterIndex < 0 || rosterIndex >= snapshot.Players.Length)
                    return;
                LobbyPlayerSnapshot player = snapshot.Players[rosterIndex];
                string name = String.IsNullOrWhiteSpace(player.Name) ? $"PLAYER {player.Slot + 1}" : player.Name;
                _lobbyPlayers[display++] = new LobbyDisplayPlayer(
                    player.Slot, (Hunter)Math.Clamp((int)player.Hunter, 0, Hunters.Playable - 1),
                    Math.Clamp((int)player.Color, 0, 3), name.Trim().ToUpperInvariant(),
                    player.Ready, player.Slot == snapshot.LocalSlot, player.IsSpectator, Occupied: true);
            }

            if (localRosterIndex >= 0)
                AddRosterPlayer(localRosterIndex);
            else if (snapshot.LocalSlot >= 0)
            {
                string localName = String.IsNullOrWhiteSpace(snapshot.PlayerName)
                    ? HubState.Capture().PlayerName : snapshot.PlayerName;
                _lobbyPlayers[display++] = new LobbyDisplayPlayer(
                    snapshot.LocalSlot, snapshot.LocalHunter, Math.Clamp((int)snapshot.LocalColor, 0, 3),
                    localName.Trim().ToUpperInvariant(), Ready: false,
                    Local: true, snapshot.PreferSpectator, Occupied: true);
            }
            for (int i = 0; i < snapshot.Players.Length && display < _lobbyPlayers.Length; i++)
                if (i != localRosterIndex) AddRosterPlayer(i);

            for (int i = display; i < _lobbyPlayers.Length; i++)
                _lobbyPlayers[i] = default;
            _lobbyPlayerCount = display;

            byte mask = 0;
            int readyCount = 0;
            for (int i = 0; i < _lobbyPlayers.Length; i++)
            {
                LobbyDisplayPlayer player = _lobbyPlayers[i];
                bool occupied = player.Occupied;
                if (occupied)
                {
                    mask |= (byte)(1 << i);
                    if (player.Ready) readyCount++;
                }
                SetBool($"slot{i}_occupied", occupied);
                SetBool($"slot{i}_ready", occupied && player.Ready);
                SetBool($"slot{i}_local", occupied && player.Local);
                SetText($"slot{i}_name", occupied ? player.Name : "");
                SetText($"slot{i}_hunter", occupied ? player.Hunter.ToString().ToUpperInvariant() : "");
                SetText($"slot{i}_state", occupied
                    ? player.Spectator ? "SPECTATING" : player.Ready ? "READY" : "WAITING"
                    : "");
            }

            LauncherLobbyVisuals.Active = true;
            LauncherLobbyVisuals.OccupiedMask = mask;

            MatchDefinition? match = snapshot.Match;
            if (match is { } definition)
            {
                LauncherBackdrop.Set(LauncherBackdropScene.Lobby, definition.RoomKey);
                SetText("lobby_map", definition.RoomKey.ToUpperInvariant());
                SetText("lobby_mode_name", definition.Mode.ToString().ToUpperInvariant());
                SetText("lobby_format", definition.Format.ToString().ToUpperInvariant());
            }
            else
            {
                SetText("lobby_map", "WAITING FOR MAP");
                SetText("lobby_mode_name", "MULTIPLAYER");
                SetText("lobby_format", "PENDING");
            }

            SetText("lobby_name", _lobbyName);
            SetText("lobby_player_count", $"{display} / 8");
            SetText("lobby_ready_count", $"{readyCount} READY");
            bool localReady = localRosterIndex >= 0 && snapshot.Players[localRosterIndex].Ready;
            bool starting = snapshot.Phase == SessionPhase.Starting;
            SetBool("lobby_local_ready", localReady);
            SetBool("lobby_require_ready", (snapshot.RuleFlags & LobbyRuleFlags.RequireReady) != 0);
            SetBool("lobby_owner", snapshot.IsOwner);
            SetBool("lobby_starting", starting);
            SetText("lobby_ready_action", localReady ? "UNREADY" : "READY");
            SetText("lobby_local_hunter",
                (display > 0 ? _lobbyPlayers[0].Hunter : snapshot.LocalHunter)
                    .ToString().ToUpperInvariant());

            string status = String.IsNullOrWhiteSpace(snapshot.CommandError)
                ? snapshot.Message : snapshot.CommandError;
            if (String.IsNullOrWhiteSpace(status))
            {
                if (starting)
                {
                    double remaining = snapshot.CountdownSeconds;
                    status = remaining > 0
                        ? $"MATCH STARTING // {Math.Max(1, (int)Math.Ceiling(remaining))}"
                        : "SYNCHRONIZING MATCH";
                }
                else if (!String.IsNullOrWhiteSpace(_lobbyEndpoint))
                    status = $"CONNECTED // {_lobbyEndpoint}";
                else
                    status = "CONNECTED // WAITING FOR PLAYERS";
            }
            SetText("lobby_status", status.ToUpperInvariant());

            if (display > 0)
                ConfigureLobbyHunter(_lobbyPlayers[0], LauncherLobbyFormation.At(0));
            else
                LauncherHunter.Wanted = false;

            if (force)
            {
                SetText("renderer_name", "OPENGL // RMLUI 6.3 // LIVE LOBBY");
                SetText("ui_cost", "RMLUI LIVE LOBBY // DIRECT GPU OVERLAY");
            }
        }

        private static void ConfigureLobbyHunter(
            LobbyDisplayPlayer player, LobbyFormationSlot placement)
        {
            LauncherHunter.Wanted = GameFiles.Ready && player.Occupied;
            LauncherHunter.CanPresent = () => Visible && _lobbyMode;
            LauncherHunter.PreviewSlot = 0;
            LauncherHunter.Hunter = player.Hunter;
            LauncherHunter.Suit = player.Suit;
            LauncherHunter.Left = placement.HunterLeft;
            LauncherHunter.Top = placement.HunterTop;
            LauncherHunter.Right = placement.HunterRight;
            LauncherHunter.Bottom = placement.HunterBottom;
            LauncherHunter.DistanceScale = placement.HunterDistance;
            LauncherHunter.TransparentBackground = true;
        }

        private static void PublishLobbyAnchors()
        {
            if (!Visible) return;
            for (int i = 0; i < LauncherLobbyFormation.Capacity; i++)
            {
                LobbyFormationSlot slot = LauncherLobbyFormation.At(i);
                _runtime.SetLobbyAnchor(i, slot.LabelX, slot.LabelY);
            }
        }

        private static void ConfigureHunter(HubSnapshot snapshot)
        {
            LauncherHunter.Wanted = snapshot.GameFilesReady && !_multiplayerMode;
            LauncherHunter.CanPresent = () => Visible && !_multiplayerMode;
            LauncherHunter.PreviewSlot = -1;
            LauncherHunter.Hunter = snapshot.DisplayHunter;
            LauncherHunter.Suit = snapshot.Suit;
            // The room profile owns composition now, not the RML document.
            // Changing activity can therefore move the map camera and Hunter
            // together without duplicating presentation constants in UI code.
            MenuStageProfile stage = LauncherMenuStage.Current;
            LauncherHunter.Left = stage.HunterLeft;
            LauncherHunter.Top = stage.HunterTop;
            LauncherHunter.Right = stage.HunterRight;
            LauncherHunter.Bottom = stage.HunterBottom;
            LauncherHunter.DistanceScale = stage.HunterDistanceScale;
            LauncherHunter.TransparentBackground = true;
        }

        private static void DrainActions()
        {
            for (int i = 0; i < 64 && _runtime.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                if (intent.Kind is RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview)
                {
                    ApplyStageAction(intent);
                    continue;
                }
                if (intent.Kind is >= RmlUiIntentKind.PlayQuick and <= RmlUiIntentKind.PlayServer)
                {
                    bool multiplayer = intent.Kind != RmlUiIntentKind.PlayCancel;
                    if (_multiplayerMode != multiplayer)
                    {
                        _multiplayerMode = multiplayer;
                        if (multiplayer) LauncherHunter.Wanted = false;
                        else ConfigureHunter(HubState.Capture());
                    }
                }
                _commands.Enqueue(intent);
            }
        }

        private static void ApplyStageAction(RmlUiIntent intent)
        {
            string stage = intent.Argument switch
            {
                0 => "quick", 1 => "browser", 2 => "offline", 3 => "adventure", _ => ""
            };
            bool preview = intent.Kind == RmlUiIntentKind.StagePreview;

            switch (stage)
            {
                case "quick":
                    LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer,
                        "MP3 PROVING GROUND");
                    break;
                case "browser":
                    LauncherBackdrop.Set(LauncherBackdropScene.Play,
                        "MP1 SANCTORUS");
                    break;
                case "offline":
                    LauncherBackdrop.Set(LauncherBackdropScene.Offline,
                        "MP3 PROVING GROUND");
                    break;
                case "adventure":
                    LauncherBackdrop.Set(LauncherBackdropScene.Adventure,
                        "UNIT1 ALINOS LANDFALL");
                    break;
                default:
                    return;
            }

            HubSnapshot snapshot = HubState.Capture();
            ConfigureHunter(snapshot);
            Mods.DebugLog.Line("rmlui",
                $"{(preview ? "deployment chamber preview" : "deployment chamber")} -> "
                + $"{LauncherMenuVisuals.Activity.Name} / "
                + $"{LauncherMenuVisuals.Hunter(snapshot.DisplayHunter).Name}");
        }

        private static void HandleGamepad(UiAction action)
        {
            if (!Visible) return;
            if (action == UiAction.Back)
            {
                // Back closes an open activity drawer first. Only a second
                // Back hands control to the production shell.
                if (!_runtime.Back())
                    _commands.Enqueue(_runtime.CreateIntent(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.News));
                return;
            }
            int key = action switch
            {
                UiAction.Up => 5,
                UiAction.Down => 6,
                UiAction.Left => 7,
                UiAction.Right => 8,
                UiAction.Accept => 2,
                UiAction.PreviousTab => 1,
                UiAction.NextTab => 1,
                _ => 0
            };
            if (key == 0) return;
            int modifiers = action == UiAction.PreviousTab ? 1 : 0;
            _runtime.Input.Key(key, true, (RmlUiInputModifiers)modifiers);
            _runtime.Input.Key(key, false, (RmlUiInputModifiers)modifiers);
        }

        private static string? CaptureDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i].Equals("-rmluipocshot", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static Vector2i? CaptureSize()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!args[i].Equals("-rmluisize", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] parts = args[i + 1].Split('x', 'X');
                if (parts.Length == 2
                    && Int32.TryParse(parts[0], out int width)
                    && Int32.TryParse(parts[1], out int height)
                    && width is >= 640 and <= 7680
                    && height is >= 360 and <= 4320)
                    return new Vector2i(width, height);
            }
            return null;
        }

        private static unsafe void ReadSize(RenderWindow window, out int width, out int height, out float density)
        {
            width = Math.Max(window.FramebufferSize.X, 1);
            height = Math.Max(window.FramebufferSize.Y, 1);

            // Do not use NativeWindow.ClientSize for the Retina ratio here.
            // On macOS/OpenTK it can already reflect framebuffer-sized pixels,
            // which makes a 2x Retina window look like 1x and shrinks every
            // density-independent RmlUi control by half on screen.
            //
            // GLFW window size is explicitly in screen coordinates while the
            // framebuffer size is in pixels. Their ratio is therefore the
            // authoritative conversion for both RmlUi dp and pointer input.
            int windowWidth = 0, windowHeight = 0;
            float framebufferScaleX = 1f, framebufferScaleY = 1f;
            try
            {
                GLFW.GetWindowSize(window.WindowPtr, out windowWidth, out windowHeight);
                if (windowWidth > 0)
                    framebufferScaleX = Math.Max(1f, width / (float)windowWidth);
                if (windowHeight > 0)
                    framebufferScaleY = Math.Max(1f, height / (float)windowHeight);
            }
            catch
            {
                // Keep the 1x fallback and let content scale below provide a
                // platform DPI answer if GLFW window-size lookup is unavailable.
            }

            _runtime.Input.SetFramebufferScale(framebufferScaleX, framebufferScaleY);

            float contentScale = 1f;
            try
            {
                GLFW.GetWindowContentScale(window.WindowPtr, out float xScale, out float yScale);
                if (float.IsFinite(xScale) && float.IsFinite(yScale))
                    contentScale = Math.Max(1f, Math.Max(xScale, yScale));
            }
            catch
            {
                // Framebuffer/window ratio above remains the primary answer.
            }

            density = Math.Max(
                Math.Max(framebufferScaleX, framebufferScaleY),
                contentScale);

            float forced = DensityOverride();
            if (forced > 0)
                density = forced;
        }

        private static float DensityOverride()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!args[i].Equals("-rmluidensity", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (float.TryParse(args[i + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value)
                    && float.IsFinite(value))
                    return Math.Clamp(value, 0.75f, 4f);
            }
            return 0;
        }

        // Presentation-neutral services are only allowed to publish through
        // these render-thread entry points. Their worker tasks never invoke
        // native RmlUi or touch the OpenGL context directly.
        internal static void SetMenuText(string name, string value) => SetText(name, value);
        internal static void SetMenuBool(string name, bool value) => SetBool(name, value);

        internal static void SetFieldValue(string id, string value)
        {
            if (_active) _runtime.SetField(_runtime.HomeDocument, id, value);
        }

        internal static string ReadFieldValue(string id) => _active
            ? _runtime.ReadField(_runtime.HomeDocument, id) : string.Empty;

        private static void SetText(string name, string value)
        {
            if (_active) _runtime.SetText(_runtime.HomeDocument, name, value);
        }

        private static void SetBool(string name, bool value)
        {
            if (_active) _runtime.SetBool(_runtime.HomeDocument, name, value);
        }

        private static void Fail(string message, Exception ex)
        {
            bool wasActive = _active;
            _failed = true;
            _active = false;
            _visible = false;
            if (wasActive)
            {
                RmlUiGpuCompositor.ReleaseNativeFrame();
                try { _runtime.Shutdown(); }
                catch (Exception shutdown) { Mods.DebugLog.Exception("rmlui", shutdown); }
            }
            _commands.Clear();
            _gamepad.Reset();
            _captureDirectory = null;
            _captureFrames = 0;
            _lobbyMode = false;
            _lobbyPlayerCount = 0;
            Array.Clear(_lobbyPlayers);
            LauncherLobbyVisuals.Reset();
            LauncherHunter.Reset();
            GamepadContexts.MenuVisible = false;
            Mods.DebugLog.Line("rmlui", $"POC {message}: {ex.Message}; falling back to Avalonia");
            Mods.DebugLog.Exception("rmlui", ex);
        }

    }
}
#endif
