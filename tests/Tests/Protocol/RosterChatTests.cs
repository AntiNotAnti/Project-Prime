using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class RosterChatTests
    {
        [Fact]
        public void RosterCodecChecksEntireBodyBeforePublishingEntries()
        {
            var expected = new NetRosterEntry[8];
            for (byte slot = 0; slot < 8; slot++)
            {
                expected[slot] = new(slot, (ulong)slot + 1, (Hunter)slot, slot, "NAME" + slot, (ushort)(100 + slot));
            }
            byte[] bytes = new byte[SessionRosterPacket.MaxSize];
            Assert.Equal(bytes.Length, SessionRosterPacket.Write(bytes, UInt32.MaxValue, expected));
            var actual = new NetRosterEntry[8];
            Assert.True(SessionRosterPacket.TryRead(bytes, actual, out uint revision, out int count));
            Assert.Equal(UInt32.MaxValue, revision);
            Assert.Equal(8, count);
            Assert.Equal(expected, actual);
            for (int size = 0; size < bytes.Length; size++)
            {
                Assert.False(SessionRosterPacket.TryRead(bytes.AsSpan(0, size), actual, out _, out _));
            }
            var sentinel = new NetRosterEntry(7, 999, Hunter.Samus, 0, "KEEP");
            Array.Fill(actual, sentinel);
            bytes[SessionRosterPacket.HeaderSize + 7 * SessionRosterPacket.EntrySize + 26] = 1;
            Assert.False(SessionRosterPacket.TryRead(bytes, actual, out _, out _));
            Assert.All(actual, value => Assert.Equal(sentinel, value));
            SessionRosterPacket.Write(bytes, 0, expected);
            bytes[SessionRosterPacket.HeaderSize + SessionRosterPacket.EntrySize] = 0;
            Assert.False(SessionRosterPacket.TryRead(bytes, actual, out _, out _));
            SessionRosterPacket.Write(bytes, 0, expected);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(SessionRosterPacket.HeaderSize + SessionRosterPacket.EntrySize + 1), 1);
            Assert.False(SessionRosterPacket.TryRead(bytes, actual, out _, out _));
        }

        [Fact]
        public void ChatCodecPreservesAsciiNormalizationAndNinetySixCharacterLimit()
        {
            byte[] bytes = new byte[SessionChatPacket.Size];
            var packet = new SessionChatPacket(123, 7, "ALPHA", "é\n" + new string('x', 100));
            packet.Write(bytes);
            Assert.True(SessionChatPacket.TryRead(bytes, out var decoded));
            Assert.Equal(96, decoded.Text.Length);
            Assert.StartsWith("??", decoded.Text);
            Assert.Equal(123ul, decoded.ConnectionId);
            for (int size = 0; size < bytes.Length; size++)
            {
                Assert.False(SessionChatPacket.TryRead(bytes.AsSpan(0, size), out _));
            }
            bytes[^1] = 0xFF;
            Assert.False(SessionChatPacket.TryRead(bytes, out _));
            packet.Write(bytes);
            bytes[8] = 8;
            Assert.False(SessionChatPacket.TryRead(bytes, out _));
        }

        [Fact]
        public void ServerAttributesChatLimitsBurstAndPublishesRosterIdentityChanges()
        {
            using var transport = new NetTransport(0);
            using var firstSocket = new NetTransport(0);
            using var secondSocket = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var first = new NetClient(firstSocket, endpoint, "ALPHA", Hunter.Samus);
            using var second = new NetClient(secondSocket, endpoint, "BRAVO", Hunter.Kanden);
            NetClient[] clients = { first, second };
            Pump(server, clients, () => first.Roster.Length == 2 && second.Roster.Length == 2);
            Assert.Equal("ALPHA", second.Roster[0].Name);
            Assert.Equal(first.Connection!.Id, second.Roster[0].ConnectionId);
            Assert.Equal(Hunter.Kanden, first.Roster[1].Hunter);
            Pump(server, clients, () => first.Roster[0].PingMs > 0 && first.Roster[1].PingMs > 0);
            Assert.InRange(first.Roster[0].PingMs, 1, 500);
            ulong previous = second.Connection!.Id;
            second.Reconnect();
            Pump(server, clients, () => second.Connection != null && second.Roster.Length == 2
                && first.Roster[1].ConnectionId == second.Connection.Id);
            Assert.NotEqual(previous, first.Roster[1].ConnectionId);
            ServerPeer speaker = server.Find(first.Connection.Id)!;
            long rejected = server.Rejected;
            byte[] malformed = new byte[ReliableEventPacket.HeaderSize + 4 + SessionChatRequest.Size];
            Span<byte> badBody = stackalloc byte[4 + SessionChatRequest.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(badBody, 1);
            SessionChatRequest.Write(badBody[4..], "hello");
            badBody[5] = 1;
            ReliableEventPacket.Write(malformed, 1000, ReliableEventType.ChatRequest, badBody);
            first.Connection.Send(firstSocket, NetMessageType.Event, malformed);
            Pump(server, clients, () => server.Rejected > rejected);
            for (int i = 0; i < 20; i++) { Assert.True(first.SendChat("line " + i)); }
            Pump(server, clients, () => first.Connection.Reliable.PendingCount == 0 && speaker.ChatDropped == 17);
            // Wait for the last server publications to arrive and ACK.
            Pump(server, clients, () => AllAcknowledged(server));
            foreach (NetClient client in clients)
            {
                int lines = 0;
                while (client.TryDequeueEvent(out var item))
                {
                    Assert.Equal(ReliableEventType.Chat, item.Type);
                    Assert.True(SessionChatPacket.TryRead(item.Payload.Span, out var chat));
                    Assert.Equal("ALPHA", chat.Name);
                    Assert.Equal(first.Accepted.Slot, chat.Slot);
                    Assert.Equal(first.Connection.Id, chat.ConnectionId);
                    lines++;
                }
                Assert.Equal(3, lines);
            }
            second.Disconnect();
            Pump(server, clients, () => server.Count == 1 && first.Roster.Length == 1);
            Assert.Equal(first.Connection.Id, first.Roster[0].ConnectionId);
        }

        private static bool AllAcknowledged(ServerNetwork server)
        {
            foreach (ServerPeer? peer in server.Peers)
            {
                if (peer != null && peer.Connection.Reliable.PendingCount != 0) { return false; }
            }
            return true;
        }

        private static void Pump(ServerNetwork server, NetClient[] clients, Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 4)
            {
                server.Poll((uint)(timer.Elapsed.TotalSeconds * 60));
                foreach (NetClient client in clients) { client.Poll(); }
                if (done()) { return; }
                Thread.Sleep(2);
            }
            Assert.Fail("Roster/chat condition timed out.");
        }
    }
}
