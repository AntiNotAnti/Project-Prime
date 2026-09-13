using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using ProjectPrime.Server.Shared;
using MphRead.Cosmetics;
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

public enum MapPreparationPhase
{
    None,
    Downloading,
    Compiling,
    Ready,
    Failed,
    Cancelled
}

public enum OnlineLifecyclePhase
{
    Offline,
    Discovering,
    Connecting,
    Connected,
    InLobby,
    PreparingContent,
    Launching,
    InMatch,
    Results,
    PreparingContinuation,
    Returning,
    Interrupted,
    Reconnecting,
    RejoiningMatch,
    AwaitingMatchSnapshot,
    SessionExpired,
    RecoverableError
}

/// <summary>
/// One immutable presentation projection for the online shell. It combines
/// existing PlayController, map-preparation, session-flow, and runtime
/// snapshots; it owns no transitions or mutable lifecycle state.
/// </summary>
public sealed record OnlineLifecyclePresentation(
    OnlineLifecyclePhase Phase,
    OnlineRecoveryState RecoveryState,
    bool HasNode,
    bool NodeConnected,
    bool HasMatch,
    string Message,
    ClientSessionPhase SessionPhase = ClientSessionPhase.Gateway,
    MapPreparationPhase PreparationPhase = MapPreparationPhase.None)
{
    internal static OnlineLifecyclePresentation FromRuntime(OnlineRecoveryState recoveryState,
        bool hasNode, bool nodeConnected, bool hasMatch)
    {
        OnlineLifecyclePhase phase = recoveryState switch
        {
            OnlineRecoveryState.Connected when nodeConnected => OnlineLifecyclePhase.Connected,
            OnlineRecoveryState.Connected when !hasNode => OnlineLifecyclePhase.Offline,
            OnlineRecoveryState.Connected => OnlineLifecyclePhase.Interrupted,
            OnlineRecoveryState.ConnectionLost when !hasNode => OnlineLifecyclePhase.Offline,
            OnlineRecoveryState.ConnectionLost => OnlineLifecyclePhase.Interrupted,
            OnlineRecoveryState.Reconnecting => OnlineLifecyclePhase.Reconnecting,
            OnlineRecoveryState.RejoiningMatch => OnlineLifecyclePhase.RejoiningMatch,
            OnlineRecoveryState.AwaitingMatchSnapshot => OnlineLifecyclePhase.AwaitingMatchSnapshot,
            OnlineRecoveryState.SessionExpired => OnlineLifecyclePhase.SessionExpired,
            _ => OnlineLifecyclePhase.Offline
        };
        return new(phase, recoveryState, hasNode, nodeConnected, hasMatch,
            MessageFor(phase, hasMatch));
    }

    internal static OnlineLifecyclePresentation FromRuntime(OnlineRecoveryState recoveryState,
        bool hasNode, bool nodeConnected, bool hasMatch, PlayState state,
        MapPreparationPhase preparationPhase, ClientSessionPhase sessionPhase)
    {
        OnlineLifecyclePresentation runtime = FromRuntime(recoveryState,
            hasNode, nodeConnected, hasMatch);
        OnlineLifecyclePhase phase = recoveryState != OnlineRecoveryState.Connected
            ? runtime.Phase
            : preparationPhase is MapPreparationPhase.Downloading or MapPreparationPhase.Compiling
                ? OnlineLifecyclePhase.PreparingContent
                : preparationPhase == MapPreparationPhase.Failed
                    ? OnlineLifecyclePhase.RecoverableError
                    : sessionPhase switch
                    {
                        ClientSessionPhase.Launching => OnlineLifecyclePhase.Launching,
                        ClientSessionPhase.InMatch => OnlineLifecyclePhase.InMatch,
                        ClientSessionPhase.Results => OnlineLifecyclePhase.Results,
                        ClientSessionPhase.PreparingContinuation => OnlineLifecyclePhase.PreparingContinuation,
                        ClientSessionPhase.ReturningToLobby => OnlineLifecyclePhase.Returning,
                        ClientSessionPhase.Closing => OnlineLifecyclePhase.Offline,
                        _ => state.Phase switch
                        {
                            PlayPhase.Nodes when state.Loading => OnlineLifecyclePhase.Discovering,
                            PlayPhase.Nodes => OnlineLifecyclePhase.Offline,
                            PlayPhase.LoadingNodes => OnlineLifecyclePhase.Connecting,
                            PlayPhase.Connected when state.Node?.MatchEnded == true => OnlineLifecyclePhase.Results,
                            PlayPhase.Connected => OnlineLifecyclePhase.Connected,
                            PlayPhase.Lobby when state.Node?.MatchEnded == true => OnlineLifecyclePhase.Results,
                            PlayPhase.Lobby => OnlineLifecyclePhase.InLobby,
                            PlayPhase.Handoff => OnlineLifecyclePhase.Launching,
                            PlayPhase.Error => OnlineLifecyclePhase.RecoverableError,
                            _ => OnlineLifecyclePhase.Offline
                        }
                    };
        return runtime with
        {
            Phase = phase,
            Message = MessageFor(phase, hasMatch),
            SessionPhase = sessionPhase,
            PreparationPhase = preparationPhase
        };
    }

    internal static string MessageFor(OnlineLifecyclePhase phase, bool hasMatch)
        => phase switch
        {
            OnlineLifecyclePhase.Discovering => "Discovering servers…",
            OnlineLifecyclePhase.Connecting => "Connecting to server…",
            OnlineLifecyclePhase.Connected => hasMatch ? "Connected · match active" : "Connected",
            OnlineLifecyclePhase.InLobby => "In lobby",
            OnlineLifecyclePhase.PreparingContent => "Preparing arena content…",
            OnlineLifecyclePhase.Launching => "Launching match…",
            OnlineLifecyclePhase.InMatch => "In match",
            OnlineLifecyclePhase.Results => "Match results",
            OnlineLifecyclePhase.PreparingContinuation => "Preparing next match…",
            OnlineLifecyclePhase.Returning => "Returning to lobby…",
            OnlineLifecyclePhase.Interrupted => "Connection lost",
            OnlineLifecyclePhase.Reconnecting => "Reconnecting…",
            OnlineLifecyclePhase.RejoiningMatch => "Rejoining match…",
            OnlineLifecyclePhase.AwaitingMatchSnapshot => "Syncing match…",
            OnlineLifecyclePhase.SessionExpired => "Session expired",
            OnlineLifecyclePhase.RecoverableError => "Connection needs attention",
            _ => "Offline"
        };
}

/// <summary>Immutable UI projection for one required-map preparation task.</summary>
public sealed record MapPreparationState(MapPreparationPhase Phase,
    MapRequirement? Requirement, long Received, long Total, string? Failure,
    long Generation)
{
    public static MapPreparationState None { get; } = new(
        MapPreparationPhase.None, null, 0, 0, null, 0);

    public bool IsReadyFor(MapRequirement requirement)
        => Phase == MapPreparationPhase.Ready && Requirement == requirement;
}

public sealed record PlayState(PlayPhase Phase, IReadOnlyList<NodeListing> Nodes,
    NodeControlClient.ViewState? Node, Hunter LobbyHunter, string Message,
    bool Loading, long Revision)
{
    public static PlayState Initial => new(PlayPhase.Nodes, Array.Empty<NodeListing>(),
        null, Hunter.Samus, "Choose Quick Play, Browse Lobbies, or Host Lobby.", false, 0);

    public LobbySnapshot? Lobby => Node?.Lobby;
    public LobbyListSnapshot? BrowsedLobbies { get; init; }
    public LobbyListSnapshot? Lobbies => BrowsedLobbies ?? Node?.Lobbies;
    /// <summary>
    /// Current public match-directory presentation state. The projection is
    /// evaluated from this immutable snapshot and never caches lobby entries.
    /// </summary>
    internal MatchDirectoryPresentationState MatchDirectoryState
        => MatchDirectoryPresentation.From(this);
    public NodeMatchHandoff? Handoff => Node?.Handoff;
    public NodeRoundSnapshot? Round => Node?.Round;
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
            throw new ArgumentException("The match connection details are invalid.", nameof(key));
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

/// <summary>
/// Narrow action boundary for the in-match transition menu. The pause view
/// only renders this projection and raises presentation events; PlayController
/// remains the owner of Node commands, revisions, and user-facing failures.
/// </summary>
internal interface IMatchTransitionMenuActions
{
    bool TransitionMenuSupported { get; }
    bool TransitionRequestInFlight { get; }
    string? TransitionError { get; }
    NodeMatchTransitionVoteSnapshot? TransitionVote { get; }
    string? CurrentMapKey { get; }
    Hunter? CurrentHunter { get; }
    IReadOnlyList<string> AvailableTransitionMaps { get; }
    event EventHandler? Changed;
    Task RequestRestartMatchAsync(CancellationToken cancellationToken = default);
    Task RequestChangeMapAsync(string mapKey, CancellationToken cancellationToken = default);
    Task RequestHunterChangeAsync(Hunter hunter, CancellationToken cancellationToken = default);
    Task RequestTransitionVoteAsync(bool accept, CancellationToken cancellationToken = default);
}

/// <summary>Node/lobby adapter for the persistent NodeSessions transport.</summary>
public sealed class PlayController : IAsyncDisposable, IMatchTransitionMenuActions
{
    private readonly record struct MapCatalogSnapshot(NodeMapCatalogState State, string[] Available);
    internal enum NodeConnectFailureKind
    {
        None,
        Transient,
        Permanent,
        Stale
    }

    private readonly record struct NodeConnectOutcome(NodeConnectFailureKind Failure,
        Exception? Error = null)
    {
        public bool Connected => Failure == NodeConnectFailureKind.None;
    }

    private const int QuickPlayMaximumCandidates = 3;
    private static readonly TimeSpan QuickPlayConnectDeadline = TimeSpan.FromSeconds(20);
    private readonly PrimeShellState _shell;
    private readonly TimeProvider _time;
    private DateTimeOffset? _directoryFetchedAt;
    private NodeControlClient? _resumeAttempted;
    private CancellationTokenSource? _activeEntry;
    private readonly SemaphoreSlim _entryOperation = new(1, 1);
    private string[] _maps;
    private readonly Func<CancellationToken, Task<AccountSession?>> _accountResolver;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _configureOperation = new(1, 1);
    private readonly SemaphoreSlim _transitionOperation = new(1, 1);
    private readonly SemaphoreSlim _presenceOperation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PlayHandoffGate _handoff = new();
    private readonly object _handoffTaskLock = new();
    private readonly object _stateLock = new();
    private readonly object _mapCatalogLock = new();
    private readonly object _mapPreparationLock = new();
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
    private Guid? _leavingLobbyId;
    private readonly ClientOnlineRuntime _online;
    private readonly bool _ownsOnline;
    private readonly MapAcquisitionService _mapAcquisition = MapAcquisitionService.Shared;
    private readonly Task _mapAcquisitionInitialization;
    private CancellationTokenSource? _mapPreparationCancellation;
    private Task<InstalledMap>? _mapPreparationTask;
    private NodeControlClient? _mapPreparationNode;
    private MapRequirement? _mapPreparationRequirement;
    private long _mapPreparationGeneration;
    private MapPreparationState _mapPreparation = MapPreparationState.None;
    private int _transitionRequestInFlight;
    private string? _transitionError;
    private readonly object _transitionReadyLock = new();
    private Guid? _transitionReadyInFlight;
    private Guid? _transitionReadyCompleted;
    private readonly object _presenceLeaseLock = new();
    private CancellationTokenSource? _presenceLease;
    private Task? _presenceLoop;
    private long _presenceGeneration;
    private PresencePresentationState _presence = PresencePresentationState.Initial;
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(12);

    public PlayController(PrimeShellState shell, IReadOnlyList<string>? maps = null,
        Func<CancellationToken, Task<AccountSession?>>? accountResolver = null, TimeProvider? timeProvider = null,
        ClientOnlineRuntime? onlineRuntime = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _maps = maps?.Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        _accountResolver = accountResolver ?? ResolveAccountAsync;
        _online = onlineRuntime ?? new ClientOnlineRuntime();
        _ownsOnline = onlineRuntime == null;
        NodeSessions.CurrentChanged += NodeSessionChanged;
        LauncherPrefs.ShowOnlinePresenceChanged += PresencePreferenceChanged;
        _mapAcquisitionInitialization = InitializeMapAcquisitionAsync();
        if (_online.Node is { } current)
            Observe(current);
    }

    public PlayState State { get { lock (_stateLock) return _state; } }
    public PresencePresentationState Presence => Volatile.Read(ref _presence);
    internal ClientOnlineRuntime Online => _online;
    /// <summary>Region used by automatic Node selection.</summary>
    public string PreferredRegion
    {
        get => LauncherPrefs.PreferredRegion;
        set => LauncherPrefs.PreferredRegion = value;
    }
    public NodeMapCatalogState MapCatalogState => GetMapCatalogSnapshot().State;
    public IReadOnlyList<string> AvailableMaps => GetMapCatalogSnapshot().Available;
    public MapPreparationState MapPreparation => Volatile.Read(ref _mapPreparation);
    bool IMatchTransitionMenuActions.TransitionMenuSupported => TransitionMenuSupported;
    bool IMatchTransitionMenuActions.TransitionRequestInFlight => TransitionRequestInFlight;
    string? IMatchTransitionMenuActions.TransitionError => TransitionError;
    NodeMatchTransitionVoteSnapshot? IMatchTransitionMenuActions.TransitionVote => TransitionVote;
    string? IMatchTransitionMenuActions.CurrentMapKey => CurrentMapKey;
    Hunter? IMatchTransitionMenuActions.CurrentHunter => CurrentHunter;
    IReadOnlyList<string> IMatchTransitionMenuActions.AvailableTransitionMaps
        => AvailableTransitionMaps;
    public bool TransitionMenuSupported => IsTransitionParticipant();
    public bool TransitionRequestInFlight => Volatile.Read(ref _transitionRequestInFlight) != 0;
    public string? TransitionError
    {
        get
        {
            string? local = Volatile.Read(ref _transitionError);
            if (local != null) return local;
            if (State.Node?.TransitionVote is { State: MatchTransitionVoteState.Failed } failed)
                return failed.FailureCode ?? "The server could not continue this match.";
            return null;
        }
    }
    public string? CurrentMapKey => State.Lobby?.MapKey;
    public Hunter? CurrentHunter
    {
        get
        {
            NodeControlClient? node = _online.Node;
            Guid? sessionId = node?.Session?.SessionId;
            return sessionId is { } id
                ? node?.Lobby?.Members.FirstOrDefault(member => member.SessionId == id)?.Hunter
                : null;
        }
    }
    public NodeMatchTransitionVoteSnapshot? TransitionVote
    {
        get
        {
            NodeControlClient? node = _online.Node;
            Guid? matchId = node?.Handoff?.MatchId ?? node?.Lobby?.CurrentMatchId
                ?? node?.State.JoinedMatchId;
            return matchId is { } id ? node?.TransitionVoteFor(id) : null;
        }
    }
    public IReadOnlyList<string> AvailableTransitionMaps
    {
        get
        {
            string? current = State.Lobby?.MapKey;
            return AvailableMaps.Where(map => !StringComparer.Ordinal.Equals(map, current))
                .ToArray();
        }
    }
    public OnlineLifecyclePresentation OnlineLifecycle
    {
        get
        {
            PlayState state = State;
            OnlineRuntimeSnapshot runtime = _online.GetSnapshot();
            return OnlineLifecyclePresentation.FromRuntime(runtime.RecoveryState,
                runtime.Node != null, runtime.Node?.Connected == true, runtime.Match != null,
                state, MapPreparation.Phase, runtime.SessionPhase);
        }
    }

    private async Task InitializeMapAcquisitionAsync()
    {
        try
        {
            await _mapAcquisition.InitializeAsync(_lifetime.Token)
                .ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) == 0)
                Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (Volatile.Read(ref _disposed) == 0)
                Publish(State with { Message = "Installed maps could not be scanned: "
                    + error.Message });
        }
    }
    public string MapCatalogMessage => GetMapCatalogMessage(MapCatalogState);
    public PlayHandoffGate HandoffGate => _handoff;
    public event EventHandler? Changed;
    public event EventHandler? PresenceChanged;
    public event EventHandler<LaunchPlan>? Launch;

    /// <summary>
    /// Owns the single route-scoped public presence refresh lease. It is
    /// independent from lobby/join operations and is stopped whenever Play is
    /// hidden or the app is suspended.
    /// </summary>
    public void SetPresenceRefreshEnabled(bool enabled)
    {
        lock (_presenceLeaseLock)
        {
            if (!enabled)
            {
                _presenceGeneration++;
                _presenceLease?.Cancel();
                _presenceLease?.Dispose();
                _presenceLease = null;
                _presenceLoop = null;
                return;
            }
            if (_presenceLease != null || Volatile.Read(ref _disposed) != 0)
                return;

            _presenceLease = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token);
            long generation = ++_presenceGeneration;
            _presenceLoop = RunPresenceLoopAsync(generation, _presenceLease.Token);
        }
    }

    public Task RefreshPresenceAsync(CancellationToken cancellationToken = default)
        => RefreshPresenceCoreAsync(Volatile.Read(ref _presenceGeneration),
            cancellationToken);

    private async Task RunPresenceLoopAsync(long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshPresenceCoreAsync(generation, cancellationToken)
                    .ConfigureAwait(false);
                await Task.Delay(PresenceRefreshInterval, _time,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
    }

    private async Task RefreshPresenceCoreAsync(long generation,
        CancellationToken cancellationToken)
    {
        if (!await _presenceOperation.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
            return;
        try
        {
            ThrowIfDisposed();
            PresencePresentationState previous = Presence;
            if (previous.State == PresenceLoadState.Unknown)
                PublishPresence(previous with { State = PresenceLoadState.Loading,
                    Error = null }, generation);

            AccountSession? account = await _accountResolver(cancellationToken)
                .ConfigureAwait(false);
            if (account == null)
                throw new InvalidOperationException(
                    "The public presence service is not configured.");
            PresenceDirectorySnapshot page = await account.GetPresenceAsync(
                cancellationToken).ConfigureAwait(false);
            PublishPresence(new PresencePresentationState(PresenceLoadState.Ready,
                page.TotalOnline, page.VisibleOnline, page.Entries,
                page.Revision, page.GeneratedAt), generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            PresencePresentationState previous = Presence;
            int fallbackTotal = IsDirectoryFresh(_directoryFetchedAt,
                _time.GetUtcNow())
                ? State.Nodes?.Sum(node => node.OnlineUsers) ?? 0 : 0;
            PublishPresence(previous with
            {
                State = PresenceLoadState.Failed,
                TotalOnline = previous.TotalOnline > 0
                    ? previous.TotalOnline : fallbackTotal,
                Error = "Online player status is unavailable."
            }, generation);
        }
        finally
        {
            _presenceOperation.Release();
        }
    }

    private void PublishPresence(PresencePresentationState state, long generation)
    {
        if (generation != Volatile.Read(ref _presenceGeneration)
            || Volatile.Read(ref _disposed) != 0)
            return;
        PresencePresentationState before = Presence;
        if (SamePresencePresentation(before, state))
        {
            // Retain freshness metadata without rebuilding the Play route or
            // announcing an unchanged population on every polling interval.
            Volatile.Write(ref _presence, state);
            return;
        }
        Volatile.Write(ref _presence, state);
        PresenceChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static bool SamePresencePresentation(PresencePresentationState left,
        PresencePresentationState right)
        => left.State == right.State
            && left.TotalOnline == right.TotalOnline
            && left.VisibleOnline == right.VisibleOnline
            && left.Revision == right.Revision
            && StringComparer.Ordinal.Equals(left.Error, right.Error)
            && left.Players.SequenceEqual(right.Players);

    private void PresencePreferenceChanged(object? sender, EventArgs args)
        => _ = ApplyPresencePreferenceAsync();

    private async Task ApplyPresencePreferenceAsync()
    {
        NodeControlClient? node = _online.Node;
        if (node is not { Connected: true }) return;
        try
        {
            await node.SetPresenceVisibilityAsync(LauncherPrefs.ShowOnlinePresence,
                _lifetime.Token).ConfigureAwait(false);
            await RefreshPresenceAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch
        {
            _shell.Notify(PrimeNotificationKind.Warning,
                "Your visibility setting was saved and will apply when you reconnect.");
        }
    }

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

    /// <summary>Supplies deterministic public-presence data to offline UI tests.</summary>
    internal void SetPresenceForCapture(PresencePresentationState state)
        => Volatile.Write(ref _presence,
            state ?? throw new ArgumentNullException(nameof(state)));

    public void SetHandoffEnabled(bool enabled)
    {
        int desired = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _handoffEnabled, desired) == desired) return;
        if (enabled) NodeChanged();
        else CancelPendingHandoff();
    }

    public Task RefreshNodesAsync(CancellationToken cancellationToken = default)
        => RefreshDirectoryAsync(false, cancellationToken);

    public Task ForceRefreshNodesAsync(CancellationToken cancellationToken = default)
        => RefreshDirectoryAsync(true, cancellationToken);

    private async Task RefreshDirectoryAsync(bool force, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!force && IsDirectoryFresh(_directoryFetchedAt, _time.GetUtcNow())) return;
            Publish(State with { Phase = PlayPhase.LoadingNodes, Loading = true,
                Message = "Finding compatible servers…" });
            AccountSession account = await RequireAccountAsync(cancellationToken).ConfigureAwait(false);
            (string Version, string ContentHash) identity = await Task.Run(
                ContentEnvironment.GetContentIdentity, cancellationToken).ConfigureAwait(false);
            NodeListing[] nodes = await account.GetNodesAsync(NetHeader.Version, BuildVersion.Display,
                identity.ContentHash, cancellationToken).ConfigureAwait(false);
            DebugLog.Line("network/directory", $"compatible nodes={nodes.Length}, "
                + $"protocol={NetHeader.Version}, build={BuildVersion.Display}, "
                + $"content={identity.Version}:{identity.ContentHash}");
            LauncherPrefs.ObservePreferredRegions(nodes
                .Select(node => node.Region)
                .Where(region => !string.IsNullOrWhiteSpace(region))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
            _directoryFetchedAt = _time.GetUtcNow();
            Publish(new PlayState(_online.Node is { Connected: true } ? PlayPhase.Connected : PlayPhase.Nodes, nodes, State.Node, State.LobbyHunter,
                nodes.Length == 0 ? EmptyDirectoryMessage(identity.Version) : "Choose a server in Advanced Network.", false,
                State.Revision + 1));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(State with { Loading = false, Message = "Server refresh cancelled. Try again." });
            throw;
        }
        catch (Exception error)
        {
            string message = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                "Server directory unavailable. Try again.");
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = message });
            _shell.Notify(PrimeNotificationKind.Error, message);
        }
        finally { _operation.Release(); }
    }

    internal static bool IsDirectoryFresh(DateTimeOffset? fetchedAt, DateTimeOffset now)
        => fetchedAt is { } fetched && now >= fetched && now - fetched < TimeSpan.FromSeconds(25);

    internal static string EmptyDirectoryMessage(string contentVersion)
        => StringComparer.OrdinalIgnoreCase.Equals(contentVersion, "AMHE1")
            ? "No compatible servers are online."
            : $"No compatible servers support {contentVersion}. Online play currently requires "
                + "North American revision 1 game files (AMHE1). Open Settings > System > Game files "
                + "and choose Repair from .nds file.";

    internal static NodeListing? SelectAutomaticNode(IEnumerable<NodeListing> nodes, string? region,
        IReadOnlyDictionary<Guid, TimeSpan>? measuredLatency = null,
        NodeHealthCache? healthCache = null, DateTimeOffset? now = null)
        => OrderAutomaticNodes(nodes, region, measuredLatency, healthCache, now).FirstOrDefault();

    internal static IEnumerable<NodeListing> OrderAutomaticNodes(IEnumerable<NodeListing> nodes,
        string? region, IReadOnlyDictionary<Guid, TimeSpan>? measuredLatency = null,
        NodeHealthCache? healthCache = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        bool hasPreferredRegion = !string.IsNullOrWhiteSpace(region)
            && !StringComparer.OrdinalIgnoreCase.Equals(region, "Automatic");
        DateTimeOffset current = now ?? DateTimeOffset.UtcNow;
        return nodes.Where(node => node.Capacity > node.OnlineUsers)
            .Select(node =>
            {
                bool recentFailure = healthCache?.IsUnhealthy(node.NodeId, current) == true;
                TimeSpan cachedLatency = default;
                bool hasCachedLatency = healthCache?.TryGetRecentRtt(node.NodeId, current,
                    out cachedLatency) == true;
                return (Node: node, RecentFailure: recentFailure,
                    HasCachedLatency: hasCachedLatency,
                    CachedLatency: hasCachedLatency ? cachedLatency : TimeSpan.MaxValue);
            })
            // A recent failure is a penalty, not a ban. If every advertised
            // Node failed, Quick Play still has a deterministic fallback set.
            .OrderBy(candidate => candidate.RecentFailure)
            .ThenByDescending(candidate => hasPreferredRegion
                && !StringComparer.OrdinalIgnoreCase.Equals(candidate.Node.Region, "Automatic")
                && StringComparer.Ordinal.Equals(candidate.Node.Region, region))
            .ThenByDescending(candidate => measuredLatency?.ContainsKey(candidate.Node.NodeId) == true
                || candidate.HasCachedLatency)
            .ThenBy(candidate => measuredLatency != null
                && measuredLatency.TryGetValue(candidate.Node.NodeId, out TimeSpan latency)
                    ? latency : candidate.CachedLatency)
            .ThenByDescending(candidate => candidate.Node.LobbyCount > 0)
            .ThenBy(candidate => candidate.Node.OnlineUsers)
            .ThenBy(candidate => candidate.Node.NodeId)
            .Select(candidate => candidate.Node);
    }

    internal static bool IsQuickPlayEligible(LobbyListEntry lobby)
        => lobby.Phase == LobbyPhase.Open && lobby.Players + lobby.BotCount < lobby.PlayerLimit;

    public async Task<bool> EnsureNodeAsync(CancellationToken cancellationToken = default)
    {
        if (!_shell.HasNetworkIdentity) throw new InvalidOperationException("Choose an account or Guest access first.");
        if (_online.Node is { Connected: true }) return true;
        NodeControlClient? previous = _online.Node;
        if (previous != null && !ReferenceEquals(previous, _resumeAttempted))
        {
            _resumeAttempted = previous;
            if (await ResumeAsync(cancellationToken).ConfigureAwait(false)) return true;
        }
        await RefreshNodesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsDirectoryFresh(_directoryFetchedAt, _time.GetUtcNow())) return false;
        IReadOnlyDictionary<Guid, TimeSpan> latency = await NodeLatencyProbe.ProbeAsync(
            State.Nodes, PreferredRegion, cancellationToken).ConfigureAwait(false);
        NodeListing? selected = SelectAutomaticNode(State.Nodes, PreferredRegion, latency);
        if (selected == null)
        {
            Publish(State with { Loading = false, Message = "No compatible servers are available. Refresh servers in Advanced Network." });
            return false;
        }
        return await ConnectNodeAsync(selected, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Quick Play is the one automatic entry point allowed to try more than
    /// one Node. Selection is deterministic, the three-attempt cap and one
    /// overall deadline fence the operation, and only transport/unreachable
    /// failures can move to the next advertised candidate. Admission,
    /// certificate, protocol, content, and server-data failures stop the
    /// operation instead of hiding a real configuration problem.
    /// </summary>
    private async Task<bool> EnsureNodeForQuickPlayAsync(CancellationToken cancellationToken)
    {
        if (!_shell.HasNetworkIdentity)
            throw new InvalidOperationException("Choose an account or Guest access first.");
        if (_online.Node is { Connected: true }) return true;

        NodeControlClient? previous = _online.Node;
        if (previous != null && !ReferenceEquals(previous, _resumeAttempted))
        {
            _resumeAttempted = previous;
            if (await ResumeAsync(cancellationToken).ConfigureAwait(false)) return true;
        }

        await RefreshNodesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsDirectoryFresh(_directoryFetchedAt, _time.GetUtcNow())) return false;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(QuickPlayConnectDeadline);
        IReadOnlyDictionary<Guid, TimeSpan> latency;
        try
        {
            latency = await NodeLatencyProbe.ProbeAsync(
                State.Nodes, PreferredRegion, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
            && deadline.IsCancellationRequested)
        {
            PublishQuickPlayFailure(permanent: false);
            return false;
        }
        NodeListing[] candidates = OrderAutomaticNodes(State.Nodes, PreferredRegion, latency,
            NodeHealthCache.Shared, _time.GetUtcNow())
            .Take(QuickPlayMaximumCandidates).ToArray();
        if (candidates.Length == 0)
        {
            Publish(State with { Loading = false,
                Message = "No compatible servers are available. Refresh servers in Advanced Network." });
            return false;
        }

        long generation = Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        foreach (NodeListing candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
            {
                PublishQuickPlayFailure(permanent: false);
                return false;
            }
            NodeConnectOutcome outcome = await ConnectNodeAttemptAsync(candidate,
                deadline.Token, generation, cancellationToken).ConfigureAwait(false);
            if (outcome.Connected)
            {
                TimeSpan? measured = latency.TryGetValue(candidate.NodeId, out TimeSpan rtt)
                    ? rtt : null;
                NodeHealthCache.Shared.RecordSuccess(candidate.NodeId, _time.GetUtcNow(), measured);
                return true;
            }
            if (outcome.Failure != NodeConnectFailureKind.Transient)
            {
                PublishQuickPlayFailure(permanent: true);
                return false;
            }
            NodeHealthCache.Shared.RecordTransientFailure(candidate.NodeId, _time.GetUtcNow());
        }
        PublishQuickPlayFailure(permanent: false);
        return false;
    }

    private void PublishQuickPlayFailure(bool permanent)
    {
        string message = permanent
            ? "Quick Play stopped because a server rejected the connection. Check your account or client version."
            : "No compatible servers could be reached. Try again later.";
        Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = message });
        _shell.Notify(PrimeNotificationKind.Error, message);
    }

    public Task PrepareHostMatchAsync(CancellationToken cancellationToken = default)
        => RunEntryAsync(async token =>
        {
            await EnsureNodeAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    public Task BrowseLobbiesAsync(CancellationToken cancellationToken = default)
        => RunEntryAsync(async token =>
        {
            if (!await EnsureNodeAsync(token).ConfigureAwait(false)) return;
            await BrowseConnectedAsync(RequireConnected(), token).ConfigureAwait(false);
        }, cancellationToken);

    private async Task BrowseConnectedAsync(NodeControlClient node, CancellationToken token)
    {
        LobbyListSnapshot list = await LoadBrowseLobbiesAsync(
            (offset, cancel) => ReadLobbyPageAsync(node, offset, cancel), token).ConfigureAwait(false);
        Publish(State with { BrowsedLobbies = list, Message = list.NextOffset != null
            ? "Showing the first 1024 lobbies. Refresh to update available games."
            : list.Lobbies.IsEmpty ? "No lobbies available. Host a new lobby." : "Choose a lobby to join." });
    }

    public Task QuickPlayAsync(CancellationToken cancellationToken = default)
        => RunEntryAsync(async token =>
        {
            if (!await EnsureNodeForQuickPlayAsync(token).ConfigureAwait(false)) return;
            NodeControlClient node = RequireConnected();
            if (node.Lobby != null) return;
            NodeControlEvent response = await node.SendAndWaitAsync("quickplay.join", new QuickPlayJoin(), token)
                .ConfigureAwait(false);
            if (response.Type != "error")
            {
                RequireResponse(response, "lobby.snapshot");
                return;
            }
            string? code = response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Code;
            if (code == "unsupported")
            {
                // Operational rollback for older/disabled Nodes. This branch is
                // intentionally transitional and can be removed with the flag.
                bool joined = await JoinQuickPlayWithRetryAsync(
                    (offset, cancel) => ReadLobbyPageAsync(node, offset, cancel),
                    (lobby, cancel) => node.SendAndWaitAsync("lobby.join",
                        new LobbyJoin(lobby.LobbyId, lobby.Revision, false), cancel), token).ConfigureAwait(false);
                if (joined) return;
            }
            else if (code != "no_match")
            {
                RequireResponse(response, "lobby.snapshot");
            }
            await BrowseConnectedAsync(node, token).ConfigureAwait(false);
            Publish(State with { Message = "No open player slots found. Browse lobbies or host a new lobby." });
        }, cancellationToken);

    /// <summary>Hosts a public lobby with the legacy default seat layout.</summary>
    public Task HostLobbyAsync(string name, CancellationToken cancellationToken = default)
        => HostLobbyAsync(name, 8, 16, LobbySeatPolicy.ImmediateSeat, cancellationToken);

    /// <summary>
    /// Hosts a public lobby. Player and observer limits are sent to the Node as
    /// authoritative capacity; the client does not maintain a second lobby
    /// roster or apply local seat policy.
    /// </summary>
    public Task HostLobbyAsync(string name, int playerLimit, int observerLimit,
        LobbySeatPolicy seatPolicy = LobbySeatPolicy.ImmediateSeat,
        CancellationToken cancellationToken = default)
        => HostLobbyCoreAsync(name, playerLimit, observerLimit, seatPolicy,
            DuelQueuePolicy.Fifo, cancellationToken);

    /// <summary>Advanced overload retaining the Node's queue-policy field.</summary>
    public Task HostLobbyAsync(string name, int playerLimit, int observerLimit,
        LobbySeatPolicy seatPolicy, DuelQueuePolicy duelQueuePolicy,
        CancellationToken cancellationToken = default)
        => HostLobbyCoreAsync(name, playerLimit, observerLimit, seatPolicy,
            duelQueuePolicy, cancellationToken);

    private Task HostLobbyCoreAsync(string name, int playerLimit, int observerLimit,
        LobbySeatPolicy seatPolicy, DuelQueuePolicy duelQueuePolicy,
        CancellationToken cancellationToken)
    {
        LobbyCreate command = CreateLobbyCommand(name, playerLimit, observerLimit,
            seatPolicy, duelQueuePolicy);
        return RunEntryAsync(async token =>
        {
            if (!await EnsureNodeAsync(token).ConfigureAwait(false)) return;
            NodeControlClient node = RequireConnected();
            if (node.Lobby != null) return;
            await SendExpectedSnapshotAsync(node, "lobby.create", command, token)
                .ConfigureAwait(false);
        }, cancellationToken);
    }

    private Task CreateLobbyCoreAsync(string name, int playerLimit, int observerLimit,
        LobbySeatPolicy seatPolicy, DuelQueuePolicy duelQueuePolicy,
        CancellationToken cancellationToken)
    {
        LobbyCreate command = CreateLobbyCommand(name, playerLimit, observerLimit,
            seatPolicy, duelQueuePolicy);
        return SendExpectedSnapshotAsync("lobby.create", command, cancellationToken);
    }

    internal static LobbyCreate CreateLobbyCommand(string name, int playerLimit,
        int observerLimit, LobbySeatPolicy seatPolicy = LobbySeatPolicy.ImmediateSeat,
        DuelQueuePolicy duelQueuePolicy = DuelQueuePolicy.Fifo)
    {
        string validName = ValidateLobbyName(name);
        if (playerLimit is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(playerLimit),
                "Player slots must be between 1 and 8.");
        if (observerLimit is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(observerLimit),
                "Observer slots must be between 0 and 16.");
        if (!Enum.IsDefined(seatPolicy))
            throw new ArgumentOutOfRangeException(nameof(seatPolicy));
        if (!Enum.IsDefined(duelQueuePolicy))
            throw new ArgumentOutOfRangeException(nameof(duelQueuePolicy));
        if (duelQueuePolicy != DuelQueuePolicy.Fifo)
            throw new NotSupportedException("Only the FIFO lobby queue policy is supported.");
        return new(validName, LobbyVisibility.Public, playerLimit, observerLimit,
            seatPolicy, duelQueuePolicy);
    }

    internal static LobbyQueueJoin CreateWaitlistJoinCommand(Guid lobbyId, long revision,
        LobbyQueueRequestedRole requestedRole = LobbyQueueRequestedRole.Player,
        byte? requestedTeam = null)
    {
        if (!Enum.IsDefined(requestedRole))
            throw new ArgumentOutOfRangeException(nameof(requestedRole));
        if (requestedTeam is >= MatchRules.MaximumTeamCount)
            throw new ArgumentOutOfRangeException(nameof(requestedTeam),
                "Team must be 0 through 3, or unspecified.");
        return new(ValidateLobbyId(lobbyId), ValidateRevision(revision), requestedRole,
            requestedTeam);
    }

    internal static LobbyQueueLeave CreateWaitlistLeaveCommand(Guid lobbyId, long revision)
        => new(ValidateLobbyId(lobbyId), ValidateRevision(revision));

    internal static NodeCommand CreateWaitlistOfferCommand(Guid lobbyId, long revision,
        Guid offerId, bool accept)
    {
        Guid id = ValidateLobbyId(lobbyId);
        long expectedRevision = ValidateRevision(revision);
        if (offerId == Guid.Empty)
            throw new ArgumentException("A seat offer identity is required.", nameof(offerId));
        return accept
            ? new LobbyQueueAccept(id, expectedRevision, offerId)
            : new LobbyQueueDecline(id, expectedRevision, offerId);
    }

    internal static LobbyRequestTeam CreateTeamCommand(byte team, long revision)
    {
        if (team >= MatchRules.MaximumTeamCount)
            throw new ArgumentOutOfRangeException(nameof(team), "Team must be 0 through 3.");
        return new(team, ValidateRevision(revision));
    }

    internal static LobbyChat CreateChatCommand(long revision, string text)
    {
        ValidateChatText(text);
        return new(text, ValidateRevision(revision));
    }

    internal static LobbyConfigure CreateStructuredConfigureCommand(long revision,
        string mapKey, MatchMode mode, int botCount, LobbyRulesOptions rules,
        BotDifficulty botDifficulty = BotDifficulty.Normal)
    {
        if (rules is null) throw new ArgumentNullException(nameof(rules));
        if (!Enum.IsDefined(botDifficulty))
            throw new ArgumentOutOfRangeException(nameof(botDifficulty));
        return new(ValidateRevision(revision), ValidateMapKey(mapKey), mode, botCount,
            Rules: rules, BotDifficulty: botDifficulty);
    }

    internal static LobbyConfigure CreateLegacyConfigureCommand(long revision,
        string mapKey, MatchMode mode, int botCount, int? timeLimitSeconds,
        int? pointGoal)
        => new(ValidateRevision(revision), ValidateMapKey(mapKey), mode, botCount,
            timeLimitSeconds, pointGoal);

    internal async Task RunEntryAsync(Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using CancellationTokenSource entry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        entry.CancelAfter(TimeSpan.FromSeconds(25));
        if (!await _entryOperation.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        lock (_stateLock) _activeEntry = entry;
        Publish(State with { Loading = true, Message = "Finding a game…" });
        try { await action(entry.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            Publish(State with { Loading = false, Message = cancellationToken.IsCancellationRequested
                || _lifetime.IsCancellationRequested ? "Connection cancelled. Try again when ready."
                : "Connection timed out or was cancelled. Try again." });
        }
        catch (TimeoutException)
        {
            Publish(State with { Loading = false, Message = "The server did not respond in time. Try again." });
        }
        finally
        {
            lock (_stateLock) _activeEntry = null;
            Publish(State with { Loading = false });
            _entryOperation.Release();
        }
    }

    internal static async Task<LobbyListSnapshot> LoadBrowseLobbiesAsync(
        Func<int, CancellationToken, Task<LobbyListSnapshot>> readPage, CancellationToken token)
    {
        var entries = ImmutableArray.CreateBuilder<LobbyListEntry>();
        var seen = new HashSet<Guid>();
        int offset = 0;
        for (int page = 0; page < 64; page++)
        {
            token.ThrowIfCancellationRequested();
            LobbyListSnapshot list = await readPage(offset, token).ConfigureAwait(false);
            foreach (LobbyListEntry lobby in list.Lobbies)
                if (seen.Add(lobby.LobbyId)) entries.Add(lobby);
            if (list.NextOffset is not { } next || next <= offset)
                return new(entries.ToImmutable(), null);
            offset = next;
        }
        return new(entries.ToImmutable(), offset);
    }

    internal static async Task<bool> JoinQuickPlayWithRetryAsync(
        Func<int, CancellationToken, Task<LobbyListSnapshot>> readPage,
        Func<LobbyListEntry, CancellationToken, Task<NodeControlEvent>> join, CancellationToken token)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            LobbyListEntry? lobby = await FindQuickPlayLobbyAsync(readPage, token).ConfigureAwait(false);
            if (lobby == null) return false;
            NodeControlEvent response = await join(lobby, token).ConfigureAwait(false);
            if (response.Type == "error" && response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Code
                is "stale_revision" or "capacity" or "full" or "phase" or "not_found") continue;
            RequireResponse(response, "lobby.snapshot");
            return true;
        }
        return false;
    }

    internal static async Task<LobbyListEntry?> FindQuickPlayLobbyAsync(
        Func<int, CancellationToken, Task<LobbyListSnapshot>> readPage, CancellationToken cancellationToken)
    {
        int offset = 0;
        for (int page = 0; page < 64; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LobbyListSnapshot list = await readPage(offset, cancellationToken).ConfigureAwait(false);
            LobbyListEntry? lobby = list.Lobbies.FirstOrDefault(IsQuickPlayEligible);
            if (lobby != null) return lobby;
            if (list.NextOffset is not { } next || next <= offset) return null;
            offset = next;
        }
        return null;
    }

    private static async Task<LobbyListSnapshot> ReadLobbyPageAsync(NodeControlClient node, int offset,
        CancellationToken cancellationToken)
    {
        NodeControlEvent response = await node.SendAndWaitAsync("lobby.list", new LobbyList(offset, 16),
            cancellationToken).ConfigureAwait(false);
        RequireResponse(response, "lobby.list");
        return response.Payload.Deserialize(NodeJsonContext.Default.LobbyListSnapshot)
            ?? throw new InvalidOperationException("The server returned an invalid lobby list.");
    }

    private static void RequireResponse(NodeControlEvent response, string expected)
    {
        if (response.Type == "error") throw new InvalidOperationException(
            PrimeRoutePresentation.PlayerFacingNetworkError(
                response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Message,
                "The server rejected the request."));
        if (response.Type != expected) throw new InvalidOperationException("The server returned an unexpected response.");
    }

    public async Task<bool> ConnectNodeAsync(NodeListing node,
        CancellationToken cancellationToken = default)
    {
        NodeConnectOutcome outcome = await ConnectNodeAttemptAsync(node, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Connected)
            NodeHealthCache.Shared.RecordSuccess(node.NodeId, _time.GetUtcNow());
        return outcome.Connected;
    }

    private async Task<NodeConnectOutcome> ConnectNodeAttemptAsync(NodeListing node,
        CancellationToken cancellationToken, long? expectedGeneration = null,
        CancellationToken? externalCancellation = null)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            long generation = expectedGeneration ?? Interlocked.Increment(ref _generation);
            if (expectedGeneration is { } expected
                && expected != Interlocked.Read(ref _generation))
                return new(NodeConnectFailureKind.Stale);
            // A directory selection replaces the current control session. An
            // older Worker handoff must be invalidated before the new Node is
            // observed, otherwise its completion can occupy the gate and race
            // the replacement session.
            _handoff.Cancel();
            ClearSelectedNodeCatalog();
            _online.ReleaseMatch(dispose: true);
            NetSession.Stop();
            AccountSession account = await RequireAccountAsync(cancellationToken).ConfigureAwait(false);
            if (expectedGeneration is { } accountExpected
                && accountExpected != Interlocked.Read(ref _generation))
                return new(NodeConnectFailureKind.Stale);
            Publish(State with { Phase = PlayPhase.LoadingNodes, Loading = true,
                Message = $"Connecting to {node.Name}…" });
            NodeControlClient connected = await NodeSessions.ConnectAsync(account, node,
                cancellationToken).ConfigureAwait(false);
            if (expectedGeneration is { } connectedExpected
                && connectedExpected != Interlocked.Read(ref _generation))
            {
                if (ReferenceEquals(_online.Node, connected))
                    await NodeSessions.DisconnectAsync().ConfigureAwait(false);
                return new(NodeConnectFailureKind.Stale);
            }
            Publish(State with { BrowsedLobbies = null });
            Observe(connected);
            _connectedNodeName = node.Name;
            _connectedNodeRegion = node.Region;
            _shell.SetNodeStatus(true, node.Name, node.Region);
            Publish(State with { Phase = PlayPhase.Connected, Loading = false,
                Message = $"Connected to {node.Name}." });
            return new(NodeConnectFailureKind.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            && (externalCancellation is null || externalCancellation.Value.IsCancellationRequested)) { throw; }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            // An internal Quick Play deadline is a transient candidate failure,
            // not a user cancellation. Let the aggregate owner publish the one
            // final outcome instead of escaping into RunEntryAsync's generic
            // cancellation message.
            return new(NodeConnectFailureKind.Transient, error);
        }
        catch (Exception error)
        {
            // Quick Play owns the aggregate outcome. Keeping candidate
            // failures out of the shell prevents three transient attempts
            // from producing three terminal notifications and leaves the
            // final classification to EnsureNodeForQuickPlayAsync.
            if (expectedGeneration is null)
            {
                string message = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                    "Could not connect to the server. Try again.");
                Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = message });
                _shell.Notify(PrimeNotificationKind.Error, message);
            }
            return new(ClassifyNodeConnectFailure(error, cancellationToken), error);
        }
        finally { _operation.Release(); }
    }

    internal static NodeConnectFailureKind ClassifyNodeConnectFailure(Exception error,
        CancellationToken cancellationToken)
    {
        if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            return NodeConnectFailureKind.Transient;
        if (ContainsSecurityFailure(error)) return NodeConnectFailureKind.Permanent;
        if (error is AccountServiceException accountService)
            return IsTransientAccountFailure(accountService.Kind)
                ? NodeConnectFailureKind.Transient : NodeConnectFailureKind.Permanent;
        if (error is NodeControlConnectException nodeConnect)
            return nodeConnect.Failure == NodeControlConnectFailure.RateLimited
                ? NodeConnectFailureKind.Transient : NodeConnectFailureKind.Permanent;
        if (error is ArgumentException or JsonException
            or InvalidDataException or FormatException or NotSupportedException
            or InvalidOperationException)
            return NodeConnectFailureKind.Permanent;
        if (error is WebSocketException webSocket
            && webSocket.WebSocketErrorCode is WebSocketError.InvalidMessageType
                or WebSocketError.NotAWebSocket or WebSocketError.HeaderError
                or WebSocketError.UnsupportedVersion or WebSocketError.UnsupportedProtocol)
            return NodeConnectFailureKind.Permanent;
        if (error is HttpRequestException or IOException or SocketException
            or TimeoutException or WebSocketException)
            return NodeConnectFailureKind.Transient;
        return NodeConnectFailureKind.Permanent;
    }

    internal static bool IsTransientAccountFailure(AccountFailureKind kind)
        => kind is AccountFailureKind.RateLimited
            or AccountFailureKind.ServiceUnavailable
            or AccountFailureKind.TransportUnavailable
            or AccountFailureKind.Timeout;

    private static bool ContainsSecurityFailure(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is AuthenticationException or SecurityException)
                return true;
        return false;
    }

    public async Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _generation);
            _handoff.Cancel();
            CancelMapPreparation();
            if (_online.Match == null) NetSession.Stop();
            NodeControlClient session = await _online.ResumeAsync(cancellationToken)
                .ConfigureAwait(false);
            Observe(session);
            _shell.SetNodeStatus(true, _connectedNodeName, _connectedNodeRegion);
            Publish(State with { Phase = PlayPhase.Connected, Loading = false,
                Message = "Connection restored." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Publish(State with { Phase = PlayPhase.Error, Loading = false,
                Message = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                    "Could not restore the server connection. Try again.") });
            return false;
        }
        finally { _operation.Release(); }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        _directoryFetchedAt = null;
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        CancelMapPreparation();
        ClearSelectedNodeCatalog();
        _connectedNodeName = "";
        _connectedNodeRegion = "";
        await NodeSessions.DisconnectAsync().ConfigureAwait(false);
        Observe(null);
        _shell.SetNodeStatus(false);
        Publish(State with { Phase = PlayPhase.Nodes, Node = null, BrowsedLobbies = null, Loading = false,
            Message = "Server disconnected." });
    }

    public Task RefreshLobbiesAsync(CancellationToken cancellationToken = default)
        => SendAsync("lobby.list", new LobbyList(), cancellationToken);

    /// <summary>Creates a public lobby with the legacy default seat layout.</summary>
    public Task CreateLobbyAsync(string name, CancellationToken cancellationToken = default)
        => CreateLobbyAsync(name, 8, 16, LobbySeatPolicy.ImmediateSeat, cancellationToken);

    /// <summary>Creates a public lobby with explicit player/observer capacity.</summary>
    public Task CreateLobbyAsync(string name, int playerLimit, int observerLimit,
        LobbySeatPolicy seatPolicy = LobbySeatPolicy.ImmediateSeat,
        CancellationToken cancellationToken = default)
        => CreateLobbyCoreAsync(name, playerLimit, observerLimit, seatPolicy,
            DuelQueuePolicy.Fifo, cancellationToken);

    /// <summary>Advanced overload retaining the Node's queue-policy field.</summary>
    public Task CreateLobbyAsync(string name, int playerLimit, int observerLimit,
        LobbySeatPolicy seatPolicy, DuelQueuePolicy duelQueuePolicy,
        CancellationToken cancellationToken = default)
        => CreateLobbyCoreAsync(name, playerLimit, observerLimit, seatPolicy,
            duelQueuePolicy, cancellationToken);

    public Task JoinLobbyAsync(Guid lobbyId, long revision, bool observer = false,
        CancellationToken cancellationToken = default)
        => SendExpectedSnapshotAsync("lobby.join",
            new LobbyJoin(ValidateLobbyId(lobbyId), ValidateRevision(revision), observer), cancellationToken);

    /// <summary>Explicit observer spelling for callers that do not use a role bool.</summary>
    public Task JoinObserverAsync(Guid lobbyId, long revision,
        CancellationToken cancellationToken = default)
        => JoinLobbyAsync(lobbyId, revision, observer: true, cancellationToken: cancellationToken);

    /// <summary>Compatibility spelling for the observer entry action.</summary>
    public Task JoinLobbyAsObserverAsync(Guid lobbyId, long revision,
        CancellationToken cancellationToken = default)
        => JoinObserverAsync(lobbyId, revision, cancellationToken);

    /// <summary>
    /// Joins the Node-owned player waitlist. The supplied lobby identity and
    /// revision are always carried on the command, including for a queued-only
    /// session that has no member seat in that lobby.
    /// </summary>
    public Task JoinWaitlistAsync(Guid lobbyId, long revision,
        LobbyQueueRequestedRole requestedRole = LobbyQueueRequestedRole.Player,
        byte? requestedTeam = null, CancellationToken cancellationToken = default)
    {
        LobbyQueueJoin command = CreateWaitlistJoinCommand(lobbyId, revision, requestedRole, requestedTeam);
        return SendExpectedSnapshotAsync("lobby.queue.join", command, cancellationToken);
    }

    /// <summary>Compatibility spelling for joining the lobby queue.</summary>
    public Task JoinQueueAsync(Guid lobbyId, long revision,
        LobbyQueueRequestedRole requestedRole = LobbyQueueRequestedRole.Player,
        byte? requestedTeam = null, CancellationToken cancellationToken = default)
        => JoinWaitlistAsync(lobbyId, revision, requestedRole, requestedTeam, cancellationToken);

    /// <summary>Leaves a Node-owned waitlist using its current authoritative revision.</summary>
    public Task LeaveWaitlistAsync(Guid lobbyId, long revision,
        CancellationToken cancellationToken = default)
        => SendExpectedSnapshotAsync("lobby.queue.leave",
            CreateWaitlistLeaveCommand(lobbyId, revision), cancellationToken);

    /// <summary>Compatibility spelling for leaving the lobby queue.</summary>
    public Task LeaveQueueAsync(Guid lobbyId, long revision,
        CancellationToken cancellationToken = default)
        => LeaveWaitlistAsync(lobbyId, revision, cancellationToken);

    /// <summary>
    /// Accepts exactly one server-issued seat offer. A stale revision or offer
    /// is surfaced to the caller; acceptance is never retried implicitly.
    /// </summary>
    public Task AcceptWaitlistAsync(Guid lobbyId, long revision, Guid offerId,
        CancellationToken cancellationToken = default)
        => SendExpectedSnapshotAsync("lobby.queue.accept",
            CreateWaitlistOfferCommand(lobbyId, revision, offerId, accept: true), cancellationToken);

    /// <summary>Compatibility spelling for accepting a queue seat offer.</summary>
    public Task AcceptQueueAsync(Guid lobbyId, long revision, Guid offerId,
        CancellationToken cancellationToken = default)
        => AcceptWaitlistAsync(lobbyId, revision, offerId, cancellationToken);

    /// <summary>Explicit spelling for accepting a waitlist seat offer.</summary>
    public Task AcceptWaitlistOfferAsync(Guid lobbyId, long revision, Guid offerId,
        CancellationToken cancellationToken = default)
        => AcceptWaitlistAsync(lobbyId, revision, offerId, cancellationToken);

    /// <summary>
    /// Declines exactly one server-issued seat offer. A stale revision or offer
    /// is surfaced to the caller; decline is never retried implicitly.
    /// </summary>
    public Task DeclineWaitlistAsync(Guid lobbyId, long revision, Guid offerId,
        CancellationToken cancellationToken = default)
        => SendExpectedSnapshotAsync("lobby.queue.decline",
            CreateWaitlistOfferCommand(lobbyId, revision, offerId, accept: false), cancellationToken);

    /// <summary>Compatibility spelling for declining a queue seat offer.</summary>
    public Task DeclineQueueAsync(Guid lobbyId, long revision, Guid offerId,
        CancellationToken cancellationToken = default)
        => DeclineWaitlistAsync(lobbyId, revision, offerId, cancellationToken);

    /// <summary>Explicit spelling for declining a waitlist seat offer.</summary>
    public Task DeclineWaitlistOfferAsync(Guid lobbyId, long revision, Guid offerId,
        CancellationToken cancellationToken = default)
        => DeclineWaitlistAsync(lobbyId, revision, offerId, cancellationToken);

    public async Task LeaveLobbyAsync(CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        if (!ReferenceEquals(_observed, node))
            throw new InvalidOperationException("The selected server connection is no longer current.");
        LobbySnapshot lobby = node.Lobby
            ?? throw new InvalidOperationException("Join a lobby first.");
        Guid lobbyId = lobby.LobbyId;
        lock (_stateLock)
        {
            if (_leavingLobbyId.HasValue)
                throw new InvalidOperationException("A lobby leave is already in progress.");
            _leavingLobbyId = lobbyId;
        }

        // Explicit leave owns the complete gameplay teardown.  A handoff gate
        // may already be completed by the time the user leaves, so the normal
        // conditional CancelPendingHandoff path is not sufficient here.
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        _online.ReleaseMatch(dispose: true);
        NetSession.Stop();
        try
        {
            await LeaveLobbyWithRetryAsync(() => node.Lobby,
                (command, token) => node.SendAndWaitAsync("lobby.leave", command, token),
                cancellationToken).ConfigureAwait(false);
            // The public directory is a pull snapshot. Once this membership
            // changes, neither its entries nor their revisions are current.
            Publish(State with { BrowsedLobbies = null });
        }
        finally
        {
            lock (_stateLock)
            {
                if (_leavingLobbyId == lobbyId) _leavingLobbyId = null;
            }
        }
    }

    internal static async Task LeaveLobbyWithRetryAsync(Func<LobbySnapshot?> currentLobby,
        Func<LobbyLeave, CancellationToken, Task<NodeControlEvent>> send,
        CancellationToken cancellationToken = default)
    {
        LobbySnapshot lobby = currentLobby() ?? throw new InvalidOperationException("Join a lobby first.");
        Guid lobbyId = lobby.LobbyId;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            NodeControlEvent response = await send(new LobbyLeave(lobby.Revision), cancellationToken)
                .ConfigureAwait(false);
            if (response.Type == "lobby.left")
            {
                if (response.Payload.Deserialize(NodeJsonContext.Default.LobbyLeft)?.LobbyId != lobbyId)
                    throw new InvalidOperationException("The server confirmed leaving a different lobby.");
                return;
            }
            if (response.Type != "error")
                throw new InvalidOperationException("The server did not confirm leaving the lobby.");
            NodeControlError error = response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)
                ?? new NodeControlError("rejected", "The server rejected leaving the lobby.");
            if (error.Code != "stale_revision" || attempt != 0)
                throw new InvalidOperationException(PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                    "The server rejected leaving the lobby. Try again."));
            LobbySnapshot? latest = currentLobby();
            if (latest == null) return; // Membership was removed while the rejected request was in flight.
            if (latest.LobbyId != lobbyId)
                throw new InvalidOperationException("Lobby membership changed before leaving was confirmed.");
            lobby = latest;
        }
    }

    public Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
    {
        if (ready && State.Lobby?.RequiredMap is { } required
            && !_mapAcquisition.IsInstalled(required))
            throw new InvalidOperationException(
                "Download and prepare the exact required map before becoming Ready.");
        return SendLobbyCommandAsync((lobby, _) => new LobbySetReady(ready, lobby.Revision),
            "lobby.ready.set", cancellationToken);
    }

    public bool IsRequiredMapInstalled(MapRequirement requirement)
        => _mapAcquisition.IsInstalled(requirement);

    public async Task AcquireRequiredMapAsync(CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        MapRequirement requirement = node.Lobby?.RequiredMap
            ?? throw new InvalidOperationException(
                "This lobby does not require a downloadable map.");
        string endpoint = node.Endpoint
            ?? throw new InvalidOperationException("The server connection is unavailable.");
        long generation = Interlocked.Read(ref _generation);
        Task<InstalledMap> preparation = StartMapPreparation(node, requirement, endpoint,
            generation);
        await preparation.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnsureMapPreparationCurrent(node, requirement, generation);
        Publish(State with { Loading = false,
            Message = $"{requirement.StableId} {requirement.Version} is ready." });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Requests a team assignment using the revision from the latest lobby
    /// snapshot. The Node remains authoritative and may reject the request or
    /// allocate a different team at the next transition.
    /// </summary>
    public Task RequestTeamAsync(byte team, CancellationToken cancellationToken = default)
    {
        if (team >= MatchRules.MaximumTeamCount)
            throw new ArgumentOutOfRangeException(nameof(team), "Team must be 0 through 3.");
        return SendLobbyCommandAndWaitAsync((lobby, _) => CreateTeamCommand(team, lobby.Revision),
            "lobby.team.request", cancellationToken);
    }

    /// <summary>
    /// Sends one lobby chat command. Chat is bounded in UTF-8 bytes to match
    /// the Node's 256-byte validation; unlike idempotent directory reads, a
    /// chat command is never retried after a stale revision or timeout.
    /// </summary>
    public Task SendLobbyChatAsync(string text, CancellationToken cancellationToken = default)
    {
        ValidateChatText(text);
        return SendLobbyCommandAndWaitAsync((lobby, _) => CreateChatCommand(lobby.Revision, text),
            "lobby.chat", cancellationToken);
    }

    /// <summary>Match hunter is lobby state; it never updates the profile favorite.</summary>
    public Task SelectLobbyHunterAsync(Hunter hunter, CancellationToken cancellationToken = default)
    {
        if (hunter is < Hunter.Samus or > Hunter.Weavel)
            throw new ArgumentOutOfRangeException(nameof(hunter));
        Publish(State with { LobbyHunter = hunter });
        return SendLobbyCommandAndWaitAsync((lobby, _) => new LobbySelectHunter(hunter, lobby.Revision),
            "lobby.hunter.select", cancellationToken);
    }

    /// <summary>Submits an explicit catalog-resolved loadout for the current
    /// lobby Hunter. This is identical for registered and guest Node sessions;
    /// callers must not silently substitute a guest selection after an account
    /// load failure.</summary>
    public Task SelectLobbyCosmeticsAsync(CosmeticLoadoutIds cosmetics,
        CancellationToken cancellationToken = default)
    {
        Hunter hunter = State.LobbyHunter;
        if (!CosmeticCatalog.BuiltIn.IsValid(cosmetics, hunter))
            throw new ArgumentException("The cosmetic loadout is not valid for the selected Hunter.",
                nameof(cosmetics));
        return SendLobbyCommandAndWaitAsync((lobby, session) =>
        {
            LobbyMember member = lobby.Members.Single(value => value.SessionId == session?.SessionId);
            if (member.Hunter != hunter)
                throw new InvalidOperationException("Wait for the authoritative Hunter selection before equipping cosmetics.");
            return new LobbySelectCosmetics(cosmetics, lobby.Revision);
        }, "lobby.cosmetics.select", cancellationToken);
    }

    /// <summary>
    /// Legacy lobby configuration overload. The legacy time/point fields stay
    /// on the wire so older Nodes can consume this call; the server normalizes
    /// them into its canonical <see cref="LobbyRulesOptions"/> representation.
    /// </summary>
    public Task ConfigureLobbyAsync(string mapKey, MatchMode mode, int botCount,
        int? timeLimitSeconds, int? pointGoal,
        CancellationToken cancellationToken = default)
        => ConfigureLobbyCoreAsync(mapKey, mode, botCount, null,
            BotDifficulty.Normal, timeLimitSeconds, pointGoal, false,
            cancellationToken);

    /// <summary>Configures a lobby with the structured authoritative rule set.</summary>
    public Task ConfigureLobbyAsync(string mapKey, MatchMode mode, int botCount,
        LobbyRulesOptions? rules, CancellationToken cancellationToken = default)
        => ConfigureLobbyCoreAsync(mapKey, mode, botCount, rules,
            BotDifficulty.Normal, null, null, true, cancellationToken);

    /// <summary>Configures bot population and difficulty as one frozen lobby setting.</summary>
    public Task ConfigureLobbyAsync(string mapKey, MatchMode mode, int botCount,
        BotDifficulty botDifficulty, LobbyRulesOptions? rules,
        CancellationToken cancellationToken = default)
        => ConfigureLobbyCoreAsync(mapKey, mode, botCount, rules,
            botDifficulty, null, null, true, cancellationToken);

    /// <summary>Convenience overload for a structured rule set with no bots.</summary>
    public Task ConfigureLobbyAsync(string mapKey, MatchMode mode,
        LobbyRulesOptions? rules, CancellationToken cancellationToken = default)
        => ConfigureLobbyAsync(mapKey, mode, 0, rules, cancellationToken);

    private async Task ConfigureLobbyCoreAsync(string mapKey, MatchMode mode, int botCount,
        LobbyRulesOptions? rules, BotDifficulty botDifficulty,
        int? legacyTimeLimitSeconds, int? legacyPointGoal,
        bool structured, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mapKey))
            throw new ArgumentException("Choose a map hosted by this server and installed locally.", nameof(mapKey));
        if (mapKey.Any(c => c is < ' ' or > '~') || mapKey.Length > 128)
            throw new ArgumentException("Choose a map hosted by this server and installed locally.", nameof(mapKey));
        MapCatalogSnapshot catalog = GetMapCatalogSnapshot();
        if (catalog.State != NodeMapCatalogState.Available
            || !catalog.Available.Contains(mapKey, StringComparer.Ordinal))
        {
            throw catalog.State switch
            {
                NodeMapCatalogState.Unknown => new InvalidOperationException(MapCatalogMessageFor(NodeMapCatalogState.Unknown)),
                NodeMapCatalogState.Empty => new InvalidOperationException(MapCatalogMessageFor(NodeMapCatalogState.Empty)),
                NodeMapCatalogState.Disjoint => new InvalidOperationException(MapCatalogMessageFor(NodeMapCatalogState.Disjoint)),
                _ => new ArgumentException("Choose a map hosted by this server and installed locally.", nameof(mapKey))
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
            LobbyRulesOptions requestedRules = NormalizeLobbyRules(mode, rules,
                legacyTimeLimitSeconds, legacyPointGoal);
            ValidateLobbyConfiguration(lobby, mode, botCount, botDifficulty,
                requestedRules);
            LobbyRulesOptions currentRules = NormalizeLobbyRules(lobby.Mode, lobby.Rules,
                lobby.TimeLimitSeconds, lobby.PointGoal);
            if (StringComparer.Ordinal.Equals(lobby.MapKey, mapKey) && lobby.Mode == mode
                && lobby.BotCount == botCount
                && lobby.BotDifficulty == botDifficulty
                && currentRules == requestedRules) return;

            LobbyConfigure configure = structured
                ? CreateStructuredConfigureCommand(lobby.Revision, mapKey, mode,
                    botCount, requestedRules, botDifficulty)
                : CreateLegacyConfigureCommand(lobby.Revision, mapKey, mode, botCount,
                    legacyTimeLimitSeconds, legacyPointGoal);

            NodeControlEvent response = await node.SendAndWaitAsync("lobby.configure",
                configure, cancellationToken).ConfigureAwait(false);
            if (response.Type == "error")
            {
                NodeControlError error = response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)
                    ?? new NodeControlError("rejected", "The server rejected the lobby settings.");
                throw new InvalidOperationException(PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                    "The server rejected the lobby settings. Try again."));
            }
            if (response.Type != "lobby.snapshot")
                throw new InvalidOperationException("The server returned an invalid lobby settings response.");
            LobbySnapshot applied = response.Payload.Deserialize(NodeJsonContext.Default.LobbySnapshot)
                ?? throw new InvalidOperationException("The server returned an invalid lobby snapshot.");
            LobbyRulesOptions appliedRules = NormalizeLobbyRules(applied.Mode, applied.Rules,
                applied.TimeLimitSeconds, applied.PointGoal);
            if (applied.LobbyId != lobby.LobbyId || applied.Revision <= lobby.Revision
                || !StringComparer.Ordinal.Equals(applied.MapKey, mapKey) || applied.Mode != mode
                || applied.BotCount != botCount
                || applied.BotDifficulty != botDifficulty
                || appliedRules != requestedRules
                || node.Session?.SessionId != session.SessionId)
                throw new InvalidOperationException("The lobby changed before these settings were applied. Try again.");
        }
        finally { _configureOperation.Release(); }
    }

    internal static void ValidateLobbyConfiguration(LobbySnapshot lobby, MatchMode mode,
        int botCount, int? timeLimitSeconds, int? pointGoal)
        => ValidateLobbyConfiguration(lobby, mode, botCount,
            NormalizeLobbyRules(mode, null, timeLimitSeconds, pointGoal));

    internal static void ValidateLobbyConfiguration(LobbySnapshot lobby, MatchMode mode,
        int botCount, LobbyRulesOptions? rules)
        => ValidateLobbyConfiguration(lobby, mode, botCount, BotDifficulty.Normal,
            rules);

    internal static void ValidateLobbyConfiguration(LobbySnapshot lobby, MatchMode mode,
        int botCount, BotDifficulty botDifficulty, LobbyRulesOptions? rules)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(botDifficulty))
            throw new ArgumentOutOfRangeException(nameof(botDifficulty));
        LobbyRulesOptions normalized = NormalizeLobbyRules(mode, rules, null, null);
        int humanPlayers = lobby.Members.Count(member => !member.Observer);
        if (botCount < 0 || botCount + humanPlayers > lobby.PlayerLimit)
            throw new ArgumentOutOfRangeException(nameof(botCount),
                "Bots cannot exceed the lobby's remaining player capacity.");
        _ = normalized;
    }

    public Task StartMatchAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAndWaitAsync((lobby, session) =>
        {
            if (lobby.OwnerSessionId != session?.SessionId)
                throw new InvalidOperationException("Only the lobby owner can start a match.");
            return new LobbyStart(lobby.Revision);
        }, "lobby.start", cancellationToken);

    public async Task CastPostMatchVoteAsync(byte optionId, CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        NodeRoundSnapshot round = node.Round ?? throw new InvalidOperationException("No post-match vote is available.");
        if (round.OwnVote != 0 || round.ResolvedOption != null || round.VoteDeadline <= DateTimeOffset.UtcNow
            || !round.Options.Any(option => option.Id == optionId))
            throw new InvalidOperationException("This vote is no longer available.");
        NodeControlEvent response = await node.SendAndWaitAsync("lobby.vote.cast",
            new LobbyVoteCast(node.Lobby!.Revision, round.BallotRevision, optionId), cancellationToken)
            .ConfigureAwait(false);
        if (response.Type == "error")
            throw new InvalidOperationException(PrimeRoutePresentation.PlayerFacingNetworkError(
                response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Message,
                "The server rejected the vote."));
        if (response.Type != "lobby.round") throw new InvalidOperationException("The server did not confirm the vote.");
    }

    public Task RematchAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, _) => new LobbyRematch(lobby.Revision), "lobby.rematch", cancellationToken);

    /// <summary>Proposes a Node-owned restart ballot for the active match.</summary>
    public Task RequestRestartMatchAsync(CancellationToken cancellationToken = default)
        => RequestTransitionAsync(MatchTransitionChoice.Restart, null,
            cancellationToken);

    /// <summary>Proposes a Node-owned map-change ballot for the active match.</summary>
    public Task RequestChangeMapAsync(string mapKey,
        CancellationToken cancellationToken = default)
    {
        if (String.IsNullOrWhiteSpace(mapKey))
            throw new ArgumentException("Choose a map before proposing a change.", nameof(mapKey));
        if (!AvailableTransitionMaps.Contains(mapKey, StringComparer.Ordinal))
            throw new ArgumentException("Choose a map hosted by this server and installed locally.",
                nameof(mapKey));
        return RequestTransitionAsync(MatchTransitionChoice.ChangeMap, mapKey,
            cancellationToken);
    }

    /// <summary>Changes this player's frozen selection for the next match.</summary>
    public Task RequestHunterChangeAsync(Hunter hunter,
        CancellationToken cancellationToken = default)
        => SelectLobbyHunterAsync(hunter, cancellationToken);

    /// <summary>Casts the local yes/no response for the active transition ballot.</summary>
    public Task RequestTransitionVoteAsync(bool accept,
        CancellationToken cancellationToken = default)
    {
        NodeMatchTransitionVoteSnapshot? ballot = TransitionVote;
        if (!TransitionMenuSupported
            || ballot is not { State: MatchTransitionVoteState.Pending })
            throw new InvalidOperationException("No active match transition vote is available.");
        return CastTransitionVoteAsync(ballot, accept, cancellationToken);
    }

    private async Task RequestTransitionAsync(MatchTransitionChoice choice,
        string? mapKey, CancellationToken cancellationToken)
    {
        if (!TransitionMenuSupported)
            throw new InvalidOperationException(
                "Match restart and map change are unavailable for this session.");
        NodeControlClient node = RequireConnected();
        LobbySnapshot lobby = node.Lobby
            ?? throw new InvalidOperationException("Join a lobby first.");
        Guid matchId = CurrentTransitionMatch(node, lobby);
        NodeMatchTransitionVoteSnapshot? existing = node.TransitionVoteFor(matchId);
        if (existing is { State: MatchTransitionVoteState.Pending })
            throw new InvalidOperationException("A match transition vote is already active.");
        LobbyMatchTransitionPropose command = new(lobby.Revision, matchId, choice, mapKey);
        await ExecuteTransitionCommandAsync(node, "match.transition.propose", command,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CastTransitionVoteAsync(NodeMatchTransitionVoteSnapshot ballot,
        bool accept, CancellationToken cancellationToken)
    {
        NodeControlClient node = RequireConnected();
        LobbySnapshot lobby = node.Lobby
            ?? throw new InvalidOperationException("Join a lobby first.");
        Guid matchId = CurrentTransitionMatch(node, lobby);
        if (ballot.MatchId != matchId)
            throw new InvalidOperationException("This transition vote belongs to an old match.");
        LobbyMatchTransitionVote command = new(lobby.Revision, matchId,
            ballot.BallotRevision, accept);
        await ExecuteTransitionCommandAsync(node, "match.transition.vote", command,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteTransitionCommandAsync<T>(NodeControlClient node,
        string type, T command, CancellationToken cancellationToken) where T : NodeCommand
    {
        await _transitionOperation.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _transitionRequestInFlight, 1);
        Volatile.Write(ref _transitionError, null);
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            NodeControlEvent response = await node.SendAndWaitAsync(type, command,
                cancellationToken).ConfigureAwait(false);
            if (response.Type == "error")
            {
                NodeControlError error = response.Payload.Deserialize(
                    NodeJsonContext.Default.NodeControlError)
                    ?? new NodeControlError("rejected", "The server rejected the transition request.");
                throw new InvalidOperationException(PrimeRoutePresentation
                    .PlayerFacingNetworkError(error.Message,
                        "The server rejected the transition request. Try again."));
            }
            if (response.Type != "match.transition.state")
                throw new InvalidOperationException(
                    "The server did not confirm the transition request.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            string message = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                "The transition request could not be sent. Try again.");
            Volatile.Write(ref _transitionError, message);
            Publish(State with { Message = message });
            throw new InvalidOperationException(message, error);
        }
        finally
        {
            Interlocked.Exchange(ref _transitionRequestInFlight, 0);
            _transitionOperation.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsTransitionParticipant()
    {
        NodeControlClient? node = _online.Node;
        LobbySnapshot? lobby = node?.Lobby;
        NodeSessionSnapshot? session = node?.Session;
        if (node is not { Connected: true } || lobby == null || session == null
            || lobby.Phase != LobbyPhase.InMatch
            || lobby.Members.FirstOrDefault(member => member.SessionId == session.SessionId)
                is not { Observer: false }
            || State.Round?.TournamentId != null
            || ReplayPlayback.IsActive || ReplayPlayback.IsModern
            || _online.Match?.Play.IsObserver == true)
            return false;
        return true;
    }

    private static Guid CurrentTransitionMatch(NodeControlClient node, LobbySnapshot lobby)
        => node.Handoff?.MatchId ?? lobby.CurrentMatchId
            ?? node.State.JoinedMatchId
            ?? throw new InvalidOperationException("The active match identity is unavailable.");

    public Task ReturnToLobbyAsync(CancellationToken cancellationToken = default)
        => SendLobbyCommandAsync((lobby, _) => new LobbyReturn(lobby.Revision), "lobby.return", cancellationToken);

    public Task RejoinWorkerAsync(CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        NodeMatchHandoff handoff = node.Handoff
            ?? throw new InvalidOperationException("No match connection is available to rejoin.");
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        NetSession.Stop();
        return node.SendAsync("match.rejoin", new NodeMatchRejoin(handoff.MatchId), cancellationToken);
    }

    public async Task<bool> RetryHandoffAsync(CancellationToken cancellationToken = default)
    {
        NodeControlClient node = RequireConnected();
        NodeMatchHandoff handoff = node.Handoff
            ?? throw new InvalidOperationException("No match connection is available.");
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
        lock (_stateLock) _activeEntry?.Cancel();
        _directoryFetchedAt = null;
        _resumeAttempted = null;
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        CancelMapPreparation();
        _online.ReleaseMatch(dispose: true);
        NetSession.Stop();
    }

    /// <summary>
    /// Cancels the currently visible Quick Play/Browse/Host entry operation.
    /// This only cancels that operation's linked token. In particular, it does
    /// not disconnect the persistent Node session or stop an active lobby.
    /// </summary>
    public bool CancelEntry()
    {
        CancellationTokenSource? entry;
        lock (_stateLock) entry = _activeEntry;
        if (entry == null) return false;
        try
        {
            if (entry.IsCancellationRequested) return false;
            entry.Cancel();
        }
        catch (ObjectDisposedException) { return false; }
        Publish(State with { Loading = false, Message = "Connection cancelled. Try again when ready." });
        return true;
    }

    public void CancelPendingHandoff()
    {
        if (!_handoff.IsActive) return;
        Interlocked.Increment(ref _generation);
        _handoff.Cancel();
        CancelMapPreparation();
        _online.ReleaseMatch(dispose: true);
        NetSession.Stop();
        Publish(State with { Loading = false, Message = "Match connection canceled." });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation);
        _lifetime.Cancel();
        CancelMapPreparation();
        _handoff.Cancel();
        _online.ReleaseMatch(dispose: true);
        NodeSessions.CurrentChanged -= NodeSessionChanged;
        LauncherPrefs.ShowOnlinePresenceChanged -= PresencePreferenceChanged;
        SetPresenceRefreshEnabled(false);
        Observe(null);
        Task<bool>? handoffTask;
        lock (_handoffTaskLock) handoffTask = _handoffTask;
        if (handoffTask != null)
        {
            try { await handoffTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        try { await _mapAcquisitionInitialization.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await _entryOperation.WaitAsync().ConfigureAwait(false);
        _entryOperation.Release();
        _entryOperation.Dispose();
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        _operation.Dispose();
        await _configureOperation.WaitAsync().ConfigureAwait(false);
        _configureOperation.Release();
        _configureOperation.Dispose();
        await _transitionOperation.WaitAsync().ConfigureAwait(false);
        _transitionOperation.Release();
        _transitionOperation.Dispose();
        await _presenceOperation.WaitAsync().ConfigureAwait(false);
        _presenceOperation.Release();
        _presenceOperation.Dispose();
        _lifetime.Dispose();
        if (_ownsOnline) await _online.DisposeAsync().ConfigureAwait(false);
    }

    private async Task SendLobbyCommandAsync(Func<LobbySnapshot, NodeSessionSnapshot?, NodeCommand> command,
        string type, CancellationToken cancellationToken)
    {
        NodeControlClient node = RequireConnected();
        LobbySnapshot lobby = node.Lobby ?? throw new InvalidOperationException("Join a lobby first.");
        await node.SendAsync(type, command(lobby, node.Session), cancellationToken).ConfigureAwait(false);
    }

    private async Task SendLobbyCommandAndWaitAsync(
        Func<LobbySnapshot, NodeSessionSnapshot?, NodeCommand> command,
        string type, CancellationToken cancellationToken)
    {
        NodeControlClient node = RequireConnected();
        LobbySnapshot lobby = node.Lobby ?? throw new InvalidOperationException("Join a lobby first.");
        await SendExpectedSnapshotAsync(node, type, command(lobby, node.Session), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendExpectedSnapshotAsync(string type, NodeCommand command,
        CancellationToken cancellationToken)
    {
        NodeControlClient node = RequireConnected();
        await SendExpectedSnapshotAsync(node, type, command, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task SendExpectedSnapshotAsync(NodeControlClient node, string type,
        NodeCommand command, CancellationToken cancellationToken)
    {
        NodeControlEvent response = await node.SendAndWaitAsync(type, command, cancellationToken)
            .ConfigureAwait(false);
        RequireResponse(response, "lobby.snapshot");
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
        if (session == null)
        {
            CancelMapPreparation();
            return;
        }
        RestoreNodeCatalog(session);
        session.Changed += NodeChanged;
        PublishFromNode(session);
        SynchronizeMapPreparation(session);
    }

    private void SynchronizeMapPreparation(NodeControlClient node)
    {
        MapRequirement? requirement = node.Lobby?.RequiredMap;
        string? endpoint = node.Endpoint;
        if (requirement == null || endpoint == null || !node.Connected)
        {
            CancelMapPreparation();
            return;
        }

        long generation = Interlocked.Read(ref _generation);
        _ = StartMapPreparation(node, requirement, endpoint, generation);
        SynchronizeTransitionMapReadiness(node, requirement, generation);
    }

    private void SynchronizeTransitionMapReadiness(NodeControlClient node,
        MapRequirement requirement, long generation)
    {
        lock (_mapPreparationLock)
        {
            if (!_mapPreparation.IsReadyFor(requirement)
                || _mapPreparationGeneration != generation
                || !ReferenceEquals(_mapPreparationNode, node))
                return;
        }

        NodeControlClient.ViewState state = node.State;
        if (!ShouldAutoReadyTransitionMap(state, requirement)) return;
        Guid transitionId = state.ExpectedTransition!.TransitionId;
        lock (_transitionReadyLock)
        {
            if (_transitionReadyCompleted == transitionId
                || _transitionReadyInFlight == transitionId)
                return;
            _transitionReadyInFlight = transitionId;
        }
        _ = ReadyTransitionMapAsync(node, requirement, transitionId);
    }

    internal static bool ShouldAutoReadyTransitionMap(NodeControlClient.ViewState state,
        MapRequirement requirement)
    {
        NodeMatchTransitionStarted? transition = state.ExpectedTransition;
        LobbySnapshot? lobby = state.Lobby;
        Guid? sessionId = state.Session?.SessionId;
        return transition is { Choice: MatchTransitionChoice.ChangeMap }
            && state.ExpectedTransitionEnded
            && state.TransitionVote is { State: MatchTransitionVoteState.Approved } vote
            && vote.TransitionId == transition.TransitionId
            && lobby is { Phase: LobbyPhase.Open, RequiredMap: not null }
            && lobby.RequiredMap == requirement
            && StringComparer.Ordinal.Equals(lobby.MapKey, transition.TargetMapKey)
            && sessionId is { } id
            && lobby.Members.FirstOrDefault(member => member.SessionId == id)
                is { Observer: false, Ready: false };
    }

    private async Task ReadyTransitionMapAsync(NodeControlClient node,
        MapRequirement requirement, Guid transitionId)
    {
        bool completed = false;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!ReferenceEquals(_online.Node, node)) return;
                NodeControlClient.ViewState state = node.State;
                if (!ShouldAutoReadyTransitionMap(state, requirement))
                {
                    completed = state.Lobby?.Members.FirstOrDefault(member =>
                        member.SessionId == state.Session?.SessionId)?.Ready == true;
                    return;
                }
                NodeControlEvent response = await node.SendAndWaitAsync("lobby.ready.set",
                    new LobbySetReady(true, state.Lobby!.Revision), _lifetime.Token)
                    .ConfigureAwait(false);
                if (response.Type == "lobby.snapshot")
                {
                    completed = true;
                    return;
                }
                NodeControlError? error = response.Type == "error"
                    ? response.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)
                    : null;
                if (error?.Code == "stale_revision") continue;
                throw new InvalidOperationException(error?.Message
                    ?? "The server did not accept transition readiness.");
            }
            throw new InvalidOperationException(
                "The lobby changed repeatedly while confirming transition readiness.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            string message = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                "The next map is ready, but the lobby could not continue. Try Ready again.");
            Publish(State with { Loading = false, Message = message });
        }
        finally
        {
            lock (_transitionReadyLock)
            {
                if (_transitionReadyInFlight == transitionId)
                    _transitionReadyInFlight = null;
                if (completed) _transitionReadyCompleted = transitionId;
            }
        }
    }

    private Task<InstalledMap> StartMapPreparation(NodeControlClient node,
        MapRequirement requirement, string endpoint, long generation)
    {
        CancellationTokenSource? previousCancellation = null;
        CancellationTokenSource currentCancellation;
        Task<InstalledMap>? task;
        bool started = false;
        lock (_mapPreparationLock)
        {
            if (_mapPreparationTask is { } existing
                && ReferenceEquals(_mapPreparationNode, node)
                && _mapPreparationRequirement == requirement
                && _mapPreparationGeneration == generation)
                return existing;

            previousCancellation = _mapPreparationCancellation;
            currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token);
            _mapPreparationCancellation = currentCancellation;
            _mapPreparationNode = node;
            _mapPreparationRequirement = requirement;
            _mapPreparationGeneration = generation;
            _mapPreparation = new MapPreparationState(MapPreparationPhase.Downloading,
                requirement, 0, requirement.PackageSize, null, generation);
            task = PrepareMapAsync(node, requirement, endpoint, generation,
                currentCancellation.Token);
            _mapPreparationTask = task;
            started = true;
        }

        if (previousCancellation != null)
        {
            try { previousCancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            previousCancellation.Dispose();
        }
        if (started)
        {
            Publish(State with { Loading = true,
                Message = $"Preparing {requirement.StableId} {requirement.Version}…" });
            _ = ObserveMapPreparationAsync(task!, node, requirement, generation,
                currentCancellation);
        }
        return task!;
    }

    private async Task<InstalledMap> PrepareMapAsync(NodeControlClient node,
        MapRequirement requirement, string endpoint, long generation,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<MapDownloadProgress>(value =>
        {
            MapPreparationPhase phase = value.Stage switch
            {
                "Ready" => MapPreparationPhase.Ready,
                "Compiling" or "Verifying" => MapPreparationPhase.Compiling,
                _ => MapPreparationPhase.Downloading
            };
            UpdateMapPreparation(node, requirement, generation, phase,
                value.Received, value.Total, null);
        });
        return await _mapAcquisition.AcquireAsync(requirement, endpoint, progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ObserveMapPreparationAsync(Task<InstalledMap> task,
        NodeControlClient node, MapRequirement requirement, long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await task.ConfigureAwait(false);
            UpdateMapPreparation(node, requirement, generation, MapPreparationPhase.Ready,
                requirement.PackageSize, requirement.PackageSize, null);
            SynchronizeTransitionMapReadiness(node, requirement, generation);
        }
        catch (OperationCanceledException)
        {
            UpdateMapPreparation(node, requirement, generation,
                MapPreparationPhase.Cancelled, 0, requirement.PackageSize, null);
        }
        catch (Exception error)
        {
            string message = error.Message is { Length: > 0 and <= 512 } text
                ? text : "The required map could not be prepared.";
            UpdateMapPreparation(node, requirement, generation,
                MapPreparationPhase.Failed, 0, requirement.PackageSize, message);
        }
        finally
        {
            lock (_mapPreparationLock)
            {
                if (ReferenceEquals(_mapPreparationTask, task)
                    && ReferenceEquals(_mapPreparationCancellation, cancellation))
                {
                    _mapPreparationCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    private void UpdateMapPreparation(NodeControlClient node, MapRequirement requirement,
        long generation, MapPreparationPhase phase, long received, long total,
        string? failure)
    {
        lock (_mapPreparationLock)
        {
            if (!ReferenceEquals(_mapPreparationNode, node)
                || _mapPreparationRequirement != requirement
                || _mapPreparationGeneration != generation
                || _mapPreparationTask == null)
                return;
            _mapPreparation = new MapPreparationState(phase, requirement,
                Math.Clamp(received, 0, requirement.PackageSize),
                Math.Clamp(total, 0, requirement.PackageSize), failure, generation);
        }
        Publish(State with
        {
            Loading = phase is not MapPreparationPhase.Ready
                and not MapPreparationPhase.Failed
                and not MapPreparationPhase.Cancelled,
            Message = phase switch
            {
                MapPreparationPhase.Ready => $"{requirement.StableId} {requirement.Version} is ready.",
                MapPreparationPhase.Failed => failure ?? "The required map could not be prepared.",
                MapPreparationPhase.Cancelled => "Required map preparation canceled.",
                _ => $"{phase} {requirement.StableId} · {received:N0}/{total:N0} bytes"
            }
        });
    }

    private void EnsureMapPreparationCurrent(NodeControlClient node,
        MapRequirement requirement, long generation)
    {
        lock (_mapPreparationLock)
        {
            if (!ReferenceEquals(_mapPreparationNode, node)
                || _mapPreparationRequirement != requirement
                || _mapPreparationGeneration != generation
                || !_mapPreparation.IsReadyFor(requirement))
                throw new OperationCanceledException(
                    "The required map preparation was replaced before launch.");
        }
    }

    private void CancelMapPreparation()
    {
        CancellationTokenSource? cancellation;
        MapRequirement? priorRequirement;
        bool changed;
        lock (_mapPreparationLock)
        {
            changed = _mapPreparationTask != null || _mapPreparationCancellation != null
                || _mapPreparation.Phase != MapPreparationPhase.None;
            if (!changed) return;
            cancellation = _mapPreparationCancellation;
            priorRequirement = _mapPreparationRequirement;
            _mapPreparationCancellation = null;
            _mapPreparationTask = null;
            _mapPreparationNode = null;
            _mapPreparationRequirement = null;
            _mapPreparationGeneration = 0;
            _mapPreparation = priorRequirement == null
                ? MapPreparationState.None
                : new MapPreparationState(MapPreparationPhase.Cancelled,
                    priorRequirement, 0, priorRequirement.PackageSize, null,
                    Interlocked.Read(ref _generation));
        }
        if (cancellation != null)
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            cancellation.Dispose();
        }
        if (Volatile.Read(ref _disposed) == 0)
            Publish(State with { Loading = false,
                Message = priorRequirement == null
                    ? "Required map preparation canceled."
                    : "Required map preparation canceled." });
    }

    private void NodeChanged()
    {
        NodeControlClient? node = _observed;
        if (node == null || Volatile.Read(ref _disposed) != 0) return;
        PublishFromNode(node);
        SynchronizeMapPreparation(node);
        if (node.State.MatchEnded) _handoff.Cancel();
        if (node.State.Lobby == null) CancelPendingHandoff();
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
            CancelMapPreparation();
            ClearSelectedNodeCatalog();
            _shell.SetNodeStatus(false);
            Publish(State with { Phase = PlayPhase.Nodes, Node = null, BrowsedLobbies = null, Loading = false,
                Message = "Server disconnected." });
        }
    }

    private void PublishFromNode(NodeControlClient node)
    {
        NodeControlClient.ViewState state = node.State;
        PlayPhase phase = state.Handoff != null && !state.MatchEnded ? PlayPhase.Handoff
            : state.Lobby != null ? PlayPhase.Lobby
            : node.Connected ? PlayPhase.Connected : PlayPhase.Error;
        string message = state.Error is { } error
            ? PrimeRoutePresentation.PlayerFacingNetworkError(error,
                "Connection issue. Reconnect and try again.")
            : (phase switch
        {
            PlayPhase.Handoff => "Match found. Connecting to the match server…",
            PlayPhase.Lobby => state.Lobby!.Name,
            PlayPhase.Connected => "Connected to server.",
            _ => "Server disconnected. Reconnect to continue."
        });
        _shell.SetNodeStatus(node.Connected, _connectedNodeName, _connectedNodeRegion);
        Publish(State with { Phase = phase, Node = state, Loading = false, Message = message });
    }

    private async Task<bool> StartHandoffAsync(NodeControlClient node, NodeMatchHandoff handoff,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _handoffEnabled) == 0) return false;
        Guid? lobbyId = node.Lobby?.LobbyId;
        if (IsLobbyLeaving(lobbyId)) return false;
        NodeSessionSnapshot session = node.Session
            ?? throw new InvalidOperationException("The server connection is not ready.");
        long generation = Interlocked.Read(ref _generation);
        PlayHandoffKey key = new(session.NodeId, handoff.MatchId, handoff.Nonce, generation);
        if (!_handoff.TryBegin(key)) return false;
        Publish(State with { Phase = PlayPhase.Handoff, Loading = true,
            Message = "Preparing the match server and synchronizing content…" });
        bool joined = false;
        try
        {
            if (node.Lobby?.RequiredMap is { } required)
            {
                string endpoint = node.Endpoint
                    ?? throw new InvalidOperationException("The server connection is unavailable.");
                Task<InstalledMap> preparation = StartMapPreparation(node, required, endpoint,
                    generation);
                await preparation.WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureMapPreparationCurrent(node, required, generation);
            }
            joined = await NetLaunch.JoinWorkerAsync(handoff, session.DisplayName, cancellationToken)
                .ConfigureAwait(false);
            if (!joined)
                throw new InvalidOperationException(PrimeRoutePresentation.PlayerFacingNetworkError(
                    NetLaunch.LastJoinError,
                    "Could not join the match. Check your connection and try again."));
            if (Volatile.Read(ref _disposed) != 0 || generation != Interlocked.Read(ref _generation)
                || Volatile.Read(ref _handoffEnabled) == 0 || cancellationToken.IsCancellationRequested
                || IsLobbyLeaving(lobbyId)
                || !ReferenceEquals(_observed, node)
                || !_handoff.TryComplete(key))
            {
                _online.ReleaseMatch(dispose: true);
                NetSession.Stop();
                _handoff.Cancel(key);
                return false;
            }
            if (_online.Match?.Play is not { } play)
                throw new InvalidOperationException("The Worker join completed without a scoped match context.");
            play.BindNodeMatch(handoff.MatchId);
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
            Publish(State with { Loading = false, Message = "Connected to the match server." });
            return true;
        }
        catch (OperationCanceledException)
        {
            _online.ReleaseMatch(dispose: true);
            NetSession.Stop();
            _handoff.Cancel(key);
            if (IsStaleHandoff(node, generation, cancellationToken)) return false;
            Publish(State with { Phase = PlayPhase.Error, Loading = false,
                Message = "Match connection canceled." });
            return false;
        }
        catch (Exception error)
        {
            _online.ReleaseMatch(dispose: true);
            NetSession.Stop();
            _handoff.Cancel(key);
            if (IsStaleHandoff(node, generation, cancellationToken)) return false;
            string message = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                "Could not join the match. Check your connection and try again.");
            Publish(State with { Phase = PlayPhase.Error, Loading = false, Message = message });
            _shell.Notify(PrimeNotificationKind.Error, message);
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

    private bool IsLobbyLeaving(Guid? lobbyId)
    {
        lock (_stateLock)
        {
            return _leavingLobbyId is { } leaving
                && (!lobbyId.HasValue || lobbyId.Value == leaving);
        }
    }

    private async Task<AccountSession> RequireAccountAsync(CancellationToken cancellationToken)
    {
        if (!_shell.HasNetworkIdentity)
            throw new InvalidOperationException("Sign in or choose Guest access before browsing servers.");
        return await _accountResolver(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Configure online services in Settings before browsing servers.");
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
        => _online.Node is { Connected: true } node
            ? node : throw new InvalidOperationException("Connect to a server first.");

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
            .OrderByDescending(map => StringComparer.Ordinal.Equals(
                map, "MP3 PROVING GROUND"))
            .ThenBy(map => map, StringComparer.Ordinal).ToArray();
        return available.Length == 0
            ? (NodeMapCatalogState.Disjoint, available)
            : (NodeMapCatalogState.Available, available);
    }

    private static string GetMapCatalogMessage(NodeMapCatalogState state) => state switch
    {
        NodeMapCatalogState.None => "Select a server to see hosted maps.",
        NodeMapCatalogState.Unknown => MapCatalogMessageFor(NodeMapCatalogState.Unknown),
        NodeMapCatalogState.Empty => MapCatalogMessageFor(NodeMapCatalogState.Empty),
        NodeMapCatalogState.Disjoint => MapCatalogMessageFor(NodeMapCatalogState.Disjoint),
        _ => ""
    };

    private static string MapCatalogMessageFor(NodeMapCatalogState state) => state switch
    {
        NodeMapCatalogState.Unknown => "This server did not provide its hosted map catalog; map selection is unavailable.",
        NodeMapCatalogState.Empty => "This server has no hosted maps.",
        NodeMapCatalogState.Disjoint => "This server's hosted maps are not installed locally.",
        _ => ""
    };

    private static string ValidateLobbyName(string name)
    {
        string value = name?.Trim() ?? "";
        if (value is not { Length: >= 1 and <= 64 } || value.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Lobby name must be 1 to 64 printable characters.", nameof(name));
        return value;
    }

    private static string ValidateMapKey(string mapKey)
    {
        if (mapKey is not { Length: >= 1 and <= 128 } || mapKey.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Map key must be 1 to 128 printable characters.", nameof(mapKey));
        return mapKey;
    }

    private static Guid ValidateLobbyId(Guid lobbyId)
        => lobbyId != Guid.Empty ? lobbyId
            : throw new ArgumentException("A lobby identity is required.", nameof(lobbyId));

    private static long ValidateRevision(long revision)
        => revision > 0 ? revision : throw new ArgumentOutOfRangeException(nameof(revision));

    private static void ValidateChatText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(text) > 256)
            throw new ArgumentException("Chat text must be non-empty, printable, and at most 256 UTF-8 bytes.",
                nameof(text));
    }

    private static LobbyRulesOptions NormalizeLobbyRules(MatchMode mode,
        LobbyRulesOptions? rules, int? legacyTimeLimitSeconds, int? legacyPointGoal)
    {
        try
        {
            return (rules ?? LobbyRulesOptions.Empty).Normalize(mode,
                legacyTimeLimitSeconds, legacyPointGoal);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw;
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException("Invalid lobby rules.", error);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
