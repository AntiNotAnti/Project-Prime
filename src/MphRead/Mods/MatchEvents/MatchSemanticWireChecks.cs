using System;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using MphRead.Mods.Network;
namespace MphRead.Mods.MatchEvents;
internal static class MatchSemanticWireChecks
{
    internal static void Run(Action<bool, string> require)
    {
        var fact = new MatchSemanticEventPacket(1, 2, 3, 4, MatchSemanticEventType.PlayerKilled,
            0, 1, 2, 1, 2, 3, 6, 1, 9, 10);
        byte[] bytes = new byte[MatchSemanticEventPacket.Size]; fact.Write(bytes);
        require(bytes.AsSpan().SequenceEqual(Convert.FromHexString("3701000200000000000000030000000400000004000100020001020003000600000001090000000A000000")), "semantic event golden bytes");
        require(MatchSemanticEventPacket.TryRead(bytes, out var parsed) && parsed == fact, "semantic round trip");
        for (int i = 0; i < bytes.Length; i++) require(!MatchSemanticEventPacket.TryRead(bytes.AsSpan(0, i), out _), "semantic truncation " + i);
        require(!MatchSemanticEventPacket.TryRead(bytes.Concat(new byte[1]).ToArray(), out _), "semantic trailing bytes");
        byte[] bad = (byte[])bytes.Clone(); bad[19] = 255;
        require(!MatchSemanticEventPacket.TryRead(bad, out _), "semantic enum rejected");
        bad = (byte[])bytes.Clone(); bad[21] = bad[22] = 0;
        require(!MatchSemanticEventPacket.TryRead(bad, out _), "semantic impossible actor identity rejected by generated decoder");
        require(!(fact with { AuthorityEpoch = 0 }).Validate(), "zero semantic epoch rejected");
        require((fact with { AuthorityEpoch = ulong.MaxValue }).Validate(), "full width epoch preserved");
        var award = new MatchAwardPacket(1, 2, 1, 3, 4, 0, 1, 2, MatchAwardKind.FirstBlood);
        byte[] awardBytes = new byte[MatchAwardPacket.Size]; award.Write(awardBytes);
        require(awardBytes.AsSpan().SequenceEqual(Convert.FromHexString("3801000200000000000000010000000300000004000000000100020000")), "award golden bytes");
        require(MatchAwardPacket.TryRead(awardBytes, out var awardRead) && awardRead == award, "award round trip");
        for (int i = 0; i < awardBytes.Length; i++) require(!MatchAwardPacket.TryRead(awardBytes.AsSpan(0, i), out _), "award truncation " + i);
        require(!MatchAwardPacket.TryRead(awardBytes.Concat(new byte[1]).ToArray(), out _), "award trailing bytes");
        bad = (byte[])awardBytes.Clone(); bad[^1] = 255;
        require(!MatchAwardPacket.TryRead(bad, out _), "award invalid enum");
        var maximum = fact with { MatchId = ushort.MaxValue, AuthorityEpoch = ulong.MaxValue, EventId = uint.MaxValue,
            Tick = uint.MaxValue, ActorGeneration = ushort.MaxValue, ActorLife = ushort.MaxValue,
            TargetGeneration = ushort.MaxValue, TargetLife = ushort.MaxValue, EntityId = int.MaxValue, Value = int.MaxValue };
        maximum.Write(bad = new byte[MatchSemanticEventPacket.Size]);
        require(MatchSemanticEventPacket.TryRead(bad, out var maximumRead) && maximumRead == maximum, "semantic maximum-width boundary");
        bad = (byte[])bytes.Clone(); bad[34] = 128;
        require(!MatchSemanticEventPacket.TryRead(bad, out _), "semantic invalid flags range");
        require(ReplayIdentityCompatibility.Convert(bytes, 34).IsEmpty, "historical protocols cannot originate new semantic packets");
        var receiver = new MatchSemanticReceiver(); receiver.Begin(1, 2);
        require(receiver.Accept(fact) && receiver.Accept(fact with { EventId = 1 }) && receiver.Accept(fact with { EventId = 2 }), "reliable facts can reorder");
        require(!receiver.Accept(fact), "reliable fact duplicate rejected");
        require(!receiver.Accept(fact with { AuthorityEpoch = 3 }), "cross epoch rejected");
        require(receiver.Accept(award), "authority award accepted without local derivation");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        receiver.WriteCheckpoint(writer); stream.Position = 0;
        var restored = MatchSemanticReceiver.ReadCheckpoint(new BinaryReader(stream), 1, 2);
        require(restored.Events.SequenceEqual(receiver.Events) && restored.Awards.SequenceEqual(receiver.Awards), "semantic receiver checkpoint retains reorder history and awards");
        require(!restored.Accept(fact) && !restored.Accept(award), "semantic checkpoint preserves dedup");
        for (uint i = 4; i < 2200; i++) receiver.Accept(fact with { EventId = i });
        require(receiver.Events.Count == MatchSemanticReceiver.Window && !receiver.Accept(fact), "bounded receive history and stale rejection");
        stream.SetLength(0); receiver.WriteCheckpoint(writer); stream.Position = 0;
        restored = MatchSemanticReceiver.ReadCheckpoint(new BinaryReader(stream), 1, 2);
        require(restored.Events.SequenceEqual(receiver.Events) && !restored.Accept(fact), "window eviction checkpoint restoration");
        require(NetReliableChannel.IsReliable(PacketType.MatchSemanticEvent) && !NetReliableChannel.IsCritical(PacketType.MatchSemanticEvent)
            && NetPacketQueue.Priority(PacketType.MatchSemanticEvent) == NetPacketPriority.Background, "semantic transport uses ordinary bounded background budget");
        var decoder = new ReplayReplicaState();
        var match = new MatchStatePacket { MatchId = 1, AuthorityEpoch = 2, RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.Battle };
        byte[] matchBytes = new byte[1 + MatchStatePacket.Size]; matchBytes[0] = (byte)PacketType.MatchState; match.Write(matchBytes.AsSpan(1));
        decoder.Accept(matchBytes, 0); decoder.Accept(bytes, 4); decoder.Accept(awardBytes, 4);
        var checkpoint = decoder.CaptureCheckpoint();
        var replica = new ReplayReplicaState(); replica.RestoreCheckpoint(checkpoint);
        require(replica.SemanticEvents.Events.Single() == fact && replica.SemanticEvents.Awards.Single() == award, "replica checkpoint restores semantic facts and awards");
        require(replica.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes), "semantic decoder checkpoint exact roundtrip");
        byte[] malformed = checkpoint.Bytes.ToArray(); malformed[^1] = 255;
        bool rejected = false;
        try { replica.RestoreCheckpoint(new(malformed)); } catch (InvalidDataException) { rejected = true; }
        require(rejected && replica.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes), "bad semantic checkpoint is atomic");
        // Version4 protocol34 ends before the new semantic component. Existing layout is unchanged.
        var empty = new ReplayReplicaState(); empty.Accept(matchBytes, 0);
        byte[] legacy = empty.CaptureCheckpoint().Bytes[..^8].ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(4), 4); legacy[6] = 34;
        replica.RestoreCheckpoint(new(legacy));
        require(replica.Match?.MatchId == 1 && replica.SemanticEvents.Events.Count == 0, "protocol34 version4 checkpoint remains readable");
    }
}
