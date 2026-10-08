#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Immutable;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Settings;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Training;
using OpenTK.Mathematics;

namespace MphRead.Droid;

// One native host on the existing match render owner. The retained launcher
// has already released its native documents and graphics lease at this point.
internal sealed class AndroidRmlUiMatchSession : IDisposable
{
    private sealed class Backend(Action leave, Action quit) : IInGameBackend
    {
        private readonly IInGameBackend _engine = InGameController.CreateEngineBackend();
        public InGameFacts Capture() => _engine.Capture();
        public ImmutableArray<string> Maps() => _engine.Maps();
        public string EditBot(InGameAction action, InGameBot bot) => _engine.EditBot(action, bot);
        public string Execute(InGameAction action, string map)
        {
            switch (action)
            {
                case InGameAction.Leave: leave(); return "";
                case InGameAction.Quit: quit(); return "";
                case InGameAction.Fullscreen: return "Android manages fullscreen through the system display controls.";
                default: return _engine.Execute(action, map);
            }
        }
    }

    internal RmlUiHost Host { get; } = new();
    internal RmlUiPageManager Pages { get; }
    private readonly Action _resume, _replay, _replayBack, _replayClosed;
    private readonly GamepadUiRouter _gamepad = new();
    private readonly AndroidRmlUiVisualPolicy _visualPolicy = new();
    private readonly SceneGameState _state;
    private readonly IInGameBackend _backend;
    private readonly AndroidRmlUiResultsSession _results;
    private readonly IDisposable _replayBoundsLease;
    private InGameController? _controller;
    private InGamePagePresenter? _presenter;
    private SettingsPagePresenter? _settingsPresenter;
    private AndroidRmlUiHudSession? _hud;
    private TheatrePlaybackController? _playbackController;
    private TheatrePlaybackPagePresenter? _playback;
    private readonly ScenePlayerRegistry _players;
    private readonly MenuSettings _menu;
    private bool _pausedLocal, _disposed;
    private Scene? _scene;
    private bool MenuVisible => _presenter?.Active == true || _settingsPresenter != null;
    private bool ReplayVisible => _playback?.IsOpen == true;
    internal bool Visible => MenuVisible || ReplayVisible || _results.Visible;

    internal AndroidRmlUiMatchSession(string root, int width, int height, float density,
        SceneGameState state, ScenePlayerRegistry players, Action resume, Action settings, Action replay, Action leave, Action quit,
        Action openPause, Action<AimTrainerSession, AimResultsAction> trainingAction,
        Action replayBack, Action replayClosed)
    {
        (_state, _players, _resume, _replay) = (state, players, resume, replay);
        (_replayBack, _replayClosed) = (replayBack, replayClosed);
        _menu = GameState.LoadSettings();
        if (!Host.Initialize(width, height, density, root, RmlUiRenderBackend.DrawList))
            throw new InvalidOperationException("The native match UI refused initialization.");
        Host.Input.SetFramebufferScale(1, 1);
        Pages = new(Host);
        _backend = new Backend(leave, quit);
        _results = new(Host, Pages, openPause, trainingAction);
        _gamepad.Action += Gamepad;
        _replayBoundsLease = DemoPlayback.RegisterPreviewBounds(ReplayBounds);
    }
    internal void Open()
    {
        if (MenuVisible) return;
        CloseReplay();
        _results.Update(_scene, menuVisible: true);
        _presenter?.Dispose(); _controller?.Dispose();
        _controller = new(_backend); _presenter = new(Host, Pages, _controller); _presenter.Open();
        _gamepad.Reset();
        // The existing live match remains authoritative. Local simulation uses
        // its own menu pause flag rather than withholding network pump frames.
        if (!NetSession.Active && !DemoPlayback.IsActive && !_state.MenuPause)
        { _state.PauseMenu(); _pausedLocal = true; }
    }
    internal void Update(Scene scene, bool menuRequested, bool replayRequested = false)
    {
        _scene = scene;
        if (replayRequested && !ReplayVisible && DemoPlayback.IsActive) OpenReplay();
        else if (!replayRequested && ReplayVisible) CloseReplay();
        if (menuRequested && !MenuVisible) Open();
        else if (!menuRequested && MenuVisible) CloseMenu();
        _results.Update(scene, MenuVisible || ReplayVisible);
        if (!Visible) return;
        _gamepad.Update(GamepadManager.Snapshot, GamepadContext.Menu, Environment.TickCount64);
        _presenter?.Refresh(); _settingsPresenter?.Refresh(); _hud?.Present(); _playback?.Present();
        _playback?.Viewport.Poll(DemoPlayback.PresentationScene ?? scene); Host.Update();
        for (int i = 0; i < 128 && Host.TryTakeIntent(out var intent); i++)
        { if (_playback?.HandleIntent(intent) != true && _results.HandleIntent(intent) != true && _hud?.HandleIntent(intent) != true && _settingsPresenter?.HandleAction(intent) != true) _presenter?.Handle(intent); }
        if (_playback?.TryTakeEngineCommand(out var playbackCommand) == true)
        {
            CloseReplay(); _replayClosed();
            if (playbackCommand == TheatrePlaybackAction.Back) _replayBack();
        }
        if (_presenter?.TryTakeEffect(out var effect) == true)
        {
            CloseMenu();
            switch (effect)
            {
                case InGameEffect.Resume: _resume(); break;
                case InGameEffect.Settings: OpenSettings(); break;
                case InGameEffect.Replay: _replay(); break;
            }
        }
        _visualPolicy.Apply(Host, _menu, Pages.Page, Pages.Top);
        _visualPolicy.ApplyRoute(Host, Pages.Page, ReplayVisible ? "playback" : Pages.PageKey);
        _visualPolicy.ApplyRoute(Host, Pages.Top, ReplayVisible ? "playback" : Pages.PageKey);
    }
    internal void Back()
    {
        if (ReplayVisible) { _playbackController?.Dispatch(TheatrePlaybackAction.Back); return; }
        if (_results.Back() != true && _hud?.Back() != true && _settingsPresenter?.Back() != true) _presenter?.Back();
    }
    internal RmlUiTextInputBounds? CaptureHudCanvas() => _hud?.CaptureCanvasBounds();
    internal void DispatchTouch(AndroidRmlUiTouchEvent touch)
    {
        if (_hud?.Active == true && touch.Document == _hud.Document && touch.Document == Host.CurrentInputDocument)
            _hud.DispatchPointer(touch.PointerId, touch.Kind, touch.X, touch.Y, touch: true);
    }
    internal void Presented(bool usable) => _settingsPresenter?.ObservePresentedFrame(usable);
    internal void Dispatch(RmlUiPlatformInputEvent input)
    {
        if (ReplayVisible)
        {
            var viewport = _playback!.Viewport;
            if (input.Kind == RmlUiPlatformInputKind.PointerDown && input.Code == 0)
            {
                if (Host.TryGetElementBounds(_playback.Document, "replay_viewport", out float x, out float y, out float w, out float h)
                    && input.X >= x && input.Y >= y && input.X < x + w && input.Y < y + h)
                    viewport.PointerDownInWindow(input.X, input.Y);
                else viewport.Release();
            }
            else if (input.Kind == RmlUiPlatformInputKind.PointerMove) viewport.PointerMoveInWindow(input.X, input.Y);
            else if (input.Kind == RmlUiPlatformInputKind.PointerUp) viewport.PointerUp();
            else if (input.Kind == RmlUiPlatformInputKind.KeyDown && viewport.KeyDown(AndroidRmlUiKeys.Physical(input.Code))) return;
            else if (input.Kind == RmlUiPlatformInputKind.KeyUp && viewport.KeyUp(AndroidRmlUiKeys.Physical(input.Code))) return;
        }
        if (_settingsPresenter != null && input.Kind == RmlUiPlatformInputKind.KeyDown
            && _settingsPresenter.TryCaptureKey(AndroidRmlUiKeys.Physical(input.Code))) return;
        if (_settingsPresenter != null && input.Kind == RmlUiPlatformInputKind.Wheel
            && _settingsPresenter.TryCaptureWheel((float)input.Delta)) return;
        Host.Input.Dispatch(input);
    }
    private void OpenSettings()
    {
        if (!NetSession.Active && !DemoPlayback.IsActive && !_state.MenuPause)
        { _state.PauseMenu(); _pausedLocal = true; }
        _settingsPresenter = new(Host, Pages, _menu, _state, () =>
        { _settingsPresenter?.Dispose(); _settingsPresenter = null; Open(); },
            () => throw new InvalidOperationException("Game data cannot be replaced during a match."), inGame: true, players: _players,
            editHud: () => { _hud ??= new(Host, Pages, _settingsPresenter!); _hud.Open(); });
        _settingsPresenter.Open();
    }
    internal void ReleaseInput() { _hud?.ReleaseInput(); _playback?.Viewport.Release(); Host.ReleaseInput(); _gamepad.Reset(); }
    private void OpenReplay()
    {
        CloseMenu(); _results.Update(_scene, menuVisible: true);
        _playbackController = new(new TheatreEngineBackend());
        _playback = new(Host, Pages, _playbackController); _playback.Open(); _gamepad.Reset();
    }
    private void CloseReplay()
    {
        if (_playback == null) return;
        ReleaseInput(); _playback.Dispose(); _playback = null;
        _playbackController?.Dispose(); _playbackController = null;
    }
    private Vector4i? ReplayBounds(int width, int height)
    {
        if (!ReplayVisible || !Host.TryGetElementBounds(_playback!.Document, "replay_viewport",
            out float x, out float y, out float w, out float h)
            || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(w) || !float.IsFinite(h)) return null;
        int left = (int)Math.Round(x), top = (int)Math.Round(y);
        int right = (int)Math.Round(x + w), bottom = (int)Math.Round(y + h);
        if (left < 0 || top < 0 || right <= left || bottom <= top || right > width || bottom > height) return null;
        return new(left, top, right - left, bottom - top);
    }
    private void CloseMenu()
    {
        ReleaseInput(); _presenter?.Dispose(); _presenter = null;
        _hud?.Dispose(); _hud = null;
        _settingsPresenter?.Dispose(); _settingsPresenter = null;
        _controller?.Dispose(); _controller = null;
        if (_pausedLocal) { _state.UnpauseMenu(); _pausedLocal = false; }
    }
    internal void Close() { CloseMenu(); CloseReplay(); _results.Close(); }
    private void Gamepad(UiAction action)
    {
        if (action == UiAction.Back) { Back(); return; }
        int code = action switch { UiAction.Accept => 2, UiAction.Up => 5, UiAction.Down => 6,
            UiAction.Left => 7, UiAction.Right => 8, UiAction.PageUp => 11, UiAction.PageDown => 12, _ => 1 };
        var modifiers = action == UiAction.PreviousTab ? RmlUiInputModifiers.Shift : RmlUiInputModifiers.None;
        Host.Input.Dispatch(new(Host.CurrentInputDocument, RmlUiPlatformInputKind.KeyDown, RmlUiInputDevice.Gamepad, Code: code, Modifiers: modifiers));
        Host.Input.Dispatch(new(Host.CurrentInputDocument, RmlUiPlatformInputKind.KeyUp, RmlUiInputDevice.Gamepad, Code: code, Modifiers: modifiers));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { Close(); _results.Dispose(); Pages.Dispose(); } finally { _replayBoundsLease.Dispose(); Host.Dispose(); }
    }
}
#endif
