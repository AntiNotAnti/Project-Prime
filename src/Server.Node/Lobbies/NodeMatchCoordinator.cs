using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead;

namespace ProjectPrime.Server.Node.Lobbies;

public sealed record NodeMatchNotification(Guid SessionId, object Payload,
    // Initial handoffs carry the personalized snapshot that was committed with
    // the admission batch. It must remain usable if a terminal callback moves
    // the lobby to PostMatch before the session sender drains this notification.
    LobbySnapshot? HandoffSnapshot = null);
public sealed record NodeMatchDeliveryOverflow;

internal sealed record CoordinatorLifecycleDiagnosticsSnapshot(Guid MatchId,
    Guid LobbyId, MatchLifecycleState State, ulong LifecycleEpoch, long Generation,
    Guid? TransitionId, string? TransitionPhase, Guid? RecoveryWorkerId);

/// <summary>Bounded coordinator retention facts for host-side soak shutdown
/// assertions. These values describe ownership state only; they do not expose
/// credentials or session payloads.</summary>
public sealed record NodeMatchCoordinatorRetentionSnapshot(
    int ActiveMatchRegistrations,
    int PendingTransitions,
    int PendingContinuationRecoveries,
    int PoisonedSessionStates,
    int CurrentSessionMismatches);

/// <summary>Injectable transition deadlines. Production uses the plan's
/// two-second cancellation acknowledgement window and five-second hard
/// terminal deadline; tests can drive the same state machine with a manual
/// clock and <see cref="NodeMatchCoordinator.CheckTransitionWatchdogs"/>.</summary>
public sealed record NodeMatchCoordinatorOptions
{
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public TimeSpan CancelAcknowledgementTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan TransitionHardTimeout { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Deterministic test seam invoked after every admission has
    /// installed but before the lobby and complete handoff batch commit. It is
    /// intentionally outside both lifecycle gates so it can exercise benign
    /// chat and membership races.</summary>
    internal Action<MatchId>? BeforeMatchCommit { get; init; }
    internal Action<MatchId>? AfterMatchCommitted { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Clock);
        if (CancelAcknowledgementTimeout <= TimeSpan.Zero
            || TransitionHardTimeout <= CancelAcknowledgementTimeout)
            throw new ArgumentOutOfRangeException(nameof(TransitionHardTimeout));
    }
}

/// <summary>Node configuration for one hosted map and its optional mode allow-list.</summary>
/// <remarks>
/// The nullable <see cref="Modes"/> property is intentional: an omitted allow-list
/// preserves the legacy configuration meaning of every defined multiplayer mode.
/// Values are numeric so invalid enum values fail catalog construction instead of
/// being silently coerced by configuration binding.
/// </remarks>
public sealed record NodeMapConfiguration
{
    public string MapKey { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public string ContentVersion { get; init; } = "";
    public string BuildVersion { get; init; } = "";
    public byte ProtocolVersion { get; init; }
    public int[]? Modes { get; init; }
    public string? StableId { get; init; }
    public string? Version { get; init; }
    public string? MapContentHash { get; init; }
    public string? ArtifactHash { get; init; }
    public long? PackageSize { get; init; }
    public string? PackagePath { get; init; }

    public NodeMapConfiguration() { }

    public NodeMapConfiguration(string mapKey, string contentHash, string contentVersion,
        string buildVersion, byte protocolVersion, int[]? modes)
    {
        MapKey = mapKey; ContentHash = contentHash; ContentVersion = contentVersion;
        BuildVersion = buildVersion; ProtocolVersion = protocolVersion; Modes = modes;
    }

    public NodeMapConfiguration(ContentIdentity identity, int[]? modes = null)
        : this(identity.MapKey, identity.ContentHash, identity.ContentVersion,
            identity.BuildVersion, identity.ProtocolVersion, modes)
    {
        if (identity.RequiredMap is { } map)
        {
            StableId = map.StableId;
            Version = map.Version;
            MapContentHash = map.ContentHash;
            ArtifactHash = map.ArtifactHash;
            PackageSize = map.PackageSize;
        }
    }

    public MapRequirement? RequiredMap
    {
        get
        {
            bool any = StableId != null || Version != null || MapContentHash != null
                || ArtifactHash != null || PackageSize != null;
            if (!any) return null;
            if (StableId == null || Version == null || MapContentHash == null
                || ArtifactHash == null || PackageSize == null)
                throw new ArgumentException("Custom map acquisition metadata must be complete.");
            return new(StableId, Version, MapContentHash, ArtifactHash, PackageSize.Value,
                MapRequirement.ComputeMatchContentHash(ContentHash, StableId, Version,
                    MapContentHash, BuildVersion, ProtocolVersion));
        }
    }

    public ContentIdentity Identity => new(MapKey, ContentHash, ContentVersion, BuildVersion,
        ProtocolVersion, RequiredMap);
}

public sealed class NodeContentCatalog
{
    private const int MaximumMaps = 256;
    private const int MaximumMapModes = 768;
    private static readonly MatchMode[] DefinedModes = Enum.GetValues<MatchMode>();
    private sealed record Entry(ContentIdentity Identity, MatchMode[] Modes);
    private readonly IReadOnlyDictionary<string, Entry> _maps;
    private readonly string[] _order;
    private readonly ContentIdentity[] _catalogEntries;

    public NodeContentCatalog(IEnumerable<ContentIdentity> maps)
        : this(maps, null) { }

    public static NodeContentCatalog FromConfiguration(IEnumerable<NodeMapConfiguration> maps)
    {
        if (maps == null) throw new ArgumentNullException(nameof(maps));
        NodeMapConfiguration[] entries = maps.ToArray();
        if (entries.Any(map => map == null)) throw new ArgumentException("Invalid Node map catalog entry.");
        var configuredModes = entries.ToDictionary(map => map.MapKey, map => map.Modes, StringComparer.Ordinal);
        return new NodeContentCatalog(entries.Select(map => map.Identity), configuredModes);
    }

    private NodeContentCatalog(IEnumerable<ContentIdentity> maps, IReadOnlyDictionary<string, int[]?>? configuredModes)
    {
        if (maps == null) throw new ArgumentNullException(nameof(maps));
        var entries = maps.ToArray();
        if (entries.Length > MaximumMaps) throw new ArgumentException("Node map catalog supports at most 256 maps.");

        var mapKeys = new HashSet<string>(StringComparer.Ordinal);
        var catalog = new Dictionary<string, Entry>(entries.Length, StringComparer.Ordinal);
        int pairCount = 0;
        foreach (ContentIdentity? identity in entries)
        {
            if (identity == null) throw new ArgumentException("Invalid Node map catalog entry.");
            if (identity.MapKey is not { Length: > 0 and <= 128 }
                || string.IsNullOrWhiteSpace(identity.MapKey) || identity.MapKey.Any(c => c is < ' ' or > '~')
                || string.IsNullOrWhiteSpace(identity.ContentHash) || string.IsNullOrWhiteSpace(identity.ContentVersion)
                || string.IsNullOrWhiteSpace(identity.BuildVersion) || identity.ProtocolVersion == 0)
                throw new ArgumentException("Invalid Node map catalog entry.");
            identity.RequiredMap?.Validate();
            if (!mapKeys.Add(identity.MapKey)) throw new ArgumentException("Node map catalog contains a duplicate map.");

            MatchMode[] modes = configuredModes != null && configuredModes.TryGetValue(identity.MapKey, out int[]? configured)
                ? configured is null ? DefinedModes.ToArray() : ParseModes(configured)
                : DefinedModes.ToArray();
            if (modes.Length > MaximumMapModes - pairCount)
                throw new ArgumentException("Node map catalog supports at most 768 map/mode pairs.");
            pairCount += modes.Length;
            catalog.Add(identity.MapKey, new(identity, modes));
        }
        _maps = catalog;
        _order = entries.Select(e => e.MapKey).ToArray();
        _catalogEntries = entries.OrderBy(e => e.MapKey, StringComparer.Ordinal).ToArray();
        string material = string.Join("\n", _catalogEntries.Select(CatalogMaterial));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        MapCatalogHash = Convert.ToHexString(hash).ToLowerInvariant();
        MapCatalogRevision = BinaryPrimitives.ReadInt64LittleEndian(hash.AsSpan()) & long.MaxValue;
        if (MapCatalogRevision == 0) MapCatalogRevision = 1;
    }
    public IReadOnlyCollection<string> Maps => _order.OrderBy(key => key, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<ContentIdentity> Entries => _order.Select(key => _maps[key].Identity).ToArray();
    /// <summary>Stable, map-key ordered catalog used for bounded Node discovery pages.</summary>
    public IReadOnlyList<ContentIdentity> CatalogEntries => _catalogEntries;
    public int MapCount => _catalogEntries.Length;
    public long MapCatalogRevision { get; }
    public string MapCatalogHash { get; }
    public ContentIdentity Get(string mapKey) => _maps.TryGetValue(mapKey, out var map) ? map.Identity : throw new LobbyCommandException("map_unavailable", "Map is not configured on this Node.");
    public MapRequirement? RequiredMap(string mapKey)
        => _maps.TryGetValue(mapKey, out var map) ? map.Identity.RequiredMap : null;

    public ContentIdentity Get(string mapKey, MatchMode mode)
    {
        Validate(mapKey, mode);
        return _maps[mapKey].Identity;
    }

    public void Validate(string mapKey, MatchMode mode)
    {
        if (!_maps.TryGetValue(mapKey, out var map))
            throw new LobbyCommandException("map_unavailable", "Map is not configured on this Node.");
        if (!Enum.IsDefined(mode)) throw new LobbyCommandException("mode_unavailable", $"Mode {mode} is not available for map '{mapKey}'.");
        if (!map.Modes.Contains(mode))
            throw new LobbyCommandException("mode_unavailable", $"Mode {mode} is not available for map '{mapKey}'.");
    }

    public IReadOnlyList<LobbyMapChoice> RoundMaps(string current, MatchMode mode)
    {
        Validate(current, mode);
        var maps = _order.Where(key => _maps[key].Modes.Contains(mode)).ToArray();
        int index = Array.IndexOf(maps, current);
        // Rotate in configured order; a one-map pool intentionally rematches.
        return Enumerable.Range(1, maps.Length).Select(offset =>
        {
            string key = maps[(index + offset) % maps.Length];
            return new LobbyMapChoice(key, mode, _maps[key].Identity.RequiredMap);
        }).ToArray();
    }

    private static MatchMode[] ParseModes(IEnumerable<int> values)
    {
        if (values is null) throw new ArgumentException("Node map catalog contains no allowed modes.");
        var modes = new List<MatchMode>();
        var seen = new HashSet<MatchMode>();
        foreach (int value in values)
        {
            if (value is < byte.MinValue or > byte.MaxValue)
                throw new ArgumentException("Node map catalog contains an invalid mode.");
            MatchMode mode = (MatchMode)value;
            if (!Enum.IsDefined(mode)) throw new ArgumentException("Node map catalog contains an invalid mode.");
            if (!seen.Add(mode)) throw new ArgumentException("Node map catalog contains a duplicate mode.");
            modes.Add(mode);
        }
        if (modes.Count == 0) throw new ArgumentException("Node map catalog contains no allowed modes.");
        return modes.ToArray();
    }

    private static string CatalogMaterial(ContentIdentity identity)
    {
        MapRequirement? required = identity.RequiredMap;
        return string.Join('\0', identity.MapKey, identity.ContentHash, identity.ContentVersion,
            identity.BuildVersion, identity.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            required?.StableId ?? "", required?.Version ?? "", required?.ContentHash ?? "",
            required?.ArtifactHash ?? "", required?.PackageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            required?.MatchContentHash ?? "");
    }
}

/// <summary>Connects frozen lobby admission to Worker placement and immutable terminal events.</summary>
public sealed class NodeMatchCoordinator : IDisposable
{
    private sealed class MatchLifecycle
    {
        public MatchLifecycle(MatchSpec spec, FrozenLobbyMember[] members,
            Guid preparationOwnerSessionId)
        {
            Spec = spec; Members = members;
            PreparationOwnerSessionId = preparationOwnerSessionId;
        }
        public MatchSpec Spec { get; }
        public FrozenLobbyMember[] Members { get; }
        public Guid PreparationOwnerSessionId { get; }
        public MatchPlacement? Placement { get; private set; }
        public long Generation { get; private set; }
        public MatchLifecycleState State { get; private set; } =
            MatchLifecycleState.PreparingWorker;
        public MatchLifecycleEpoch LifecycleEpoch
            => Spec.LifecycleEpoch.Value == 0 ? MatchLifecycleEpoch.Initial : Spec.LifecycleEpoch;
        public Dictionary<Guid, HandoffState> Handoffs { get; } = [];

        public void BeginAdmissionInstallation(MatchPlacement placement)
        {
            if (State != MatchLifecycleState.PreparingWorker || Placement != null)
                throw new InvalidOperationException("Match lifecycle cannot install admissions from its current state.");
            Placement = placement;
            Generation++;
            State = MatchLifecycleState.InstallingAdmissions;
        }

        public void AdmissionsInstalled()
        {
            if (State != MatchLifecycleState.InstallingAdmissions)
                throw new InvalidOperationException("Match lifecycle admissions are not being installed.");
            State = MatchLifecycleState.ReadyToCommit;
        }

        public void Commit()
        {
            if (State != MatchLifecycleState.ReadyToCommit)
                throw new InvalidOperationException("Match lifecycle is not ready to commit.");
            State = MatchLifecycleState.Running;
        }

        public void BeginRetirement()
        {
            if (State == MatchLifecycleState.Retired) return;
            State = MatchLifecycleState.Retiring;
        }

        public void Retire()
        {
            if (State != MatchLifecycleState.Retiring)
                BeginRetirement();
            State = MatchLifecycleState.Retired;
        }
    }
    private sealed class HandoffState
    {
        public HandoffGeneration Generation;
        public NodeMatchHandoff? Value;
        public long ExpiresAt;
        public InstallAdmissionKey? Installed;
        public TaskCompletionSource<NodeMatchHandoff>? InFlight;
    }
    private enum TransitionPhase
    {
        AwaitingAcknowledgement,
        AwaitingTerminal,
        Completed,
        TimedOut,
        Failed
    }
    private sealed class PendingTransition(LobbyMatchTransitionSelection selection,
        WorkerMatchAssignment assignment, DateTimeOffset now, long startedTimestamp,
        NodeMatchCoordinatorOptions options)
    {
        public LobbyMatchTransitionSelection Selection { get; } = selection;
        public WorkerMatchAssignment Assignment { get; } = assignment;
        public TransitionPhase Phase = TransitionPhase.AwaitingAcknowledgement;
        public DateTimeOffset AcknowledgementDeadline { get; } = now + options.CancelAcknowledgementTimeout;
        public DateTimeOffset HardDeadline { get; } = now + options.TransitionHardTimeout;
        public long StartedTimestamp { get; } = startedTimestamp;
        public bool RetrySent;
        public bool TerminalAcknowledged;
        public bool Fenced;
    }
    /// <summary>Retains an unexpected continuation's coordinator ownership
    /// after cancellation has been requested. Cancellation is only a request;
    /// the lifecycle remains fenced until the scheduler observes a Worker
    /// terminal or verified Worker retirement.</summary>
    private sealed class PendingContinuationRecovery(
        MatchId matchId, WorkerId workerId, DateTimeOffset now,
        NodeMatchCoordinatorOptions options)
    {
        public MatchId MatchId { get; } = matchId;
        public WorkerId WorkerId { get; } = workerId;
        public DateTimeOffset AcknowledgementDeadline { get; } =
            now + options.CancelAcknowledgementTimeout;
        public DateTimeOffset HardDeadline { get; } =
            now + options.TransitionHardTimeout;
        public bool RetrySent;
        public bool ForceRetirementStarted;
    }
    private readonly object _gate = new();
    private readonly LobbyManager _lobbies;
    private readonly WorkerScheduler _scheduler;
    private readonly WorkerManager _workers;
    private readonly WorkerAdmissionIssuer _issuer;
    private readonly NodeContentCatalog _content;
    private readonly ILogger<NodeMatchCoordinator> _logger;
    private readonly Dictionary<MatchId, MatchLifecycle> _matches = [];
    private readonly Dictionary<MatchId, PendingTransition> _transitions = [];
    private readonly Dictionary<MatchId, PendingContinuationRecovery> _continuationRecoveries = [];
    private readonly ConcurrentDictionary<Guid, object> _latest = [];
    private readonly ConcurrentDictionary<Guid, NodeMatchCompletion> _completions = [];
    private readonly Dictionary<Guid, Queue<NodeMatchNotification>> _notifications = [];
    // Expected transition ownership is independent of the single latest
    // payload slot.  A reconnect must replay Started before any terminal
    // marker or replacement handoff until the fresh MatchId is delivered.
    private readonly Dictionary<Guid, NodeMatchTransitionStarted> _expectedTransitions = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly TimeProvider _clock;
    private readonly NodeMatchCoordinatorOptions _options;
    private readonly Dictionary<WorkerId, Task> _forcedWorkerRetirements = [];
    private bool _disposed;
    private const int MaximumPendingEventsPerSession = 32;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(1);
    public NodeMatchCoordinator(LobbyManager lobbies, WorkerScheduler scheduler, WorkerManager workers,
        WorkerAdmissionIssuer issuer, NodeContentCatalog content, ILogger<NodeMatchCoordinator>? logger = null,
        NodeMatchCoordinatorOptions? options = null)
    {
        _options = options ?? new NodeMatchCoordinatorOptions();
        _options.Validate();
        _lobbies = lobbies; _scheduler = scheduler; _workers = workers; _issuer = issuer; _content = content;
        _logger = logger ?? NullLogger<NodeMatchCoordinator>.Instance;
        _clock = _options.Clock;
        _lifetimeToken = _lifetime.Token;
        _lobbies.ContentCatalog = content;
        _scheduler.Completed += Completed;
        _scheduler.Ended += Ended;
        _scheduler.TransitionEnded += TransitionEnded;
    }
    public object? ForSession(Guid sessionId)
    {
        object? value;
        lock (_gate)
        {
            if (!_latest.TryGetValue(sessionId, out value)) return null;
            if (value is NodeMatchHandoff && !IsLiveHandoffLocked(sessionId))
            {
                _latest.TryRemove(sessionId, out _);
                return null;
            }
        }
        if (!IsPayloadCurrent(sessionId, value))
        {
            lock (_gate)
                if (_latest.TryGetValue(sessionId, out object? current) && ReferenceEquals(current, value))
                    _latest.TryRemove(sessionId, out _);
            return null;
        }
        return value;
    }

    public NodeMatchCoordinatorRetentionSnapshot RetentionSnapshot
    {
        get
        {
            lock (_gate)
            {
                int mismatches = _latest.Count(pair => !IsPayloadCurrentLocked(pair.Key, pair.Value))
                    + _completions.Count(pair => !IsPayloadCurrentLocked(pair.Key, pair.Value))
                    + _expectedTransitions.Count(pair => !IsPayloadCurrentLocked(pair.Key, pair.Value));
                return new(_matches.Count, _transitions.Count,
                    _continuationRecoveries.Count, _expectedTransitions.Count, mismatches);
            }
        }
    }

    /// <summary>Returns the coordinator-owned state for one exact frozen
    /// lifecycle. This is an internal read-only seam for lifecycle diagnostics
    /// and deterministic Node tests; it does not grant callers any mutation or
    /// terminal authority.</summary>
    internal bool TryGetLifecycleState(MatchId matchId,
        out MatchLifecycleState state)
    {
        lock (_gate)
        {
            if (_matches.TryGetValue(matchId, out MatchLifecycle? lifecycle))
            {
                state = lifecycle.State;
                return true;
            }
            state = default;
            return false;
        }
    }

    /// <summary>Returns immutable, bounded lifecycle projections for host
    /// diagnostics. Coordinator state is copied while its gate is held; no
    /// lobby or Worker owner is consulted under that gate.</summary>
    internal IReadOnlyList<CoordinatorLifecycleDiagnosticsSnapshot> LifecycleDiagnosticsSnapshot()
    {
        lock (_gate)
        {
            List<CoordinatorLifecycleDiagnosticsSnapshot> result = [];
            foreach ((MatchId matchId, MatchLifecycle lifecycle) in _matches)
            {
                _transitions.TryGetValue(matchId, out PendingTransition? transition);
                _continuationRecoveries.TryGetValue(matchId,
                    out PendingContinuationRecovery? recovery);
                result.Add(new(matchId.Value, lifecycle.Spec.LobbyId.Value,
                    lifecycle.State, lifecycle.LifecycleEpoch.Value, lifecycle.Generation,
                    transition?.Selection.TransitionId,
                    transition?.Phase.ToString(), recovery?.WorkerId.Value));
            }
            return result.OrderBy(entry => entry.MatchId).ToImmutableArray();
        }
    }

    public IReadOnlyList<object> ForSessionEvents(Guid sessionId)
    {
        NodeMatchTransitionVoteSnapshot? transition = _lobbies.MatchTransitionForSession(sessionId);
        NodeMatchTransitionStarted? expected;
        NodeMatchCompletion? completion;
        object? latest;
        List<object> result;
        lock (_gate)
        {
            result = new List<object>(3);
            _expectedTransitions.TryGetValue(sessionId, out expected);
            _completions.TryGetValue(sessionId, out completion);
            _latest.TryGetValue(sessionId, out latest);
            if (transition is { } currentTransition)
                result.Add(currentTransition);
            if (expected is { })
                result.Add(expected);
            if (completion is { }) result.Add(completion);
            // Bootstrap is intentionally reconstructive only. A handoff is a
            // credential-bearing recovery mint and may be produced solely by
            // an explicit match.rejoin request; never replay one passively.
            if (latest is not null and not NodeMatchHandoff
                && (result.Count == 0 || !ReferenceEquals(result[0], latest)))
                result.Add(latest);
        }
        return result.Where(payload => IsPayloadCurrent(sessionId, payload)).ToArray();
    }

    private bool IsLiveHandoffLocked(Guid sessionId)
    {
        long now = _clock.GetUtcNow().ToUnixTimeSeconds();
        return _matches.Values.Any(pending => pending.Handoffs.TryGetValue(sessionId, out HandoffState? state)
            && state.Value != null && state.ExpiresAt > now + 1);
    }

    /// <summary>Compatibility wrapper for callers that previously used an
    /// asynchronous bootstrap. Passive reconstruction never mints, installs,
    /// or waits for a Worker handoff; explicit <c>match.rejoin</c> owns that
    /// recovery work.</summary>
    public async Task<IReadOnlyList<object>> ForSessionEventsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return await Task.FromResult(ForSessionEvents(sessionId)).ConfigureAwait(false);
    }

    /// <summary>
    /// Host-only entry point for the bounded QZ1.14 diagnostic presentation.
    /// The Node resolves the frozen human seat and constructs the authenticated
    /// Node-to-Worker command; no public control-socket request reaches this
    /// method. A stale match, non-player seat, or unavailable Worker fails
    /// closed.
    /// </summary>
    public bool TrySendHistoricalDebug(MatchId matchId, AdminAction action, byte seat)
    {
        if (action is not (AdminAction.LagCompHistory or AdminAction.LagCompDynamic or AdminAction.LagCompClear)) return false;
        MatchLifecycle? pending;
        lock (_gate)
        {
            if (!_matches.TryGetValue(matchId, out pending) || pending.Placement == null
                || !pending.Spec.Roster.Any(roster => roster.SeatId == seat && roster.Role == SeatRole.Player))
                return false;
        }
        return _scheduler.TrySendMatchAdmin(new MatchAdminCommand(matchId, action, seat));
    }

    public void ReconcileMembership()
    {
        MatchId[] empty;
        MatchLifecycle[] pending;
        lock (_gate) pending = _matches.Values.ToArray();
        empty = pending.Where(value => value.Members.All(member =>
                !_lobbies.IsCurrentMembership(member.SessionId,
                    value.Spec.LobbyId.Value, member.Generation)))
            .Select(value => value.Spec.MatchId).ToArray();
        foreach (var id in empty) _scheduler.CancelMatch(id, "Lobby has no remaining sessions.");
    }
    /// <summary>Clears reconnectable match state after a connected lobby
    /// leave.  This is deliberately not a permanent tombstone: a later
    /// session with the same Node connection identity may join a new lobby and
    /// must not inherit an unbounded forgotten-session poison set.</summary>
    public void ClearSessionMatchState(Guid sessionId)
    {
        lock (_gate)
        {
            _latest.TryRemove(sessionId, out _); _completions.TryRemove(sessionId, out _); _notifications.Remove(sessionId);
            _expectedTransitions.Remove(sessionId);
        }
    }

    /// <summary>Clears all session-owned replay state when the Node session
    /// has actually expired.  Kept separate from an ordinary lobby leave so
    /// reconnect grace and membership fencing retain their distinct meaning.</summary>
    public void ExpireSession(Guid sessionId) => ClearSessionMatchState(sessionId);
    public async IAsyncEnumerable<NodeMatchNotification> ReadNotifications([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var _ in _signal.Reader.ReadAllAsync(ct))
        {
            NodeMatchNotification[] batch;
            lock (_gate)
            {
                batch = _notifications.SelectMany(pair => pair.Value).ToArray();
                _notifications.Clear();
            }
            foreach (var notification in batch)
                if (IsNotificationCurrent(notification))
                    yield return notification;
        }
    }

    public async Task<object> ExecuteAsync(LobbyIdentity identity, NodeCommand command,
        CancellationToken cancellationToken = default)
    {
        identity.Validate();
        if (command is LobbyConfigure configure)
            _content.Validate(configure.MapKey, configure.Mode);
        if (command is NodeMatchRejoin rejoin)
        {
            using var rejoinActivity = NodeMetrics.StartActivity("match.rejoin");
            long rejoinStarted = _clock.GetTimestamp();
            MatchLifecycle pendingMatch;
            MatchPlacement placement;
            FrozenLobbyMember member;
            LobbySnapshot? lobby = _lobbies.ForSession(identity.SessionId);
            lock (_gate)
            {
                if (lobby?.Phase != LobbyPhase.InMatch || lobby.CurrentMatchId != rejoin.MatchId
                    || !_matches.TryGetValue(new(rejoin.MatchId), out pendingMatch!)
                    || pendingMatch.Placement == null
                    || pendingMatch.State != MatchLifecycleState.Running)
                    throw new LobbyCommandException("phase", "This session has no active match reservation.");
                member = pendingMatch.Members.SingleOrDefault(m => m.SessionId == identity.SessionId
                    && m.IdentityKey == identity.IdentityKey)
                    ?? throw new LobbyCommandException("identity", "This session does not own the frozen reservation.");
                placement = pendingMatch.Placement;
            }
            // The waiter may be tied to a socket, but the producer is owned by
            // the Node and therefore continues after that waiter disconnects.
            rejoinActivity?.SetTag("match.id", rejoin.MatchId);
            rejoinActivity?.SetTag("session.id", identity.SessionId);
            try
            {
                NodeMatchHandoff handoff = await EnsureHandoffAsync(pendingMatch, placement,
                    member, forceFresh: true, cancellationToken: cancellationToken);
                NodeMetrics.RecordDuration(NodeMetrics.RejoinDuration, _clock,
                    rejoinStarted, "success");
                return handoff;
            }
            catch
            {
                NodeMetrics.RecordDuration(NodeMetrics.RejoinDuration, _clock,
                    rejoinStarted, "failure");
                NodeMetrics.RejoinFailures.Add(1);
                throw;
            }
        }
        if (command is LobbyReturn returning)
        {
            LobbySnapshot? currentLobby = _lobbies.ForSession(identity.SessionId);
            if (currentLobby?.Phase != LobbyPhase.InMatch)
                return _lobbies.ReturnToLobby(identity.SessionId,
                    returning.ExpectedRevision);
            LobbyMatchTransitionSelection selection =
                _lobbies.RequestActiveMatchReturn(identity,
                    returning.ExpectedRevision);
            await BeginTransitionAsync(selection.MatchId,
                selection.TransitionId).ConfigureAwait(false);
            return _lobbies.ForSession(identity.SessionId)
                ?? throw new LobbyCommandException("interrupted",
                    "Lobby membership changed while returning from the match.");
        }
        // Keep the legacy wire shape decodable for older clients, but never
        // reinterpret it as ReturnToLobby. Rematch is a Node-owned post-match
        // ballot and must carry its authoritative ballot revision/option.
        if (command is LobbyRematch)
            throw new LobbyCommandException("unsupported",
                "Rematch must be selected from the post-match ballot.");
        if (command is not LobbyStart start)
        {
            var response = _lobbies.Execute(identity, command);
            if (response is NodeMatchTransitionVoteSnapshot transition
                && transition.State == MatchTransitionVoteState.Approved)
                await BeginTransitionAsync(transition.MatchId, transition.TransitionId);
            if (response is LobbyLeft) ClearSessionMatchState(identity.SessionId);
            return response;
        }
        var before = _lobbies.ForSession(identity.SessionId) ?? throw new LobbyCommandException("not_joined", "Join a lobby first.");
        _content.Validate(before.MapKey, before.Mode);
        var content = _content.Get(before.MapKey);
        // Re-read and validate immediately before the state-freezing call. The
        // expected revision protects this cross-call boundary without holding
        // the coordinator gate while LobbyManager takes its own lock.
        var current = _lobbies.ForSession(identity.SessionId)
            ?? throw new LobbyCommandException("not_joined", "Join a lobby first.");
        _content.Validate(current.MapKey, current.Mode);
        content = _content.Get(current.MapKey);
        MatchSpec spec = _lobbies.PrepareMatch(identity.SessionId, start.ExpectedRevision, content, _workers.NodeId, _workers.NodeIncarnation);
        LobbySnapshot prepared = _lobbies.ForSession(identity.SessionId)
            ?? throw new LobbyCommandException("interrupted", "Match preparation is no longer current.");
        MatchLifecycle pending = CreatePending(spec, prepared.Members.ToArray(), prepared);
        lock (_gate) _matches.Add(spec.MatchId, pending);
        NodeDiagnostics.Lifecycle(_logger, "match", "started", spec.MatchId.Value);
        await PlaceAsync(spec, cancellationToken);
        return _lobbies.ForSession(identity.SessionId) ?? before;
    }
    public async Task RunContinuationsAsync(CancellationToken ct)
    {
        // One owned loop and at most one in-flight placement per lobby. The
        // Worker event reader only changes state; it never awaits placement.
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var active = new List<Task>();
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                lock (_gate) if (_disposed) break;
                foreach (var done in active.Where(task => task.IsCompleted).ToArray())
                { await done; active.Remove(done); }
                // Approval can be produced by the Node's owned ballot
                // continuation (for example when electorate pruning makes the
                // threshold decisive), not only by the command that cast the
                // final vote. Discover it on the same loop and let the normal
                // atomic coordinator path claim it exactly once.
                CheckTransitionWatchdogs();
                DiscoverApprovedTransitions();
                DiscoverTransitionFailures();
                List<(MatchSpec Spec, MatchLifecycle Lifecycle)> prepared = [];
                List<(Guid SessionId, NodeMatchEnded Message)> failuresToNotify = [];
                var continuations = _lobbies.PrepareContinuations(_workers.NodeId, _workers.NodeIncarnation, 64 - active.Count, out var failures);
                foreach (var (spec, members) in continuations)
                {
                    LobbySnapshot? preparedSnapshot = members.Where(member => member.SessionId != Guid.Empty)
                        .Select(member => _lobbies.ForSession(member.SessionId))
                        .FirstOrDefault(snapshot => snapshot?.CurrentMatchId == spec.MatchId.Value);
                    if (preparedSnapshot == null) continue;
                    MatchLifecycle pending = CreatePending(spec, members, preparedSnapshot);
                    lock (_gate)
                    {
                        if (_disposed) continue;
                        _matches.Add(spec.MatchId, pending);
                        prepared.Add((spec, pending));
                    }
                }
                lock (_gate) if (_disposed) continue;
                foreach (var failure in failures)
                    foreach (LobbyMember member in failure.Members)
                        if (_lobbies.MembershipForSession(member.SessionId) is
                            { } current && current.LobbyId == failure.LobbyId)
                            failuresToNotify.Add((member.SessionId,
                                new NodeMatchEnded(failure.MatchId, true,
                                    MatchLifecycleEpoch.Initial, current.Generation,
                                    failure.LobbyId)));
                foreach (var failure in failuresToNotify) Notify(failure.SessionId, failure.Message);
                foreach (var (spec, _) in prepared) active.Add(ContinueAsync(spec, ct));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { await Task.WhenAll(active); }
    }
    private async Task ContinueAsync(MatchSpec spec, CancellationToken ct)
    {
        NodeDiagnostics.Lifecycle(_logger, "continuation", "started", spec.MatchId.Value);
        try
        {
            await PlaceAsync(spec, ct).ConfigureAwait(false);
        }
        catch (LobbyCommandException)
        {
            // PlaceAsync has already published the bounded, player-safe
            // placement failure and performed its normal cleanup.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested
            || _lifetimeToken.IsCancellationRequested)
        {
            // Shutdown/request cancellation is not a continuation failure. The
            // owned placement remains subject to the normal Worker terminal
            // path rather than being converted into a second lobby outcome.
        }
        catch (Exception error)
        {
            // A continuation is one item in a shared loop. Never allow an
            // unexpected observer/placement exception to fault that loop or
            // strand the exact Worker reservation it was preparing.
            NodeDiagnostics.Worker(_logger, "continuation", "failed");
            NodeDiagnostics.Lifecycle(_logger, "continuation", "failed", spec.MatchId.Value);
            _logger.LogError(error,
                "Unexpected continuation failure; attempting fenced recovery for {MatchId}.",
                spec.MatchId.Value);
            try
            {
                await RecoverUnexpectedContinuationAsync(spec).ConfigureAwait(false);
            }
            catch (Exception recoveryError)
            {
                // Recovery is deliberately a second containment boundary. A
                // failed cleanup must be visible to operators but cannot stop
                // other lobby continuations from progressing.
                NodeDiagnostics.Worker(_logger, "continuation_recovery", "failed");
                _logger.LogError(recoveryError,
                    "Continuation recovery failed for {MatchId}; Worker ownership remains fenced.",
                    spec.MatchId.Value);
            }
        }
    }

    /// <summary>
    /// Recovers an unexpected failure before the MatchReady/commit boundary.
    /// The scheduler fence is established before the coordinator record is
    /// retired, so a terminal raised by the cancellation command cannot leave
    /// the lobby without an owner. A committed/running match is intentionally
    /// left alone: an observer callback after commit must not cancel a valid
    /// gameplay lifecycle.
    /// </summary>
    private async Task RecoverUnexpectedContinuationAsync(MatchSpec spec)
    {
        MatchLifecycle pending;
        MatchPlacement? placement;
        lock (_gate)
        {
            if (!_matches.TryGetValue(spec.MatchId, out MatchLifecycle? current))
                return;
            if (current.State is not (MatchLifecycleState.PreparingWorker
                or MatchLifecycleState.InstallingAdmissions
                or MatchLifecycleState.ReadyToCommit))
                return;
            pending = current;
            placement = current.Placement;
            current.BeginRetirement();
        }

        // A scheduler assignment can exist before the lifecycle has received
        // its ready placement. Capture its exact Worker as well so a failed
        // cancellation can still be fenced without guessing at ownership.
        WorkerId? workerId = placement?.WorkerId;
        bool assignmentPresent = _scheduler.TryGetAssignment(spec.MatchId,
            out WorkerMatchAssignment? assignment);
        if (assignmentPresent && assignment is not null)
            workerId ??= assignment.WorkerId;

        // Fence the exact scheduler placement before reopening the lobby. The
        // scheduler retains capacity until Worker terminal evidence arrives;
        // this method never disposes a shared Worker or fabricates completion.
        bool cancelAccepted = false;
        try
        {
            cancelAccepted = _scheduler.CancelMatch(spec.MatchId,
                "Unexpected continuation failure.");
        }
        catch (Exception error)
        {
            NodeDiagnostics.Worker(_logger, "continuation_cancel", "failed");
            _logger.LogError(error,
                "Continuation cancellation failed for {MatchId}; attempting Worker quarantine.",
                spec.MatchId.Value);
        }

        // If the scheduler no longer has an assignment, there is no Worker
        // reservation left to fence. Otherwise a failed enqueue must quarantine
        // the exact assigned Worker before recovery can proceed.
        bool fenced = cancelAccepted || !assignmentPresent;
        if (!fenced && workerId is { } fencedWorker)
        {
            try
            {
                fenced = _scheduler.QuarantineWorker(fencedWorker,
                    "Unexpected continuation failure; cancellation enqueue failed.");
            }
            catch (Exception error)
            {
                NodeDiagnostics.Worker(_logger, "continuation_quarantine", "failed");
                _logger.LogError(error,
                    "Continuation Worker quarantine failed for {MatchId} on {WorkerId}.",
                    spec.MatchId.Value, fencedWorker.Value);
            }
        }

        // Cancellation/quarantine is only a fence, never terminal evidence.
        // Keep the coordinator owner in Retiring until the scheduler's normal
        // terminal/Worker-loss callback proves that the placement is finished.
        // If no assignment remains, the scheduler itself has already supplied
        // the equivalent retirement proof and the old preparation can close.
        bool recoverImmediately = false;
        bool terminalWon = false;
        lock (_gate)
        {
            if (!_matches.TryGetValue(spec.MatchId, out MatchLifecycle? current)
                || !ReferenceEquals(current, pending)
                || current.State == MatchLifecycleState.Running)
            {
                // A concurrent terminal already owns the normal boundary, or
                // an observer callback completed after commit. Do not invent a
                // second outcome; the captured admissions can still be
                // retired idempotently below when the lifecycle disappeared.
                terminalWon = !_matches.ContainsKey(spec.MatchId);
            }
            else if (assignmentPresent && workerId is { } knownWorker)
            {
                _continuationRecoveries.TryAdd(spec.MatchId,
                    new PendingContinuationRecovery(spec.MatchId, knownWorker,
                        _clock.GetUtcNow(), _options));
            }
            else
            {
                // No scheduler assignment remains. This is the only path that
                // may complete locally; it does not treat an accepted cancel as
                // terminal evidence.
                _matches.Remove(spec.MatchId);
                current.Retire();
                recoverImmediately = true;
            }
        }

        if (terminalWon)
        {
            try
            {
                await CleanupInstalledAdmissionsAsync(pending, _lifetimeToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error)
            {
                NodeDiagnostics.Worker(_logger, "continuation_admission_cleanup", "failed");
                _logger.LogError(error,
                    "Continuation admission cleanup failed for terminal {MatchId}.",
                    spec.MatchId.Value);
            }
            return;
        }

        if (!fenced)
        {
            NodeDiagnostics.Worker(_logger, "continuation_recovery", "fence_unestablished");
            _logger.LogError(
                "Continuation recovery could not fence the Worker for {MatchId}; lifecycle remains retained.",
                spec.MatchId.Value);
            return;
        }

        try
        {
            await CleanupInstalledAdmissionsAsync(pending, _lifetimeToken)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            NodeDiagnostics.Worker(_logger, "continuation_admission_cleanup", "failed");
            _logger.LogError(error,
                "Continuation admission cleanup failed for {MatchId}.",
                spec.MatchId.Value);
        }

        if (!recoverImmediately)
            return;

        // MatchEnded(interrupted) is the existing authoritative reopen boundary
        // for a preparation that never acquired a scheduler assignment. It is
        // intentionally not called for a fenced Worker: that path waits for
        // Ended/Worker-loss proof and uses the normal terminal callback.
        try
        {
            _lobbies.MatchEnded(spec.MatchId, interrupted: true);
        }
        catch (Exception error)
        {
            NodeDiagnostics.Worker(_logger, "continuation_reopen", "failed");
            _logger.LogError(error,
                "Continuation lobby recovery failed for {MatchId}.",
                spec.MatchId.Value);
        }
    }
    private async Task PlaceAsync(MatchSpec spec, CancellationToken ct = default)
    {
        using var activity = NodeMetrics.StartActivity("match.prepare");
        activity?.SetTag("match.id", spec.MatchId.Value);
        activity?.SetTag("lobby.id", spec.LobbyId.Value);
        activity?.SetTag("lifecycle.epoch", spec.LifecycleEpoch.Value);
        long preparationStarted = _clock.GetTimestamp();
        using CancellationTokenSource? linkedCancellation = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken) : null;
        CancellationToken effectiveCancellation = linkedCancellation?.Token ?? _lifetimeToken;
        MatchLifecycle? pending = null;
        try
        {
            // Placement lifetime belongs to Node, not a socket that can reconnect.
            MatchPlacement placement = await _scheduler.PlaceAsync(spec, effectiveCancellation);
            lock (_gate)
            {
                if (!_matches.TryGetValue(spec.MatchId, out pending!))
                    throw new LobbyCommandException("interrupted", "Match ended during placement.");
                if (pending.Placement != null)
                    throw new LobbyCommandException("interrupted", "Match ended during placement.");
                pending.BeginAdmissionInstallation(placement);
            }
            FrozenLobbyMember[] members = pending.Members.Where(member => member.SessionId != Guid.Empty).ToArray();
            NodeMatchHandoff[] handoffs = await Task.WhenAll(members.Select(member =>
                EnsureHandoffAsync(pending, placement, member, forceFresh: false, effectiveCancellation)));
            lock (_gate) pending.AdmissionsInstalled();
            // This hook remains outside the coordinator and lobby gates. It is
            // a deterministic seam for presentation-only updates (which must
            // not invalidate the frozen membership) and membership changes
            // (which must fail the commit and clean up exact admissions).
            _options.BeforeMatchCommit?.Invoke(spec.MatchId);
            using var commitActivity = NodeMetrics.StartActivity("match.commit");
            commitActivity?.SetTag("match.id", spec.MatchId.Value);
            commitActivity?.SetTag("lobby.id", spec.LobbyId.Value);
            commitActivity?.SetTag("lifecycle.epoch", spec.LifecycleEpoch.Value);
            // Terminal callbacks take the same coordinator gate. Revalidate,
            // commit, and enqueue the complete immutable handoff batch while
            // holding it so a Worker terminal cannot split the publication or
            // enqueue an ended event before a stale handoff.
            NodeMatchHandoff[] committedHandoffs;
            lock (_gate)
            {
                if (!_matches.TryGetValue(spec.MatchId, out MatchLifecycle? current)
                    || !ReferenceEquals(current, pending)
                    || current.Placement != placement
                    || current.Generation != pending.Generation)
                    throw new LobbyCommandException("interrupted",
                        "Match ended before handoff publication.");
                if (!_lobbies.CommitMatchReady(placement, pending.PreparationOwnerSessionId,
                    pending.LifecycleEpoch, pending.Members))
                    throw new LobbyCommandException("stale_preparation",
                        "Match preparation changed before admission completed.");
                pending.Commit();
                committedHandoffs = handoffs;
                foreach (var pair in members.Zip(committedHandoffs))
                    if (IsPayloadCurrentLocked(pair.First.SessionId, pair.Second))
                        _lobbies.WithCurrentHandoffSnapshot(pair.First.SessionId,
                            pair.Second.MatchId, pair.Second.LifecycleEpoch,
                            pair.Second.MembershipGeneration,
                            snapshot =>
                            {
                                // LobbyManager holds its gate while this
                                // callback runs, so the membership recheck and
                                // notification enqueue cannot be split by a
                                // concurrent leave/rejoin.
                                if (!IsPayloadCurrentLocked(pair.First.SessionId,
                                        pair.Second)) return false;
                                EnqueueLocked(pair.First.SessionId, pair.Second,
                                    snapshot);
                                return true;
                            });
            }
            // The complete immutable batch is already queued under the
            // coordinator gate. Do not invoke callbacks while either lifecycle
            // gate is held; terminal callbacks now observe either the entire
            // handoff batch or no committed match at all.
            _options.AfterMatchCommitted?.Invoke(spec.MatchId);
            NodeMetrics.RecordDuration(NodeMetrics.MatchPreparationDuration, _clock,
                preparationStarted, "success");
            return;
        }
        catch (Exception ex) when (ex is WorkerPlacementException or TimeoutException or OperationCanceledException
            or LobbyCommandException or ArgumentException or InvalidOperationException
            or ObjectDisposedException)
        {
            // Match and exception text are intentionally omitted: event IDs and
            // finite outcomes are sufficient for operational correlation.
            NodeDiagnostics.Worker(_logger, "placement", "failed");
            NodeDiagnostics.Lifecycle(_logger, "match", ex is LobbyCommandException { Code: "stale_preparation" }
                ? "commit_rejected" : "startup_failed", spec.MatchId.Value);
            NodeMetrics.RecordDuration(NodeMetrics.MatchPreparationDuration, _clock,
                preparationStarted, "failure");
            NodeMetrics.MatchPreparationFailures.Add(1);
            if (pending != null) await CleanupInstalledAdmissionsAsync(pending, effectiveCancellation);
            Ended(spec.MatchId, true);
            _scheduler.CancelMatch(spec.MatchId, "Lobby placement failed.");
            throw new LobbyCommandException("placement_failed", "Match could not be placed; the lobby remains available.");
        }
    }

    private async Task CleanupInstalledAdmissionsAsync(MatchLifecycle pending,
        CancellationToken cancellationToken)
    {
        (FrozenLobbyMember Member, InstallAdmissionKey Command)[] installed;
        lock (_gate)
        {
            installed = pending.Handoffs
                .Where(pair => pair.Value.Installed is not null)
                .Select(pair => (pending.Members.Single(member => member.SessionId == pair.Key),
                    pair.Value.Installed!))
                .ToArray();
            foreach ((Guid sessionId, HandoffState state) in pending.Handoffs)
            {
                state.Installed = null;
                state.Value = null;
                state.ExpiresAt = 0;
                _latest.TryRemove(sessionId, out _);
            }
        }

        foreach (var (member, install) in installed)
        {
            try
            {
                await _scheduler.RetireAdmissionAsync(new RetireAdmission(
                    pending.Spec.MatchId, member.SessionId, install.SeatId,
                    install.HandoffGeneration, install.AdmissionId,
                    install.WorkerId, install.WorkerIncarnation), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (error is MatchControlException or TimeoutException
                or OperationCanceledException)
            {
                // The exact admission identity is still fenced by Worker
                // authority; a retirement transport failure must not block the
                // match terminal cancellation path.
                _logger.LogWarning(error, "Admission cleanup was not acknowledged before match cancellation.");
            }
        }
    }

    private MatchLifecycle CreatePending(MatchSpec spec, LobbyMember[] members,
        LobbySnapshot prepared)
    {
        if (prepared.CurrentMatchId != spec.MatchId.Value
            || prepared.Phase != LobbyPhase.StartingMatch
            || prepared.LifecycleEpoch.Value == 0
            || prepared.OwnerSessionId == Guid.Empty
            || spec.LifecycleEpoch != prepared.LifecycleEpoch)
            throw new LobbyCommandException("interrupted", "Match preparation is no longer current.");
        spec.Validate();
        if (!_lobbies.TryFreezeMembers(prepared.LobbyId, members,
                out FrozenLobbyMember[] frozen))
            throw new LobbyCommandException("interrupted",
                "Match membership changed before the roster could be frozen.");
        return new MatchLifecycle(spec, frozen, prepared.OwnerSessionId);
    }

    private Task BeginTransitionAsync(Guid matchId, Guid transitionId)
    {
        MatchId oldMatch = new(matchId);
        using var activity = NodeMetrics.StartActivity("match.transition");
        activity?.SetTag("match.id", matchId);
        activity?.SetTag("transition.id", transitionId);
        PendingTransition? pending = null;
        lock (_gate)
        {
            if (_disposed || _transitions.ContainsKey(oldMatch)) return Task.CompletedTask;
            if (!_matches.TryGetValue(oldMatch, out MatchLifecycle? match) || match.Placement == null)
                return Task.CompletedTask;

            // This is deliberately inside the coordinator gate: ordinary
            // Completed/Ended callbacks cannot retire the pending placement
            // between tentative ownership and the scheduler's atomic claim.
            if (!_scheduler.TryClaimTransition(oldMatch, out WorkerMatchAssignment? assignment)
                || assignment == null)
                return Task.CompletedTask;
            if (!_lobbies.TryBeginMatchTransition(oldMatch.Value, transitionId,
                out LobbyMatchTransitionSelection? selection)
                || selection == null)
            {
                _scheduler.RollbackTransitionClaim(oldMatch);
                return Task.CompletedTask;
            }
            pending = new PendingTransition(selection, assignment, _clock.GetUtcNow(),
                _clock.GetTimestamp(), _options);
            match.BeginRetirement();
            _transitions.Add(oldMatch, pending);
            foreach (LobbyMember member in selection.Members)
            {
                if (selection.ReturnToLobby)
                    continue;
                MembershipGeneration generation = SelectionGeneration(selection, member.SessionId);
                if (generation.Value == 0
                    || !IsCurrentMembershipLocked(member.SessionId,
                        selection.LobbyId, generation)) continue;
                _expectedTransitions[member.SessionId] = new NodeMatchTransitionStarted(
                    selection.LobbyId, selection.MatchId, selection.TransitionId,
                    selection.Choice, selection.TargetMapKey, selection.Mode,
                    selection.LifecycleEpoch, generation);
                _completions.TryRemove(member.SessionId, out _);
            }
        }

        // Publish before cancellation. Notify is queue-owned and does not hold
        // the scheduler gate; every affected session observes the same frozen
        // transition identity.
        foreach (LobbyMember member in pending.Selection.Members)
        {
            if (pending.Selection.ReturnToLobby)
                continue;
            MembershipGeneration generation = SelectionGeneration(pending.Selection,
                member.SessionId);
            if (generation.Value == 0) continue;
            NodeMatchTransitionStarted started = new(pending.Selection.LobbyId,
                pending.Selection.MatchId, pending.Selection.TransitionId,
                pending.Selection.Choice, pending.Selection.TargetMapKey,
                pending.Selection.Mode, pending.Selection.LifecycleEpoch,
                generation);
            started.Validate();
            Notify(member.SessionId, started);
        }

        bool cancelSent = _scheduler.TryCancelTransition(oldMatch,
            "Lobby approved a match transition.");
        _scheduler.TryGetCancellationSnapshot(oldMatch,
            out WorkerCancellationSnapshot cancellationSnapshot);
        bool completeNow = false;
        lock (_gate)
        {
            if (_transitions.TryGetValue(oldMatch, out PendingTransition? current))
            {
                // A terminal event can arrive before TryCancelTransition
                // returns. Terminal evidence is sufficient even when the
                // cancellation receipt is reordered or missing.
                if (current.TerminalAcknowledged || cancellationSnapshot.TerminalObserved)
                {
                    current.TerminalAcknowledged = true;
                    current.Phase = TransitionPhase.Completed;
                    _transitions.Remove(oldMatch);
                    if (_matches.Remove(oldMatch, out MatchLifecycle? lifecycle))
                        lifecycle.Retire();
                    completeNow = true;
                }
                else if (cancelSent || cancellationSnapshot.Sent)
                    current.Phase = TransitionPhase.AwaitingTerminal;
                // If the initial send failed, retain the transition and its
                // operation identity. CheckTransitionWatchdogs will perform
                // the one permitted retry after the acknowledgement window.
            }
        }
        if (completeNow) CompleteTransition(oldMatch, pending);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Advances cancellation transitions without relying on wall-clock sleeps.
    /// The coordinator owns the deadlines, while WorkerScheduler owns the
    /// cancellation operation and its terminal observation. A terminal event
    /// wins over both deadlines; an expired transition is fenced before its
    /// Worker is quarantined so late events cannot create a replacement lobby
    /// or handoff.
    /// </summary>
    public void CheckTransitionWatchdogs()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        CheckContinuationRecoveryWatchdogs(now);

        KeyValuePair<MatchId, PendingTransition>[] transitions;
        lock (_gate) transitions = _transitions.ToArray();

        List<(MatchId MatchId, PendingTransition Pending, bool TimedOut)> complete = [];
        List<(MatchId MatchId, PendingTransition Pending)> timedOut = [];
        List<MatchId> retry = [];

        foreach (var pair in transitions)
        {
            _scheduler.TryGetCancellationSnapshot(pair.Key,
                out WorkerCancellationSnapshot snapshot);
            lock (_gate)
            {
                if (!_transitions.TryGetValue(pair.Key, out PendingTransition? current)
                    || !ReferenceEquals(current, pair.Value))
                    continue;

                if (current.TerminalAcknowledged || snapshot.TerminalObserved)
                {
                    current.TerminalAcknowledged = true;
                    bool transitionTimedOut = current.Fenced
                        || current.Phase == TransitionPhase.TimedOut;
                    current.Phase = transitionTimedOut
                        ? TransitionPhase.TimedOut
                        : TransitionPhase.Completed;
                    _transitions.Remove(pair.Key);
                    if (_matches.Remove(pair.Key, out MatchLifecycle? lifecycle))
                        lifecycle.Retire();
                    complete.Add((pair.Key, current, transitionTimedOut));
                    continue;
                }

                if (current.Phase == TransitionPhase.TimedOut)
                    continue;

                if (now >= current.HardDeadline)
                {
                    current.Phase = TransitionPhase.TimedOut;
                    current.Fenced = true;
                    timedOut.Add((pair.Key, current));
                    continue;
                }

                if (!current.RetrySent && now >= current.AcknowledgementDeadline
                    && snapshot.OperationId is { Length: > 0 })
                {
                    current.RetrySent = true;
                    current.Phase = TransitionPhase.AwaitingTerminal;
                    retry.Add(pair.Key);
                }
            }
        }

        foreach (var (matchId, pending, transitionTimedOut) in complete)
        {
            if (transitionTimedOut) CompleteTimedOutTransition(matchId, pending);
            else CompleteTransition(matchId, pending);
        }

        foreach (MatchId matchId in retry)
            _scheduler.TryRetryTransitionCancellation(matchId,
                "Retrying the transition cancellation receipt.");

        foreach (var (matchId, pending) in timedOut)
        {
            // Keep transition ownership until exact terminal/Worker-loss
            // proof. Reverting the claim here would allow a late completion
            // and report to re-enter the ordinary match lifecycle.
            _scheduler.QuarantineWorker(pending.Assignment.WorkerId,
                "Transition terminal deadline exceeded.");
            _lobbies.FailMatchTransition(matchId.Value,
                pending.Selection.TransitionId, "transition_timeout");
            NodeMetrics.RecordDuration(NodeMetrics.TransitionDuration, _clock,
                pending.StartedTimestamp, "timeout");
            NodeMetrics.TransitionTimeouts.Add(1);
            PublishTransitionFailure(pending.Selection);
            StartForcedWorkerRetirement(pending.Assignment.WorkerId);
        }
    }

    /// <summary>Applies the same bounded cancellation/retirement deadlines to
    /// an unexpected continuation. A cancellation acknowledgement is only
    /// progress; the lifecycle stays Retiring until the scheduler delivers its
    /// terminal or Worker-loss callback.</summary>
    private void CheckContinuationRecoveryWatchdogs(DateTimeOffset now)
    {
        KeyValuePair<MatchId, PendingContinuationRecovery>[] recoveries;
        lock (_gate) recoveries = _continuationRecoveries.ToArray();

        List<MatchId> retry = [];
        List<(MatchId MatchId, WorkerId WorkerId)> forceRetirement = [];
        List<MatchId> retired = [];
        foreach (var pair in recoveries)
        {
            _scheduler.TryGetCancellationSnapshot(pair.Key,
                out WorkerCancellationSnapshot cancellation);
            bool assignmentPresent = _scheduler.TryGetAssignment(pair.Key,
                out _);
            lock (_gate)
            {
                if (!_continuationRecoveries.TryGetValue(pair.Key,
                        out PendingContinuationRecovery? current)
                    || !ReferenceEquals(current, pair.Value))
                    continue;
                if (!_matches.TryGetValue(pair.Key, out MatchLifecycle? lifecycle))
                {
                    // The normal Ended/Worker-loss callback may have removed
                    // the coordinator owner just before this watchdog pass.
                    _continuationRecoveries.Remove(pair.Key);
                    continue;
                }
                if (lifecycle.State != MatchLifecycleState.Retiring)
                {
                    _continuationRecoveries.Remove(pair.Key);
                    continue;
                }
                if (!assignmentPresent)
                {
                    // Scheduler retirement is an explicit ownership proof,
                    // but it is still completed through the normal terminal
                    // boundary below rather than by inventing a Worker event.
                    _continuationRecoveries.Remove(pair.Key);
                    retired.Add(pair.Key);
                    continue;
                }
                if (now >= current.HardDeadline
                    && !current.ForceRetirementStarted)
                {
                    current.ForceRetirementStarted = true;
                    forceRetirement.Add((pair.Key, current.WorkerId));
                    NodeDiagnostics.Lifecycle(_logger, "continuation", "timeout", pair.Key.Value);
                    continue;
                }
                if (!current.RetrySent
                    && now >= current.AcknowledgementDeadline
                    && cancellation.OperationId is { Length: > 0 })
                {
                    // WorkerScheduler reuses the existing operation identity;
                    // a retry never becomes a second cancellation operation.
                    current.RetrySent = true;
                    retry.Add(pair.Key);
                }
            }
        }

        foreach (MatchId matchId in retry)
            _scheduler.CancelMatch(matchId,
                "Retrying unexpected continuation cancellation.");

        foreach (var (matchId, workerId) in forceRetirement)
        {
            try
            {
                if (!_scheduler.QuarantineWorker(workerId,
                        "Continuation cancellation exceeded its terminal deadline."))
                    NodeDiagnostics.Worker(_logger, "continuation_quarantine", "failed");
            }
            catch (Exception error)
            {
                NodeDiagnostics.Worker(_logger, "continuation_quarantine", "failed");
                _logger.LogError(error,
                    "Continuation Worker quarantine failed for {MatchId} on {WorkerId}.",
                    matchId.Value, workerId.Value);
            }
            StartForcedWorkerRetirement(workerId);
        }

        foreach (MatchId matchId in retired)
            Ended(matchId, interrupted: true);
    }

    private void StartForcedWorkerRetirement(WorkerId workerId)
    {
        lock (_gate)
        {
            if (_disposed || _forcedWorkerRetirements.ContainsKey(workerId)) return;
            Task retirement = _scheduler.ForceRetireWorkerAsync(workerId, _lifetimeToken);
            _forcedWorkerRetirements.Add(workerId, retirement);
            _ = retirement.ContinueWith(completed =>
            {
                if (completed.IsFaulted)
                    _logger.LogError(completed.Exception,
                        "Forced retirement failed for a transition-timed-out Worker.");
                lock (_gate) _forcedWorkerRetirements.Remove(workerId);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void DiscoverApprovedTransitions()
    {
        MatchId[] candidates;
        lock (_gate)
            candidates = _matches.Keys.Where(matchId => !_transitions.ContainsKey(matchId)).ToArray();
        foreach (MatchId matchId in candidates)
            if (_lobbies.TryGetApprovedMatchTransition(matchId.Value,
                    out LobbyMatchTransitionSelection? selection) && selection != null)
                _ = BeginTransitionAsync(selection.MatchId, selection.TransitionId);
    }

    /// <summary>Preparation failures are produced by LobbyManager's owned
    /// continuation loop, not by a Worker callback. Replay the failed ballot to
    /// affected sessions once and retire their expected Started marker so a
    /// reconnect cannot remain in PreparingContinuation forever.</summary>
    private void DiscoverTransitionFailures()
    {
        KeyValuePair<Guid, NodeMatchTransitionStarted>[] expected;
        lock (_gate) expected = _expectedTransitions.ToArray();
        foreach (var pair in expected)
        {
            if (_lobbies.MatchTransitionForSession(pair.Key) is not { } state
                || state.TransitionId != pair.Value.TransitionId
                || state.State != MatchTransitionVoteState.Failed)
                continue;
            Notify(pair.Key, state);
            lock (_gate)
                if (_expectedTransitions.TryGetValue(pair.Key, out var current)
                    && current.TransitionId == pair.Value.TransitionId)
                    _expectedTransitions.Remove(pair.Key);
        }
    }

    private void PublishTransitionFailure(LobbyMatchTransitionSelection selection)
    {
        foreach (LobbyMember member in selection.Members)
            if (_lobbies.MatchTransitionForSession(member.SessionId) is { } state)
                Notify(member.SessionId, state);
    }

    private void TransitionEnded(MatchId matchId, bool interrupted)
    {
        PendingTransition? pending;
        bool complete = false;
        bool timedOut = false;
        lock (_gate)
        {
            if (!_transitions.TryGetValue(matchId, out pending))
                return;
            pending.TerminalAcknowledged = true;
            timedOut = pending.Fenced || pending.Phase == TransitionPhase.TimedOut;
            pending.Phase = timedOut ? TransitionPhase.TimedOut : TransitionPhase.Completed;
            _transitions.Remove(matchId);
            if (_matches.Remove(matchId, out MatchLifecycle? lifecycle))
                lifecycle.Retire();
            complete = true;
        }
        if (!complete) return;
        if (timedOut) CompleteTimedOutTransition(matchId, pending);
        else CompleteTransition(matchId, pending);
    }

    private void CompleteTimedOutTransition(MatchId matchId,
        PendingTransition pending)
    {
        bool recovered = _lobbies.FailCompletedMatchTransition(matchId,
            pending.Selection.TransitionId, "transition_timeout",
            out LobbyMatchTransitionSelection? failed);
        if (recovered && failed != null) PublishTransitionFailure(failed);
        foreach (LobbyMember member in pending.Selection.Members)
        {
            MembershipGeneration generation = SelectionGeneration(pending.Selection,
                member.SessionId);
            if (generation.Value != 0)
                Notify(member.SessionId, new NodeMatchEnded(matchId.Value, true,
                    pending.Selection.LifecycleEpoch, generation,
                    pending.Selection.LobbyId));
        }
        lock (_gate)
            foreach (LobbyMember member in pending.Selection.Members)
                _expectedTransitions.Remove(member.SessionId);
    }

    private void CompleteTransition(MatchId matchId, PendingTransition pending)
    {
        bool completed;
        try
        {
            completed = _lobbies.CompleteMatchTransition(matchId.Value,
                pending.Selection.TransitionId);
        }
        catch (Exception error) when (error is LobbyCommandException or ArgumentException)
        {
            _logger.LogWarning(error, "Transition completion was rejected for {MatchId}", matchId.Value);
            completed = false;
        }
        if (!completed)
        {
            NodeMetrics.RecordDuration(NodeMetrics.TransitionDuration, _clock,
                pending.StartedTimestamp, "failure");
            // The old Worker has already acknowledged cancellation. Reopen
            // the lobby and retain a Failed transition snapshot; the ordinary
            // failed-cancel path intentionally keeps InMatch and is not safe
            // to reuse after this terminal boundary.
            bool recovered = _lobbies.FailCompletedMatchTransition(matchId,
                pending.Selection.TransitionId, "transition_ack_rejected",
                out LobbyMatchTransitionSelection? failed);
            if (recovered && failed != null)
            {
                PublishTransitionFailure(failed);
                foreach (LobbyMember member in failed.Members)
                {
                    MembershipGeneration generation = SelectionGeneration(failed,
                        member.SessionId);
                    if (generation.Value != 0)
                        Notify(member.SessionId, new NodeMatchEnded(matchId.Value, true,
                            pending.Selection.LifecycleEpoch, generation,
                            pending.Selection.LobbyId));
                }
            }
            else
            {
                // A concurrent ordinary completion may already own the lobby;
                // still release the intentional transition marker for every
                // frozen member.
                foreach (LobbyMember member in pending.Selection.Members)
                {
                    MembershipGeneration generation = SelectionGeneration(pending.Selection,
                        member.SessionId);
                    if (generation.Value != 0)
                        Notify(member.SessionId, new NodeMatchEnded(matchId.Value, true,
                            pending.Selection.LifecycleEpoch, generation,
                            pending.Selection.LobbyId));
                }
            }
            lock (_gate)
                foreach (LobbyMember member in pending.Selection.Members)
                    _expectedTransitions.Remove(member.SessionId);
            return;
        }
        NodeMetrics.RecordDuration(NodeMetrics.TransitionDuration, _clock,
            pending.StartedTimestamp, "success");
        // This is the intentional transition terminal marker, not an ordinary
        // result/interruption.  It follows Started and lets clients release
        // the old scene before accepting the fresh continuation handoff.
        foreach (LobbyMember member in pending.Selection.Members)
        {
            MembershipGeneration generation = SelectionGeneration(pending.Selection,
                member.SessionId);
            if (generation.Value != 0)
                Notify(member.SessionId, new NodeMatchEnded(matchId.Value, true,
                    pending.Selection.LifecycleEpoch, generation,
                    pending.Selection.LobbyId));
        }
    }
    private async Task<NodeMatchHandoff> EnsureHandoffAsync(MatchLifecycle pending, MatchPlacement placement,
        FrozenLobbyMember member, bool forceFresh, CancellationToken cancellationToken)
    {
        TaskCompletionSource<NodeMatchHandoff> completion;
        bool producer = false;
        HandoffGeneration generation = default;
        long placementGeneration;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_matches.TryGetValue(pending.Spec.MatchId, out var current) || !ReferenceEquals(current, pending)
                || current.Placement != placement || !current.Members.Any(candidate => candidate.SessionId == member.SessionId
                    && candidate.IdentityKey == member.IdentityKey))
                throw new LobbyCommandException("interrupted", "Match handoff is no longer current.");
            if (!pending.Handoffs.TryGetValue(member.SessionId, out HandoffState? state))
                pending.Handoffs.Add(member.SessionId, state = new());
            long now = _clock.GetUtcNow().ToUnixTimeSeconds();
            if (state.InFlight is { } existing)
            {
                // Concurrent explicit rejoin requests share one producer and
                // one generation. Do not advance the generation merely because
                // another waiter arrived while installation was in flight.
                completion = existing;
                generation = state.Generation;
            }
            else
            {
                if (!forceFresh && state.Value is { } cached && state.ExpiresAt > now + 1)
                    return cached;
                if (forceFresh)
                {
                    ulong prior = state.Generation.Value;
                    if (prior == ulong.MaxValue)
                        throw new LobbyCommandException("handoff_exhausted", "Admission retry capacity is exhausted.");
                    state.Generation = new HandoffGeneration(prior == 0 ? 1 : prior + 1);
                    state.Value = null; state.ExpiresAt = 0;
                    _latest.TryRemove(member.SessionId, out _);
                }
                else if (state.Generation.Value == 0)
                    state.Generation = HandoffGeneration.Initial;
                generation = state.Generation;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                state.InFlight = completion;
                producer = true;
            }
            placementGeneration = pending.Generation;
        }
        if (producer)
            _ = ProduceHandoffAsync(pending, placement, member, generation,
                placementGeneration, completion);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Owns one admission-key installation independently from any socket
    /// waiter. A disconnect cancels that waiter's observation only; the
    /// Node-owned install continues until the match lifetime ends and every
    /// exit settles the shared completion source.
    /// </summary>
    private async Task ProduceHandoffAsync(MatchLifecycle pending, MatchPlacement placement,
        FrozenLobbyMember member, HandoffGeneration generation, long placementGeneration,
        TaskCompletionSource<NodeMatchHandoff> completion)
    {
        using var activity = NodeMetrics.StartActivity("match.admission.install");
        activity?.SetTag("match.id", pending.Spec.MatchId.Value);
        activity?.SetTag("session.id", member.SessionId);
        activity?.SetTag("handoff.generation", generation.Value);
        long installStarted = _clock.GetTimestamp();
        InstallAdmissionKey? installed = null;
        try
        {
            var issued = CreateHandoff(pending, placement, member, generation);
            if (issued.Install is { } install)
            {
                await _scheduler.InstallAdmissionKeyAsync(install, _lifetimeToken).ConfigureAwait(false);
                installed = install;
                lock (_gate)
                    if (pending.Handoffs.TryGetValue(member.SessionId, out HandoffState? acceptedState))
                        acceptedState.Installed = install;
            }
            // Lobby membership is read outside the coordinator gate. A
            // departed session must never receive a newly installed key.
            if (_lifetimeToken.IsCancellationRequested
                || !IsCurrentMembership(member.SessionId, pending.Spec.LobbyId.Value,
                    member.Generation))
                throw new LobbyCommandException("interrupted", "Session membership changed before handoff publication.");
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_matches.TryGetValue(pending.Spec.MatchId, out var current) || !ReferenceEquals(current, pending)
                    || current.Generation != placementGeneration || current.Placement != placement
                    || !current.Members.Any(candidate => candidate.SessionId == member.SessionId
                        && candidate.IdentityKey == member.IdentityKey))
                    throw new LobbyCommandException("interrupted", "Match handoff became stale before publication.");
                HandoffState state = pending.Handoffs[member.SessionId];
                if (state.Generation != generation)
                    throw new LobbyCommandException("stale_generation", "Match handoff generation is no longer current.");
                if (!IsCurrentMembershipLocked(member.SessionId, pending.Spec.LobbyId.Value,
                        member.Generation))
                    throw new LobbyCommandException("interrupted",
                        "Session membership changed before handoff storage.");
                if (!IsPayloadCurrentLocked(member.SessionId, issued.Handoff))
                    throw new LobbyCommandException("interrupted",
                        "Session membership changed before handoff storage.");
                state.Value = issued.Handoff; state.ExpiresAt = issued.ExpiresAt; state.InFlight = null;
                _latest[member.SessionId] = issued.Handoff;
                completion.TrySetResult(issued.Handoff);
            }
            NodeMetrics.RecordDuration(NodeMetrics.AdmissionInstallDuration, _clock,
                installStarted, "success");
            NodeMetrics.RecordDuration(NodeMetrics.HandoffDuration, _clock,
                installStarted, "success");
        }
        catch (Exception error)
        {
            NodeMetrics.RecordDuration(NodeMetrics.AdmissionInstallDuration, _clock,
                installStarted, "failure");
            NodeMetrics.RecordDuration(NodeMetrics.HandoffDuration, _clock,
                installStarted, "failure");
            NodeMetrics.AdmissionInstallFailures.Add(1);
            NodeMetrics.HandoffFailures.Add(1);
            lock (_gate)
            {
                if (pending.Handoffs.TryGetValue(member.SessionId, out HandoffState? state)
                    && ReferenceEquals(state.InFlight, completion)) state.InFlight = null;
            }
            if (installed is { } accepted)
                await RetireInstalledAdmissionAsync(pending, placement, member, accepted).ConfigureAwait(false);
            completion.TrySetException(error);
        }
    }

    private async Task RetireInstalledAdmissionAsync(MatchLifecycle pending,
        MatchPlacement placement, FrozenLobbyMember member, InstallAdmissionKey install)
    {
        try
        {
            await _scheduler.RetireAdmissionAsync(new RetireAdmission(
                pending.Spec.MatchId, member.SessionId, install.SeatId,
                install.HandoffGeneration, install.AdmissionId,
                placement.WorkerId, placement.WorkerIncarnation), _lifetimeToken)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupError) when (cleanupError is MatchControlException
            or TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning(cleanupError, "Stale admission cleanup was not acknowledged.");
        }
    }

    private (NodeMatchHandoff Handoff, InstallAdmissionKey? Install, long ExpiresAt, long Generation) CreateHandoff(
        MatchLifecycle pending, MatchPlacement placement, FrozenLobbyMember member,
        HandoffGeneration generation)
    {
        var spec = pending.Spec;
        HumanIdentityKey identity = member.IdentityKey;
        RosterSeat seat = spec.Roster.Single(s => s.Role != SeatRole.Bot
            && (identity.Kind == HumanIdentityKind.Registered ? s.PlayerId?.Value == identity.Value
                : s.GuestSessionId == identity.Value));
        ulong nonce;
        do { nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); } while (nonce == 0);
        long now = _clock.GetUtcNow().ToUnixTimeSeconds();
        long expires = now + 120;
        Guid ticketId = Guid.NewGuid();
        Guid admissionId = Guid.NewGuid();
        MatchLifecycleEpoch lifecycleEpoch = pending.LifecycleEpoch;
        string ticket = _issuer.Issue(new(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation,
            spec.LobbyId, spec.MatchId, placement.WireMatchId, member.SessionId, seat.PlayerId, seat.GuestSessionId, seat.Role, seat.SeatId,
            seat.DisplayName, nonce, now, expires, ticketId, generation));
        string admissionKey = "";
        InstallAdmissionKey? install = null;
        if (placement.UdpAuthenticationEnabled)
        {
            byte[] rawKey = RandomNumberGenerator.GetBytes(AdmissionKeyRules.ByteLength);
            try { admissionKey = Convert.ToBase64String(rawKey); }
            finally { CryptographicOperations.ZeroMemory(rawKey); }
            install = new InstallAdmissionKey(admissionId, ticketId, member.SessionId,
                spec.NodeId, spec.NodeIncarnation, spec.MatchId, placement.WireMatchId,
                placement.WorkerId, placement.WorkerIncarnation, seat.SeatId, nonce, expires, admissionKey,
                generation);
        }
        var handoff = new NodeMatchHandoff(spec.MatchId.Value, placement.WireMatchId.Value, placement.Host, placement.Port,
            ticket, nonce, member.Observer, member.Hunter,
            placement.UdpAuthenticationEnabled ? admissionId : Guid.Empty, admissionKey,
            placement.UdpAuthenticationEnabled, generation, lifecycleEpoch,
            member.Generation);
        handoff.ValidateProduction();
        return (handoff, install, expires, pending.Generation);
    }
    private void Ended(MatchId matchId, bool interrupted)
    {
        MatchLifecycle? pending;
        bool continuationRecovery;
        lock (_gate)
        {
            // A transition claim owns terminal delivery. The dedicated
            // TransitionEnded callback performs the lobby boundary after the
            // Worker acknowledgement; this ordinary callback is stale.
            if (_transitions.ContainsKey(matchId)) return;
            if (!_matches.Remove(matchId, out pending))
            {
                _continuationRecoveries.Remove(matchId);
                return;
            }
            continuationRecovery = _continuationRecoveries.Remove(matchId);
            pending.Retire();
        }
        if (continuationRecovery)
        {
            // A failed pre-commit continuation never became a playable match.
            // Its first actual terminal/Worker-loss callback is therefore an
            // interrupted recovery boundary: reopen the lobby and publish only
            // that terminal marker, never a gameplay completion projection.
            _lobbies.MatchEnded(matchId, interrupted: true);
            NodeDiagnostics.Lifecycle(_logger, "match", "interrupted", matchId.Value);
            foreach (var member in pending.Members)
                Notify(member.SessionId, new NodeMatchEnded(matchId.Value, true,
                    pending.LifecycleEpoch, member.Generation, pending.Spec.LobbyId.Value));
            return;
        }
        // A replacement can fail before its MatchReady boundary. Preserve the
        // original transition identity and expose a bounded failure instead
        // of routing this pre-handoff terminal through ordinary post-match
        // interruption/results handling.
        if (_lobbies.FailPreparedMatchTransition(matchId,
            "replacement_placement_failed", out LobbyMatchTransitionSelection? failed))
        {
            if (failed != null) PublishTransitionFailure(failed);
            lock (_gate)
                foreach (LobbyMember member in failed?.Members ?? [])
                    _expectedTransitions.Remove(member.SessionId);
            return;
        }
        _lobbies.MatchEnded(matchId, interrupted);
        if (interrupted)
            NodeDiagnostics.Lifecycle(_logger, "match", "interrupted", matchId.Value);
        foreach (var member in pending.Members)
            Notify(member.SessionId, new NodeMatchEnded(matchId.Value, interrupted,
                pending.LifecycleEpoch, member.Generation, pending.Spec.LobbyId.Value));
    }

    private void Completed(MatchCompletionSummary summary)
    {
        summary.Validate();
        MatchLifecycle? pending;
        lock (_gate)
        {
            if (_transitions.ContainsKey(summary.MatchId)) return;
            // A continuation that failed before commit is retained only to
            // fence the Worker until its actual terminal/Worker-loss proof.
            // Never expose that failed replacement's completion as gameplay
            // state or let it replace the recovery boundary.
            if (_continuationRecoveries.ContainsKey(summary.MatchId)) return;
            if (!_matches.TryGetValue(summary.MatchId, out pending)
                || pending.Spec.LobbyId != summary.LobbyId) return;
            pending.BeginRetirement();
        }
        NodeDiagnostics.Lifecycle(_logger, "match", "completed", summary.MatchId.Value);
        // Membership is checked before the per-session replay slot is written,
        // not only at socket delivery. A session that left and rejoined another
        // lobby must not retain the old completion while the Worker callback
        // is racing that boundary.
        foreach (var member in pending.Members)
            if (IsCurrentMembership(member.SessionId, pending.Spec.LobbyId.Value,
                    member.Generation))
            {
                NodeMatchCompletion completion = new(summary, pending.LifecycleEpoch,
                    member.Generation);
                lock (_gate)
                    if (_matches.TryGetValue(summary.MatchId, out MatchLifecycle? current)
                        && ReferenceEquals(current, pending)
                        && IsPayloadCurrentLocked(member.SessionId, completion))
                        _completions[member.SessionId] = completion;
                Notify(member.SessionId, completion);
            }
    }
    private void Notify(Guid sessionId, object message)
    {
        lock (_gate)
        {
            // The membership check must be in the same gate as the write. A
            // leave can clear session state between an earlier check and this
            // enqueue, so a pre-check alone would allow stale replay to be
            // repopulated.
            if (IsPayloadCurrentLocked(sessionId, message))
                EnqueueLocked(sessionId, message);
        }
    }

    private void EnqueueLocked(Guid sessionId, object message,
        LobbySnapshot? handoffSnapshot = null)
    {
        if (_disposed) return;
        if (!IsPayloadCurrentLocked(sessionId, message)) return;
        if (message is NodeMatchHandoff handoff
            && _expectedTransitions.TryGetValue(sessionId, out NodeMatchTransitionStarted? expected)
            && handoff.MatchId != expected.PreviousMatchId)
            _expectedTransitions.Remove(sessionId);
        _latest[sessionId] = message;
        if (!_notifications.TryGetValue(sessionId, out var queue))
            _notifications.Add(sessionId, queue = new());
        if (queue.TryPeek(out NodeMatchNotification? first)
            && first.Payload is NodeMatchDeliveryOverflow) return;
        // Never silently replace an ended event with the following handoff.
        // An exhausted consumer explicitly loses its connection and must resume.
        if (queue.Count == MaximumPendingEventsPerSession)
        {
            queue.Clear();
            queue.Enqueue(new NodeMatchNotification(sessionId,
                new NodeMatchDeliveryOverflow()));
        }
        else queue.Enqueue(new NodeMatchNotification(sessionId, message,
            message is NodeMatchHandoff ? handoffSnapshot : null));
        _signal.Writer.TryWrite(true);
    }

    private bool IsCurrentMembershipLocked(Guid sessionId, Guid lobbyId,
        MembershipGeneration membershipGeneration)
    {
        // Node-owned lifecycle payloads are never valid without an explicit
        // recipient boundary. Compatibility is retained only for payload
        // types that do not carry this field at all.
        if (lobbyId == Guid.Empty || membershipGeneration.Value == 0) return false;
        return _lobbies.IsCurrentMembership(sessionId, lobbyId, membershipGeneration);
    }

    private bool IsCurrentMembership(Guid sessionId, Guid lobbyId,
        MembershipGeneration membershipGeneration)
    {
        lock (_gate)
            return IsCurrentMembershipLocked(sessionId, lobbyId, membershipGeneration);
    }

    private bool IsNotificationCurrent(NodeMatchNotification notification)
    {
        // A committed handoff may still be waiting in this queue when its
        // Worker publishes the terminal and the lobby advances to PostMatch.
        // Its immutable snapshot is the delivery boundary; only the current
        // recipient membership may invalidate it. The ordinary match map is
        // intentionally not required after terminal consumption.
        if (notification.Payload is NodeMatchHandoff handoff
            && notification.HandoffSnapshot is { } snapshot)
        {
            if (snapshot.CurrentMatchId != handoff.MatchId
                || EffectiveLifecycleEpoch(snapshot.LifecycleEpoch)
                    != EffectiveLifecycleEpoch(handoff.LifecycleEpoch)
                || handoff.MembershipGeneration.Value == 0
                || snapshot.SelfMembershipGeneration
                    != handoff.MembershipGeneration)
                return false;
            lock (_gate)
                return IsCurrentMembershipLocked(notification.SessionId,
                    snapshot.LobbyId, handoff.MembershipGeneration);
        }
        return IsPayloadCurrent(notification.SessionId, notification.Payload);
    }

    private static ulong EffectiveLifecycleEpoch(MatchLifecycleEpoch epoch)
        => epoch.Value == 0 ? MatchLifecycleEpoch.Initial.Value : epoch.Value;

    private static MembershipGeneration SelectionGeneration(
        LobbyMatchTransitionSelection selection, Guid sessionId)
        => selection.MembershipGenerations?.GetValueOrDefault(sessionId) ?? default;

    private bool IsPayloadCurrent(Guid sessionId, object payload)
    {
        lock (_gate)
            return IsPayloadCurrentLocked(sessionId, payload);
    }

    /// <summary>Evaluates a payload fence while the coordinator gate is held.
    /// The only subsequent lock is the LobbyManager gate, preserving the
    /// coordinator-to-lobby order used by storage and publication.</summary>
    private bool IsPayloadCurrentLocked(Guid sessionId, object payload)
    {
        Guid lobbyId = Guid.Empty;
        MembershipGeneration membershipGeneration = default;
        switch (payload)
        {
            case NodeMatchTransitionVoteSnapshot transition:
                lobbyId = transition.LobbyId;
                membershipGeneration = transition.MembershipGeneration;
                break;
            case NodeMatchTransitionStarted started:
                lobbyId = started.LobbyId;
                membershipGeneration = started.MembershipGeneration;
                break;
            case NodeMatchCompletion completion:
                lobbyId = completion.Summary.LobbyId.Value;
                membershipGeneration = completion.MembershipGeneration;
                break;
            case NodeMatchEnded ended:
                lobbyId = ended.LobbyId;
                membershipGeneration = ended.MembershipGeneration;
                break;
            case NodeMatchHandoff handoff:
                MatchLifecycle? match = _matches.GetValueOrDefault(new MatchId(handoff.MatchId));
                if (match == null) return false;
                lobbyId = match.Spec.LobbyId.Value;
                membershipGeneration = handoff.MembershipGeneration;
                break;
            default:
                return true;
        }
        return IsCurrentMembershipLocked(sessionId, lobbyId, membershipGeneration);
    }
    public void Dispose()
    {
        TaskCompletionSource<NodeMatchHandoff>[] inFlight;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            inFlight = _matches.Values.SelectMany(pending => pending.Handoffs.Values)
                .Select(state => state.InFlight).OfType<TaskCompletionSource<NodeMatchHandoff>>().Distinct().ToArray();
            _matches.Clear();
            _transitions.Clear();
            _continuationRecoveries.Clear();
            _latest.Clear(); _completions.Clear(); _notifications.Clear(); _expectedTransitions.Clear();
        }
        _scheduler.Completed -= Completed;
        _scheduler.Ended -= Ended;
        _scheduler.TransitionEnded -= TransitionEnded;
        _lifetime.Cancel();
        _signal.Writer.TryComplete();
        foreach (TaskCompletionSource<NodeMatchHandoff> completion in inFlight)
            completion.TrySetException(new ObjectDisposedException(nameof(NodeMatchCoordinator)));
        _lifetime.Dispose();
    }
}
