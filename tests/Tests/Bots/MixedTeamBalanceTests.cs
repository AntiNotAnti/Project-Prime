using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests;
[Collection("Match baseline globals")]
public sealed class MixedTeamBalanceTests
{
    [Fact]
    public void PrestartBalancesHumansAndBotsTogetherWithoutResettingBodies()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1"); ServerContent.Open(data, "AMHE1");
        var rules = new MatchRules(MatchMode.TeamBattle, "MP1 SANCTORUS");
        using var simulation = new ServerSimulation(rules, botFill: new(4));
        using var transport = new NetTransport(0);
        var network = new ServerNetwork(transport, rules);
        network.BotRosterEntry = simulation.Bots.Roster;
        network.BotTeamAssigned = simulation.Bots.AssignTeam;
        network.CanClaimPlayerSlot = slot => simulation.Bots.Claim(slot, network, network.Tick);
        using var socket0 = new NetTransport(0); using var socket1 = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        using var client0 = new NetClient(socket0, endpoint, "P0", Hunter.Samus);
        using var client1 = new NetClient(socket1, endpoint, "P1", Hunter.Samus);
        uint tick = 1; var timer = Stopwatch.StartNew();
        while (network.Count < 2 && timer.ElapsedMilliseconds < 3000)
        { network.Poll(tick++); client0.Poll(); client1.Poll(); Thread.Sleep(1); }
        while ((!client0.HasRoster || !client1.HasRoster) && timer.ElapsedMilliseconds < 3000)
        { network.Poll(tick++); client0.Poll(); client1.Poll(); Thread.Sleep(1); }
        Assert.True(client0.Ready(1)); Assert.True(client1.Ready(1)); timer.Restart();
        while ((network.Peers[0]!.Connection.State != NetConnectionState.Ready || network.Peers[1]!.Connection.State != NetConnectionState.Ready)
            && timer.ElapsedMilliseconds < 3000)
        { network.Poll(tick++); client0.Poll(); client1.Poll(); Thread.Sleep(1); }
        simulation.Step(network, tick++);
        Assert.Equal(2, simulation.Bots.Count);
        network.Phase = MatchPhase.WaitingForPlayers;
        network.Peers[0]!.TeamIndex = network.Peers[1]!.TeamIndex = 0;
        simulation.Bots.AssignTeam(2, 1); simulation.Bots.AssignTeam(3, 1);
        Assert.False(network.RebalanceBeforeStart()); // Already 2:2; human-only balancing made this 1:3.
        simulation.Bots.AssignTeam(2, 0); simulation.Bots.AssignTeam(3, 0);
        var position = PlayerEntity.Players[3].Position; int health = PlayerEntity.Players[3].Health;
        ulong identity = simulation.Bots.Roster(3)!.Value.ConnectionId;
        Assert.True(network.RebalanceBeforeStart());
        int team0 = 0;
        for (int slot = 0; slot < 4; slot++)
        {
            int team = network.Peers[slot]?.TeamIndex ?? simulation.Bots.Roster(slot)!.Value.Team;
            if (team == 0) team0++;
            if (slot >= 2) Assert.Equal(team, PlayerEntity.Players[slot].TeamIndex);
        }
        Assert.Equal(2, team0); Assert.Equal(position, PlayerEntity.Players[3].Position);
        Assert.Equal(health, PlayerEntity.Players[3].Health); Assert.Equal(identity, simulation.Bots.Roster(3)!.Value.ConnectionId);
        using var socket2 = new NetTransport(0);
        using var client2 = new NetClient(socket2, endpoint, "P2", Hunter.Samus);
        timer.Restart();
        while (!client2.HasRoster && timer.ElapsedMilliseconds < 3000)
        { network.Poll(tick++); client0.Poll(); client1.Poll(); client2.Poll(); Thread.Sleep(1); }
        Assert.True(client2.HasRoster); Assert.Equal(4, client2.Accepted.Slot);
        // Actual teams are tied2:2, so slot4 uses deterministic team0. Counting
        // only the two team0 humans incorrectly selected team1.
        Assert.Equal(0, network.Peers[4]!.TeamIndex);
        network.AssignAdminTeam(network.Peers[0]!, 0);
        simulation.Bots.AssignTeam(2, 0); simulation.Bots.AssignTeam(3, 0);
        Assert.False(network.RebalanceBeforeStart());
    }
}
