using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FruityPrime.Server.Node.Identity;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Shared;

namespace FruityPrime.Server.Node.Sessions;

public sealed class NodeSessionManager
{
    private sealed class Connection(WebSocket socket, CancellationToken aborted)
    {
        public WebSocket Socket = socket;
        public CancellationTokenSource Stop = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        public Channel<byte[]> Outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(32)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
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
    private readonly ConcurrentDictionary<Guid, Session> _sessions = [];
    private readonly Dictionary<Guid, Guid> _players = [];
    private readonly Dictionary<string, Guid> _resume = new(StringComparer.Ordinal);
    private readonly object _admission = new();
    private readonly LobbyManager _lobbies;
    private readonly Guid _nodeId;
    private readonly int _maximumSessions;
    private readonly TimeProvider _clock;
    private readonly NodeMatchCoordinator? _matches;
    public static TimeSpan DisconnectGrace => TimeSpan.FromSeconds(45);
    private long _protocolClosures;
    public long ProtocolClosures => Interlocked.Read(ref _protocolClosures);
    public int Count => _sessions.Values.Count(s => s.Connection != null);
    public NodeSessionManager(LobbyManager lobbies, Guid nodeId, int maximumSessions = 1024, TimeProvider? clock = null, NodeMatchCoordinator? matches = null)
    {
        if (maximumSessions is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumSessions));
        _lobbies = lobbies; _nodeId = nodeId; _maximumSessions = maximumSessions; _clock = clock ?? TimeProvider.System; _matches = matches;
    }
    public async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        await foreach (var snapshot in _lobbies.ReadNotifications(cancellationToken))
            foreach (var member in snapshot.Members)
                if (_sessions.TryGetValue(member.SessionId, out var session)) Send(session, "lobby.snapshot", null, snapshot);
    }
    public async Task BroadcastMatchesAsync(CancellationToken cancellationToken)
    {
        if (_matches == null) return;
        await foreach (var message in _matches.ReadNotifications(cancellationToken))
            if (_sessions.TryGetValue(message.SessionId, out var session)) SendMatch(session, message.Payload);
    }
    public bool CanResume(string token)
    {
        if (token.Length != 43) return false;
        lock (_admission) return _resume.TryGetValue(Hash(token), out var id) && _sessions.TryGetValue(id, out var session)
            && session.Connection == null && session.ResumeUntil > _clock.GetUtcNow();
    }
    public void PruneExpired()
    {
        lock (_admission)
        {
            foreach (var session in _sessions.Values.Where(s => s.Connection == null && s.ResumeUntil <= _clock.GetUtcNow()).ToArray())
            {
                _sessions.TryRemove(session.Id, out _); _players.Remove(session.Identity.PlayerId); _resume.Remove(session.ResumeHash);
                _lobbies.Disconnect(session.Id);
                _matches?.ForgetSession(session.Id);
            }
        }
        _matches?.ReconcileMembership();
    }
    public async Task RunAsync(WebSocket socket, NodeIdentity? identity, CancellationToken cancellationToken, string? resumeToken = null)
    {
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
                { connection.Stop.Dispose(); socket.Abort(); return; }
                _resume.Remove(session.ResumeHash);
            }
            else
            {
                if (identity == null || _sessions.Count >= _maximumSessions || _players.ContainsKey(identity.PlayerId))
                { connection.Stop.Dispose(); socket.Abort(); return; }
                session = new(identity); _players[identity.PlayerId] = session.Id; _sessions[session.Id] = session;
            }
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            session.ResumeHash = Hash(token); _resume[session.ResumeHash] = session.Id;
            lock (session) session.Connection = connection;
        }
        Task sender = SendLoop(connection);
        try
        {
            Send(session, "node.session", null, new NodeSessionSnapshot(session.Id, session.Identity.PlayerId, session.Identity.DisplayName, _nodeId, token));
            if (_lobbies.ForSession(session.Id) is { } restored) Send(session, "lobby.snapshot", null, restored);
            if (_matches?.ForSession(session.Id) is { } matchState) SendMatch(session, matchState);
            byte[] buffer = new byte[NodeControlCodec.MaximumFrameBytes];
            long window = _clock.GetTimestamp(); int requests = 0;
            while (!connection.Stop.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                int length = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text || result.Count == 0 && !result.EndOfMessage)
                        throw new JsonException("Text control frames required.");
                    length += result.Count;
                    if (length == buffer.Length && !result.EndOfMessage) throw new JsonException("Frame exceeds limit.");
                } while (!result.EndOfMessage);
                if (_clock.GetElapsedTime(window) >= TimeSpan.FromSeconds(1)) { window = _clock.GetTimestamp(); requests = 0; }
                if (++requests > 30) throw new JsonException("Control rate exceeded.");
                var request = NodeControlCodec.Read(buffer.AsMemory(0, length));
                if (!session.RecentRequests.Add(request.RequestId))
                { Send(session, "error", request.RequestId, new NodeControlError("duplicate_request", "Request was already processed.")); continue; }
                session.RequestOrder.Enqueue(request.RequestId);
                if (session.RequestOrder.Count > 256) session.RecentRequests.Remove(session.RequestOrder.Dequeue());
                if (request.Command is NodePing)
                { Send(session, "node.pong", request.RequestId, new NodePong(_clock.GetUtcNow().ToUnixTimeMilliseconds())); continue; }
                try
                {
                    var lobbyIdentity = new LobbyIdentity(session.Id, session.Identity.PlayerId, session.Identity.DisplayName);
                    object response = _matches != null ? await _matches.ExecuteAsync(lobbyIdentity, request.Command) : _lobbies.Execute(lobbyIdentity, request.Command);
                    switch (response)
                    {
                        case LobbySnapshot snapshot: Send(session, "lobby.snapshot", request.RequestId, snapshot); break;
                        case LobbyListSnapshot list: Send(session, "lobby.list", request.RequestId, list); break;
                        case LobbyLeft left: Send(session, "lobby.left", request.RequestId, left); break;
                        case NodeRoundSnapshot round: Send(session, "lobby.round", request.RequestId, round); break;
                        case NodeMatchHandoff handoff: Send(session, "match.handoff", request.RequestId, handoff); break;
                    }
                }
                catch (LobbyCommandException ex) { Send(session, "error", request.RequestId, new NodeControlError(ex.Code, ex.Message)); }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { Interlocked.Increment(ref _protocolClosures); }
        finally
        {
            connection.Stop.Cancel(); connection.Outbound.Writer.TryComplete();
            try { await sender; } catch (OperationCanceledException) { }
            lock (_admission)
            {
                lock (session) session.Connection = null;
                session.ResumeUntil = _clock.GetUtcNow() + DisconnectGrace;
            }
            socket.Abort(); connection.Stop.Dispose();
        }
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    private static void SendMatch(Session session, object message)
    {
        switch (message)
        {
            case NodeMatchHandoff handoff: Send(session, "match.handoff", null, handoff); break;
            case NodeMatchEnded ended: Send(session, "match.ended", null, ended); break;
        }
    }
    private static void Send<T>(Session session, string type, Guid? requestId, T payload)
    {
        lock (session)
        {
            var connection = session.Connection;
            if (connection == null) return;
            byte[] bytes = NodeControlCodec.Write(type, ++session.EventId, requestId, payload);
            if (!connection.Outbound.Writer.TryWrite(bytes)) connection.Stop.Cancel();
        }
    }
    private static async Task SendLoop(Connection connection)
    {
        try
        {
            await foreach (var message in connection.Outbound.Reader.ReadAllAsync(connection.Stop.Token))
                await connection.Socket.SendAsync(message, WebSocketMessageType.Text, true, connection.Stop.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { connection.Stop.Cancel(); }
    }
}

public sealed class NodeSessionReaper(NodeSessionManager sessions) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Reap(stoppingToken), sessions.BroadcastAsync(stoppingToken), sessions.BroadcastMatchesAsync(stoppingToken));
    private async Task Reap(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) sessions.PruneExpired();
    }
}
