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
        foreach (MatchDeathKind kind in Enum.GetValues<MatchDeathKind>())
        {
            var details = new MatchDeathDetails(kind, kind == MatchDeathKind.Environment ? -1 : 0, 7);
            require(MatchDeathDetails.TryDecode(details.Encode(), out var decoded) && decoded == details, "death classification " + kind);
        }
        require(MatchDeathDetails.Classify(false, false, true, true, true, true) == MatchDeathKind.Deathalt
            && MatchDeathDetails.Classify(false, false, false, true, true, true) == MatchDeathKind.Burn
            && MatchDeathDetails.Classify(false, false, false, false, false, true) == MatchDeathKind.Bomb
            && MatchDeathDetails.Classify(true, true, true, true, true, true) == MatchDeathKind.Environment,
            "death precedence agrees with legacy presentation");
        require(Replay.ReplayDirector.SemanticInterest(MatchSemanticEventType.PlayerKilled) == (70f, 34f, "recent kill", true)
            && Replay.ReplayDirector.SemanticInterest(MatchSemanticEventType.ObjectiveCaptured).Major
            && Replay.ReplayDirector.SemanticInterest(MatchSemanticEventType.WeaponFired).Actor == 0,
            "director consumes canonical combat/objective interest without weapon-shot noise");
        var delivery = new NetRetainedDelivery(4);
        var cursor = delivery.Join();
        byte[] source = { 1 };
        delivery.Append(PacketType.MatchSemanticEvent, source); source[0] = 99;
        require(delivery.Pump(cursor, _ => false) && cursor.Next == 1, "backpressure retains cursor");
        int accepted = 0;
        require(delivery.Pump(cursor, value => { require(value.Payload[0] == 1, "retained payload owns copy"); accepted++; return true; })
            && accepted == 1 && cursor.Next == 2, "retry delivers exactly once");
        require(delivery.Pump(cursor, _ => { accepted++; return true; }) && accepted == 1, "accepted facts not republished");
        var lateJoin = delivery.Join();
        require(delivery.Pump(lateJoin, _ => { accepted++; return true; }) && accepted == 1, "late join skips historical announcements");
        var slow = delivery.Join();
        for (int i = 0; i < 4; i++) delivery.Append(PacketType.MatchAward, new byte[] { (byte)i });
        require(delivery.Pump(lateJoin, _ => true, 2) && lateJoin.Next == 4, "bounded pump work");
        delivery.Append(PacketType.MatchAward, new byte[] { 4 });
        require(!delivery.Pump(slow, _ => throw new Exception("must fail before delivering suffix")), "history overrun fails closed");
        require(delivery.Pump(lateJoin, _ => true) && lateJoin.Next == 7, "slow peer does not block healthy peer");
        var fact = new MatchSemanticEventPacket(1, 2, 3, 4, MatchSemanticEventType.PlayerKilled,
            0, 1, 2, 1, 2, 3, 6, 1, 9, 10);
        byte[] bytes = new byte[MatchSemanticEventPacket.Size]; fact.Write(bytes);
        require(bytes.AsSpan().SequenceEqual(Convert.FromHexString("3701000200000000000000030000000400000004000100020001020003000600000001090000000A0000000000000000")), "semantic event golden bytes");
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
        var telemetry = MatchSemanticTelemetry.Convert(maximum);
        require(telemetry.AuthorityEpoch == ulong.MaxValue && telemetry.Id == uint.MaxValue
            && telemetry.Generation == ushort.MaxValue && telemetry.VictimGeneration == ushort.MaxValue
            && telemetry.Life == ushort.MaxValue && telemetry.VictimLife == ushort.MaxValue
            && telemetry.EntityId == int.MaxValue && telemetry.Value == int.MaxValue,
            "canonical telemetry preserves exact full-width identities and values");
        var awardTelemetry = MatchSemanticTelemetry.Convert(award);
        require(awardTelemetry.SourceEventId == award.SourceEventId && awardTelemetry.Id == award.AwardId
            && awardTelemetry.Result == (int)award.Kind, "canonical award telemetry preserves causal event identity");
        var aggregate = new Network.Telemetry.NetTelemetryAggregator();
        aggregate.Add(telemetry); aggregate.Add(awardTelemetry);
        aggregate.Add(telemetry with { Result = -1 });
        var header = new Network.Telemetry.TelemetryHeader(5, 35, "test", "test", "test", "test", "Battle", "test", 8);
        var summary = aggregate.Capture(header, 1, default);
        require(summary.SemanticEvents[(int)maximum.Type] == 1 && summary.MatchAwards[(int)award.Kind] == 1
            && summary.SemanticEvents.Sum() == 1, "canonical telemetry uses bounded taxonomy counters");
        aggregate.Add(telemetry);
        require(summary.SemanticEvents.Sum() == 1, "published semantic summary owns its counters");
        string json = System.Text.Json.JsonSerializer.Serialize(telemetry, Network.Telemetry.TelemetryJsonContext.Default.NetTelemetryEvent);
        require(json.Contains("\"authorityEpoch\":18446744073709551615"), "telemetry JSON retains ulong epoch exactly");
        maximum.Write(bad = new byte[MatchSemanticEventPacket.Size]);
        require(MatchSemanticEventPacket.TryRead(bad, out var maximumRead) && maximumRead == maximum, "semantic maximum-width boundary");
        bad = (byte[])bytes.Clone(); bad[34] = 128;
        require(!MatchSemanticEventPacket.TryRead(bad, out _), "semantic invalid flags range");
        require(ReplayIdentityCompatibility.Convert(bytes, 34).IsEmpty, "historical protocols cannot originate new semantic packets");
        var ordered = new MatchSemanticReceiver(); ordered.Begin(1, 2, requireBaseline: true);
        int shown = 0, shownAwards = 0;
        ordered.Accept(fact); ordered.Accept(award);
        ordered.DrainPresentation(_ => shown++, _ => shownAwards++);
        require(shown == 0 && shownAwards == 0, "no announcements before explicit baseline");
        var baseline = new MatchSemanticEventPacket(1, 2, 1, 0, MatchSemanticEventType.MatchStarted,
            255, 0, 0, 255, 0, 0, -1, 0, -1, 0, true, 0);
        byte[] baselineBytes = new byte[MatchSemanticEventPacket.Size]; baseline.Write(baselineBytes);
        require(MatchSemanticEventPacket.TryRead(baselineBytes, out var baselineRead) && baselineRead == baseline,
            "explicit frontier baseline round trips through generated wire");
        require(ordered.Accept(baseline) && !ordered.Accept(baseline), "baseline applies exactly once");
        ordered.DrainPresentation(_ => shown++, _ => shownAwards++);
        require(shown == 0, "reordered facts wait for missing predecessor");
        ordered.Accept(fact with { EventId = 2 });
        ordered.DrainPresentation(_ => shown++, _ => shownAwards++);
        require(shown == 2 && shownAwards == 1, "contiguous facts precede dependent authoritative awards");
        ordered.Accept(fact with { EventId = 1 });
        ordered.DrainPresentation(_ => shown++, _ => shownAwards++);
        require(shown == 2, "old baseline history never announces");
        require(!(baseline with { ActorSlot = 0 }).Validate(), "baseline cannot carry player identity");
        require(!(fact with { AwardFrontier = 1 }).Validate(), "ordinary fact cannot smuggle baseline frontier");
        var overrun = new MatchSemanticReceiver(); overrun.Begin(1, 2, requireBaseline: true);
        overrun.Accept(fact with { EventId = 1025 }); overrun.Accept(baseline with { EventId = 0 });
        require(overrun.PresentationOverrun, "baseline cannot silently skip a missing fact outside reorder window");
        var replayStart = new MatchSemanticReceiver(); replayStart.Begin(1, 2);
        replayStart.SuppressHistory(); // An initially empty restored world initializes once.
        replayStart.Accept(fact with { EventId = 1 });
        int firstLive = 0; replayStart.DrainPresentation(_ => firstLive++, _ => { });
        require(firstLive == 1, "first future fact after empty replay world is not mistaken for history");
        require(replayStart.RestorePresentationBaseline(baseline with { EventId = 99 }), "explicit replay history frontier");
        replayStart.SuppressHistory(); replayStart.Accept(fact with { EventId = 100 });
        replayStart.DrainPresentation(_ => firstLive++, _ => { });
        require(firstLive == 2, "first future fact follows explicit nonzero restored frontier");
        using (var emptyHistoryStorage = new MemoryStream())
        {
            var emptyReceiver = new MatchSemanticReceiver(); emptyReceiver.Begin(1, 2);
            emptyReceiver.RestorePresentationBaseline(baseline with { EventId = 99 });
            emptyReceiver.WriteCheckpoint(new BinaryWriter(emptyHistoryStorage)); emptyHistoryStorage.Position = 0;
            emptyReceiver = MatchSemanticReceiver.ReadCheckpoint(new BinaryReader(emptyHistoryStorage), 1, 2);
            emptyReceiver.Accept(fact with { EventId = 100 });
            int future = 0; emptyReceiver.DrainPresentation(_ => future++, _ => { });
            require(future == 1, "checkpoint preserves frontier even with empty semantic history");
        }
        var receiver = new MatchSemanticReceiver(); receiver.Begin(1, 2);
        require(receiver.Accept(fact) && receiver.Accept(fact with { EventId = 1 }) && receiver.Accept(fact with { EventId = 2 }), "reliable facts can reorder");
        require(!receiver.Accept(fact), "reliable fact duplicate rejected");
        require(!receiver.Accept(fact with { AuthorityEpoch = 3 }), "cross epoch rejected");
        require(receiver.Accept(award), "authority award accepted without local derivation");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        receiver.WriteCheckpoint(writer); stream.Position = 0;
        var restored = MatchSemanticReceiver.ReadCheckpoint(new BinaryReader(stream), 1, 2);
        int replayAnnouncements = 0;
        restored.DrainPresentation(_ => replayAnnouncements++, _ => replayAnnouncements++);
        require(replayAnnouncements == 0, "checkpoint restore never repeats historical announcements");
        require(restored.Events.SequenceEqual(receiver.Events) && restored.Awards.SequenceEqual(receiver.Awards), "semantic receiver checkpoint retains reorder history and awards");
        require(!restored.Accept(fact) && !restored.Accept(award), "semantic checkpoint preserves dedup");
        for (uint i = 4; i < 2200; i++) receiver.Accept(fact with { EventId = i });
        require(receiver.Events.Count == MatchSemanticReceiver.Window && !receiver.Accept(fact), "bounded receive history and stale rejection");
        stream.SetLength(0); receiver.WriteCheckpoint(writer); stream.Position = 0;
        restored = MatchSemanticReceiver.ReadCheckpoint(new BinaryReader(stream), 1, 2);
        replayAnnouncements = 0;
        restored.DrainPresentation(_ => replayAnnouncements++, _ => replayAnnouncements++);
        require(replayAnnouncements == 0, "checkpoint restore never repeats historical announcements");
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
        byte[] malformed = checkpoint.Bytes.ToArray(); malformed[^9] = 255;
        bool rejected = false;
        try { replica.RestoreCheckpoint(new(malformed)); } catch (InvalidDataException) { rejected = true; }
        require(rejected && replica.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes), "bad semantic checkpoint is atomic");
        // Version4 protocol34 ends before the semantic component and predates
        // protocol42's player/roster fields and larger fire-event records.
        var empty = new ReplayReplicaState(); empty.Accept(matchBytes, 0);
        byte[] schemaFive = empty.CaptureCheckpoint().Bytes[..^8].ToArray(); schemaFive[4] = 5;
        var oldFive = new ReplayReplicaState(); oldFive.RestoreCheckpoint(new(schemaFive));
        require(oldFive.SemanticEvents.Events.Count == 0, "schema 5 checkpoints remain readable without presentation frontier");
        var legacyBytes = empty.CaptureCheckpoint().Bytes[..^16].ToArray().ToList();
        const int matchStart = 46;
        int rosterStart = matchStart + MatchStatePacket.Size + 1;
        int firstPlayer = rosterStart + RosterPacket.Size;
        int stride = 7 + PlayerState.Size + 1 + IntentPacket.FullSize + 4;
        for (int slot = Entities.PlayerEntity.SlotCapacity - 1; slot >= 0; slot--)
        {
            legacyBytes.RemoveRange(firstPlayer + slot * stride + 7 + PlayerState.Size + 1 + IntentPacket.Protocol38FullSize,
                IntentPacket.FullSize - IntentPacket.Protocol38FullSize);
            legacyBytes.RemoveRange(firstPlayer + slot * stride + 7 + PlayerState.Protocol41Size,
                PlayerState.Size - PlayerState.Protocol41Size);
        }
        for (int slot = RosterPacket.MaxSlots - 1; slot >= 0; slot--)
            legacyBytes.RemoveAt(rosterStart + RosterPacket.HeaderSize + slot * RosterPacket.EntrySize + RosterPacket.EntrySize - 1);
        byte[] legacy = legacyBytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(4), 4); legacy[6] = 34;
        replica.RestoreCheckpoint(new(legacy));
        require(replica.Match?.MatchId == 1 && replica.SemanticEvents.Events.Count == 0, "protocol34 version4 checkpoint remains readable");
    }
}
