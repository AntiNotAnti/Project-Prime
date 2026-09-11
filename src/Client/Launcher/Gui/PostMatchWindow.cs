using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Input;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

internal enum PostMatchTransition { Wait, Continue, Lobby, Quit }

internal enum PostMatchPresentationMode
{
    Results,
    ContinuationLoading
}

internal static class PostMatchFlow
{
    internal static PostMatchTransition Evaluate(NodeControlClient.ViewState? state, Guid completedMatch, bool gameWindowOpen = true)
    {
        if (!gameWindowOpen) return PostMatchTransition.Quit;
        if (state?.Lobby == null) return PostMatchTransition.Lobby;
        if (state.Handoff is { } next && next.MatchId != completedMatch && !state.MatchEnded)
            return PostMatchTransition.Continue;
        if (state.LastMatchInterrupted && state.LastEndedMatchId != completedMatch
            || state.Lobby.Phase == LobbyPhase.Open)
            return PostMatchTransition.Lobby;
        return PostMatchTransition.Wait;
    }
}

/// <summary>Desktop frame only; the view and Node controller own presentation and commands.</summary>
internal sealed class PostMatchWindow : Window
{
    private readonly PostMatchView _view;
    private readonly PlayController _play;
    private readonly Guid _completedMatch;
    private readonly PostMatchCommandScope _commands = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Func<bool>? _pump;
    private DispatcherFrame? _frame;
    private GamepadButtons _previousButtons;
    private int _previousDirection;
    private DateTimeOffset _repeatAt;
    private bool _closed;
    private string? _message;
    private bool _leaveRequested;
    private Task? _voteTask;
    private Task? _leaveTask;
    private bool _continuationCompleted;
    private bool _ownerClosing;
    public PostMatchTransition Transition { get; private set; }
    public string? Failure { get; private set; }
    internal PostMatchPresentationMode Mode { get; private set; } = PostMatchPresentationMode.Results;
    internal PostMatchView View => _view;
    internal bool IsScenePumpActive => _pump != null;
    internal bool IsGameplayPollingActive => _timer.IsEnabled;
    internal bool IsClosed => _closed;
    internal event EventHandler? UserCloseRequested;

    internal PostMatchWindow(PlayController play, Guid completedMatch, MatchResultsSnapshot? results)
    {
        _play = play;
        _completedMatch = completedMatch;
        _view = new PostMatchView(results, AuthoritativePlay.Current?.LocalSlot ?? -1);
        Content = _view;
        Title = Branding.Name + " — Results";
        Icon = GuiTheme.AppIcon.Value;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = GuiTheme.ScrimBrush;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        _view.VoteRequested += Vote;
        _view.LeaveRequested += Leave;
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) =>
        {
            bool userClose = !_ownerClosing;
            _closed = true;
            _timer.Stop();
            _commands.Dispose();
            _view.Dispose();
            if (_frame != null) _frame.Continue = false;
            if (userClose)
            {
                Transition = PostMatchTransition.Quit;
                UserCloseRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    internal PostMatchTransition Wait(Func<bool> pump,
        Action? showResults = null, Action? continuationSelected = null)
    {
        if (_closed || Mode != PostMatchPresentationMode.Results) return Transition;
        _pump = pump;
        if (!_pump()) { Transition = PostMatchFlow.Evaluate(_play.State.Node, _completedMatch, gameWindowOpen: false); CloseForTransition(); return Transition; }
        PauseMenuWindow.CoverGameWindow(this);
        _view.Update(_play.State.Round);
        _previousButtons = GamepadInput.State.Buttons;
        _frame = new DispatcherFrame();
        if (showResults != null) showResults();
        else ShowForTransition();
        Dispatcher.UIThread.PushFrame(_frame);
        _frame = null;
        _pump = null; // The completed scene will now be cleaned; stop its Results event pump.
        if (Transition == PostMatchTransition.Continue && !_closed)
        {
            _timer.Stop();
            if (continuationSelected != null) continuationSelected();
            else EnterContinuationLoading(new MatchTransitionState(
                MatchTransitionStage.LoadingNextRound,
                Detail: "Preparing the next mission."));
        }
        else if (Transition == PostMatchTransition.Quit && !_closed)
        {
            CloseForTransition();
        }
        else
        {
            // Lobby/disconnect paths remain covered until the desktop
            // coordinator has painted the shell and retires both sources.
            _timer.Stop();
        }
        return Transition;
    }

    internal void ShowForTransition()
    {
        if (_closed || Mode != PostMatchPresentationMode.Results) return;
        PauseMenuWindow.CoverGameWindow(this);
        Show();
        Activate();
        _view.Focus();
        _timer.Start();
    }

    private void Tick()
    {
        if (_closed || Mode != PostMatchPresentationMode.Results) return;
        ObserveCommands();
        if (_pump != null)
        {
            if (!_pump()) { Transition = PostMatchFlow.Evaluate(_play.State.Node, _completedMatch, gameWindowOpen: false); CloseForTransition(); return; }
            PauseMenuWindow.CoverGameWindow(this);
        }
        else GamepadDesktop.PollForMenu();
        PollGamepad();
        if (NodeSessions.Current?.Connected != true)
        {
            Failure = "Node connection lost. Reconnect to continue.";
            Transition = PostMatchTransition.Lobby;
            EndResultsWait();
            return;
        }
        NodeControlClient.ViewState? state = _play.State.Node;
        _view.Update(state?.Round, _message ?? state?.Error);
        PostMatchTransition next = PostMatchFlow.Evaluate(state, _completedMatch);
        if (next == PostMatchTransition.Lobby)
        {
            if (state?.LastMatchInterrupted == true && state.LastEndedMatchId != _completedMatch)
                Failure = "The next match could not start. Return to your lobby and try again.";
            Transition = next;
            EndResultsWait();
        }
        else if (next == PostMatchTransition.Continue && _frame != null && !_leaveRequested)
        {
            Transition = next;
            _frame.Continue = false;
        }
    }

    private void EndResultsWait()
    {
        _pump = null;
        _timer.Stop();
        if (_frame is { } frame) frame.Continue = false;
    }

    /// <summary>
    /// Replaces the results presentation with the bounded continuation stage.
    /// The mode is committed before cancelling any work so late command or
    /// preview completions cannot publish stale results UI.
    /// </summary>
    internal bool EnterContinuationLoading(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_closed || Mode != PostMatchPresentationMode.Results) return false;

        Mode = PostMatchPresentationMode.ContinuationLoading;
        Transition = PostMatchTransition.Continue;
        _pump = null;
        _timer.Stop();
        if (_frame is { } frame) frame.Continue = false;
        _previousButtons = GamepadButtons.None;
        _previousDirection = 0;
        _repeatAt = default;
        _commands.Dispose();
        _view.EnterContinuationLoading(state);

        // Keep the results host in front of the still-live game window until
        // the coordinator publishes the next stable shell state.
        Topmost = true;
        Background = GuiTheme.InkBrush;
        if (!IsVisible) Show();
        Activate();
        _view.Focus();
        return true;
    }

    internal void UpdateContinuationLoading(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_closed || Mode != PostMatchPresentationMode.ContinuationLoading) return;
        _view.UpdateContinuationLoading(state);
    }

    /// <summary>Closes the loading stage once its coordinator has handed off.</summary>
    internal bool CompleteContinuation()
    {
        if (_closed || Mode != PostMatchPresentationMode.ContinuationLoading
            || _continuationCompleted)
            return false;
        _continuationCompleted = true;
        CloseForTransition();
        return true;
    }

    internal void CloseForTransition()
    {
        if (_closed) return;
        _ownerClosing = true;
        Close();
    }

    private void Vote(byte option)
    {
        if (_closed || Mode != PostMatchPresentationMode.Results || _voteTask != null
            || !_commands.TryBeginVote(out CancellationToken token)) return;
        _message = null;
        _voteTask = RunVoteAsync(option, token);
    }

    private void Leave()
    {
        if (_closed || Mode != PostMatchPresentationMode.Results || _leaveRequested || _leaveTask != null
            || !_commands.TryBeginLeave(out CancellationToken token)) return;
        _leaveRequested = true;
        _message = null;
        _leaveTask = RunLeaveAsync(token);
    }

    private async Task RunVoteAsync(byte option, CancellationToken token)
    {
        try { await _play.CastPostMatchVoteAsync(option, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { PostToUi(() => ShowCommandError(ex)); }
        finally { _commands.CompleteVote(); }
    }

    private async Task RunLeaveAsync(CancellationToken token)
    {
        try { await _play.LeaveLobbyAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            PostToUi(() => _leaveRequested = false);
        }
        catch (Exception ex)
        {
            PostToUi(() =>
            {
                _leaveRequested = false;
                ShowCommandError(ex);
            });
        }
        finally { _commands.CompleteLeave(); }
    }

    private void ObserveCommands()
    {
        if (_voteTask is { IsCompleted: true })
        {
            _voteTask.GetAwaiter().GetResult();
            _voteTask = null;
        }
        if (_leaveTask is { IsCompleted: true })
        {
            _leaveTask.GetAwaiter().GetResult();
            _leaveTask = null;
        }
    }

    private void PostToUi(Action action)
    {
        if (_closed || Mode != PostMatchPresentationMode.Results) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && Mode == PostMatchPresentationMode.Results) action();
        });
    }

    private void ShowCommandError(Exception ex)
    {
        if (_closed || Mode != PostMatchPresentationMode.Results) return;
        _message = ex.Message;
        _view.RejectPending();
        _view.Update(_play.State.Round, ex.Message);
    }

    private void PollGamepad()
    {
        if (_closed || Mode != PostMatchPresentationMode.Results) return;
        GamepadState state = GamepadInput.State;
        GamepadButtons pressed = state.Buttons & ~_previousButtons;
        int direction = state.Down(GamepadButtons.DpadDown) || state.Down(GamepadButtons.DpadRight)
            || state.LeftY < -.65f || state.LeftX > .65f ? 1
            : state.Down(GamepadButtons.DpadUp) || state.Down(GamepadButtons.DpadLeft)
                || state.LeftY > .65f || state.LeftX < -.65f ? -1 : 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (direction != 0 && (direction != _previousDirection || now >= _repeatAt))
        {
            _view.MoveSelection(direction);
            _repeatAt = now.AddMilliseconds(direction != _previousDirection ? 350 : 120);
        }
        if (direction != 0 || pressed != GamepadButtons.None)
            _view.SetInputDevice(PrimeInputDevice.Gamepad);
        if ((pressed & GamepadButtons.A) != 0) _view.SubmitSelection();
        else if ((pressed & GamepadButtons.B) != 0)
        {
            if (_view.LeaveConfirmationPending) _view.CancelLeaveConfirmation();
            else _view.RequestLeave();
        }
        _previousButtons = state.Buttons;
        _previousDirection = direction;
    }
}
