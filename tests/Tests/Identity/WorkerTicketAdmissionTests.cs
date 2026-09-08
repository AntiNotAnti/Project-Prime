using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using FruityPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using SharedBotFillPolicy = FruityPrime.Server.Shared.BotFillPolicy;
using Xunit;
using BotFillPolicy = FruityPrime.Server.Shared.BotFillPolicy;

namespace MphRead.Tests;

public sealed class WorkerTicketAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerAdmissionOwnsExactSeatTeamAndRetryEndpoint(bool guest)
    {
        var rules = MatchRules.CreateDefault(MatchMode.TeamBattle, "MP1 SANCTORUS");
        PlayerId? player = guest ? null : new PlayerId(Guid.NewGuid()); Guid? guestId = guest ? Guid.NewGuid() : null;
        var spec = new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(), rules,
            new(rules.RoomKey, "hash", "AMHE1", "test", NetHeader.Version), MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(3, player, guestId, "SEAT", Hunter.Samus, 1, SeatRole.Player, false)),
            SharedBotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var placement = new MatchPlacement(spec.MatchId, new(55), new(Guid.NewGuid()), Guid.NewGuid(), "127.0.0.1", 50001);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new WorkerAdmissionClaims(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation,
            spec.LobbyId, spec.MatchId, placement.WireMatchId, Guid.NewGuid(), player, guestId, SeatRole.Player, 3, "SEAT", 123,
            now, now + 60, Guid.NewGuid());
        using var issuer = new WorkerAdmissionIssuer("node");
        using var authority = new WorkerTicketAuthority(spec, placement, issuer.KeyId, issuer.ExportPublicKey());
        using var transport = new MemoryTransport();
        var network = new ServerNetwork(transport, rules, 55) { TicketAuthority = authority, RequireRoutedJoins = true };
        var endpoint = new IPEndPoint(IPAddress.Loopback, 50002);
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "SEAT", Ticket: issuer.Issue(claims), WireMatchId: 55);
        void Send(JoinPacket value, IPEndPoint target)
        {
            byte[] bytes = new byte[NetHeader.Size + value.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
            value.Write(bytes.AsSpan(NetHeader.Size));
            transport.Incoming.Enqueue(new(target, bytes, bytes.Length));
        }
        void Pump(Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done() && timer.ElapsedMilliseconds < 3000) { network.Poll(0); Thread.Sleep(1); }
            Assert.True(done());
        }
        Send(join with { WireMatchId = 0 }, endpoint); network.Poll(0); Assert.Equal(0, network.Count);
        Send(join with { WireMatchId = 56 }, endpoint); network.Poll(0); Assert.Equal(0, network.Count);
        Send(join, endpoint); Pump(() => network.Count == 1);
        ServerPeer peer = network.Peers[3]!;
        Assert.NotNull(peer); Assert.Equal((byte)3, peer.Slot); Assert.Equal((byte)1, peer.TeamIndex);
        Assert.Equal(player, peer.PlayerId); Assert.Equal(guestId, peer.GuestSessionId);
        Assert.Null(network.Peers[0]);
        Assert.True(authority.Submit(endpoint, join));
        ValidatedTicketJoin retry = default;
        var timer = Stopwatch.StartNew();
        while (!authority.TryRead(out retry) && timer.ElapsedMilliseconds < 3000) Thread.Sleep(1);
        Assert.NotNull(retry.Identity);
        Assert.True(authority.Submit(new(IPAddress.Loopback, 50003), join));
        timer.Restart();
        while (!authority.TryRead(out retry) && timer.ElapsedMilliseconds < 3000) Thread.Sleep(1);
        Assert.Null(retry.Identity); Assert.Equal(0, authority.ValidationFaults);
        Assert.Same(peer, network.Peers[3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerObserverReconnectRequiresFreshTicketAndPriorConnectionProof(bool guest)
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        PlayerId? player = guest ? null : new PlayerId(Guid.NewGuid());
        Guid? guestId = guest ? Guid.NewGuid() : null;
        var spec = new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(), rules,
            new(rules.RoomKey, "hash", "AMHE1", "test", NetHeader.Version), MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(8, player, guestId, "WATCH", Hunter.Samus, 0, SeatRole.Observer, false)),
            BotFillPolicy.Disabled, ObserverPolicy.Allowed, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var placement = new MatchPlacement(spec.MatchId, new(55), new(Guid.NewGuid()), Guid.NewGuid(), "127.0.0.1", 50001);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var issuer = new WorkerAdmissionIssuer("observer-reconnect");
        using var authority = new WorkerTicketAuthority(spec, placement, issuer.KeyId, issuer.ExportPublicKey());
        using var transport = new MemoryTransport();
        var network = new ServerNetwork(transport, rules, 55, new ObserverOptions(1))
        {
            TicketAuthority = authority,
            RequireRoutedJoins = true,
            AdmissionIdentityPolicy = static (join, identity) => join.Observer && identity.ReservedSeat == 8
        };
        network.CaptureObserverSnapshot(new byte[] { 1 });
        network.CaptureObserverWorld(new byte[] { 2 });
        network.Poll(1);
        network.CommitObserverTick(1);

        WorkerAdmissionClaims Claims(ulong nonce, Guid ticketId) => new(spec.NodeId, spec.NodeIncarnation,
            placement.WorkerId, placement.WorkerIncarnation, spec.LobbyId, spec.MatchId, placement.WireMatchId,
            Guid.NewGuid(), player, guestId, SeatRole.Observer, 8, "WATCH", nonce, now, now + 60, ticketId);
        JoinPacket Join(ulong nonce, Guid ticketId, ulong previous, string ticket) =>
            new(NetHeader.Version, nonce, Hunter.Samus, "WATCH", previous, ticket, Observer: true, WireMatchId: 55);
        void Send(JoinPacket join, IPEndPoint endpoint)
        {
            byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
            join.Write(bytes.AsSpan(NetHeader.Size));
            transport.Incoming.Enqueue(new(endpoint, bytes, bytes.Length));
        }
        uint tick = 2;
        void Pump(Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done() && timer.ElapsedMilliseconds < 3000)
            { network.Poll(tick++); Thread.Sleep(1); }
            Assert.True(done());
        }
        void PumpFor(int iterations = 120)
        {
            for (int i = 0; i < iterations; i++) { network.Poll(tick++); Thread.Sleep(1); }
        }

        IPEndPoint firstEndpoint = new(IPAddress.Loopback, 51001);
        Guid firstTicketId = Guid.NewGuid();
        JoinPacket first = Join(1001, firstTicketId, 0, issuer.Issue(Claims(1001, firstTicketId)));
        Send(first, firstEndpoint);
        Pump(() => network.ObserverCount == 1);
        ServerPeer original = network.ObserverPeers[0]!;

        // The original nonce/ticket remains an idempotent retry on its endpoint.
        Send(first, firstEndpoint);
        PumpFor();
        Assert.Same(original, network.ObserverPeers[0]);
        Assert.Equal(1, network.ObserverCount);

        IPEndPoint reconnectEndpoint = new(IPAddress.Loopback, 51002);
        Guid reconnectTicketId = Guid.NewGuid();
        JoinPacket reconnect = Join(1002, reconnectTicketId, original.Connection.Id,
            issuer.Issue(Claims(1002, reconnectTicketId)));
        Send(reconnect, reconnectEndpoint);
        Pump(() => network.ObserverPeers[0]?.Connection.Id != original.Connection.Id);
        ServerPeer resumed = network.ObserverPeers[0]!;
        Assert.Equal(1, network.ObserverCount);
        Assert.Equal((byte)8, resumed.ReservedSeat);
        Assert.Equal(player, resumed.PlayerId);
        Assert.Equal(reconnectEndpoint, resumed.Connection.Endpoint);
        Assert.Equal(NetConnectionState.Disconnecting, original.Connection.State);

        // A reused ticket ID is a replay even when wrapped in a fresh join nonce.
        JoinPacket sameTicket = Join(1003, reconnectTicketId, resumed.Connection.Id,
            issuer.Issue(Claims(1003, reconnectTicketId)));
        Send(sameTicket, new(IPAddress.Loopback, 51003));
        PumpFor();
        Assert.Same(resumed, network.ObserverPeers[0]);

        // A fresh ticket cannot replace the current observer through the stale
        // connection ID from the already-replaced session.
        Guid staleProofTicketId = Guid.NewGuid();
        JoinPacket staleProof = Join(1004, staleProofTicketId, original.Connection.Id,
            issuer.Issue(Claims(1004, staleProofTicketId)));
        Send(staleProof, new(IPAddress.Loopback, 51004));
        PumpFor();
        Assert.Same(resumed, network.ObserverPeers[0]);
        Assert.Equal(1, network.ObserverCount);
    }

    [Fact]
    public void WorkerObserverReconnectRejectsCrossIdentityBeforeDetachingOldPeer()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        var networkPlayer = new PlayerId(Guid.NewGuid());
        var attackerPlayer = new PlayerId(Guid.NewGuid());
        using var transport = new MemoryTransport();
        using var authority = new StaticTicketAuthority(
            ("A.B.C", new TicketIdentity(networkPlayer, Guid.NewGuid(), DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60,
                ReservedSeat: 8, WorkerAdmission: true)),
            ("D.E.F", new TicketIdentity(attackerPlayer, Guid.NewGuid(), DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60,
                ReservedSeat: 8, WorkerAdmission: true)));
        var network = new ServerNetwork(transport, rules, 55, new ObserverOptions(1))
        {
            TicketAuthority = authority,
            RequireRoutedJoins = true,
            AdmissionIdentityPolicy = static (join, identity) => join.Observer && identity.ReservedSeat == 8
        };
        network.CaptureObserverSnapshot(new byte[] { 1 });
        network.CaptureObserverWorld(new byte[] { 2 });
        network.Poll(1);
        network.CommitObserverTick(1);

        void Send(JoinPacket join, IPEndPoint endpoint)
        {
            byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
            join.Write(bytes.AsSpan(NetHeader.Size));
            transport.Incoming.Enqueue(new(endpoint, bytes, bytes.Length));
        }
        uint tick = 2;
        void PumpFor(int iterations = 120)
        {
            for (int i = 0; i < iterations; i++) { network.Poll(tick++); Thread.Sleep(1); }
        }

        JoinPacket first = new(NetHeader.Version, 2001, Hunter.Samus, "WATCH", Ticket: "A.B.C", Observer: true, WireMatchId: 55);
        IPEndPoint endpoint = new(IPAddress.Loopback, 52001);
        Send(first, endpoint);
        PumpFor();
        Assert.Equal(1, network.ObserverCount);
        ServerPeer original = network.ObserverPeers[0]!;

        JoinPacket crossIdentity = new(NetHeader.Version, 2002, Hunter.Samus, "WATCH", original.Connection.Id,
            "D.E.F", Observer: true, WireMatchId: 55);
        Send(crossIdentity, new(IPAddress.Loopback, 52002));
        PumpFor();
        Assert.Same(original, network.ObserverPeers[0]);
        Assert.Equal(1, network.ObserverCount);
        Assert.Equal(NetConnectionState.Loading, original.Connection.State);
    }

    private sealed class StaticTicketAuthority : IServerTicketAuthority
    {
        private readonly Dictionary<string, TicketIdentity> _identities;
        private readonly Queue<ValidatedTicketJoin> _results = new();
        public Guid ServerId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool RequireTickets => true;
        public StaticTicketAuthority(params (string Ticket, TicketIdentity Identity)[] identities)
            => _identities = identities.ToDictionary(value => value.Ticket, value => value.Identity, StringComparer.Ordinal);
        public bool Submit(IPEndPoint endpoint, in JoinPacket join)
        {
            _results.Enqueue(new(endpoint, join, _identities.TryGetValue(join.Ticket, out TicketIdentity identity) ? identity : null));
            return true;
        }
        public bool TryRead(out ValidatedTicketJoin result)
        {
            if (_results.TryDequeue(out result)) return true;
            result = default;
            return false;
        }
        public void Dispose() { }
    }

    private sealed class MemoryTransport : INetTransport
    {
        public Queue<ReceivedPacket> Incoming = new();
        public int LocalPort => 50001; public long PacketsDropped => 0; public int QueuedPackets => Incoming.Count;
        public int HeldIncomingPackets => 0; public int HeldOutgoingPackets => 0; public NetTrafficMetrics Metrics { get; } = new();
        public IEnumerable<ReceivedPacket> Drain() { while (Incoming.TryDequeue(out var item)) yield return item; }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes, long extraHoldTicks) { }
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> bytes, long extraHoldTicks = 0) { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> bytes = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
