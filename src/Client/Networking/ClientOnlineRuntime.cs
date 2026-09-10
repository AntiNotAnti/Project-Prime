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
    SessionExpired
}

/// <summary>Per-Worker-session resources. No object here survives into a later MatchId.</summary>
public sealed class MatchClientContext : IDisposable
{
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
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) Play.Dispose();
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
    private int _disposed;

    public static bool DefaultEnabled => ParseEnabled(
        Environment.GetEnvironmentVariable("PROJECT_PRIME_ONLINE_RUNTIME_V2"));
    public static ClientOnlineRuntime? Current => Volatile.Read(ref _current);
    public bool Enabled { get; }
    public ClientSessionCoordinator Flow { get; } = new();
    public NodeControlClient? Node { get { lock (_gate) return _node; } }
    public MatchClientContext? Match { get { lock (_gate) return _match; } }
    public OnlineRecoveryState RecoveryState { get; private set; } = OnlineRecoveryState.Connected;
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
            RecoveryState = OnlineRecoveryState.Connected;
        }
        Changed?.Invoke();
        return next;
    }

    public void ReleaseMatch(AuthoritativePlay? expected = null, bool dispose = false)
    {
        MatchClientContext? removed;
        lock (_gate)
        {
            if (_match == null || expected != null && !ReferenceEquals(_match.Play, expected)) return;
            removed = _match;
            _match = null;
        }
        if (dispose) removed.Dispose();
        Changed?.Invoke();
    }

    public async Task<NodeControlClient> ResumeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        SetRecovery(OnlineRecoveryState.Reconnecting);
        try
        {
            NodeControlClient node = await NodeSessions.ResumeAsync(linked.Token).ConfigureAwait(false);
            MatchClientContext? match = Match;
            if (!Enabled || match == null)
            {
                SetRecovery(OnlineRecoveryState.Connected);
                return node;
            }

            SetRecovery(OnlineRecoveryState.RejoiningMatch);
            NodeControlEvent response = await node.SendAndWaitAsync("match.rejoin",
                new NodeMatchRejoin(match.MatchId), linked.Token).ConfigureAwait(false);
            if (response.Type == "error")
            {
                SetRecovery(OnlineRecoveryState.SessionExpired);
                throw new InvalidOperationException(response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Message
                    ?? "The multiplayer session expired.");
            }
            if (response.Type != "match.handoff")
                throw new InvalidOperationException("The Node returned an unexpected rejoin response.");
            NodeMatchHandoff handoff = response.Payload.Deserialize(NodeJsonContext.Default.NodeMatchHandoff)
                ?? throw new InvalidOperationException("The Node returned an invalid rejoin handoff.");
            await Task.Run(() => match.Play.Rejoin(handoff, linked.Token), linked.Token).ConfigureAwait(false);
            node.MarkGameplayJoined(match.MatchId);
            SetRecovery(OnlineRecoveryState.Connected);
            return node;
        }
        catch when (RecoveryState != OnlineRecoveryState.SessionExpired)
        {
            SetRecovery(OnlineRecoveryState.ConnectionLost);
            throw;
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

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        NodeSessions.CurrentChanged -= NodeChanged;
        if (Node is { } node) node.Changed -= NodeStateChanged;
        Interlocked.CompareExchange(ref _current, null, this);
        _lifetime.Cancel();
        ReleaseMatch(dispose: true);
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }
}
