using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using SharedBotFillPolicy = ProjectPrime.Server.Shared.BotFillPolicy;
using Xunit;
using BotFillPolicy = ProjectPrime.Server.Shared.BotFillPolicy;

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
            now, now + 60, Guid.NewGuid(), HandoffGeneration.Initial);
        using var issuer = new WorkerAdmissionIssuer("node");
        using var authority = new WorkerTicketAuthority(spec, placement, issuer.KeyId, issuer.ExportPublicKey());
        Guid admissionId = Guid.NewGuid();
        Guid sessionId = claims.NodeSessionId;
        Guid ticketId = claims.TicketId;
        string admissionKey = Convert.ToBase64String(Enumerable.Range(0, AdmissionKeyRules.ByteLength).Select(value => (byte)value).ToArray());
        var admission = new InstallAdmissionKey(admissionId, ticketId, sessionId, spec.NodeId, spec.NodeIncarnation,
            spec.MatchId, placement.WireMatchId, placement.WorkerId, placement.WorkerIncarnation,
            claims.SeatId, claims.JoinNonce, now + 60, admissionKey, claims.HandoffGeneration);
        Assert.True(authority.TryInstallAdmissionKey(admission, out string installReason), installReason);
        Assert.True(authority.TryGetAdmissionKey(admissionId, out byte[] storedKey));
        Assert.Equal(Convert.FromBase64String(admissionKey), storedKey);
        storedKey[0] ^= 0xFF;
        Assert.True(authority.TryGetAdmissionKey(admissionId, out byte[] retainedKey));
        Assert.Equal(Convert.FromBase64String(admissionKey), retainedKey);
        Assert.False(authority.TryInstallAdmissionKey(admission with
        {
            AdmissionKey = Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, AdmissionKeyRules.ByteLength).ToArray())
        }, out _));
        Assert.False(authority.TryInstallAdmissionKey(admission with { MatchId = new MatchId(Guid.NewGuid()) }, out _));
        Assert.False(authority.TryInstallAdmissionKey(admission with { AdmissionId = Guid.NewGuid(), ExpiresAt = now - 1 }, out _));
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

    [Fact]
    public void AdmissionGenerationSupersessionAndExactRetirementAreBoundToTheSeatOwner()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        var player = new PlayerId(Guid.NewGuid());
        var spec = new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(), rules,
            new(rules.RoomKey, "hash", "AMHE1", "test", NetHeader.Version), MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(0, player, null, "SEAT", Hunter.Samus, 0, SeatRole.Player, false)),
            BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var placement = new MatchPlacement(spec.MatchId, new(55), new(Guid.NewGuid()), Guid.NewGuid(), "127.0.0.1", 50001);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var issuer = new WorkerAdmissionIssuer("generation-test");
        using var authority = new WorkerTicketAuthority(spec, placement, issuer.KeyId, issuer.ExportPublicKey());
        Guid session = Guid.NewGuid();

        WorkerAdmissionClaims Claims(HandoffGeneration generation, Guid ticketId, ulong nonce)
            => new(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation,
                spec.LobbyId, spec.MatchId, placement.WireMatchId, session, player, null, SeatRole.Player, 0,
                "SEAT", nonce, now, now + 60, ticketId, generation);
        InstallAdmissionKey Admission(WorkerAdmissionClaims claims, Guid admissionId, byte marker)
            => new(admissionId, claims.TicketId, claims.NodeSessionId, spec.NodeId, spec.NodeIncarnation,
                spec.MatchId, placement.WireMatchId, placement.WorkerId, placement.WorkerIncarnation,
                claims.SeatId, claims.JoinNonce, now + 60,
                Convert.ToBase64String(Enumerable.Range(0, AdmissionKeyRules.ByteLength)
                    .Select(value => (byte)(value + marker)).ToArray()), claims.HandoffGeneration);

        HandoffGeneration firstGeneration = new(1);
        HandoffGeneration secondGeneration = new(2);
        WorkerAdmissionClaims firstClaims = Claims(firstGeneration, Guid.NewGuid(), 1001);
        InstallAdmissionKey first = Admission(firstClaims, Guid.NewGuid(), 0);
        Assert.True(authority.TryInstallAdmissionKey(first, out string firstReason), firstReason);

        WorkerAdmissionClaims secondClaims = Claims(secondGeneration, Guid.NewGuid(), 1002);
        InstallAdmissionKey second = Admission(secondClaims, Guid.NewGuid(), 1);
        Assert.True(authority.TryInstallAdmissionKey(second, out string secondReason, out Guid superseded), secondReason);
        Assert.Equal(first.AdmissionId, superseded);
        Assert.False(authority.TryGetAdmissionKey(first.AdmissionId, out _));
        Assert.True(authority.TryGetAdmissionKey(second.AdmissionId, out byte[] retained));
        Assert.Equal(Convert.FromBase64String(second.AdmissionKey), retained);

        var staleRetire = new RetireAdmission(spec.MatchId, session, 0, firstGeneration,
            first.AdmissionId, placement.WorkerId, placement.WorkerIncarnation);
        Assert.False(authority.TryRetireAdmission(staleRetire, out string staleReason));
        Assert.Equal("admission_stale_generation", staleReason);
        Assert.True(authority.TryGetAdmissionKey(second.AdmissionId, out _));

        var join = new JoinPacket(NetHeader.Version, secondClaims.JoinNonce, Hunter.Samus, "SEAT",
            Ticket: issuer.Issue(secondClaims), WireMatchId: placement.WireMatchId.Value,
            AdmissionId: second.AdmissionId);
        var identity = new TicketIdentity(player, secondClaims.TicketId, secondClaims.ExpiresAt,
            ReservedSeat: 0, WorkerAdmission: true, NodeSessionId: session,
            HandoffGeneration: secondGeneration);
        Assert.True(authority.ValidateAdmissionIdentity(second.AdmissionId, join, identity));

        var retire = new RetireAdmission(spec.MatchId, session, 0, secondGeneration,
            second.AdmissionId, placement.WorkerId, placement.WorkerIncarnation);
        Assert.True(authority.TryRetireAdmission(retire, out string retireReason), retireReason);
        Assert.False(authority.TryGetAdmissionKey(second.AdmissionId, out _));
        // Repeating the exact retirement is idempotent; it cannot affect a
        // future lease for the same immutable owner.
        Assert.True(authority.TryRetireAdmission(retire, out _));

        // Retirement does not roll the owner generation back.  A delayed
        // install with the retired generation is rejected before a new lease
        // can be opened, and remains stale after a newer lease is installed.
        InstallAdmissionKey delayed = Admission(Claims(secondGeneration, Guid.NewGuid(), 1003), Guid.NewGuid(), 2);
        Assert.False(authority.TryInstallAdmissionKey(delayed, out string delayedReason));
        Assert.Equal("admission_stale_generation", delayedReason);
        InstallAdmissionKey third = Admission(Claims(new HandoffGeneration(3), Guid.NewGuid(), 1004), Guid.NewGuid(), 3);
        Assert.True(authority.TryInstallAdmissionKey(third, out string thirdReason), thirdReason);
        Assert.False(authority.TryInstallAdmissionKey(delayed, out delayedReason));
        Assert.Equal("admission_stale_generation", delayedReason);
        Assert.True(authority.TryGetAdmissionKey(third.AdmissionId, out _));
        Assert.False(authority.TryInstallAdmissionKey(first with
        {
            AdmissionId = Guid.NewGuid(), HandoffGeneration = default
        }, out string zeroReason));
        Assert.Equal("admission_generation", zeroReason);
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
            Guid.NewGuid(), player, guestId, SeatRole.Observer, 8, "WATCH", nonce, now, now + 60, ticketId,
            HandoffGeneration.Initial);
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
