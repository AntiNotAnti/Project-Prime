using System;
using SDL;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// Owns the native SDL window handle and the small set of window operations
/// that must stay on the SDL host thread.  The host remains the sole caller;
/// this type deliberately does not expose a public native pointer.
/// </summary>
internal unsafe sealed class SdlWindowController : IDisposable
{
    private SDL_Window* _window;
    private bool _disposed;

    internal SdlWindowController(Vector2i size, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        SDL_WindowFlags flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE
            | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY
            | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        _window = SDL3.SDL_CreateWindow(title, size.X, size.Y, flags);
        if (_window == null)
            throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL_GetError()}");
        try
        {
            WindowId = SDL3.SDL_GetWindowID(_window);
            RefreshSize();
            Focused = (SDL3.SDL_GetWindowFlags(_window)
                & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
        }
        catch
        {
            SDL3.SDL_DestroyWindow(_window);
            _window = null;
            throw;
        }
    }

    internal SDL_Window* NativeWindow
        => _window;

    internal SDL_WindowID WindowId { get; }
    internal Vector2i LogicalSize { get; private set; }
    internal Vector2i FramebufferSize { get; private set; }
    internal bool Focused { get; private set; }
    internal bool Fullscreen { get; private set; }
    internal bool ActivationDeferred { get; private set; }
    internal bool CursorCaptured { get; private set; }

    internal bool IsOurWindow(SDL_WindowID windowId)
        => windowId == WindowId;

    internal SDL_WindowFlags Flags
        => SDL3.SDL_GetWindowFlags(_window);

    internal void Show()
    {
        ThrowIfDisposed();
        SDL3.SDL_ShowWindow(_window);
    }

    internal void Hide()
    {
        ThrowIfDisposed();
        SDL3.SDL_HideWindow(_window);
    }

    internal void SetActivationDeferred(bool deferred)
        => ActivationDeferred = deferred;

    internal void SetFocused(bool focused)
        => Focused = focused;

    internal void SetCursorCaptured(bool captured)
    {
        ThrowIfDisposed();
        if (!SDL3.SDL_SetWindowRelativeMouseMode(_window, captured))
            throw new InvalidOperationException($"SDL relative mouse mode failed: {SDL3.SDL_GetError()}");
        if (captured) SDL3.SDL_HideCursor();
        else SDL3.SDL_ShowCursor();
        CursorCaptured = captured;
    }

    internal void SetWindowFocusable(bool focusable)
    {
        ThrowIfDisposed();
        if (!SDL3.SDL_SetWindowFocusable(_window, focusable))
            throw new InvalidOperationException(
                $"SDL window focusability change failed: {SDL3.SDL_GetError()}");
    }

    internal void Raise()
    {
        ThrowIfDisposed();
        if (!SDL3.SDL_RaiseWindow(_window))
            Mods.DebugLog.Line("sdl", $"window activation request failed: {SDL3.SDL_GetError()}");
    }

    internal void RestoreIfMinimized()
    {
        ThrowIfDisposed();
        if ((SDL3.SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) == 0)
            return;
        if (!SDL3.SDL_RestoreWindow(_window))
        {
            // Restore is a best-effort request on some window managers. Keep
            // activation asynchronous and let the following raise/focus
            // reconciliation decide whether the window actually became live.
            Mods.DebugLog.Line("sdl", $"window restore request failed: {SDL3.SDL_GetError()}");
        }
    }

    internal void SetPosition(Vector2i position)
    {
        ThrowIfDisposed();
        if (!SDL3.SDL_SetWindowPosition(_window, position.X, position.Y))
            throw new InvalidOperationException(
                $"SDL window placement failed: {SDL3.SDL_GetError()}");
    }

    internal Vector2i Position
    {
        get
        {
            int x = 0, y = 0;
            SDL3.SDL_GetWindowPosition(_window, &x, &y);
            return new Vector2i(x, y);
        }
    }

    internal void SetTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);
        SDL3.SDL_SetWindowTitle(_window, title);
    }

    internal void SetFullscreen(bool fullscreen)
    {
        ThrowIfDisposed();
        SDL3.SDL_SetWindowBordered(_window, !fullscreen);
        if (!SDL3.SDL_SetWindowFullscreen(_window, fullscreen))
            throw new InvalidOperationException(
                $"SDL fullscreen transition failed: {SDL3.SDL_GetError()}");
        Fullscreen = fullscreen;
    }

    internal void RefreshSize()
    {
        ThrowIfDisposed();
        int logicalWidth = 0, logicalHeight = 0;
        int framebufferWidth = 0, framebufferHeight = 0;
        SDL3.SDL_GetWindowSize(_window, &logicalWidth, &logicalHeight);
        if (!SDL3.SDL_GetWindowSizeInPixels(_window, &framebufferWidth, &framebufferHeight))
        {
            framebufferWidth = logicalWidth;
            framebufferHeight = logicalHeight;
        }
        LogicalSize = new Vector2i(logicalWidth, logicalHeight);
        FramebufferSize = new Vector2i(framebufferWidth, framebufferHeight);
    }

    internal void SetSizes(Vector2i logicalSize, Vector2i framebufferSize)
    {
        LogicalSize = logicalSize;
        FramebufferSize = framebufferSize;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window != null)
        {
            SDL3.SDL_DestroyWindow(_window);
            _window = null;
        }
    }
}
