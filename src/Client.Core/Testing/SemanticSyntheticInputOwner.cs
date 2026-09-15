using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Testing;

public enum SemanticSyntheticInputSubmission
{
    Accepted,
    QueueFull,
    StaleIdentity,
    Canceled,
    InvalidArguments,
    Unavailable
}

public readonly record struct SemanticSyntheticInputScope(
    string MatchId,
    string PhaseId,
    ulong ConnectionId,
    uint Life,
    bool Connected,
    bool MatchPlaying,
    bool SceneOwned,
    bool Paused,
    bool Replay,
    bool FrameAdvance)
{
    public bool Eligible => Connected && MatchPlaying && SceneOwned
        && !Paused && !Replay && !FrameAdvance;
}

public readonly record struct SemanticSyntheticInputFrame(
    bool MoveLeft,
    bool MoveRight,
    bool MoveUp,
    bool MoveDown,
    bool Fire)
{
    public bool Active => MoveLeft || MoveRight || MoveUp || MoveDown || Fire;
}

/// <summary>
/// Bounded, match-scoped synthetic input owner for the development E2E path.
/// Commands are drained by the shipping SDL host and expire by monotonic host
/// time. Identity, connection, life, phase, pause and scene ownership changes
/// clear every queued and active input.
/// </summary>
public sealed class SemanticSyntheticInputOwner : IDisposable
{
    public const int DefaultDurationMilliseconds = 100;
    public const int MaximumDurationMilliseconds = 5_000;
    public const int MaximumQueuedCommands = 64;

    private readonly object _gate = new();
    private readonly Queue<Command> _commands = new(MaximumQueuedCommands);
    private SemanticControlIdentity _identity;
    private SemanticSyntheticInputScope? _scope;
    private bool _hostAttached;
    private bool _shutdown;
    private double _moveX;
    private double _moveY;
    private long _movementExpiresAt;
    private bool _fire;
    private long _fireExpiresAt;

    public SemanticSyntheticInputOwner(SemanticControlIdentity identity)
    {
        identity.Validate();
        _identity = identity;
    }

    public SemanticControlIdentity Identity
    {
        get { lock (_gate) return _identity; }
    }

    public bool IsHostAttached
    {
        get { lock (_gate) return _hostAttached && !_shutdown; }
    }

    public bool AttachHost()
    {
        lock (_gate)
        {
            if (_shutdown) return false;
            _hostAttached = true;
            return true;
        }
    }

    public void DetachHost()
    {
        lock (_gate)
        {
            _hostAttached = false;
            ClearLocked();
        }
    }

    public void AdvanceIdentity(SemanticControlIdentity identity)
    {
        identity.Validate();
        lock (_gate)
        {
            if (_identity == identity) return;
            _identity = identity;
            ClearLocked();
        }
    }

    public SemanticSyntheticInputSubmission SubmitMovement(
        SemanticControlIdentity identity, double x, double y, int durationMilliseconds,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)
            || x is < -1 or > 1 || y is < -1 or > 1
            || durationMilliseconds is < 1 or > MaximumDurationMilliseconds)
            return SemanticSyntheticInputSubmission.InvalidArguments;
        return Submit(new Command(CommandKind.Movement, x, y, false,
            durationMilliseconds), identity, cancellationToken);
    }

    public SemanticSyntheticInputSubmission SubmitFire(
        SemanticControlIdentity identity, bool pressed, int durationMilliseconds,
        CancellationToken cancellationToken)
    {
        if (durationMilliseconds is < 1 or > MaximumDurationMilliseconds)
            return SemanticSyntheticInputSubmission.InvalidArguments;
        return Submit(new Command(CommandKind.Fire, 0, 0, pressed,
            durationMilliseconds), identity, cancellationToken);
    }

    private SemanticSyntheticInputSubmission Submit(Command command,
        SemanticControlIdentity identity, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return SemanticSyntheticInputSubmission.Canceled;
        lock (_gate)
        {
            if (_shutdown || !_hostAttached)
                return SemanticSyntheticInputSubmission.Unavailable;
            if (_identity != identity)
                return SemanticSyntheticInputSubmission.StaleIdentity;
            if (_commands.Count >= MaximumQueuedCommands)
                return SemanticSyntheticInputSubmission.QueueFull;
            _commands.Enqueue(command);
            return SemanticSyntheticInputSubmission.Accepted;
        }
    }

    public SemanticSyntheticInputFrame Drain(
        SemanticSyntheticInputScope scope, long nowMilliseconds)
    {
        lock (_gate)
        {
            if (_shutdown || !_hostAttached || !ScopeMatchesIdentity(scope)
                || !scope.Eligible)
            {
                ClearLocked();
                _scope = scope;
                return default;
            }
            if (_scope is SemanticSyntheticInputScope previous && previous != scope)
                ClearLocked();
            _scope = scope;

            while (_commands.TryDequeue(out Command command))
            {
                long expiresAt = SaturatingAdd(nowMilliseconds,
                    command.DurationMilliseconds);
                if (command.Kind == CommandKind.Movement)
                {
                    _moveX = command.X;
                    _moveY = command.Y;
                    _movementExpiresAt = expiresAt;
                }
                else
                {
                    _fire = command.Pressed;
                    _fireExpiresAt = command.Pressed ? expiresAt : nowMilliseconds;
                }
            }
            if (nowMilliseconds >= _movementExpiresAt)
            {
                _moveX = 0;
                _moveY = 0;
            }
            if (nowMilliseconds >= _fireExpiresAt) _fire = false;
            return new SemanticSyntheticInputFrame(
                MoveLeft: _moveX < 0,
                MoveRight: _moveX > 0,
                MoveUp: _moveY > 0,
                MoveDown: _moveY < 0,
                Fire: _fire);
        }
    }

    public void Clear()
    {
        lock (_gate) ClearLocked();
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            _shutdown = true;
            _hostAttached = false;
            ClearLocked();
        }
    }

    public void Dispose() => Shutdown();

    private bool ScopeMatchesIdentity(SemanticSyntheticInputScope scope)
        => StringComparer.Ordinal.Equals(scope.MatchId, _identity.MatchId)
            && StringComparer.Ordinal.Equals(scope.PhaseId, _identity.PhaseId);

    private void ClearLocked()
    {
        _commands.Clear();
        _scope = null;
        _moveX = 0;
        _moveY = 0;
        _movementExpiresAt = 0;
        _fire = false;
        _fireExpiresAt = 0;
    }

    private static long SaturatingAdd(long value, int increment)
        => value > long.MaxValue - increment ? long.MaxValue : value + increment;

    private enum CommandKind { Movement, Fire }

    private readonly record struct Command(CommandKind Kind, double X, double Y,
        bool Pressed, int DurationMilliseconds);
}
