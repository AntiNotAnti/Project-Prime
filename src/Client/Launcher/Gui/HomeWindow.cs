using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// A frame around <see cref="PrimeShellView"/>, and nothing else.
    ///
    /// Everything the front screen *is* lives in the view, which is what the
    /// Android head shows directly; this is the title bar, the icon and the
    /// size, which are the three things a phone has no use for. What is
    /// deliberately not the same as the WinForms screen it replaced is the
    /// chrome: an ordinary decorated window rather than a borderless panel,
    /// because a window with no frame that a Linux window manager will not let
    /// you move is a trap, and there are many window managers.
    /// </summary>
    internal sealed class HomeWindow : Window
    {
        private readonly PrimeShellView _view;
        private readonly DesktopGameOverlayCoordinator? _overlay;
        private PostMatchWindow? _results;
        private bool _waitingForContinuation;

        /// <summary>What the screen decided. Kind None means it was closed.</summary>
        public LaunchPlan Plan => IsClosed ? default : _view.Plan;
        internal ClientSessionCoordinator SessionCoordinator => _view.Online.Flow;
        internal bool WaitingForContinuation => _waitingForContinuation;
        public bool IsClosed { get; private set; }
        public event EventHandler? LaunchRequested;
        internal event EventHandler? GameHostPrewarmRequested;
        internal event EventHandler? ResultsCloseRequested;
        internal event EventHandler? TransitionReturnToLobbyRequested;
        /// <summary>
        /// Raised when an automatic next-match handoff has finished and the
        /// shell may take focus. GuiLauncher routes this through the single
        /// desktop presentation coordinator.
        /// </summary>
        internal event EventHandler? ShellReadyForTransition;

        public void Resume(MatchRunResult? result, bool activate = true)
        {
            _view.Reset();
            if (result != null) _view.ShowMatchOutcome(result);
            _waitingForContinuation = result?.Reason == MatchExitReason.Completed
                && NodeSessions.Current?.State is { Handoff: { } handoff, MatchEnded: false }
                && handoff.MatchId != result.MatchId;
            _view.Activate();
            _view.SetMenuInputEnabled(result == null && !_waitingForContinuation);
            if (result == null)
            {
                Show();
                if (activate) Activate();
            }
            if (_view.Play.State.Lobby != null)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsClosed && _view.Play.State.Lobby != null)
                        GameHostPrewarmRequested?.Invoke(this, EventArgs.Empty);
                }, DispatcherPriority.Background);
        }

        /// <summary>Shows the shell without taking native focus.</summary>
        internal void ShowForPreparation()
        {
            if (IsClosed) return;
            if (_results != null) _results.Topmost = false;
            Topmost = true;
            Show();
        }

        /// <summary>Hides the shell and releases its controller focus.</summary>
        internal void HideForTransition()
        {
            if (IsClosed) return;
            _view.Deactivate();
            Hide();
            Topmost = false;
        }

        /// <summary>Takes native focus after the shell has been prepared.</summary>
        internal void ActivateForTransition()
        {
            if (IsClosed) return;
            _view.Activate();
            _view.SetMenuInputEnabled(true);
            Activate();
            Topmost = false;
        }

        internal void ShowMatchTransition(MatchTransitionState state)
        {
            if (IsClosed) return;
            Topmost = true;
            _view.SetMenuInputEnabled(false);
            _view.ShowMatchTransition(state);
        }

        internal void UpdateMatchTransition(MatchTransitionState state)
            => _view.UpdateMatchTransition(state);

        internal void FailMatchTransition(string message)
        {
            if (IsClosed) return;
            ShowForPreparation();
            _view.FailMatchTransition(message);
            // Failure is an interactive modal state. The underlying shell is
            // still isolated by modal navigation, while controller polling is
            // required for the only recovery action.
            _view.SetMenuInputEnabled(true);
        }

        internal void CloseMatchTransition() => _view.CloseMatchTransition();

        internal void ShowContinuationTransition(MatchTransitionState state)
        {
            if (_overlay != null) _overlay.ShowContinuationTransition(state);
            else _results?.EnterContinuationLoading(state);
        }

        internal void UpdateContinuationTransition(MatchTransitionState state)
        {
            if (_overlay != null) _overlay.UpdateContinuationTransition(state);
            else _results?.UpdateContinuationLoading(state);
        }

        internal void HideResultsForTransition()
        {
            if (_overlay != null)
            {
                _overlay.HideResultsForTransition();
                return;
            }
            if (_results == null) return;
            _results.UserCloseRequested -= ResultsWindowCloseRequested;
            if (!_results.CompleteContinuation()) _results.CloseForTransition();
            _results = null;
        }

        internal void ShowResultsForTransition()
        {
            if (_overlay != null) _overlay.ShowResultsForTransition();
            else _results?.ShowForTransition();
        }

        internal MatchResultsPresentationResult PresentResults(MatchResultsSnapshot? results,
            Func<bool> pump, Action resultsVisible,
            Action<MatchTransitionState> continuationSelected)
        {
            Guid? completed = AuthoritativePlay.Current?.NodeMatchId ?? NodeSessions.Current?.State.JoinedMatchId;
            if (!completed.HasValue) return new();
            if (_overlay != null)
            {
                return _overlay.PresentResults(_view.Play, completed.Value, results,
                    pump, resultsVisible, continuationSelected);
            }
            PauseMenuWindow.CloseIfOpen();
            var resultsWindow = new PostMatchWindow(_view.Play, completed.Value, results);
            _results = resultsWindow;
            resultsWindow.UserCloseRequested += ResultsWindowCloseRequested;
            resultsWindow.Wait(pump, resultsVisible,
                () => continuationSelected(ContinuationState()));
            return new(resultsWindow.Transition == PostMatchTransition.Quit,
                resultsWindow.Failure);
        }

        private MatchTransitionState ContinuationState()
        {
            NodeControlClient.ViewState? state = _view.Play.State.Node;
            LobbyVoteEntry? resolved = state?.Round?.ResolvedOption;
            return new MatchTransitionState(MatchTransitionStage.LoadingNextRound,
                Map: resolved?.MapKey ?? state?.Lobby?.MapKey,
                Mode: (resolved?.Mode ?? state?.Lobby?.Mode)?.ToString(),
                Hunter: state?.Handoff?.Hunter.ToString(),
                Detail: "Preparing the selected arena and frozen roster.");
        }

        private void CloseResults()
        {
            if (_overlay != null)
            {
                _overlay.CloseForTransition();
                return;
            }
            if (_results != null)
            {
                _results.UserCloseRequested -= ResultsWindowCloseRequested;
                _results.CloseForTransition();
            }
            _results = null;
        }

        private void ResultsWindowCloseRequested(object? sender, EventArgs args)
            => ResultsCloseRequested?.Invoke(this, EventArgs.Empty);

        public HomeWindow(MenuSettings settings, IReadOnlyList<string> rooms,
            DesktopGameOverlayCoordinator? overlay = null)
        {
            _overlay = overlay;
            if (_overlay != null)
                _overlay.CloseRequested += OverlayCloseRequested;
            _view = new PrimeShellView(settings, rooms);
            _view.Done += (_, plan) =>
            {
                if (plan.Kind == LaunchKind.None) { Close(); return; }
                _waitingForContinuation = false;
                _view.Deactivate();
                LaunchRequested?.Invoke(this, EventArgs.Empty);
            };
            _view.Play.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (IsClosed) return;
                if (_view.Play.State.Lobby != null)
                    GameHostPrewarmRequested?.Invoke(this, EventArgs.Empty);
                if (!_waitingForContinuation) return;
                if (_view.Play.State.Phase != PlayPhase.Handoff && !_view.Play.State.Loading)
                {
                    _waitingForContinuation = false;
                    ShellReadyForTransition?.Invoke(this, EventArgs.Empty);
                }
            });
            _view.MatchTransitionReturnToLobbyRequested += (_, _) =>
                TransitionReturnToLobbyRequested?.Invoke(this, EventArgs.Empty);
            Closed += (_, _) =>
            {
                IsClosed = true;
                if (_overlay != null) _overlay.CloseRequested -= OverlayCloseRequested;
                CloseResults();
            };
            Closed += (_, _) => _ = _view.DisposeAsync().AsTask();

            Title = Mods.Branding.Name;
            Icon = GuiTheme.AppIcon.Value;
            Width = 940;
            Height = 560;
            MinWidth = 780;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = GuiTheme.PanelBrush;
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Content = _view;
        }

        private void OverlayCloseRequested(object? sender, EventArgs args)
            => ResultsCloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
