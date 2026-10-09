#if MPHREAD_RMLUI_POC || MPHREAD_RMLUI
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Components;
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
        private static RmlUiLauncherPages? _pages;
        private static readonly Queue<RmlUiIntent> _commands = new();
        private static bool _visible;
        private static bool _gameplayOverlay;
        private static LobbySnapshot? _lobbySnapshot;
        private static string _restoreFocus = "";
        private static RmlUiWindowsIme? _windowsIme;
        private static RmlUiCocoaIme? _cocoaIme;
        private static RmlUiLinuxIme? _linuxIme;
        private static RmlUiAccessibilityService? _accessibility;
        private static RmlUiCocoaAccessibility? _cocoaAccessibility;
        private static RmlUiWindowsAccessibility? _windowsAccessibility;
        private static RmlUiLinuxAccessibility? _linuxAccessibility;
        private static RenderWindow? _inputWindow;
        private static RmlUiInputModifiers _inputModifiers;
        private static readonly GamepadUiRouter _gamepad = new();
        private static bool _active;
        private static bool _failed;
        private static bool _tearingDown;
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
        private static readonly LobbyDisplayPlayer[] _fadingPlayers = new LobbyDisplayPlayer[8];

        private readonly record struct LobbyDisplayPlayer(
            int NetSlot, Hunter Hunter, int Suit, string Name,
            bool Ready, bool Local, bool Spectator, bool Occupied);

        static RmlUiPrototype()
        {
            _gamepad.Action += HandleGamepad;
            _runtime.CleanupFailed += error => Mods.DebugLog.Exception("rmlui", error);
        }

        public static bool Requested => LauncherUiRuntime.UseNative;

        public static bool CaptureRequested => CaptureDirectory() != null;
        public static bool Active => _active;
        public static bool Visible => _active && _visible;
        internal static RmlUiHost Runtime => _runtime;
        internal static RmlUiLauncherPages? Pages => _pages;
        internal static Func<bool>? BackRequested { get; set; }
        internal static Func<RenderWindow, int, int, bool>? HunterPreviewDrawOverride { get; set; }
        internal static Func<UiAction, bool>? GamepadActionOverride { get; set; }
        internal static Action? InputReleaseRequested { get; set; }
        internal static Action? PresentationRetiring { get; set; }
        public static bool Failed => _failed;
        public static bool LobbyMode => _active && _lobbyMode;

        public static bool TryActivate(RenderWindow window)
        {
            if (!Requested || _active || _failed)
                return _active;

            LauncherUiRuntime.BeginNativeAttempt();
            string root = Path.Combine(AppContext.BaseDirectory, "rmlui");
            // App bundles must seal UI documents as Resources, not nested
            // unsigned code under Contents/MacOS. Loose desktop releases keep
            // the original sibling rmlui directory.
            if (OperatingSystem.IsMacOS() && !File.Exists(Path.Combine(root, "prime_home.rml")))
            {
                string bundleRoot = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "Resources", "rmlui"));
                if (File.Exists(Path.Combine(bundleRoot, "prime_home.rml")))
                    root = bundleRoot;
            }
            string document = Path.Combine(root, "prime_home.rml");
            if (!File.Exists(document))
            {
                Mods.DebugLog.Line("rmlui", $"Native UI assets are missing at {document}.");
                _failed = true;
                LauncherUiRuntime.RecordNativeFailure(LauncherUiFailure.Initialization);
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
                    Mods.DebugLog.Line("rmlui", "Native RmlUi host refused initialization.");
                    _failed = true;
                    LauncherUiRuntime.RecordNativeFailure(LauncherUiFailure.Initialization);
                    return false;
                }

                _width = width;
                _height = height;
                _density = density;
                _active = true;
                _visible = true;
                _gameplayOverlay = false;
                _pages = _runtime.ProtocolVersion == RmlUiIntentRegistry.ProtocolVersion
                    ? new RmlUiLauncherPages(_runtime) : null;
                _inputWindow = window;
                window.FocusedChanged += WindowFocusChanged;
                _windowsIme = RmlUiDesktopInput.AttachIme(_runtime, window, () => Visible);
                _cocoaIme = RmlUiDesktopInput.AttachCocoaIme(_runtime, window, () => Visible);
                _linuxIme = RmlUiLinuxDesktopInput.Attach(_runtime, window, () => Visible);
                AttachAccessibility(window);
                ReadSize(window, out _, out _, out _);
                _multiplayerMode = false;
                _nextStateRefresh = 0;
                _captureFrames = 0;
                _captureDirectory = CaptureDirectory();
                _diagnosticsVisible = false;
                _runtime.Input.DiagnosticsEnabled = false;
                GamepadContexts.MenuVisible = Visible;
                _gamepad.Reset();

                LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
                HubSnapshot snapshot = HubState.Capture();
                ConfigureHunter(snapshot);
                RefreshState(snapshot, force: true);
                _pages?.ShowBaseline(RmlUiMenuPage.Home);
                Mods.DebugLog.Line("rmlui",
                    $"RmlUi 6.3 active at {width}x{height} ({density:0.##}x density) "
                    + $"// {LauncherMenuVisuals.Style.Name} / {LauncherMenuVisuals.Activity.Name}");

                return true;
            }
            catch (DllNotFoundException ex)
            {
                Fail("native bridge not found", ex, LauncherUiFailure.Initialization);
            }
            catch (EntryPointNotFoundException ex)
            {
                Fail("native bridge ABI mismatch", ex, LauncherUiFailure.Initialization);
            }
            catch (Exception ex)
            {
                Fail("initialization failed", ex, LauncherUiFailure.Initialization);
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
            _pages?.ShowBaseline(RmlUiMenuPage.Lobby);
            Show();
            RefreshLobbyState(force: true);
            _nextStateRefresh = 0;
            Mods.DebugLog.Line("rmlui",
                $"live lobby chamber active // {_lobbyName} // {_lobbyPlayerCount}/8");
            return true;
        }

        public static void ExitLobby()
        {
            _gameplayOverlay = false;
            if (!_active || !_lobbyMode)
                return;

            _commands.Clear();
            _runtime.DiscardIntents();
            _lobbyMode = false;
            _multiplayerMode = false;
            _lobbyPlayerCount = 0;
            Array.Clear(_lobbyPlayers);
            Array.Clear(_fadingPlayers);
            LauncherLobbyVisuals.Reset();
            SetBool("lobby_mode", false);
            _pages?.ShowBaseline(RmlUiMenuPage.Home);
            LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
            HubSnapshot snapshot = HubState.Capture();
            ConfigureHunter(snapshot);
            RefreshState(snapshot, force: true);
            _nextStateRefresh = 0;
            Mods.DebugLog.Line("rmlui", "live lobby chamber closed; home restored");
        }

        public static void DrawHunters(RenderWindow window, int width, int height)
        {
            if (!Visible || _gameplayOverlay || width <= 0 || height <= 0)
                return;
            // A customization viewport draws after the modal's background, in its empty
            // authored rectangle. Suppress the chamber hero while that viewport owns it.
            if (HunterPreviewDrawOverride != null && Shell.HasNativeHunterPreview) return;

            if (!_lobbyMode)
            {
                // Browser/Create overlays use the central bay for real data.
                // Do not paint a giant Hunter through translucent server rows.
                if (_multiplayerMode || Shell.HasNativeContentPage) return;
                LauncherHunter.PreviewSlot = -1;
                LauncherHunter.Draw(window, width, height);
                return;
            }

            // Draw rear-to-front so a distant Hunter can never paint over
            // the local/front hero where their preview rectangles overlap.
            ReadOnlySpan<int> drawOrder = stackalloc int[] { 7, 5, 6, 3, 4, 1, 2, 0 };
            foreach (int i in drawOrder)
            {
                LobbyDisplayPlayer player = _lobbyPlayers[i];
                if (player.Occupied) _fadingPlayers[i] = player;
                else if (LauncherPresentation.Motion.Occupancy[i] > .02f) player = _fadingPlayers[i];
                else { _fadingPlayers[i] = default; continue; }
                if (!player.Occupied) continue;

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
                LauncherHunter.Formation = true;
                try { LauncherHunter.Draw(window, width, height); }
                finally { LauncherHunter.Formation = Scene.LauncherPreviewFormation = false; }
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
            long uiStarted = LauncherUiPerformance.Start();
            try
            {
                long phaseStarted = LauncherUiPerformance.Start();
                ReadSize(window, out int width, out int height, out float density);
                if (width != _width || height != _height || Math.Abs(density - _density) > 0.01f)
                {
                    _width = width;
                    _height = height;
                    _density = density;
                    _runtime.Resize(width, height, density);
                    if (_lobbyMode) PublishLobbyAnchors();
                }
                LauncherUiPerformance.RecordPhase("viewport", phaseStarted);

                phaseStarted = LauncherUiPerformance.Start();
                long now = Environment.TickCount64;
                if (!_gameplayOverlay && now >= _nextStateRefresh)
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
                LauncherUiPerformance.RecordPhase("stateRefresh", phaseStarted);

                phaseStarted = LauncherUiPerformance.Start();
                Mods.Input.GamepadDesktop.Poll();
                if (!GamepadContexts.Focused)
                    _gamepad.Reset();
                else
                    _gamepad.Update(GamepadManager.Snapshot, GamepadContext.Menu, now);
                LauncherUiPerformance.RecordPhase("gamepad", phaseStarted);

                phaseStarted = LauncherUiPerformance.Start();
                _pages?.Flush();
                LauncherUiPerformance.RecordPhase("pageBindings", phaseStarted);
                phaseStarted = LauncherUiPerformance.Start();
                _accessibility?.Drain(_runtime);
                LauncherUiPerformance.RecordPhase("accessibilityDrain", phaseStarted);
                phaseStarted = LauncherUiPerformance.Start();
                _runtime.Update();
                LauncherUiPerformance.RecordPhase("contextUpdate", phaseStarted);
                phaseStarted = LauncherUiPerformance.Start();
                _pages?.AfterUpdate();
                LauncherUiPerformance.RecordPhase("pageFocus", phaseStarted);
                phaseStarted = LauncherUiPerformance.Start();
                _windowsIme?.RefreshCandidatePosition();
                _cocoaIme?.RefreshCandidatePosition();
                _linuxIme?.Pump();
                LauncherUiPerformance.RecordPhase("ime", phaseStarted);
                if (_accessibility != null)
                {
                    phaseStarted = LauncherUiPerformance.Start();
                    var semantics = _accessibility.Capture(_runtime);
                    LauncherUiPerformance.RecordPhase("accessibilityCapture", phaseStarted);
                    phaseStarted = LauncherUiPerformance.Start();
                    PublishAccessibility(semantics);
                    LauncherUiPerformance.RecordPhase("accessibilityPublish", phaseStarted);
                    if (LauncherUiPerformance.Enabled)
                    {
                        var counters = _accessibility.CaptureMetrics;
                        LauncherUiPerformance.RecordAccessibilityCapture(counters.Requests, counters.NativeReads,
                            counters.DecodedSnapshots, counters.ReusedSnapshots, counters.IdleSkips);
                    }
                }
                phaseStarted = LauncherUiPerformance.Start();
                DrainActions();
                if (_runtime.Input.DiagnosticsEnabled)
                    SetText("input_debug", _runtime.Input.Diagnostics.Display);
                LauncherUiPerformance.RecordPhase("intentDrain", phaseStarted);
            }
            catch (Exception ex)
            {
                Fail("update failed", ex);
            }
            finally { LauncherUiPerformance.RecordNativeUpdate(uiStarted); }
        }

        public static void Render(int width, int height)
        {
            if (!Visible || width <= 0 || height <= 0)
                return;
            try
            {
                long started = Stopwatch.GetTimestamp();
                long contextStarted = LauncherUiPerformance.Start();
                _runtime.Render(width, height);
                LauncherUiPerformance.RecordPhase("contextRender", contextStarted);
                if (ModernGraphicsCompat.Active) RmlUiGpuCompositor.DrawNativeFrame(width, height);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LauncherUiPerformance.RecordNativeRender(started);
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
            if (Visible && _lobbyMode && !_gameplayOverlay) RefreshLobbyState(force: false);
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
            InputReleaseRequested?.Invoke();
            _restoreFocus = _runtime.FocusedElement();
            _windowsIme?.Cancel();
            _cocoaIme?.Cancel();
            _linuxIme?.Cancel();
            RetireAccessibility();
            _visible = false;
            _gameplayOverlay = false;
            _runtime.ReleaseInput();
            if (_pages != null) _pages.SetVisible(false);
            else _runtime.ShowDocument(_runtime.HomeDocument, false);
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
            _gameplayOverlay = false;
            _visible = true;
            if (_pages != null) _pages.SetVisible(true);
            else _runtime.ShowDocument(_runtime.HomeDocument, true);
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
            _pages?.Flush();
            RmlUiDocumentToken inputDocument = _runtime.CurrentInputDocument;
            if (!_runtime.FocusDocument(inputDocument, _restoreFocus))
                _runtime.FocusDocument(inputDocument,
                    _lobbyMode ? "lobby_hunter" : _multiplayerMode ? "play_quick" : "deploy");
            _nextStateRefresh = 0;
        }

        internal static void ShowGameplayMenu()
        {
            if (!_active) return;
            _runtime.VerifyOwnerThread();
            _gameplayOverlay = true;
            _visible = true;
            if (_pages != null) _pages.SetVisible(true);
            else _runtime.ShowDocument(_runtime.HomeDocument, true);
            _gamepad.Reset();
            GamepadContexts.MenuVisible = true;
            LauncherHunter.Wanted = false;
            LauncherLobbyVisuals.Active = false;
            if (!String.IsNullOrEmpty(_restoreFocus))
                _runtime.FocusDocument(_runtime.CurrentInputDocument, _restoreFocus);
        }

        public static void PointerMoved(double x, double y)
        {
            if (Visible) _runtime.Input.Dispatch(new(_runtime.CurrentInputDocument,
                RmlUiPlatformInputKind.PointerMove, RmlUiInputDevice.Pointer, X: x, Y: y, Modifiers: _inputModifiers));
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
            _runtime.Input.Dispatch(new(_runtime.CurrentInputDocument,
                down ? RmlUiPlatformInputKind.PointerDown : RmlUiPlatformInputKind.PointerUp,
                RmlUiInputDevice.Pointer, X: x, Y: y, Code: translated, Modifiers: _inputModifiers));
        }

        public static void PointerWheel(double deltaX, double deltaY)
        {
            if (Visible) _runtime.Input.Dispatch(new(_runtime.CurrentInputDocument,
                RmlUiPlatformInputKind.Wheel, RmlUiInputDevice.Pointer, Delta: deltaY, Modifiers: _inputModifiers));
        }

        public static void KeyDown(KeyboardKeyEventArgs e)
        {
            if (!Visible) return;
            _inputModifiers = RmlUiDesktopInput.Modifiers(e);
#if DEBUG
            if (e.Key == Keys.F9)
            {
                try
                {
                    var retiredPages = _pages;
                    _pages = null;
                    RmlUiCleanup.Run(ReportCleanup,
                        () => _windowsIme?.Cancel(), () => _cocoaIme?.Cancel(), () => _linuxIme?.Cancel(),
                        RetireAccessibility, () => PresentationRetiring?.Invoke(), () => retiredPages?.Dispose());
                    if (!_runtime.ReloadAssets())
                        throw new InvalidOperationException("Native RmlUi asset reload failed.");
                    _pages = _runtime.ProtocolVersion == RmlUiIntentRegistry.ProtocolVersion
                        ? new RmlUiLauncherPages(_runtime) : null;
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
                _runtime.Input.DiagnosticsEnabled = _diagnosticsVisible;
                SetBool("diagnostics_visible", _diagnosticsVisible);
                return;
            }
            if (e.Key == Keys.Escape)
            {
                HandleBack();
                return;
            }
            if (!RmlUiLinuxDesktopInput.Process(_linuxIme, e, released: false))
                RmlUiDesktopInput.KeyDown(_runtime, e);
        }

        public static void KeyUp(KeyboardKeyEventArgs e)
        {
            if (!Visible) return;
            _inputModifiers = RmlUiDesktopInput.Modifiers(e);
            if (!RmlUiLinuxDesktopInput.Process(_linuxIme, e, released: true))
                RmlUiDesktopInput.KeyUp(_runtime, e);
        }

        public static void TextInput(string text)
        {
            if (!Visible) return;
            if (_linuxIme?.SuppressCharacterCallback() == true) return;
            if (_runtime.TryGetTextInputState(out var scope))
                _runtime.Input.Dispatch(new(scope.Document, RmlUiPlatformInputKind.TextCommitted,
                    FocusEpoch: scope.FocusEpoch, Text: text));
            else _runtime.Input.Text(text); // Compatibility bridge/platform committed Unicode path.
        }

        private static void WindowFocusChanged(FocusedChangedEventArgs e)
        {
            if (!_active || e.IsFocused) return;
            InputReleaseRequested?.Invoke();
            _windowsIme?.Cancel();
            _cocoaIme?.Cancel();
            _linuxIme?.Cancel();
            _runtime.Input.Dispatch(new(_runtime.CurrentInputDocument, RmlUiPlatformInputKind.FocusLost));
            _inputModifiers = default;
            _gamepad.Reset();
        }

        private static void DetachPlatformInput()
        {
            var cocoaAccessibility = _cocoaAccessibility;
            var windowsAccessibility = _windowsAccessibility;
            var linuxAccessibility = _linuxAccessibility;
            var accessibility = _accessibility;
            var inputWindow = _inputWindow;
            var windowsIme = _windowsIme;
            var cocoaIme = _cocoaIme;
            var linuxIme = _linuxIme;
            _cocoaAccessibility = null;
            _windowsAccessibility = null;
            _linuxAccessibility = null;
            _accessibility = null;
            _inputWindow = null;
            _windowsIme = null;
            _cocoaIme = null;
            _linuxIme = null;
            _inputModifiers = default;
            RmlUiCleanup.Run(ReportCleanup,
                () => cocoaAccessibility?.Dispose(), () => windowsAccessibility?.Dispose(),
                () => linuxAccessibility?.Dispose(), () => accessibility?.Retire(),
                () => { if (inputWindow != null) inputWindow.FocusedChanged -= WindowFocusChanged; },
                () => windowsIme?.Dispose(), () => cocoaIme?.Dispose(), () => linuxIme?.Dispose());
        }

        private static unsafe void AttachAccessibility(RenderWindow window)
        {
            _accessibility = new RmlUiAccessibilityService();
            if (OperatingSystem.IsMacOS())
                _cocoaAccessibility = new RmlUiCocoaAccessibility(GLFW.GetCocoaView(window.WindowPtr), _accessibility);
            else if (OperatingSystem.IsWindows())
                _windowsAccessibility = new RmlUiWindowsAccessibility(GLFW.GetWin32Window(window.WindowPtr), _accessibility);
            else if (OperatingSystem.IsLinux())
                _linuxAccessibility = new RmlUiLinuxAccessibility(_accessibility);
        }

        private static unsafe void PublishAccessibility(RmlUiAccessibilitySnapshot snapshot)
        {
            _cocoaAccessibility?.Publish(snapshot);
            _windowsAccessibility?.Publish(snapshot);
            if (_linuxAccessibility == null || _inputWindow == null) return;
            GLFW.GetWindowSize(_inputWindow.WindowPtr, out int width, out int height);
            int x = 0, y = 0;
            bool screenCoordinates = false;
            try { screenCoordinates = GLFW.GetX11Display() != 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException) { }
            if (screenCoordinates) GLFW.GetWindowPos(_inputWindow.WindowPtr, out x, out y);
            _linuxAccessibility.Publish(snapshot, new(x, y, width, height), screenCoordinates);
        }

        private static void RetireAccessibility()
        {
            if (_accessibility == null) return;
            _accessibility.Retire();
            PublishAccessibility(_accessibility.Snapshot);
        }

        public static void Shutdown()
        {
            if (_tearingDown || (!_active && !_runtime.Active)) return;
            RetireNativePresentation();
            if (!_failed) LauncherUiRuntime.CompleteCleanShutdown();
            RmlUiCleanup.Try(() => Mods.DebugLog.Line("rmlui", "RmlUi shut down"));
        }

        private static void RetireNativePresentation()
        {
            if (_tearingDown) return;
            _tearingDown = true;
            _visible = false;
            var retiring = PresentationRetiring;
            var releaseInput = InputReleaseRequested;
            var pages = _pages;
            PresentationRetiring = null;
            InputReleaseRequested = null;
            _pages = null;
            try
            {
                RmlUiCleanup.Run(ReportCleanup,
                    () => releaseInput?.Invoke(), () => retiring?.Invoke(), DetachPlatformInput,
                    () => pages?.Dispose(), RmlUiGpuCompositor.ReleaseNativeFrame, _runtime.Shutdown);
            }
            finally
            {
                try { ResetPresentationState(); }
                finally { _tearingDown = false; }
            }
        }

        private static void ResetPresentationState()
        {
            _active = false;
            _visible = false;
            _gameplayOverlay = false;
            _commands.Clear();
            _captureDirectory = null;
            _captureFrames = 0;
            _lobbyMode = false;
            _multiplayerMode = false;
            _lobbyPlayerCount = 0;
            _lobbySnapshot = null;
            _restoreFocus = "";
            BackRequested = null;
            GamepadActionOverride = null; InputReleaseRequested = null; PresentationRetiring = null;
            Array.Clear(_lobbyPlayers);
            Array.Clear(_fadingPlayers);
            RmlUiCleanup.Run(ReportCleanup, _gamepad.Reset, LauncherLobbyVisuals.Reset,
                LauncherHunter.Reset, () => GamepadContexts.MenuVisible = false);
        }

        private static void ReportCleanup(Exception error) => Mods.DebugLog.Exception("rmlui", error);
        private static string RendererLabel => ModernGraphicsCompat.Active
            ? GraphicsBackendPolicy.DisplayName(ModernGraphicsCompat.DeviceIdentity.Backend).ToUpperInvariant() : "OPENGL";

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
                SetText("renderer_name", RendererLabel + " // RMLUI 6.3");
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

            SetText("lobby_chat_history", snapshot.Chat.Length == 0 ? "No messages yet." : string.Join("\n", snapshot.Chat.TakeLast(6)));
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
            byte readyMask = 0;
            for (int i = 0; i < _lobbyPlayers.Length; i++)
            {
                LobbyDisplayPlayer player = _lobbyPlayers[i];
                bool occupied = player.Occupied;
                if (occupied)
                {
                    mask |= (byte)(1 << i);
                    if (player.Ready) { readyCount++; readyMask |= (byte)(1 << i); }
                }

            }

            LauncherLobbyVisuals.Active = true;
            LauncherLobbyVisuals.OccupiedMask = mask;
            LauncherLobbyVisuals.ReadyMask = readyMask;
            LauncherLobbyVisuals.Starting = snapshot.Phase == SessionPhase.Starting;
            LauncherLobbyVisuals.CountdownSeconds = snapshot.CountdownSeconds;
            for (int slot = 0; slot < 8; slot++) LauncherLobbyVisuals.SetIdentity(slot, _lobbyPlayers[slot].NetSlot);
            for (int slot = 0; slot < LauncherLobbyFormation.Capacity; slot++)
                LauncherLobbyVisuals.SetHunter(slot,
                    _lobbyPlayers[slot].Occupied ? _lobbyPlayers[slot].Hunter : Hunter.Samus);

            MatchDefinition? match = snapshot.Match;
            if (match is { } definition)
            {
                LauncherBackdrop.Set(LauncherBackdropScene.Lobby, definition.RoomKey);
                SetText("lobby_map", definition.RoomKey.ToUpperInvariant());
                _lobbyPreview.Present(definition.RoomKey, SetText, SetBool, _pages!.Manager.Lifetime(_pages.Document));
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
            SetBool("lobby_can_start", LobbyPresentation.From(snapshot).CanStart);
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
            if (_pages != null) RmlUiLobbyBindings.Present(_pages, snapshot);

            if (display > 0)
                ConfigureLobbyHunter(_lobbyPlayers[0], LauncherLobbyFormation.At(0));
            else
                LauncherHunter.Wanted = false;

            if (force)
            {
                SetText("renderer_name", RendererLabel + " // RMLUI 6.3 // LIVE LOBBY");
                SetText("ui_cost", "RMLUI LIVE LOBBY // DIRECT GPU OVERLAY");
            }
        }

        private static readonly RmlUi.Presenters.LobbyMapPreview _lobbyPreview = new();
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
            float halfWidth = (stage.HunterRight - stage.HunterLeft) * 0.5f;
            LauncherHunter.Left = 0.5f - halfWidth;
            LauncherHunter.Top = stage.HunterTop;
            LauncherHunter.Right = 0.5f + halfWidth;
            LauncherHunter.Bottom = stage.HunterBottom;
            LauncherHunter.DistanceScale = stage.HunterDistanceScale;
            LauncherHunter.TransparentBackground = true;
        }

        private static void DrainActions()
        {
            for (int i = 0; i < 64 && _runtime.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                if (_pages != null && _pages.HandleIntent(intent, out RmlUiIntent forwarded))
                {
                    if (forwarded.Kind == 0) continue;
                    intent = forwarded;
                }
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

        // Restore the selected Home activity after a route owns the chamber.
        // This also rebinds the real Hunter to the correct preview recipe.
        internal static void RestoreHomeBackdrop()
        {
            if (!Active || _lobbyMode || _pages == null) return;
            ApplyStageAction(new RmlUiIntent(RmlUiIntentKind.StageSelect,
                _pages.SelectedActivityIndex, default, 0));
        }

        private static void ApplyStageAction(RmlUiIntent intent)
        {
            string stage = intent.Argument switch
            {
                0 => "quick", 1 => "browser", 2 => "offline", 3 => "adventure", 4 => "training", _ => ""
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
                case "training":
                    LauncherBackdrop.Set(LauncherBackdropScene.Training,
                        "MP3 PROVING GROUND");
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
            if (GamepadActionOverride?.Invoke(action) == true) return;
            if (action == UiAction.Back)
            {
                HandleBack();
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
            if (_runtime.TryGetTextInputState(out var composition) && composition.Composing) return;
            var document = _runtime.CurrentInputDocument;
            _runtime.Input.Dispatch(new(document, RmlUiPlatformInputKind.KeyDown, RmlUiInputDevice.Gamepad,
                Code: key, Modifiers: (RmlUiInputModifiers)modifiers));
            _runtime.Input.Dispatch(new(document, RmlUiPlatformInputKind.KeyUp, RmlUiInputDevice.Gamepad,
                Code: key, Modifiers: (RmlUiInputModifiers)modifiers));
        }

        private static void HandleBack()
        {
            if (_runtime.TryGetTextInputState(out var composition) && composition.Composing)
            {
                _runtime.Input.Dispatch(new(composition.Document, RmlUiPlatformInputKind.CompositionCancel,
                    RmlUiInputDevice.InputMethod, composition.FocusEpoch));
                _windowsIme?.Cancel();
                _cocoaIme?.Cancel();
                _linuxIme?.Cancel();
                return;
            }
            if (BackRequested?.Invoke() == true) return;
            if (_pages != null && _pages.Back(out RmlUiIntent forwarded))
            {
                if (forwarded.Kind is RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview)
                    ApplyStageAction(forwarded);
                else if (forwarded.Kind != 0) _commands.Enqueue(forwarded);
                return;
            }
            if (!_runtime.Back())
                _commands.Enqueue(_runtime.CreateIntent(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.News));
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
            if (!_active) return;
            if (_pages != null) _pages.SetField(id, value);
            else _runtime.SetField(_runtime.HomeDocument, id, value);
        }

        internal static string ReadFieldValue(string id) => _active
            ? _pages?.ReadField(id) ?? _runtime.ReadField(_runtime.HomeDocument, id) : string.Empty;

        private static void SetText(string name, string value)
        {
            if (!_active) return;
            if (_pages != null) _pages.SetText(name, value);
            else _runtime.SetText(_runtime.HomeDocument, name, value);
        }

        private static void SetBool(string name, bool value)
        {
            if (!_active) return;
            if (_pages != null) _pages.SetBool(name, value);
            else _runtime.SetBool(_runtime.HomeDocument, name, value);
        }

        private static void Fail(string message, Exception ex, LauncherUiFailure failure = LauncherUiFailure.Runtime)
        {
            _failed = true;
            LauncherUiRuntime.RecordNativeFailure(failure);
            RetireNativePresentation();
            RmlUiCleanup.Run(null, () => Mods.DebugLog.Line("rmlui", $"Native UI {message}: {ex.Message}"),
                () => Mods.DebugLog.Exception("rmlui", ex));
        }

    }
}
#endif
