using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SDL;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using MphRead.Mods.Render;

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
        public static SdlSoftwarePacingPolicy Resolve(int frameRateCap, bool minimized,
            bool allowExplicitPacing = true)
        {
            if (minimized)
            {
                return new SdlSoftwarePacingPolicy(SdlSoftwarePacingMode.Minimized,
                    frameRateCap, Mods.Render.FrameTiming.SimulationHz);
            }
            if (frameRateCap == Mods.Render.FrameTiming.DisplayRate || !allowExplicitPacing)
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
        private SdlWindowController? _windowController;
        private readonly FrameTiming _frameTiming = new();
        private readonly GameWindowFrameLoop _frameLoop;
        private SdlFramePacer _framePacer = new();
        private readonly SdlInputHub _inputHub = new();
        private readonly SdlCompatibilityInput _compatibilityInput = new();
        // SDL's gamepad events carry a joystick id. Keep opened handles and
        // the active neutral state in one host-thread-owned hub.
        private readonly SdlGamepadHub _gamepadHub = new();
        private readonly SdlPointerHub _pointerHub;
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
        private bool? _focusEventThisPump;
        private ControllerCapabilityOwner? _capabilityOwner;
        private ScenePresentation? _presentation;
        private GameHostPresentationTracker? _presentationTracker;
        // A host used without the desktop shell still needs a local owner.
        // The normal launcher attaches the owner held by its transition
        // coordinator, so input ownership is never process-global.
        private readonly DesktopInputOwner _fallbackInputOwner = new();
        private DesktopInputOwner? _attachedInputOwner;
        internal KeyboardState Keyboard => _compatibilityInput.Keyboard;
        internal MouseState Mouse => _compatibilityInput.Mouse;

        private SDL_Window* NativeWindow
        {
            get => _windowController == null ? null : _windowController.NativeWindow;
        }

        private SDL_WindowID WindowId => _windowController?.WindowId ?? 0;

        internal DesktopInputOwner InputOwner
            => _attachedInputOwner ?? _fallbackInputOwner;

        /// <summary>
        /// Raised only when the host presentation contract changes.  Consumers
        /// must not infer application activation from focus loss; focus is one
        /// field in this event-driven snapshot and activation is an explicit
        /// operation on the owning coordinator.
        /// </summary>
        internal event Action<GameHostPresentationState>? PresentationStateChanged;

        internal GameHostPresentationState PresentationState
            => _presentationTracker?.Current ?? default;

        internal void AttachInputOwner(DesktopInputOwner owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _attachedInputOwner = owner;
            owner.Reset(_gamepadHub.Buttons);
        }

        internal void DetachInputOwner(DesktopInputOwner owner)
        {
            if (ReferenceEquals(_attachedInputOwner, owner))
            {
                _attachedInputOwner = null;
                _fallbackInputOwner.Reset(_gamepadHub.Buttons);
            }
        }

        public SdlGameHost(Vector2i? initialSize = null, string title = "Project Prime — SDL GPU",
            bool showWindow = true)
        {
            Vector2i size = initialSize ?? new Vector2i(1280, 720);
            if (size.X <= 0 || size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(initialSize));
            _frameLoop = new GameWindowFrameLoop(_frameTiming);
            _pointerHub = new SdlPointerHub(ConfigureStylus,
                sample => NativeBottomScreenPlatformBridge.TryPointerDown(sample),
                sample => NativeBottomScreenPlatformBridge.TryPointerMove(sample),
                sample => NativeBottomScreenPlatformBridge.TryPointerUp(sample),
                id => NativeBottomScreenPlatformBridge.CancelPointer(id),
                LogicalDisplayScale);

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

                Mods.DebugLog.Line("sdl", "window creation starting");
                _windowController = new SdlWindowController(size, title);
                SDL_Window* window = _windowController.NativeWindow;
                ReadWindowSize(out _logicalSize, out _framebufferSize);
                ReadWindowPosition(out Vector2i position);
                Mods.DebugLog.Line("sdl", $"window creation complete; logical={_logicalSize.X}x{_logicalSize.Y} "
                    + $"framebuffer={_framebufferSize.X}x{_framebufferSize.Y} fullscreen=false visible={showWindow}");
                Mods.DebugLog.Line("gpu", "device creation starting");
                _backend = new SdlGpuBackend(window, _logicalSize, _framebufferSize);
                Mods.DebugLog.Line("gpu", $"device creation complete; backend={_backend.Info.Name} "
                    + $"driver={_backend.Info.Driver} shaders={_backend.Info.ShaderFormats}");
                Mods.DebugLog.Line("gpu", $"swapchain creation complete; format={_backend.Surface.SwapchainFormat} "
                    + $"present={_backend.Surface.PresentMode} framebuffer={_backend.Surface.FramebufferSize.X}x{_backend.Surface.FramebufferSize.Y}");
                Mods.WindowMode.SetFullscreenState(false);
                if (!SDL3.SDL_StartTextInput(window))
                {
                    throw new InvalidOperationException($"SDL text input setup failed: {SDL3.SDL_GetError()}");
                }
                if (showWindow)
                {
                    _windowController.Show();
                }
                _focused = (SDL3.SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
                _windowController.SetFocused(_focused);
                _presentationTracker = new GameHostPresentationTracker(_logicalSize,
                    _framebufferSize, position, isVisible: showWindow,
                    isMinimized: IsWindowMinimized(), isFocused: _focused);
                _presentationTracker.Changed += state =>
                    PresentationStateChanged?.Invoke(state);
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
        public bool IsMinimized => _presentationTracker?.Current.IsMinimized
            ?? ((_backend?.Surface.IsMinimized ?? false)
                || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0);
        public bool IsFocused => _focused;

        // The deterministic render-tool adapter uses the same SDL device and
        // compatibility input objects as the interactive scene host. Keep
        // these accessors internal so native SDL types do not leak into the
        // utility surface or the public client API.
        internal SdlGpuBackend Backend => _backend
            ?? throw new InvalidOperationException("SDL GPU backend is not initialized.");
        internal SdlCompatibilityInput CompatibilityInput => _compatibilityInput;
        internal FrameTiming Timing => _frameTiming;
        internal bool ToolWindowVisible
        {
            get => (SDL3.SDL_GetWindowFlags(NativeWindow) & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0;
            set => SetToolWindowVisible(value);
        }

        internal void SetToolWindowVisible(bool visible)
        {
            if (visible) _windowController!.Show();
            else _windowController!.Hide();
            PublishPresentationState();
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
            _windowController!.Show();
            _activationDeferred = true;
            _windowController.SetActivationDeferred(true);
            _focused = false;
            _windowController.SetFocused(false);
            PublishPresentationState();
        }

        /// <summary>Hides the SDL window and neutralizes all held input.</summary>
        internal void Hide()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ResetInput();
            _windowController!.Hide();
            _activationDeferred = false;
            _windowController.SetActivationDeferred(false);
            _focused = false;
            _windowController.SetFocused(false);
            PublishPresentationState();
        }

        /// <summary>Takes focus for an already mapped SDL scene.</summary>
        internal void Activate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activationDeferred = false;
            _windowController!.SetActivationDeferred(false);
            SetWindowFocusable(true);
            _windowController!.RestoreIfMinimized();
            _windowController!.Raise();
            // Raising is an asynchronous request on several desktop window
            // managers. Do not claim focus early: relative mouse capture can
            // otherwise be re-enabled while another application is active.
            SynchronizeNativeFocus();
            PublishPresentationState();
        }

        private void SetWindowFocusable(bool focusable)
        {
            _windowController!.SetWindowFocusable(focusable);
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
            _windowController!.SetPosition(position);
            PublishPresentationState();
        }

        internal void AttachToolPresentation(ScenePresentation presentation)
        {
            ArgumentNullException.ThrowIfNull(presentation);
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
                SDL3.SDL_GetWindowPosition(NativeWindow, &x, &y);
                return new Vector2i(x, y);
            }
        }

        public Vector2i ClientSize => _logicalSize;

        public void Focus()
        {
            Activate();
        }

        public void ToggleFullscreen()
        {
            ApplyWindowMode(_fullscreen ? Mods.WindowStartMode.Windowed
                : Mods.WindowStartMode.BorderlessFullscreen);
        }

        public void Run(IGameWindowFrameClient client)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(client);

            Stopwatch clock = Stopwatch.StartNew();
            double previous = clock.Elapsed.TotalSeconds;
            while (!_lifetime.CloseRequested && !_lifetime.SceneStopRequested)
            {
                int frameRateCap = Mods.Render.FrameTiming.FrameRateCap;
                SdlGpuPresentPolicy presentPolicy = _backend!.ApplyPresentPolicy(frameRateCap);
                SdlSoftwarePacingPolicy pacing = SdlSoftwarePacingPolicy.Resolve(frameRateCap,
                    IsWindowMinimized(), presentPolicy.UsesSoftwarePacing);
                SdlFramePaceDecision decision = _framePacer.Plan(clock.Elapsed.TotalSeconds,
                    pacing, _frameTiming.Discontinuities);
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
                _compatibilityInput.Apply(snapshot, client.SuppressNativeInput);
                client.OnResize(_framebufferSize.X > 0 && _framebufferSize.Y > 0
                    ? _framebufferSize : _logicalSize);
                DispatchCompatibilityInput(snapshot, client);
                _frameLoop.Tick(elapsed, snapshot, client, _backend!);
                DrainCaptureResults();
            }
        }

        /// <summary>
        /// Initializes the real SDL video/GPU path and submits a bounded
        /// content-free frame.  This is intentionally a diagnostic entry
        /// point: it proves native lifetime, device creation, command
        /// submission and disposal without requiring proprietary game data.
        /// </summary>
        internal static int RunRuntimeSmoke(int frames = 1)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
            try
            {
                using var host = new SdlGameHost(new Vector2i(16, 16),
                    "Project Prime — runtime smoke", showWindow: false);
                RenderBackendInfo info = host.Backend.Info;
                Console.WriteLine($"runtime-smoke: backend={info.Name} driver={info.Driver} "
                    + $"shaders={info.ShaderFormats} swapchain={info.SwapchainFormat}");
                var client = new SdlTriangleFrameClient(host.Close, frames,
                    maxAttempts: Math.Max(30, frames * 120));
                host.Run(client);
                if (client.PresentedFrames < frames)
                {
                    throw new InvalidOperationException(
                        $"SDL runtime smoke could not submit a drawable frame "
                        + $"(attempts={client.Attempts}, presented={client.PresentedFrames}).");
                }
                Console.WriteLine($"runtime-smoke: submitted={client.PresentedFrames} "
                    + $"surface={host.Backend.Surface.FramebufferSize.X}x{host.Backend.Surface.FramebufferSize.Y}");
                return 0;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"runtime-smoke: FAIL: {exception.Message}");
                return 1;
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
            => (SDL3.SDL_GetWindowFlags(NativeWindow) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0
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
            Action<ulong>? windowPrepared = null, ISceneServices? sceneServices = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(configure);
            _lifetime.Run(scene, () =>
            {
                ProcessEvents();
                if (_lifetime.CloseRequested) return;
                ResetInput();
                _inputHub.BeginFrame();
                _compatibilityInput.Apply(BuildSnapshot());
                GamepadDesktop.Publish(default);
                _frameTiming.Reset();
                _framePacer = new SdlFramePacer();
                _suspendFrame = suspendFrame;
                _presentation = new ScenePresentation(scene, _logicalSize, _compatibilityInput.Keyboard,
                    _compatibilityInput.Mouse, SetTitle, StopScene, _frameTiming, sceneServices);
                Vector2i drawable = _framebufferSize.X > 0 && _framebufferSize.Y > 0
                    ? _framebufferSize : _logicalSize;
                NativeBottomScreenPlatformBridge.Configure(_logicalSize, drawable,
                    Mods.InputSettings.BottomScreenMode);
                _presentation.EnableDesktopLook();
                InputOwner.SetOwner(DesktopInputOwnerKind.Scene,
                    _gamepadHub.Buttons);
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
                            PublishPresentationState();
                        }
                        InputOwner.SetOwner(
                            exitPresentation == SceneExitPresentation.KeepWindowVisible
                                && Mods.PauseMenu.Open
                                ? DesktopInputOwnerKind.Overlay
                                : DesktopInputOwnerKind.None,
                            _gamepadHub.Buttons);
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
            ArgumentNullException.ThrowIfNull(title);
            _windowController!.SetTitle(title);
        }

        public void SetCursorCaptured(bool captured)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _windowController!.SetCursorCaptured(captured);
            _cursorCaptured = captured;
        }

        public void ApplyWindowMode(Mods.WindowStartMode mode)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool fullscreen = mode == Mods.WindowStartMode.BorderlessFullscreen;
            _windowController!.SetFullscreen(fullscreen);
            _fullscreen = fullscreen;
            Mods.WindowMode.SetFullscreenState(fullscreen);
            RefreshWindowSize();
        }

        private void ProcessEvents()
        {
            _inputHub.BeginFrame();
            _focusEventThisPump = null;
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
                        {
                            _focusEventThisPump = true;
                            ApplyFocusState(IsInputEligible(
                                SDL3.SDL_GetWindowFlags(NativeWindow),
                                _activationDeferred,
                                framebufferValid: _framebufferSize.X > 0
                                    && _framebufferSize.Y > 0));
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            _focusEventThisPump = false;
                            ApplyFocusState(false);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                    case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            RefreshWindowSize();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_MOVED:
                    case SDL_EventType.SDL_EVENT_WINDOW_SHOWN:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            // Position and native visibility can change at the
                            // window-manager boundary without going through a
                            // launcher-owned Show/Hide call. Publish the SDL
                            // flags/geometry as the single source of truth.
                            PublishPresentationState();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_HIDDEN:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            // A queued focus/input event can arrive after the
                            // window was hidden. Make the current pump's
                            // explicit focus result a loss and clear held
                            // state before publishing the hidden surface.
                            _focusEventThisPump = false;
                            ApplyFocusState(false);
                            // Position and native visibility can change at the
                            // window-manager boundary without going through a
                            // launcher-owned Show/Hide call. Publish the SDL
                            // flags/geometry as the single source of truth.
                            PublishPresentationState();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            // SDL may report a zero drawable size while a
                            // window is minimized. Retain the last valid
                            // logical/framebuffer dimensions for overlays and
                            // report minimization separately.
                            _focusEventThisPump = false;
                            ApplyFocusState(false);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_RESTORED:
                        if (IsOurWindow(evt.window.windowID))
                        {
                            RefreshWindowSize();
                            SynchronizeNativeFocus();
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    case SDL_EventType.SDL_EVENT_KEY_UP:
                        if (IsOurWindow(evt.key.windowID))
                        {
                            if (evt.key.down) ConfirmFocusFromInputEvent();
                            HandleKey(evt.key);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                        if (IsOurWindow(evt.text.windowID) && evt.text.text != null)
                        {
                            ConfirmFocusFromInputEvent();
                            _inputHub.AppendText((IntPtr)evt.text.text);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                        if (IsOurWindow(evt.motion.windowID)
                            && !IsSyntheticPenMouseId((uint)evt.motion.which))
                        {
                            _inputHub.HandleMouseMotion(evt.motion);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                        if (IsOurWindow(evt.button.windowID)
                            && !IsSyntheticPenMouseId((uint)evt.button.which))
                        {
                            if (evt.button.down) ConfirmFocusFromInputEvent();
                            HandleMouseButton(evt.button);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                        if (IsOurWindow(evt.wheel.windowID)
                            && !IsSyntheticPenMouseId((uint)evt.wheel.which))
                        {
                            _inputHub.AddWheel(evt.wheel.x, evt.wheel.y);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                        HandleGamepadAdded(evt.gdevice.which);
                        break;
                    case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                        HandleGamepadRemoved(evt.gdevice.which);
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
            // A platform can coalesce or reorder focus events around task
            // switching. Preserve an explicit event for this pump, then use
            // both SDL's cached flag and direct keyboard-focus owner so a
            // Windows Alt-Tab cannot leave input latched off indefinitely.
            SynchronizeNativeFocus();
            GamepadDesktop.Publish(_gamepadHub.State);
            GamepadHaptics.Pump();
            PublishPresentationState();
        }

        private void SynchronizeNativeFocus()
        {
            SDL_WindowFlags flags = SDL3.SDL_GetWindowFlags(NativeWindow);
            bool keyboardFocus = SDL3.SDL_GetKeyboardFocus() == NativeWindow;
            bool focused = ResolveFocusAfterPump(_focusEventThisPump,
                (flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0,
                keyboardFocus,
                (flags & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0,
                (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0
                    || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0,
                _activationDeferred);
            if (focused == _focused) return;
            ApplyFocusState(focused);
        }

        private void ApplyFocusState(bool focused)
        {
            bool changed = focused != _focused;
            _focused = focused;
            _windowController?.SetFocused(focused);
            if (!focused)
            {
                ClearInputAfterFocusLoss();
            }
            else
            {
                RestoreInputAfterFocusGain();
            }

            if (changed)
            {
                Mods.DebugLog.Line("sdl", focused
                    ? "window focus regained; input restored"
                    : "window focus lost; held input cleared");
            }

            PublishPresentationState();
        }

        private void ConfirmFocusFromInputEvent()
        {
            SDL_WindowFlags flags = SDL3.SDL_GetWindowFlags(NativeWindow);
            if (!IsInputEligible(flags, _activationDeferred,
                framebufferValid: _framebufferSize.X > 0
                    && _framebufferSize.Y > 0)) return;
            // Input queued before a task switch must not manufacture a local
            // focus gain. Confirm the native owner as well as visibility;
            // SDL's focus event may be delivered in a later pump.
            bool nativeInputFocus = (flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
            bool keyboardFocus = SDL3.SDL_GetKeyboardFocus() == NativeWindow;
            if (!nativeInputFocus && !keyboardFocus) return;
            _focusEventThisPump = true;
            if (!_focused) ApplyFocusState(true);
        }

        private void RestoreInputAfterFocusGain()
        {
            // SDL's direct keyboard-focus query is the authoritative fallback
            // on Windows when the cached window flag lags behind Alt-Tab. A
            // focus gain can also arrive after an overlay released ownership,
            // so repair an orphaned live scene without stealing input from an
            // active pause/settings surface.
            if (ShouldRestoreSceneInputOwner(InputOwner.Current,
                _presentation != null, Mods.PauseMenu.Open))
            {
                InputOwner.SetOwner(DesktopInputOwnerKind.Scene,
                    _gamepadHub.Buttons);
            }
            if (!SDL3.SDL_StartTextInput(NativeWindow))
            {
                Mods.DebugLog.Line("sdl",
                    $"text input restore failed: {SDL3.SDL_GetError()}");
            }
        }

        internal static bool IsInputEligible(bool isVisible, bool isMinimized,
            bool activationDeferred)
            => isVisible && !isMinimized && !activationDeferred;

        private static bool IsInputEligible(SDL_WindowFlags flags,
            bool activationDeferred, bool framebufferValid = true)
            => IsInputEligible(
                (flags & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0,
                (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0
                    || !framebufferValid,
                activationDeferred);

        internal static bool ResolveNativeFocus(bool nativeInputFocus,
            bool keyboardFocus, bool isVisible, bool isMinimized,
            bool activationDeferred)
            => IsInputEligible(isVisible, isMinimized, activationDeferred)
                && (nativeInputFocus || keyboardFocus);

        // Keep the pre-eligibility seam source-compatible for focused tests and
        // callers that have no native visibility sample. A visible, drawable
        // window is assumed by this overload.
        internal static bool ResolveNativeFocus(bool nativeInputFocus,
            bool keyboardFocus, bool activationDeferred)
            => ResolveNativeFocus(nativeInputFocus, keyboardFocus,
                isVisible: true, isMinimized: false, activationDeferred);

        internal static bool ResolveFocusAfterPump(bool? focusEvent,
            bool nativeInputFocus, bool keyboardFocus, bool isVisible,
            bool isMinimized, bool activationDeferred)
            => focusEvent.HasValue
                ? focusEvent.Value
                    && IsInputEligible(isVisible, isMinimized,
                        activationDeferred)
                : ResolveNativeFocus(nativeInputFocus, keyboardFocus,
                    isVisible, isMinimized, activationDeferred);

        internal static bool ResolveFocusAfterPump(bool? focusEvent,
            bool nativeInputFocus, bool keyboardFocus, bool activationDeferred)
            => ResolveFocusAfterPump(focusEvent, nativeInputFocus, keyboardFocus,
                isVisible: true, isMinimized: false, activationDeferred);

        internal static bool ShouldRestoreSceneInputOwner(
            DesktopInputOwnerKind currentOwner, bool hasPresentation,
            bool pauseOpen)
            => currentOwner == DesktopInputOwnerKind.None
                && hasPresentation && !pauseOpen;

        private WindowInputSnapshot BuildSnapshot()
            => _inputHub.Snapshot(_focused, _presentation?.FrameAdvance ?? false);

        private void DispatchCompatibilityInput(WindowInputSnapshot snapshot,
            IGameWindowFrameClient? frameClient = null)
        {
            Mods.ClientInputState.WindowFocused = snapshot.Focused;
            if (_presentation == null) return;
            if (frameClient?.SuppressNativeInput == true)
            {
                // Keep the window responsive while quarantining all live
                // gameplay/menu bindings during a replay-owned surface.
                _pointerHub.CancelInteraction();
                NativeBottomScreenPlatformBridge.Cancel();
                _presentation.ResetRenderLook();
                if (_cursorCaptured) SetCursorCaptured(false);
                return;
            }
            bool shouldCapture = _focused
                && InputOwner.Owns(DesktopInputOwnerKind.Scene)
                && (_presentation.CameraMode == CameraMode.Player || _presentation.IsFreeCam)
                && !Mods.PauseMenu.Open
                && !_presentation.ShowCursor
                && !_presentation.FrameAdvance;
            if (shouldCapture != _cursorCaptured) SetCursorCaptured(shouldCapture);
            bool bottomScreenSession
                = NativeBottomScreenPlatformBridge.DesktopSessionActive;
            bool bottomScreenHoldContact
                = NativeBottomScreenPlatformBridge.DesktopHoldContactActive;
            if (shouldCapture && bottomScreenSession)
            {
                _pointerHub.CancelInteraction();
                // A mouse-bound activation may have set the renderer's
                // viewer click latch on the opening frame. Clear it while the
                // scene-owned cursor session owns all pointer input.
                _presentation.OnMouseClick(false);
                NativeBottomScreenPlatformBridge.TryDesktopCursorMove(
                    snapshot.RelativeMouse);
                if (!bottomScreenHoldContact)
                {
                    foreach (WindowMouseButtonEvent button in snapshot.MouseButtonEvents)
                    {
                        if (button.Button != MouseButton.Left) continue;
                        if (button.Down)
                            NativeBottomScreenPlatformBridge.TryDesktopPointerDown();
                        else
                            NativeBottomScreenPlatformBridge.TryDesktopPointerUp();
                    }
                }
                _presentation.ResetRenderLook();
            }
            else if (shouldCapture && _presentation.CanCaptureSimulationLook)
            {
                _presentation.RenderLook?.Add(snapshot.RelativeMouse.X, snapshot.RelativeMouse.Y);
                _presentation.SubmitMouseLook(snapshot.RelativeMouse);
            }
            else
            {
                _pointerHub.CancelInteraction();
                NativeBottomScreenPlatformBridge.Cancel();
                _presentation.ResetRenderLook();
            }
            foreach (WindowKeyEvent key in snapshot.KeyEvents)
            {
                if (!InputOwner.Owns(DesktopInputOwnerKind.Scene)) break;
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
                // The shared quick-capture command runs at the frame-client
                // boundary below. Do not also leak its key chord into normal
                // gameplay bindings on this input frame.
                if (Mods.Network.ReplayQuickCapture.IsHotkey(key))
                {
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
            if (!InputOwner.Owns(DesktopInputOwnerKind.Scene)) return;
            foreach (WindowMouseButtonEvent button in snapshot.MouseButtonEvents)
            {
                if (button.Button == MouseButton.Left && !bottomScreenSession)
                    _presentation.OnMouseClick(button.Down);
            }
            if (snapshot.RelativeMouse != Vector2.Zero && !bottomScreenSession)
            {
                _presentation.OnMouseMove(snapshot.RelativeMouse.X, snapshot.RelativeMouse.Y);
            }
            if (snapshot.Wheel.Y != 0 && !bottomScreenSession)
                _presentation.OnMouseWheel(snapshot.Wheel.Y);
            for (int i = 0; i < snapshot.Text.Length; i++) Mods.Chat.ChatBox.HandleText(snapshot.Text[i]);
        }

        private void HandleKey(SDL_KeyboardEvent evt)
            => _inputHub.HandleKey(evt);

        private void HandleMouseButton(SDL_MouseButtonEvent evt)
            => _inputHub.HandleMouseButton(evt);

        private void ClearInputAfterFocusLoss()
        {
            Mods.ClientInputState.WindowFocused = false;
            _inputHub.ClearHeld();
            _gamepadHub.ResetState();
            _pointerHub.Reset();
            NativeBottomScreenPlatformBridge.Cancel();
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
            NativeBottomScreenPlatformBridge.Configure(_logicalSize, drawable,
                Mods.InputSettings.BottomScreenMode);
        }

        private void HandleGamepadAdded(SDL_JoystickID id)
        {
            if (_gamepadHub.Open(id, out SdlGamepadCapabilities capabilities))
                PublishGamepadCapabilities(capabilities);
            SyncGyroSensor();
        }

        private void HandleGamepadRemoved(SDL_JoystickID id)
        {
            bool wasActive = _gamepadHub.IsActive(id);
            if (wasActive)
            {
                GamepadHaptics.Stop();
                GamepadHaptics.Pump();
            }
            SdlGamepadCapabilities? capabilities = _gamepadHub.Close(id,
                out bool closedActive);
            if (capabilities.HasValue)
                PublishGamepadCapabilities(capabilities.Value);
            else if (closedActive)
                _capabilityOwner?.Publish(
                    ControllerCapabilitySnapshot.Disconnected(ControllerBackend.Sdl));
            SyncGyroSensor();
        }

        private void HandleGamepadAxis(SDL_GamepadAxisEvent evt)
            => _gamepadHub.HandleAxis(evt);

        private void HandleGamepadButton(SDL_GamepadButtonEvent evt)
            => _gamepadHub.HandleButton(evt);

        private void HandleGamepadSensor(SDL_GamepadSensorEvent evt)
            => _gamepadHub.HandleSensor(evt);

        private void SyncGyroSensor()
        {
            bool requested = _gamepadHub.HasActive
                && Mods.InputSettings.GamepadGyroEnabled
                && _focused
                && !Mods.PauseMenu.Open
                && (_presentation == null || _presentation.CanCaptureSimulationLook);
            _gamepadHub.SetGyroSensor(requested);
        }

        private void PublishGamepadCapabilities(SdlGamepadCapabilities capabilities)
            => _capabilityOwner?.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl,
                $"gamepad:{capabilities.Id}", capabilities.Name, capabilities.Family,
                capabilities.HasGyroscope,
                // SDL 3.4.16 exposes rumble but no non-invasive capability query.
                hasRumble: null, hasAnalogTriggers: capabilities.HasAnalogTriggers));

        private void HandlePenProximity(SDL_PenProximityEvent evt, bool entered)
        {
            _pointerHub.HandleProximity(evt, entered);
        }

        private void HandlePenTouch(SDL_PenTouchEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            _pointerHub.HandleTouch(evt);
        }

        private void HandlePenMotion(SDL_PenMotionEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            _pointerHub.HandleMotion(evt);
        }

        private void HandlePenButton(SDL_PenButtonEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            _pointerHub.HandleButton(evt);
        }

        private void HandlePenAxis(SDL_PenAxisEvent evt)
        {
            if (!IsOurWindow(evt.windowID)) return;
            _pointerHub.HandleAxis(evt);
        }

        private void ConfigureStylus(StylusInput stylus)
        {
            PlayerEntity? local = _presentation?.World.LocalPlayer;
            stylus.Configure(Mods.InputSettings.StylusAimingEnabled,
                Mods.InputSettings.StylusSensitivity, Mods.InputSettings.StylusInvertY,
                Mods.InputSettings.StylusPressureToFire,
                Mods.InputSettings.StylusPressureThreshold, density: 1);
            stylus.ConfigureGestures(Mods.InputSettings.StylusClassicGestures,
                Mods.InputSettings.StylusDoubleTapJump,
                Mods.InputSettings.StylusFlickBoost,
                flickContext: IsStylusFlickContext(local != null,
                    local?.IsAltForm == true), density: 1);
        }

        internal static bool IsStylusFlickContext(bool hasLocalPlayer, bool isAltForm)
            => hasLocalPlayer && isAltForm;

        internal static bool ShouldBeginStylusContact(bool contact,
            bool stylusActive)
            => SdlPointerHub.ShouldBeginStylusContact(contact, stylusActive);

        internal static bool ShouldOfferPenToBottomScreen(bool stylusActive)
            => SdlPointerHub.ShouldOfferPenToBottomScreen(stylusActive);

        internal static bool IsSyntheticPenMouseId(uint id)
            => id == (uint)SDL3.SDL_PEN_MOUSEID;

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

        void IGamepadHapticsSink.Apply(in HapticPattern pattern)
            => _gamepadHub.ApplyRumble(pattern);

        void IGamepadHapticsSink.Stop()
            => _gamepadHub.StopRumble();

        private bool IsOurWindow(SDL_WindowID windowId)
            => _windowController?.IsOurWindow(windowId) == true;

        private void ReadWindowSize(out Vector2i logical, out Vector2i framebuffer)
        {
            int logicalWidth = 0, logicalHeight = 0, framebufferWidth = 0, framebufferHeight = 0;
            SDL3.SDL_GetWindowSize(NativeWindow, &logicalWidth, &logicalHeight);
            if (!SDL3.SDL_GetWindowSizeInPixels(NativeWindow, &framebufferWidth, &framebufferHeight))
            {
                framebufferWidth = logicalWidth;
                framebufferHeight = logicalHeight;
            }
            logical = new Vector2i(logicalWidth, logicalHeight);
            framebuffer = new Vector2i(framebufferWidth, framebufferHeight);
        }

        private void RefreshWindowSize()
        {
            ReadWindowSize(out Vector2i logical, out Vector2i framebuffer);
            if (logical.X > 0 && logical.Y > 0) _logicalSize = logical;
            if (framebuffer.X > 0 && framebuffer.Y > 0) _framebufferSize = framebuffer;
            _windowController?.SetSizes(_logicalSize, _framebufferSize);
            _backend?.Resize(_logicalSize, _framebufferSize);
            SyncPresentationSize();
            PublishPresentationState();
        }

        private void ReadWindowPosition(out Vector2i position)
        {
            int x = 0, y = 0;
            SDL3.SDL_GetWindowPosition(NativeWindow, &x, &y);
            position = new Vector2i(x, y);
        }

        private void PublishPresentationState()
        {
            if (_presentationTracker == null) return;
            ReadWindowPosition(out Vector2i position);
            SDL_WindowFlags flags = SDL3.SDL_GetWindowFlags(NativeWindow);
            bool visible = (flags & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0;
            bool minimized = (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0
                || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0;
            _presentationTracker.Update(visible, minimized, _focused,
                _activationDeferred, _logicalSize, _framebufferSize, position,
                _fullscreen);
        }

        internal static KeyModifiers TranslateModifiers(SDL_Keymod modifiers)
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

        internal static MouseButton TranslateMouseButton(byte button)
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

        internal static Keys TranslateKey(SDL_Scancode scancode)
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
            _pointerHub.Dispose();
            NativeBottomScreenPlatformBridge.Cancel();
            GamepadGyro.ResetDevice();
            GamepadHaptics.Stop();
            GamepadHaptics.Pump();
            GamepadHaptics.Detach(this);
            _capabilityOwner?.Dispose();
            _capabilityOwner = null;
            PresentationStateChanged = null;
            GamepadDesktop.Publish(default);
            try
            {
                _gamepadHub.Dispose();
                if (_windowController != null)
                {
                    SDL3.SDL_StopTextInput(NativeWindow);
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
                    _windowController.Dispose();
                    _windowController = null;
                }
            }
            finally
            {
                _fullscreen = false;
                Mods.WindowMode.SetFullscreenState(false);
                if (_sdlInitialized) SDL3.SDL_Quit();
            }
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
            ArgumentOutOfRangeException.ThrowIfZero(generation);
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
        private readonly ReplayPresentationController? _replayPresentation;
        private readonly SceneFirstFrameNotification? _firstFrame;
        private IRenderBackendTelemetry? _telemetry;

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
            Mods.Network.AuthoritativePlay? play =
                (presentation.World.Services as Mods.Network.ClientSceneServices)?.Play;
            if (play != null)
                _killcam = new KillcamController(presentation, () =>
                    host.FramebufferSize.X > 0 && host.FramebufferSize.Y > 0
                        ? host.FramebufferSize : host.LogicalSize,
                    host.Keyboard, host.Mouse, play);
            else if (Mods.Network.ReplayPlayback.IsActive)
                _replayPresentation = new ReplayPresentationController(presentation);
        }

        private ScenePresentation ActivePresentation
            => _killcam?.RenderedPresentation ?? _presentation;

        public bool SuppressNativeInput => _killcam?.IsActive == true;

        public void OnResize(Vector2i size) => _killcam?.Resize(size);

        public void OnInput(WindowInputSnapshot input)
        {
            _killcam?.SubmitInput(input);
            if (_killcam == null || _killcam.State == KillcamState.Idle)
                Mods.Network.ReplayQuickCapture.HandleInput(input, _presentation);
        }

        public void AdvanceSimulation(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                _presentation.OnSimulationFrame();
                _killcam?.Advance();
            }
            _replayPresentation?.Advance();
            if (_replayPresentation?.ShouldClose == true) _host.StopScene();
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
        {
            _telemetry = backend as IRenderBackendTelemetry;
            backend.Render(frame, ActivePresentation.CurrentRenderFrame);
        }

        public void OnFrameRendered() => ActivePresentation.OnFrameRendered();

        public void OnFramePresented()
        {
            if (_firstFrame == null) ActivePresentation.OnFramePresented();
            else _firstFrame.Notify(ActivePresentation.OnFramePresented);
            if (_telemetry != null)
                ActivePresentation.UpdateDynamicResolution(_telemetry.Telemetry);
        }
        public void AfterRenderFrame() => ActivePresentation.AfterRenderFrame();
        public void PumpPauseMenu() => Mods.PauseMenu.Poll(_host);
        public bool CanRenderFrame => !ActivePresentation.Exiting;
        public void Dispose()
        {
            _replayPresentation?.Dispose();
            _killcam?.Dispose();
        }
    }

    /// <summary>
    /// A deliberately small SDL smoke client. It remains available as an
    /// explicit diagnostic helper; the SDL shipping path uses
    /// SdlSceneFrameClient.
    /// </summary>
    internal sealed class SdlTriangleFrameClient : IGameWindowFrameClient
    {
        private readonly RenderFrame _snapshot = new RenderFrame(1, 1);
        private readonly Action _close;
        private readonly int _targetFrames;
        private readonly int _maxAttempts;

        internal SdlTriangleFrameClient(Action close, int targetFrames, int maxAttempts)
        {
            _close = close ?? throw new ArgumentNullException(nameof(close));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFrames);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, targetFrames);
            _targetFrames = targetFrames;
            _maxAttempts = maxAttempts;
        }

        public int PresentedFrames { get; private set; }
        public int Attempts { get; private set; }

        public void OnInput(WindowInputSnapshot input)
        {
            _ = input;
            if (++Attempts > _maxAttempts) _close();
        }
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
            foreach (RenderPresentationStage stage in Enum.GetValues<RenderPresentationStage>())
                _snapshot.AddOverlayCommand(RenderOverlayCommand.StageMarker(stage));
            _snapshot.Seal();
        }
        public void Render(RenderBackendFrame frame, IRenderBackend backend)
            => backend.Render(frame, _snapshot);
        public void OnFramePresented()
        {
            if (++PresentedFrames >= _targetFrames) _close();
        }
        public void AfterRenderFrame() { }
        public void PumpPauseMenu() { }
    }
}
