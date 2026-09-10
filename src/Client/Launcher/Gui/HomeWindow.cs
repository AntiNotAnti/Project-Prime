using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.Network;

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
        private PostMatchWindow? _results;
        private bool _waitingForContinuation;

        /// <summary>What the screen decided. Kind None means it was closed.</summary>
        public LaunchPlan Plan => IsClosed ? default : _view.Plan;
        internal ClientSessionCoordinator SessionCoordinator => _view.Online.Flow;
        public bool IsClosed { get; private set; }
        public event EventHandler? LaunchRequested;
        public void Resume(MatchRunResult? result)
        {
            _view.Reset();
            if (result != null) _view.ShowMatchOutcome(result);
            _waitingForContinuation = result?.Reason == MatchExitReason.Completed
                && NodeSessions.Current?.State is { Handoff: { } handoff, MatchEnded: false }
                && handoff.MatchId != result.MatchId;
            if (!_waitingForContinuation) { CloseResults(); Show(); }
            _view.Activate();
            _view.SetMenuInputEnabled(!_waitingForContinuation);
            if (!_waitingForContinuation) Activate();
        }

        internal MatchResultsPresentationResult PresentResults(MatchResultsSnapshot? results, Func<bool> pump)
        {
            Guid? completed = AuthoritativePlay.Current?.NodeMatchId ?? NodeSessions.Current?.State.JoinedMatchId;
            if (!completed.HasValue) return new();
            PauseMenuWindow.CloseIfOpen();
            _results = new PostMatchWindow(_view.Play, completed.Value, results);
            _results.Wait(pump);
            return new(_results.Transition == PostMatchTransition.Quit, _results.Failure);
        }

        private void CloseResults()
        {
            _results?.Close();
            _results = null;
        }

        public HomeWindow(MenuSettings settings, IReadOnlyList<string> rooms)
        {
            _view = new PrimeShellView(settings, rooms);
            _view.Done += (_, plan) =>
            {
                if (plan.Kind == LaunchKind.None) { Close(); return; }
                _waitingForContinuation = false;
                CloseResults();
                _view.Deactivate();
                Hide();
                LaunchRequested?.Invoke(this, EventArgs.Empty);
            };
            _view.Play.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (!_waitingForContinuation || IsClosed) return;
                if (_view.Play.State.Phase != PlayPhase.Handoff && !_view.Play.State.Loading)
                {
                    _waitingForContinuation = false;
                    CloseResults();
                    _view.SetMenuInputEnabled(true);
                    Show(); Activate();
                }
            });
            Closed += (_, _) => { IsClosed = true; CloseResults(); };
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
    }
}
