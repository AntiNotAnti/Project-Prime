using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SDL;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using PrimeGamepadState = MphRead.Mods.Input.GamepadState;

namespace MphRead
{
    internal enum SdlSoftwarePacingMode
    {
        Disabled,
        ExplicitCap,
        Minimized
    }

    internal readonly record struct SdlSoftwarePacingPolicy(
        SdlSoftwarePacingMode Mode, int FrameRateCap, int EffectiveRate)
    {
        public static SdlSoftwarePacingPolicy Resolve(int frameRateCap, bool minimized)
        {
            if (minimized)
            {
                return new SdlSoftwarePacingPolicy(SdlSoftwarePacingMode.Minimized,
                    frameRateCap, Mods.Render.FrameTiming.SimulationHz);
            }
            if (frameRateCap == Mods.Render.FrameTiming.DisplayRate)
            {
                return new SdlSoftwarePacingPolicy(SdlSoftwarePacingMode.Disabled,
                    frameRateCap, 0);
            }
            return new SdlSoftwarePacingPolicy(SdlSoftwarePacingMode.ExplicitCap,
                frameRateCap, frameRateCap);
        }
    }

    internal readonly record struct SdlFramePaceDecision(bool ShouldWait,
        double DeadlineSeconds);

    internal sealed class SdlFramePacer
    {
        private const double LargeLatenessSeconds = 0.25;
        private bool _initialized;
        private SdlSoftwarePacingPolicy _policy;
        private long _discontinuities;
        private double _nextDeadline;

        public SdlFramePaceDecision Plan(double now, SdlSoftwarePacingPolicy policy,
            long discontinuities)
        {
            if (policy.EffectiveRate <= 0)
            {
                _initialized = false;
                return default;
            }

            double interval = 1d / policy.EffectiveRate;
            if (!_initialized || policy != _policy || discontinuities != _discontinuities)
            {
                _initialized = true;
                _policy = policy;
                _discontinuities = discontinuities;
                _nextDeadline = now + interval;
                return default;
            }

            if (now > _nextDeadline)
            {
                if (now - _nextDeadline >= LargeLatenessSeconds)
                {
                    _nextDeadline = now + interval;
                    return default;
                }
                // Skip deadlines already missed and leave the next future
                // deadline for the following call. Rendering the already-late
                // frame now avoids halving throughput when work itself takes
                // slightly longer than the requested interval; retaining the
                // future deadline prevents an unbounded catch-up burst.
                while (_nextDeadline <= now)
                {
                    _nextDeadline += interval;
                }
                return default;
            }

            double deadline = _nextDeadline;
            _nextDeadline += interval;
            return new SdlFramePaceDecision(deadline > now, deadline);
        }
    }

    /// <summary>
    /// SDL owns the window and event pump on this thread. SDL types stop at
    /// this file: callers receive the same neutral input snapshot consumed by
    /// the compatibility scene and the same fixed-step frame loop used by
    /// deterministic tests.
    /// </summary>
    public unsafe sealed class SdlGameHost : IGameWindowHost, Mods.IPauseMenuHost,
        IGamepadHapticsSink
    {
        private SdlGpuBackend? _backend;
        private SDL_Window* _window;
        private readonly SDL_WindowID _windowId;
        private readonly GameWindowFrameLoop _frameLoop = new();
        private SdlFramePacer _framePacer = new();
        private readonly HashSet<int> _keys = new();
        private readonly HashSet<int> _mouseButtons = new();
        private readonly List<WindowKeyEvent> _keyEvents = new();
        private readonly List<WindowMouseButtonEvent> _mouseButtonEvents = new();
        private readonly StringBuilder _text = new();
        private readonly SdlCompatibilityInput _compatibilityInput = new();
        // SDL's gamepad events carry a joystick id. Keep the opened handles
        // alive for the duration of their connection so SDL can normalize
        // mappings and reports consistently; the active handle is the one
        // published through the legacy input adapter.
        private readonly Dictionary<uint, IntPtr> _gamepads = new();
        private readonly Dictionary<uint, DesktopPenState> _pens = new();
        private readonly StylusInput _stylus = DesktopStylusInput.Input;
        private PrimeGamepadState _gamepadState;
        private Vector2 _mousePosition;
        private Vector2 _relativeMouse;
        private Vector2 _wheel;
        private Vector2i _logicalSize;
        private Vector2i _framebufferSize;
        private bool _focused = true;
        private readonly SceneHostLifetime _lifetime = new();
        private Func<bool>? _suspendFrame;
        private readonly SceneWindowModePreference _windowModePreference = new();
        private bool _disposed;
        private bool _sdlInitialized;
        private bool _fullscreen;
        private bool _cursorCaptured;
        private bool _activationDeferred;
        private uint _activeGamepadId;
        private bool _hasActiveGamepad;
        private bool _gyroSensorEnabled;
        private ControllerCapabilityOwner? _capabilityOwner;
        private uint? _capturedPenId;
        private ScenePresentation? _presentation;
        internal KeyboardState Keyboard => _compatibilityInput.Keyboard;
        internal MouseState Mouse => _compatibilityInput.Mouse;

        public SdlGameHost(Vector2i? initialSize = null, string title = "Project Prime — SDL GPU",
            bool showWindow = true)
        {
            Vector2i size = initialSize ?? new Vector2i(1280, 720);
            if (size.X <= 0 || size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(initialSize));

            try
            {
                Mods.DebugLog.Line("sdl", "initialization starting");
                // Pen input has its own neutral path below. Disable SDL's
                // compatibility synthesis before initializing the event system.
                SDL3.SDL_SetHint("SDL_PEN_MOUSE_EVENTS", "0");
                SDL3.SDL_SetHint("SDL_PEN_TOUCH_EVENTS", "0");
                SDL_InitFlags initFlags = SDL_InitFlags.SDL_INIT_VIDEO
                    | SDL_InitFlags.SDL_INIT_EVENTS
                    | SDL_InitFlags.SDL_INIT_GAMEPAD
                    | SDL_InitFlags.SDL_INIT_SENSOR;
                if (!SDL3.SDL_Init(initFlags))
                {
                    throw new InvalidOperationException($"SDL initialization failed: {SDL3.SDL_GetError()}");
                }
                _sdlInitialized = true;
                Mods.DebugLog.Line("sdl", $"initialization complete; version={SDL3.SDL_GetVersion()} "
                    + $"video-driver={SDL3.SDL_GetCurrentVideoDriver() ?? "unknown"}");

                SDL_WindowFlags flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE
                    | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY
                    | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
                Mods.DebugLog.Line("sdl", "window creation starting");
                _window = SDL3.SDL_CreateWindow(title, size.X, size.Y, flags);
                if (_window == null)
                {
                    throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL_GetError()}");
                }
                _windowId = SDL3.SDL_GetWindowID(_window);
                ReadWindowSize(out _logicalSize, out _framebufferSize);
                Mods.DebugLog.Line("sdl", $"window creation complete; logical={_logicalSize.X}x{_logicalSize.Y} "
                    + $"framebuffer={_framebufferSize.X}x{_framebufferSize.Y} fullscreen=false visible={showWindow}");
                Mods.DebugLog.Line("gpu", "device creation starting");
                _backend = new SdlGpuBackend(_window, _logicalSize, _framebufferSize);
                Mods.DebugLog.Line("gpu", $"device creation complete; backend={_backend.Info.Name} "
                    + $"driver={_backend.Info.Driver} shaders={_backend.Info.ShaderFormats}");
                Mods.DebugLog.Line("gpu", $"swapchain creation complete; format={_backend.Surface.SwapchainFormat} "
                    + $"present={_backend.Surface.PresentMode} framebuffer={_backend.Surface.FramebufferSize.X}x{_backend.Surface.FramebufferSize.Y}");
                Mods.WindowMode.SetFullscreenState(false);
                if (!SDL3.SDL_StartTextInput(_window))
                {
                    throw new InvalidOperationException($"SDL text input setup failed: {SDL3.SDL_GetError()}");
                }
                if (showWindow)
                {
                    SDL3.SDL_ShowWindow(_window);
                }
                _focused = (SDL3.SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
                _capabilityOwner = ControllerCapabilities.TryAcquire(
                    ControllerBackend.Sdl, replaceCurrent: true);
                _capabilityOwner?.Publish(
                    ControllerCapabilitySnapshot.Disconnected(ControllerBackend.Sdl));
                GamepadDesktop.Publish(default);
                GamepadHaptics.Attach(this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Vector2i LogicalSize => _logicalSize;
        public Vector2i FramebufferSize => _framebufferSize;
        public bool IsMinimized => (_backend?.Surface.IsMinimized ?? false)
            || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0;
        public bool IsFocused => _focused;

        // The deterministic render-tool adapter uses the same SDL device and
        // compatibility input objects as the interactive scene host. Keep
        // these accessors internal so native SDL types do not leak into the
        // utility surface or the public client API.
        internal SdlGpuBackend Backend => _backend
            ?? throw new InvalidOperationException("SDL GPU backend is not initialized.");
        internal SdlCompatibilityInput CompatibilityInput => _compatibilityInput;
        internal bool ToolWindowVisible
        {
            get => (SDL3.SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0;
            set => SetToolWindowVisible(value);
        }

        internal void SetToolWindowVisible(bool visible)
        {
            if (visible) SDL3.SDL_ShowWindow(_window);
            else SDL3.SDL_HideWindow(_window);
        }

        /// <summary>
        /// Maps the SDL window and clears stale input without taking focus.
        /// The desktop transition coordinator calls this before the first
        /// submitted scene frame so the launcher remains the active surface
        /// until the scene is genuinely ready.
        /// </summary>
        internal void ShowForPreparation()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ResetInput();
            SetWindowFocusable(false);
            SDL3.SDL_ShowWindow(_window);
            _activationDeferred = true;
            _focused = false;
        }

        /// <summary>Hides the SDL window and neutralizes all held input.</summary>
        internal void Hide()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ResetInput();
            SDL3.SDL_HideWindow(_window);
            _activationDeferred = false;
            _focused = false;
        }

        /// <summary>Takes focus for an already mapped SDL scene.</summary>
        internal void Activate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activationDeferred = false;
            SetWindowFocusable(true);
            SDL3.SDL_RaiseWindow(_window);
            _focused = true;
        }

        private void SetWindowFocusable(bool focusable)
        {
            if (!SDL3.SDL_SetWindowFocusable(_window, focusable))
            {
                throw new InvalidOperationException($"SDL window focusability change failed: {SDL3.SDL_GetError()}");
            }
        }

        /// <summary>
        /// Clears keyboard, mouse, gamepad, stylus and relative-mode state.
        /// This is safe to call at a presentation boundary and is deliberately
        /// separate from focus acquisition.
        /// </summary>
        internal void ResetInput()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ClearInputAfterFocusLoss();
        }

        /// <summary>
        /// Seeds the hidden host onto the shell's display before its first
        /// fullscreen/windowed transition. Subsequent rounds keep SDL's own
        /// user-adjusted geometry because the host is persistent.
        /// </summary>
        internal void SetInitialPosition(Vector2i position)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!SDL3.SDL_SetWindowPosition(_window, position.X, position.Y))
            {
                throw new InvalidOperationException(
                    $"SDL window placement failed: {SDL3.SDL_GetError()}");
            }
        }

        internal void AttachToolPresentation(ScenePresentation presentation)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            if (_presentation != null && !ReferenceEquals(_presentation, presentation))
            {
                throw new InvalidOperationException("An SDL presentation is already attached to this host.");
            }
            _presentation = presentation;
            SyncPresentationSize();
        }

        internal void DetachToolPresentation()
        {
            _presentation = null;
        }

        public Vector2i ClientLocation
        {
            get
            {
                int x = 0, y = 0;
                SDL3.SDL_GetWindowPosition(_window, &x, &y);
                return new Vector2i(x, y);
            }
        }

        public Vector2i ClientSize => _logicalSize;

        public void Focus()
        {
            Activate();
        }

        public void SyncTopmost(bool menuOpen)
        {
            // SDL owns this window and its always-on-top attribute. WindowMode
            // only mirrors the active mode for launcher/pause-menu state.
            SDL3.SDL_SetWindowAlwaysOnTop(_window, _fullscreen && !menuOpen);
        }

        public void ToggleFullscreen()
        {
            ApplyWindowMode(_fullscreen ? Mods.WindowStartMode.Windowed
                : Mods.WindowStartMode.BorderlessFullscreen);
        }

        public void Run(IGameWindowFrameClient client)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (client == null) throw new ArgumentNullException(nameof(client));

            Stopwatch clock = Stopwatch.StartNew();
            double previous = clock.Elapsed.TotalSeconds;
            while (!_lifetime.CloseRequested && !_lifetime.SceneStopRequested)
            {
                int frameRateCap = Mods.Render.FrameTiming.FrameRateCap;
                _backend!.ApplyPresentPolicy(frameRateCap);
                SdlSoftwarePacingPolicy pacing = SdlSoftwarePacingPolicy.Resolve(frameRateCap,
                    IsWindowMinimized());
                SdlFramePaceDecision decision = _framePacer.Plan(clock.Elapsed.TotalSeconds,
                    pacing, Mods.Render.FrameTiming.Discontinuities);
                if (decision.ShouldWait)
                {
                    WaitUntil(clock, decision.DeadlineSeconds);
                }
                ProcessEvents();
                if (_lifetime.CloseRequested) break;
                if (_suspendFrame?.Invoke() == true) continue;
                double now = clock.Elapsed.TotalSeconds;
                double elapsed = Math.Clamp(now - previous, 0, 0.25);
                previous = now;
                WindowInputSnapshot snapshot = BuildSnapshot();
                SyncPresentationSize();
                _compatibilityInput.Apply(snapshot);
                DispatchCompatibilityInput(snapshot);
                _frameLoop.Tick(elapsed, snapshot, client, _backend!);
                DrainCaptureResults();
            }
        }

        // Results owns a nested Avalonia dispatcher on this same thread. Keep SDL
        // window/gamepad events responsive without advancing the completed Scene.
        internal bool PumpResultsEvents()
        {
            ProcessEvents();
            Mods.PauseMenu.TakeWindowRect(ClientLocation, ClientSize);
            return !_lifetime.CloseRequested;
        }

        /// <summary>
        /// Keeps SDL's authoritative controller ownership live while Avalonia
        /// owns the visible shell. No scene is advanced and no hidden window
        /// receives keyboard or pointer input.
        /// </summary>
        internal bool PumpShellEvents()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ProcessEvents();
            return !_lifetime.CloseRequested;
        }

        private bool IsWindowMinimized()
            => (SDL3.SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0
                || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0;

        private static void WaitUntil(Stopwatch clock, double deadlineSeconds)
        {
            double remaining = deadlineSeconds - clock.Elapsed.TotalSeconds;
            if (remaining > 0)
            {
                SDL3.SDL_DelayPrecise(checked((ulong)Math.Ceiling(remaining * 1_000_000_000d)));
            }
        }

        /// <summary>
        /// Pumps native events for a deterministic render utility without
        /// entering the wall-clock fixed-step loop. The utility owns the
        /// simulation/draw cadence; SDL still owns window resize, close,
        /// cursor, keyboard, mouse and gamepad dispatch.
        /// </summary>
        internal bool PumpToolEvents()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ProcessEvents();
            SyncPresentationSize();
            WindowInputSnapshot snapshot = BuildSnapshot();
            _compatibilityInput.Apply(snapshot);
            DispatchCompatibilityInput(snapshot);
            return !_lifetime.CloseRequested;
        }

        /// <summary>
        /// Runs the ScenePresentation used by the desktop viewer. World
        /// loading and draw-item preparation happen once before the first
        /// frame; the SDL client submits the resulting sealed snapshot to the
        /// GPU backend.
        /// </summary>
        public void RunScene(Scene scene, Action<ScenePresentation> configure, Action? beforeCleanup = null,
            Action? started = null, Func<bool>? suspendFrame = null,
            SceneExitPresentation exitPresentation = SceneExitPresentation.HideWindow,
            ulong transitionGeneration = 0, Action<ulong>? firstFramePresented = null,
            Action<ulong>? windowPrepared = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(configure);
            _lifetime.Run(scene, () =>
            {
                ProcessEvents();
                if (_lifetime.CloseRequested) return;
                ResetInput();
                _keyEvents.Clear();
                _mouseButtonEvents.Clear();
                _text.Clear();
                _wheel = Vector2.Zero;
                _compatibilityInput.Apply(BuildSnapshot());
                GamepadDesktop.Publish(default);
                Mods.Render.FrameTiming.Reset();
                _framePacer = new SdlFramePacer();
                _suspendFrame = suspendFrame;
                _presentation = new ScenePresentation(scene, _logicalSize, _compatibilityInput.Keyboard,
                    _compatibilityInput.Mouse, SetTitle, StopScene);
                _presentation.EnableDesktopLook();
                configure(_presentation);
                _presentation.OnLoad();
                _windowModePreference.ApplyIfChanged(Mods.WindowMode.Startup, ApplyWindowMode);
                if (windowPrepared == null)
                {
                    ShowForPreparation();
                    Activate();
                }
                else
                {
                    if (transitionGeneration == 0)
                        throw new ArgumentOutOfRangeException(nameof(transitionGeneration),
                            "A prepared-window callback requires a transition generation.");
                    windowPrepared(transitionGeneration);
                }
                started?.Invoke();
                using var frameClient = new SdlSceneFrameClient(this, _presentation,
                    transitionGeneration, firstFramePresented);
                Run(frameClient);
            }, () => beforeCleanup?.Invoke(), () =>
            {
                try
                {
                    _backend!.EndScene();
                    DrainCaptureResults();
                }
                finally
                {
                    try
                    {
                        if (_presentation != null) _presentation.DoCleanup();
                        else scene.CloseWorld();
                    }
                    finally
                    {
                        _presentation = null;
                        _suspendFrame = null;
                        if (exitPresentation == SceneExitPresentation.HideWindow)
                        {
                            Hide();
                        }
                        else
                        {
                            // Results are presented over the still-live scene
                            // by the GUI. Leave it mapped, but release all
                            // focus-sensitive state before the shell return.
                            ResetInput();
                            _activationDeferred = false;
                            _focused = false;
                        }
                    }
                }
            });
        }

        public bool CloseRequested => _lifetime.CloseRequested;
        public void StopScene() => _lifetime.StopScene();
        void Mods.IPauseMenuHost.Close() => StopScene();
        public void Close() => _lifetime.Close();

        public void SetTitle(string title)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (title == null) throw new ArgumentNullException(nameof(title));
            SDL3.SDL_SetWindowTitle(_window, title);
        }

        public void SetCursorCaptured(bool captured)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!SDL3.SDL_SetWindowRelativeMouseMode(_window, captured))
            {
                throw new InvalidOperationException($"SDL relative mouse mode failed: {SDL3.SDL_GetError()}");
            }
            if (captured) SDL3.SDL_HideCursor();
            else SDL3.SDL_ShowCursor();
            _cursorCaptured = captured;
        }

        public void ApplyWindowMode(Mods.WindowStartMode mode)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool fullscreen = mode == Mods.WindowStartMode.BorderlessFullscreen;
            SDL3.SDL_SetWindowBordered(_window, !fullscreen);
            if (!SDL3.SDL_SetWindowFullscreen(_window, fullscreen))
            {
                throw new InvalidOperationException($"SDL fullscreen transition failed: {SDL3.SDL_GetError()}");
            }
            _fullscreen = fullscreen;
            Mods.WindowMode.SetFullscreenState(fullscreen);
            SyncTopmost(Mods.PauseMenu.Open);
            ReadWindowSize(out _logicalSize, out _framebufferSize);
            _backend!.Resize(_logicalSize, _framebufferSize);
        }

        private void ProcessEvents()
        {
            _keyEvents.Clear();
            _mouseButtonEvents.Clear();
            _text.Clear();
            _relativeMouse = Vector2.Zero;
            _wheel = Vector2.Zero;
            SyncGyroSensor();
            SDL_Event evt = default;
            while (SDL3.SDL_PollEvent(&evt))
            {
                switch ((SDL_EventType)evt.type)
                {
                    case SDL_EventType.SDL_EVENT_QUIT:
                        _lifetime.Close();
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                        if (IsOurWindow(evt.window.windowID)) _lifetime.Close();
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                        if (IsOurWindow(evt.window.windowID))
                            _focused = !_activationDeferred;
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            _focused = false;
                            ClearInputAfterFocusLoss();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                    case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            ReadWindowSize(out _logicalSize, out _framebufferSize);
                            _backend!.Resize(_logicalSize, _framebufferSize);
                            SyncPresentationSize();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            _logicalSize = new Vector2i(evt.window.data1, evt.window.data2);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_RESTORED:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            ReadWindowSize(out _logicalSize, out _framebufferSize);
                            _backend!.Resize(_logicalSize, _framebufferSize);
                            SyncPresentationSize();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    case SDL_EventType.SDL_EVENT_KEY_UP:
                        if (IsOurWindow(evt.key.windowID)) HandleKey(evt.key);
                        break;
                    case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                        if (IsOurWindow(evt.text.windowID) && evt.text.text != null)
                        {
                            string? text = Marshal.PtrToStringUTF8((IntPtr)evt.text.text);
                            if (!string.IsNullOrEmpty(text)) _text.Append(text);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                        if (IsOurWindow(evt.motion.windowID)
                            && !IsSyntheticPenMouseId((uint)evt.motion.which))
                        {
                            _mousePosition = new Vector2(evt.motion.x, evt.motion.y);
                            _relativeMouse += new Vector2(evt.motion.xrel, evt.motion.yrel);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                        if (IsOurWindow(evt.button.windowID)
                            && !IsSyntheticPenMouseId((uint)evt.button.which))
                            HandleMouseButton(evt.button);
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                        if (IsOurWindow(evt.wheel.windowID)
                            && !IsSyntheticPenMouseId((uint)evt.wheel.which))
                        {
                            _wheel += new Vector2(evt.wheel.x, evt.wheel.y);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                        OpenGamepad(evt.gdevice.which);
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                        CloseGamepad(evt.gdevice.which);
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                        HandleGamepadAxis(evt.gaxis);
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
                    case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
                        HandleGamepadButton(evt.gbutton);
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_SENSOR_UPDATE:
                        HandleGamepadSensor(evt.gsensor);
                        break;
                    case SDL_EventType.SDL_EVENT_PEN_PROXIMITY_IN:
                        if (_presentation is null || _focused)
                            HandlePenProximity(evt.pproximity, entered: true);
                        break;
                    case SDL_EventType.SDL_EVENT_PEN_PROXIMITY_OUT:
                        if (_presentation is null || _focused)
                            HandlePenProximity(evt.pproximity, entered: false);
                        break;
                    case SDL_EventType.SDL_EVENT_PEN_DOWN:
                    case SDL_EventType.SDL_EVENT_PEN_UP:
                        if (_presentation is null || _focused)
                            HandlePenTouch(evt.ptouch);
                        break;
                    case SDL_EventType.SDL_EVENT_PEN_MOTION:
                        if (_presentation is null || _focused)
                            HandlePenMotion(evt.pmotion);
                        break;
                    case SDL_EventType.SDL_EVENT_PEN_BUTTON_DOWN:
                    case SDL_EventType.SDL_EVENT_PEN_BUTTON_UP:
                        if (_presentation is null || _focused)
                            HandlePenButton(evt.pbutton);
                        break;
                    case SDL_EventType.SDL_EVENT_PEN_AXIS:
                        if (_presentation is null || _focused)
                            HandlePenAxis(evt.paxis);
                        break;
                }
            }
            GamepadDesktop.Publish(_gamepadState);
            GamepadHaptics.Pump();
        }

        private WindowInputSnapshot BuildSnapshot()
            => new(_keys, _mouseButtons, _mousePosition,
                _relativeMouse, _wheel, _text.ToString(), _focused,
                _keyEvents, _mouseButtonEvents,
                _presentation?.FrameAdvance ?? false);

        private void DispatchCompatibilityInput(WindowInputSnapshot snapshot)
        {
            Mods.ClientInputState.WindowFocused = snapshot.Focused;
            if (_presentation == null) return;
            bool shouldCapture = _focused
                && (_presentation.CameraMode == CameraMode.Player || _presentation.IsFreeCam)
                && !Mods.PauseMenu.Open
                && !_presentation.ShowCursor
                && !_presentation.FrameAdvance;
            if (shouldCapture != _cursorCaptured) SetCursorCaptured(shouldCapture);
            if (shouldCapture && _presentation.CanCaptureSimulationLook)
            {
                _presentation.RenderLook?.Add(snapshot.RelativeMouse.X, snapshot.RelativeMouse.Y);
                _presentation.SubmitMouseLook(snapshot.RelativeMouse);
            }
            else
            {
                DesktopStylusInput.Cancel();
                _capturedPenId = null;
                _presentation.ResetRenderLook();
            }
            foreach (WindowKeyEvent key in snapshot.KeyEvents)
            {
                if (!key.Down) continue;
                if (Mods.Chat.ChatBox.HandleKeyDown(key,
                    canOpen: !Mods.Network.ReplayPlayback.IsActive
                        && (_presentation.CameraMode == CameraMode.Player || _presentation.IsFreeCam)))
                {
                    continue;
                }
                if (key.Key == Keys.F11 || (key.Key == Keys.Enter && key.Alt))
                {
                    ToggleFullscreen();
                    continue;
                }
                if (key.Key == Keys.Space
                    && (Mods.Network.ReplayPlayback.IsActive || Mods.SpectatorMode.IsSpectating))
                {
                    if (Mods.Network.ReplayPlayback.IsActive)
                    {
                        _presentation.ToggleFreeCamera();
                    }
                    else
                    {
                        Mods.SpectatorMode.ToggleView(_presentation.World);
                    }
                    continue;
                }
                if (key.Key == Keys.Escape
                    && (_presentation.CameraMode == CameraMode.Player || _presentation.IsFreeCam)
                    && Mods.PauseMenu.HandleEscape(this, _presentation.World))
                {
                    continue;
                }
                if (key.Key == Keys.Escape)
                {
                    StopScene();
                    continue;
                }
                _presentation.OnKeyDown(key);
            }
            foreach (WindowMouseButtonEvent button in snapshot.MouseButtonEvents)
            {
                if (button.Button == MouseButton.Left)
                    _presentation.OnMouseClick(button.Down);
            }
            if (snapshot.RelativeMouse != Vector2.Zero)
            {
                _presentation.OnMouseMove(snapshot.RelativeMouse.X, snapshot.RelativeMouse.Y);
            }
            if (snapshot.Wheel.Y != 0) _presentation.OnMouseWheel(snapshot.Wheel.Y);
            for (int i = 0; i < snapshot.Text.Length; i++) Mods.Chat.ChatBox.HandleText(snapshot.Text[i]);
        }

        private void HandleKey(SDL_KeyboardEvent evt)
        {
            Keys key = TranslateKey(evt.scancode);
            if (key == Keys.Unknown) return;
            bool down = evt.down;
            if (down) _keys.Add((int)key);
            else _keys.Remove((int)key);
            _keyEvents.Add(new WindowKeyEvent(key, down, evt.repeat, TranslateModifiers(evt.mod)));
        }

        private void HandleMouseButton(SDL_MouseButtonEvent evt)
        {
            MouseButton button = TranslateMouseButton(evt.button);
            if (button == MouseButton.Last) return;
            bool down = evt.down;
            if (down) _mouseButtons.Add((int)button);
            else _mouseButtons.Remove((int)button);
            _mouseButtonEvents.Add(new WindowMouseButtonEvent(button, down));
        }

        private void ClearInputAfterFocusLoss()
        {
            Mods.ClientInputState.WindowFocused = false;
            SetGyroSensor(enabled: false);
            _keys.Clear();
            _mouseButtons.Clear();
            _relativeMouse = Vector2.Zero;
            _gamepadState = default;
            _stylus.Cancel();
            _capturedPenId = null;
            _pens.Clear();
            GamepadGyro.Reset();
            GamepadHaptics.Stop();
            GamepadHaptics.Pump();
            if (_cursorCaptured)
            {
                SetCursorCaptured(false);
            }
        }

        private void SyncPresentationSize()
        {
            // ScenePresentation's Size is the drawable viewport for the SDL
            // path. Use device pixels so projection and the backend's target
            // agree on Retina displays as well as after a resize.
            Vector2i drawable = _framebufferSize.X > 0 && _framebufferSize.Y > 0
                ? _framebufferSize : _logicalSize;
            if (_presentation != null && _presentation.Size != drawable)
            {
                _presentation.Size = drawable;
                _presentation.OnResize();
            }
        }

        private void OpenGamepad(SDL_JoystickID id)
        {
            uint rawId = (uint)id;
            if (_gamepads.ContainsKey(rawId)) return;
            SDL_Gamepad* handle = SDL3.SDL_OpenGamepad(id);
            if (handle == null) return;
            _gamepads.Add(rawId, (IntPtr)handle);
            if (!_hasActiveGamepad)
            {
                _activeGamepadId = rawId;
                _hasActiveGamepad = true;
                _gamepadState = new PrimeGamepadState
                {
                    Connected = true,
                    Name = SDL3.SDL_GetGamepadName(handle) ?? "SDL gamepad",
                    Family = ControllerFamilyFrom(SDL3.SDL_GetGamepadType(handle))
                };
                PublishActiveGamepadCapabilities(rawId, handle);
                // Bias belongs to the physical device. A newly active device
                // must never inherit calibration from the previous one.
                GamepadGyro.ResetDevice();
                SyncGyroSensor();
            }
        }

        private void CloseGamepad(SDL_JoystickID id)
        {
            uint rawId = (uint)id;
            bool wasActive = _hasActiveGamepad && rawId == _activeGamepadId;
            if (wasActive) SetGyroSensor(enabled: false);
            if (wasActive)
            {
                GamepadHaptics.Stop();
                GamepadHaptics.Pump();
            }
            if (_gamepads.Remove(rawId, out IntPtr pointer))
            {
                SDL3.SDL_CloseGamepad((SDL_Gamepad*)pointer);
            }
            if (!wasActive) return;
            GamepadGyro.ResetDevice();
            _hasActiveGamepad = false;
            _gamepadState = default;
            foreach (KeyValuePair<uint, IntPtr> gamepad in _gamepads)
            {
                _activeGamepadId = gamepad.Key;
                _hasActiveGamepad = true;
                SDL_Gamepad* handle = (SDL_Gamepad*)gamepad.Value;
                _gamepadState.Connected = true;
                _gamepadState.Name = SDL3.SDL_GetGamepadName(handle) ?? "SDL gamepad";
                _gamepadState.Family = ControllerFamilyFrom(
                    SDL3.SDL_GetGamepadType(handle));
                PublishActiveGamepadCapabilities(gamepad.Key, handle);
                break;
            }
            if (!_hasActiveGamepad)
            {
                _capabilityOwner?.Publish(
                    ControllerCapabilitySnapshot.Disconnected(ControllerBackend.Sdl));
            }
            SyncGyroSensor();
        }

        private void HandleGamepadAxis(SDL_GamepadAxisEvent evt)
        {
            if (!_hasActiveGamepad || (uint)evt.which != _activeGamepadId) return;
            _gamepadState.Connected = true;
            float normalized = evt.value / 32767f;
            switch ((SDL_GamepadAxis)evt.axis)
            {
                case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX: _gamepadState.LeftX = normalized; break;
                case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY: _gamepadState.LeftY = -normalized; break;
                case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX: _gamepadState.RightX = normalized; break;
                case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY: _gamepadState.RightY = -normalized; break;
                case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER: _gamepadState.LeftTrigger = Math.Clamp(evt.value / 32767f, 0, 1); break;
                case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER: _gamepadState.RightTrigger = Math.Clamp(evt.value / 32767f, 0, 1); break;
            }
        }

        private void HandleGamepadButton(SDL_GamepadButtonEvent evt)
        {
            if (!_hasActiveGamepad || (uint)evt.which != _activeGamepadId) return;
            _gamepadState.Connected = true;
            GamepadButtons button = (SDL_GamepadButton)evt.button switch
            {
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => GamepadButtons.A,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => GamepadButtons.B,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => GamepadButtons.X,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => GamepadButtons.Y,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => GamepadButtons.LeftBumper,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => GamepadButtons.RightBumper,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => GamepadButtons.Back,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => GamepadButtons.Start,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => GamepadButtons.LeftThumb,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => GamepadButtons.RightThumb,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => GamepadButtons.DpadUp,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => GamepadButtons.DpadRight,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => GamepadButtons.DpadDown,
                SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => GamepadButtons.DpadLeft,
                _ => GamepadButtons.None
            };
            if (button != GamepadButtons.None)
            {
                if (evt.down) _gamepadState.Buttons |= button;
                else _gamepadState.Buttons &= ~button;
            }
        }

        private void HandleGamepadSensor(SDL_GamepadSensorEvent evt)
        {
            if (!_hasActiveGamepad || !_gyroSensorEnabled
                || (uint)evt.which != _activeGamepadId
                || evt.sensor != (int)SDL_SensorType.SDL_SENSOR_GYRO) return;
            // SDL specifies gamepad gyro values in radians/second around the
            // controller's right-handed X/Y/Z axes. Preserve its sensor
            // timestamp (nanoseconds) instead of assigning render arrival time.
            double seconds = (evt.sensor_timestamp != 0
                ? evt.sensor_timestamp : evt.timestamp) / 1_000_000_000d;
            GamepadGyro.SubmitRadiansPerSecond(
                new Vector3(evt.data[0], evt.data[1], evt.data[2]), seconds);
        }

        private void SyncGyroSensor()
        {
            bool requested = _hasActiveGamepad
                && Mods.InputSettings.GamepadGyroEnabled
                && _focused
                && !Mods.PauseMenu.Open
                && (_presentation == null || _presentation.CanCaptureSimulationLook);
            if (requested == _gyroSensorEnabled) return;
            SetGyroSensor(requested);
        }

        private void SetGyroSensor(bool enabled)
        {
            if (!_hasActiveGamepad
                || !_gamepads.TryGetValue(_activeGamepadId, out IntPtr pointer))
            {
                _gyroSensorEnabled = false;
                GamepadGyro.Reset();
                return;
            }
            SDL_Gamepad* handle = (SDL_Gamepad*)pointer;
            bool applied = SDL3.SDL_SetGamepadSensorEnabled(handle,
                SDL_SensorType.SDL_SENSOR_GYRO, enabled);
            _gyroSensorEnabled = enabled && applied;
            if (!_gyroSensorEnabled) GamepadGyro.Reset();
        }

        private void PublishActiveGamepadCapabilities(uint rawId, SDL_Gamepad* handle)
        {
            bool? hasGyroscope = TryHasGyroscope(handle);
            bool? hasLeftTrigger = TryHasAxis(handle,
                SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            bool? hasRightTrigger = TryHasAxis(handle,
                SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
            bool? hasAnalogTriggers = hasLeftTrigger == false
                || hasRightTrigger == false
                ? false
                : hasLeftTrigger == true && hasRightTrigger == true
                    ? true : null;
            _capabilityOwner?.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl,
                $"gamepad:{rawId}",
                _gamepadState.Name,
                _gamepadState.Family,
                hasGyroscope,
                // SDL 3.4.16 exposes the rumble operation but no non-invasive
                // capability query. Do not probe by actuating the device.
                hasRumble: null,
                hasAnalogTriggers: hasAnalogTriggers));
        }

        private static bool? TryHasGyroscope(SDL_Gamepad* handle)
        {
            try
            {
                return SDL3.SDL_GamepadHasSensor(handle,
                    SDL_SensorType.SDL_SENSOR_GYRO);
            }
            catch (Exception ex) when (ex is DllNotFoundException
                || ex is EntryPointNotFoundException || ex is BadImageFormatException)
            {
                return null;
            }
        }

        private static bool? TryHasAxis(SDL_Gamepad* handle, SDL_GamepadAxis axis)
        {
            try
            {
                return SDL3.SDL_GamepadHasAxis(handle, axis);
            }
            catch (Exception ex) when (ex is DllNotFoundException
                || ex is EntryPointNotFoundException || ex is BadImageFormatException)
            {
                return null;
            }
        }

        private void HandlePenProximity(SDL_PenProximityEvent evt, bool entered)
        {
            uint id = (uint)evt.which;
            if (!entered)
            {
                DesktopPenState state = GetPenState(id);
                bool ended = state.InProximity
                    && _stylus.PointerProximityExit(PenSample(id, state, 0));
                if (_capturedPenId == id)
                {
                    // A malformed or truncated event stream may omit the
                    // matching proximity sample. Only the captured pen may
                    // use the fallback cancellation; another pen leaving
                    // proximity must not cancel its owner.
                    if (!ended) _stylus.Cancel();
                    _capturedPenId = null;
                }
                _pens.Remove(id);
                return;
            }
            PointerCoordinateKind coordinateKind = PenCoordinateKind(evt.which);
            _pens[id] = new DesktopPenState
            {
                Tool = PointerToolKind.Stylus,
                InProximity = true,
                CoordinateKind = coordinateKind,
                // SDL reports direct pen positions in a window-relative
                // coordinate stream. Keep the known logical-pixel scale in
                // the neutral sample so high-DPI windows do not make direct
                // pen aim depend on the framebuffer size.
                LogicalDisplayScale = coordinateKind == PointerCoordinateKind.Direct
                    ? LogicalDisplayScale() : 1,
                // SDL 3.4.16 exposes Direct/Indirect classification but no
                // mapped tablet extent. Zero is intentional: the neutral
                // layer uses its conservative fallback until an adapter can
                // provide a real normalized extent; no physical dimensions
                // are invented here.
                MappedExtentX = 0,
                MappedExtentY = 0
            };
        }

        private void HandlePenTouch(SDL_PenTouchEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            uint id = (uint)evt.which;
            DesktopPenState state = GetPenState(id);
            state.Contact = evt.down;
            state.InProximity = true;
            state.X = evt.x;
            state.Y = evt.y;
            state.Tool = evt.eraser ? PointerToolKind.Eraser : PenTool(evt.pen_state);
            state.Buttons = PenButtons(evt.pen_state);
            _pens[id] = state;
            ConfigureStylus();
            PointerSample sample = PenSample(id, state, evt.timestamp);
            if (state.Contact)
            {
                if (_stylus.PointerDown(sample)) _capturedPenId = id;
            }
            else if (_capturedPenId == id)
            {
                _stylus.PointerUp(sample);
                _capturedPenId = null;
            }
        }

        private void HandlePenMotion(SDL_PenMotionEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            uint id = (uint)evt.which;
            DesktopPenState state = GetPenState(id);
            state.X = evt.x;
            state.Y = evt.y;
            state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
            state.InProximity = true;
            state.Tool = PenTool(evt.pen_state);
            state.Buttons = PenButtons(evt.pen_state);
            _pens[id] = state;
            PointerSample sample = PenSample(id, state, evt.timestamp);
            if (!state.Contact)
            {
                _stylus.PointerProximityMove(sample);
                return;
            }
            if (_capturedPenId != id || !_stylus.Active)
            {
                if (!_stylus.Active) _capturedPenId = null;
                return;
            }
            ConfigureStylus();
            _stylus.PointerMove(sample);
        }

        private void HandlePenButton(SDL_PenButtonEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            uint id = (uint)evt.which;
            DesktopPenState state = GetPenState(id);
            state.X = evt.x;
            state.Y = evt.y;
            state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
            state.InProximity = true;
            state.Tool = PenTool(evt.pen_state);
            state.Buttons = PenButtons(evt.pen_state);
            _pens[id] = state;
            _stylus.UpdateButtonState(PenSample(id, state, evt.timestamp));
        }

        private void HandlePenAxis(SDL_PenAxisEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            uint id = (uint)evt.which;
            DesktopPenState state = GetPenState(id);
            state.X = evt.x;
            state.Y = evt.y;
            state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
            state.InProximity = true;
            state.Tool = PenTool(evt.pen_state);
            state.Buttons = PenButtons(evt.pen_state);
            if (evt.axis == SDL_PenAxis.SDL_PEN_AXIS_PRESSURE)
                state.Pressure = Math.Clamp(evt.value, 0, 1);
            _pens[id] = state;
            _stylus.UpdateButtonState(PenSample(id, state, evt.timestamp));
        }

        private void ConfigureStylus()
        {
            PlayerEntity? local = _presentation?.World.LocalPlayer;
            _stylus.Configure(Mods.InputSettings.StylusAimingEnabled,
                Mods.InputSettings.StylusSensitivity, Mods.InputSettings.StylusInvertY,
                Mods.InputSettings.StylusPressureToFire,
                Mods.InputSettings.StylusPressureThreshold, density: 1);
            _stylus.ConfigureGestures(Mods.InputSettings.StylusClassicGestures,
                Mods.InputSettings.StylusDoubleTapJump,
                Mods.InputSettings.StylusFlickBoost,
                flickContext: IsStylusFlickContext(local != null,
                    local?.IsAltForm == true), density: 1);
        }

        internal static bool IsStylusFlickContext(bool hasLocalPlayer, bool isAltForm)
            => hasLocalPlayer && isAltForm;

        internal static bool IsSyntheticPenMouseId(uint id)
            => id == (uint)SDL3.SDL_PEN_MOUSEID;

        private DesktopPenState GetPenState(uint id)
            => _pens.TryGetValue(id, out DesktopPenState state) ? state
                : new DesktopPenState { Tool = PointerToolKind.Stylus };

        private PointerCoordinateKind PenCoordinateKind(SDL_PenID id)
            => SDL3.SDL_GetPenDeviceType(id) switch
            {
                SDL_PenDeviceType.SDL_PEN_DEVICE_TYPE_DIRECT
                    => PointerCoordinateKind.Direct,
                SDL_PenDeviceType.SDL_PEN_DEVICE_TYPE_INDIRECT
                    => PointerCoordinateKind.Indirect,
                _ => PointerCoordinateKind.Unknown
            };

        private float LogicalDisplayScale()
        {
            if (_logicalSize.X <= 0 || _logicalSize.Y <= 0
                || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0)
            {
                return 1;
            }
            float x = _framebufferSize.X / (float)_logicalSize.X;
            float y = _framebufferSize.Y / (float)_logicalSize.Y;
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0)
            {
                return 1;
            }
            return (x + y) * .5f;
        }

        private static PointerSample PenSample(uint id, DesktopPenState state,
            ulong timestampNanoseconds)
            => new(unchecked((int)id), state.Tool, state.X, state.Y,
                state.Pressure, state.Buttons,
                (long)Math.Min(timestampNanoseconds / 1_000_000UL, (ulong)long.MaxValue),
                state.CoordinateKind, state.LogicalDisplayScale,
                state.MappedExtentX, state.MappedExtentY);

        private static PointerToolKind PenTool(SDL_PenInputFlags flags)
            => (flags & SDL_PenInputFlags.SDL_PEN_INPUT_ERASER_TIP) != 0
                ? PointerToolKind.Eraser : PointerToolKind.Stylus;

        private static StylusButtons PenButtons(SDL_PenInputFlags flags)
        {
            StylusButtons result = StylusButtons.None;
            if ((flags & SDL_PenInputFlags.SDL_PEN_INPUT_BUTTON_1) != 0)
                result |= StylusButtons.Primary;
            if ((flags & (SDL_PenInputFlags.SDL_PEN_INPUT_BUTTON_2
                | SDL_PenInputFlags.SDL_PEN_INPUT_BUTTON_3)) != 0)
                result |= StylusButtons.Secondary;
            return result;
        }

        private static ControllerFamily ControllerFamilyFrom(SDL_GamepadType type)
            => type switch
            {
                SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOX360
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOXONE => ControllerFamily.Xbox,
                SDL_GamepadType.SDL_GAMEPAD_TYPE_PS3
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5 => ControllerFamily.PlayStation,
                SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_GAMECUBE => ControllerFamily.Nintendo,
                _ => ControllerFamily.Generic
            };

        void IGamepadHapticsSink.Apply(in HapticPattern pattern)
        {
            if (!_hasActiveGamepad || !_gamepads.TryGetValue(_activeGamepadId,
                out IntPtr pointer)) return;
            SDL3.SDL_RumbleGamepad((SDL_Gamepad*)pointer, pattern.LowFrequency,
                pattern.HighFrequency, pattern.DurationMilliseconds);
        }

        void IGamepadHapticsSink.Stop()
        {
            if (_hasActiveGamepad && _gamepads.TryGetValue(_activeGamepadId,
                out IntPtr pointer)) SDL3.SDL_RumbleGamepad((SDL_Gamepad*)pointer, 0, 0, 0);
        }

        private bool IsOurWindow(SDL_WindowID windowId) => windowId == _windowId;

        private void ReadWindowSize(out Vector2i logical, out Vector2i framebuffer)
        {
            int logicalWidth = 0, logicalHeight = 0, framebufferWidth = 0, framebufferHeight = 0;
            SDL3.SDL_GetWindowSize(_window, &logicalWidth, &logicalHeight);
            if (!SDL3.SDL_GetWindowSizeInPixels(_window, &framebufferWidth, &framebufferHeight))
            {
                framebufferWidth = logicalWidth;
                framebufferHeight = logicalHeight;
            }
            logical = new Vector2i(logicalWidth, logicalHeight);
            framebuffer = new Vector2i(framebufferWidth, framebufferHeight);
        }

        private static KeyModifiers TranslateModifiers(SDL_Keymod modifiers)
        {
            ushort raw = (ushort)modifiers;
            KeyModifiers result = 0;
            if ((raw & (0x0001 | 0x0002)) != 0) result |= KeyModifiers.Shift;
            if ((raw & (0x0040 | 0x0080)) != 0) result |= KeyModifiers.Control;
            if ((raw & (0x0100 | 0x0200)) != 0) result |= KeyModifiers.Alt;
            if ((raw & (0x0400 | 0x0800)) != 0) result |= KeyModifiers.Super;
            if ((raw & 0x1000) != 0) result |= KeyModifiers.NumLock;
            if ((raw & 0x2000) != 0) result |= KeyModifiers.CapsLock;
            return result;
        }

        private static MouseButton TranslateMouseButton(byte button)
            => button switch
            {
                1 => MouseButton.Left,
                2 => MouseButton.Middle,
                3 => MouseButton.Right,
                4 => MouseButton.Button4,
                5 => MouseButton.Button5,
                6 => MouseButton.Button6,
                7 => MouseButton.Button7,
                8 => MouseButton.Button8,
                _ => MouseButton.Last
            };

        private static Keys TranslateKey(SDL_Scancode scancode)
            => scancode switch
            {
                SDL_Scancode.SDL_SCANCODE_SPACE => Keys.Space,
                SDL_Scancode.SDL_SCANCODE_APOSTROPHE => Keys.Apostrophe,
                SDL_Scancode.SDL_SCANCODE_COMMA => Keys.Comma,
                SDL_Scancode.SDL_SCANCODE_MINUS => Keys.Minus,
                SDL_Scancode.SDL_SCANCODE_PERIOD => Keys.Period,
                SDL_Scancode.SDL_SCANCODE_SLASH => Keys.Slash,
                SDL_Scancode.SDL_SCANCODE_0 => Keys.D0,
                SDL_Scancode.SDL_SCANCODE_1 => Keys.D1,
                SDL_Scancode.SDL_SCANCODE_2 => Keys.D2,
                SDL_Scancode.SDL_SCANCODE_3 => Keys.D3,
                SDL_Scancode.SDL_SCANCODE_4 => Keys.D4,
                SDL_Scancode.SDL_SCANCODE_5 => Keys.D5,
                SDL_Scancode.SDL_SCANCODE_6 => Keys.D6,
                SDL_Scancode.SDL_SCANCODE_7 => Keys.D7,
                SDL_Scancode.SDL_SCANCODE_8 => Keys.D8,
                SDL_Scancode.SDL_SCANCODE_9 => Keys.D9,
                SDL_Scancode.SDL_SCANCODE_SEMICOLON => Keys.Semicolon,
                SDL_Scancode.SDL_SCANCODE_EQUALS => Keys.Equal,
                SDL_Scancode.SDL_SCANCODE_A => Keys.A,
                SDL_Scancode.SDL_SCANCODE_B => Keys.B,
                SDL_Scancode.SDL_SCANCODE_C => Keys.C,
                SDL_Scancode.SDL_SCANCODE_D => Keys.D,
                SDL_Scancode.SDL_SCANCODE_E => Keys.E,
                SDL_Scancode.SDL_SCANCODE_F => Keys.F,
                SDL_Scancode.SDL_SCANCODE_G => Keys.G,
                SDL_Scancode.SDL_SCANCODE_H => Keys.H,
                SDL_Scancode.SDL_SCANCODE_I => Keys.I,
                SDL_Scancode.SDL_SCANCODE_J => Keys.J,
                SDL_Scancode.SDL_SCANCODE_K => Keys.K,
                SDL_Scancode.SDL_SCANCODE_L => Keys.L,
                SDL_Scancode.SDL_SCANCODE_M => Keys.M,
                SDL_Scancode.SDL_SCANCODE_N => Keys.N,
                SDL_Scancode.SDL_SCANCODE_O => Keys.O,
                SDL_Scancode.SDL_SCANCODE_P => Keys.P,
                SDL_Scancode.SDL_SCANCODE_Q => Keys.Q,
                SDL_Scancode.SDL_SCANCODE_R => Keys.R,
                SDL_Scancode.SDL_SCANCODE_S => Keys.S,
                SDL_Scancode.SDL_SCANCODE_T => Keys.T,
                SDL_Scancode.SDL_SCANCODE_U => Keys.U,
                SDL_Scancode.SDL_SCANCODE_V => Keys.V,
                SDL_Scancode.SDL_SCANCODE_W => Keys.W,
                SDL_Scancode.SDL_SCANCODE_X => Keys.X,
                SDL_Scancode.SDL_SCANCODE_Y => Keys.Y,
                SDL_Scancode.SDL_SCANCODE_Z => Keys.Z,
                SDL_Scancode.SDL_SCANCODE_LEFTBRACKET => Keys.LeftBracket,
                SDL_Scancode.SDL_SCANCODE_BACKSLASH => Keys.Backslash,
                SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET => Keys.RightBracket,
                SDL_Scancode.SDL_SCANCODE_GRAVE => Keys.GraveAccent,
                SDL_Scancode.SDL_SCANCODE_ESCAPE => Keys.Escape,
                SDL_Scancode.SDL_SCANCODE_RETURN => Keys.Enter,
                SDL_Scancode.SDL_SCANCODE_TAB => Keys.Tab,
                SDL_Scancode.SDL_SCANCODE_BACKSPACE => Keys.Backspace,
                SDL_Scancode.SDL_SCANCODE_INSERT => Keys.Insert,
                SDL_Scancode.SDL_SCANCODE_DELETE => Keys.Delete,
                SDL_Scancode.SDL_SCANCODE_RIGHT => Keys.Right,
                SDL_Scancode.SDL_SCANCODE_LEFT => Keys.Left,
                SDL_Scancode.SDL_SCANCODE_DOWN => Keys.Down,
                SDL_Scancode.SDL_SCANCODE_UP => Keys.Up,
                SDL_Scancode.SDL_SCANCODE_PAGEUP => Keys.PageUp,
                SDL_Scancode.SDL_SCANCODE_PAGEDOWN => Keys.PageDown,
                SDL_Scancode.SDL_SCANCODE_HOME => Keys.Home,
                SDL_Scancode.SDL_SCANCODE_END => Keys.End,
                SDL_Scancode.SDL_SCANCODE_CAPSLOCK => Keys.CapsLock,
                SDL_Scancode.SDL_SCANCODE_SCROLLLOCK => Keys.ScrollLock,
                SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR => Keys.NumLock,
                SDL_Scancode.SDL_SCANCODE_PRINTSCREEN => Keys.PrintScreen,
                SDL_Scancode.SDL_SCANCODE_PAUSE => Keys.Pause,
                SDL_Scancode.SDL_SCANCODE_F1 => Keys.F1,
                SDL_Scancode.SDL_SCANCODE_F2 => Keys.F2,
                SDL_Scancode.SDL_SCANCODE_F3 => Keys.F3,
                SDL_Scancode.SDL_SCANCODE_F4 => Keys.F4,
                SDL_Scancode.SDL_SCANCODE_F5 => Keys.F5,
                SDL_Scancode.SDL_SCANCODE_F6 => Keys.F6,
                SDL_Scancode.SDL_SCANCODE_F7 => Keys.F7,
                SDL_Scancode.SDL_SCANCODE_F8 => Keys.F8,
                SDL_Scancode.SDL_SCANCODE_F9 => Keys.F9,
                SDL_Scancode.SDL_SCANCODE_F10 => Keys.F10,
                SDL_Scancode.SDL_SCANCODE_F11 => Keys.F11,
                SDL_Scancode.SDL_SCANCODE_F12 => Keys.F12,
                SDL_Scancode.SDL_SCANCODE_LSHIFT => Keys.LeftShift,
                SDL_Scancode.SDL_SCANCODE_LCTRL => Keys.LeftControl,
                SDL_Scancode.SDL_SCANCODE_LALT => Keys.LeftAlt,
                SDL_Scancode.SDL_SCANCODE_LGUI => Keys.LeftSuper,
                SDL_Scancode.SDL_SCANCODE_RSHIFT => Keys.RightShift,
                SDL_Scancode.SDL_SCANCODE_RCTRL => Keys.RightControl,
                SDL_Scancode.SDL_SCANCODE_RALT => Keys.RightAlt,
                SDL_Scancode.SDL_SCANCODE_RGUI => Keys.RightSuper,
                SDL_Scancode.SDL_SCANCODE_MENU => Keys.Menu,
                _ => Keys.Unknown
            };

        /// <summary>
        /// Capture readbacks complete after a submitted GPU frame. Delivery is
        /// deliberately kept on the SDL host thread so no SDL transfer or
        /// fence API crosses into the recording worker.
        /// </summary>
        private void DrainCaptureResults()
        {
            if (_backend == null) return;
            while (_backend.TryDequeueCapture(out RenderCaptureResult? result))
            {
                MphRead.Export.ScreenCapture.Deliver(result!);
            }
            while (_backend.TryDequeueCaptureFailure(out RenderCaptureFailure? failure))
            {
                Console.Error.WriteLine($"[capture] request {failure!.RequestId} ({failure.Delivery}) failed: {failure.Error}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _lifetime.Dispose();
            _disposed = true;
            _stylus.Cancel();
            _capturedPenId = null;
            GamepadGyro.ResetDevice();
            GamepadHaptics.Stop();
            GamepadHaptics.Pump();
            GamepadHaptics.Detach(this);
            _capabilityOwner?.Dispose();
            _capabilityOwner = null;
            GamepadDesktop.Publish(default);
            try
            {
                foreach (IntPtr pointer in _gamepads.Values)
                {
                    SDL3.SDL_CloseGamepad((SDL_Gamepad*)pointer);
                }
                _gamepads.Clear();
                if (_window != null)
                {
                    SDL3.SDL_StopTextInput(_window);
                    if (_backend != null)
                    {
                        try
                        {
                            _backend.FlushCaptures();
                            DrainCaptureResults();
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[capture] final SDL GPU drain failed: {ex.Message}");
                        }
                        _backend.Dispose();
                    }
                    SDL3.SDL_DestroyWindow(_window);
                }
            }
            finally
            {
                _fullscreen = false;
                Mods.WindowMode.SetFullscreenState(false);
                if (_sdlInitialized) SDL3.SDL_Quit();
            }
        }

        private struct DesktopPenState
        {
            public float X, Y, Pressure;
            public StylusButtons Buttons;
            public PointerToolKind Tool;
            public bool Contact, InProximity;
            public PointerCoordinateKind CoordinateKind;
            public float LogicalDisplayScale;
            public float MappedExtentX, MappedExtentY;
        }
    }

    /// <summary>
    /// Determines whether a reused SDL host remains mapped after a scene's
    /// cleanup. The GUI keeps it mapped for results; standalone callers retain
    /// the historical hide-on-exit behavior.
    /// </summary>
    public enum SceneExitPresentation
    {
        HideWindow,
        KeepWindowVisible
    }

    /// <summary>
    /// Per-frame presentation acknowledgement with a one-shot external first
    /// frame callback. The presentation acknowledgement runs before the
    /// external callback, matching <see cref="GameWindowFrameLoop"/> ordering.
    /// </summary>
    internal sealed class SceneFirstFrameNotification
    {
        private readonly ulong _generation;
        private readonly Action<ulong>? _callback;
        private bool _reported;

        public SceneFirstFrameNotification(ulong generation, Action<ulong>? callback)
        {
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            _generation = generation;
            _callback = callback;
        }

        public bool Notify(Action acknowledgePresentation)
        {
            ArgumentNullException.ThrowIfNull(acknowledgePresentation);
            // Every successful submit acknowledges its active presentation;
            // only the external handoff callback is a first-frame one-shot.
            acknowledgePresentation();
            if (_reported) return false;
            // Set before the callback so re-entry from the coordinator cannot
            // report a second first frame.
            _reported = true;
            _callback?.Invoke(_generation);
            return true;
        }
    }

    internal sealed class SdlSceneFrameClient : IGameWindowFrameClient, IDisposable
    {
        private readonly SdlGameHost _host;
        private readonly ScenePresentation _presentation;
        private readonly KillcamController? _killcam;
        private readonly SceneFirstFrameNotification? _firstFrame;
        private uint _highlightFocusFrame = uint.MaxValue;
        private bool _highlightEndObserved;

        public SdlSceneFrameClient(SdlGameHost host, ScenePresentation presentation,
            ulong transitionGeneration = 0, Action<ulong>? firstFramePresented = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            if (firstFramePresented != null)
            {
                if (transitionGeneration == 0)
                    throw new ArgumentOutOfRangeException(nameof(transitionGeneration),
                        "A first-frame callback requires a non-zero transition generation.");
                _firstFrame = new SceneFirstFrameNotification(transitionGeneration,
                    firstFramePresented);
            }
            if (Mods.Network.AuthoritativePlay.Current != null)
                _killcam = new KillcamController(presentation, host.LogicalSize,
                    host.Keyboard, host.Mouse);
        }

        private ScenePresentation ActivePresentation
            => _killcam?.RenderedPresentation ?? _presentation;

        public void OnInput(WindowInputSnapshot input) { }

        public void AdvanceSimulation(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                _presentation.OnSimulationFrame();
                _killcam?.Advance();
                if (_killcam == null && Mods.Network.ReplayPlayback.CurrentHighlight
                    is ReplayHighlight highlight
                    && highlight.FocusFrame != _highlightFocusFrame
                    && Mods.SpectatorMode.IsSpectating)
                {
                    _highlightFocusFrame = highlight.FocusFrame;
                    _presentation.SpectatorCamera.FocusHighlight(_presentation, highlight);
                }
                if (_killcam == null && Mods.Network.ReplayPlayback.ShouldExitAtEnd)
                {
                    if (_highlightEndObserved) _host.StopScene();
                    else _highlightEndObserved = true;
                }
                else _highlightEndObserved = false;
            }
        }

        public void OnDrawFrame()
        {
            ScenePresentation active = ActivePresentation;
            if (_killcam?.IsPresenting != true && Mods.Input.GamepadInput.TakeMenuPress()
                && (_presentation.CameraMode == CameraMode.Player || _presentation.IsFreeCam))
            {
                Mods.PauseMenu.HandleEscape(_host, _presentation.World);
            }
            active.OnDrawFrame();
        }

        public void Render(RenderBackendFrame frame, IRenderBackend backend)
            => backend.Render(frame, ActivePresentation.CurrentRenderFrame);

        public void OnFramePresented()
        {
            if (_firstFrame == null) ActivePresentation.OnFramePresented();
            else _firstFrame.Notify(ActivePresentation.OnFramePresented);
        }
        public void AfterRenderFrame() => ActivePresentation.AfterRenderFrame();
        public void PumpPauseMenu() => Mods.PauseMenu.Poll(_host);
        public bool CanRenderFrame => !ActivePresentation.Exiting;
        public void Dispose() => _killcam?.Dispose();
    }

    /// <summary>
    /// A deliberately small SDL smoke client. It remains available as an
    /// explicit diagnostic helper; the SDL shipping path uses
    /// SdlSceneFrameClient.
    /// </summary>
    internal sealed class SdlTriangleFrameClient : IGameWindowFrameClient
    {
        private readonly RenderFrame _snapshot = new RenderFrame(1, 1);

        public int PresentedFrames { get; private set; }

        public void OnInput(WindowInputSnapshot input) { }
        public void AdvanceSimulation(int steps) { }
        public void OnDrawFrame()
        {
            _snapshot.Reset();
            _snapshot.CaptureState(OpenTK.Mathematics.Matrix4.Identity,
                OpenTK.Mathematics.Matrix4.Identity, OpenTK.Mathematics.Matrix4.Identity,
                OpenTK.Mathematics.Matrix4.Identity, OpenTK.Mathematics.Vector3.Zero,
                new OpenTK.Mathematics.Vector2i(1, 1),
                new OpenTK.Mathematics.Vector2i(1, 1), OpenTK.Mathematics.Vector4.UnitW,
                OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.Zero,
                OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.Zero,
                false, OpenTK.Mathematics.Vector4.Zero, 0, 0, default);
            _snapshot.Seal();
        }
        public void Render(RenderBackendFrame frame, IRenderBackend backend)
            => backend.Render(frame, _snapshot);
        public void OnFramePresented() => PresentedFrames++;
        public void AfterRenderFrame() { }
        public void PumpPauseMenu() { }
    }
}
