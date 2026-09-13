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
    ControlDisconnected,
    ResumingControl,
    AwaitingRejoinGrant,
    RejoiningWorker,
    AwaitingAuthoritativeSnapshot,
    Expired,
    Failed,

    // Source-compatible presentation aliases. The runtime itself uses the
    // canonical lifecycle names above.
    ConnectionLost = ControlDisconnected,
    Reconnecting = ResumingControl,
    RejoiningMatch = RejoiningWorker,
    AwaitingMatchSnapshot = AwaitingAuthoritativeSnapshot,
    SessionExpired = Expired
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
    public MatchLifecycleEpoch LifecycleEpoch { get; private set; }
    public HandoffGeneration HandoffGeneration { get; private set; }

    internal MatchClientContext(AuthoritativePlay play, Guid matchId,
        MatchLifecycleEpoch lifecycleEpoch = default,
        HandoffGeneration handoffGeneration = default)
    {
        Play = play ?? throw new ArgumentNullException(nameof(play));
        if (matchId == Guid.Empty) throw new ArgumentException("Match identity is required.", nameof(matchId));
        MatchId = matchId;
        LifecycleEpoch = lifecycleEpoch;
        HandoffGeneration = handoffGeneration;
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
        Task<RejoinCompletion>? idempotent = null;
        lock (_rejoinGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (handoff.MatchId != MatchId)
                throw new InvalidOperationException("A rejoin handoff cannot change MatchId.");
            if ((LifecycleEpoch.Value != 0 && handoff.LifecycleEpoch.Value != 0
                    && handoff.LifecycleEpoch.Value < LifecycleEpoch.Value)
                || (HandoffGeneration.Value != 0 && handoff.HandoffGeneration.Value != 0
                    && handoff.HandoffGeneration.Value < HandoffGeneration.Value))
                throw new InvalidOperationException("The rejoin handoff is stale.");
            if (HandoffGeneration.Value != 0
                && handoff.HandoffGeneration == HandoffGeneration)
            {
                RejoinRequest? existing = _queuedRejoin ?? _activeRejoin;
                if (existing?.Handoff.AdmissionId != handoff.AdmissionId)
                    throw new InvalidOperationException("The rejoin handoff conflicts with the current generation.");
                request = existing;
                idempotent = existing.Completion;
            }
            else
            {
                if (handoff.LifecycleEpoch.Value != 0)
                    LifecycleEpoch = handoff.LifecycleEpoch;
                if (handoff.HandoffGeneration.Value != 0)
                    HandoffGeneration = handoff.HandoffGeneration;
                request = new RejoinRequest(++_nextRejoinEpoch, handoff, cancellationToken);
                _queuedRejoin?.TrySetCanceled();
                _activeRejoin?.TrySetCanceled();
                _queuedRejoin = request;
            }
        }
        try
        {
            return await (idempotent ?? request.Completion).WaitAsync(cancellationToken)
                .ConfigureAwait(false);
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
        // The context is the lifetime fence for every gameplay completion. A
        // completion which was already queued must observe the disposed bit
        // before it can touch a later MatchId.
        Play.DetachOnlineContext(this);
        Play.Dispose();
    }
}

/// <summary>
/// Canonical owner for one online shell lifetime. NodeSessions and
/// AuthoritativePlay.Current remain compatibility facades while call sites
/// migrate to this explicit owner.
/// </summary>
public sealed class ClientOnlineRuntime : IDisposable, IAsyncDisposable
{
    private static ClientOnlineRuntime? _current;
    private static readonly object CurrentGate = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MatchClientContext? _match;
    private NodeControlClient? _node;
    private OnlineRecoveryState _recoveryState = OnlineRecoveryState.Connected;
    private int _disposed;
    private long _recoveryEpoch;
    private CancellationTokenSource? _recoveryOperation;
    private int _resumeAttempts;
    private DateTimeOffset _resumeNotBefore;

    // Online runtime ownership is no longer a rollout choice. Keeping this
    // property makes older callers source-compatible while making the
    // canonical owner unconditional.
    public static bool DefaultEnabled => true;
    public static ClientOnlineRuntime? Current => Volatile.Read(ref _current);
    public static ClientOnlineRuntime Ensure()
    {
        lock (CurrentGate)
            return Current ?? new ClientOnlineRuntime();
    }
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
        _ = enabled;
        Enabled = true;
        _node = NodeSessions.Current;
        if (Interlocked.CompareExchange(ref _current, this, null) != null)
            throw new InvalidOperationException("A client online runtime is already active.");
        NodeSessions.CurrentChanged += NodeChanged;
        if (_node != null) _node.Changed += NodeStateChanged;
    }

    internal static bool ParseEnabled(string? value) => true;

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
            NodeMatchHandoff? handoff = _node?.Handoff is { MatchId: var handoffMatchId } activeHandoff
                && handoffMatchId == matchId ? activeHandoff : null;
            next = new MatchClientContext(play, matchId,
                handoff?.LifecycleEpoch ?? default,
                handoff?.HandoffGeneration ?? default);
            _match = next;
            _recoveryEpoch++;
            RecoveryState = OnlineRecoveryState.Connected;
        }
        Changed?.Invoke();
        return next;
    }

    public void ReleaseMatch(AuthoritativePlay? expected = null, bool dispose = true)
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
        // Release always disposes the context. The bool remains accepted for
        // source compatibility with old callers, but a detached context must
        // never be left alive to complete work against a future match.
        _ = dispose;
        removed.Dispose();
        if (Volatile.Read(ref _disposed) == 0) Changed?.Invoke();
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
        SetRecovery(recoveryEpoch, OnlineRecoveryState.ResumingControl);
        try
        {
            int attempt = Interlocked.Increment(ref _resumeAttempts);
            TimeSpan delay = ReconnectDelay(attempt, Random.Shared.NextDouble());
            lock (_gate)
            {
                TimeSpan retryAfter = _resumeNotBefore - DateTimeOffset.UtcNow;
                if (retryAfter > delay) delay = retryAfter;
            }
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
            NodeControlClient node = await NodeSessions.ResumeAsync(linked.Token).ConfigureAwait(false);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            MatchClientContext? match = Match;
            if (!Enabled || match == null)
            {
                Interlocked.Exchange(ref _resumeAttempts, 0);
                SetRecovery(recoveryEpoch, OnlineRecoveryState.Connected);
                return node;
            }

            SetRecovery(recoveryEpoch, OnlineRecoveryState.AwaitingRejoinGrant);
            NodeControlEvent response = await node.SendAndWaitAsync("match.rejoin",
                new NodeMatchRejoin(match.MatchId), linked.Token).ConfigureAwait(false);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            if (response.Type == "error")
            {
                NodeControlError error = response.Payload.Deserialize(
                    NodeJsonContext.Default.NodeControlError)
                    ?? new NodeControlError("unknown", "The multiplayer recovery request failed.");
                MatchControlFailure failure = MatchControlFailureCodes.Parse(error.Code);
                SetRecovery(recoveryEpoch, failure is MatchControlFailure.SessionExpired
                        or MatchControlFailure.NotFound or MatchControlFailure.NotActive
                    ? OnlineRecoveryState.Expired : OnlineRecoveryState.Failed);
                throw new MatchControlException(failure, error.Message);
            }
            if (response.Type != "match.handoff")
                throw new InvalidOperationException("The Node returned an unexpected rejoin response.");
            NodeMatchHandoff handoff = response.Payload.Deserialize(NodeJsonContext.Default.NodeMatchHandoff)
                ?? throw new InvalidOperationException("The Node returned an invalid rejoin handoff.");
            SetRecovery(recoveryEpoch, OnlineRecoveryState.RejoiningWorker);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            SetRecovery(recoveryEpoch, OnlineRecoveryState.AwaitingAuthoritativeSnapshot);
            RejoinCompletion completion = await match.QueueRejoinAsync(handoff, linked.Token).ConfigureAwait(false);
            EnsureRecoveryCurrent(recoveryEpoch, linked.Token);
            if (!ReferenceEquals(Match, match)) throw new OperationCanceledException(linked.Token);
            if (completion.ConnectionId == 0 || !match.Play.IsObserver && completion.InputEpoch == 0)
                throw new InvalidOperationException("The Worker rejoin did not establish a valid input epoch.");
            node.MarkGameplayJoined(match.MatchId);
            Interlocked.Exchange(ref _resumeAttempts, 0);
            lock (_gate) _resumeNotBefore = default;
            SetRecovery(recoveryEpoch, OnlineRecoveryState.Connected);
            return node;
        }
        catch (OperationCanceledException) when (!IsRecoveryCurrent(recoveryEpoch)
            && !cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NodeControlConnectException error)
        {
            if (error.Failure == NodeControlConnectFailure.RateLimited
                && error.RetryAfter is { } retryAfter)
                lock (_gate)
                    _resumeNotBefore = DateTimeOffset.UtcNow + retryAfter;
            if (IsRecoveryCurrent(recoveryEpoch))
                SetRecovery(recoveryEpoch,
                    RecoveryStateForConnectFailure(error.Failure));
            throw;
        }
        catch when (RecoveryState is not (OnlineRecoveryState.Expired or OnlineRecoveryState.Failed))
        {
            if (IsRecoveryCurrent(recoveryEpoch))
                SetRecovery(recoveryEpoch, OnlineRecoveryState.ControlDisconnected);
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

    internal static TimeSpan ReconnectDelay(int attempt, double jitter)
    {
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
        if (!double.IsFinite(jitter) || jitter is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(jitter));
        if (attempt == 1) return TimeSpan.Zero;
        double minimumMilliseconds = attempt switch
        {
            2 => 250,
            3 => 500,
            4 => 1_000,
            _ => Math.Min(5_000, 2_000 * Math.Pow(2, attempt - 5))
        };
        double maximumMilliseconds = Math.Min(5_000, minimumMilliseconds * 2);
        return TimeSpan.FromMilliseconds(minimumMilliseconds
            + (maximumMilliseconds - minimumMilliseconds) * jitter);
    }

    internal static OnlineRecoveryState RecoveryStateForConnectFailure(
        NodeControlConnectFailure failure)
        => failure == NodeControlConnectFailure.AuthenticationRejected
            ? OnlineRecoveryState.Expired
            : OnlineRecoveryState.ControlDisconnected;

    private void NodeChanged(NodeControlClient? node)
    {
        NodeControlClient? previous;
        lock (_gate)
        {
            if (_disposed != 0)
            {
                if (node != null) node.Changed -= NodeStateChanged;
                return;
            }
            previous = _node;
            _node = node;
            if (previous != null) previous.Changed -= NodeStateChanged;
            if (node != null) node.Changed += NodeStateChanged;
        }
        if (Volatile.Read(ref _disposed) != 0) return;
        if (node is { Connected: false }) SetRecovery(OnlineRecoveryState.ControlDisconnected);
        else Changed?.Invoke();
    }

    private void NodeStateChanged()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        NodeControlClient? node = Node;
        if (node is { Connected: false }) SetRecovery(OnlineRecoveryState.ControlDisconnected);
        else Changed?.Invoke();
    }

    private void SetRecovery(OnlineRecoveryState state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
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
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _recoveryEpoch);
        NodeSessions.CurrentChanged -= NodeChanged;
        NodeControlClient? node;
        lock (_gate)
        {
            node = _node;
            _node = null;
        }
        if (node != null) node.Changed -= NodeStateChanged;
        Interlocked.CompareExchange(ref _current, null, this);
        _lifetime.Cancel();
        ReleaseMatch(dispose: true);
        _lifetime.Dispose();
        Changed = null;
    }

    public void Dispose() => DisposeCore();
}
