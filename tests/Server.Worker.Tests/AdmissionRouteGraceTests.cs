using System;
using System.Collections.Generic;
using System.Net;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Worker.Tests;

public sealed class AdmissionRouteGraceTests
{
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 51000);

    [Fact]
    public void AcceptedAdmissionUsesGraceAndLeavesEstablishedRouteAlive()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        using var physical = new TestTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(),
            new RoutedMatchDatagramRouter(), matchLimit: 1, udpAuthenticationEnabled: true,
            admissionRetransmissionGrace: TimeSpan.FromSeconds(3), clock: clock);
        using MatchDatagramTransport match = hub.RegisterMatch(1);
        Guid admissionId = Guid.NewGuid();

        Assert.True(match.RegisterAdmissionId(admissionId,
            clock.GetUtcNow().ToUnixTimeSeconds() + 60, out bool created) && created);
        ulong connectionId = match.AllocateConnectionId();

        match.MarkAdmissionEstablished(admissionId);
        clock.Advance(TimeSpan.FromSeconds(2));
        // The acceptance callback is idempotent and cannot extend the grace.
        match.MarkAdmissionEstablished(admissionId);
        hub.PumpOnce();
        Assert.Equal(1, match.ActiveAdmissionRouteCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        hub.PumpOnce();
        Assert.Equal(0, match.ActiveAdmissionRouteCount);

        // Admission-route expiry must not remove the already-established
        // connection route.
        byte[] packet = new byte[NetHeader.Size];
        new NetHeader(NetMessageType.Ack, NetHeaderFlags.None, connectionId, 1, 0, 0).Write(packet);
        physical.Incoming.Enqueue(new(Endpoint, packet, packet.Length));
        hub.PumpOnce();
        Assert.Single(match.Drain());
    }

    [Fact]
    public void GraceIsBoundedAndSupersededAdmissionRetiresImmediately()
    {
        using var physical = new TestTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(),
            new RoutedMatchDatagramRouter(), matchLimit: 1, udpAuthenticationEnabled: true);
        Assert.Equal(TimeSpan.FromSeconds(3), hub.AdmissionRetransmissionGrace);

        using var shortPhysical = new TestTransport();
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerNetworkHub(shortPhysical,
            Guid.NewGuid(), new RoutedMatchDatagramRouter(),
            admissionRetransmissionGrace: TimeSpan.FromSeconds(1)));
        using var longPhysical = new TestTransport();
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerNetworkHub(longPhysical,
            Guid.NewGuid(), new RoutedMatchDatagramRouter(),
            admissionRetransmissionGrace: TimeSpan.FromSeconds(6)));

        var clock = new ManualClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        using var replacementPhysical = new TestTransport();
        using var replacementHub = new WorkerNetworkHub(replacementPhysical, Guid.NewGuid(),
            new RoutedMatchDatagramRouter(), matchLimit: 1, udpAuthenticationEnabled: true,
            clock: clock);
        using MatchDatagramTransport match = replacementHub.RegisterMatch(1);
        Guid oldAdmission = Guid.NewGuid();
        Guid newAdmission = Guid.NewGuid();
        long expires = clock.GetUtcNow().ToUnixTimeSeconds() + 60;
        Assert.True(match.RegisterAdmissionId(oldAdmission, expires, out _));
        Assert.True(match.ReplaceAdmissionId(oldAdmission, newAdmission, expires, out bool replaced));
        Assert.True(replaced);
        Assert.Equal(1, match.ActiveAdmissionRouteCount);

        // The old lease is gone immediately; the replacement keeps its full
        // lease until its own authenticated join is accepted.
        clock.Advance(TimeSpan.FromSeconds(3));
        replacementHub.PumpOnce();
        Assert.Equal(1, match.ActiveAdmissionRouteCount);
        match.MarkAdmissionEstablished(newAdmission);
        clock.Advance(TimeSpan.FromSeconds(3));
        replacementHub.PumpOnce();
        Assert.Equal(0, match.ActiveAdmissionRouteCount);
    }

    [Fact]
    public void ServerNetworkSignalsAcceptedPlayerAdmissionAfterPublication()
    {
        using var transport = new TestTransport();
        using var authority = new ImmediateTicketAuthority(new PlayerId(Guid.NewGuid()), null, false);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        var network = new ServerNetwork(transport, rules, 1) { TicketAuthority = authority };
        Guid admissionId = Guid.NewGuid();
        EnqueueJoin(transport, new JoinPacket(NetHeader.Version, 1001, Hunter.Samus, "PLAYER",
            Ticket: "A.B.C", WireMatchId: 1, AdmissionId: admissionId));

        network.Poll(1);

        Assert.Equal(1, network.Count);
        Assert.Equal(new[] { admissionId }, transport.EstablishedAdmissions);
    }

    [Fact]
    public void ServerNetworkSignalsAcceptedObserverAdmissionAfterPublication()
    {
        using var transport = new TestTransport();
        using var authority = new ImmediateTicketAuthority(null, Guid.NewGuid(), true);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        var network = new ServerNetwork(transport, rules, 1, new ObserverOptions(1))
        {
            TicketAuthority = authority
        };
        network.CaptureObserverSnapshot(new byte[] { 1 });
        network.CaptureObserverWorld(new byte[] { 2 });
        network.Poll(1);
        network.CommitObserverTick(1);
        Guid admissionId = Guid.NewGuid();
        EnqueueJoin(transport, new JoinPacket(NetHeader.Version, 1002, Hunter.Samus, "WATCH",
            Ticket: "A.B.C", Observer: true, WireMatchId: 1, AdmissionId: admissionId));

        network.Poll(2);

        Assert.Equal(1, network.ObserverCount);
        Assert.Equal(new[] { admissionId }, transport.EstablishedAdmissions);
    }

    private static void EnqueueJoin(TestTransport transport, JoinPacket join)
    {
        byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
        join.Write(bytes.AsSpan(NetHeader.Size));
        transport.Incoming.Enqueue(new(Endpoint, bytes, bytes.Length));
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => 1;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += checked((long)duration.TotalSeconds);
        }
    }

    private sealed class ImmediateTicketAuthority(PlayerId? playerId, Guid? guestSessionId,
        bool trustedObserver) : IServerTicketAuthority
    {
        private readonly Queue<ValidatedTicketJoin> _results = new();

        public Guid ServerId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool RequireTickets => true;

        public bool Submit(IPEndPoint endpoint, in JoinPacket join)
        {
            _results.Enqueue(new(endpoint, join, new TicketIdentity(playerId, Guid.NewGuid(),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60, TrustedObserver: trustedObserver,
                GuestSessionId: guestSessionId)));
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

    private sealed class TestTransport : INetTransport, IMatchConnectionRoutes
    {
        public Queue<ReceivedPacket> Incoming { get; } = new();
        public List<Guid> EstablishedAdmissions { get; } = new();
        private ulong _nextConnectionId = 100;

        public int LocalPort => 50001;
        public long PacketsDropped => 0;
        public int QueuedPackets => Incoming.Count;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();

        public IEnumerable<ReceivedPacket> Drain()
        {
            while (Incoming.TryDequeue(out ReceivedPacket item)) yield return item;
        }

        public ulong AllocateConnectionId() => ++_nextConnectionId;
        public void RemoveConnection(ulong connectionId) { }
        public void MarkAdmissionEstablished(Guid admissionId) => EstablishedAdmissions.Add(admissionId);
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes, long extraHoldTicks) { }
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> bytes,
            long extraHoldTicks = 0) { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> bytes = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length)
            => throw new NotSupportedException();
        public void Dispose() { }
    }
}
