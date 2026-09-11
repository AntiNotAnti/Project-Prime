using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Network;

public enum OnlineRecoveryState
{
    Connected,
    ConnectionLost,
    Reconnecting,
    RejoiningMatch,
    AwaitingMatchSnapshot,
    SessionExpired
}

internal readonly record struct RejoinCompletion(ulong ConnectionId, uint InputEpoch);

internal readonly record struct OnlineRuntimeSnapshot(NodeControlClient? Node,
    MatchClientContext? Match, OnlineRecoveryState RecoveryState,
    ClientSessionPhase SessionPhase);

/// <summary>
/// One bounded, epoch-fenced request owned by a MatchClientContext. The
/// request is completed by the gameplay owner; no transport work runs here.
/// </summary>
internal sealed class RejoinRequest
{
    private readonly TaskCompletionSource<RejoinCompletion> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal RejoinRequest(long epoch, NodeMatchHandoff handoff, CancellationToken cancellationToken)
    {
        Epoch = epoch;
        Handoff = handoff;
        CancellationToken = cancellationToken;
    }

    internal long Epoch { get; }
    internal NodeMatchHandoff Handoff { get; }
    internal CancellationToken CancellationToken { get; }
    internal Task<RejoinCompletion> Completion => _completion.Task;
    internal bool IsSettled => _completion.Task.IsCompleted;
    internal bool TrySetResult(RejoinCompletion result) => _completion.TrySetResult(result);
    internal bool TrySetException(Exception error) => _completion.TrySetException(error);
    internal bool TrySetCanceled() => _completion.TrySetCanceled();
}

/// <summary>Per-Worker-session resources. No object here survives into a later MatchId.</summary>
public sealed class MatchClientContext : IDisposable
{
    private readonly object _rejoinGate = new();
    private long _nextRejoinEpoch;
    private RejoinRequest? _queuedRejoin;
    private RejoinRequest? _activeRejoin;
    private int _disposed;
    public AuthoritativePlay Play { get; }
    public NetClient Client => Play.Client;
    public ClientPrediction Prediction => Play.Prediction;
    public Guid MatchId { get; }

    internal MatchClientContext(AuthoritativePlay play, Guid matchId)
    {
        Play = play ?? throw new ArgumentNullException(nameof(play));
        if (matchId == Guid.Empty) throw new ArgumentException("Match identity is required.", nameof(matchId));
        MatchId = matchId;
        play.AttachOnlineContext(this);
    }

    /// <summary>
    /// Queue the newest recovery handoff. A reconnect storm never grows a
    /// queue: a pending or active older request is settled as stale and the
    /// gameplay owner starts only the newest request.
    /// </summary>
    internal async Task<RejoinCompletion> QueueRejoinAsync(NodeMatchHandoff handoff,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejoinRequest request;
        lock (_rejoinGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            request = new RejoinRequest(++_nextRejoinEpoch, handoff, cancellationToken);
            _queuedRejoin?.TrySetCanceled();
            _activeRejoin?.TrySetCanceled();
            _queuedRejoin = request;
        }
        try
        {
            return await request.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested) CancelRejoin(request);
        }
    }

    internal bool TryTakeRejoin(out RejoinRequest request)
    {
        lock (_rejoinGate)
        {
            if (_disposed != 0 || _activeRejoin != null || _queuedRejoin is not { } queued)
            {
                request = null!;
                return false;
            }
            _queuedRejoin = null;
            _activeRejoin = request = queued;
            return true;
        }
    }

    internal bool IsCurrentRejoin(RejoinRequest request)
    {
        lock (_rejoinGate)
            return _disposed == 0 && _activeRejoin is { } active
                && active.Epoch == request.Epoch && ReferenceEquals(active, request)
                && !request.IsSettled;
    }

    internal void CompleteRejoin(RejoinRequest request, RejoinCompletion completion)
    {
        lock (_rejoinGate)
        {
            if (!ReferenceEquals(_activeRejoin, request)) return;
            _activeRejoin = null;
            request.TrySetResult(completion);
        }
    }

    internal void FailRejoin(RejoinRequest request, Exception error)
    {
        lock (_rejoinGate)
        {
            if (!ReferenceEquals(_activeRejoin, request)) return;
            _activeRejoin = null;
            request.TrySetException(error);
        }
    }

    internal void AbandonRejoin(RejoinRequest request)
    {
        lock (_rejoinGate)
        {
            if (ReferenceEquals(_activeRejoin, request)) _activeRejoin = null;
        }
    }

    private void CancelRejoin(RejoinRequest request)
    {
        lock (_rejoinGate)
        {
            if (ReferenceEquals(_queuedRejoin, request)) _queuedRejoin = null;
            request.TrySetCanceled();
        }
    }

    internal void CancelPendingRejoin()
    {
        lock (_rejoinGate)
        {
            _queuedRejoin?.TrySetCanceled();
            _activeRejoin?.TrySetCanceled();
            _queuedRejoin = null;
            _activeRejoin = null;
        }
    }

    internal bool Owns(AuthoritativePlay play)
        => ReferenceEquals(Play, play) && Volatile.Read(ref _disposed) == 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelPendingRejoin();
        Play.DetachOnlineContext(this);
        Play.Dispose();
    }
}

/// <summary>
/// Canonical owner for one online shell lifetime. NodeSessions and
/// AuthoritativePlay.Current remain compatibility facades while call sites
/// migrate to this explicit owner.
/// </summary>
public sealed class ClientOnlineRuntime : IAsyncDisposable
{
    private static ClientOnlineRuntime? _current;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MatchClientContext? _match;
    private NodeControlClient? _node;
    private OnlineRecoveryState _recoveryState = OnlineRecoveryState.Connected;
    private int _disposed;
    private long _recoveryEpoch;
    private CancellationTokenSource? _recoveryOperation;

    public static bool DefaultEnabled => ParseEnabled(
        Environment.GetEnvironmentVariable("PROJECT_PRIME_ONLINE_RUNTIME_V2"));
    public static ClientOnlineRuntime? Current => Volatile.Read(ref _current);
    public bool Enabled { get; }
    public ClientSessionCoordinator Flow { get; } = new();
    public NodeControlClient? Node { get { lock (_gate) return _node; } }
    public MatchClientContext? Match { get { lock (_gate) return _match; } }
    public OnlineRecoveryState RecoveryState
    {
        get { lock (_gate) return _recoveryState; }
        private set { lock (_gate) _recoveryState = value; }
    }
    internal OnlineRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
            return new(_node, _match, _recoveryState, Flow.Phase);
    }
    public CancellationToken Lifetime => _lifetime.Token;
    public event Action? Changed;

    public ClientOnlineRuntime(bool? enabled = null)
    {
        Enabled = enabled ?? DefaultEnabled;
        _node = NodeSessions.Current;
        if (Enabled && Interlocked.CompareExchange(ref _current, this, null) != null)
            throw new InvalidOperationException("A client online runtime is already active.");
        NodeSessions.CurrentChanged += NodeChanged;
        if (_node != null) _node.Changed += NodeStateChanged;
    }

    internal static bool ParseEnabled(string? value)
        => value == null || value.Trim() switch
        {
            "0" or "false" or "False" or "FALSE" or "off" or "Off" or "OFF" => false,
            _ => true
        };

    public MatchClientContext? AdoptMatch(AuthoritativePlay play, Guid matchId)
    {
        if (!Enabled) return null;
        ArgumentNullException.ThrowIfNull(play);
        MatchClientContext next;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_match is { } current)
            {
                if (!ReferenceEquals(current.Play, play) || current.MatchId != matchId)
                    throw new InvalidOperationException("An online match context is already active.");
                return current;
            }
            next = new MatchClientContext(play, matchId);
            _match = next;
            _recoveryEpoch++;
            RecoveryState = OnlineRecoveryState.Connected;
        }
        Changed?.Invoke();
        return next;
    }

    public void ReleaseMatch(AuthoritativePlay? expected = null, bool dispose = false)
    {
        MatchClientContext? removed;
        CancellationTokenSource? recovery;
        lock (_gate)
        {
            if (_match == null || expected != null && !ReferenceEquals(_match.Play, expected)) return;
            removed = _match;
            _match = null;
            _recoveryEpoch++;
            recovery = _recoveryOperation;
        }
        CancelRecoveryOperation(recovery);
        removed.Play.DetachOnlineContext(removed);
        removed.CancelPendingRejoin();
        if (dispose) removed.Dispose();
        Changed?.Invoke();
    }

    public async Task<NodeControlClient> ResumeAsync(CancellationToken cancellationToken = default)
    {
        long recoveryEpoch;
        CancellationTokenSource linked;
        CancellationTokenSource? priorOperation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            recoveryEpoch = ++_recoveryEpoch;
            priorOperation = _recoveryOperation;
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            _recoveryOperation = linked;
        }
        CancelRecoveryOperation(priorOperation);
        SetRecovery(recoveryEpoch, OnlineRecoveryState.Reconnecting);
        try
        {
            NodeControlClient node = await NodeSessions.ResumeAsync(linked.Token).ConfigureAwait(false);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            MatchClientContext? match = Match;
            if (!Enabled || match == null)
            {
                SetRecovery(recoveryEpoch, OnlineRecoveryState.Connected);
                return node;
            }

            SetRecovery(recoveryEpoch, OnlineRecoveryState.RejoiningMatch);
            NodeControlEvent response = await node.SendAndWaitAsync("match.rejoin",
                new NodeMatchRejoin(match.MatchId), linked.Token).ConfigureAwait(false);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            if (response.Type == "error")
            {
                SetRecovery(recoveryEpoch, OnlineRecoveryState.SessionExpired);
                throw new InvalidOperationException(response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Message
                    ?? "The multiplayer session expired.");
            }
            if (response.Type != "match.handoff")
                throw new InvalidOperationException("The Node returned an unexpected rejoin response.");
            NodeMatchHandoff handoff = response.Payload.Deserialize(NodeJsonContext.Default.NodeMatchHandoff)
                ?? throw new InvalidOperationException("The Node returned an invalid rejoin handoff.");
            SetRecovery(recoveryEpoch, OnlineRecoveryState.AwaitingMatchSnapshot);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            RejoinCompletion completion = await match.QueueRejoinAsync(handoff, linked.Token).ConfigureAwait(false);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            if (!ReferenceEquals(Match, match)) throw new OperationCanceledException(linked.Token);
            if (completion.ConnectionId == 0 || !match.Play.IsObserver && completion.InputEpoch == 0)
                throw new InvalidOperationException("The Worker rejoin did not establish a valid input epoch.");
            node.MarkGameplayJoined(match.MatchId);
            SetRecovery(recoveryEpoch, OnlineRecoveryState.Connected);
            return node;
        }
        catch (OperationCanceledException) when (!IsRecoveryCurrent(recoveryEpoch)
            && !cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch when (RecoveryState != OnlineRecoveryState.SessionExpired)
        {
            if (IsRecoveryCurrent(recoveryEpoch)) SetRecovery(recoveryEpoch, OnlineRecoveryState.ConnectionLost);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_recoveryOperation, linked)) _recoveryOperation = null;
            }
            linked.Dispose();
        }
    }

    private void NodeChanged(NodeControlClient? node)
    {
        NodeControlClient? previous;
        lock (_gate) { previous = _node; _node = node; }
        if (previous != null) previous.Changed -= NodeStateChanged;
        if (node != null) node.Changed += NodeStateChanged;
        if (node is { Connected: false }) SetRecovery(OnlineRecoveryState.ConnectionLost);
        else Changed?.Invoke();
    }

    private void NodeStateChanged()
    {
        NodeControlClient? node = Node;
        if (node is { Connected: false }) SetRecovery(OnlineRecoveryState.ConnectionLost);
        else Changed?.Invoke();
    }

    private void SetRecovery(OnlineRecoveryState state)
    {
        RecoveryState = state;
        Changed?.Invoke();
    }

    private void SetRecovery(long epoch, OnlineRecoveryState state)
    {
        if (!IsRecoveryCurrent(epoch)) return;
        SetRecovery(state);
    }

    private bool IsRecoveryCurrent(long epoch)
        => Volatile.Read(ref _recoveryEpoch) == epoch && Volatile.Read(ref _disposed) == 0;

    private static void CancelRecoveryOperation(CancellationTokenSource? operation)
    {
        try { operation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void EnsureRecoveryCurrent(long epoch, CancellationToken cancellationToken)
    {
        if (!IsRecoveryCurrent(epoch)) throw new OperationCanceledException(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        Interlocked.Increment(ref _recoveryEpoch);
        NodeSessions.CurrentChanged -= NodeChanged;
        if (Node is { } node) node.Changed -= NodeStateChanged;
        Interlocked.CompareExchange(ref _current, null, this);
        _lifetime.Cancel();
        ReleaseMatch(dispose: true);
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }
}
