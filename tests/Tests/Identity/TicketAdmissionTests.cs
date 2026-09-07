using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests;
public sealed class TicketAdmissionTests
{
    private sealed class KeysHandler : HttpMessageHandler
    {
        private readonly string _keys;
        public KeysHandler(string keys) => _keys = keys;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.True(request.Headers.Contains("X-Server-Id"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(request.Method == HttpMethod.Put ? "{}" : _keys) });
        }
    }
    [Fact]
    public void SignedAdmissionReconnectsSameAccountAcrossEndpointWithoutPublicIdentityTrust()
    {
        using var issuer = new GameTicketTests.Issuer();
        using var http = new HttpClient(new KeysHandler(issuer.Keys));
        using var authority = new ServerTicketAuthority(new(new Uri(GameTicketTests.Issuer.Origin + "/"), GameTicketTests.Issuer.Origin, issuer.Server, "operator-key")
            { SessionId = issuer.Session }, http);
        using var serverSocket = new NetTransport(0);
        var server = new ServerNetwork(serverSocket, "MP1 SANCTORUS", GameMode.Battle) { TicketAuthority = authority };
        var endpoint = new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort);
        using var socket = new NetTransport(0);
        string originalTicket = issuer.Ticket();
        using var first = new NetClient(socket, endpoint, "TICKET", Hunter.Samus, 123, originalTicket);
        uint tick = 0;
        void Pump(NetClient? client, Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done() && timer.ElapsedMilliseconds < 4000)
            { server.Poll(++tick); client?.Poll(); Thread.Sleep(1); }
            Assert.True(done(), client?.Failure ?? "Ticket admission timed out.");
        }
        Pump(first, () => first.HasRoster);
        int slot = first.Accepted.Slot; ServerPeer old = server.Peers[slot]!;
        Assert.Equal(new PlayerId(issuer.Player), old.PlayerId);
        old.HasParticipated = true; old.Connection.StartPlaying(); server.Phase = MatchPhase.Playing;
        server.Remove(slot);
        Assert.True(server.HasReconnectReservation(slot));
        // Even the original endpoint cannot reclaim a disconnected account
        // with the spent admission ticket; a fresh credential is required.
        var spent = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "TICKET", old.Connection.Id, originalTicket);
        byte[] replay = new byte[NetHeader.Size + spent.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(replay);
        spent.Write(replay.AsSpan(NetHeader.Size)); socket.SendDatagram(endpoint, replay);
        for (int i = 0; i < 30; i++) { server.Poll(++tick); Thread.Sleep(1); }
        Assert.Equal(0, server.Count);
        using var attacker = new NetTransport(0);
        void Send(JoinPacket join)
        {
            byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
            join.Write(bytes.AsSpan(NetHeader.Size)); attacker.SendDatagram(endpoint, bytes);
        }
        Send(new(NetHeader.Version, 333, Hunter.Samus, "TICKET", old.Connection.Id));
        for (int i = 0; i < 30; i++) { server.Poll(++tick); Thread.Sleep(1); }
        Assert.Equal(0, server.Count); Assert.True(server.HasReconnectReservation(slot));
        // A different signed account cannot present the public old connection ID either.
        Send(new(NetHeader.Version, 444, Hunter.Samus, "TICKET", old.Connection.Id, issuer.Ticket(444, player: Guid.NewGuid())));
        for (int i = 0; i < 60; i++) { server.Poll(++tick); Thread.Sleep(1); }
        Assert.Equal(0, server.Count);
        using var nextSocket = new NetTransport(0);
        using var next = new NetClient(nextSocket, endpoint, "TICKET", Hunter.Samus, 456, issuer.Ticket(456));
        Pump(next, () => next.HasRoster);
        ServerPeer resumed = server.Peers[next.Accepted.Slot]!;
        Assert.Equal(slot, next.Accepted.Slot); Assert.Equal(old.Connection.Id, resumed.ReturningFromConnectionId);
        Assert.True(resumed.ReturningParticipant); Assert.Equal(old.PlayerId, resumed.PlayerId);
        Assert.NotEqual(old.Connection.Id, resumed.Connection.Id);
        Assert.Throws<ArgumentException>(() => next.Reconnect());
    }
    private sealed class BlockedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }
    }
    [Fact]
    public void PendingAdmissionIsBoundedWhileBackendIsUnavailableAndRetriesAreIdempotent()
    {
        using var http = new HttpClient(new BlockedHandler());
        using var authority = new ServerTicketAuthority(new(new Uri("https://tickets.example.test/"), "https://tickets.example.test", Guid.NewGuid(), "key"), http);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 1234);
        for (ulong i = 1; i <= 64; i++) Assert.True(authority.Submit(endpoint, new(NetHeader.Version, i, Hunter.Samus, "TICKET", Ticket: "A.B.C")));
        Assert.True(authority.Submit(endpoint, new(NetHeader.Version, 1, Hunter.Samus, "TICKET", Ticket: "A.B.C")));
        Assert.False(authority.Submit(endpoint, new(NetHeader.Version, 1, Hunter.Samus, "TICKET", Ticket: "D.E.F")));
        Assert.False(authority.Submit(endpoint, new(NetHeader.Version, 65, Hunter.Samus, "TICKET", Ticket: "A.B.C")));
    }
    [Fact]
    public void DiscoveryIdentityIsOptionalAndV1RulesTailStillDecodes()
    {
        var status = new ServerStatusPacket { HasRules = true, ServerId = Guid.NewGuid(), RequiresTicket = true,
            Protocol = NetHeader.Version, MaxPlayers = 8, ServerName = "Test", Match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "MP1 SANCTORUS" } };
        byte[] bytes = new byte[ServerStatusPacket.ExtendedSize]; status.Write(bytes);
        Assert.True(ServerStatusPacket.TryRead(bytes, out var parsed)); Assert.Equal(status.ServerId, parsed.ServerId); Assert.True(parsed.RequiresTicket);
        byte[] old = bytes.AsSpan(0, ServerStatusPacket.RulesV1Size).ToArray(); old[ServerStatusPacket.Size] = 1; old[ServerStatusPacket.Size + 5] = 0;
        Assert.True(ServerStatusPacket.TryRead(old, out parsed)); Assert.Equal(Guid.Empty, parsed.ServerId); Assert.False(parsed.RequiresTicket);
        old[ServerStatusPacket.Size] = 2; Assert.False(ServerStatusPacket.TryRead(old, out _));
    }
    [Theory]
    [InlineData("https://user:secret@tickets.example/", "https://issuer.example/")]
    [InlineData("https://tickets.example/?q=1", "https://issuer.example/")]
    [InlineData("https://tickets.example/#part", "https://issuer.example/")]
    [InlineData("https://tickets.example/", "ftp://localhost/")]
    public void UnsafeConfigurationFailsBeforeStartingWorker(string backend, string issuer)
    {
        Assert.Throws<ArgumentException>(() => new ServerTicketOptions(new Uri(backend), issuer, Guid.NewGuid(), "key").Validate());
    }
    private sealed class EndlessStream : System.IO.Stream
    {
        internal int BytesRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { Array.Fill(buffer, (byte)'A', offset, count); BytesRead += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { buffer.Span.Fill((byte)'A'); BytesRead += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() { }
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class OversizedKeysHandler : HttpMessageHandler
    {
        internal readonly EndlessStream Stream = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.Method == HttpMethod.Put
                ? new StringContent("{}") : new StreamContent(Stream) });
    }
    [Fact]
    public void StreamingJwksStopsAtBoundWithoutBufferingUnboundedBody()
    {
        var handler = new OversizedKeysHandler(); using var http = new HttpClient(handler);
        using var authority = new ServerTicketAuthority(new(new Uri("https://tickets.example/"), "https://tickets.example/", Guid.NewGuid(), "key"), http);
        Assert.True(authority.Submit(new(IPAddress.Loopback, 1), new(NetHeader.Version, 1, Hunter.Samus, "TICKET", Ticket: "A.B.C")));
        var timer = Stopwatch.StartNew(); ValidatedTicketJoin result = default; bool completed = false;
        while (!completed && timer.ElapsedMilliseconds < 3000) { completed = authority.TryRead(out result); Thread.Sleep(1); }
        Assert.True(completed); Assert.Null(result.Identity); Assert.Equal(64 * 1024 + 1, handler.Stream.BytesRead);
    }
    [Fact]
    public void TicketRequiredServerRejectsGuestWhileBackendIsUnavailable()
    {
        using var http = new HttpClient(new BlockedHandler());
        using var authority = new ServerTicketAuthority(new(new Uri("https://tickets.example/"), "https://tickets.example/", Guid.NewGuid(), "key")
            { RequireTickets = true }, http);
        using var serverSocket = new NetTransport(0); using var clientSocket = new NetTransport(0);
        var server = new ServerNetwork(serverSocket, "MP1 SANCTORUS", GameMode.Battle) { TicketAuthority = authority };
        using var client = new NetClient(clientSocket, new(IPAddress.Loopback, serverSocket.LocalPort), "GUEST", Hunter.Samus);
        var timer = Stopwatch.StartNew(); uint tick = 0;
        while (client.Failure == null && timer.ElapsedMilliseconds < 3000)
        { server.Poll(++tick); client.Poll(); Thread.Sleep(1); }
        Assert.Contains("requires a game ticket", client.Failure); Assert.Equal(0, server.Count);
    }
}
