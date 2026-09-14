using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using MphRead.Identity;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class FrozenMixedRosterAdmissionTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void FrozenMixedRosterOwnsConcurrentLateAndReconnectAdmissionsWithoutBotSeatTheft()
    {
        using IDisposable content = OpenContent();
        PlayerId firstPlayer = new(Guid.NewGuid());
        PlayerId latePlayer = new(Guid.NewGuid());
        PlayerId returningPlayer = new(Guid.NewGuid());
        MatchSpec spec = Spec(MatchMode.TeamBattle, firstPlayer, latePlayer, returningPlayer);
        using var authority = new StaticTicketAuthority();
        using var transport = new MemoryTransport();
        using var match = new MatchInstance(new(spec, WireMatchId)
        {
            Tickets = authority,
            UdpAuthenticationEnabled = false
        }, transport);

        authority.Add("bot.seat.theft", Identity(firstPlayer, seat: 1, team: 0));
        Send(transport, Join("bot.seat.theft", 100, Hunter.Spire, "BOT ALPHA"), 51000);
        match.Network.Poll(1);

        Assert.Equal(0, match.Network.Count);
        AssertBotsUnchanged(match);

        authority.Add("first.concurrent.ticket", Identity(firstPlayer, seat: 0, team: 0));
        authority.Add("returning.concurrent.ticket", Identity(returningPlayer, seat: 4, team: 1));
        Send(transport, Join("first.concurrent.ticket", 101, Hunter.Samus, "FIRST"), 51001);
        Send(transport, Join("returning.concurrent.ticket", 102, Hunter.Trace, "RETURNING"), 51002);
        match.Network.Poll(2);

        AssertHuman(match.Network.Peers[0], firstPlayer, 0, 0, "FIRST");
        AssertHuman(match.Network.Peers[4], returningPlayer, 4, 1, "RETURNING");
        Assert.Equal(2, match.Network.Count);
        AssertBotsUnchanged(match);

        match.Network.Phase = MatchPhase.Playing;
        authority.Add("late.initial.ticket", Identity(latePlayer, seat: 3, team: 1));
        Send(transport, Join("late.initial.ticket", 103, Hunter.Noxus, "LATE"), 51003);
        match.Network.Poll(3);

        AssertHuman(match.Network.Peers[3], latePlayer, 3, 1, "LATE");
        Assert.False(match.Network.Peers[3]!.WaitingForNextMatch);
        Assert.Equal(3, match.Network.Count);
        AssertBotsUnchanged(match);

        ServerPeer previous = match.Network.Peers[4]!;
        previous.HasParticipated = true;
        match.Network.Remove(4);
        Assert.True(match.Network.HasReconnectReservation(4));
        Assert.Equal(2, match.Network.Count);

        authority.Add("returning.reconnect.ticket", Identity(returningPlayer, seat: 4, team: 1));
        Send(transport, Join("returning.reconnect.ticket", 104, Hunter.Trace, "RETURNING",
            previous.Connection.Id), 51004);
        match.Network.Poll(4);

        ServerPeer resumed = match.Network.Peers[4]!;
        AssertHuman(resumed, returningPlayer, 4, 1, "RETURNING");
        Assert.True(resumed.ReturningParticipant);
        Assert.Equal(previous.Connection.Id, resumed.ReturningFromConnectionId);
        Assert.Equal(NetConnectionState.Disconnecting, previous.Connection.State);
        Assert.Equal(3, match.Network.Count);
        AssertBotsUnchanged(match);
    }

    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData(MatchMode.TeamBattle)]
    [InlineData(MatchMode.TeamSurvival)]
    [InlineData(MatchMode.Capture)]
    [InlineData(MatchMode.TeamBounty)]
    [InlineData(MatchMode.TeamNodes)]
    [InlineData(MatchMode.TeamDefender)]
    public void EveryTeamModePreservesFrozenHumanAndBotAssignments(MatchMode mode)
    {
        using IDisposable content = OpenContent();
        PlayerId firstPlayer = new(Guid.NewGuid());
        PlayerId latePlayer = new(Guid.NewGuid());
        PlayerId returningPlayer = new(Guid.NewGuid());
        MatchSpec spec = Spec(mode, firstPlayer, latePlayer, returningPlayer);
        using var authority = new StaticTicketAuthority();
        using var transport = new MemoryTransport();
        using var match = new MatchInstance(new(spec, WireMatchId)
        {
            Tickets = authority,
            UdpAuthenticationEnabled = false
        }, transport);

        authority.Add("team.zero.ticket", Identity(firstPlayer, seat: 0, team: 0));
        authority.Add("team.one.ticket", Identity(returningPlayer, seat: 4, team: 1));
        Send(transport, Join("team.zero.ticket", 201, Hunter.Samus, "FIRST"), 51101);
        Send(transport, Join("team.one.ticket", 202, Hunter.Trace, "RETURNING"), 51102);
        match.Network.Poll(1);

        Assert.True(match.Network.Rules.Teams);
        Assert.Equal(2, match.Network.Rules.TeamCount);
        AssertHuman(match.Network.Peers[0], firstPlayer, 0, 0, "FIRST");
        AssertHuman(match.Network.Peers[4], returningPlayer, 4, 1, "RETURNING");
        AssertBotsUnchanged(match);
    }

    private const uint WireMatchId = 87;

    private static MatchSpec Spec(MatchMode mode, PlayerId firstPlayer, PlayerId latePlayer,
        PlayerId returningPlayer)
    {
        string room = mode == MatchMode.TeamBounty
            ? "MP4 HIGHGROUND - EXPANDED" : "MP1 SANCTORUS";
        var rules = new MatchRules(mode, room, maxPlayers: 5,
            timeLimit: TimeSpan.FromMinutes(7), scoreGoal: 7, startingLives: 2,
            teamBalancePolicy: TeamBalancePolicy.Locked, teamCount: 2);
        return new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(),
            rules, new(rules.RoomKey, "test-content", "AMHE1", "test-build", NetHeader.Version),
            MatchTrustClass.Private, null, null, ImmutableArray.Create(
                new RosterSeat(0, firstPlayer, null, "FIRST", Hunter.Samus, 0, SeatRole.Player, false),
                new RosterSeat(1, null, null, "BOT ALPHA", Hunter.Spire, 0, SeatRole.Bot, false),
                new RosterSeat(2, null, null, "BOT BRAVO", Hunter.Kanden, 1, SeatRole.Bot, false),
                new RosterSeat(3, latePlayer, null, "LATE", Hunter.Noxus, 1, SeatRole.Player, false),
                new RosterSeat(4, returningPlayer, null, "RETURNING", Hunter.Trace, 1, SeatRole.Player, false)),
            ProjectPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled,
            ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 123, 456);
    }

    private static TicketIdentity Identity(PlayerId player, byte seat, byte team) => new(player,
        Guid.NewGuid(), DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60, ReservedSeat: seat,
        WorkerAdmission: true, ReservedTeam: team, NodeSessionId: Guid.NewGuid(),
        HandoffGeneration: HandoffGeneration.Initial);

    private static JoinPacket Join(string ticket, ulong nonce, Hunter hunter, string name,
        ulong previousConnectionId = 0) => new(NetHeader.Version, nonce, hunter, name,
            previousConnectionId, ticket, WireMatchId: WireMatchId);

    private static void Send(MemoryTransport transport, JoinPacket join, int port)
    {
        byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
        join.Write(bytes.AsSpan(NetHeader.Size));
        transport.Incoming.Enqueue(new(new IPEndPoint(IPAddress.Loopback, port), bytes, bytes.Length));
    }

    private static void AssertHuman(ServerPeer? peer, PlayerId player, byte seat, byte team,
        string name)
    {
        Assert.NotNull(peer);
        Assert.Equal(player, peer.PlayerId);
        Assert.Equal(seat, peer.ReservedSeat);
        Assert.Equal(seat, peer.Slot);
        Assert.Equal(team, peer.TeamIndex);
        Assert.Equal(name, peer.Name);
    }

    private static void AssertBotsUnchanged(MatchInstance match)
    {
        Assert.Equal(2, match.Simulation.Bots.Count);
        Assert.Equal("BOT ALPHA", match.Simulation.Bots.Participants[1]!.Name);
        Assert.Equal((byte)0, match.Simulation.Bots.Participants[1]!.TeamIndex);
        Assert.Equal("BOT BRAVO", match.Simulation.Bots.Participants[2]!.Name);
        Assert.Equal((byte)1, match.Simulation.Bots.Participants[2]!.TeamIndex);
        Assert.Null(match.Network.Peers[1]);
        Assert.Null(match.Network.Peers[2]);
    }

    private static IDisposable OpenContent()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        IDisposable context = ServerContent.PreserveContext("AMHE1");
        try
        {
            ServerContent.Open(data, "AMHE1");
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private sealed class StaticTicketAuthority : IServerTicketAuthority
    {
        private readonly Dictionary<string, TicketIdentity> _identities = new(StringComparer.Ordinal);
        private readonly Queue<ValidatedTicketJoin> _results = new();

        public Guid ServerId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool RequireTickets => true;

        public void Add(string ticket, TicketIdentity identity) => _identities.Add(ticket, identity);

        public bool Submit(IPEndPoint endpoint, in JoinPacket join)
        {
            _results.Enqueue(new(endpoint, join,
                _identities.TryGetValue(join.Ticket, out TicketIdentity identity) ? identity : null));
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
        public Queue<ReceivedPacket> Incoming { get; } = new();
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
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes, long extraHoldTicks) { }
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> bytes,
            long extraHoldTicks = 0) { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> bytes = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
