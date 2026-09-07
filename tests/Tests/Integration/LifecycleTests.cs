using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class LifecycleTests
    {
        [Fact]
        public void TransitionCodecRejectsMalformedRoomsModesAndLengths()
        {
            byte[] bytes = new byte[MatchTransitionPacket.Size];
            var expected = new MatchTransitionPacket(UInt32.MaxValue, 777, GameMode.Nodes, "MP1 SANCTORUS");
            expected.Write(bytes);
            Assert.True(MatchTransitionPacket.TryRead(bytes, out var actual));
            Assert.Equal(expected, actual);
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.False(MatchTransitionPacket.TryRead(bytes.AsSpan(0, length), out _));
            }
            bytes[8] = 255;
            Assert.False(MatchTransitionPacket.TryRead(bytes, out _));
            expected.Write(bytes);
            bytes[9] = 255;
            Assert.False(MatchTransitionPacket.TryRead(bytes, out _));
        }

        [Fact]
        public void MapTransitionResetsInputSnapshotAndRejectsLateReadyAndSnapshot()
        {
            using var transport = new NetTransport(0);
            using var socket = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            // The fixture starts the peer manually and does not run a
            // ServerSimulation, therefore it must publish an explicit playing
            // phase before sending synthetic input.
            server.Phase = MatchPhase.Playing;
            server.PhaseRevision = 1;
            using var client = new NetClient(socket, endpoint, "LIFECYCLE", Hunter.Samus);
            Pump(server, client, () => client.Connection != null);
            Assert.True(client.Ready(1));
            Pump(server, client, () => server.Peers[0]!.Connection.State == NetConnectionState.Ready);
            ServerPeer peer = server.Peers[0]!;
            peer.Connection.StartPlaying();
            var command = new InputCommand(1, 1, 1, InputButtons.Forward, InputButtons.Jump,
                -Vector3.UnitZ, InputCommand.NoWeapon);
            Assert.True(client.SendInputs(new[] { command }, server.PhaseRevision));
            Pump(server, client, () => { peer.Inputs.Take(10); return peer.Inputs.HasProcessed; });
            Assert.True(peer.Inputs.HasProcessed);
            byte[] snapshot = Snapshot(1, peer);
            peer.Connection.Send(transport, NetMessageType.Snapshot, snapshot);
            Pump(server, client, () => client.HasSnapshot);
            Assert.Equal(NetConnectionState.Playing, client.State);
            Assert.True(client.SnapshotReceivedAt > 0);
            server.ChangeMatch(2, "MP3 PROVINGGROUND", GameMode.Nodes, 500);
            Assert.False(peer.Inputs.HasProcessed);
            Assert.Equal(NetConnectionState.Loading, peer.Connection.State);
            Pump(server, client, () => client.Accepted.MatchId == 2);
            Assert.False(client.HasSnapshot);
            Assert.Equal(0, client.SnapshotReceivedAt);
            Assert.Empty(client.SnapshotPlayers.ToArray());
            Assert.Equal("MP3 PROVINGGROUND", client.Accepted.Room);
            Span<byte> ready = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(ready, 1);
            Assert.True(client.Connection!.Reliable.TryEnqueue(ReliableEventType.ClientReady, ready, out _));
            peer.Connection.Send(transport, NetMessageType.Snapshot, snapshot);
            Pump(server, client, () => client.Connection.Reliable.PendingCount == 0);
            Assert.Equal(NetConnectionState.Loading, peer.Connection.State);
            Assert.False(client.HasSnapshot);
            Assert.True(client.Ready(2));
            Pump(server, client, () => peer.Connection.State == NetConnectionState.Ready
                && peer.Connection.Metrics.Rtt.Count > 0);
            Assert.True(peer.Connection.Metrics.SmoothedRttMs > 0);
            Assert.True(client.Disconnect());
            Pump(server, client, () => server.Count == 0);
        }

        [Fact]
        public void LoadingPeersPreventIdleAndAdmissionCanCloseAndReopen()
        {
            using var transport = new NetTransport(0);
            using var firstSocket = new NetTransport(0);
            using var secondSocket = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var first = new NetClient(firstSocket, endpoint, "LOADING", Hunter.Samus);
            Pump(server, first, () => first.Connection != null);
            ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(first.Connection!.Id));
            Assert.Equal(NetConnectionState.Loading, peer.Connection.State);
            Assert.Equal(1, server.Count); // The updater's owner gate includes unready players.

            server.AdmissionClosed = true;
            long rejected = server.Rejected;
            using var second = new NetClient(secondSocket, endpoint, "WAITING", Hunter.Samus);
            Pump(server, second, () => { first.Poll(); return server.Rejected > rejected; });
            Assert.Null(second.Connection);
            Assert.Equal(1, server.Count);
            Assert.Equal(NetConnectionState.Loading, peer.Connection.State);

            Assert.True(first.Disconnect());
            Pump(server, second, () => { first.Poll(); return server.Count == 0; });
            Assert.True(server.AdmissionClosed);
            Assert.Null(second.Connection);

            server.AdmissionClosed = false;
            Pump(server, second, () => second.Connection != null);
            Assert.Equal(1, server.Count);
            Assert.Equal(NetConnectionState.Loading, server.Find(second.Connection!.Id)!.Connection.State);
            Assert.True(second.Disconnect());
            Pump(server, second, () => server.Count == 0);
        }

        [Fact]
        public void ReadOnlyDiscoveryReportsCurrentProtocolWithoutTakingSlot()
        {
            using var transport = new NetTransport(0);
            using var socket = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            socket.SendDatagram(new IPEndPoint(IPAddress.Loopback, transport.LocalPort),
                new byte[] { (byte)PacketType.StatusQuery, 4 });
            bool received = false;
            var timer = Stopwatch.StartNew();
            while (!received && timer.Elapsed.TotalSeconds < 3)
            {
                server.Poll(1);
                foreach (ReceivedPacket packet in socket.Drain())
                {
                    Assert.Equal(PacketType.StatusReply, packet.Type);
                    var status = ServerStatusPacket.Read(packet.Payload);
                    Assert.Equal(NetHeader.Version, status.Protocol);
                    Assert.Equal(NetWireFamily.Authoritative, status.Family);
                    Assert.Equal("MP1 SANCTORUS", status.Match.RoomKey);
                    received = true;
                }
                Thread.Sleep(2);
            }
            Assert.True(received);
            Assert.Equal(0, server.Count);
        }

        private static byte[] Snapshot(uint matchId, ServerPeer peer)
        {
            byte[] payload = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
            var player = new SnapshotPlayer
            {
                Slot = peer.Slot, Hunter = peer.Hunter, ConnectionId = peer.Connection.Id,
                Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
                Life = 1, Aim = -Vector3.UnitZ, Facing = -Vector3.UnitZ, Health = 100, AvailableWeapons = 1
            };
            new SnapshotPacket(10, 1, matchId, 1, true, 1, 1).Write(payload, new[] { player });
            return payload;
        }

        private static void Pump(ServerNetwork server, NetClient client, Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 5)
            {
                server.Poll((uint)(timer.Elapsed.TotalSeconds * 60));
                client.Poll();
                if (done()) { return; }
                Thread.Sleep(2);
            }
            Assert.Fail("Lifecycle condition timed out: " + client.Failure);
        }
    }
    public sealed partial class ConnectionIntegrationTests
    {
        [Fact]
        public void ServerLoadingLongerThanTimeoutKeepsEightClientsConnected()
        {
            using var transport = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            var clients = new NetClient[8];
            var sockets = new NetTransport[8];
            var proxies = new ImpairedLink[8];
            try
            {
                for (int i = 0; i < clients.Length; i++)
                {
                    proxies[i] = new ImpairedLink(transport.LocalPort, 1300 + i);
                    sockets[i] = new NetTransport(0);
                    clients[i] = new NetClient(sockets[i], proxies[i].Endpoint, "LOAD" + i, Hunter.Samus);
                }
                PumpUntil(server, clients, () => Array.TrueForAll(clients,
                    client => client.HasRoster && client.Roster.Length == 8), 12);
                server.ChangeMatch(2, "MP3 PROVINGGROUND", GameMode.Nodes, 50);
                PumpUntil(server, clients, () => Array.TrueForAll(clients,
                    client => client.Accepted.MatchId == 2), 12);
                double initialArrival = clients[0].Connection!.LastReceived;
                var loading = Stopwatch.StartNew();
                // Deliberately never poll the server owner during this load.
                while (loading.Elapsed.TotalSeconds < NetConfig.TimeoutSeconds + 2)
                {
                    foreach (NetClient client in clients) { client.Poll(); }
                    Thread.Sleep(5);
                }
                foreach (NetClient client in clients)
                {
                    Assert.Null(client.Failure);
                    Assert.Equal(NetConnectionState.Loading, client.State);
                    Assert.True(client.Connection!.LastReceived - initialArrival > NetConfig.TimeoutSeconds - 2);
                    Assert.True(client.Ready(2));
                }
                PumpUntil(server, clients, () => AllReady(server), 12);
                Assert.Equal(8, server.Count);
            }
            finally
            {
                foreach (NetClient? client in clients) { client?.Dispose(); }
                foreach (NetTransport? socket in sockets) { socket?.Dispose(); }
                foreach (ImpairedLink? proxy in proxies) { proxy?.Dispose(); }
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void RotationReconnectAndDisconnectSurviveLossAndReordering(int count)
        {
            using var transport = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            var clients = new NetClient[count];
            var sockets = new NetTransport[count];
            var proxies = new ImpairedLink[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    proxies[i] = new ImpairedLink(transport.LocalPort, 900 + i);
                    sockets[i] = new NetTransport(0);
                    clients[i] = new NetClient(sockets[i], proxies[i].Endpoint, "ROTATE" + i, Hunter.Samus);
                }
                PumpUntil(server, clients, () => Array.TrueForAll(clients, c => c.Connection != null), 12);
                PumpUntil(server, clients, () => Array.TrueForAll(clients, c => c.Roster.Length == count), 12);
                var chatCounts = new int[count];
                foreach (NetClient client in clients) { Assert.True(client.SendChat("hello")); }
                PumpUntil(server, clients, () =>
                {
                    for (int index = 0; index < count; index++)
                    {
                        while (clients[index].TryDequeueEvent(out var item))
                        {
                            Assert.Equal(ReliableEventType.Chat, item.Type);
                            Assert.True(SessionChatPacket.TryRead(item.Payload.Span, out var chat));
                            Assert.Equal("hello", chat.Text);
                            Assert.Contains(clients, sender => sender.Connection!.Id == chat.ConnectionId
                                && sender.Accepted.Slot == chat.Slot);
                            chatCounts[index]++;
                        }
                    }
                    return Array.TrueForAll(chatCounts, delivered => delivered == count);
                }, 12);
                foreach (NetClient client in clients) { Assert.True(client.Ready(1)); }
                PumpUntil(server, clients, () => AllReady(server), 12);
                foreach (ServerPeer? peer in server.Peers) { peer?.Connection.StartPlaying(); }
                Assert.True(server.TryBroadcastEvent(ReliableEventType.MatchState, new byte[] { 42 }));
                int delivered = 0;
                PumpUntil(server, clients, () =>
                {
                    foreach (NetClient client in clients)
                    {
                        while (client.TryDequeueEvent(out var item))
                        {
                            Assert.Equal(1u, item.MatchId);
                            Assert.Equal(42, item.Payload.Span[0]);
                            delivered++;
                        }
                    }
                    return delivered >= count;
                }, 12);
                Assert.Equal(count, delivered);
                ulong oldId = clients[0].Connection!.Id;
                server.ChangeMatch(2, "MP3 PROVINGGROUND", GameMode.Nodes, 500);
                clients[0].Reconnect(); // Admission races the transition, including delayed old traffic.
                PumpUntil(server, clients, () => Array.TrueForAll(clients,
                    c => c.Connection != null && c.Accepted.MatchId == 2), 12);
                Assert.Null(server.Find(oldId));
                Assert.NotEqual(oldId, clients[0].Connection!.Id);
                foreach (NetClient client in clients)
                {
                    Assert.Equal(NetConnectionState.Loading, client.State);
                    Assert.False(client.HasSnapshot);
                    Assert.False(client.Ready(1));
                    Assert.True(client.Ready(2));
                }
                PumpUntil(server, clients, () => AllReady(server), 12);
                foreach (ServerPeer? peer in server.Peers)
                {
                    if (peer != null) { Assert.False(peer.Inputs.HasProcessed); }
                }
                foreach (NetClient client in clients) { Assert.True(client.Disconnect()); }
                PumpUntil(server, clients, () => server.Count == 0, 12);
            }
            finally
            {
                foreach (NetClient? client in clients) { client?.Dispose(); }
                foreach (NetTransport? socket in sockets) { socket?.Dispose(); }
                foreach (ImpairedLink? proxy in proxies) { proxy?.Dispose(); }
            }
        }
    }
}
