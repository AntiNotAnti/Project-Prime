using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using MphRead.Mods.Accounts;

namespace MphRead.Mods.Network;

/// <summary>Persistent control transport. It owns no UDP gameplay state.</summary>
public sealed class NodeControlClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly TaskCompletionSource _greeting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _receiver;
    private Task? _heartbeat;
    private long _eventId;
    private Guid _nodeId;
    private Guid? _resumingSession;
    internal string? Endpoint { get; private set; }
    public NodeControlClient() { }
    internal NodeControlClient(ClientWebSocket socket) { _socket.Dispose(); _socket = socket; }
    internal NodeControlClient(Guid expectedNodeId) { _nodeId = expectedNodeId; }
    public sealed record ViewState(NodeSessionSnapshot? Session = null, LobbySnapshot? Lobby = null,
        LobbyListSnapshot? Lobbies = null, NodeMatchHandoff? Handoff = null, bool MatchEnded = false, string? Error = null, Guid? JoinedMatchId = null, Guid? LastEndedMatchId = null);
    private ViewState _state = new();
    public ViewState State => Volatile.Read(ref _state);
    private void Publish(Func<ViewState, ViewState> update)
    {
        ViewState before, after;
        do { before = State; after = update(before); } while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, after, before), before));
    }
    public void MarkGameplayJoined(Guid matchId) => Publish(state => state with { JoinedMatchId = matchId });
    public bool ShouldReturnFromGameplay { get { var state = State; return state.JoinedMatchId.HasValue && state.JoinedMatchId == state.LastEndedMatchId; } }
    public NodeSessionSnapshot? Session => State.Session;
    public LobbySnapshot? Lobby => State.Lobby;
    public LobbyListSnapshot? Lobbies => State.Lobbies;
    public NodeMatchHandoff? Handoff => State.Handoff;
    public bool MatchEnded => State.MatchEnded;
    public string? Error => State.Error;
    private int _pendingSends;
    private int _disposed;
    private readonly TaskCompletionSource _sendsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Connected => _socket.State == WebSocketState.Open && Session != null;
    public event Action? Changed;
    public event Action<NodeControlEvent>? EventReceived;

    public async Task ConnectAsync(NodeAdmissionTicket grant, CancellationToken cancel = default)
    {
        if (!AccountSession.ValidNodeUri(grant.PublicControlUri)) throw new ArgumentException("A secure Node endpoint is required.");
        await OpenAsync(grant.NodeId, grant.PublicControlUri, "Bearer " + grant.Ticket, cancel).ConfigureAwait(false);
    }
    internal Task ResumeAsync(NodeControlClient previous, CancellationToken cancel)
    {
        _resumingSession = previous.Session?.SessionId ?? throw new InvalidOperationException("No resumable Node session.");
        return OpenAsync(previous.Session.NodeId, previous.Endpoint!, "Resume " + previous.Session.ResumeToken, cancel);
    }
    private async Task OpenAsync(Guid nodeId, string endpoint, string authorization, CancellationToken cancel)
    {
        _nodeId = nodeId; Endpoint = endpoint;
        _socket.Options.SetRequestHeader("Authorization", authorization);
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel, _stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await _socket.ConnectAsync(new Uri(endpoint), deadline.Token).ConfigureAwait(false);
            _receiver = ReceiveLoop();
            await _greeting.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            _heartbeat = HeartbeatLoop();
        }
        catch { _stop.Cancel(); _socket.Abort(); throw; }
    }
    public async Task SendAsync(string type, NodeCommand command, CancellationToken cancel = default)
    {
        int pending = Interlocked.Increment(ref _pendingSends);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (pending > 32) throw new InvalidOperationException("Node command queue is full.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel, _stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var info = NodeJsonContext.Default.GetTypeInfo(command.GetType()) ?? throw new ArgumentException("Unknown Node command.");
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = NodeControlCodec.Version, requestId = Guid.NewGuid(), type,
                payload = JsonSerializer.SerializeToElement(command, info)
            });
            if (bytes.Length > NodeControlCodec.MaximumFrameBytes) throw new ArgumentException("Node command is too large.");
            await _send.WaitAsync(deadline.Token).ConfigureAwait(false);
            try { await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, deadline.Token).ConfigureAwait(false); }
            finally { _send.Release(); }
        }
        finally { if (Interlocked.Decrement(ref _pendingSends) == 0 && Volatile.Read(ref _disposed) != 0) _sendsDrained.TrySetResult(); }
    }

    private async Task HeartbeatLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                await SendAsync("node.ping", new NodePing(), _stop.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
    }
    private async Task ReceiveLoop()
    {
        try
        {
            byte[] bytes = new byte[NodeControlCodec.MaximumFrameBytes];
            while (!_stop.IsCancellationRequested)
            {
                int length = 0; ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(bytes.AsMemory(length), _stop.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Node connection closed.");
                    if (result.MessageType != WebSocketMessageType.Text || result.Count == 0 && !result.EndOfMessage)
                        throw new JsonException("Invalid Node control frame.");
                    length += result.Count;
                    if (length == bytes.Length && !result.EndOfMessage) throw new JsonException("Node frame exceeds limit.");
                } while (!result.EndOfMessage);
                ApplyEvent(bytes.AsMemory(0, length));
            }
        }
        catch (Exception e)
        {
            Publish(state => state with { Error = e is OperationCanceledException ? "Node disconnected." : "Node connection failed." });
            _greeting.TrySetException(new InvalidOperationException(Error));
            _stop.Cancel(); _socket.Abort(); NotifyChanged();
        }
    }
    internal void ApplyEvent(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > NodeControlCodec.MaximumFrameBytes) throw new JsonException("Invalid Node event size.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        NodeControlCodec.RejectDuplicates(doc.RootElement);
        var value = doc.RootElement.Deserialize(NodeJsonContext.Default.NodeControlEvent) ?? throw new JsonException("Missing Node event.");
        if (value.Version != NodeControlCodec.Version || value.EventId < 1 || (Session != null && value.EventId != _eventId + 1)) throw new JsonException("Invalid Node event sequence.");
        if (Session == null && value.Type != "node.session") throw new JsonException("Node greeting is required first.");
        _eventId = value.EventId;
        switch (value.Type)
        {
            case "node.session":
                var session = value.Payload.Deserialize(NodeJsonContext.Default.NodeSessionSnapshot)!;
                if (session.NodeId != _nodeId || session.SessionId == Guid.Empty || session.PlayerId == Guid.Empty
                    || session.DisplayName is not { Length: >= 1 and <= 16 } || session.DisplayName.Any(c => c is < ' ' or > '~')
                    || session.ResumeToken is not { Length: 43 } || !session.ResumeToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                    || Session != null || (_resumingSession.HasValue && session.SessionId != _resumingSession.Value)) throw new JsonException("Unexpected Node identity.");
                Publish(state => state with { Session = session }); _greeting.TrySetResult(); break;
            case "lobby.snapshot":
                var lobby = value.Payload.Deserialize(NodeJsonContext.Default.LobbySnapshot) ?? throw new JsonException("Missing lobby.");
                if (lobby.LobbyId == Guid.Empty || lobby.Revision < 1 || lobby.Name is not { Length: >= 1 and <= 64 }
                    || !Enum.IsDefined(lobby.Phase) || !Enum.IsDefined(lobby.Visibility) || lobby.PlayerLimit is < 1 or > 8
                    || lobby.ObserverLimit is < 0 or > 128 || lobby.Members.Length > lobby.PlayerLimit + lobby.ObserverLimit
                    || lobby.Chat.Length > 128 || lobby.Members.Any(m => m.SessionId == Guid.Empty || m.PlayerId == Guid.Empty
                        || m.DisplayName is not { Length: >= 1 and <= 16 } || m.DisplayName.Any(c => c is < ' ' or > '~')
                        || !Enum.IsDefined(m.Hunter) || m.Team > 7) || !lobby.Members.Any(x => x.SessionId == Session!.SessionId)
                    || lobby.Members.Select(x => x.SessionId).Distinct().Count() != lobby.Members.Length)
                    throw new JsonException("Invalid lobby snapshot.");
                if (Lobby?.LobbyId == lobby.LobbyId && lobby.Revision <= Lobby.Revision) break;
                Publish(state => state with { Lobby = lobby }); break;
            case "lobby.list":
                var list = value.Payload.Deserialize(NodeJsonContext.Default.LobbyListSnapshot) ?? throw new JsonException("Missing lobby list.");
                if (list.Lobbies.Length > 64 || list.Lobbies.Any(x => x.LobbyId == Guid.Empty || x.Revision < 1)) throw new JsonException("Invalid lobby list.");
                Publish(state => state with { Lobbies = list }); break;
            case "lobby.left":
                var left = value.Payload.Deserialize(NodeJsonContext.Default.LobbyLeft);
                if (left?.LobbyId == Lobby?.LobbyId) Publish(state => state with { Lobby = null });
                break;
            case "match.handoff":
                var handoff = value.Payload.Deserialize(NodeJsonContext.Default.NodeMatchHandoff) ?? throw new JsonException("Missing match handoff.");
                if (handoff.MatchId == Guid.Empty || handoff.WireMatchId == 0 || handoff.Nonce == 0
                    || handoff.Ticket is not { Length: > 0 and <= JoinPacket.MaxRoutedTicketBytes }) throw new JsonException("Invalid match handoff.");
                Publish(state => state with { Handoff = handoff, MatchEnded = false }); break;
            case "match.ended":
                var ended = value.Payload.Deserialize(NodeJsonContext.Default.NodeMatchEnded);
                if (ended != null && Handoff != null && ended.MatchId == Handoff.MatchId) Publish(state => state with { MatchEnded = true, LastEndedMatchId = ended.MatchId });
                break;
            case "error": Publish(state => state with { Error = value.Payload.Deserialize(NodeJsonContext.Default.NodeControlError)?.Message is { Length: <= 512 } error ? error : "Node rejected the command." }); break;
        }
        if (EventReceived != null) foreach (Action<NodeControlEvent> handler in EventReceived.GetInvocationList())
            try { handler(value); } catch { Publish(state => state with { Error = "A Node event listener failed." }); }
        NotifyChanged();
    }
    private void NotifyChanged()
    {
        if (Changed != null) foreach (Action handler in Changed.GetInvocationList())
            try { handler(); } catch { Publish(state => state with { Error = "A Node view listener failed." }); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { await _disposeDone.Task.ConfigureAwait(false); return; }
        try
        {
            _stop.Cancel(); _socket.Abort();
            if (_receiver != null) await _receiver.ConfigureAwait(false);
            if (_heartbeat != null) await _heartbeat.ConfigureAwait(false);
            if (Volatile.Read(ref _pendingSends) != 0) await _sendsDrained.Task.ConfigureAwait(false);
            _socket.Dispose(); _stop.Dispose(); _send.Dispose();
        }
        finally { _disposeDone.TrySetResult(); }
    }

}

public static class NodeSessions
{
    private static NodeControlClient? _current;
    public static NodeControlClient? Current { get => Volatile.Read(ref _current); private set => Volatile.Write(ref _current, value); }
    public static async Task<NodeControlClient> ConnectAsync(AccountSession account, Guid nodeId, CancellationToken cancel = default)
    {
        if (Current != null) await Current.DisposeAsync();
        Current = null;
        var next = new NodeControlClient();
        try { await next.ConnectAsync(await account.GetNodeTicketAsync(nodeId, cancel), cancel); Current = next; return next; }
        catch { await next.DisposeAsync(); throw; }
    }
    public static async Task<NodeControlClient> ResumeAsync(CancellationToken cancel = default)
    {
        var previous = Current ?? throw new InvalidOperationException("No Node session to resume.");
        var next = new NodeControlClient();
        try { await next.ResumeAsync(previous, cancel); Current = next; await previous.DisposeAsync(); return next; }
        catch { await next.DisposeAsync(); throw; }
    }
    public static async Task DisconnectAsync()
    {
        var current = Current; Current = null;
        if (current != null) await current.DisposeAsync();
    }
}
