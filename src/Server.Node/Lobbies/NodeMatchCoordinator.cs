using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace FruityPrime.Server.Node.Lobbies;

public sealed record NodeMatchNotification(Guid SessionId, object Payload);
public sealed class NodeContentCatalog
{
    private readonly IReadOnlyDictionary<string, ContentIdentity> _maps;
    public NodeContentCatalog(IEnumerable<ContentIdentity> maps)
    {
        var entries = maps.ToArray();
        if (entries.Length > 256 || entries.Any(m => string.IsNullOrWhiteSpace(m.MapKey) || m.MapKey.Length > 128
            || string.IsNullOrWhiteSpace(m.ContentHash) || string.IsNullOrWhiteSpace(m.ContentVersion)
            || string.IsNullOrWhiteSpace(m.BuildVersion) || m.ProtocolVersion == 0)) throw new ArgumentException("Invalid Node map catalog.");
        _maps = entries.ToDictionary(m => m.MapKey, StringComparer.Ordinal);
    }
    public IReadOnlyCollection<string> Maps => _maps.Keys.ToArray();
    public ContentIdentity Get(string mapKey) => _maps.TryGetValue(mapKey, out var map) ? map : throw new LobbyCommandException("map_unavailable", "Map is not configured on this Node.");
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
    private readonly ConcurrentDictionary<Guid, object> _notifications = [];
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(1);
    public NodeMatchCoordinator(LobbyManager lobbies, WorkerScheduler scheduler, WorkerManager workers,
        WorkerAdmissionIssuer issuer, NodeContentCatalog content, ILogger<NodeMatchCoordinator>? logger = null)
    {
        _lobbies = lobbies; _scheduler = scheduler; _workers = workers; _issuer = issuer; _content = content;
        _logger = logger ?? NullLogger<NodeMatchCoordinator>.Instance;
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
    public void ReconcileMembership()
    {
        MatchId[] empty;
        lock (_gate) empty = _matches.Where(p => p.Value.Members.All(m => _lobbies.ForSession(m.SessionId)?.LobbyId != p.Value.Spec.LobbyId.Value)).Select(p => p.Key).ToArray();
        foreach (var id in empty) _scheduler.CancelMatch(id, "Lobby has no remaining sessions.");
    }
    public void ForgetSession(Guid sessionId)
    { lock (_gate) { _lastRejoin.Remove(sessionId); _latest.TryRemove(sessionId, out _); _notifications.TryRemove(sessionId, out _); } }
    public async IAsyncEnumerable<NodeMatchNotification> ReadNotifications([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var _ in _signal.Reader.ReadAllAsync(ct))
            foreach (var pair in _notifications.ToArray())
                if (_notifications.TryRemove(pair)) yield return new(pair.Key, pair.Value);
    }
    public async Task<object> ExecuteAsync(LobbyIdentity identity, NodeCommand command)
    {
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
                var member = pending.Members.SingleOrDefault(m => m.SessionId == identity.SessionId && m.PlayerId == identity.PlayerId)
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
        var content = _content.Get(before.MapKey);
        MatchSpec spec;
        lock (_gate)
        {
            spec = _lobbies.PrepareMatch(identity.SessionId, start.ExpectedRevision, content, _workers.NodeId, _workers.NodeIncarnation);
            _matches.Add(spec.MatchId, new(spec, before.Members.ToArray()));
        }
        try
        {
            // Placement lifetime belongs to Node, not a socket that can reconnect.
            MatchPlacement placement = await _scheduler.PlaceAsync(spec);
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
            return _lobbies.ForSession(identity.SessionId) ?? before;
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
        var seat = spec.Roster.Single(s => s.PlayerId?.Value == member.PlayerId);
        ulong nonce;
        do { nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); } while (nonce == 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string ticket = _issuer.Issue(new(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation,
            spec.LobbyId, spec.MatchId, placement.WireMatchId, member.SessionId, seat.PlayerId, null, seat.Role, seat.SeatId,
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
    { _latest[sessionId] = message; _notifications[sessionId] = message; _signal.Writer.TryWrite(true); }
    public void Dispose() => _scheduler.Ended -= Ended;
}
