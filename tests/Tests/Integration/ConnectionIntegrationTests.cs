using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed partial class ConnectionIntegrationTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void JoinReadyAndReconnectSurviveExtremeWan(int count)
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
                    proxies[i] = new ImpairedLink(transport.LocalPort, 500 + i);
                    sockets[i] = new NetTransport(0);
                    clients[i] = new NetClient(sockets[i], proxies[i].Endpoint, "TEST" + i, Hunter.Samus);
                }
                PumpUntil(server, clients, () => Array.TrueForAll(clients,
                    c => c.State == NetConnectionState.Loading), 12);
                Assert.Equal(count, server.Count);
                var identities = new HashSet<ulong>();
                var slots = new HashSet<byte>();
                foreach (NetClient client in clients)
                {
                    Assert.True(identities.Add(client.Connection!.Id));
                    Assert.True(slots.Add(client.Accepted.Slot));
                    Assert.False(client.Ready(client.Accepted.MatchId - 1));
                    Assert.True(client.Ready(client.Accepted.MatchId));
                    Assert.False(client.Ready(client.Accepted.MatchId));
                }
                PumpUntil(server, clients, () => AllReady(server) && Array.TrueForAll(clients,
                    c => c.Clock.Synchronized && c.Connection!.Reliable.PendingCount == 0), 12);
                ulong oldId = clients[0].Connection!.Id;
                byte oldSlot = clients[0].Accepted.Slot;
                clients[0].Reconnect();
                PumpUntil(server, clients, () => clients[0].State == NetConnectionState.Loading, 12);
                Assert.Equal(count, server.Count);
                Assert.NotEqual(oldId, clients[0].Connection!.Id);
                Assert.Equal(oldSlot, clients[0].Accepted.Slot);
                Assert.Null(server.Find(oldId));
                Assert.True(clients[0].Ready(clients[0].Accepted.MatchId));
                PumpUntil(server, clients, () => AllReady(server), 12);
                foreach (NetClient client in clients)
                {
                    Assert.Null(client.Failure);
                }
            }
            finally
            {
                foreach (NetClient? client in clients) { client?.Dispose(); }
                foreach (NetTransport? socket in sockets) { socket?.Dispose(); }
                foreach (ImpairedLink? proxy in proxies) { proxy?.Dispose(); }
            }
        }

        [Fact]
        public void LoadingLongerThanTimeoutStaysConnectedWithoutGameThreadPolling()
        {
            using var transport = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var link = new ImpairedLink(transport.LocalPort, 42);
            using var socket = new NetTransport(0);
            using var client = new NetClient(socket, link.Endpoint, "LOADING", Hunter.Kanden);
            PumpUntil(server, new[] { client }, () => client.Connection != null, 12);
            ulong id = client.Connection!.Id;
            double initialArrival = server.Find(id)!.Connection.LastReceived;
            var loading = Stopwatch.StartNew();
            while (loading.Elapsed.TotalSeconds < NetConfig.TimeoutSeconds + 2)
            {
                server.Poll((uint)(loading.Elapsed.TotalSeconds * 60));
                Thread.Sleep(5);
            }
            ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(id));
            Assert.Equal(NetConnectionState.Loading, peer.Connection.State);
            Assert.True(peer.Connection.LastReceived - initialArrival > NetConfig.TimeoutSeconds - 2);
            Assert.True(client.Ready(client.Accepted.MatchId));
            PumpUntil(server, new[] { client }, () => AllReady(server), 12);
            Assert.Null(client.Failure);
        }

        [Fact]
        public void WrongProtocolIsRefusedAndMalformedBodiesCannotAcknowledgeWelcome()
        {
            using var transport = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var socket = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            byte[] request = new byte[NetHeader.Size + JoinPacket.Size];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(request);
            new JoinPacket(5, 123, Hunter.Samus, "OLD").Write(request.AsSpan(NetHeader.Size));
            socket.SendDatagram(endpoint, request);
            bool refused = false;
            PumpUntil(server, Array.Empty<NetClient>(), () =>
            {
                foreach (ReceivedPacket packet in socket.Drain())
                {
                    refused |= NetHeader.TryRead(packet.Data, out NetHeader header)
                        && header.Type == NetMessageType.Refused;
                }
                return refused;
            }, 3);
            Assert.Equal(0, server.Count);
            new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "NEW").Write(request.AsSpan(NetHeader.Size));
            socket.SendDatagram(endpoint, request);
            PumpUntil(server, Array.Empty<NetClient>(), () => server.Count == 1, 3);
            ServerPeer peer = Assert.IsType<ServerPeer>(server.Peers[0]);
            Assert.Equal(2, peer.Connection.Reliable.PendingCount);
            byte[] badAck = new byte[NetHeader.Size + 1];
            new NetHeader(NetMessageType.Ack, NetHeaderFlags.HasAck, peer.Connection.Id, 1, 0, 0).Write(badAck);
            long rejected = server.Rejected;
            socket.SendDatagram(endpoint, badAck);
            PumpUntil(server, Array.Empty<NetClient>(), () => server.Rejected > rejected, 3);
            Assert.Equal(2, peer.Connection.Reliable.PendingCount);
        }

        private static bool AllReady(ServerNetwork server)
        {
            foreach (ServerPeer? peer in server.Peers)
            {
                if (peer != null && peer.Connection.State != NetConnectionState.Ready)
                {
                    return false;
                }
            }
            return server.Count > 0;
        }

        private static void PumpUntil(ServerNetwork server, NetClient[] clients, Func<bool> done, int seconds)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed.TotalSeconds < seconds)
            {
                server.Poll((uint)(Stopwatch.GetTimestamp() * (60.0 / Stopwatch.Frequency)));
                foreach (NetClient client in clients) { client.Poll(); }
                if (done()) { return; }
                Thread.Sleep(2);
            }
            Assert.Fail("Connection condition did not complete before its deadline.");
        }

        // Test-only actual UDP proxy: 250 +/- 50 ms RTT, independent 3% loss
        // and 2% duplicate datagrams each way. PriorityQueue permits reordering.
        private sealed class ImpairedLink : IDisposable
        {
            private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly IPEndPoint _server;
            private readonly Thread _thread;
            private readonly Random _random;
            private readonly PriorityQueue<(byte[] Data, IPEndPoint Target), long> _pending = new();
            private volatile bool _running = true;
            public IPEndPoint Endpoint => (IPEndPoint)_socket.Client.LocalEndPoint!;

            public ImpairedLink(int serverPort, int seed)
            {
                _server = new IPEndPoint(IPAddress.Loopback, serverPort);
                _random = new Random(seed);
                _thread = new Thread(Run) { IsBackground = true };
                _thread.Start();
            }

            private void Run()
            {
                IPEndPoint? client = null;
                while (_running)
                {
                    while (_socket.Available > 0)
                    {
                        IPEndPoint from = new(IPAddress.Any, 0);
                        byte[] bytes = _socket.Receive(ref from);
                        IPEndPoint? target;
                        if (from.Equals(_server)) { target = client; }
                        else { client = from; target = _server; }
                        if (target == null || _random.Next(100) < 3) { continue; }
                        Queue(bytes, target);
                        if (_random.Next(100) < 2) { Queue(bytes, target); }
                    }
                    long now = Stopwatch.GetTimestamp();
                    while (_pending.TryPeek(out _, out long due) && due <= now)
                    {
                        var packet = _pending.Dequeue();
                        _socket.Send(packet.Data, packet.Target);
                    }
                    Thread.Sleep(1);
                }
            }

            private void Queue(byte[] data, IPEndPoint target)
            {
                if (_pending.Count < 4096)
                {
                    long due = Stopwatch.GetTimestamp() + (long)((0.1 + _random.NextDouble() * 0.05) * Stopwatch.Frequency);
                    _pending.Enqueue((data, target), due);
                }
            }

            public void Dispose()
            {
                _running = false;
                Assert.True(_thread.Join(3000));
                _socket.Dispose();
            }
        }
    }
}
