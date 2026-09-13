using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class TeamAllocatorTests
    {
        [Fact]
        public void RebalanceExhaustivelyUsesTheMinimumHighestSlotMoves()
        {
            for (int code = 0; code < 6561; code++) // 3^8: absent, orange, green.
            {
                byte[] teams = Decode(code);
                byte[] original = (byte[])teams.Clone();
                int orange = teams.Count(team => team == 0);
                int green = teams.Count(team => team == 1);
                int expectedMoves = Math.Abs(orange - green) / 2;
                byte[] expected = (byte[])original.Clone();
                for (int slot = expected.Length - 1; slot >= 0 && Math.Abs(orange - green) > 1; slot--)
                {
                    byte larger = orange > green ? (byte)0 : (byte)1;
                    if (expected[slot] != larger) { continue; }
                    expected[slot] = (byte)(1 - larger);
                    orange += larger == 0 ? -1 : 1;
                    green += larger == 1 ? -1 : 1;
                }

                int actualMoves = TeamAllocator.Rebalance(teams);

                Assert.Equal(expectedMoves, actualMoves);
                Assert.Equal(expected, teams);
                Assert.InRange(Math.Abs(teams.Count(team => team == 0) - teams.Count(team => team == 1)), 0, 1);
                for (int slot = 0; slot < teams.Length; slot++)
                {
                    Assert.True(teams[slot] == original[slot]
                        || (original[slot] is 0 or 1 && teams[slot] == (byte)(1 - original[slot])));
                }

                byte[] repeat = (byte[])original.Clone();
                Assert.Equal(actualMoves, TeamAllocator.Rebalance(repeat));
                Assert.Equal(teams, repeat);
            }
        }

        [Fact]
        public void AdmissionBalancesTeamsAndRebalanceIsGatedBeforePlaying()
        {
            using var serverTransport = new NetTransport(0);
            var server = new ServerNetwork(serverTransport,
                new MatchRules(MatchMode.TeamBattle, "MP1 SANCTORUS", maxPlayers: 4));
            var sockets = new NetTransport[4];
            var clients = new NetClient[4];
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort);
                for (int i = 0; i < clients.Length; i++)
                {
                    sockets[i] = new NetTransport(0);
                    clients[i] = new NetClient(sockets[i], endpoint, "TEAM" + i, Hunter.Samus);
                }
                Pump(server, clients, () => clients.All(client => client.Connection != null));
                Assert.Equal(4, server.Count);
                Assert.Equal(2, CountTeam(server, 0));
                Assert.Equal(2, CountTeam(server, 1));

                foreach (NetClient client in clients)
                {
                    Assert.True(client.Ready(client.Accepted.MatchId));
                }
                Pump(server, clients, () => server.Peers.ToArray().Where(peer => peer != null)
                    .All(peer => peer!.Connection.State == NetConnectionState.Ready));

                // An artificial pre-match imbalance lets this test exercise the
                // server's lifecycle gate without relying on network timing.
                foreach (ServerPeer peer in server.Peers.ToArray().OfType<ServerPeer>())
                {
                    peer.TeamIndex = 0;
                }
                server.Phase = MatchPhase.Playing;
                Assert.False(server.RebalanceBeforeStart());
                Assert.Equal(4, CountTeam(server, 0));
                Assert.Equal(0, CountTeam(server, 1));

                server.Phase = MatchPhase.Countdown;
                Assert.True(server.RebalanceBeforeStart());
                Assert.Equal(2, CountTeam(server, 0));
                Assert.Equal(2, CountTeam(server, 1));
            }
            finally
            {
                foreach (NetClient? client in clients) { client?.Dispose(); }
                foreach (NetTransport? socket in sockets) { socket?.Dispose(); }
            }
        }

        [Fact]
        public void FourTeamAllocationAndRebalanceAreDeterministic()
        {
            byte[] teams = [0, 0, 0, 0, 1, 2, 3, TeamAllocator.Unassigned];

            Assert.Equal(1, TeamAllocator.Select(teams, tieBreak: 1,
                teamCount: 4));
            Assert.Equal(2, TeamAllocator.Rebalance(teams, teamCount: 4));
            Assert.Equal(new byte[] { 0, 0, 2, 1, 1, 2, 3,
                TeamAllocator.Unassigned }, teams);
        }

        private static byte[] Decode(int code)
        {
            var teams = new byte[8];
            int value = code;
            for (int slot = 0; slot < teams.Length; slot++)
            {
                teams[slot] = (byte)(value % 3 == 0 ? TeamAllocator.Unassigned : value % 3 - 1);
                value /= 3;
            }
            return teams;
        }

        private static int CountTeam(ServerNetwork server, byte team)
            => server.Peers.ToArray().Count(peer => peer?.TeamIndex == team);

        private static void Pump(ServerNetwork server, NetClient[] clients, Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            uint tick = 0;
            while (timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                server.Poll(tick++);
                foreach (NetClient client in clients) { client.Poll(); }
                if (done()) { return; }
                Thread.Sleep(2);
            }
            Assert.Fail("Team allocator network fixture timed out.");
        }
    }
}
