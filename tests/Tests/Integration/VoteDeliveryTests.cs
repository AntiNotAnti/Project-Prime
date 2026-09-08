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
}
