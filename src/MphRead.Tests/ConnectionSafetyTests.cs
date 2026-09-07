using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ConnectionSafetyTests
    {
        [Fact]
        public void MalformedAndClientAuthoredStateCannotPoisonAConnectionOrReachOtherPeers()
        {
            using var serverSocket = new NetTransport(0);
            using var senderSocket = new NetTransport(0);
            using var viewerSocket = new NetTransport(0);
            using var raw = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var endpoint = new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort);
            var server = new ServerNetwork(serverSocket, "MP1 SANCTORUS", GameMode.Battle);
            // This test injects commands directly rather than running the owner
            // simulation, so establish the phase facts that production would
            // publish before accepting input.
            server.Phase = MatchPhase.Playing;
            server.PhaseRevision = 1;
            using var sender = new NetClient(senderSocket, endpoint, "SENDER", Hunter.Samus);
            using var viewer = new NetClient(viewerSocket, endpoint, "VIEWER", Hunter.Kanden);
            NetClient[] clients = { sender, viewer };
            uint tick = 0;
            void Poll()
            {
                server.Poll(tick++);
                foreach (ServerPeer? peer in server.Peers)
                {
                    if (peer?.Connection.State == NetConnectionState.Ready) peer.Connection.StartPlaying();
                    if (peer?.Connection.State == NetConnectionState.Playing) peer.Inputs.Take(tick);
                }
                foreach (NetClient client in clients)
                {
                    client.Poll();
                    if (client.State == NetConnectionState.Loading) client.Ready(client.Accepted.MatchId);
                }
            }
            Wait(() => server.Count == 2 && Array.TrueForAll(clients, c => c.Connection != null
                && server.Find(c.Connection.Id)?.Connection.State == NetConnectionState.Playing), Poll);
            long rejected = server.Rejected;
            // A roster or keepalive arriving before Accepted is rejected
            // during admission, then reliable state is retried. The attack
            // must not deliver any rejected packet to the established viewer.
            long viewerRejected = viewer.Rejected;
            foreach (int length in new[] { 1, NetHeader.Size, NetConfig.MaxPacketSize, NetConfig.MaxPacketSize + 1, 4096 })
            {
                byte[] malformed = new byte[length];
                raw.Send(malformed, endpoint);
            }
            var forged = new SnapshotPlayer[]
            {
                State(sender, new Vector3(999, 999, 999))
            };
            forged[0].Health = forged[0].AmmoUa = UInt16.MaxValue;
            byte[] packet = new byte[NetConfig.MaxPacketSize];
            var snapshot = new SnapshotPacket(tick, 0, server.MatchId, 0, false, 0, 0);
            int bytes = snapshot.Write(packet.AsSpan(NetHeader.Size), forged);
            new NetHeader(NetMessageType.Snapshot, NetHeaderFlags.None, sender.Connection!.Id,
                0x70000000, 0, 0).Write(packet);
            senderSocket.SendDatagram(endpoint, packet.AsSpan(0, NetHeader.Size + bytes));
            InputCommand[] command = { new(123, 123, tick, InputButtons.Jump, InputButtons.Jump, -Vector3.UnitZ, InputCommand.NoWeapon) };
            bytes = InputBundle.Write(packet.AsSpan(NetHeader.Size), server.MatchId, command, server.PhaseRevision);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(NetHeader.Size + InputBundle.HeaderSize + 20), Single.NaN);
            new NetHeader(NetMessageType.Input, NetHeaderFlags.None, sender.Connection.Id,
                0x70000001, 0, 0).Write(packet);
            senderSocket.SendDatagram(endpoint, packet.AsSpan(0, NetHeader.Size + bytes));
            Wait(() => server.Rejected >= rejected + 5 && serverSocket.Metrics.PacketsRejected >= 2, Poll);
            Assert.False(viewer.HasSnapshot);
            Assert.True(sender.SendInputs(command, server.PhaseRevision));
            Wait(() => server.Find(sender.Connection.Id)!.Inputs.HasProcessed, Poll);
            Assert.Equal(123u, server.Find(sender.Connection.Id)!.Inputs.LastProcessed);

            // Only the server can publish state. A valid lower-sequence packet
            // still works after the rejected high-sequence malicious packets.
            SnapshotPlayer[] authoritative = { State(sender, Vector3.Zero), State(viewer, Vector3.UnitX) };
            bytes = snapshot.Write(packet, authoritative);
            server.Find(viewer.Connection!.Id)!.Connection.Send(serverSocket, NetMessageType.Snapshot, packet.AsSpan(0, bytes));
            Wait(() => viewer.HasSnapshot, Poll);
            Assert.Equal(2, viewer.SnapshotPlayers.Length);
            Assert.Equal(Vector3.Zero, viewer.SnapshotPlayers[0].Position);
            Assert.Equal(99, viewer.SnapshotPlayers[0].Health);
            Assert.Equal(viewerRejected, viewer.Rejected);
            Assert.Equal(2, server.Count);
            Assert.Null(sender.Failure);
            Assert.Null(viewer.Failure);
        }

        [Fact]
        public void TransportCountsAuthoritativeDatagramsAndRejectsOversizeWithoutThrowing()
        {
            using var receiver = new NetTransport(0);
            using var sender = new NetTransport(0);
            var target = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
            sender.SendDatagram(target, new byte[NetConfig.MaxPacketSize + 1]);
            Assert.Equal(1, sender.Metrics.PacketsRejected);
            Assert.Equal(0, sender.Metrics.PacketsSent);
            Span<byte> bytes = stackalloc byte[NetHeader.Size];
            new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced, 1, 0, 0, 0).Write(bytes);
            sender.SendDatagram(target, bytes);
            Assert.True(SpinWait.SpinUntil(() => receiver.Metrics.PacketsReceived == 1, 3000));
            Assert.Equal(1, sender.Metrics.PacketsSent);
            Assert.Equal(NetHeader.Size, sender.Metrics.BytesSent);
            Assert.Equal(NetHeader.Size, receiver.Metrics.BytesReceived);
        }

        private static SnapshotPlayer State(NetClient client, Vector3 position) => new()
        {
            Slot = client.Accepted.Slot, ConnectionId = client.Connection!.Id,
            Hunter = Hunter.Samus, Health = 99, Position = position, Aim = -Vector3.UnitZ,
            Facing = -Vector3.UnitZ, AvailableWeapons = 1, Life = 1,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
        };

        private static void Wait(Func<bool> complete, Action poll)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 5)
            {
                poll();
                if (complete()) return;
                Thread.Sleep(1);
            }
            Assert.Fail("Authoritative safety check timed out.");
        }
    }
}
