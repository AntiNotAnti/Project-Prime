#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Input;
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
        private static Task<int>? _stageRefresh;
        private static CancellationTokenSource? _stageRefreshCancel;
        private static string _stageRefreshRoom = "";
        private static bool _diagnosticsVisible;
        private static CancellationTokenSource? _socialCancel;
        private static Task<SocialSnapshot>? _socialLoad;
        private static Task<SocialMutationResult>? _socialMutation;
        private static Task<SocialLookupResult>? _socialLookup;
        private static SocialSnapshot? _socialSnapshot;
        private static SocialPlayer? _socialLookupPlayer;
        private static string _socialPendingAction = "";
        private static string _socialSearch = "";
        private static string _socialFingerprint = "";
        private static int _socialTab;
        private static long _nextSocialReload;
        private static bool _socialDrawerOpen;
        private static bool _socialFixture;

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
                _socialCancel?.Cancel();
                _socialCancel?.Dispose();
                _socialCancel = new CancellationTokenSource();
                _socialLoad = null;
                _socialMutation = null;
                _socialLookup = null;
                _socialSnapshot = null;
                _socialLookupPlayer = null;
                _socialPendingAction = "";
                _socialSearch = "";
                _socialFingerprint = "";
                _socialTab = 0;
                _nextSocialReload = 0;
                _socialDrawerOpen = false;
                _socialFixture = SocialCaptureRequested();
                GamepadContexts.MenuVisible = true;
                _gamepad.Reset();

                LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
                HubSnapshot snapshot = HubState.Capture();
                ConfigureHunter(snapshot);
                RefreshState(snapshot, force: true);
                BeginMenuStageRefresh(snapshot);
                if (_socialFixture)
                    SeedSocialCapture();
                else
                    BeginSocialLoad(force: true);
                Mods.DebugLog.Line("rmlui", $"RmlUi 6.3 POC active at {width}x{height} ({density:0.##}x density)");
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
                }

                long now = Environment.TickCount64;
                if (now >= _nextStateRefresh)
                {
                    HubSnapshot snapshot = HubState.Capture();
                    ConfigureHunter(snapshot);
                    RefreshState(snapshot, force: false);
                    if (!_socialFixture)
                    {
                        PollSocialWork();
                        if (_socialDrawerOpen && now >= _nextSocialReload)
                            BeginSocialLoad(force: false);
                        RefreshSocialUi();
                    }
                    _nextStateRefresh = now + 1000;
                }

                Mods.Input.GamepadDesktop.Poll();
                if (!GamepadContexts.Focused)
                    _gamepad.Reset();
                else
                    _gamepad.Update(GamepadManager.Snapshot, GamepadContext.Menu, now);

                PollMenuStageRefresh();
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
                    "RmlUi proof final composite: cinematic ground + engine Hunter + direct RmlUi overlay");
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
                // Give RmlUi overlays first refusal before handing Back to the
                // production shell. This keeps Escape/controller B inside the
                // social drawer and its context sheet.
                if (NativeBack() != 0)
                    return;
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
            _stageRefreshCancel?.Cancel();
            _stageRefreshCancel = null;
            _stageRefresh = null;
            _stageRefreshRoom = "";
            _socialCancel?.Cancel();
            _socialCancel?.Dispose();
            _socialCancel = null;
            _socialLoad = null;
            _socialMutation = null;
            _socialLookup = null;
            _socialSnapshot = null;
            _socialLookupPlayer = null;
            _socialPendingAction = "";
            _socialSearch = "";
            _socialFingerprint = "";
            _socialDrawerOpen = false;
            _socialFixture = false;
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

        private static void ConfigureHunter(HubSnapshot snapshot)
        {
            LauncherHunter.Wanted = snapshot.GameFilesReady;
            LauncherHunter.CanPresent = () => _active;
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

        private static void BeginMenuStageRefresh(HubSnapshot snapshot)
        {
            if (!snapshot.GameFilesReady || !ThumbnailBatch.CanRun || _stageRefresh != null)
                return;

            string room = LauncherBackdrop.RoomKey;
            if (String.IsNullOrWhiteSpace(room)
                || ThumbnailGenerator.HasCinematicPresentation(room))
                return;

            _stageRefreshRoom = room;
            _stageRefreshCancel = new CancellationTokenSource();
            CancellationToken token = _stageRefreshCancel.Token;
            Mods.DebugLog.Line("rmlui",
                $"refreshing cinematic menu stage for {room} in a background preview worker");
            _stageRefresh = Task.Run(() => ThumbnailBatch.Run(
                new[] { room },
                parallelism: 1,
                width: ThumbnailGenerator.ThumbnailWidth,
                height: ThumbnailGenerator.ThumbnailHeight,
                report: line => Mods.DebugLog.Line("rmlui", line),
                cancel: token,
                force: true), token);
        }

        private static void PollMenuStageRefresh()
        {
            Task<int>? task = _stageRefresh;
            if (task == null || !task.IsCompleted)
                return;

            string room = _stageRefreshRoom;
            CancellationTokenSource? completedCancel = _stageRefreshCancel;
            _stageRefresh = null;
            _stageRefreshRoom = "";
            _stageRefreshCancel = null;
            try
            {
                int written = task.GetAwaiter().GetResult();
                if (written > 0 || ThumbnailGenerator.HasCinematicPresentation(room))
                {
                    // The old GPU texture is still valid while the worker
                    // replaces the PNG, so the menu never flashes to black.
                    // Swap to the clean image now, on the GL owner thread.
                    LauncherPhoto.Invalidate();
                    LauncherBackdrop.Refresh();
                    Mods.DebugLog.Line("rmlui",
                        $"cinematic menu stage refreshed for {room}");
                }
            }
            catch (OperationCanceledException)
            {
                Mods.DebugLog.Line("rmlui", "cinematic menu-stage refresh cancelled");
            }
            catch (Exception ex)
            {
                // A stale backdrop is cosmetic. The frontend remains usable.
                Mods.DebugLog.Line("rmlui",
                    $"cinematic menu-stage refresh failed: {ex.Message}");
            }
            finally
            {
                completedCancel?.Dispose();
                // The player may have selected another activity while this
                // worker was rendering. Chain the current room now that the
                // single preview-worker slot is free.
                if (_active && !String.Equals(room, LauncherBackdrop.RoomKey,
                    StringComparison.OrdinalIgnoreCase))
                    BeginMenuStageRefresh(HubState.Capture());
            }
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
                if (action.StartsWith("stage:", StringComparison.Ordinal))
                {
                    ApplyStageAction(action);
                    continue;
                }
                if (action.StartsWith("social:", StringComparison.Ordinal))
                {
                    HandleSocialAction(action);
                    continue;
                }
                _commands.Enqueue(action);
            }
        }

        private static void ApplyStageAction(string action)
        {
            switch (action)
            {
                case "stage:quick":
                    LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer,
                        "MP3 PROVING GROUND");
                    break;
                case "stage:browser":
                    LauncherBackdrop.Set(LauncherBackdropScene.Play,
                        "MP1 SANCTORUS");
                    break;
                case "stage:offline":
                    LauncherBackdrop.Set(LauncherBackdropScene.Offline,
                        "MP3 PROVING GROUND");
                    break;
                case "stage:adventure":
                    LauncherBackdrop.Set(LauncherBackdropScene.Adventure,
                        "UNIT1 ALINOS LANDFALL");
                    break;
                default:
                    return;
            }

            HubSnapshot snapshot = HubState.Capture();
            ConfigureHunter(snapshot);
            BeginMenuStageRefresh(snapshot);
            Mods.DebugLog.Line("rmlui",
                $"menu stage -> {LauncherBackdrop.Scene}/{LauncherBackdrop.RoomKey} "
                + $"profile={LauncherMenuStage.Current.Name}");
        }

        private static void HandleGamepad(UiAction action)
        {
            if (!_active) return;
            if (action == UiAction.Back)
            {
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

        private static bool SocialCaptureRequested()
            => Array.Exists(Environment.GetCommandLineArgs(),
                value => value.Equals("-rmluisocial", StringComparison.OrdinalIgnoreCase));

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
            _stageRefreshCancel?.Cancel();
            _stageRefreshCancel = null;
            _stageRefresh = null;
            _stageRefreshRoom = "";
            _socialCancel?.Cancel();
            _socialCancel?.Dispose();
            _socialCancel = null;
            _socialLoad = null;
            _socialMutation = null;
            _socialLookup = null;
            _socialSnapshot = null;
            _socialLookupPlayer = null;
            _socialPendingAction = "";
            _socialSearch = "";
            _socialFingerprint = "";
            _socialDrawerOpen = false;
            _socialFixture = false;
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

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_clear")]
        private static extern void NativeSocialClear();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_add_row")]
        private static extern void NativeSocialAddRow(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string primeId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string activity,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detail,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string relation,
            int online, int friendOnline);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_add_home_friend")]
        private static extern void NativeSocialAddHomeFriend(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string primeId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string activity,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detail);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_commit")]
        private static extern void NativeSocialCommit();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_back")]
        private static extern int NativeBack();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_take_action")]
        private static extern int NativeTakeAction([Out] byte[] buffer, int capacity);
    }
}
#endif
