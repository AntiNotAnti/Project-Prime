#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Opt-in RmlUi 6.3 proof of concept for the front screen.
    ///
    /// This deliberately does not replace any authoritative launcher service.
    /// It proves the rendering/input seam only: RmlUi draws directly into the
    /// shell's OpenGL back buffer while HubState supplies the same player/hunter
    /// snapshot the Avalonia shell consumes. Actions hand back to the existing
    /// Prime shell until a destination is migrated for real.
    /// </summary>
    internal static class RmlUiPrototype
    {
        private const string NativeLibraryName = "ProjectPrime.RmlUi.Native";
        private const int ActionBufferSize = 128;
        private static readonly byte[] _actionBuffer = new byte[ActionBufferSize];
        private static readonly Queue<string> _commands = new();
        private static readonly GamepadUiRouter _gamepad = new();
        private static bool _active;
        private static bool _failed;
        private static int _width;
        private static int _height;
        private static float _density = 1;
        private static float _pointerScaleX = 1;
        private static float _pointerScaleY = 1;
        private static long _nextStateRefresh;
        private static int _renderSamples;
        private static double _renderMs;
        private static int _captureFrames;
        private static string? _captureDirectory;
        private static bool _diagnosticsVisible;
        private static bool _lobbyMode;
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
        }

        public static bool Requested => Array.Exists(Environment.GetCommandLineArgs(),
            value => value.Equals("-rmluipoc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("-rmluipocshot", StringComparison.OrdinalIgnoreCase));

        public static bool CaptureRequested => CaptureDirectory() != null;
        public static bool Active => _active;
        public static bool Failed => _failed;
        public static bool LobbyMode => _active && _lobbyMode;

        public static bool TryActivate(RenderWindow window)
        {
            if (!Requested || _active || _failed)
                return _active;

            if (ModernGraphicsCompat.Active || GraphicsBackendPolicy.ModernGameplayRequested)
            {
                Mods.DebugLog.Line("rmlui", "POC requires the compatibility OpenGL renderer; falling back to Avalonia");
                return false;
            }

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
                int ok = NativeInitialize(width, height, density, root);
                if (ok == 0)
                {
                    Mods.DebugLog.Line("rmlui", "native RmlUi host refused initialization; falling back to Avalonia");
                    _failed = true;
                    return false;
                }

                _width = width;
                _height = height;
                _density = density;
                _active = true;
                _nextStateRefresh = 0;
                _captureFrames = 0;
                _captureDirectory = CaptureDirectory();
                _diagnosticsVisible = false;
                GamepadContexts.MenuVisible = true;
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

            _lobbyMode = true;
            _lobbyName = String.IsNullOrWhiteSpace(lobbyName)
                ? "MULTIPLAYER LOBBY"
                : lobbyName.Trim().ToUpperInvariant();
            _lobbyEndpoint = endpoint?.Trim() ?? "";
            LauncherLobbyVisuals.Active = true;
            PublishLobbyAnchors();
            LauncherBackdrop.Set(LauncherBackdropScene.Lobby,
                NetSession.ActiveMatchDefinition?.RoomKey);
            SetBool("lobby_mode", true);
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

            _lobbyMode = false;
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
            if (!_active || width <= 0 || height <= 0)
                return;

            if (!_lobbyMode)
            {
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
                LauncherHunter.CanPresent = () => _active && _lobbyMode;
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
            if (!_active)
                return;
            try
            {
                ReadSize(window, out int width, out int height, out float density);
                if (width != _width || height != _height || Math.Abs(density - _density) > 0.01f)
                {
                    _width = width;
                    _height = height;
                    _density = density;
                    NativeResize(width, height, density);
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

                NativeUpdate();
                DrainActions();
            }
            catch (Exception ex)
            {
                Fail("update failed", ex);
            }
        }

        public static void Render(int width, int height)
        {
            if (!_active || width <= 0 || height <= 0)
                return;
            try
            {
                long started = Stopwatch.GetTimestamp();
                NativeRender(width, height);
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
            if (!_active || _captureDirectory == null)
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

        public static bool TryTakeCommand(out string command)
        {
            if (_commands.Count > 0)
            {
                command = _commands.Dequeue();
                return true;
            }
            command = string.Empty;
            return false;
        }

        public static void PointerMoved(double x, double y)
        {
            if (_active)
            {
                NativeMouseMove(
                    (int)Math.Round(x * _pointerScaleX),
                    (int)Math.Round(y * _pointerScaleY), 0);
            }
        }

        public static void PointerButton(MouseButton button, double x, double y, bool down)
        {
            if (!_active) return;
            PointerMoved(x, y);
            int translated = button switch
            {
                MouseButton.Button2 => 1,
                MouseButton.Button3 => 2,
                _ => 0
            };
            NativeMouseButton(translated, down ? 1 : 0, 0);
        }

        public static void PointerWheel(double deltaX, double deltaY)
        {
            if (_active) NativeMouseWheel((float)deltaY, 0);
        }

        public static void KeyDown(KeyboardKeyEventArgs e)
        {
            if (!_active) return;
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
                if (NativeBack() == 0)
                    _commands.Enqueue("route:news");
                return;
            }
            int key = TranslateKey(e.Key);
            if (key != 0) NativeKey(key, 1, Modifiers(e));
        }

        public static void KeyUp(KeyboardKeyEventArgs e)
        {
            if (!_active) return;
            int key = TranslateKey(e.Key);
            if (key != 0) NativeKey(key, 0, Modifiers(e));
        }

        public static void TextInput(string text)
        {
            if (!_active || string.IsNullOrEmpty(text)) return;
            foreach (Rune rune in text.EnumerateRunes())
                NativeText((uint)rune.Value);
        }

        public static void Shutdown()
        {
            if (!_active)
                return;
            try { NativeShutdown(); }
            catch (Exception ex) { Mods.DebugLog.Exception("rmlui", ex); }
            _active = false;
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

            RosterPacket roster = NetSession.LobbyRoster();
            int localRosterIndex = -1;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster.Slots[i] == NetSession.LocalSlot)
                {
                    localRosterIndex = i;
                    break;
                }
            }

            int display = 0;
            void AddRosterPlayer(int rosterIndex)
            {
                if (display >= _lobbyPlayers.Length || rosterIndex < 0 || rosterIndex >= roster.Count)
                    return;
                int netSlot = roster.Slots[rosterIndex];
                Hunter hunter = (Hunter)Math.Clamp((int)roster.Hunters[rosterIndex], 0, Hunters.Playable - 1);
                int suit = Math.Clamp((int)roster.Colors[rosterIndex], 0, 3);
                string name = roster.Names[rosterIndex];
                if (String.IsNullOrWhiteSpace(name))
                    name = $"PLAYER {netSlot + 1}";
                bool ready = roster.LobbyReady[rosterIndex];
                bool local = netSlot == NetSession.LocalSlot;
                bool spectator = roster.Roles[rosterIndex] != 0;
                _lobbyPlayers[display++] = new LobbyDisplayPlayer(
                    netSlot, hunter, suit, name.Trim().ToUpperInvariant(),
                    ready, local, spectator, Occupied: true);
            }

            if (localRosterIndex >= 0)
            {
                AddRosterPlayer(localRosterIndex);
            }
            else if (NetSession.LocalSlot >= 0)
            {
                // The authoritative roster may arrive one packet after the
                // session. Always reserve the front position for the local
                // player so another peer never temporarily becomes the hero.
                string localName = String.IsNullOrWhiteSpace(NetSession.PlayerName)
                    ? HubState.Capture().PlayerName
                    : NetSession.PlayerName;
                _lobbyPlayers[display++] = new LobbyDisplayPlayer(
                    NetSession.LocalSlot, NetSession.LocalHunter,
                    Math.Clamp(NetSession.LocalColor, 0, 3),
                    localName.Trim().ToUpperInvariant(),
                    NetSession.LocalSlot < NetSession.SlotLobbyReady.Length
                        && NetSession.SlotLobbyReady[NetSession.LocalSlot],
                    Local: true, SpectatorMode.PreferSpectator, Occupied: true);
            }
            for (int i = 0; i < roster.Count && display < _lobbyPlayers.Length; i++)
            {
                if (i != localRosterIndex)
                    AddRosterPlayer(i);
            }

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

            MatchDefinition? match = NetSession.ActiveMatchDefinition;
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
            bool localReady = NetSession.LocalSlot >= 0
                && NetSession.LocalSlot < NetSession.SlotLobbyReady.Length
                && NetSession.SlotLobbyReady[NetSession.LocalSlot];
            SetBool("lobby_local_ready", localReady);
            SetBool("lobby_owner", NetSession.LocalIsLobbyOwner);
            SetBool("lobby_starting", NetSession.IsStarting);
            SetText("lobby_ready_action", localReady ? "UNREADY" : "READY");
            SetText("lobby_local_hunter",
                (display > 0 ? _lobbyPlayers[0].Hunter : NetSession.LocalHunter)
                    .ToString().ToUpperInvariant());

            string status = NetSession.LobbyMessage;
            if (String.IsNullOrWhiteSpace(status))
            {
                if (NetSession.IsStarting)
                {
                    double remaining = NetSession.StartCountdownRemainingSeconds;
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
                ConfigureLobbyHunter(_lobbyPlayers[0], LobbyPlacements[0]);
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
            LauncherHunter.CanPresent = () => _active && _lobbyMode;
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
            if (!_active) return;
            for (int i = 0; i < LauncherLobbyFormation.Capacity; i++)
            {
                LobbyFormationSlot slot = LauncherLobbyFormation.At(i);
                NativeSetLobbyAnchor(i, slot.LabelX, slot.LabelY);
            }
        }

        private static void ConfigureHunter(HubSnapshot snapshot)
        {
            LauncherHunter.Wanted = snapshot.GameFilesReady;
            LauncherHunter.CanPresent = () => _active;
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
            for (int i = 0; i < 8; i++)
            {
                Array.Clear(_actionBuffer, 0, _actionBuffer.Length);
                int length = NativeTakeAction(_actionBuffer, _actionBuffer.Length);
                if (length <= 0) return;
                string action = Encoding.UTF8.GetString(
                    _actionBuffer, 0, Math.Min(length, _actionBuffer.Length - 1));
                if (action.StartsWith("stage:", StringComparison.Ordinal)
                    || action.StartsWith("stage-preview:", StringComparison.Ordinal))
                {
                    ApplyStageAction(action);
                    continue;
                }
                _commands.Enqueue(action);
            }
        }

        private static void ApplyStageAction(string action)
        {
            int separator = action.IndexOf(':');
            string stage = separator >= 0 && separator + 1 < action.Length
                ? action[(separator + 1)..]
                : "";
            bool preview = action.StartsWith("stage-preview:", StringComparison.Ordinal);

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
            if (!_active) return;
            if (action == UiAction.Back)
            {
                // Back closes an open activity drawer first. Only a second
                // Back hands control to the production shell.
                if (NativeBack() == 0)
                    _commands.Enqueue("route:news");
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
            NativeKey(key, 1, modifiers);
            NativeKey(key, 0, modifiers);
        }

        private static int TranslateKey(Keys key) => key switch
        {
            Keys.Tab => 1,
            Keys.Enter or Keys.KeyPadEnter => 2,
            Keys.Escape => 3,
            Keys.Space => 4,
            Keys.Up => 5,
            Keys.Down => 6,
            Keys.Left => 7,
            Keys.Right => 8,
            Keys.Home => 9,
            Keys.End => 10,
            Keys.PageUp => 11,
            Keys.PageDown => 12,
            _ => 0
        };

        private static int Modifiers(KeyboardKeyEventArgs e)
        {
            int modifiers = 0;
            if (e.Shift) modifiers |= 1;
            if (e.Control) modifiers |= 2;
            if (e.Alt) modifiers |= 4;
            if (e.Command) modifiers |= 8;
            return modifiers;
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

            _pointerScaleX = framebufferScaleX;
            _pointerScaleY = framebufferScaleY;

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

        private static void SetText(string name, string value)
        {
            if (_active) NativeSetText(name, value ?? string.Empty);
        }

        private static void SetBool(string name, bool value)
        {
            if (_active) NativeSetBool(name, value ? 1 : 0);
        }

        private static void Fail(string message, Exception ex)
        {
            bool wasActive = _active;
            _failed = true;
            _active = false;
            if (wasActive)
            {
                try { NativeShutdown(); }
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

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_initialize")]
        private static extern int NativeInitialize(int width, int height, float density,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string assetRoot);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_shutdown")]
        private static extern void NativeShutdown();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_update")]
        private static extern void NativeUpdate();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_render")]
        private static extern void NativeRender(int width, int height);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_resize")]
        private static extern void NativeResize(int width, int height, float density);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_move")]
        private static extern int NativeMouseMove(int x, int y, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_button")]
        private static extern int NativeMouseButton(int button, int down, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_wheel")]
        private static extern int NativeMouseWheel(float deltaY, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_key")]
        private static extern int NativeKey(int key, int down, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_text")]
        private static extern int NativeText(uint codepoint);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_text")]
        private static extern void NativeSetText(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_bool")]
        private static extern void NativeSetBool(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_lobby_anchor")]
        private static extern void NativeSetLobbyAnchor(int slot, float centerX, float centerY);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_back")]
        private static extern int NativeBack();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_take_action")]
        private static extern int NativeTakeAction([Out] byte[] buffer, int capacity);
    }
}
#endif
