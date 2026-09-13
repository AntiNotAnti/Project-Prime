using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectPrime.Server.Node.Identity;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Sessions;

/// <summary>
/// Immutable, presentation-only projection consumed by the optional Node
/// presence reporter. TotalSessions is intentionally separate from Players
/// because private sessions still count online.
/// </summary>
public sealed record NodePresenceSnapshot(long Revision, int TotalSessions,
    ImmutableArray<NodePresenceEntry> Players);

public sealed class NodeSessionManager
{
    private sealed record PendingOutbound(string Type, Guid? RequestId,
        Func<long, byte[]> Encode);
    private sealed class Connection(WebSocket socket, CancellationToken aborted)
    {
        public WebSocket Socket = socket;
        public CancellationTokenSource Stop = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        public Channel<PendingOutbound> Ordered = Channel.CreateBounded<PendingOutbound>(new BoundedChannelOptions(MultiplayerLimits.MaxLifecycleQueue)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        public Dictionary<string, PendingOutbound> State = new(StringComparer.Ordinal);
        public Channel<bool> Wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
        public bool Overflow;
        public Exception? ReceiveFailure;
    }
    private sealed class Session(NodeIdentity identity)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public NodeIdentity Identity = identity;
        public string ResumeHash = "";
        public DateTimeOffset ResumeUntil;
        public Connection? Connection;
        public long EventId;
        public readonly HashSet<Guid> RecentRequests = [];
        public readonly Queue<Guid> RequestOrder = [];
    }
    private sealed record PresenceIdentity(Guid SessionId, string DisplayName,
        bool PublicPresence, PlayerPresenceActivity Activity);
    private readonly ConcurrentDictionary<Guid, Session> _sessions = [];
    private readonly Dictionary<HumanIdentityKey, Guid> _identities = [];
    private readonly Dictionary<string, Guid> _resume = new(StringComparer.Ordinal);
    private readonly object _admission = new();
    private readonly LobbyManager _lobbies;
    private readonly Guid _nodeId;
    private readonly int _maximumSessions;
    private readonly TimeProvider _clock;
    private readonly NodeMatchCoordinator? _matches;
    private readonly NodeContentCatalog? _catalog;
    private readonly ILogger _logger;
    // Snapshot assembly is serialized separately from the short projection
    // state lock. This prevents two callers that captured different moments
    // from moving the internal revision/projection backwards.
    private readonly object _presenceBuildGate = new();
    private readonly object _presenceGate = new();
    private ImmutableArray<PresenceIdentity> _presenceProjection = [];
    private long _presenceRevision;
    // The empty projection is the initial authoritative state. Starting from
    // that baseline means the first connected session is a real connect
    // transition even when no reporter happened to poll before admission.
    private bool _presenceInitialized = true;
    public static TimeSpan DisconnectGrace => ReconnectPolicy.SessionGrace;
    private long _protocolClosures;
    public long ProtocolClosures => Interlocked.Read(ref _protocolClosures);
    public int Count => _sessions.Values.Count(s => s.Connection != null);
    public NodeSessionManager(LobbyManager lobbies, Guid nodeId, int maximumSessions = 1024,
        TimeProvider? clock = null, NodeMatchCoordinator? matches = null, NodeContentCatalog? catalog = null,
        ILogger<NodeSessionManager>? logger = null)
    {
        if (maximumSessions is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumSessions));
        _lobbies = lobbies; _nodeId = nodeId; _maximumSessions = maximumSessions; _clock = clock ?? TimeProvider.System;
        _matches = matches; _catalog = catalog;
        _logger = logger ?? NullLogger<NodeSessionManager>.Instance;
        NodeMetrics.RegisterOutboundDepth(OutboundDepth);
    }

    private long OutboundDepth()
    {
        long depth = 0;
        lock (_admission)
            foreach (Session session in _sessions.Values)
                lock (session)
                    if (session.Connection is { } connection)
                        depth += connection.Ordered.Reader.Count
                            + connection.State.Count;
        return depth;
    }

    /// <summary>
    /// Builds one immutable view from the session and lobby owners. Session
    /// identity is captured before the lobby read and neither lock is held
    /// while the other owner is consulted, avoiding a lock-order inversion.
    /// </summary>
    public NodePresenceSnapshot CreatePresenceSnapshot()
    {
        lock (_presenceBuildGate)
        {
            List<(Guid SessionId, NodeIdentity Identity)> connected = [];
            lock (_admission)
            {
                foreach (Session session in _sessions.Values)
                {
                    lock (session)
                    {
                        if (session.Connection != null)
                            connected.Add((session.Id, session.Identity));
                    }
                }
            }

            ImmutableDictionary<Guid, PlayerPresenceActivity> activities =
                _lobbies.SnapshotPresenceActivities();
            PresenceIdentity[] current = connected
                .Select(value => new PresenceIdentity(value.SessionId, value.Identity.DisplayName,
                    value.Identity.PublicPresence,
                    activities.TryGetValue(value.SessionId, out PlayerPresenceActivity activity)
                        ? activity : PlayerPresenceActivity.Online))
                .OrderBy(value => value.SessionId)
                .ToArray();

            lock (_presenceGate)
            {
                if (!_presenceInitialized || !_presenceProjection.SequenceEqual(current))
                {
                    if (_presenceInitialized && _presenceRevision < long.MaxValue)
                        _presenceRevision++;
                    _presenceProjection = current.ToImmutableArray();
                    _presenceInitialized = true;
                }

                ImmutableArray<NodePresenceEntry> players = current
                    .Where(value => value.PublicPresence)
                    .Select(value => new NodePresenceEntry(value.DisplayName, value.Activity))
                    .ToImmutableArray();
                return new NodePresenceSnapshot(_presenceRevision, connected.Count, players);
            }
        }
    }

    /// <summary>Updates only the addressed connected session. The caller can
    /// acknowledge a no-op without causing a new presence revision.</summary>
    public bool TrySetPresenceVisibility(Guid sessionId, bool visible,
        out long revision)
    {
        if (sessionId == Guid.Empty)
        {
            revision = CurrentPresenceRevision;
            return false;
        }
        bool unavailable = false;
        lock (_admission)
        {
            if (!_sessions.TryGetValue(sessionId, out Session? session))
            {
                unavailable = true;
            }
            else lock (session)
            {
                if (session.Connection == null)
                {
                    unavailable = true;
                }
                else if (session.Identity.PublicPresence != visible)
                    session.Identity = session.Identity with { PublicPresence = visible };
            }
        }
        if (unavailable)
        {
            revision = CurrentPresenceRevision;
            return false;
        }
        revision = CreatePresenceSnapshot().Revision;
        return true;
    }

    public long PresenceRevision => CreatePresenceSnapshot().Revision;

    private long CurrentPresenceRevision
    {
        get { lock (_presenceGate) return _presenceRevision; }
    }

    public async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        await foreach (var snapshot in _lobbies.ReadNotifications(cancellationToken))
            foreach (Guid sessionId in _lobbies.Recipients(snapshot.LobbyId))
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    Send(session, "lobby.snapshot", null, _lobbies.ForSession(sessionId) ?? snapshot);
                    if (_lobbies.RoundForSession(sessionId) is { } round) Send(session, "lobby.round", null, round);
                    if (_lobbies.MatchTransitionForSession(sessionId) is { } transition)
                        Send(session, "match.transition.state", null, transition);
                }
    }
    public async Task BroadcastMatchesAsync(CancellationToken cancellationToken)
    {
        if (_matches == null) return;
        await foreach (var message in _matches.ReadNotifications(cancellationToken))
            if (_sessions.TryGetValue(message.SessionId, out var session)) SendMatch(session, message.Payload);
    }
    public Task ContinueRoundsAsync(CancellationToken ct) => _matches?.RunContinuationsAsync(ct) ?? Task.CompletedTask;
    public bool CanResume(string token)
    {
        if (token.Length != 43) return false;
        lock (_admission) return _resume.TryGetValue(Hash(token), out var id) && _sessions.TryGetValue(id, out var session)
            && session.Connection == null && session.ResumeUntil > _clock.GetUtcNow();
    }
    public void PruneExpired()
    {
        int expiredCount = 0;
        lock (_admission)
        {
            foreach (var session in _sessions.Values.Where(s => s.Connection == null && s.ResumeUntil <= _clock.GetUtcNow()).ToArray())
            {
                _sessions.TryRemove(session.Id, out _); _identities.Remove(session.Identity.IdentityKey); _resume.Remove(session.ResumeHash);
                _lobbies.Disconnect(session.Id);
                _matches?.ForgetSession(session.Id);
                expiredCount++;
            }
        }
        if (expiredCount != 0) NodeDiagnostics.Session(_logger, "expiry", "pruned");
        _matches?.ReconcileMembership();
    }
    public void PruneWaitlists() => _lobbies.PruneWaitlists();
    public async Task RunAsync(WebSocket socket, NodeIdentity? identity, CancellationToken cancellationToken, string? resumeToken = null)
    {
        using var activity = NodeMetrics.StartActivity(resumeToken == null
            ? "node.session.connect" : "node.session.resume");
        PruneExpired();
        Session session;
        var connection = new Connection(socket, cancellationToken);
        string token;
        lock (_admission)
        {
            if (resumeToken != null)
            {
                if (resumeToken.Length != 43 || !_resume.TryGetValue(Hash(resumeToken), out var id)
                    || !_sessions.TryGetValue(id, out session!) || session.Connection != null || session.ResumeUntil <= _clock.GetUtcNow())
                {
                    NodeDiagnostics.Session(_logger, "resume", "rejected");
                    connection.Stop.Dispose(); socket.Abort(); return;
                }
                _resume.Remove(session.ResumeHash);
            }
            else
            {
                if (identity == null)
                {
                    NodeDiagnostics.Session(_logger, "connect", "missing_identity");
                    connection.Stop.Dispose(); socket.Abort(); return;
                }
                try { identity.Validate(); }
                catch (ArgumentException)
                {
                    NodeDiagnostics.Session(_logger, "connect", "invalid_identity");
                    connection.Stop.Dispose(); socket.Abort(); return;
                }
                if (_sessions.Count >= _maximumSessions || _identities.ContainsKey(identity.IdentityKey))
                {
                    NodeDiagnostics.Session(_logger, "connect", "capacity_or_duplicate");
                    connection.Stop.Dispose(); socket.Abort(); return;
                }
                session = new(identity); _identities[identity.IdentityKey] = session.Id; _sessions[session.Id] = session;
            }
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            session.ResumeHash = Hash(token); _resume[session.ResumeHash] = session.Id;
            lock (session) session.Connection = connection;
            _lobbies.SetSessionResumeDeadline(session.Id, null);
        }
        NodeDiagnostics.Session(_logger, resumeToken == null ? "connect" : "resume", "success");
        NodeMetrics.ControlConnected(resumeToken != null);
        activity?.SetTag("session.id", session.Id);
        activity?.SetTag("node.id", _nodeId);
        string disconnectReason = "transport";
        Task sender = SendLoop(session, connection);
        Task receiver = Task.CompletedTask;
        try
        {
            Send(session, "node.session", null, new NodeSessionSnapshot(session.Id, session.Identity.PlayerId, session.Identity.DisplayName,
                _nodeId, token, session.Identity.GuestSessionId, session.Identity.PublicPresence));
            var inbound = Channel.CreateBounded<NodeControlRequest>(new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            // Start transport consumption before reconstructing state. Commands
            // are buffered by one bounded reader and executed only after the
            // ordered restoration events below have been enqueued.
            receiver = ReceiveLoopAsync(socket, session, connection, inbound.Writer);
            if (_lobbies.ForSession(session.Id) is { } restored) Send(session, "lobby.snapshot", null, restored);
            if (_lobbies.RoundForSession(session.Id) is { } restoredRound) Send(session, "lobby.round", null, restoredRound);
            if (_lobbies.MatchTransitionForSession(session.Id) is { } restoredTransition)
                Send(session, "match.transition.state", null, restoredTransition);
            if (_matches != null)
                // Resume is reconstructive only. Credential-bearing handoffs
                // are minted by an explicit match.rejoin request after the
                // client has restored its stable match/lifecycle state.
                foreach (object matchState in _matches.ForSessionEvents(session.Id)) SendMatch(session, matchState);
            await foreach (NodeControlRequest request in inbound.Reader.ReadAllAsync())
            {
                if (connection.Stop.IsCancellationRequested) break;
                if (request.Command is NodePing)
                { Send(session, "node.pong", request.RequestId, new NodePong(_clock.GetUtcNow().ToUnixTimeMilliseconds())); continue; }
                if (request.Command is NodeSetPresenceVisibility visibility)
                {
                    if (!TrySetPresenceVisibility(session.Id, visibility.Visible, out long revision))
                    {
                        Send(session, "error", request.RequestId,
                            new NodeControlError("session_unavailable", "The Node session is no longer active."));
                    }
                    else
                    {
                        Send(session, "node.presence.visibility", request.RequestId,
                            new NodePresenceVisibilityChanged(visibility.Visible, revision));
                    }
                    continue;
                }
                if (request.Command is NodeCatalogRequest catalogRequest)
                {
                    SendCatalog(session, request.RequestId, catalogRequest);
                    continue;
                }
                try
                {
                    var lobbyIdentity = new LobbyIdentity(session.Id, session.Identity.PlayerId, session.Identity.DisplayName,
                        session.Identity.GuestSessionId);
                    object response = _matches != null
                        ? await _matches.ExecuteAsync(lobbyIdentity, request.Command, connection.Stop.Token)
                        : _lobbies.Execute(lobbyIdentity, request.Command);
                    switch (response)
                    {
                        case LobbySnapshot snapshot: Send(session, "lobby.snapshot", request.RequestId, _lobbies.ForSession(session.Id) ?? snapshot); break;
                        case LobbyListSnapshot list: Send(session, "lobby.list", request.RequestId, list); break;
                        case LobbyLeft left: Send(session, "lobby.left", request.RequestId, left); break;
                        case NodeRoundSnapshot round: Send(session, "lobby.round", request.RequestId, round); break;
                        case NodeMatchHandoff handoff: Send(session, "match.handoff", request.RequestId, handoff); break;
                        case NodeMatchTransitionVoteSnapshot transition:
                            Send(session, "match.transition.state", request.RequestId, transition); break;
                    }
                }
                catch (LobbyCommandException ex) { Send(session, "error", request.RequestId, new NodeControlError(ex.Code, ex.Message)); }
                catch (MatchControlException ex)
                {
                    NodeDiagnostics.Worker(_logger, "request", ex.Code);
                    Send(session, "error", request.RequestId,
                        new NodeControlError(ex.Code, "The match operation could not be completed."));
                }
                catch (TimeoutException)
                {
                    Send(session, "error", request.RequestId,
                        new NodeControlError(MatchControlFailure.Timeout.Code(), "The match operation timed out."));
                }
                catch (OperationCanceledException) when (!connection.Stop.IsCancellationRequested)
                {
                    Send(session, "error", request.RequestId,
                        new NodeControlError(MatchControlFailure.Timeout.Code(), "The match operation timed out."));
                }
            }
            await receiver.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connection.ReceiveFailure is
            JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentException)
        {
            disconnectReason = "protocol";
            Interlocked.Increment(ref _protocolClosures);
            NodeDiagnostics.Session(_logger, "protocol", "rejected");
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        { NodeDiagnostics.Session(_logger, "disconnect", "transport"); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            disconnectReason = "protocol";
            Interlocked.Increment(ref _protocolClosures);
            NodeDiagnostics.Session(_logger, "protocol", "rejected");
        }
        finally
        {
            connection.Stop.Cancel(); connection.Ordered.Writer.TryComplete();
            connection.Wake.Writer.TryComplete();
            try { await receiver.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException
                or JsonException or InvalidOperationException or KeyNotFoundException
                or FormatException or ArgumentException) { }
            try { await sender; } catch (OperationCanceledException) { }
            lock (_admission)
            {
                lock (session) session.Connection = null;
                session.ResumeUntil = _clock.GetUtcNow() + DisconnectGrace;
                _lobbies.SetSessionResumeDeadline(session.Id, session.ResumeUntil);
            }
            NodeDiagnostics.Session(_logger, "disconnect", "resume_grace");
            NodeMetrics.ControlDisconnected(disconnectReason);
            socket.Abort(); connection.Stop.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, Session session,
        Connection connection, ChannelWriter<NodeControlRequest> writer)
    {
        Exception? failure = null;
        try
        {
            byte[] buffer = new byte[NodeControlCodec.MaximumFrameBytes];
            long window = _clock.GetTimestamp();
            int requests = 0;
            while (!connection.Stop.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    connection.Stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                int length = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer,
                        length, buffer.Length - length), timeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text
                        || result.Count == 0 && !result.EndOfMessage)
                        throw new JsonException("Text control frames required.");
                    length += result.Count;
                    if (length == buffer.Length && !result.EndOfMessage)
                        throw new JsonException("Frame exceeds limit.");
                }
                while (!result.EndOfMessage);

                if (_clock.GetElapsedTime(window) >= TimeSpan.FromSeconds(1))
                {
                    window = _clock.GetTimestamp();
                    requests = 0;
                }
                if (++requests > 30)
                    throw new JsonException("Control rate exceeded.");
                NodeControlRequest request = NodeControlCodec.Read(
                    buffer.AsMemory(0, length));
                if (!session.RecentRequests.Add(request.RequestId))
                {
                    Send(session, "error", request.RequestId,
                        new NodeControlError("duplicate_request",
                            "Request was already processed."));
                    continue;
                }
                session.RequestOrder.Enqueue(request.RequestId);
                if (session.RequestOrder.Count > 256)
                    session.RecentRequests.Remove(session.RequestOrder.Dequeue());
                await writer.WriteAsync(request, connection.Stop.Token);
            }
        }
        catch (Exception error)
        {
            failure = error;
            Volatile.Write(ref connection.ReceiveFailure, error);
            throw;
        }
        finally
        {
            writer.TryComplete(failure);
            connection.Stop.Cancel();
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private void SendCatalog(Session session, Guid requestId, NodeCatalogRequest request)
    {
        if (_catalog == null)
        {
            Send(session, "error", requestId,
                new NodeControlError("catalog_unavailable", "The Node catalog is unavailable."));
            return;
        }
        if (request.Revision != _catalog.MapCatalogRevision)
        {
            Send(session, "error", requestId,
                new NodeControlError("catalog_revision_changed", "The Node catalog revision is no longer current."));
            return;
        }

        // Eight entries keeps the worst-case required-map metadata comfortably
        // below the shared 32 KiB control frame bound. The client may request a
        // smaller page, but never a larger one.
        int pageSize = Math.Min(request.PageSize, 8);
        int pageCount = Math.Max(1, (_catalog.MapCount + pageSize - 1) / pageSize);
        if (request.Page >= pageCount)
        {
            Send(session, "error", requestId,
                new NodeControlError("catalog_page_not_found", "The requested Node catalog page does not exist."));
            return;
        }
        ImmutableArray<ContentIdentity> entries = _catalog.CatalogEntries
            .Skip(request.Page * pageSize).Take(pageSize).ToImmutableArray();
        Send(session, "node.catalog.page", requestId, new NodeCatalogPage(
            _catalog.MapCatalogRevision, request.Page, pageCount, _catalog.MapCount,
            _catalog.MapCatalogHash, entries));
    }
    private static void SendMatch(Session session, object message)
    {
        switch (message)
        {
            case NodeMatchDeliveryOverflow:
                MarkOverflow(session);
                break;
            case NodeMatchHandoff handoff: Send(session, "match.handoff", null, handoff); break;
            case NodeMatchCompletion completion: Send(session, "match.completion", null, completion); break;
            case NodeMatchEnded ended: Send(session, "match.ended", null, ended); break;
            case NodeMatchTransitionVoteSnapshot transition: Send(session, "match.transition.state", null, transition); break;
            case NodeMatchTransitionStarted started: Send(session, "match.transition.started", null, started); break;
        }
    }
    private static void Send<T>(Session session, string type, Guid? requestId, T payload)
    {
        var pending = new PendingOutbound(type, requestId,
            eventId => NodeControlCodec.Write(type, eventId, requestId, payload));
        lock (session)
        {
            var connection = session.Connection;
            if (connection == null) return;
            if (connection.Overflow) return;
            if (requestId == null && IsCoalescible(type))
                connection.State[type] = pending;
            else if (!connection.Ordered.Writer.TryWrite(pending))
            {
                connection.Overflow = true;
                NodeMetrics.DeliveryOverflow();
            }
            connection.Wake.Writer.TryWrite(true);
        }
    }

    private static bool IsCoalescible(string type)
        => type is "lobby.snapshot" or "lobby.round" or "match.transition.state"
            or "presence.state";

    private static void MarkOverflow(Session session)
    {
        lock (session)
        {
            if (session.Connection is not { } connection || connection.Overflow) return;
            connection.Overflow = true;
            NodeMetrics.DeliveryOverflow();
            connection.Wake.Writer.TryWrite(true);
        }
    }

    private static async Task SendLoop(Session session, Connection connection)
    {
        try
        {
            while (!connection.Stop.IsCancellationRequested)
            {
                PendingOutbound? pending = null;
                bool overflow;
                lock (session)
                {
                    if (!connection.Ordered.Reader.TryRead(out pending))
                    {
                        overflow = connection.Overflow;
                        if (!overflow && connection.State.Count != 0)
                        {
                            KeyValuePair<string, PendingOutbound> state = connection.State.First();
                            connection.State.Remove(state.Key);
                            pending = state.Value;
                        }
                    }
                    else overflow = false;
                }

                if (pending != null)
                {
                    byte[] message;
                    lock (session) message = pending.Encode(++session.EventId);
                    await connection.Socket.SendAsync(message, WebSocketMessageType.Text,
                        true, connection.Stop.Token);
                    continue;
                }
                if (overflow)
                {
                    byte[] message;
                    lock (session)
                        message = NodeControlCodec.Write("control.delivery_overflow",
                            ++session.EventId, null, new NodeControlDeliveryOverflow());
                    await connection.Socket.SendAsync(message, WebSocketMessageType.Text,
                        true, connection.Stop.Token);
                    connection.Stop.Cancel();
                    return;
                }
                await connection.Wake.Reader.ReadAsync(connection.Stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException
            or InvalidOperationException or ArgumentException)
        { connection.Stop.Cancel(); }
    }
}

public sealed class NodeSessionReaper(NodeSessionManager sessions) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Reap(stoppingToken), sessions.BroadcastAsync(stoppingToken), sessions.BroadcastMatchesAsync(stoppingToken), sessions.ContinueRoundsAsync(stoppingToken));
    private async Task Reap(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            sessions.PruneExpired();
            sessions.PruneWaitlists();
        }
    }
}
