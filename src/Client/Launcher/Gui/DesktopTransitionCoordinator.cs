using System;
using System.Diagnostics;
using MphRead;

namespace MphRead.Mods.Launcher.Gui;

internal interface IDesktopTransitionSurface
{
    void ShowShellForPreparation();
    void PumpShell();
    void HideShell();
    void ActivateShell();
    void ShowSceneForPreparation();
    void HideScene();
    void ActivateScene();
    void ShowShellTransition(MatchTransitionState state);
    void UpdateShellTransition(MatchTransitionState state);
    void FailShellTransition(string message);
    void CloseShellTransition();
    void ShowContinuationTransition(MatchTransitionState state);
    void UpdateContinuationTransition(MatchTransitionState state);
    void ShowResults();
    void HideResults();
}

internal enum DesktopTransitionState
{
    Shell,
    PreparingGame,
    Game,
    Results,
    PreparingContinuation,
    ReturningToShell,
    Failed,
    Closing
}

/// <summary>
/// Owns only desktop presentation handoffs. State and generation are committed
/// before native calls because Show, Hide, Activate, Close, and dispatcher
/// pumping may synchronously re-enter.
/// </summary>
internal sealed class DesktopTransitionCoordinator : IDisposable
{
    private readonly IDesktopTransitionSurface _surface;
    private readonly Action<string> _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DesktopInputOwner _inputOwner;
    private DesktopTransitionState _state = DesktopTransitionState.Shell;
    private ulong _generation;
    private bool _scenePrepared;
    private bool _disposed;

    public DesktopTransitionCoordinator(IDesktopTransitionSurface surface,
        Action<string>? log = null, DesktopInputOwner? inputOwner = null)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _log = log ?? (message => DebugLog.Line("transition", message));
        _inputOwner = inputOwner ?? new DesktopInputOwner();
        if (_surface is DesktopTransitionSurface desktopSurface)
            desktopSurface.AttachInputOwner(_inputOwner);
    }

    public DesktopTransitionState State => _state;
    public ulong CurrentGeneration => _generation;
    internal DesktopInputOwner InputOwner => _inputOwner;

    public ulong BeginMatchLaunch(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfClosed();
        if (_state == DesktopTransitionState.PreparingGame) return _generation;
        if (_state != DesktopTransitionState.Shell)
            throw new InvalidOperationException($"Cannot launch a match from {_state}.");

        ulong generation = checked(++_generation);
        _state = DesktopTransitionState.PreparingGame;
        _scenePrepared = false;
        Log($"generation={generation} shell -> preparing-match");
        _surface.ShowShellTransition(state);
        return generation;
    }

    public void BeginResults()
    {
        ThrowIfClosed();
        if (_state == DesktopTransitionState.Results) return;
        if (_state != DesktopTransitionState.Game)
            throw new InvalidOperationException($"Cannot show results from {_state}.");
        _state = DesktopTransitionState.Results;
        Log($"generation={_generation} results visible");
        _surface.ShowResults();
    }

    public ulong BeginContinuation(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfClosed();
        if (_state == DesktopTransitionState.PreparingContinuation)
        {
            _surface.UpdateContinuationTransition(state);
            return _generation;
        }
        if (_state != DesktopTransitionState.Results)
            throw new InvalidOperationException($"Cannot continue from {_state}.");

        ulong generation = checked(++_generation);
        _state = DesktopTransitionState.PreparingContinuation;
        _scenePrepared = false;
        Log($"generation={generation} results -> preparing-continuation");
        _surface.ShowContinuationTransition(state);
        return generation;
    }

    public bool GameWindowPrepared(ulong generation)
    {
        ThrowIfClosed();
        if (!IsCurrentPreparation(generation) || _scenePrepared) return false;
        _scenePrepared = true;
        Log($"generation={generation} SDL mapped");
        _surface.ShowSceneForPreparation();
        return true;
    }

    public void UpdateLoading(ulong generation, MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfClosed();
        if (!IsCurrentPreparation(generation)) return;
        Log($"generation={generation} scene-load stage={state.Stage} map={state.Map ?? "unknown"}");
        if (_state == DesktopTransitionState.PreparingContinuation)
            _surface.UpdateContinuationTransition(state);
        else
            _surface.UpdateShellTransition(state);
    }

    public bool GameFirstFramePresented(ulong generation)
    {
        ThrowIfClosed();
        if (!IsCurrentPreparation(generation) || !_scenePrepared) return false;

        DesktopTransitionState source = _state;
        _state = DesktopTransitionState.Game;
        Log($"generation={generation} first-frame-presented");
        if (source == DesktopTransitionState.PreparingContinuation)
        {
            _surface.HideResults();
            if (!StillCurrent(DesktopTransitionState.Game, generation)) return false;
        }
        else
        {
            _surface.CloseShellTransition();
            if (!StillCurrent(DesktopTransitionState.Game, generation)) return false;
            _surface.HideShell();
            if (!StillCurrent(DesktopTransitionState.Game, generation)) return false;
        }
        _surface.ActivateScene();
        Log($"generation={generation} gameplay active");
        return true;
    }

    public bool FailLaunch(ulong generation, string? message)
    {
        ThrowIfClosed();
        if (!IsCurrentPreparation(generation)) return false;
        bool continuation = _state == DesktopTransitionState.PreparingContinuation;
        _state = DesktopTransitionState.Failed;
        string failure = String.IsNullOrWhiteSpace(message)
            ? "The arena could not be prepared." : message.Trim();
        Log($"generation={generation} launch failed continuation={continuation}");
        if (continuation)
        {
            _surface.ShowShellForPreparation();
            if (!StillCurrent(DesktopTransitionState.Failed, generation)) return false;
            _surface.PumpShell();
            if (!StillCurrent(DesktopTransitionState.Failed, generation)) return false;
        }
        _surface.FailShellTransition(failure);
        if (!StillCurrent(DesktopTransitionState.Failed, generation)) return false;
        _surface.PumpShell();
        if (!StillCurrent(DesktopTransitionState.Failed, generation)) return false;
        if (continuation)
        {
            _surface.HideResults();
            if (!StillCurrent(DesktopTransitionState.Failed, generation)) return false;
            _surface.HideScene();
            if (!StillCurrent(DesktopTransitionState.Failed, generation)) return false;
            _surface.ActivateShell();
        }
        return true;
    }

    public bool CompleteFailedReturn()
    {
        ThrowIfClosed();
        if (_state != DesktopTransitionState.Failed) return false;
        ulong generation = _generation;
        _state = DesktopTransitionState.Shell;
        Log($"generation={_generation} failed -> shell");
        _surface.CloseShellTransition();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.ShowShellForPreparation();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.PumpShell();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.HideResults();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.HideScene();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.ActivateShell();
        return true;
    }

    /// <summary>Shows and paints the target before hiding either source.</summary>
    public bool BeginReturnToShell()
    {
        ThrowIfClosed();
        if (_state is DesktopTransitionState.Shell or DesktopTransitionState.Failed
            or DesktopTransitionState.ReturningToShell)
            return false;

        ulong generation = _generation = checked(_generation + 1);
        _state = DesktopTransitionState.ReturningToShell;
        _scenePrepared = false;
        Log($"generation={_generation} returning-to-shell");
        _surface.ShowShellForPreparation();
        if (!StillCurrent(DesktopTransitionState.ReturningToShell, generation)) return false;
        _surface.PumpShell();
        if (!StillCurrent(DesktopTransitionState.ReturningToShell, generation)) return false;
        _state = DesktopTransitionState.Shell;
        _surface.HideResults();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.HideScene();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.CloseShellTransition();
        if (!StillCurrent(DesktopTransitionState.Shell, generation)) return false;
        _surface.ActivateShell();
        Log($"generation={_generation} shell ready");
        return true;
    }

    public void Close()
    {
        if (_disposed) return;
        _disposed = true;
        _state = DesktopTransitionState.Closing;
        _inputOwner.SetOwner(DesktopInputOwnerKind.None);
        Log($"generation={_generation} closing");
    }

    public void Dispose() => Close();

    private bool IsCurrentPreparation(ulong generation)
        => generation != 0 && generation == _generation
            && _state is DesktopTransitionState.PreparingGame
                or DesktopTransitionState.PreparingContinuation;

    private bool StillCurrent(DesktopTransitionState expected, ulong generation)
        => !_disposed && _state == expected && _generation == generation;

    private void ThrowIfClosed()
    {
        if (_disposed || _state == DesktopTransitionState.Closing)
            throw new ObjectDisposedException(nameof(DesktopTransitionCoordinator));
    }

    private void Log(string message)
        => _log($"elapsedMs={_clock.Elapsed.TotalMilliseconds:F1} {message}");
}

internal sealed class DesktopTransitionSurface : IDesktopTransitionSurface
{
    private readonly Func<HomeWindow?> _shell;
    private readonly Func<SdlGameHost?> _scene;
    private readonly Action _pump;
    private DesktopInputOwner? _inputOwner;

    public DesktopTransitionSurface(Func<HomeWindow?> shell,
        Func<SdlGameHost?> scene, Action pump,
        DesktopInputOwner? inputOwner = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _inputOwner = inputOwner;
    }

    internal void AttachInputOwner(DesktopInputOwner inputOwner)
    {
        ArgumentNullException.ThrowIfNull(inputOwner);
        _inputOwner = inputOwner;
    }

    public void ShowShellForPreparation() => RequireShell().ShowForPreparation();
    public void PumpShell() => _pump();
    public void HideShell() => RequireShell().HideForTransition();
    public void ActivateShell() => RequireShell().ActivateForTransition();
    public void ShowSceneForPreparation()
    {
        SdlGameHost scene = RequireScene();
        if (_inputOwner != null) scene.AttachInputOwner(_inputOwner);
        scene.ShowForPreparation();
    }
    public void HideScene() => _scene()?.Hide();
    public void ActivateScene() => RequireScene().Activate();
    public void ShowShellTransition(MatchTransitionState state)
        => RequireShell().ShowMatchTransition(state);
    public void UpdateShellTransition(MatchTransitionState state)
        => RequireShell().UpdateMatchTransition(state);
    public void FailShellTransition(string message)
        => RequireShell().FailMatchTransition(message);
    public void CloseShellTransition() => RequireShell().CloseMatchTransition();
    public void ShowContinuationTransition(MatchTransitionState state)
        => RequireShell().ShowContinuationTransition(state);
    public void UpdateContinuationTransition(MatchTransitionState state)
        => RequireShell().UpdateContinuationTransition(state);
    public void ShowResults() => RequireShell().ShowResultsForTransition();
    public void HideResults() => RequireShell().HideResultsForTransition();

    private HomeWindow RequireShell()
        => _shell() ?? throw new InvalidOperationException("The launcher shell is not available.");

    private SdlGameHost RequireScene()
        => _scene() ?? throw new InvalidOperationException("The SDL scene host is not available.");
}
