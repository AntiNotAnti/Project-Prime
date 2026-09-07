using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class VoteDeliveryTests
{
    [Fact]
    public void ReliableBallotReorderingCannotEraseConfirmedVoteAndRequestRoundTrips()
    {
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
        using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, transport.LocalPort), "VOTER", Hunter.Samus);
        void Pump(Func<bool> complete)
        {
            var clock = Stopwatch.StartNew();
            while (!complete() && clock.ElapsedMilliseconds < 3000)
            { server.Poll((uint)(clock.Elapsed.TotalSeconds * 60)); client.Poll(); Thread.Sleep(1); }
            Assert.True(complete(), "Timed out waiting for loopback vote delivery.");
        }
        Pump(() => client.HasRoster);
        ServerPeer peer = server.Peers[client.Accepted.Slot]!;
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(() => peer.Connection.State == NetConnectionState.Ready);
        var ballot = new IntermissionBallot(server.MatchId, 9, 1, 300, MatchPhase.Intermission, true, 0, 1,
            ImmutableArray.Create(new IntermissionOption(1, IntermissionChoice.Rematch, 0, "Rematch")), 1);
        void Send(IntermissionBallot item)
        {
            var body = new byte[IntermissionBallot.MaximumSize]; int n = item.Write(body);
            Assert.True(peer.Connection.Reliable.TryEnqueue(ReliableEventType.IntermissionBallot, body.AsSpan(0, n), out _));
        }
        Send(ballot); Pump(() => client.Ballot != null);
        bool received = false;
        server.IntermissionVoteReceived = (sender, request) =>
        {
            Assert.Same(peer, sender); Assert.Equal(new IntermissionVoteRequest(server.MatchId, 9, 1, 1), request);
            received = true; return true;
        };
        Assert.True(client.Vote(1)); Pump(() => received);
        Send(ballot with { SelectedId = 1, UpdateRevision = 3,
            Options = ImmutableArray.Create(new IntermissionOption(1, IntermissionChoice.Rematch, 1, "Rematch")) });
        Pump(() => client.Ballot!.SelectedId == 1);
        Send(ballot with { UpdateRevision = 2 });
        Pump(() => peer.Connection.Reliable.PendingCount == 0);
        Assert.Equal(3u, client.Ballot!.UpdateRevision);
        Assert.Equal(1, client.Ballot!.SelectedId);
        Assert.False(client.Vote(1));
    }
    [Fact]
    public void ActualContentLobbyRematchReleasesOnlyVoteGate()
    {
        using var saved = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? "/Users/jarrett/Documents/Development/Fruity-Prime/AMHE1", "AMHE1");
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, transport.LocalPort), "LOBBY", Hunter.Samus);
        void Pump(Func<bool> complete)
        {
            var clock = Stopwatch.StartNew();
            while (!complete() && clock.ElapsedMilliseconds < 3000)
            { server.Poll(1); client.Poll(); Thread.Sleep(1); }
            Assert.True(complete());
        }
        Pump(() => client.HasRoster); Assert.True(client.Ready(client.Accepted.MatchId));
        var peer = server.Peers[client.Accepted.Slot]!;
        Pump(() => peer.Connection.State == NetConnectionState.Ready);
        simulation.VoteLobbyHold = true;
        var voting = new ServerVoting(server, () => simulation, new(VotePolicy.PrivateRematch), null, 42);
        simulation.Step(server, 1); voting.Tick(1);
        Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
        Assert.False(voting.Ballot.HasDeadline);
        Assert.True(voting.Ballot.Cast(peer.Slot, peer.Connection.Id,
            new(server.MatchId, voting.Ballot.PhaseRevision, voting.Ballot.Revision, 1), 2));
        simulation.AdminMayStart = false; simulation.ReportingMayStart = false;
        voting.Tick(voting.Ballot.DeadlineTick);
        Assert.False(simulation.VoteLobbyHold);
        simulation.Step(server, 303);
        Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
        simulation.AdminMayStart = true; simulation.Step(server, 304);
        Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
        simulation.ReportingMayStart = true; simulation.Step(server, 305);
        Assert.Equal(MatchPhase.Countdown, simulation.Scene.Match.Phase);
    }
}
