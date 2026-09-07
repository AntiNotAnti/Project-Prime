using System;
using System.Buffers.Binary;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    [CollectionDefinition("Demo global state", DisableParallelization = true)]
    public sealed class DemoGlobalStateCollection { }

    [Collection("Demo global state")]
    public sealed class DemoPlaybackTests
    {
        [Fact]
        public void LegacyPlaybackIgnoresConnectionControlAndRewindsOpeningSnapshot()
        {
            string path = TemporaryFile();
            try
            {
                using (var writer = new DemoWriter(path, 4))
                {
                    writer.WriteRecord(0, new byte[] { 12 }); // Historical authority opcode must be inert.
                    writer.WriteRecord(0, new byte[] { (byte)PacketType.Welcome, 7 });
                    var match = new MatchStatePacket { Mode = (byte)GameMode.Battle, RoomKey = "MP1 SANCTORUS", NextRoomKey = "", MatchId = 1 };
                    byte[] bytes = new byte[1 + MatchStatePacket.Size]; bytes[0] = (byte)PacketType.MatchState; match.Write(bytes.AsSpan(1));
                    writer.WriteRecord(0, bytes);
                    var snapshot = new SnapshotHeader { Frame = 20, Rng1 = 1, Rng2 = 2, PlayerCount = 0 };
                    bytes = new byte[1 + SnapshotHeader.Size]; bytes[0] = (byte)PacketType.Snapshot; snapshot.Write(bytes.AsSpan(1));
                    writer.WriteRecord(0, bytes);
                }
                Assert.True(DemoPlayback.Join(path));
                Assert.True(NetSession.Active);
                Assert.False(DemoPlayback.IsModern);
                Assert.Equal(-1, NetSession.LocalSlot);
                Assert.Null(NetSession.TrafficMetrics);
                Assert.Equal(0, NetSession.SnapshotsReceived);
                DemoPlayback.PumpFrame(); NetSession.Update(0);
                Assert.Equal(1, NetSession.SnapshotsReceived);
                Assert.Equal(20u, NetSession.LastSnapshotFrame);
                Assert.Equal(-1, NetSession.LocalSlot);
            }
            finally { DemoPlayback.Stop(); NetSession.Stop(); File.Delete(path); }
        }

        [Theory]
        [InlineData(5)]
        [InlineData(6)]
        public void AuthoritativeFilesPreserveOpeningStateRosterWorldAndFrameGaps(byte protocol)
        {
            string path = TemporaryFile();
            try
            {
                byte[] match = Match(5);
                byte[] snapshot = Snapshot(5, uint.MaxValue, 9);
                var roster = new NetRosterEntry[] { new(7, 99, Hunter.Sylux, 7, "Seven") };
                byte[] rosterBody = new byte[4 + SessionRosterPacket.MaxSize];
                BinaryPrimitives.WriteUInt32LittleEndian(rosterBody, 5);
                int rosterSize = SessionRosterPacket.Write(rosterBody.AsSpan(4), 1, roster);
                byte[] rosterRecord = Record(DemoRecordKind.Roster, rosterBody.AsSpan(0, rosterSize + 4));
                byte[] world = World(5);
                using (var writer = new DemoWriter(path, protocol))
                {
                    writer.WriteRecord(0, match); writer.WriteRecord(0, rosterRecord);
                    writer.WriteRecord(0, snapshot); writer.WriteRecord(0, world);
                    writer.WriteRecord(400, Snapshot(5, 0, 12));
                }
                using (DemoReader reader = DemoReader.Open(path)!)
                {
                    Assert.Equal(protocol, reader.ProtocolVersion);
                    var state = new ModernDemoState();
                    DemoRecord record = reader.ReadNext()!.Value;
                    Assert.Equal(0u, record.Frame); Assert.True(state.Receive(record.Data));
                    for (int i = 0; i < 3; i++) { Assert.True(state.Receive(reader.ReadNext()!.Value.Data)); }
                    Assert.True(state.World.HasState); Assert.Equal(17, state.World.Count);
                    Assert.Equal(7, state.Players[0].Slot); Assert.Equal(9, state.Players[0].Points);
                    Assert.Equal(31, state.Players[0].AmmoUa);
                    Assert.Equal("Seven", GameState.Nicknames[7]);
                    record = reader.ReadNext()!.Value; Assert.Equal(400u, record.Frame);
                    Assert.True(state.Receive(record.Data)); Assert.Equal(12, state.Players[0].Points);
                    Assert.Null(reader.ReadNext());
                }
                Assert.True(DemoPlayback.Join(path)); Assert.True(DemoPlayback.IsModern);
                Assert.Null(AuthoritativePlay.Current); Assert.Null(NetSession.TrafficMetrics);
                Assert.Equal(-1, NetSession.LocalSlot); Assert.Equal("MP1 SANCTORUS", NetSession.ServerMatch?.RoomKey);
                Assert.False(DemoPlayback.AtEnd); Assert.Equal(0u, NetSession.NetFrame);
            }
            finally { DemoPlayback.Stop(); NetSession.Stop(); File.Delete(path); }
        }

        [Fact]
        public void RelayProtocolFiveRecordsAreNotMistakenForAuthoritativeDemos()
        {
            string path = TemporaryFile();
            try
            {
                using (var writer = new DemoWriter(path, 5))
                {
                    var match = new MatchStatePacket { Mode = (byte)GameMode.Battle,
                        RoomKey = "MP1 SANCTORUS", NextRoomKey = "", MatchId = 1 };
                    byte[] data = new byte[1 + MatchStatePacket.Size];
                    data[0] = (byte)PacketType.MatchState;
                    match.Write(data.AsSpan(1));
                    writer.WriteRecord(0, data);
                }
                Assert.False(DemoPlayback.Join(path));
                Assert.False(NetSession.Active);
                Assert.Null(AuthoritativePlay.Current);
                Assert.Contains("no match info", DemoPlayback.LastError);
            }
            finally { DemoPlayback.Stop(); File.Delete(path); }
        }

        [Fact]
        public void ModernMalformedAndStaleMatchRecordsCannotReplaceState()
        {
            var state = new ModernDemoState();
            Assert.True(state.Receive(Match(5)));
            byte[] good = Snapshot(5, 12, 9); Assert.True(state.Receive(good));
            for (int length = 0; length < good.Length; length++) { Assert.False(state.Receive(good.AsSpan(0, length))); }
            Assert.False(state.Receive(Snapshot(6, 20, 88)));
            Assert.True(state.Receive(Snapshot(5, 11, 88))); // Valid reordered record is inert.
            Assert.Equal(9, state.Players[0].Points);
            Assert.True(state.Receive(Match(6))); Assert.False(state.HasSnapshot); Assert.False(state.World.HasState);
            Assert.False(state.Receive(good));
        }

        [Fact]
        public void ModernCombatAndChatValidateWholeRecordsBeforePlayback()
        {
            var state = new ModernDemoState(); Assert.True(state.Receive(Match(5)));
            Span<byte> body = stackalloc byte[5 + CombatEventBatch.MaxSize];
            BinaryPrimitives.WriteUInt32LittleEndian(body, 5); body[4] = (byte)ReliableEventType.Combat;
            var hit = new CombatEvent(1, 20, 0, CombatEventKind.Damage, 0, 0,
                new CombatActor(0, 12, 1), new CombatActor(7, 99, 1), 80, 20, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
            int length = CombatEventBatch.Write(body[5..], new[] { hit });
            byte[] data = Record(DemoRecordKind.Event, body[..(length + 5)]);
            Assert.True(state.Receive(data));
            data[^1] = 255; data[6 + 1 + 12] = 255;
            Assert.False(state.Receive(data));
            state.DiscardEvents();
            byte[] chatBody = new byte[5 + SessionChatPacket.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(chatBody, 5); chatBody[4] = (byte)ReliableEventType.Chat;
            new SessionChatPacket(99, 7, "Seven", "hello").Write(chatBody.AsSpan(5));
            Assert.True(state.Receive(Record(DemoRecordKind.Event, chatBody)));
            chatBody[0] = 6;
            Assert.False(state.Receive(Record(DemoRecordKind.Event, chatBody)));
        }

        [Fact]
        public void UnsupportedDemoVersionAndOversizedRecordsAreRefused()
        {
            string path = TemporaryFile();
            try
            {
                using (var writer = new DemoWriter(path, 99)) { writer.WriteRecord(0, Match(5)); }
                Assert.False(DemoPlayback.Join(path));
                Assert.Contains("Unsupported", DemoPlayback.LastError);
                Assert.False(DemoPlayback.IsActive);
                string other = TemporaryFile();
                try
                {
                    using var writer = new DemoWriter(other);
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteRecord(0, new byte[1025]));
                }
                finally { File.Delete(other); }
            }
            finally { DemoPlayback.Stop(); NetSession.Stop(); File.Delete(path); }
        }

        [Fact]
        public void LiveAcceptedFactsRecordAndReplayWithoutNetworkControl()
        {
            string path = TemporaryFile();
            using var transport = new NetTransport(0);
            using var socket = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, transport.LocalPort), "Recorded", Hunter.Samus);
            client.WorldPacketValidator = WorldPacket.TryValidate;
            client.WorldPacketReceived = DemoRecorder.RecordWorld;
            try
            {
                Pump(server, client, () => client.HasRoster);
                Assert.True(DemoRecorder.Start(path, client));
                var peer = server.Peers[client.Accepted.Slot]!;
                Assert.True(client.Ready(client.Accepted.MatchId));
                Pump(server, client, () => peer.Connection.State == NetConnectionState.Ready);
                peer.Connection.StartPlaying();
                var source = new SnapshotPlayer { Slot = client.Accepted.Slot, Hunter = Hunter.Samus, TeamIndex = 0,
                    ConnectionId = peer.Connection.Id, Life = 1, Aim = Vector3.UnitZ, Facing = Vector3.UnitZ,
                    AmmoUa = 23, Health = 80, Points = 7, AvailableWeapons = 1,
                    Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned };
                byte[] body = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
                new SnapshotPacket(20, 1, 1, 0, false, 1, 2).Write(body, new[] { source });
                peer.Connection.Send(transport, NetMessageType.Snapshot, body);
                peer.Connection.Send(transport, NetMessageType.World, World(1).AsSpan(1));
                var hit = new CombatEvent(1, 20, 0, CombatEventKind.Damage, 0, 0,
                    new CombatActor(0, peer.Connection.Id, 1), new CombatActor(0, peer.Connection.Id, 1),
                    80, 20, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
                body = new byte[4 + 1 + CombatEvent.Size];
                BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
                CombatEventBatch.Write(body.AsSpan(4), new[] { hit });
                Assert.True(peer.Connection.Reliable.TryEnqueue(ReliableEventType.Combat, body, out _));
                bool receivedEvent = false;
                Pump(server, client, () =>
                {
                    while (client.TryDequeueEvent(out NetApplicationEvent value))
                    { DemoRecorder.RecordEvent(value); receivedEvent |= value.Type == ReliableEventType.Combat; }
                    return receivedEvent && client.HasSnapshot;
                });
                DemoRecorder.Stop();
                using DemoReader reader = DemoReader.Open(path)!;
                Assert.Equal(NetHeader.Version, reader.ProtocolVersion);
                Assert.True(DemoFile.IsAuthoritativeProtocol(reader.ProtocolVersion));
                var state = new ModernDemoState();
                var kinds = new HashSet<DemoRecordKind>();
                while (reader.ReadNext() is { } record)
                {
                    kinds.Add((DemoRecordKind)record.Data[0]);
                    Assert.True(state.Receive(record.Data));
                }
                Assert.Equal(5, kinds.Count);
                Assert.Equal(source.AmmoUa, state.Players[0].AmmoUa);
                Assert.Equal(source.Points, state.Players[0].Points);
                Assert.Equal(source.ConnectionId, state.Players[0].ConnectionId);
                Assert.True(state.World.HasState);
            }
            finally { DemoRecorder.Stop(); NetSession.Stop(); File.Delete(path); }
        }

        private static void Pump(ServerNetwork server, NetClient client, Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                server.Poll(20); client.Poll(); DemoRecorder.RecordFrame(client);
                if (done()) { return; }
                Thread.Sleep(2);
            } while (timer.Elapsed.TotalSeconds < 4);
            Assert.Fail("Demo recording fixture timed out.");
        }

        private static string TemporaryFile() => Path.Combine(Path.GetTempPath(), $"fruity-demo-test-{Guid.NewGuid():N}.fpdemo");
        private static byte[] Record(DemoRecordKind kind, ReadOnlySpan<byte> body)
        {
            var data = new byte[1 + body.Length]; data[0] = (byte)kind; body.CopyTo(data.AsSpan(1)); return data;
        }
        private static byte[] Match(uint match)
        {
            var body = new byte[MatchTransitionPacket.Size];
            new MatchTransitionPacket(match, 120, GameMode.Battle, "MP1 SANCTORUS").Write(body);
            return Record(DemoRecordKind.Match, body);
        }
        private static byte[] Snapshot(uint match, uint sequence, int points)
        {
            var player = new SnapshotPlayer { Slot = 7, Hunter = Hunter.Sylux, TeamIndex = 7, ConnectionId = 99,
                Life = 1, Aim = Vector3.UnitZ, Facing = Vector3.UnitZ, Points = points, AmmoUa = 31,
                AvailableWeapons = 1, Health = 100, Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned };
            var body = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
            new SnapshotPacket(120, sequence, match, 0, false, 1, 2).Write(body, new[] { player });
            return Record(DemoRecordKind.Snapshot, body);
        }
        private static byte[] World(uint match)
        {
            var records = new WorldRecord[17];
            records[0] = new WorldRecord(WorldRecordKind.Match, 255, 0, 0, new Vector3(600, 600, 0), 3, 0, 10, uint.MaxValue, 0);
            for (byte slot = 0; slot < 8; slot++)
            {
                records[1 + slot * 2] = new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[2 + slot * 2] = new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
            }
            var body = new byte[WorldPacket.HeaderSize + records.Length * WorldRecord.Size];
            WorldPacket.Write(body, match, 1, 120, records, 0);
            return Record(DemoRecordKind.World, body);
        }
    }
}
