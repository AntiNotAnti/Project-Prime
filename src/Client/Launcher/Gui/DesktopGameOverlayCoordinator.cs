using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Owns the one desktop overlay surface and swaps scene-bound views into it.
/// The mode is committed before replacing content or calling a native window
/// so a close/activation callback cannot re-enter an obsolete presentation.
/// </summary>
internal sealed class DesktopGameOverlayCoordinator : IDisposable, IPauseMenuPresenter
{
    private readonly IDesktopGameOverlaySurface _surface;
    private readonly Func<GameHostPresentationState>? _state;
    private readonly Action? _pump;
    // The transition coordinator supplies the production instance. Tests and
    // standalone overlay callers receive an instance scoped to this
    // coordinator; there is deliberately no process-global owner.
    private readonly DesktopInputOwner _inputOwner;
    private SdlGameHost? _host;
    private Scene? _scene;
    private PauseMenuView? _pauseView;
    private SettingsView? _settingsView;
    private PostMatchSession? _results;
    private MatchTransitionView? _transition;
    private IMatchTransitionMenuActions? _transitionActions;
    private bool _disposed;
    private bool _hostFocusKnown;
    private bool _hostWasFocused;
    private DesktopOverlayMode _mode;

    // Compatibility routing hook for PauseMenu/HomeWindow. It exposes the
    // active coordinator instance only; presentation and input state remain
    // owned by that instance (and by the transition coordinator supplied to
    // it), never by a process-global input singleton.
    internal DesktopOverlayMode Mode => _mode;
    internal bool IsOpen => _mode != DesktopOverlayMode.None;
    internal DesktopGameOverlayWindow? Window => _surface as DesktopGameOverlayWindow;
    internal PostMatchSession? ResultsSession => _results;
    internal DesktopInputOwner InputOwner => _inputOwner;
    internal event EventHandler? CloseRequested;

    bool IPauseMenuPresenter.IsOpen => IsOpen;
    bool IPauseMenuPresenter.Open(Scene scene) => OpenPause(scene);
    void IPauseMenuPresenter.Close() => CloseFromMenu();
    void IPauseMenuPresenter.Pump() => Pump();
    void IPauseMenuPresenter.OnWindowMoved() { }

    internal DesktopGameOverlayCoordinator(IDesktopGameOverlaySurface surface,
        Func<GameHostPresentationState>? state = null, Action? pump = null,
        DesktopInputOwner? inputOwner = null)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _state = state;
        _pump = pump;
        _inputOwner = inputOwner ?? new DesktopInputOwner();
        if (_state != null)
        {
            _hostWasFocused = _state().IsFocused;
            _hostFocusKnown = true;
        }
        _surface.UserCloseRequested += SurfaceUserCloseRequested;
        _surface.Activated += SurfaceActivated;
        _surface.Deactivated += SurfaceDeactivated;
    }

    internal DesktopGameOverlayCoordinator(Func<SdlGameHost?> host,
        Action? pump = null, DesktopInputOwner? inputOwner = null)
        : this(new DesktopGameOverlayWindow(),
            () => host()?.PresentationState ?? default, pump, inputOwner)
    {
        ArgumentNullException.ThrowIfNull(host);
        SdlGameHost? initialHost = host();
        if (initialHost != null) AttachHost(initialHost);
    }

    /// <summary>Attach the shell-owned transition command boundary.</summary>
    internal void AttachTransitionActions(IMatchTransitionMenuActions? actions)
    {
        if (ReferenceEquals(_transitionActions, actions)) return;
        if (_transitionActions != null) _transitionActions.Changed -= TransitionPlayChanged;
        _transitionActions = actions;
        if (_transitionActions != null) _transitionActions.Changed += TransitionPlayChanged;
        PostTransitionRefresh();
    }

    private void TransitionPlayChanged(object? sender, EventArgs args)
        => PostTransitionRefresh();

    private void PostTransitionRefresh(PauseMenuView? expected = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _mode != DesktopOverlayMode.Pause) return;
            PauseMenuView? view = _pauseView;
            if (view == null || expected != null && !ReferenceEquals(view, expected)) return;
            view.RefreshTransitionPresentation();
        }, DispatcherPriority.Background);
    }

    internal void AttachHost(SdlGameHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (ReferenceEquals(_host, host)) return;
        if (_host != null)
        {
            _host.PresentationStateChanged -= HostPresentationChanged;
            _host.DetachInputOwner(_inputOwner);
        }
        _host = host;
        _hostWasFocused = host.PresentationState.IsFocused;
        _hostFocusKnown = true;
        _host.AttachInputOwner(_inputOwner);
        _host.PresentationStateChanged += HostPresentationChanged;
        HostPresentationChanged(host.PresentationState);
    }

    internal bool OpenPause(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ThrowIfDisposed();
        _scene = scene;
        PauseMenu.SetOverlayOpen(true);
        if (_mode == DesktopOverlayMode.Pause)
        {
            ShowCurrent(activate: true);
            return true;
        }
        EndCurrentContent();
        _mode = DesktopOverlayMode.Pause;
        var view = _pauseView = new PauseMenuView(offerWindowMode: true,
            transitionActions: _transitionActions);
        view.Resumed += (_, _) => CloseFromMenu();
        view.SettingsRequested += (_, _) => OpenSettings();
        view.FullscreenRequested += (_, _) =>
        {
            PauseMenu.RequestFullscreenToggle();
            CloseFromMenu();
        };
        view.SpectateRequested += (_, _) =>
        {
            CloseFromMenu();
            SpectatorMode.Start(scene);
        };
        view.RejoinRequested += (_, _) =>
        {
            CloseFromMenu();
            SpectatorMode.Rejoin(scene);
        };
        view.RecordToggleRequested += (_, _) =>
        {
            if (ReplayRecorder.IsRecording) ReplayRecorder.Stop();
            else ReplayRecorder.Start();
            CloseFromMenu();
        };
        view.LeaveRequested += (_, _) =>
        {
            PauseMenu.RequestLeave();
            CloseFromMenu();
        };
        view.QuitRequested += (_, _) =>
        {
            PauseMenu.RequestQuit();
            CloseFromMenu();
        };
        view.RestartMatchRequested += (_, _) => _ = ProposeRestartAsync(view);
        view.ChangeMapRequested += (_, _) => OpenTransitionMapPicker(view);
        view.HunterChangeRequested += hunter => _ = ChangeHunterAsync(view, hunter);
        view.TransitionVoteRequested += accept => _ = CastTransitionVoteAsync(view, accept);
        _surface.SetContent(view, _mode);
        _inputOwner.SetOwner(DesktopInputOwnerKind.Overlay,
            GamepadInput.State.Buttons);
        ShowCurrent(activate: true);
        view.FocusResume();
        return true;
    }

    private async Task ProposeRestartAsync(PauseMenuView view)
    {
        IMatchTransitionMenuActions? actions = _transitionActions;
        if (actions == null) return;
        try { await actions.RequestRestartMatchAsync().ConfigureAwait(false); }
        catch (Exception error) { DebugLog.Line("transition-menu", error.Message); }
        finally { PostTransitionRefresh(view); }
    }

    private async Task CastTransitionVoteAsync(PauseMenuView view, bool accept)
    {
        IMatchTransitionMenuActions? actions = _transitionActions;
        if (actions == null) return;
        try { await actions.RequestTransitionVoteAsync(accept).ConfigureAwait(false); }
        catch (Exception error) { DebugLog.Line("transition-menu", error.Message); }
        finally { PostTransitionRefresh(view); }
    }

    private async Task ChangeHunterAsync(PauseMenuView view, Hunter hunter)
    {
        IMatchTransitionMenuActions? actions = _transitionActions;
        if (actions == null) return;
        try { await actions.RequestHunterChangeAsync(hunter).ConfigureAwait(false); }
        catch (Exception error) { DebugLog.Line("transition-menu", error.Message); }
        finally { PostTransitionRefresh(view); }
    }

    private void OpenTransitionMapPicker(PauseMenuView view)
    {
        IMatchTransitionMenuActions? actions = _transitionActions;
        if (actions == null || _scene == null) return;
        IReadOnlyList<string> maps = actions.AvailableTransitionMaps;
        if (maps.Count == 0) return;
        var picker = new MapPickerView(maps, actions.CurrentMapKey ?? "",
            excludeCurrent: true);
        var window = new MapPickerWindow(picker);
        picker.Closed += async (_, _) =>
        {
            string? map = picker.RoomKey;
            window.Close();
            if (map == null) return;
            try { await actions.RequestChangeMapAsync(map).ConfigureAwait(false); }
            catch (Exception error) { DebugLog.Line("transition-menu", error.Message); }
            finally { PostTransitionRefresh(view); }
        };
        window.Show();
        window.Activate();
    }

    internal void OpenSettings()
    {
        ThrowIfDisposed();
        if (_scene == null) return;
        if (_mode == DesktopOverlayMode.Settings)
        {
            ShowCurrent(activate: true);
            return;
        }
        if (_mode != DesktopOverlayMode.Pause) return;

        // Pause is a presentation session, not a parent window. Dispose it
        // before creating a fresh settings edit session so SettingsView's
        // visual-tree lifetime cannot accidentally share a stale draft.
        _pauseView = null;
        _mode = DesktopOverlayMode.Settings;
        SettingsView settings = _settingsView = new SettingsView(
            ClientSettings.LoadSettings(), inGame: true, scene: _scene,
            embedActionBar: false);
        settings.Closed += SettingsClosed;
        _surface.SetContent(BuildSettingsHost(settings), _mode);
        _inputOwner.SetOwner(DesktopInputOwnerKind.Overlay,
            GamepadInput.State.Buttons);
        ShowCurrent(activate: true);
    }

    private void SettingsClosed(object? sender, EventArgs args)
    {
        SettingsView? settings = _settingsView;
        if (settings == null || !ReferenceEquals(sender, settings)) return;
        settings.Closed -= SettingsClosed;
        settings.Dispose();
        _settingsView = null;
        if (_disposed || _mode != DesktopOverlayMode.Settings) return;
        // Settings -> Pause is explicit: the old edit session is complete and
        // a new pause view is mounted into the same native window.
        _surface.SetContent(null, DesktopOverlayMode.None);
        _mode = DesktopOverlayMode.None;
        if (_scene != null) OpenPause(_scene);
    }

    private static Control BuildSettingsHost(SettingsView settings)
    {
        var host = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = GuiTheme.InkBrush
        };
        host.Children.Add(settings);
        var actions = new SettingsActionBar(settings, inGame: true);
        host.Children.Add(actions);
        Grid.SetRow(actions, 1);
        return host;
    }

    internal MatchResultsPresentationResult PresentResults(PlayController play,
        Guid completedMatch, MatchResultsSnapshot? results, Func<bool> pump,
        Action resultsVisible, Action<MatchTransitionState> continuationSelected)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(resultsVisible);
        ArgumentNullException.ThrowIfNull(continuationSelected);
        ThrowIfDisposed();
        EndCurrentContent();
        _mode = DesktopOverlayMode.Results;
        PauseMenu.SetOverlayOpen(true);
        PostMatchSession session = _results = new PostMatchSession(play,
            completedMatch, results, _inputOwner);
        _surface.SetContent(session.View, _mode);
        _inputOwner.SetOwner(DesktopInputOwnerKind.Overlay,
            GamepadInput.State.Buttons);
        session.Wait(pump, () =>
        {
            resultsVisible();
            // The transition coordinator commits Results before this native
            // show callback. If a direct caller has no coordinator, still
            // make the overlay visible here.
            if (_mode == DesktopOverlayMode.Results) ShowCurrent(activate: true);
        }, () => continuationSelected(ContinuationState(play)));
        return new(session.Transition == PostMatchTransition.Quit,
            session.Failure);
    }

    internal void ShowResultsForTransition()
    {
        if (_mode != DesktopOverlayMode.Results || _results == null) return;
        _inputOwner.SetOwner(DesktopInputOwnerKind.Overlay,
            GamepadInput.State.Buttons);
        ShowCurrent(activate: true);
    }

    internal void ShowContinuationTransition(MatchTransitionState state)
    {
        if (_mode == DesktopOverlayMode.Results && _results != null)
        {
            if (!_results.EnterContinuationLoading(state)) return;
            _mode = DesktopOverlayMode.ContinuationLoading;
            _surface.SetContent(_results.View, _mode);
        }
        else if (_mode == DesktopOverlayMode.None)
        {
            // A Node-owned transition may begin directly from gameplay. There
            // is no Results session to reuse in that path, but the same
            // presentation surface still owns the short loading modal.
            _mode = DesktopOverlayMode.ContinuationLoading;
            PauseMenu.SetOverlayOpen(true);
            _transition = new MatchTransitionView(state);
            _transition.ReturnToLobbyRequested += DirectTransitionReturnRequested;
            _surface.SetContent(_transition, _mode);
        }
        else return;
        _inputOwner.SetOwner(DesktopInputOwnerKind.Overlay,
            GamepadInput.State.Buttons);
        ShowCurrent(activate: true);
    }

    internal void UpdateContinuationTransition(MatchTransitionState state)
    {
        if (_mode != DesktopOverlayMode.ContinuationLoading) return;
        if (_results != null) _results.UpdateContinuationLoading(state);
        else _transition?.Update(state);
    }

    internal void HideResultsForTransition()
    {
        if (_results != null)
        {
            PostMatchSession session = _results;
            _results = null;
            if (!session.CompleteContinuation()) session.CloseForTransition();
            session.Dispose();
        }
        if (_transition != null)
        {
            _transition.ReturnToLobbyRequested -= DirectTransitionReturnRequested;
            _transition.Dispose();
            _transition = null;
        }
        _surface.ReleaseContent();
        _mode = DesktopOverlayMode.None;
        PauseMenu.SetOverlayOpen(false);
        _inputOwner.SetOwner(_scene == null ? DesktopInputOwnerKind.None
            : DesktopInputOwnerKind.Scene, GamepadInput.State.Buttons);
    }

    internal void HideContinuationTransition() => HideResultsForTransition();

    internal void Pump()
    {
        // The game thread calls this exactly once at its frame-loop boundary;
        // no second persistent polling timer is introduced for the overlay.
        _pump?.Invoke();
        if (_mode == DesktopOverlayMode.Results && _results != null
            && !_results.IsPolling)
        {
            _results.Pump();
        }
    }

    /// <summary>Headless seam for deterministic host minimize/restore tests.</summary>
    internal void ApplyHostPresentationForTests(GameHostPresentationState state)
        => HostPresentationChanged(state);

    internal void CloseFromMenu()
    {
        if (_mode == DesktopOverlayMode.None) return;
        // Commit the logical handoff before hiding the native overlay. Hiding
        // can synchronously transfer Windows focus back to the SDL window.
        _mode = DesktopOverlayMode.None;
        EndCurrentContent();
        PauseMenu.SetOverlayOpen(false);
        _inputOwner.SetOwner(_scene == null ? DesktopInputOwnerKind.None
            : DesktopInputOwnerKind.Scene, GamepadInput.State.Buttons);
        PauseMenu.MarkClosed();
        if (_host is { PresentationState.IsVisible: true,
            PresentationState.IsMinimized: false })
        {
            DebugLog.Line("sdl", "overlay closed; returning focus to scene");
            _host.Activate();
        }
    }

    internal void CloseForTransition()
    {
        if (_mode == DesktopOverlayMode.None) return;
        EndCurrentContent();
        _mode = DesktopOverlayMode.None;
        PauseMenu.SetOverlayOpen(false);
        _inputOwner.SetOwner(_scene == null ? DesktopInputOwnerKind.None
            : DesktopInputOwnerKind.Scene, GamepadInput.State.Buttons);
    }

    private void EndCurrentContent()
    {
        SettingsView? settings = _settingsView;
        _settingsView = null;
        if (settings != null)
        {
            settings.Closed -= SettingsClosed;
            settings.Dispose();
        }
        _pauseView = null;
        PostMatchSession? results = _results;
        _results = null;
        if (results != null) results.Dispose();
        MatchTransitionView? transition = _transition;
        _transition = null;
        if (transition != null)
        {
            transition.ReturnToLobbyRequested -= DirectTransitionReturnRequested;
            transition.Dispose();
        }
        _surface.ReleaseContent();
    }

    private void ShowCurrent(bool activate)
    {
        if (_mode == DesktopOverlayMode.None) return;
        GameHostPresentationState state = CurrentHostState();
        if (state.IsMinimized)
        {
            _surface.SetZOrderOwned(false);
            _surface.HideForHostPreservingContent(state);
        }
        else
            _surface.ShowForHost(state, activate);
    }

    private GameHostPresentationState CurrentHostState()
    {
        GameHostPresentationState state = _state?.Invoke() ?? _host?.PresentationState ?? default;
        if (state.HasValidGeometry) return state;
        return new GameHostPresentationState(new(1280, 720), new(1280, 720),
            default, IsVisible: true, IsMinimized: false, IsFocused: true,
            ActivationDeferred: false, IsFullscreen: false);
    }

    private void HostPresentationChanged(GameHostPresentationState state)
    {
        bool regainedFocus = _hostFocusKnown && !_hostWasFocused
            && state.IsFocused;
        _hostWasFocused = state.IsFocused;
        _hostFocusKnown = true;
        if (_disposed || _mode == DesktopOverlayMode.None) return;
        if (state.IsMinimized || !state.IsVisible)
        {
            // A source-host visibility change is not an activation request.
            // Suspend the overlay z-order while retaining its logical mode and
            // attached content for minimize/settings-draft preservation.
            _surface.SetZOrderOwned(false);
            _surface.HideForHostPreservingContent(state);
        }
        else
        {
            // The SDL window is the taskbar-visible member of the pair. When
            // Windows restores it after Alt-Tab while a logical overlay is
            // still open, return activation to that overlay exactly once.
            // A focus-loss edge never activates anything, so switching away
            // from Project Prime still yields to the foreground application.
            if (regainedFocus)
            {
                DebugLog.Line("sdl",
                    $"window focus returned with {_mode} overlay open; activating overlay");
            }
            _surface.ShowForHost(state, activate: regainedFocus);
        }
    }

    private void SurfaceUserCloseRequested(object? sender, EventArgs args)
    {
        if (_disposed) return;
        if (_results != null)
        {
            _results.RequestClose();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        CloseFromMenu();
    }

    private void DirectTransitionReturnRequested(object? sender, EventArgs args)
    {
        if (_disposed) return;
        CloseForTransition();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SurfaceActivated(object? sender, EventArgs args)
    {
        if (!_disposed && _mode != DesktopOverlayMode.None)
        {
            _surface.SetZOrderOwned(true);
            _inputOwner.SetOwner(DesktopInputOwnerKind.Overlay,
                GamepadInput.State.Buttons);
        }
    }

    private void SurfaceDeactivated(object? sender, EventArgs args)
    {
        // Deactivation is not proof of Alt-Tab. Release input ownership, but
        // leave the logical mode/content intact and never auto-activate on a
        // background restore. A real activation event reacquires ownership.
        if (!_disposed && _mode != DesktopOverlayMode.None)
        {
            _surface.SetZOrderOwned(false);
            if (_inputOwner.Owns(DesktopInputOwnerKind.Overlay))
                _inputOwner.SetOwner(DesktopInputOwnerKind.None);
        }
    }

    private MatchTransitionState ContinuationState(PlayController play)
    {
        NodeControlClient.ViewState? state = play.State.Node;
        LobbyVoteEntry? resolved = state?.Round?.ResolvedOption;
        return new MatchTransitionState(MatchTransitionStage.LoadingNextRound,
            Map: resolved?.MapKey ?? state?.Lobby?.MapKey,
            Mode: (resolved?.Mode ?? state?.Lobby?.Mode)?.ToString(),
            Hunter: state?.Handoff?.Hunter.ToString(),
            Detail: "Preparing the selected arena and frozen roster.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_host != null)
        {
            _host.PresentationStateChanged -= HostPresentationChanged;
            _host.DetachInputOwner(_inputOwner);
        }
        EndCurrentContent();
        _mode = DesktopOverlayMode.None;
        _inputOwner.SetOwner(DesktopInputOwnerKind.None);
        _surface.UserCloseRequested -= SurfaceUserCloseRequested;
        _surface.Activated -= SurfaceActivated;
        _surface.Deactivated -= SurfaceDeactivated;
        if (_transitionActions != null)
            _transitionActions.Changed -= TransitionPlayChanged;
        _transitionActions = null;
        _surface.Dispose();
        CloseRequested = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
