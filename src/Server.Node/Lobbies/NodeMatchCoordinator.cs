using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead;

namespace FruityPrime.Server.Node.Lobbies;

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

    public NodeMapConfiguration() { }

    public NodeMapConfiguration(string mapKey, string contentHash, string contentVersion,
        string buildVersion, byte protocolVersion, int[]? modes)
    {
        MapKey = mapKey; ContentHash = contentHash; ContentVersion = contentVersion;
        BuildVersion = buildVersion; ProtocolVersion = protocolVersion; Modes = modes;
    }

    public NodeMapConfiguration(ContentIdentity identity, int[]? modes = null)
        : this(identity.MapKey, identity.ContentHash, identity.ContentVersion,
            identity.BuildVersion, identity.ProtocolVersion, modes) { }

    public ContentIdentity Identity => new(MapKey, ContentHash, ContentVersion, BuildVersion, ProtocolVersion);
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
    public ContentIdentity Get(string mapKey) => _maps.TryGetValue(mapKey, out var map) ? map.Identity : throw new LobbyCommandException("map_unavailable", "Map is not configured on this Node.");

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
            new LobbyMapChoice(maps[(index + offset) % maps.Length], mode)).ToArray();
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
    private sealed record Pending(MatchSpec Spec, LobbyMember[] Members)
    { public MatchPlacement? Placement { get; set; } }
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
    private readonly Dictionary<Guid, Queue<object>> _notifications = [];
    private const int MaximumPendingEventsPerSession = 32;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(1);
    public NodeMatchCoordinator(LobbyManager lobbies, WorkerScheduler scheduler, WorkerManager workers,
        WorkerAdmissionIssuer issuer, NodeContentCatalog content, ILogger<NodeMatchCoordinator>? logger = null)
    {
        _lobbies = lobbies; _scheduler = scheduler; _workers = workers; _issuer = issuer; _content = content;
        _logger = logger ?? NullLogger<NodeMatchCoordinator>.Instance;
        _lobbies.ContentCatalog = content;
        _scheduler.Ended += Ended;
    }
    public object? ForSession(Guid sessionId)
    {
        lock (_gate)
        {
            var lobbyId = _lobbies.ForSession(sessionId)?.LobbyId;
            var pending = _matches.Values.SingleOrDefault(p => p.Spec.LobbyId.Value == lobbyId && p.Members.Any(m => m.SessionId == sessionId));
            if (pending?.Placement is { } placement)
                return Handoff(pending, placement, pending.Members.Single(m => m.SessionId == sessionId));
            return _latest.TryGetValue(sessionId, out var value) ? value : null;
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
        lock (_gate) empty = _matches.Where(p => p.Value.Members.All(m => _lobbies.ForSession(m.SessionId)?.LobbyId != p.Value.Spec.LobbyId.Value)).Select(p => p.Key).ToArray();
        foreach (var id in empty) _scheduler.CancelMatch(id, "Lobby has no remaining sessions.");
    }
    public void ForgetSession(Guid sessionId)
    { lock (_gate) { _lastRejoin.Remove(sessionId); _latest.TryRemove(sessionId, out _); _notifications.Remove(sessionId); } }
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
            lock (_gate)
            {
                var lobby = _lobbies.ForSession(identity.SessionId);
                if (lobby?.Phase != LobbyPhase.InMatch || lobby.CurrentMatchId != rejoin.MatchId
                    || !_matches.TryGetValue(new(rejoin.MatchId), out var pending) || pending.Placement == null)
                    throw new LobbyCommandException("phase", "This session has no active match reservation.");
                long now = Environment.TickCount64;
                if (_lastRejoin.TryGetValue(identity.SessionId, out long prior) && now - prior < 5000)
                    throw new LobbyCommandException("rate_limit", "Wait five seconds before retrying admission.");
                var member = pending.Members.SingleOrDefault(m => m.SessionId == identity.SessionId
                    && m.IdentityKey == identity.IdentityKey)
                    ?? throw new LobbyCommandException("identity", "This session does not own the frozen reservation.");
                _lastRejoin[identity.SessionId] = now;
                return Handoff(pending, pending.Placement, member);
            }
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
        MatchSpec spec;
        lock (_gate)
        {
            // Re-read and validate the live lobby immediately before the
            // state-freezing call. The expected revision still protects the
            // cross-lock gap if a concurrent configure arrives here.
            var current = _lobbies.ForSession(identity.SessionId)
                ?? throw new LobbyCommandException("not_joined", "Join a lobby first.");
            _content.Validate(current.MapKey, current.Mode);
            content = _content.Get(current.MapKey);
            spec = _lobbies.PrepareMatch(identity.SessionId, start.ExpectedRevision, content, _workers.NodeId, _workers.NodeIncarnation);
            _matches.Add(spec.MatchId, new(spec, current.Members.ToArray()));
        }
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
                foreach (var done in active.Where(task => task.IsCompleted).ToArray())
                { await done; active.Remove(done); }
                List<MatchSpec> prepared = [];
                lock (_gate)
                {
                    var continuations = _lobbies.PrepareContinuations(_workers.NodeId, _workers.NodeIncarnation, 64 - active.Count, out var failures);
                    foreach (var failure in failures)
                        foreach (var member in failure.Members) Notify(member.SessionId, new NodeMatchEnded(failure.MatchId, true));
                    foreach (var (spec, members) in continuations)
                    {
                        _matches.Add(spec.MatchId, new(spec, members));
                        prepared.Add(spec);
                    }
                }
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
        try
        {
            // Placement lifetime belongs to Node, not a socket that can reconnect.
            MatchPlacement placement = await _scheduler.PlaceAsync(spec, ct);
            lock (_gate)
            {
                if (!_matches.TryGetValue(spec.MatchId, out var pending) || !_lobbies.MatchReady(placement))
                    throw new LobbyCommandException("interrupted", "Match ended during placement.");
                pending.Placement = placement;
                foreach (var member in pending.Members)
                {
                    if (_lobbies.ForSession(member.SessionId)?.LobbyId == pending.Spec.LobbyId.Value)
                        Notify(member.SessionId, Handoff(pending, placement, member));
                }
            }
            return;
        }
        catch (Exception ex) when (ex is WorkerPlacementException or TimeoutException or OperationCanceledException or LobbyCommandException or ArgumentException)
        {
            _logger.LogWarning("Match {MatchId} placement failed: {FailureType}: {Reason}", spec.MatchId.Value, ex.GetType().Name, ex.Message);
            Ended(spec.MatchId, true);
            _scheduler.CancelMatch(spec.MatchId, "Lobby placement failed.");
            throw new LobbyCommandException("placement_failed", "Match could not be placed; the lobby remains available.");
        }
    }
    private NodeMatchHandoff Handoff(Pending pending, MatchPlacement placement, LobbyMember member)
    {
        var spec = pending.Spec;
        HumanIdentityKey identity = member.IdentityKey;
        var seat = spec.Roster.Single(s => s.Role != SeatRole.Bot
            && (identity.Kind == HumanIdentityKind.Registered ? s.PlayerId?.Value == identity.Value
                : s.GuestSessionId == identity.Value));
        ulong nonce;
        do { nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); } while (nonce == 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string ticket = _issuer.Issue(new(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation,
            spec.LobbyId, spec.MatchId, placement.WireMatchId, member.SessionId, seat.PlayerId, seat.GuestSessionId, seat.Role, seat.SeatId,
            seat.DisplayName, nonce, now, now + 120, Guid.NewGuid()));
        return new(spec.MatchId.Value, placement.WireMatchId.Value, placement.Host, placement.Port, ticket, nonce, member.Observer, member.Hunter);
    }
    private void Ended(MatchId matchId, bool interrupted)
    {
        lock (_gate)
        {
            if (!_matches.Remove(matchId, out var pending)) return;
            _lobbies.MatchEnded(matchId, interrupted);
            foreach (var member in pending.Members)
                if (_lobbies.ForSession(member.SessionId)?.LobbyId == pending.Spec.LobbyId.Value)
                    Notify(member.SessionId, new NodeMatchEnded(matchId.Value, interrupted));
        }
    }
    private void Notify(Guid sessionId, object message)
    {
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
    public void Dispose() => _scheduler.Ended -= Ended;
}
