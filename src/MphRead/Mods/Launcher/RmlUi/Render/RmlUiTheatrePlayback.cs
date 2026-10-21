#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Network;
using MphRead.Mods.StudioIntegration;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static TheatrePlaybackController? _playbackController;
    private static TheatrePlaybackPagePresenter? _playback;
    // This receipt fences the existing Shell request, not its replaceable presentation replica.
    private sealed record NativeReplayPageLease(RenderWindow Window, Scene CanonicalScene, object Request,
        string LogicalPath, string PlaybackPath);
    private static NativeReplayPageLease? _nativeReplayLease;
    private static NativeReplayPageLease? _pendingNativeReplayEditor;
    private static string? _nativeReplayFailure;

    internal static bool HandleNativeReplayFailure(RenderWindow window, string error)
    {
        if (!Requested) return false;
        if (window.HasScene)
        {
            _nativeReplayFailure = error;
            Shell.RequestEndMatch();
        }
        else
        {
            _controller?.ReplayPlaybackClosed();
            _controller?.ReportStudioLaunch(false, error);
        }
        Mods.DebugLog.Line("rmlui", error);
        return true;
    }

    internal static void NativeReplayClosed(RenderWindow window)
    {
        if (!ReferenceEquals(Shell.Window, window) || window.HasScene || DemoPlayback.IsActive) return;
        _controller?.ReplayPlaybackClosed();
        if (_nativeReplayFailure is { } error)
        {
            _nativeReplayFailure = null;
            _controller?.ReportStudioLaunch(false, error);
        }
    }

    private static bool NativeReplayLeaseCurrent(RenderWindow window)
    {
        var lease = _nativeReplayLease;
        return lease != null && ReferenceEquals(lease.Window, window) && ReferenceEquals(Shell.Window, window)
            && Shell.Active && !window.IsExiting && window.HasScene
            && ReferenceEquals(window.CanonicalScene, lease.CanonicalScene)
            && ReferenceEquals(_gameplayScene, lease.CanonicalScene)
            && DemoPlayback.RequestCurrent(lease.Request, lease.CanonicalScene)
            && DemoPlayback.LogicalPath == lease.LogicalPath && DemoPlayback.PlaybackPath == lease.PlaybackPath;
    }
    private static void RequestNativeReplayEditor(RenderWindow window)
    {
        if (!Shell.StudioWindow || !NativeReplayLeaseCurrent(window)) return;
        _pendingNativeReplayEditor = _nativeReplayLease;
        CompleteNativeReplayEditor(window);
    }
    private static void CompleteNativeReplayEditor(RenderWindow window)
    {
        if (!Shell.StudioWindow) { _pendingNativeReplayEditor = null; return; }
        var pending = _pendingNativeReplayEditor;
        if (pending == null || !ReferenceEquals(pending.Window, window)) return;
        if (!ReferenceEquals(pending, _nativeReplayLease) || !NativeReplayLeaseCurrent(window))
        { _pendingNativeReplayEditor = null; return; }
        if (!DemoPlayback.RequestReady(pending.Request, pending.CanonicalScene)) return;
        // One actual-ready attempt on this exact immutable receipt. A failed panel
        // capture is reported by the existing presenter and requires explicit retry.
        _pendingNativeReplayEditor = null;
        _playback?.TryOpenEditor();
    }

    internal static bool TryOpenNativePlayback(RenderWindow window, bool openEditor = false)
    {
        if (!Requested || _disabled || !Shell.Active || !ReferenceEquals(Shell.Window, window)
            || window.IsExiting || window.CanonicalScene is not { } canonical || !DemoPlayback.IsActive
            || DemoPlayback.LogicalPath is not { } logical || DemoPlayback.PlaybackPath is not { } playback) return false;
        object request = DemoPlayback.RequestOwner;
        if (!DemoPlayback.RequestCurrent(request, canonical)) return false;
        if (_playback != null && ReferenceEquals(_gameplayWindow, window) && NativeReplayLeaseCurrent(window))
        {
            if (openEditor) RequestNativeReplayEditor(window);
            return true;
        }
        if (!TryStart(window) || _host == null || _pages == null) return false;
        try
        {
            CloseGameplayOverlays();
            _launcherPages?.Suspend();
            _playbackController = new TheatrePlaybackController(new CurrentReplayStudio());
            bool editing = Shell.StudioWindow && !DemoPlayback.IsCustomPlayback;
            var editor = editing ? new CurrentReplayEditor() : null;
            _playback = editing
                ? new TheatrePlaybackPagePresenter(_host,_pages,_playbackController,
                    new CurrentReplayBookmarks(), new CurrentReplayRanges(), new CurrentReplayCamera(),
                    new CurrentReplayExports(), editor)
                : new TheatrePlaybackPagePresenter(_host,_pages,_playbackController);
            _playback.Open();
            _gameplayScene = canonical; _gameplayWindow = window;
            _nativeReplayLease = new(window, canonical, request, logical, playback);
            if (editor != null) BeginNativeStudioPresentationPreferences(window,editor);
            _shown = true;
            if (openEditor) RequestNativeReplayEditor(window);
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); return false; }
    }

    private sealed class CurrentReplayStudio : ITheatrePlaybackBackend
    {
        public bool OpenStudio(string path, out string? error)
        {
            if (DemoPlayback.IsCustomPlayback) { error = "Custom replay Studio editing is not qualified."; return false; }
            error = "REPLAY OWNER CHANGED";
            if (!GameplayLeaseCurrent || !DemoPlayback.IsActive || DemoPlayback.LogicalPath != path) return false;
            if (!Shell.StudioWindow) return StudioApplicationLauncher.TryOpen(path, false, out error);
            if (_playback?.TryOpenEditor() != true) return false;
            error = null; return true;
        }
    }

    private static void CloseNativePlayback()
    {
        RetireNativeStudioWaveChooser();
        RetireNativeStudioPresentationPreferences();
        RetireNativeStudioProjectImport();
        var page = _playback; var controller = _playbackController;
        _playback = null; _playbackController = null;
        _nativeReplayLease = null; _pendingNativeReplayEditor = null;
        try { page?.Dispose(); }
        finally
        {
            try { controller?.Dispose(); }
            finally { ReleaseGameplayLeaseIfIdle(); }
        }
    }

    private static void ApplyPlaybackEffects()
    {
        if (_playback == null || !GameplayLeaseCurrent) return;
        var window = _gameplayWindow!;
        PumpNativeStudioWaveChooser(window);
        PumpNativeStudioPresentationPreferences(window);
        if (PumpNativeStudioWorkspaceRequests(window)) return;
        if (_playback.TryTakeEngineCommand(out TheatrePlaybackAction command))
        {
            CloseNativePlayback(); _shown = false;
            ReleaseNativeInput();
            if (command == TheatrePlaybackAction.Back) Shell.RequestEndMatch();
            else { PauseMenu.MarkClosed(); try { window.Focus(); } catch (Exception error) when (Recoverable(error)) { } }
        }
    }

    internal static Vector4i? NativeReplayViewportBounds(int width, int height)
    {
        if (_playback is not { IsOpen:true } page || !GameplayLeaseCurrent || _host == null
            || _pages?.Top != page.Document || _width <= 0 || _height <= 0
            || !_host.TryGetElementBounds(page.Document,"replay_viewport",out float x,out float y,out float w,out float h))
            return null;
        int left = Math.Clamp((int)MathF.Round(x * width / _width),0,width);
        int top = Math.Clamp((int)MathF.Round(y * height / _height),0,height);
        int right = Math.Clamp((int)MathF.Round((x+w) * width / _width),left,width);
        int bottom = Math.Clamp((int)MathF.Round((y+h) * height / _height),top,height);
        return right > left && bottom > top ? new Vector4i(left,top,right-left,bottom-top) : null;
    }

    internal static bool PollNativeReplay(Scene scene)
    {
        if (_playback == null || !GameplayLeaseCurrent) return false;
        if (!ReferenceEquals(scene,_gameplayScene) && !DemoPlayback.Owns(scene)) return false;
        _playback.Viewport.Poll(scene);
        return true;
    }

    private static void ReplayPointerButton(int code,double x,double y,bool down)
    {
        if (_playback == null || !GameplayLeaseCurrent || code != 0) return;
        if (!down) { _playback.Viewport.PointerUp(); return; }
        var window = _gameplayWindow!;
        var bounds = NativeReplayViewportBounds(window.FramebufferSize.X,window.FramebufferSize.Y);
        float px = (float)x * window.FramebufferSize.X / Math.Max(1,window.ClientSize.X);
        float py = (float)y * window.FramebufferSize.Y / Math.Max(1,window.ClientSize.Y);
        if (bounds is { } box && px >= box.X && py >= box.Y && px < box.X+box.Z && py < box.Y+box.W)
        { _playback.Viewport.PointerDownInWindow(x,y); _host?.FocusDocument(_playback.Document,"replay_viewport"); }
        else _playback.Viewport.Release();
    }

    private static bool NativeReplayKeyboardFocused => _playback is { } page && GameplayLeaseCurrent
        && (page.Viewport.Focused || _host?.FocusedElement() == "replay_viewport");
    private static bool ReleaseNativeReplayCamera()
    {
        if (!NativeReplayKeyboardFocused || _playback == null) return false;
        _playback.Viewport.Release();
        _host?.FocusDocument(_playback.Document,"replay_pause");
        return true;
    }
    private static bool ReplayKeyDown(Keys key)
    {
        if (_playback is not { } page || !GameplayLeaseCurrent || !NativeReplayKeyboardFocused) return false;
        if (key == Keys.Escape) return ReleaseNativeReplayCamera();
        if (!page.Viewport.Focused) page.Viewport.Focus();
        return page.Viewport.KeyDown(key);
    }
    private static bool ReplayKeyUp(Keys key)
        => _playback is { } page && GameplayLeaseCurrent && page.Viewport.KeyUp(key);
}
#endif
