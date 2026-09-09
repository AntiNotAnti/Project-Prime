using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SDL;
using MphRead.Mods.Input;
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
    public unsafe sealed class SdlGameHost : IGameWindowHost, Mods.IPauseMenuHost
    {
        private SdlGpuBackend? _backend;
        private SDL_Window* _window;
        private readonly SDL_WindowID _windowId;
        private readonly GameWindowFrameLoop _frameLoop = new();
        private readonly SdlFramePacer _framePacer = new();
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
        private PrimeGamepadState _gamepadState;
        private Vector2 _mousePosition;
        private Vector2 _relativeMouse;
        private Vector2 _wheel;
        private Vector2i _logicalSize;
        private Vector2i _framebufferSize;
        private bool _focused = true;
        private bool _closeRequested;
        private bool _disposed;
        private bool _sdlInitialized;
        private bool _fullscreen;
        private bool _cursorCaptured;
        private uint _activeGamepadId;
        private bool _hasActiveGamepad;
        private ScenePresentation? _presentation;

        public SdlGameHost(Vector2i? initialSize = null, string title = "Project Prime — SDL GPU",
            bool showWindow = true)
        {
            Vector2i size = initialSize ?? new Vector2i(1280, 720);
            if (size.X <= 0 || size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(initialSize));

            try
            {
                SDL_InitFlags initFlags = SDL_InitFlags.SDL_INIT_VIDEO
                    | SDL_InitFlags.SDL_INIT_EVENTS
                    | SDL_InitFlags.SDL_INIT_GAMEPAD;
                if (!SDL3.SDL_Init(initFlags))
                {
                    throw new InvalidOperationException($"SDL initialization failed: {SDL3.SDL_GetError()}");
                }
                _sdlInitialized = true;

                SDL_WindowFlags flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE
                    | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY
                    | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
                _window = SDL3.SDL_CreateWindow(title, size.X, size.Y, flags);
                if (_window == null)
                {
                    throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL_GetError()}");
                }
                _windowId = SDL3.SDL_GetWindowID(_window);
                ReadWindowSize(out _logicalSize, out _framebufferSize);
                _backend = new SdlGpuBackend(_window, _logicalSize, _framebufferSize);
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
                GamepadDesktop.Publish(default);
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
            SDL3.SDL_RaiseWindow(_window);
            _focused = true;
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
            while (!_closeRequested)
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
            return !_closeRequested;
        }

        /// <summary>
        /// Runs the ScenePresentation used by the desktop viewer. World
        /// loading and draw-item preparation happen once before the first
        /// frame; the SDL client submits the resulting sealed snapshot to the
        /// GPU backend.
        /// </summary>
        public void RunScene(Scene scene, Action<ScenePresentation> configure)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            _presentation = new ScenePresentation(scene, _logicalSize, _compatibilityInput.Keyboard,
                _compatibilityInput.Mouse, SetTitle, Close);
            try
            {
                _presentation.EnableDesktopLook();
                configure(_presentation);
                _presentation.OnLoad();
                if (Mods.WindowMode.Startup == Mods.WindowStartMode.BorderlessFullscreen)
                {
                    ApplyWindowMode(Mods.WindowStartMode.BorderlessFullscreen);
                }
                else
                {
                    Mods.WindowMode.SetFullscreenState(false);
                }
                Run(new SdlSceneFrameClient(this, _presentation));
            }
            finally
            {
                _presentation.DoCleanup();
                _presentation = null;
            }
        }

        public void Close() => _closeRequested = true;

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
            SDL_Event evt = default;
            while (SDL3.SDL_PollEvent(&evt))
            {
                switch ((SDL_EventType)evt.type)
                {
                    case SDL_EventType.SDL_EVENT_QUIT:
                        _closeRequested = true;
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                        if (IsOurWindow(evt.window.windowID)) _closeRequested = true;
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                        if (IsOurWindow(evt.window.windowID)) _focused = true;
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
                        if (IsOurWindow(evt.motion.windowID))
                        {
                            _mousePosition = new Vector2(evt.motion.x, evt.motion.y);
                            _relativeMouse += new Vector2(evt.motion.xrel, evt.motion.yrel);
                        }
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                        if (IsOurWindow(evt.button.windowID)) HandleMouseButton(evt.button);
                        break;
                    case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                        if (IsOurWindow(evt.wheel.windowID))
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
                }
            }
            GamepadDesktop.Publish(_gamepadState);
        }

        private WindowInputSnapshot BuildSnapshot()
            => new(_keys, _mouseButtons, _mousePosition,
                _relativeMouse, _wheel, _text.ToString(), _focused,
                _keyEvents, _mouseButtonEvents,
                _presentation?.FrameAdvance ?? false);

        private void DispatchCompatibilityInput(WindowInputSnapshot snapshot)
        {
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
            }
            else
            {
                _presentation.ResetRenderLook();
            }
            foreach (WindowKeyEvent key in snapshot.KeyEvents)
            {
                if (!key.Down) continue;
                if (Mods.Chat.ChatBox.HandleKeyDown(key,
                    canOpen: !Mods.Network.DemoPlayback.IsActive
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
                    && (Mods.Network.DemoPlayback.IsActive || Mods.SpectatorMode.IsSpectating))
                {
                    if (Mods.Network.DemoPlayback.IsActive)
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
                    _presentation.DoCleanup();
                    Close();
                    continue;
                }
                _presentation.OnKeyDown(key);
            }
            foreach (WindowMouseButtonEvent button in snapshot.MouseButtonEvents)
            {
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
            _keys.Clear();
            _mouseButtons.Clear();
            _relativeMouse = Vector2.Zero;
            _gamepadState = default;
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
                    Name = SDL3.SDL_GetGamepadName(handle) ?? "SDL gamepad"
                };
            }
        }

        private void CloseGamepad(SDL_JoystickID id)
        {
            uint rawId = (uint)id;
            if (_gamepads.Remove(rawId, out IntPtr pointer))
            {
                SDL3.SDL_CloseGamepad((SDL_Gamepad*)pointer);
            }
            if (!_hasActiveGamepad || rawId != _activeGamepadId) return;
            _hasActiveGamepad = false;
            _gamepadState = default;
            foreach (KeyValuePair<uint, IntPtr> gamepad in _gamepads)
            {
                _activeGamepadId = gamepad.Key;
                _hasActiveGamepad = true;
                SDL_Gamepad* handle = (SDL_Gamepad*)gamepad.Value;
                _gamepadState.Connected = true;
                _gamepadState.Name = SDL3.SDL_GetGamepadName(handle) ?? "SDL gamepad";
                break;
            }
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
            UpdateTriggerButtons();
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

        private void UpdateTriggerButtons()
        {
            if (_gamepadState.LeftTrigger > 0.65f) _gamepadState.Buttons |= GamepadButtons.LeftTrigger;
            else _gamepadState.Buttons &= ~GamepadButtons.LeftTrigger;
            if (_gamepadState.RightTrigger > 0.65f) _gamepadState.Buttons |= GamepadButtons.RightTrigger;
            else _gamepadState.Buttons &= ~GamepadButtons.RightTrigger;
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
            _disposed = true;
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
    }

    internal sealed class SdlSceneFrameClient : IGameWindowFrameClient
    {
        private readonly SdlGameHost _host;
        private readonly ScenePresentation _presentation;

        public SdlSceneFrameClient(SdlGameHost host, ScenePresentation presentation)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        }

        public void OnInput(WindowInputSnapshot input) { }

        public void AdvanceSimulation(int steps)
        {
            for (int i = 0; i < steps; i++) _presentation.OnSimulationFrame();
        }

        public void OnDrawFrame()
        {
            if (Mods.Input.GamepadInput.TakeMenuPress()
                && (_presentation.CameraMode == CameraMode.Player || _presentation.IsFreeCam))
            {
                Mods.PauseMenu.HandleEscape(_host, _presentation.World);
            }
            _presentation.OnDrawFrame();
        }

        public void Render(RenderBackendFrame frame, IRenderBackend backend)
            => backend.Render(frame, _presentation.CurrentRenderFrame);

        public void OnFramePresented() => _presentation.OnFramePresented();
        public void AfterRenderFrame() => _presentation.AfterRenderFrame();
        public void PumpPauseMenu() => Mods.PauseMenu.Poll(_host);
        public bool CanRenderFrame => !_presentation.Exiting;
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
                OpenTK.Mathematics.Matrix4.Identity, new OpenTK.Mathematics.Vector2i(1, 1),
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
