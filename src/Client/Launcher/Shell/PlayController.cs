using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using FruityPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.Gui;

public enum PlayPhase
{
    Nodes,
    LoadingNodes,
    Connected,
    Lobby,
    Handoff,
    Error
}

public enum NodeMapCatalogState
{
    None,
    Unknown,
    Empty,
    Disjoint,
    Available
}

public sealed record PlayState(PlayPhase Phase, IReadOnlyList<NodeListing> Nodes,
    NodeControlClient.ViewState? Node, Hunter LobbyHunter, string Message,
    bool Loading, long Revision)
{
    public static PlayState Initial => new(PlayPhase.Nodes, Array.Empty<NodeListing>(),
        null, Hunter.Samus, "Choose a compatible Node.", false, 0);

    public LobbySnapshot? Lobby => Node?.Lobby;
    public LobbyListSnapshot? Lobbies => Node?.Lobbies;
    public NodeMatchHandoff? Handoff => Node?.Handoff;
}

public readonly record struct PlayHandoffKey(Guid NodeId, Guid MatchId, ulong Nonce,
    long Generation);

/// <summary>
/// One tracked Worker handoff. A completed operation can produce one launch
/// only; canceling or replacing it invalidates its generation.
/// </summary>
public sealed class PlayHandoffGate
{
    private readonly object _gate = new();
    private PlayHandoffKey? _current;
    private bool _completed;

    public PlayHandoffKey? Current { get { lock (_gate) return _current; } }
    public bool IsActive { get { lock (_gate) return _current.HasValue && !_completed; } }

    public bool TryBegin(PlayHandoffKey key)
    {
        if (key.NodeId == Guid.Empty || key.MatchId == Guid.Empty || key.Nonce == 0)
            throw new ArgumentException("A handoff requires Node, match, and nonce identities.", nameof(key));
        lock (_gate)
        {
            if (_current is { } current)
            {
                // Repeated Node events for one handoff are expected and are
                // not a second operation. Once that operation completed, a
                // different authoritative nonce is a new handoff (rejoin or
                // rematch) and may take ownership of the gate.
                if (!_completed || current == key) return false;
            }
            _current = key;
            _completed = false;
            return true;
        }
    }

    public bool TryComplete(PlayHandoffKey key)
    {
        lock (_gate)
        {
            if (_current != key || _completed) return false;
            _completed = true;
            return true;
        }
    }

    public bool IsCurrent(PlayHandoffKey key)
    {
        lock (_gate) return _current == key && !_completed;
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _current = null;
            _completed = false;
        }
    }

    public void Cancel(PlayHandoffKey key)
    {
        lock (_gate)
        {
            if (_current == key)
            {
                _current = null;
                _completed = false;
            }
        }
    }
}

/// <summary>Node/lobby adapter for the persistent NodeSessions transport.</summary>
public sealed class PlayController : IAsyncDisposable
{
    private readonly record struct MapCatalogSnapshot(NodeMapCatalogState State, string[] Available);
    private readonly PrimeShellState _shell;
    private string[] _maps;
    private readonly Func<CancellationToken, Task<AccountSession?>> _accountResolver;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _configureOperation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PlayHandoffGate _handoff = new();
    private readonly object _handoffTaskLock = new();
    private readonly object _stateLock = new();
    private readonly object _mapCatalogLock = new();
    private NodeControlClient? _observed;
    private bool _hasSelectedNode;
    private string[]? _hostedMapKeys;
    private string _connectedNodeName = "";
    private string _connectedNodeRegion = "";
    private PlayState _state = PlayState.Initial;
    private long _generation;
    private int _handoffEnabled;
    private Task<bool>? _handoffTask;
    private int _disposed;

    public PlayController(PrimeShellState shell, IReadOnlyList<string>? maps = null,
        Func<CancellationToken, Task<AccountSession?>>? accountResolver = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _maps = maps?.Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        _accountResolver = accountResolver ?? ResolveAccountAsync;
        NodeSessions.CurrentChanged += NodeSessionChanged;
        if (NodeSessions.Current is { } current)
            Observe(current);
    }

    public PlayState State { get { lock (_stateLock) return _state; } }
    public NodeMapCatalogState MapCatalogState => GetMapCatalogSnapshot().State;
    public IReadOnlyList<string> AvailableMaps => GetMapCatalogSnapshot().Available;
    public string MapCatalogMessage => GetMapCatalogMessage(MapCatalogState);
    public PlayHandoffGate HandoffGate => _handoff;
    public event EventHandler? Changed;
    public event EventHandler<LaunchPlan>? Launch;

    public void SetMaps(IEnumerable<string> maps)
    {
        Volatile.Write(ref _maps, maps?.Distinct(StringComparer.Ordinal).ToArray()
            ?? Array.Empty<string>());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Supplies deterministic Node map data to the offline UI capture only.</summary>
    internal void SetCaptureMapCatalog(IEnumerable<string> hostedMapKeys)
    {
        lock (_mapCatalogLock)
        {
            _hasSelectedNode = true;
            _hostedMapKeys = hostedMapKeys?.Distinct(StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>();
        }
    }

    public void SetHandoffEnabled(bool enabled)
    {
        Volatile.Write(ref _handoffEnabled, enabled ? 1 : 0);
        if (!enabled) CancelPendingHandoff();
    }

    public async Task RefreshNodesAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (NodeSessions.Current is { Connected: true } connected)
            {
                Observe(connected);
                await connected.SendAsync("lobby.list", new LobbyList(), cancellationToken)
                    .ConfigureAwait(false);
                Publish(State with { Phase = PlayPhase.Connected, Loading = false,
                    Message = "Connected to Node." });
                return;
            }
            Publish(State with { Phase = PlayPhase.LoadingNodes, Loading = true,
                Message = "Finding compatible Nodes…" });
            AccountSession account = await RequireAccountAsync(cancellationToken).ConfigureAwait(false);
            (IReadOnlyList<string> failures, (string Version, string ContentHash) identity) prepared =
                await Task.Run(() =>
                {
                    IReadOnlyList<string> failures = MapPreparation.GenerateMissing();
                    return (failures, ContentEnvironment.GetContentIdentity());
                }, cancellationToken).ConfigureAwait(false);
            if (prepared.failures.Count != 0)
                throw new InvalidOperationException("Custom map preparation failed: " + prepared.failures[0]);
            NodeListing[] nodes = await account.GetNodesAsync(NetHeader.Version, BuildVersion.Display,
                prepared.identity.ContentHash, cancellationToken).ConfigureAwait(false);
            Publish(new PlayState(PlayPhase.Nodes, nodes, null, State.LobbyHunter,
                nodes.Length == 0 ? "No compatible Nodes are online." : "Choose a Node.", false,
                State.Revision + 1));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = error.Message });
            _shell.Notify(PrimeNotificationKind.Error, error.Message);
        }
        finally { _operation.Release(); }
    }

    public async Task<bool> ConnectNodeAsync(NodeListing node,
        CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            // A directory selection replaces the current control session. An
            // older Worker handoff must be invalidated before the new Node is
            // observed, otherwise its completion can occupy the gate and race
            // the replacement session.
            Interlocked.Increment(ref _generation);
            _handoff.Cancel();
            ClearSelectedNodeCatalog();
            NetSession.Stop();
            AccountSession account = await RequireAccountAsync(cancellationToken).ConfigureAwait(false);
            Publish(State with { Phase = PlayPhase.LoadingNodes, Loading = true,
                Message = $"Connecting to {node.Name}…" });
            NodeControlClient connected = await NodeSessions.ConnectAsync(account, node,
                cancellationToken).ConfigureAwait(false);
            Observe(connected);
            _connectedNodeName = node.Name;
            _connectedNodeRegion = node.Region;
            _shell.SetNodeStatus(true, node.Name, node.Region);
            await connected.SendAsync("lobby.list", new LobbyList(), cancellationToken)
                .ConfigureAwait(false);
            Publish(State with { Phase = PlayPhase.Connected, Loading = false,
                Message = $"Connected to {node.Name}." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = error.Message });
            _shell.Notify(PrimeNotificationKind.Error, error.Message);
            return false;
        }
        finally { _operation.Release(); }
    }

    public async Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _generation);
            _handoff.Cancel();
            NetSession.Stop();
            NodeControlClient session = await NodeSessions.ResumeAsync(cancellationToken)
                .ConfigureAwait(false);
            Observe(session);
            _shell.SetNodeStatus(true, _connectedNodeName, _connectedNodeRegion);
            await session.SendAsync("lobby.list", new LobbyList(), cancellationToken)
                .ConfigureAwait(false);
            Publish(State with { Phase = PlayPhase.Connected, Loading = false,
                Message = "Node session resumed." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = error.Message });
            return false;
        }
        finally { _operation.Release(); }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        _generation++;
        _handoff.Cancel();
        ClearSelectedNodeCatalog();
        _connectedNodeName = "";
        _connectedNodeRegion = "";
        await NodeSessions.DisconnectAsync().ConfigureAwait(false);
        Observe(null);
        _shell.SetNodeStatus(false);
        Publish(State with { Phase = PlayPhase.Nodes, Node = null, Loading = false,
            Message = "Node disconnected." });
    }

    public Task RefreshLobbiesAsync(CancellationToken cancellationToken = default)
        => SendAsync("lobby.list", new LobbyList(), cancellationToken);

    public Task CreateLobbyAsync(string name, CancellationToken cancellationToken = default)
        => SendAsync("lobby.create", new LobbyCreate(ValidateLobbyName(name), LobbyVisibility.Public), cancellationToken);

    public Task JoinLobbyAsync(Guid lobbyId, long revision, bool observer = false,
        CancellationToken cancellationToken = default)
        => SendAsync("lobby.join", new LobbyJoin(lobbyId, ValidateRevision(revision), observer), cancellationToken);

    public Task LeaveLobbyAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, _) => new LobbyLeave(lobby.Revision), "lobby.leave", cancellationToken);

    public Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, _) => new LobbySetReady(ready, lobby.Revision),
            "lobby.ready.set", cancellationToken);

    /// <summary>Match hunter is lobby state; it never updates the profile favorite.</summary>
    public Task SelectLobbyHunterAsync(Hunter hunter, CancellationToken cancellationToken = default)
    {
        if (hunter is < Hunter.Samus or > Hunter.Weavel)
            throw new ArgumentOutOfRangeException(nameof(hunter));
        Publish(State with { LobbyHunter = hunter });
        return SendLobbyCommandAsync((lobby, _) => new LobbySelectHunter(hunter, lobby.Revision),
            "lobby.hunter.select", cancellationToken);
    }

    public async Task ConfigureLobbyAsync(string mapKey, MatchMode mode, int botCount,
        int? timeLimitSeconds, int? pointGoal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapKey))
            throw new ArgumentException("Choose a map hosted by this Node and installed locally.", nameof(mapKey));
        MapCatalogSnapshot catalog = GetMapCatalogSnapshot();
        if (catalog.State != NodeMapCatalogState.Available
            || !catalog.Available.Contains(mapKey, StringComparer.Ordinal))
        {
            throw catalog.State switch
            {
                NodeMapCatalogState.Unknown => new InvalidOperationException(MapCatalogMessageFor(NodeMapCatalogState.Unknown)),
                NodeMapCatalogState.Empty => new InvalidOperationException(MapCatalogMessageFor(NodeMapCatalogState.Empty)),
                NodeMapCatalogState.Disjoint => new InvalidOperationException(MapCatalogMessageFor(NodeMapCatalogState.Disjoint)),
                _ => new ArgumentException("Choose a map hosted by this Node and installed locally.", nameof(mapKey))
            };
        }

        await _configureOperation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NodeControlClient node = RequireConnected();
            LobbySnapshot lobby = node.Lobby ?? throw new InvalidOperationException("Join a lobby first.");
            NodeSessionSnapshot? session = node.Session;
            if (session is null || lobby.OwnerSessionId != session.SessionId)
                throw new InvalidOperationException("Only the lobby owner can configure a match.");
            ValidateLobbyConfiguration(lobby, mode, botCount, timeLimitSeconds, pointGoal);
            if (StringComparer.Ordinal.Equals(lobby.MapKey, mapKey) && lobby.Mode == mode
                && lobby.BotCount == botCount && lobby.TimeLimitSeconds == timeLimitSeconds
                && lobby.PointGoal == pointGoal) return;

            NodeControlEvent response = await node.SendAndWaitAsync("lobby.configure",
                new LobbyConfigure(lobby.Revision, mapKey, mode, botCount, timeLimitSeconds, pointGoal),
                cancellationToken).ConfigureAwait(false);
            if (response.Type == "error")
            {
                NodeControlError error = response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)
                    ?? new NodeControlError("rejected", "The Node rejected the lobby settings.");
                throw new InvalidOperationException(error.Message);
            }
            if (response.Type != "lobby.snapshot")
                throw new InvalidOperationException("The Node returned an invalid lobby settings response.");
            LobbySnapshot applied = response.Payload.Deserialize(NodeJsonContext.Default.LobbySnapshot)
                ?? throw new InvalidOperationException("The Node returned an invalid lobby snapshot.");
            if (applied.LobbyId != lobby.LobbyId || applied.Revision <= lobby.Revision
                || !StringComparer.Ordinal.Equals(applied.MapKey, mapKey) || applied.Mode != mode
                || applied.BotCount != botCount || applied.TimeLimitSeconds != timeLimitSeconds
                || applied.PointGoal != pointGoal
                || node.Session?.SessionId != session.SessionId)
                throw new InvalidOperationException("The lobby changed before these settings were applied. Try again.");
        }
        finally { _configureOperation.Release(); }
    }

    internal static void ValidateLobbyConfiguration(LobbySnapshot lobby, MatchMode mode,
        int botCount, int? timeLimitSeconds, int? pointGoal)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        int humanPlayers = lobby.Members.Count(member => !member.Observer);
        if (botCount < 0 || botCount + humanPlayers > lobby.PlayerLimit)
            throw new ArgumentOutOfRangeException(nameof(botCount),
                "Bots cannot exceed the lobby's remaining player capacity.");
        if (timeLimitSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds),
                "Time limit must be Default or between 1 and 3600 seconds.");
        if (pointGoal is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pointGoal),
                "Point limit must be Default or between 1 and 65535.");
    }

    public Task StartMatchAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, session) =>
        {
            if (lobby.OwnerSessionId != session?.SessionId)
                throw new InvalidOperationException("Only the lobby owner can start a match.");
            return new LobbyStart(lobby.Revision);
        }, "lobby.start", cancellationToken);

    public Task RematchAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, _) => new LobbyRematch(lobby.Revision), "lobby.rematch", cancellationToken);

    public Task ReturnToLobbyAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, _) => new LobbyReturn(lobby.Revision), "lobby.return", cancellationToken);

    public Task RejoinWorkerAsync(CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        NodeMatchHandoff handoff = node.Handoff
            ?? throw new InvalidOperationException("No Worker handoff is available to rejoin.");
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        NetSession.Stop();
        return node.SendAsync("match.rejoin", new NodeMatchRejoin(handoff.MatchId), cancellationToken);
    }

    public async Task<bool> RetryHandoffAsync(CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        NodeMatchHandoff handoff = node.Handoff
            ?? throw new InvalidOperationException("No Worker handoff is available.");
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        return await StartTrackedHandoffAsync(node, handoff, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Invalidate in-flight Worker work when the identity changes. Node
    /// disconnect is owned by GatewayController; this method only closes the
    /// gameplay transport and advances the stale-operation generation.
    /// </summary>
    public void CancelIdentityOperations()
    {
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        NetSession.Stop();
    }

    public void CancelPendingHandoff()
    {
        if (!_handoff.IsActive) return;
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        NetSession.Stop();
        Publish(State with { Loading = false, Message = "Worker connection cancelled." });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _generation++;
        _lifetime.Cancel();
        _handoff.Cancel();
        NodeSessions.CurrentChanged -= NodeSessionChanged;
        Observe(null);
        Task<bool>? handoffTask;
        lock (_handoffTaskLock) handoffTask = _handoffTask;
        if (handoffTask != null)
        {
            try { await handoffTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        _operation.Dispose();
        await _configureOperation.WaitAsync().ConfigureAwait(false);
        _configureOperation.Release();
        _configureOperation.Dispose();
        _lifetime.Dispose();
    }

    private async Task SendLobbyCommandAsync(Func<LobbySnapshot, NodeSessionSnapshot?, NodeCommand> command,
        string type, CancellationToken cancellationToken)
    {
        NodeControlClient node = RequireConnected();
        LobbySnapshot lobby = node.Lobby ?? throw new InvalidOperationException("Join a lobby first.");
        await node.SendAsync(type, command(lobby, node.Session), cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(string type, NodeCommand command,
        CancellationToken cancellationToken)
    {
        NodeControlClient node = RequireConnected();
        await node.SendAsync(type, command, cancellationToken).ConfigureAwait(false);
    }

    private void Observe(NodeControlClient? session)
    {
        if (_observed != null) _observed.Changed -= NodeChanged;
        _observed = session;
        if (session == null) return;
        RestoreNodeCatalog(session);
        session.Changed += NodeChanged;
        PublishFromNode(session);
    }

    private void NodeChanged()
    {
        NodeControlClient? node = _observed;
        if (node == null || Volatile.Read(ref _disposed) != 0) return;
        PublishFromNode(node);
        if (node.State.MatchEnded)
            _handoff.Cancel();
        if (Volatile.Read(ref _handoffEnabled) != 0
            && node.State.Handoff is { } handoff && !node.State.MatchEnded)
            _ = StartTrackedHandoffAsync(node, handoff, _lifetime.Token);
    }

    private void NodeSessionChanged(NodeControlClient? node)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Observe(node);
        if (node == null)
        {
            ClearSelectedNodeCatalog();
            _shell.SetNodeStatus(false);
            Publish(State with { Phase = PlayPhase.Nodes, Node = null, Loading = false,
                Message = "Node disconnected." });
        }
    }

    private void PublishFromNode(NodeControlClient node)
    {
        NodeControlClient.ViewState state = node.State;
        PlayPhase phase = state.Handoff != null && !state.MatchEnded ? PlayPhase.Handoff
            : state.Lobby != null ? PlayPhase.Lobby
            : node.Connected ? PlayPhase.Connected : PlayPhase.Error;
        string message = state.Error ?? (phase switch
        {
            PlayPhase.Handoff => "Match authorized. Connecting gameplay transport…",
            PlayPhase.Lobby => state.Lobby!.Name,
            PlayPhase.Connected => "Connected to Node.",
            _ => "Node disconnected. Reconnect to continue."
        });
        _shell.SetNodeStatus(node.Connected, _connectedNodeName, _connectedNodeRegion);
        Publish(State with { Phase = phase, Node = state, Loading = false, Message = message });
    }

    private async Task<bool> StartHandoffAsync(NodeControlClient node, NodeMatchHandoff handoff,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _handoffEnabled) == 0) return false;
        NodeSessionSnapshot session = node.Session
            ?? throw new InvalidOperationException("The Node session is not ready.");
        long generation = Interlocked.Read(ref _generation);
        PlayHandoffKey key = new(session.NodeId, handoff.MatchId, handoff.Nonce, generation);
        if (!_handoff.TryBegin(key)) return false;
        Publish(State with { Phase = PlayPhase.Handoff, Loading = true,
            Message = "Allocating Worker and synchronizing content…" });
        bool joined = false;
        try
        {
            joined = await NetLaunch.JoinWorkerAsync(handoff, session.DisplayName, cancellationToken)
                .ConfigureAwait(false);
            if (!joined) throw new InvalidOperationException(NetLaunch.LastJoinError);
            if (Volatile.Read(ref _disposed) != 0 || generation != Interlocked.Read(ref _generation)
                || Volatile.Read(ref _handoffEnabled) == 0 || cancellationToken.IsCancellationRequested
                || !ReferenceEquals(_observed, node)
                || !_handoff.TryComplete(key))
            {
                NetSession.Stop();
                _handoff.Cancel(key);
                return false;
            }
            node.MarkGameplayJoined(handoff.MatchId);
            LobbySnapshot? lobby = node.Lobby;
            LaunchPlan plan = new()
            {
                Kind = LaunchKind.Online,
                Hunter = handoff.Hunter,
                PlayerName = session.DisplayName,
                RoomKey = lobby?.MapKey ?? "",
                Mode = lobby?.Mode.ToLegacyMode() ?? GameMode.Battle,
                Port = handoff.Port
            };
            Launch?.Invoke(this, plan);
            Publish(State with { Loading = false, Message = "Gameplay transport connected." });
            return true;
        }
        catch (OperationCanceledException)
        {
            NetSession.Stop();
            _handoff.Cancel(key);
            if (IsStaleHandoff(node, generation, cancellationToken)) return false;
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = "Worker connection cancelled." });
            return false;
        }
        catch (Exception error)
        {
            NetSession.Stop();
            _handoff.Cancel(key);
            if (IsStaleHandoff(node, generation, cancellationToken)) return false;
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = error.Message });
            _shell.Notify(PrimeNotificationKind.Error, error.Message);
            return false;
        }
    }

    private Task<bool> StartTrackedHandoffAsync(NodeControlClient node, NodeMatchHandoff handoff,
        CancellationToken cancellationToken)
    {
        lock (_handoffTaskLock)
        {
            if (_handoffTask is { IsCompleted: false }) return _handoffTask;
            _handoffTask = RunHandoffWithLifetimeAsync(node, handoff, cancellationToken);
            return _handoffTask;
        }
    }

    private async Task<bool> RunHandoffWithLifetimeAsync(NodeControlClient node,
        NodeMatchHandoff handoff, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        return await StartHandoffAsync(node, handoff, linked.Token).ConfigureAwait(false);
    }

    private bool IsStaleHandoff(NodeControlClient node, long generation,
        CancellationToken cancellationToken)
        => Volatile.Read(ref _disposed) != 0
            || generation != Interlocked.Read(ref _generation)
            || Volatile.Read(ref _handoffEnabled) == 0
            || cancellationToken.IsCancellationRequested
            || !ReferenceEquals(_observed, node);

    private async Task<AccountSession> RequireAccountAsync(CancellationToken cancellationToken)
    {
        if (!_shell.HasNetworkIdentity)
            throw new InvalidOperationException("Sign in or explicitly choose Guest access before browsing Nodes.");
        return await _accountResolver(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Configure a Backend in Settings before browsing Nodes.");
    }

    private static async Task<AccountSession?> ResolveAccountAsync(CancellationToken cancellationToken)
    {
        if (AccountSessions.Current != null) return AccountSessions.Current;
        if (!Uri.TryCreate(LauncherPrefs.BackendAddress?.Trim(), UriKind.Absolute, out Uri? backend)
            || !AccountSession.IsAllowedBackend(backend)) return null;
        return await AccountSessions.ConfigureAsync(backend, restore: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private NodeControlClient RequireConnected()
        => NodeSessions.Current is { Connected: true } node
            ? node : throw new InvalidOperationException("Connect to a Node first.");

    private void Publish(PlayState state)
    {
        lock (_stateLock) _state = state with { Revision = state.Revision + 1 };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreNodeCatalog(NodeControlClient node)
    {
        lock (_mapCatalogLock)
        {
            _hasSelectedNode = true;
            _hostedMapKeys = node.AdvertisedMapKeys;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSelectedNodeCatalog()
    {
        lock (_mapCatalogLock)
        {
            _hasSelectedNode = false;
            _hostedMapKeys = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MapCatalogSnapshot GetMapCatalogSnapshot()
    {
        string[]? hosted;
        bool selected;
        lock (_mapCatalogLock)
        {
            selected = _hasSelectedNode;
            hosted = _hostedMapKeys?.ToArray();
        }
        if (!selected) return new(NodeMapCatalogState.None, Array.Empty<string>());
        (NodeMapCatalogState state, string[] available) = ResolveMapCatalog(Volatile.Read(ref _maps), hosted);
        return new(state, available);
    }

    internal static (NodeMapCatalogState State, string[] Available) ResolveMapCatalog(
        IEnumerable<string> localMaps, string[]? hostedMapKeys)
    {
        ArgumentNullException.ThrowIfNull(localMaps);
        if (hostedMapKeys == null) return (NodeMapCatalogState.Unknown, Array.Empty<string>());
        if (hostedMapKeys.Length == 0) return (NodeMapCatalogState.Empty, Array.Empty<string>());

        HashSet<string> hosted = hostedMapKeys.ToHashSet(StringComparer.Ordinal);
        string[] available = localMaps.Where(hosted.Contains).Distinct(StringComparer.Ordinal)
            .OrderBy(map => map, StringComparer.Ordinal).ToArray();
        return available.Length == 0
            ? (NodeMapCatalogState.Disjoint, available)
            : (NodeMapCatalogState.Available, available);
    }

    private static string GetMapCatalogMessage(NodeMapCatalogState state) => state switch
    {
        NodeMapCatalogState.None => "Select a Node to see hosted maps.",
        NodeMapCatalogState.Unknown => MapCatalogMessageFor(NodeMapCatalogState.Unknown),
        NodeMapCatalogState.Empty => MapCatalogMessageFor(NodeMapCatalogState.Empty),
        NodeMapCatalogState.Disjoint => MapCatalogMessageFor(NodeMapCatalogState.Disjoint),
        _ => ""
    };

    private static string MapCatalogMessageFor(NodeMapCatalogState state) => state switch
    {
        NodeMapCatalogState.Unknown => "This Node did not advertise its hosted map catalog; map selection is unavailable.",
        NodeMapCatalogState.Empty => "This Node advertises no hosted maps.",
        NodeMapCatalogState.Disjoint => "This Node's hosted maps are not installed locally.",
        _ => ""
    };

    private static string ValidateLobbyName(string name)
    {
        string value = name?.Trim() ?? "";
        if (value is not { Length: >= 1 and <= 64 } || value.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Lobby name must be 1 to 64 printable characters.", nameof(name));
        return value;
    }

    private static long ValidateRevision(long revision)
        => revision > 0 ? revision : throw new ArgumentOutOfRangeException(nameof(revision));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
