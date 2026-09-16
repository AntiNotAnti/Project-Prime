using System;
using MphRead.Mods.Input;
using SDL;
using OpenTK.Mathematics;

namespace MphRead;

internal enum SdlCursorPolicy
{
    UiNormal,
    GameplayRelative,
    BottomPanelConfined
}

/// <summary>
/// Tracks the requested fullscreen intent separately from the native state
/// acknowledged by SDL. Native enter/leave events can arrive after a newer
/// request. Every event is still applied as the native state, while an
/// opposite event leaves the latest intent pending for its eventual
/// acknowledgement. This keeps rapid toggle ordering deterministic without a
/// blocking SDL_SyncWindow call.
/// </summary>
internal sealed class FullscreenTransitionTracker
{
    internal FullscreenTransitionTracker(bool confirmed = false)
    {
        Confirmed = confirmed;
    }

    internal bool Confirmed { get; private set; }
    internal bool? Pending { get; private set; }
    internal bool Requested => Pending ?? Confirmed;

    /// <summary>Whether a native request is needed for this latest intent.</summary>
    internal bool NeedsRequest(bool fullscreen)
        => Pending is bool pending ? pending != fullscreen : Confirmed != fullscreen;

    /// <summary>Records a new latest intent and returns the prior pending one.</summary>
    internal bool? Request(bool fullscreen)
    {
        bool? previous = Pending;
        if (!NeedsRequest(fullscreen)) return previous;
        Pending = fullscreen;
        return previous;
    }

    /// <summary>
    /// Applies a native acknowledgement. The native state is always observed;
    /// only an acknowledgement matching the latest pending intent clears that
    /// intent. An opposite event therefore updates <see cref="Confirmed"/>
    /// while retaining <see cref="Pending"/>.
    /// </summary>
    internal bool Confirm(bool fullscreen)
    {
        Confirmed = fullscreen;
        if (Pending is not bool pending) return true;
        if (pending != fullscreen) return false;
        Pending = null;
        return true;
    }

    internal void RestorePending(bool? pending)
        => Pending = pending;
}

/// <summary>
/// Owns the native SDL window handle and the small set of window operations
/// that must stay on the SDL host thread.  The host remains the sole caller;
/// this type deliberately does not expose a public native pointer.
/// </summary>
internal unsafe sealed class SdlWindowController : IDisposable
{
    private SDL_Window* _window;
    private bool _disposed;
    private readonly FullscreenTransitionTracker _fullscreen = new();
    private SdlCursorPolicy _cursorPolicy;
    private BottomScreenRect? _cursorPanel;
    private BottomScreenRect? _confinementRetryPanel;
    private long _nextConfinementRetryAt;

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
    /// <summary>Last fullscreen state confirmed by SDL, never an optimistic request.</summary>
    internal bool Fullscreen => _fullscreen.Confirmed;
    internal bool? PendingFullscreen => _fullscreen.Pending;
    internal bool RequestedFullscreen => _fullscreen.Requested;
    internal bool ActivationDeferred { get; private set; }
    internal bool CursorCaptured { get; private set; }
    internal SdlCursorPolicy CursorPolicy => _cursorPolicy;

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
        => SetCursorPolicy(captured ? SdlCursorPolicy.GameplayRelative
            : SdlCursorPolicy.UiNormal);

    internal void SetCursorPolicy(SdlCursorPolicy policy,
        BottomScreenRect? panel = null)
    {
        ThrowIfDisposed();
        if (policy == SdlCursorPolicy.BottomPanelConfined
            && (panel is not BottomScreenRect rect
                || rect.Width <= 0 || rect.Height <= 0))
        {
            policy = SdlCursorPolicy.UiNormal;
            panel = null;
        }
        if (policy == SdlCursorPolicy.BottomPanelConfined
            && _confinementRetryPanel == panel
            && Environment.TickCount64 < _nextConfinementRetryAt)
        {
            return;
        }
        if (policy != SdlCursorPolicy.BottomPanelConfined)
        {
            _confinementRetryPanel = null;
            _nextConfinementRetryAt = 0;
        }
        if (_cursorPolicy == policy && _cursorPanel == panel) return;

        if (policy == SdlCursorPolicy.GameplayRelative)
        {
            _confinementRetryPanel = null;
            _nextConfinementRetryAt = 0;
            ClearMouseRectBestEffort();
            if (!SDL3.SDL_SetWindowRelativeMouseMode(_window, true))
                throw new InvalidOperationException(
                    $"SDL relative mouse mode failed: {SDL3.SDL_GetError()}");
            SDL3.SDL_HideCursor();
            CursorCaptured = true;
            _cursorPanel = null;
        }
        else
        {
            if (!SDL3.SDL_SetWindowRelativeMouseMode(_window, false))
                throw new InvalidOperationException(
                    $"SDL relative mouse mode failed: {SDL3.SDL_GetError()}");
            CursorCaptured = false;
            if (policy == SdlCursorPolicy.BottomPanelConfined
                && panel is BottomScreenRect confined)
            {
                SDL_Rect mouseRect = ToSdlMouseRect(confined, LogicalSize);
                if (!SDL3.SDL_SetWindowMouseRect(_window, &mouseRect))
                {
                    Mods.DebugLog.Line("sdl", "mouse confinement failed; "
                        + $"continuing with software-clamped panel cursor: {SDL3.SDL_GetError()}");
                    ClearMouseRectBestEffort();
                    _confinementRetryPanel = confined;
                    _nextConfinementRetryAt = Environment.TickCount64 + 1000;
                    policy = SdlCursorPolicy.UiNormal;
                    panel = null;
                }
                else
                {
                    _confinementRetryPanel = null;
                    _nextConfinementRetryAt = 0;
                }
                _cursorPanel = panel;
            }
            else
            {
                _confinementRetryPanel = null;
                _nextConfinementRetryAt = 0;
                ClearMouseRectBestEffort();
                _cursorPanel = null;
            }
            SDL3.SDL_ShowCursor();
        }
        _cursorPolicy = policy;
    }

    internal static SDL_Rect ToSdlMouseRect(BottomScreenRect panel,
        Vector2i logicalSize)
    {
        int left = Math.Clamp((int)MathF.Floor(panel.Left), 0,
            Math.Max(0, logicalSize.X));
        int top = Math.Clamp((int)MathF.Floor(panel.Top), 0,
            Math.Max(0, logicalSize.Y));
        int right = Math.Clamp((int)MathF.Ceiling(panel.Right), left,
            Math.Max(left, logicalSize.X));
        int bottom = Math.Clamp((int)MathF.Ceiling(panel.Bottom), top,
            Math.Max(top, logicalSize.Y));
        return new SDL_Rect { x = left, y = top, w = right - left,
            h = bottom - top };
    }

    private void ClearMouseRectBestEffort()
    {
        if (!SDL3.SDL_SetWindowMouseRect(_window, null))
        {
            Mods.DebugLog.Line("sdl", $"mouse confinement release failed: {SDL3.SDL_GetError()}");
        }
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
        if (!_fullscreen.NeedsRequest(fullscreen)) return;
        bool? previousPending = _fullscreen.Request(fullscreen);
        try
        {
            if (!SDL3.SDL_SetWindowBordered(_window, !fullscreen))
                throw new InvalidOperationException(
                    $"SDL window border transition failed: {SDL3.SDL_GetError()}");
            if (!SDL3.SDL_SetWindowFullscreen(_window, fullscreen))
                throw new InvalidOperationException(
                    $"SDL fullscreen transition failed: {SDL3.SDL_GetError()}");
        }
        catch
        {
            // An immediate API failure does not change confirmed state. Keep a
            // prior in-flight intent alive, but clear a request that had no
            // earlier pending operation; restore the confirmed border best
            // effort without introducing a blocking synchronization call.
            _fullscreen.RestorePending(previousPending);
            SDL3.SDL_SetWindowBordered(_window, !_fullscreen.Confirmed);
            throw;
        }
    }

    /// <summary>Records the native enter/leave acknowledgement and returns intent status.</summary>
    internal bool ConfirmFullscreen(bool fullscreen)
        => _fullscreen.Confirm(fullscreen);

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
        if (_window != null) ClearMouseRectBestEffort();
        _disposed = true;
        if (_window != null)
        {
            SDL3.SDL_DestroyWindow(_window);
            _window = null;
        }
    }
}
