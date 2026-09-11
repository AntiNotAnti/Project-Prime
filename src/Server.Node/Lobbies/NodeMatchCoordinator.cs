using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead;

namespace ProjectPrime.Server.Node.Lobbies;

public sealed record NodeMatchNotification(Guid SessionId, object Payload);
public sealed record NodeMatchDeliveryOverflow;

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
    }
    public IReadOnlyCollection<string> Maps => _order.OrderBy(key => key, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<ContentIdentity> Entries => _order.Select(key => _maps[key].Identity).ToArray();
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
}

/// <summary>Connects frozen lobby admission to Worker placement and immutable terminal events.</summary>
public sealed class NodeMatchCoordinator : IDisposable
{
    private sealed class Pending(MatchSpec spec, LobbyMember[] members)
    {
        public MatchSpec Spec { get; } = spec;
        public LobbyMember[] Members { get; } = members;
        public MatchPlacement? Placement { get; set; }
        public long Generation { get; set; }
        public Dictionary<Guid, HandoffState> Handoffs { get; } = [];
    }
    private sealed class HandoffState
    {
        public NodeMatchHandoff? Value;
        public long ExpiresAt;
        public TaskCompletionSource<NodeMatchHandoff>? InFlight;
    }
    private readonly object _gate = new();
    private readonly LobbyManager _lobbies;
    private readonly WorkerScheduler _scheduler;
    private readonly WorkerManager _workers;
    private readonly WorkerAdmissionIssuer _issuer;
    private readonly NodeContentCatalog _content;
    private readonly ILogger<NodeMatchCoordinator> _logger;
    private readonly Dictionary<MatchId, Pending> _matches = [];
    private readonly Dictionary<Guid, long> _lastRejoin = [];
    private readonly ConcurrentDictionary<Guid, object> _latest = [];
    private readonly ConcurrentDictionary<Guid, NodeMatchCompletion> _completions = [];
    private readonly Dictionary<Guid, Queue<object>> _notifications = [];
    private readonly HashSet<Guid> _forgottenSessions = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private bool _disposed;
    private const int MaximumPendingEventsPerSession = 32;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(1);
    public NodeMatchCoordinator(LobbyManager lobbies, WorkerScheduler scheduler, WorkerManager workers,
        WorkerAdmissionIssuer issuer, NodeContentCatalog content, ILogger<NodeMatchCoordinator>? logger = null)
    {
        _lobbies = lobbies; _scheduler = scheduler; _workers = workers; _issuer = issuer; _content = content;
        _logger = logger ?? NullLogger<NodeMatchCoordinator>.Instance;
        _lifetimeToken = _lifetime.Token;
        _lobbies.ContentCatalog = content;
        _scheduler.Completed += Completed;
        _scheduler.Ended += Ended;
    }
    public object? ForSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (!_latest.TryGetValue(sessionId, out var value)) return null;
            if (value is NodeMatchHandoff && !IsLiveHandoffLocked(sessionId))
            {
                _latest.TryRemove(sessionId, out _);
                return null;
            }
            return value;
        }
    }

    public IReadOnlyList<object> ForSessionEvents(Guid sessionId)
    {
        lock (_gate)
        {
            var result = new List<object>(2);
            if (_completions.TryGetValue(sessionId, out var completion)) result.Add(completion);
            if (_latest.TryGetValue(sessionId, out var latest)
                && (latest is not NodeMatchHandoff || IsLiveHandoffLocked(sessionId))
                && (result.Count == 0 || !ReferenceEquals(result[0], latest))) result.Add(latest);
            return result;
        }
    }

    private bool IsLiveHandoffLocked(Guid sessionId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return _matches.Values.Any(pending => pending.Handoffs.TryGetValue(sessionId, out HandoffState? state)
            && state.Value != null && state.ExpiresAt > now + 1);
    }

    /// <summary>
    /// Reconnect path: cache reads remain synchronous, while a replacement
    /// handoff is issued and confirmed asynchronously before it is returned.
    /// </summary>
    public async Task<IReadOnlyList<object>> ForSessionEventsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource? linkedCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken) : null;
        CancellationToken effectiveCancellation = linkedCancellation?.Token ?? _lifetimeToken;
        Pending? pending;
        MatchPlacement? placement;
        LobbyMember? member;
        lock (_gate)
        {
            pending = _matches.Values.SingleOrDefault(value => value.Members.Any(candidate => candidate.SessionId == sessionId));
            placement = pending?.Placement;
            member = pending?.Members.SingleOrDefault(candidate => candidate.SessionId == sessionId);
        }
        if (pending == null || placement == null || member == null) return ForSessionEvents(sessionId);
        NodeMatchHandoff handoff = await EnsureHandoffAsync(pending, placement, member, forceFresh: true, effectiveCancellation);
        lock (_gate)
        {
            var result = new List<object>(2);
            if (_completions.TryGetValue(sessionId, out var completion)) result.Add(completion);
            if (result.Count == 0 || !ReferenceEquals(result[0], handoff)) result.Add(handoff);
            return result;
        }
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
        Pending? pending;
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
        Pending[] pending;
        lock (_gate) pending = _matches.Values.ToArray();
        empty = pending.Where(value => value.Members.All(member => _lobbies.ForSession(member.SessionId)?.LobbyId != value.Spec.LobbyId.Value))
            .Select(value => value.Spec.MatchId).ToArray();
        foreach (var id in empty) _scheduler.CancelMatch(id, "Lobby has no remaining sessions.");
    }
    public void ForgetSession(Guid sessionId)
    {
        lock (_gate)
        {
            _forgottenSessions.Add(sessionId);
            _lastRejoin.Remove(sessionId); _latest.TryRemove(sessionId, out _); _completions.TryRemove(sessionId, out _); _notifications.Remove(sessionId);
        }
    }
    public async IAsyncEnumerable<NodeMatchNotification> ReadNotifications([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var _ in _signal.Reader.ReadAllAsync(ct))
        {
            NodeMatchNotification[] batch;
            lock (_gate)
            {
                batch = _notifications.SelectMany(pair => pair.Value.Select(payload => new NodeMatchNotification(pair.Key, payload))).ToArray();
                _notifications.Clear();
            }
            foreach (var notification in batch) yield return notification;
        }
    }

    public async Task<object> ExecuteAsync(LobbyIdentity identity, NodeCommand command)
    {
        identity.Validate();
        if (command is LobbyConfigure configure)
            _content.Validate(configure.MapKey, configure.Mode);
        if (command is NodeMatchRejoin rejoin)
        {
            Pending pending;
            MatchPlacement placement;
            LobbyMember member;
            LobbySnapshot? lobby = _lobbies.ForSession(identity.SessionId);
            lock (_gate)
            {
                if (lobby?.Phase != LobbyPhase.InMatch || lobby.CurrentMatchId != rejoin.MatchId
                    || !_matches.TryGetValue(new(rejoin.MatchId), out pending!) || pending.Placement == null)
                    throw new LobbyCommandException("phase", "This session has no active match reservation.");
                long now = Environment.TickCount64;
                if (_lastRejoin.TryGetValue(identity.SessionId, out long prior) && now - prior < 5000)
                    throw new LobbyCommandException("rate_limit", "Wait five seconds before retrying admission.");
                member = pending.Members.SingleOrDefault(m => m.SessionId == identity.SessionId
                    && m.IdentityKey == identity.IdentityKey)
                    ?? throw new LobbyCommandException("identity", "This session does not own the frozen reservation.");
                _lastRejoin[identity.SessionId] = now;
                placement = pending.Placement;
            }
            return await EnsureHandoffAsync(pending, placement, member, forceFresh: true, cancellationToken: _lifetimeToken);
        }
        if (command is LobbyReturn returning) return _lobbies.ReturnToLobby(identity.SessionId, returning.ExpectedRevision);
        // Rematch preserves the lobby and settings, then requires fresh readiness
        // before the next explicit start; it never reuses a completed MatchId.
        if (command is LobbyRematch rematch) return _lobbies.ReturnToLobby(identity.SessionId, rematch.ExpectedRevision);
        if (command is not LobbyStart start)
        {
            var response = _lobbies.Execute(identity, command);
            if (response is LobbyLeft) ForgetSession(identity.SessionId);
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
        lock (_gate) _matches.Add(spec.MatchId, new(spec, current.Members.ToArray()));
        await PlaceAsync(spec);
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
                List<MatchSpec> prepared = [];
                List<(Guid SessionId, NodeMatchEnded Message)> failuresToNotify = [];
                var continuations = _lobbies.PrepareContinuations(_workers.NodeId, _workers.NodeIncarnation, 64 - active.Count, out var failures);
                lock (_gate)
                {
                    if (_disposed) continue;
                    foreach (var failure in failures)
                        failuresToNotify.AddRange(failure.Members.Select(member => (member.SessionId, new NodeMatchEnded(failure.MatchId, true))));
                    foreach (var (spec, members) in continuations)
                    {
                        _matches.Add(spec.MatchId, new(spec, members));
                        prepared.Add(spec);
                    }
                }
                foreach (var failure in failuresToNotify) Notify(failure.SessionId, failure.Message);
                foreach (var spec in prepared) active.Add(ContinueAsync(spec, ct));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { await Task.WhenAll(active); }
    }
    private async Task ContinueAsync(MatchSpec spec, CancellationToken ct)
    {
        try { await PlaceAsync(spec, ct); }
        catch (LobbyCommandException) { /* PlaceAsync publishes recoverable interruption. */ }
    }
    private async Task PlaceAsync(MatchSpec spec, CancellationToken ct = default)
    {
        using CancellationTokenSource? linkedCancellation = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken) : null;
        CancellationToken effectiveCancellation = linkedCancellation?.Token ?? _lifetimeToken;
        try
        {
            // Placement lifetime belongs to Node, not a socket that can reconnect.
            MatchPlacement placement = await _scheduler.PlaceAsync(spec, effectiveCancellation);
            Pending pending;
            lock (_gate)
            {
                if (!_matches.TryGetValue(spec.MatchId, out pending!))
                    throw new LobbyCommandException("interrupted", "Match ended during placement.");
            }
            if (!_lobbies.MatchReady(placement))
                throw new LobbyCommandException("interrupted", "Match ended during placement.");
            lock (_gate)
            {
                if (!_matches.TryGetValue(spec.MatchId, out pending!) || pending.Placement != null)
                    throw new LobbyCommandException("interrupted", "Match ended during placement.");
                pending.Placement = placement;
                pending.Generation++;
            }
            LobbyMember[] members = pending.Members.Where(member => member.SessionId != Guid.Empty).ToArray();
            NodeMatchHandoff[] handoffs = await Task.WhenAll(members.Select(member =>
                EnsureHandoffAsync(pending, placement, member, forceFresh: false, effectiveCancellation)));
            foreach (var pair in members.Zip(handoffs))
                Notify(pair.First.SessionId, pair.Second);
            return;
        }
        catch (Exception ex) when (ex is WorkerPlacementException or TimeoutException or OperationCanceledException
            or LobbyCommandException or ArgumentException or ObjectDisposedException)
        {
            _logger.LogWarning("Match {MatchId} placement failed: {FailureType}: {Reason}", spec.MatchId.Value, ex.GetType().Name, ex.Message);
            Ended(spec.MatchId, true);
            _scheduler.CancelMatch(spec.MatchId, "Lobby placement failed.");
            throw new LobbyCommandException("placement_failed", "Match could not be placed; the lobby remains available.");
        }
    }
    private async Task<NodeMatchHandoff> EnsureHandoffAsync(Pending pending, MatchPlacement placement,
        LobbyMember member, bool forceFresh, CancellationToken cancellationToken)
    {
        TaskCompletionSource<NodeMatchHandoff> completion;
        bool producer = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_matches.TryGetValue(pending.Spec.MatchId, out var current) || !ReferenceEquals(current, pending)
                || current.Placement != placement || !current.Members.Any(candidate => candidate.SessionId == member.SessionId
                    && candidate.IdentityKey == member.IdentityKey))
                throw new LobbyCommandException("interrupted", "Match handoff is no longer current.");
            if (!pending.Handoffs.TryGetValue(member.SessionId, out HandoffState? state))
                pending.Handoffs.Add(member.SessionId, state = new());
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!forceFresh && state.Value is { } cached && state.ExpiresAt > now + 1)
                return cached;
            if (forceFresh)
            {
                state.Value = null; state.ExpiresAt = 0;
                _latest.TryRemove(member.SessionId, out _);
            }
            if (state.InFlight is { } existing)
                completion = existing;
            else
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                state.InFlight = completion;
                producer = true;
            }
        }
        if (producer)
            _ = ProduceHandoffAsync(pending, placement, member, completion);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Owns one admission-key installation independently from any socket
    /// waiter. A disconnect cancels that waiter's observation only; the
    /// Node-owned install continues until the match lifetime ends and every
    /// exit settles the shared completion source.
    /// </summary>
    private async Task ProduceHandoffAsync(Pending pending, MatchPlacement placement,
        LobbyMember member, TaskCompletionSource<NodeMatchHandoff> completion)
    {
        try
        {
            var issued = CreateHandoff(pending, placement, member);
            if (issued.Install is { } install)
                await _scheduler.InstallAdmissionKeyAsync(install, _lifetimeToken).ConfigureAwait(false);
            // Lobby membership is read outside the coordinator gate. A
            // departed session must never receive a newly installed key.
            if (_lifetimeToken.IsCancellationRequested
                || _lobbies.ForSession(member.SessionId)?.LobbyId != pending.Spec.LobbyId.Value)
                throw new LobbyCommandException("interrupted", "Session membership changed before handoff publication.");
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_matches.TryGetValue(pending.Spec.MatchId, out var current) || !ReferenceEquals(current, pending)
                    || current.Generation != issued.Generation || current.Placement != placement
                    || !current.Members.Any(candidate => candidate.SessionId == member.SessionId
                        && candidate.IdentityKey == member.IdentityKey))
                    throw new LobbyCommandException("interrupted", "Match handoff became stale before publication.");
                HandoffState state = pending.Handoffs[member.SessionId];
                state.Value = issued.Handoff; state.ExpiresAt = issued.ExpiresAt; state.InFlight = null;
                _latest[member.SessionId] = issued.Handoff;
                completion.TrySetResult(issued.Handoff);
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (pending.Handoffs.TryGetValue(member.SessionId, out HandoffState? state)
                    && ReferenceEquals(state.InFlight, completion)) state.InFlight = null;
            }
            completion.TrySetException(error);
        }
    }

    private (NodeMatchHandoff Handoff, InstallAdmissionKey? Install, long ExpiresAt, long Generation) CreateHandoff(
        Pending pending, MatchPlacement placement, LobbyMember member)
    {
        var spec = pending.Spec;
        HumanIdentityKey identity = member.IdentityKey;
        RosterSeat seat = spec.Roster.Single(s => s.Role != SeatRole.Bot
            && (identity.Kind == HumanIdentityKind.Registered ? s.PlayerId?.Value == identity.Value
                : s.GuestSessionId == identity.Value));
        ulong nonce;
        do { nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); } while (nonce == 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expires = now + 120;
        Guid ticketId = Guid.NewGuid();
        Guid admissionId = Guid.NewGuid();
        string ticket = _issuer.Issue(new(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation,
            spec.LobbyId, spec.MatchId, placement.WireMatchId, member.SessionId, seat.PlayerId, seat.GuestSessionId, seat.Role, seat.SeatId,
            seat.DisplayName, nonce, now, expires, ticketId));
        string admissionKey = "";
        InstallAdmissionKey? install = null;
        if (placement.UdpAuthenticationEnabled)
        {
            byte[] rawKey = RandomNumberGenerator.GetBytes(AdmissionKeyRules.ByteLength);
            try { admissionKey = Convert.ToBase64String(rawKey); }
            finally { CryptographicOperations.ZeroMemory(rawKey); }
            install = new InstallAdmissionKey(admissionId, ticketId, member.SessionId,
                spec.NodeId, spec.NodeIncarnation, spec.MatchId, placement.WireMatchId,
                placement.WorkerId, placement.WorkerIncarnation, seat.SeatId, nonce, expires, admissionKey);
        }
        var handoff = new NodeMatchHandoff(spec.MatchId.Value, placement.WireMatchId.Value, placement.Host, placement.Port,
            ticket, nonce, member.Observer, member.Hunter,
            placement.UdpAuthenticationEnabled ? admissionId : Guid.Empty, admissionKey,
            placement.UdpAuthenticationEnabled);
        handoff.Validate();
        return (handoff, install, expires, pending.Generation);
    }
    private void Ended(MatchId matchId, bool interrupted)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_matches.Remove(matchId, out pending)) return;
        }
        _lobbies.MatchEnded(matchId, interrupted);
        foreach (var member in pending.Members) Notify(member.SessionId, new NodeMatchEnded(matchId.Value, interrupted));
    }

    private void Completed(MatchCompletionSummary summary)
    {
        summary.Validate();
        Pending? pending;
        NodeMatchCompletion completion;
        lock (_gate)
        {
            if (!_matches.TryGetValue(summary.MatchId, out pending)
                || pending.Spec.LobbyId != summary.LobbyId) return;
            completion = new NodeMatchCompletion(summary);
            foreach (var member in pending.Members) _completions[member.SessionId] = completion;
        }
        foreach (var member in pending.Members) Notify(member.SessionId, completion);
    }
    private void Notify(Guid sessionId, object message)
    {
        lock (_gate)
        {
            if (_disposed || _forgottenSessions.Contains(sessionId)) return;
            _latest[sessionId] = message;
            if (!_notifications.TryGetValue(sessionId, out var queue)) _notifications.Add(sessionId, queue = new());
            if (queue.TryPeek(out var first) && first is NodeMatchDeliveryOverflow) return;
            // Never silently replace an ended event with the following handoff.
            // An exhausted consumer explicitly loses its connection and must resume.
            if (queue.Count == MaximumPendingEventsPerSession)
            { queue.Clear(); queue.Enqueue(new NodeMatchDeliveryOverflow()); }
            else queue.Enqueue(message);
            _signal.Writer.TryWrite(true);
        }
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
            _latest.Clear(); _completions.Clear(); _notifications.Clear();
        }
        _scheduler.Completed -= Completed;
        _scheduler.Ended -= Ended;
        _lifetime.Cancel();
        _signal.Writer.TryComplete();
        foreach (TaskCompletionSource<NodeMatchHandoff> completion in inFlight)
            completion.TrySetException(new ObjectDisposedException(nameof(NodeMatchCoordinator)));
        _lifetime.Dispose();
    }
}
