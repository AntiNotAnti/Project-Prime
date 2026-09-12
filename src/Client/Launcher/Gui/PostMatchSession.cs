using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Input;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Scene-bound results/continuation state without a native window.  The
/// persistent desktop overlay owns the surface; this session owns the view,
/// ballot commands, and the nested results wait.  In particular, the scene
/// pump is cut off before a continuation callback is invoked.
/// </summary>
internal sealed class PostMatchSession : IDisposable
{
    private readonly PostMatchView _view;
    private readonly PlayController _play;
    private readonly Guid _completedMatch;
    private readonly DesktopInputOwner _inputOwner;
    private readonly PostMatchCommandScope _commands = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Func<bool>? _pump;
    private DispatcherFrame? _frame;
    private GamepadButtons _previousButtons;
    private int _previousDirection;
    private DateTimeOffset _repeatAt;
    private bool _closed;
    private bool _disposed;
    private string? _message;
    private bool _leaveRequested;
    private Task? _voteTask;
    private Task? _hunterTask;
    private Task? _leaveTask;
    private bool _continuationCompleted;

    internal PostMatchTransition Transition { get; private set; }
    internal string? Failure { get; private set; }
    internal PostMatchPresentationMode Mode { get; private set; }
        = PostMatchPresentationMode.Results;
    internal PostMatchView View => _view;
    internal bool IsScenePumpActive => _pump != null;
    internal bool IsPolling => _timer.IsEnabled;
    internal bool IsClosed => _closed;

    internal PostMatchSession(PlayController play, Guid completedMatch,
        MatchResultsSnapshot? results, DesktopInputOwner inputOwner)
    {
        _play = play ?? throw new ArgumentNullException(nameof(play));
        _inputOwner = inputOwner ?? throw new ArgumentNullException(nameof(inputOwner));
        _completedMatch = completedMatch;
        _view = new PostMatchView(results, AuthoritativePlay.Current?.LocalSlot ?? -1);
        _view.VoteRequested += Vote;
        _view.HunterRequested += SelectHunter;
        _view.LeaveRequested += Leave;
        _timer.Tick += (_, _) => Tick();
    }

    internal PostMatchTransition Wait(Func<bool> pump,
        Action showResults, Action? continuationSelected = null)
    {
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(showResults);
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results)
            return Transition;

        _pump = pump;
        if (!_pump())
        {
            Transition = PostMatchFlow.Evaluate(_play.State.Node, _completedMatch,
                gameWindowOpen: false);
            CloseForTransition();
            return Transition;
        }
        UpdateView(_play.State.Node);
        _previousButtons = GamepadInput.State.Buttons;
        _frame = new DispatcherFrame();
        showResults();
        _timer.Start();
        Dispatcher.UIThread.PushFrame(_frame);
        _frame = null;

        // The completed scene is cleaned immediately after this callback
        // returns. Never let a late dispatcher tick pump that scene while the
        // next-map transition is being selected.
        _pump = null;
        _timer.Stop();
        if (Transition == PostMatchTransition.Continue && !_closed)
        {
            continuationSelected?.Invoke();
            if (continuationSelected == null)
            {
                EnterContinuationLoading(new MatchTransitionState(
                    MatchTransitionStage.LoadingNextRound,
                    Detail: "Preparing the next mission."));
            }
        }
        else if (Transition == PostMatchTransition.Quit && !_closed)
        {
            CloseForTransition();
        }
        return Transition;
    }

    /// <summary>Advances one same-thread overlay pump while results are open.</summary>
    internal void Pump()
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results)
            return;
        Tick();
    }

    internal void RequestClose()
    {
        if (_closed || _disposed) return;
        Transition = PostMatchTransition.Quit;
        EndResultsWait();
    }

    private void Tick()
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results)
            return;
        ObserveCommands();
        if (_pump != null && !_pump())
        {
            Transition = PostMatchFlow.Evaluate(_play.State.Node, _completedMatch,
                gameWindowOpen: false);
            CloseForTransition();
            return;
        }
        if (_pump == null) GamepadDesktop.PollForMenu();
        PollGamepad();
        if (NodeSessions.Current?.Connected != true)
        {
            Failure = "Connection lost.";
            Transition = PostMatchTransition.Lobby;
            EndResultsWait();
            return;
        }
        NodeControlClient.ViewState? state = _play.State.Node;
        UpdateView(state, _message ?? state?.Error);
        PostMatchTransition next = PostMatchFlow.Evaluate(state, _completedMatch);
        if (next == PostMatchTransition.Lobby)
        {
            if (state?.LastMatchInterrupted == true
                && state.LastEndedMatchId != _completedMatch)
            {
                Failure = "The next match could not start. Return to your lobby and try again.";
            }
            Transition = next;
            EndResultsWait();
        }
        else if (next == PostMatchTransition.Continue && _frame != null
            && !_leaveRequested)
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

    internal bool EnterContinuationLoading(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results)
            return false;
        Mode = PostMatchPresentationMode.ContinuationLoading;
        Transition = PostMatchTransition.Continue;
        EndResultsWait();
        _previousButtons = GamepadButtons.None;
        _previousDirection = 0;
        _repeatAt = default;
        _commands.Dispose();
        _view.EnterContinuationLoading(state);
        return true;
    }

    internal void UpdateContinuationLoading(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_closed || _disposed || Mode != PostMatchPresentationMode.ContinuationLoading)
            return;
        _view.UpdateContinuationLoading(state);
    }

    internal bool CompleteContinuation()
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.ContinuationLoading
            || _continuationCompleted)
            return false;
        _continuationCompleted = true;
        CloseForTransition();
        return true;
    }

    internal void CloseForTransition()
    {
        if (_closed) return;
        _closed = true;
        EndResultsWait();
        _commands.Dispose();
        _view.VoteRequested -= Vote;
        _view.HunterRequested -= SelectHunter;
        _view.LeaveRequested -= Leave;
        _view.Dispose();
        if (_frame is { } frame) frame.Continue = false;
    }

    private void Vote(byte option)
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results
            || _voteTask != null || !_commands.TryBeginVote(out CancellationToken token))
            return;
        _message = null;
        _voteTask = RunVoteAsync(option, token);
    }

    private void SelectHunter(Hunter hunter)
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results
            || _hunterTask != null
            || !_commands.TryBeginHunter(out CancellationToken token))
        {
            _view.RejectPending();
            return;
        }
        _message = null;
        _hunterTask = RunHunterAsync(hunter, token);
    }

    private void Leave()
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results
            || _leaveRequested || _leaveTask != null
            || !_commands.TryBeginLeave(out CancellationToken token))
            return;
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

    private async Task RunHunterAsync(Hunter hunter, CancellationToken token)
    {
        try { await _play.SelectLobbyHunterAsync(hunter, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { PostToUi(() => ShowCommandError(ex)); }
        finally { _commands.CompleteHunter(); }
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
        if (_hunterTask is { IsCompleted: true })
        {
            _hunterTask.GetAwaiter().GetResult();
            _hunterTask = null;
        }
        if (_leaveTask is { IsCompleted: true })
        {
            _leaveTask.GetAwaiter().GetResult();
            _leaveTask = null;
        }
    }

    private void PostToUi(Action action)
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && !_disposed && Mode == PostMatchPresentationMode.Results) action();
        });
    }

    private void ShowCommandError(Exception ex)
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results) return;
        _message = ex.Message;
        _view.RejectPending();
        UpdateView(_play.State.Node, ex.Message);
    }

    private void UpdateView(NodeControlClient.ViewState? state, string? message = null)
        => _view.Update(state?.Round, message, state?.Lobby,
            state?.Session?.SessionId);

    private void PollGamepad()
    {
        if (_closed || _disposed || Mode != PostMatchPresentationMode.Results) return;
        GamepadState state = GamepadInput.State;
        GamepadButtons pressed = _inputOwner.ConsumePressed(state.Buttons);
        int direction = state.Down(GamepadButtons.DpadDown)
            || state.Down(GamepadButtons.DpadRight) || state.LeftY < -.65f
            || state.LeftX > .65f ? 1
            : state.Down(GamepadButtons.DpadUp)
                || state.Down(GamepadButtons.DpadLeft) || state.LeftY > .65f
                || state.LeftX < -.65f ? -1 : 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (direction != 0 && (direction != _previousDirection || now >= _repeatAt))
        {
            _view.MoveSelection(direction);
            _repeatAt = now.AddMilliseconds(direction != _previousDirection ? 350 : 120);
        }
        if (direction != 0 || pressed != GamepadButtons.None)
            _view.SetInputDevice(PrimeInputDevice.Gamepad);
        if ((pressed & GamepadButtons.LeftBumper) != 0) _view.CycleHunter(-1);
        else if ((pressed & GamepadButtons.RightBumper) != 0) _view.CycleHunter(1);
        else if ((pressed & GamepadButtons.A) != 0) _view.SubmitSelection();
        else if ((pressed & GamepadButtons.B) != 0)
        {
            if (_view.LeaveConfirmationPending) _view.CancelLeaveConfirmation();
            else _view.RequestLeave();
        }
        _previousButtons = state.Buttons;
        _previousDirection = direction;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _closed = true;
        EndResultsWait();
        _commands.Dispose();
        _view.VoteRequested -= Vote;
        _view.HunterRequested -= SelectHunter;
        _view.LeaveRequested -= Leave;
        _view.Dispose();
    }
}
