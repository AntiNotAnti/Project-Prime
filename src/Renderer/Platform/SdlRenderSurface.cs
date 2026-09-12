using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using SDL;

namespace MphRead;

public enum RenderSurfaceKey
{
    Unknown,
    Escape,
    Enter,
    Tab,
    Backspace,
    Delete,
    Space,
    W,
    A,
    S,
    D,
    Q,
    E,
    F,
    G,
    R,
    X,
    Y,
    Z,
    Left,
    Right,
    Up,
    Down,
    LeftShift,
    RightShift,
    LeftControl,
    RightControl
}

public sealed record RenderSurfaceInput(
    IReadOnlySet<RenderSurfaceKey> Keys,
    IReadOnlySet<RenderSurfaceKey> PressedKeys,
    IReadOnlySet<RenderSurfaceKey> ReleasedKeys,
    Vector2 MousePosition,
    Vector2 MouseDelta,
    Vector2 MouseWheel,
    bool LeftMouse,
    bool MiddleMouse,
    bool RightMouse,
    bool LeftPressed,
    bool LeftReleased,
    string Text,
    bool Focused)
{
    public bool Down(RenderSurfaceKey key) => Keys.Contains(key);
    public bool Pressed(RenderSurfaceKey key) => PressedKeys.Contains(key);
}

/// <summary>
/// Reusable SDL window and event owner for native rendering tools. It submits
/// the same immutable RenderFrame consumed by Project Prime's desktop client;
/// editor policy, documents, and widgets remain outside the renderer.
/// </summary>
public unsafe sealed class SdlRenderSurface : IDisposable
{
    private SDL_Window* _window;
    private SdlGpuBackend? _backend;
    private readonly SDL_WindowID _windowId;
    private readonly HashSet<RenderSurfaceKey> _keys = [];
    private readonly HashSet<RenderSurfaceKey> _pressed = [];
    private readonly HashSet<RenderSurfaceKey> _released = [];
    private readonly StringBuilder _text = new();
    private Vector2i _logicalSize;
    private Vector2i _framebufferSize;
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _mouseWheel;
    private bool _leftMouse;
    private bool _middleMouse;
    private bool _rightMouse;
    private bool _leftPressed;
    private bool _leftReleased;
    private bool _focused = true;
    private bool _closed;
    private bool _disposed;

    public SdlRenderSurface(Vector2i size, string title)
    {
        if (size.X <= 0 || size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        try
        {
            if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS))
                throw new InvalidOperationException($"SDL initialization failed: {SDL3.SDL_GetError()}");
            SDL_WindowFlags flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE
                | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
            _window = SDL3.SDL_CreateWindow(title, size.X, size.Y, flags);
            if (_window == null)
                throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL_GetError()}");
            _windowId = SDL3.SDL_GetWindowID(_window);
            ReadSize();
            _backend = new SdlGpuBackend(_window, _logicalSize, _framebufferSize);
            if (!SDL3.SDL_StartTextInput(_window))
                throw new InvalidOperationException($"SDL text input setup failed: {SDL3.SDL_GetError()}");
            SDL3.SDL_ShowWindow(_window);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Vector2i LogicalSize => _logicalSize;
    public Vector2i FramebufferSize => _framebufferSize;
    public RenderBackendInfo BackendInfo => Backend.Info;
    public RenderTelemetrySnapshot Telemetry => Backend.Telemetry;
    public bool Closed => _closed;

    public RenderSurfaceInput PumpEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pressed.Clear();
        _released.Clear();
        _text.Clear();
        _mouseDelta = Vector2.Zero;
        _mouseWheel = Vector2.Zero;
        _leftPressed = false;
        _leftReleased = false;
        SDL_Event evt = default;
        while (SDL3.SDL_PollEvent(&evt))
        {
            switch ((SDL_EventType)evt.type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                    _closed = true;
                    break;
                case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                    if (evt.window.windowID == _windowId) _closed = true;
                    break;
                case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                    if (evt.window.windowID == _windowId) _focused = true;
                    break;
                case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                    if (evt.window.windowID == _windowId)
                    {
                        _focused = false;
                        _keys.Clear();
                        _leftMouse = _middleMouse = _rightMouse = false;
                    }
                    break;
                case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                case SDL_EventType.SDL_EVENT_WINDOW_RESTORED:
                    if (evt.window.windowID == _windowId)
                    {
                        ReadSize();
                        Backend.Resize(_logicalSize, _framebufferSize);
                    }
                    break;
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                case SDL_EventType.SDL_EVENT_KEY_UP:
                    if (evt.key.windowID == _windowId) HandleKey(evt.key);
                    break;
                case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    if (evt.text.windowID == _windowId && evt.text.text != null)
                    {
                        string? value = Marshal.PtrToStringUTF8((IntPtr)evt.text.text);
                        if (!string.IsNullOrEmpty(value)) _text.Append(value);
                    }
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    if (evt.motion.windowID == _windowId)
                    {
                        _mousePosition = new Vector2(evt.motion.x, evt.motion.y);
                        _mouseDelta += new Vector2(evt.motion.xrel, evt.motion.yrel);
                    }
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    if (evt.button.windowID == _windowId) HandleMouse(evt.button.button, evt.button.down);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    if (evt.wheel.windowID == _windowId)
                        _mouseWheel += new Vector2(evt.wheel.x, evt.wheel.y);
                    break;
            }
        }
        return new RenderSurfaceInput(new HashSet<RenderSurfaceKey>(_keys),
            new HashSet<RenderSurfaceKey>(_pressed), new HashSet<RenderSurfaceKey>(_released),
            _mousePosition, _mouseDelta, _mouseWheel, _leftMouse, _middleMouse,
            _rightMouse, _leftPressed, _leftReleased, _text.ToString(), _focused);
    }

    public bool Render(RenderFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.IsSealed) throw new ArgumentException("RenderFrame must be sealed before submission.", nameof(frame));
        Backend.ApplyPresentPolicy(Mods.Render.FrameTiming.DisplayRate);
        if (!Backend.TryBeginFrame(out RenderBackendFrame token)) return false;
        Backend.Render(token, frame);
        return Backend.TrySubmitFrame(token);
    }

    public bool TryDequeueCapture(out RenderCaptureResult? result)
        => ((IRenderCaptureSource)Backend).TryDequeueCapture(out result);

    public bool TryDequeueCaptureFailure(out RenderCaptureFailure? failure)
        => ((IRenderCaptureSource)Backend).TryDequeueCaptureFailure(out failure);

    public void SetTitle(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        SDL3.SDL_SetWindowTitle(_window, title);
    }

    public void Close() => _closed = true;

    private SdlGpuBackend Backend => _backend
        ?? throw new InvalidOperationException("SDL GPU backend is not initialized.");

    private void HandleKey(SDL_KeyboardEvent evt)
    {
        RenderSurfaceKey key = Translate(evt.scancode);
        if (key == RenderSurfaceKey.Unknown) return;
        if (evt.down)
        {
            if (_keys.Add(key) && !evt.repeat) _pressed.Add(key);
        }
        else
        {
            _keys.Remove(key);
            _released.Add(key);
        }
    }

    private void HandleMouse(byte button, bool down)
    {
        if (button == 1)
        {
            if (down && !_leftMouse) _leftPressed = true;
            if (!down && _leftMouse) _leftReleased = true;
            _leftMouse = down;
        }
        else if (button == 2) _middleMouse = down;
        else if (button == 3) _rightMouse = down;
    }

    private void ReadSize()
    {
        int lw = 0, lh = 0, fw = 0, fh = 0;
        SDL3.SDL_GetWindowSize(_window, &lw, &lh);
        if (!SDL3.SDL_GetWindowSizeInPixels(_window, &fw, &fh)) { fw = lw; fh = lh; }
        _logicalSize = new Vector2i(Math.Max(1, lw), Math.Max(1, lh));
        _framebufferSize = new Vector2i(Math.Max(1, fw), Math.Max(1, fh));
    }

    private static RenderSurfaceKey Translate(SDL_Scancode key) => key switch
    {
        SDL_Scancode.SDL_SCANCODE_ESCAPE => RenderSurfaceKey.Escape,
        SDL_Scancode.SDL_SCANCODE_RETURN => RenderSurfaceKey.Enter,
        SDL_Scancode.SDL_SCANCODE_TAB => RenderSurfaceKey.Tab,
        SDL_Scancode.SDL_SCANCODE_BACKSPACE => RenderSurfaceKey.Backspace,
        SDL_Scancode.SDL_SCANCODE_DELETE => RenderSurfaceKey.Delete,
        SDL_Scancode.SDL_SCANCODE_SPACE => RenderSurfaceKey.Space,
        SDL_Scancode.SDL_SCANCODE_W => RenderSurfaceKey.W,
        SDL_Scancode.SDL_SCANCODE_A => RenderSurfaceKey.A,
        SDL_Scancode.SDL_SCANCODE_S => RenderSurfaceKey.S,
        SDL_Scancode.SDL_SCANCODE_D => RenderSurfaceKey.D,
        SDL_Scancode.SDL_SCANCODE_Q => RenderSurfaceKey.Q,
        SDL_Scancode.SDL_SCANCODE_E => RenderSurfaceKey.E,
        SDL_Scancode.SDL_SCANCODE_F => RenderSurfaceKey.F,
        SDL_Scancode.SDL_SCANCODE_G => RenderSurfaceKey.G,
        SDL_Scancode.SDL_SCANCODE_R => RenderSurfaceKey.R,
        SDL_Scancode.SDL_SCANCODE_X => RenderSurfaceKey.X,
        SDL_Scancode.SDL_SCANCODE_Y => RenderSurfaceKey.Y,
        SDL_Scancode.SDL_SCANCODE_Z => RenderSurfaceKey.Z,
        SDL_Scancode.SDL_SCANCODE_LEFT => RenderSurfaceKey.Left,
        SDL_Scancode.SDL_SCANCODE_RIGHT => RenderSurfaceKey.Right,
        SDL_Scancode.SDL_SCANCODE_UP => RenderSurfaceKey.Up,
        SDL_Scancode.SDL_SCANCODE_DOWN => RenderSurfaceKey.Down,
        SDL_Scancode.SDL_SCANCODE_LSHIFT => RenderSurfaceKey.LeftShift,
        SDL_Scancode.SDL_SCANCODE_RSHIFT => RenderSurfaceKey.RightShift,
        SDL_Scancode.SDL_SCANCODE_LCTRL => RenderSurfaceKey.LeftControl,
        SDL_Scancode.SDL_SCANCODE_RCTRL => RenderSurfaceKey.RightControl,
        _ => RenderSurfaceKey.Unknown
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend?.Dispose();
        _backend = null;
        if (_window != null)
        {
            SDL3.SDL_StopTextInput(_window);
            SDL3.SDL_DestroyWindow(_window);
            _window = null;
        }
        SDL3.SDL_Quit();
    }
}
