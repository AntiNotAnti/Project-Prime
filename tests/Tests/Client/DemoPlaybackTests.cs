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
        [Theory]
        [InlineData(8, false)]
        [InlineData(8, true)]
        [InlineData(9, false)]
        [InlineData(9, true)]
        public void JoinRoutingRevisionPreservesAuthoritativeReplayFacts(byte protocol, bool indexed)
        {
            string path = TemporaryFile();
            try
            {
                using (var writer = new DemoWriter(path, protocol, indexed))
                {
                    writer.WriteRecord(0, Match(5));
                    writer.WriteRecord(0, Snapshot(5, 7, 12));
                }
                using var reader = DemoReader.Open(path)!;
                Assert.Equal(indexed ? DemoFile.IndexedFormatVersion : DemoFile.FormatVersion, reader.FormatVersion);
                Assert.Equal(protocol, reader.ProtocolVersion);
                Assert.True(DemoFile.IsAuthoritativeProtocol(protocol));
                var state = new ModernDemoState(); state.Reset(protocol);
                while (reader.ReadNext() is { } record) Assert.True(state.Receive(record.Data));
                Assert.Equal(12, state.Players[0].Points);
                Assert.False(DemoFile.IsSupportedProtocol(10));
            }
            finally { File.Delete(path); }
        }

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
                byte[] match = Match(5, legacy: true);
                byte[] snapshot = HistoricalSnapshot(5, uint.MaxValue, 9);
                var roster = new NetRosterEntry[] { new(7, 99, Hunter.Sylux, 7, "Seven") };
                byte[] rosterBody = new byte[4 + SessionRosterPacket.MaxSize];
                BinaryPrimitives.WriteUInt32LittleEndian(rosterBody, 5);
                int rosterSize = Protocol7DemoRoster.Write(rosterBody.AsSpan(4), 1, roster);
                byte[] rosterRecord = Record(DemoRecordKind.Roster, rosterBody.AsSpan(0, rosterSize + 4));
                // Protocol 5/6 authoritative recordings retain the legacy
                // seventeen-record layout; protocol 7 and later carry the
                // lifecycle record.
                byte[] world = World(5, legacy: true);
                using (var writer = new DemoWriter(path, protocol))
                {
                    writer.WriteRecord(0, match); writer.WriteRecord(0, rosterRecord);
                    writer.WriteRecord(0, snapshot); writer.WriteRecord(0, world);
                    writer.WriteRecord(400, HistoricalSnapshot(5, 0, 12));
                }
                using (DemoReader reader = DemoReader.Open(path)!)
                {
                    Assert.Equal(protocol, reader.ProtocolVersion);
                    var state = new ModernDemoState();
                    state.Reset(protocol);
                    DemoRecord record = reader.ReadNext()!.Value;
                    Assert.Equal(0u, record.Frame); Assert.True(state.Receive(record.Data));
                    for (int i = 0; i < 3; i++) { Assert.True(state.Receive(reader.ReadNext()!.Value.Data)); }
                    Assert.True(state.World.HasState); Assert.Equal(17, state.World.Count);
                    Assert.Equal(7, state.Players[0].Slot); Assert.Equal(9, state.Players[0].Points);
                    Assert.Equal(31, state.Players[0].AmmoUa);
                    Assert.Equal(7, state.Roster[0].Slot);
                    Assert.Equal("Seven", state.Roster[0].Name);
                    using var scene = new Scene();
                    using var unrelated = new Scene();
                    string unrelatedName = unrelated.Roster.Nicknames[7];
                    state.ApplyRoster(scene);
                    Assert.Equal("Seven", scene.Roster.Nicknames[7]);
                    Assert.Equal(unrelatedName, unrelated.Roster.Nicknames[7]);
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
        public void ModernKillRecordsValidateMatchLengthAndBoundedBuffer()
        {
            var state = new ModernDemoState();
            Assert.True(state.Receive(Match(5)));
            byte[] body = new byte[5 + KillEvent.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(body, 5);
            body[4] = (byte)ReliableEventType.Kill;
            var kill = new KillEvent(1, 20, 5, 1, new CombatActor(0, 12, 1), new CombatActor(7, 99, 1),
                0, 0, System.Collections.Immutable.ImmutableArray<CombatActor>.Empty);
            kill.Write(body.AsSpan(5));
            byte[] record = Record(DemoRecordKind.Event, body);
            for (int length = 0; length < record.Length; length++) Assert.False(state.Receive(record.AsSpan(0, length)));
            for (int i = 0; i < 256; i++) Assert.True(state.Receive(record));
            Assert.False(state.Receive(record));
            state.DiscardEvents();
            Assert.True(state.Receive(record));
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5 + 8), 6);
            Assert.False(state.Receive(Record(DemoRecordKind.Event, body)));
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
                foreach (byte[] world in LiveWorld(1, terminal: true))
                    peer.Connection.Send(transport, NetMessageType.World, world.AsSpan(1));
                var hit = new CombatEvent(1, 20, 0, CombatEventKind.Damage, 0, 0,
                    new CombatActor(0, peer.Connection.Id, 1), new CombatActor(0, peer.Connection.Id, 1),
                    80, 20, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
                body = new byte[4 + 1 + CombatEvent.Size];
                BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
                CombatEventBatch.Write(body.AsSpan(4), new[] { hit });
                Assert.True(peer.Connection.Reliable.TryEnqueue(ReliableEventType.Combat, body, out _));
                var kill = new KillEvent(2, 20, 1, 1, hit.Actor, new CombatActor(1, 999, 1), 4,
                    KillEventFlags.Headshot, System.Collections.Immutable.ImmutableArray<CombatActor>.Empty);
                body = new byte[4 + KillEvent.Size]; BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
                kill.Write(body.AsSpan(4));
                Assert.True(peer.Connection.Reliable.TryEnqueue(ReliableEventType.Kill, body, out _));
                var objective = new WorldEvent(3, 20, 1, 1, WorldSubjectKind.Node, WorldSignalKind.NodeCaptured,
                    0, 123, hit.Actor, new Vector3(1, 2, 3));
                body = new byte[4 + WorldEvent.Size]; BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
                objective.Write(body.AsSpan(4));
                Assert.True(peer.Connection.Reliable.TryEnqueue(ReliableEventType.WorldEvent, body, out _));
                MatchEvent nodeCaptured = new(4, 20, 1, 1, MatchEventKind.NodeCaptured,
                    hit.Actor, CombatActor.None, EntityId: 123, Team: 0);
                MatchEvent matchEnded = new(5, 20, 1, 1, MatchEventKind.MatchEnded,
                    CombatActor.None, CombatActor.None, Team: 0);
                foreach (MatchEvent semantic in new[] { nodeCaptured, matchEnded })
                {
                    MatchSemanticEventPacket packet = MatchSemanticEventPacketConversion.FromEvent(semantic);
                    body = new byte[4 + MatchSemanticEventPacket.Size];
                    BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
                    packet.Write(body.AsSpan(4));
                    Assert.True(peer.Connection.Reliable.TryEnqueue(
                        ReliableEventType.MatchSemantic, body, out _));
                }
                var receivedTypes = new HashSet<ReliableEventType>();
                var receivedSemantics = new HashSet<MatchEventKind>();
                Pump(server, client, () =>
                {
                    while (client.TryDequeueEvent(out NetApplicationEvent value))
                    {
                        DemoRecorder.RecordEvent(value);
                        receivedTypes.Add(value.Type);
                        if (value.Type == ReliableEventType.MatchSemantic
                            && MatchSemanticEventPacket.TryRead(value.Payload.Span,
                                out MatchSemanticEventPacket packet)
                            && MatchSemanticEventPacketConversion.TryToEvent(packet,
                                out MatchEvent semantic))
                            receivedSemantics.Add(semantic.Kind);
                    }
                    return receivedTypes.Contains(ReliableEventType.Combat) && receivedTypes.Contains(ReliableEventType.Kill)
                        && receivedTypes.Contains(ReliableEventType.WorldEvent)
                        && receivedSemantics.Contains(MatchEventKind.NodeCaptured)
                        && receivedSemantics.Contains(MatchEventKind.MatchEnded)
                        && client.HasSnapshot;
                });
                DemoRecorder.Stop();
                using DemoReader reader = DemoReader.Open(path)!;
                Assert.Equal(NetHeader.Version, reader.ProtocolVersion);
                Assert.True(DemoFile.IsAuthoritativeProtocol(reader.ProtocolVersion));
                var state = new ModernDemoState();
                var kinds = new HashSet<DemoRecordKind>();
                bool sawKill = false, sawObjective = false;
                var replayedSemantics = new HashSet<MatchEventKind>();
                while (reader.ReadNext() is { } record)
                {
                    kinds.Add((DemoRecordKind)record.Data[0]);
                    Assert.True(state.Receive(record.Data));
                    if ((DemoRecordKind)record.Data[0] == DemoRecordKind.Event && record.Data[5] == (byte)ReliableEventType.Kill)
                    {
                        Assert.True(KillEvent.TryRead(record.Data.AsSpan(6), out KillEvent restored));
                        Assert.Equal(kill.Id, restored.Id); Assert.Equal(kill.Killer, restored.Killer);
                        Assert.Equal(kill.Flags, restored.Flags); Assert.Equal(kill.Victim, restored.Victim);
                        sawKill = true;
                    }
                    if ((DemoRecordKind)record.Data[0] == DemoRecordKind.Event && record.Data[5] == (byte)ReliableEventType.WorldEvent)
                    {
                        Assert.True(WorldEvent.TryRead(record.Data.AsSpan(6), out WorldEvent restored));
                        Assert.Equal(objective, restored); sawObjective = true;
                    }
                    if ((DemoRecordKind)record.Data[0] == DemoRecordKind.Event
                        && record.Data[5] == (byte)ReliableEventType.MatchSemantic)
                    {
                        Assert.True(MatchSemanticEventPacket.TryRead(record.Data.AsSpan(6),
                            out MatchSemanticEventPacket packet));
                        Assert.True(MatchSemanticEventPacketConversion.TryToEvent(packet,
                            out MatchEvent semantic));
                        replayedSemantics.Add(semantic.Kind);
                    }
                }
                Assert.Contains(DemoRecordKind.Match, kinds); Assert.Contains(DemoRecordKind.Roster, kinds);
                Assert.Contains(DemoRecordKind.Snapshot, kinds); Assert.Contains(DemoRecordKind.World, kinds);
                Assert.True(sawKill); Assert.True(sawObjective);
                Assert.Contains(MatchEventKind.NodeCaptured, replayedSemantics);
                Assert.Contains(MatchEventKind.MatchEnded, replayedSemantics);
                Assert.Contains(reader.Index, entry => entry.Marker == (ReplayMarker.Kill | ReplayMarker.Headshot));
                Assert.Single(reader.Index, entry => entry.Marker == ReplayMarker.NodeCapture);
                Assert.Single(reader.Index, entry => entry.Marker == ReplayMarker.MatchEnd);
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
        internal static byte[] Record(DemoRecordKind kind, ReadOnlySpan<byte> body)
        {
            var data = new byte[1 + body.Length]; data[0] = (byte)kind; body.CopyTo(data.AsSpan(1)); return data;
        }
        internal static byte[] Match(uint match, bool legacy = false)
        {
            if (legacy)
            {
                var legacyBody = new byte[9 + MatchStatePacket.MaxNameBytes];
                BinaryPrimitives.WriteUInt32LittleEndian(legacyBody, match);
                BinaryPrimitives.WriteUInt32LittleEndian(legacyBody.AsSpan(4), 120);
                legacyBody[8] = (byte)GameMode.Battle;
                NetText.Write(legacyBody.AsSpan(9), "MP1 SANCTORUS");
                return Record(DemoRecordKind.Match, legacyBody);
            }
            var body = new byte[MatchTransitionPacket.Size];
            new MatchTransitionPacket(match, 120, GameMode.Battle, "MP1 SANCTORUS").Write(body);
            return Record(DemoRecordKind.Match, body);
        }
        private static byte[] HistoricalSnapshot(uint match, uint sequence, int points)
        {
            var player = new Protocol7SnapshotPlayer { Slot = 7, Hunter = Hunter.Sylux, TeamIndex = 7, ConnectionId = 99,
                Life = 1, Aim = Vector3.UnitZ, Facing = Vector3.UnitZ, Points = points, AmmoUa = 31,
                AvailableWeapons = 1, Health = 100,
                Flags = Protocol7SnapshotPlayerFlags.Active | Protocol7SnapshotPlayerFlags.Spawned };
            var body = new byte[Protocol7SnapshotPacket.HeaderSize + Protocol7SnapshotPlayer.Size];
            new Protocol7SnapshotPacket(120, sequence, match, 0, false, 1, 2).Write(body, new[] { player });
            return Record(DemoRecordKind.Snapshot, body);
        }
        internal static byte[] Snapshot(uint match, uint sequence, int points)
        {
            var player = new SnapshotPlayer { Slot = 7, Hunter = Hunter.Sylux, TeamIndex = 7, ConnectionId = 99,
                Life = 1, Aim = Vector3.UnitZ, Facing = Vector3.UnitZ, Points = points, AmmoUa = 31,
                AvailableWeapons = 1, Health = 100, Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned };
            var body = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
            new SnapshotPacket(120, sequence, match, 0, false, 1, 2).Write(body, new[] { player });
            return Record(DemoRecordKind.Snapshot, body);
        }
        private static byte[] World(uint match, bool legacy = false)
        {
            var records = new WorldRecord[legacy ? 17 : 18];
            records[0] = new WorldRecord(WorldRecordKind.Match, 255, 0, 0, new Vector3(600, 600, 0), 3,
                legacy ? 0u : (uint)MatchPhase.Playing, 10, uint.MaxValue, 0);
            for (byte slot = 0; slot < 8; slot++)
            {
                records[1 + slot * 2] = new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[2 + slot * 2] = new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
            }
            if (!legacy)
            {
                records[17] = new WorldRecord(WorldRecordKind.Lifecycle, 255, 0, 0,
                    Vector3.Zero, 0, 0, 1, 0, 0);
            }
            var body = new byte[WorldPacket.HeaderSize + records.Length * WorldRecord.Size];
            WorldPacket.Write(body, match, 1, 120, records, 0);
            return Record(DemoRecordKind.World, body);
        }

        internal static IEnumerable<byte[]> LiveWorld(uint match, bool terminal = false)
        {
            var records = new WorldRecord[WorldPacket.CanonicalRecordCount];
            records[0] = new WorldRecord(WorldRecordKind.Match, 255, 0, 0, new Vector3(600, 600, 0), 3,
                (uint)(terminal ? MatchPhase.Ending : MatchPhase.Playing), 10, uint.MaxValue,
                terminal ? 1u : 0u);
            for (byte slot = 0; slot < 8; slot++)
            {
                records[1 + slot * 2] = new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[2 + slot * 2] = new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
            }
            records[17] = new WorldRecord(WorldRecordKind.Lifecycle, 255, 0, 0, Vector3.Zero, 0, 0, 1, 0, 0);
            for (byte slot = 0; slot < 8; slot++)
            {
                int index = 18 + slot * 5;
                records[index] = new(WorldRecordKind.CombatStats, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[index + 1] = new(WorldRecordKind.ObjectiveStats, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[index + 2] = new(WorldRecordKind.WeaponStats0, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[index + 3] = new(WorldRecordKind.WeaponStats1, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[index + 4] = new(WorldRecordKind.PlayerIdentity, slot, 0, 0, Vector3.Zero,
                    (uint)Hunter.Samus, slot < 2 ? slot : uint.MaxValue, slot < 2 ? 1u : 0u,
                    terminal ? (uint)slot << 16 : 0, 0)
                { PlayerName = $"P{slot}" };
            }
            byte[] body = new byte[WorldPacket.MaxSize];
            for (int offset = 0; offset < records.Length; offset += WorldPacket.RecordsPerBatch)
            {
                int length = WorldPacket.Write(body, match, 1, 120, records, offset);
                yield return Record(DemoRecordKind.World, body.AsSpan(0, length));
            }
        }
    }
}
