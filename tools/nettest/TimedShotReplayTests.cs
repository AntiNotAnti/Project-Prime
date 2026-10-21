using System;
using System.IO;
using System.Buffers.Binary;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Explicit storage fixtures: these exercise archival value ownership and codecs.
// They do not establish native admission, projectile rendering or combat fidelity.
internal static class TimedShotReplayTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Timed shot replay: " + label); }

    private static TimedShotCertificate Certificate(uint shot = 1, int slot = 0,
        ushort life = 1, uint authority = 100, bool granted = false)
    {
        var key = new ShotKey(23, 7, slot, 2, life, shot);
        var fire = new FireEvent(shot, 90, 88, 17, FireEventKind.PressFire,
            (byte)BeamType.PowerBeam, 0, 0, FireEvent.FlagPose,
            new Vector3(1, 2, 3), Vector3.UnitX, Vector3.UnitX, Vector3.UnitX,
            SourceLife: life);
        var source = new ShotSourceEvidence(ShotProofReason.Verified, ShotProofReason.Verified,
            !granted, 0, 1, 8);
        TimedShotFunding? funding = granted ? new TimedShotFunding(19,
            new PowerUpSpawnKey(23, 7, 41, 5, 3), key with { ShotId = 0 },
            TimedPowerUp.DoubleDamage, 70, 65, 1870, 95) : null;
        return new(key, fire, 41, authority, TimedShotSource.AcceptedFire, source,
            new TimedGrantAnswer(granted ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted,
                granted ? 19ul : 0ul),
            granted ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted,
            funding, TimedShotUnavailable.None, 1,
            new TimedShotDamageTemplate(granted ? 12 : 6, 9, 2, Vector3.Zero,
                Vector3.UnitX, .25f, 1, 50, 0, false, 0, true));
    }
    private static ReplayTimedShotFact Fact(uint shot = 1, int slot = 0,
        ushort life = 1, uint authority = 100, bool granted = false)
        => ReplayTimedShotFact.From(Certificate(shot, slot, life, authority, granted), TimedGrantProofMode.Enforce);
    private static byte[] Bytes(ReplayTimedShotState state)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) state.Write(writer);
        return stream.ToArray();
    }
    private static ReplayTimedShotState Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var state = ReplayTimedShotState.Read(reader);
        Check(stream.Position == stream.Length, "state consumes exact serialized appendix");
        return state;
    }
    private static void Codec()
    {
        foreach (bool granted in new[] { false, true })
        {
            var original = Fact(granted: granted); var packet = original.Packet();
            Check(ReplayTimedShotFact.TryRead(packet, out var decoded) && decoded == original,
                "full tagged record preserves source, raw ledger, final knowledge, channels and full funding");
            Check(decoded.Channels.Body == (granted ? 12 : 6) && decoded.Channels.Present,
                "ordinary native channels remain actual values");
            for (int length = 0; length < packet.Length; length++)
                Check(!ReplayTimedShotFact.TryRead(packet.AsSpan(0, length), out _), "truncated record " + length);
            var extra = new byte[packet.Length + 1]; packet.CopyTo(extra, 0);
            Check(!ReplayTimedShotFact.TryRead(extra, out _), "trailing record bytes rejected");
            var wrongTag = (byte[])packet.Clone(); wrongTag[0] ^= 0xff;
            Check(!ReplayTimedShotFact.TryRead(wrongTag, out _), "file-only tag required");
        }
        // Fixed v1 byte contract: mutate decoded fields without asking Packet()
        // to serialize an invalid value. Every mutation begins from valid bytes.
        var valid = Fact(granted: true).Packet();
        foreach (var field in new[] { (2, "zero epoch"), (10, "zero match"), (13, "zero generation"),
            (15, "zero life"), (17, "zero shot"), (131, "zero funding sequence") })
        {
            var bad = (byte[])valid.Clone();
            int width = field.Item1 == 2 || field.Item1 == 131 ? 8 : field.Item1 == 17 ? 4 : 2;
            bad.AsSpan(field.Item1, width).Clear();
            Check(!ReplayTimedShotFact.TryRead(bad, out _), field.Item2);
        }
        foreach (var field in new[] { (12, "slot"), (103, "lane"), (104, "capture mode"),
            (105, "source reason"), (106, "pose reason"), (107, "hint"), (120, "raw ledger knowledge"),
            (129, "final knowledge"), (130, "funding flag"), (197, "unavailable reason"),
            (212, "balanced boolean"), (215, "present boolean") })
        {
            var bad = (byte[])valid.Clone(); bad[field.Item1] = 255;
            Check(!ReplayTimedShotFact.TryRead(bad, out _), "malformed " + field.Item2);
        }
        foreach (int channel in new[] { 200, 204, 208 })
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1f, float.MaxValue })
            {
                var bad = (byte[])valid.Clone(); BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(channel), value);
                Check(!ReplayTimedShotFact.TryRead(bad, out _), "invalid archived channel " + channel + "/" + value);
            }
        var unknown = Certificate() with { LedgerAnswer = new(TimedGrantKnowledge.Granted, 19),
            Knowledge = TimedGrantKnowledge.Unavailable, Unavailable = TimedShotUnavailable.Source };
        var raw = ReplayTimedShotFact.From(unknown, TimedGrantProofMode.Shadow);
        Check(ReplayTimedShotFact.TryRead(raw.Packet(), out var retained)
            && retained.LedgerAnswer.Knowledge == TimedGrantKnowledge.Granted
            && retained.Knowledge == TimedGrantKnowledge.Unavailable,
            "raw granted ledger answer survives final source refusal");
    }
    private static void Retention()
    {
        var state = new ReplayTimedShotState();
        Check(!state.Enabled, "new state has no timed certificate capability");
        Check(state.Begin(23, 7, 41, TimedGrantProofMode.Enforce) && state.Enabled, "explicit stream begins");
        var first = Fact();
        Check(state.Remember(first, 100) == TimedShotRetention.Stored, "first immutable value stored");
        Check(state.Remember(first, 101) == TimedShotRetention.Duplicate, "retry is duplicate");
        var later = ReplayTimedShotFact.From(Certificate() with {
            Knowledge = TimedGrantKnowledge.Unavailable, Unavailable = TimedShotUnavailable.Source }, TimedGrantProofMode.Shadow);
        Check(state.Remember(later, 101) == TimedShotRetention.Duplicate
            && state.TryGet(first.Key, first.Fire.SourceFrame, 41, 101, out var unchanged) && unchanged == first,
            "later evidence cannot rewrite same-fire immutable certificate");
        var changed = ReplayTimedShotFact.From(Certificate() with {
            Fire = first.Fire with { AckFrame = first.Fire.AckFrame + 1 } }, TimedGrantProofMode.Enforce);
        Check(state.Remember(changed, 101) == TimedShotRetention.Alias, "same logical key cannot alias timing");
        Check(state.TryGet(first.Key, first.Fire.SourceFrame, 41, 1124, out var found) && found == first,
            "inclusive TTL retains original value after alias");
        Check(!state.TryGet(first.Key, first.Fire.SourceFrame, 41, 1125, out _), "TTL expires next frame");
        Check(!state.TryGet(first.Key, first.Fire.SourceFrame + 1, 41, 100, out _), "source frame must match");
        Check(!state.TryGet(first.Key, first.Fire.SourceFrame, 42, 100, out _), "room must match");
        var oldLife = Fact(2, life: 2);
        Check(state.Remember(oldLife, 100) == TimedShotRetention.Stored
            && state.TryGet(first.Key, first.Fire.SourceFrame, 41, 100, out _), "new life preserves old flight");
        foreach (var key in new[] { first.Key with { AuthorityEpoch = 0 }, first.Key with { MatchId = 0 },
            first.Key with { ShooterSlot = 8 }, first.Key with { Generation = 0 },
            first.Key with { LifeId = 0 }, first.Key with { ShotId = 0 } })
            Check(state.Remember(first with { Key = key }, 100) == TimedShotRetention.Invalid,
                "invalid logical identity refused without mutation");
        var unknownState = new ReplayTimedShotState(); unknownState.Begin(23, 7, 0, TimedGrantProofMode.Shadow);
        var unknownCertificate = Certificate() with { Room = 0, Lane = TimedShotSource.NativeInput,
            LedgerAnswer = new(TimedGrantKnowledge.Unavailable), Knowledge = TimedGrantKnowledge.Unavailable,
            Unavailable = TimedShotUnavailable.ObservationMissing };
        var unknownFact = ReplayTimedShotFact.From(unknownCertificate, TimedGrantProofMode.Shadow);
        Check(unknownState.Remember(unknownFact, 100) == TimedShotRetention.Stored
            && unknownState.TryGet(unknownFact.Key, unknownFact.Fire.SourceFrame, 0, 100, out var unknownFound)
            && unknownFound.Channels.Body == 6 && unknownFound.Knowledge == TimedGrantKnowledge.Unavailable,
            "raw unknown room-zero native launch retains actual ordinary six");
        var bytes = Bytes(state); var copy = Read(bytes);
        Check(Bytes(copy).AsSpan().SequenceEqual(bytes) && copy.Count == state.Count,
            "state roundtrip reproduces exact independent value appendix");
        Check(copy.TryGet(first.Key, first.Fire.SourceFrame, 41, 100, out found) && found == first,
            "restored pending identity resolves without ReplayShotFact");
        var full = new ReplayTimedShotState(); full.Begin(23, 7, 41, TimedGrantProofMode.Enforce);
        for (int slot = 0; slot < 8; slot++)
            for (uint shot = 1; shot <= 256; shot++)
                Check(full.Remember(Fact(shot, slot), 100) == TimedShotRetention.Stored, "full eight-slot capacity");
        Check(full.Count == 2048 && full.Remember(Fact(257), 100) == TimedShotRetention.Capacity,
            "full live slot refuses overwrite");
        Check(full.Remember(Fact(257, authority: 1125), 1125) == TimedShotRetention.Stored,
            "expired cells become reusable");
        var rollover = new ReplayTimedShotState(); rollover.Begin(23, 7, 41, TimedGrantProofMode.Enforce);
        var wrap = Fact(authority: uint.MaxValue - 10);
        Check(rollover.Remember(wrap, 5) == TimedShotRetention.Stored
            && rollover.TryGet(wrap.Key, wrap.Fire.SourceFrame, 41, 5, out _), "unsigned authority rollover retains flight");
    }
    private static void Match(ReplayReplicaState state)
    {
        var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = "fixture-room",
            Mode = (byte)GameMode.Battle }.Write(packet.AsSpan(1));
        state.Accept(packet, 0);
    }
    private static void ScopeRecords()
    {
        var decoder = new ReplayReplicaState(); Match(decoder);
        var fact = ReplayTimedShotFact.From(Certificate(), TimedGrantProofMode.Shadow);
        long ignored = decoder.IgnoredPackets;
        decoder.Accept(fact.Packet(), 1);
        Check(!decoder.TimedShots.Enabled && decoder.TimedShots.Count == 0 && decoder.IgnoredPackets == ignored + 1,
            "fact without producer scope stays unknown and cannot fabricate timed capability");
        var scope = new ReplayTimedShotScope(23, 7, 41, TimedGrantProofMode.Shadow, 100);
        var packet = scope.Packet();
        Check(ReplayTimedShotScope.TryRead(packet, out var restoredScope) && restoredScope == scope,
            "file-only producer scope roundtrips exact");
        decoder.Accept(packet, 2); decoder.Accept(fact.Packet(), 3);
        Check(decoder.TimedShots.Enabled && decoder.TimedShots.Count == 1
            && decoder.TimedShots.TryGet(fact.Key, fact.Fire.SourceFrame, 41, 100, out var found) && found == fact,
            "matching producer scope admits independent pending fact");
        var foreign = new ReplayReplicaState(); Match(foreign);
        foreign.Accept(new ReplayTimedShotScope(24, 7, 41, TimedGrantProofMode.Shadow, 100).Packet(), 2);
        Check(!foreign.TimedShots.Enabled, "foreign authority scope cannot enable current match");
        for (int length = 0; length < packet.Length; length++)
            Check(!ReplayTimedShotScope.TryRead(packet.AsSpan(0, length), out _), "truncated scope " + length);
        var invalid = (byte[])packet.Clone(); invalid[16] = 255;
        Check(!ReplayTimedShotScope.TryRead(invalid, out _), "unknown scope capture mode rejected");
    }
    private static void Checkpoints()
    {
        var ordinary = new ReplayReplicaState();
        var baseline = ordinary.CaptureCheckpoint().Bytes.ToArray();
        Check(BinaryPrimitives.ReadUInt16LittleEndian(baseline.AsSpan(4)) == 16
            && baseline[6] == 85 && baseline[7] == 87,
            "current normal checkpoint declares v16/protocol85 with exact untimed features87");
        var producer = new ReplayReplicaState(); Match(producer);
        Check(producer.TimedShots.Begin(23, 7, 41, TimedGrantProofMode.Enforce), "checkpoint producer enables timed capture");
        var fact = Fact(granted: true);
        Check(producer.TimedShots.Remember(fact, 100) == TimedShotRetention.Stored, "checkpoint owns full fact");
        var timed = producer.CaptureCheckpoint().Bytes.ToArray();
        Check(BinaryPrimitives.ReadUInt16LittleEndian(timed.AsSpan(4)) == 16
            && timed[6] == 85 && timed[7] == 95,
            "current timed checkpoint declares v16/protocol85 with exact timed features95");
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(new(timed));
        Check(restored.CaptureCheckpoint().Bytes.SequenceEqual(timed)
            && restored.TimedShots.TryGet(fact.Key, fact.Fire.SourceFrame, 41, 100, out var found) && found == fact,
            "detached checkpoint retains independent pending timed fact");
        var malformed = timed.AsSpan(0, timed.Length - 1).ToArray(); bool refused = false;
        try { restored.RestoreCheckpoint(new(malformed)); }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { refused = true; }
        Check(refused && restored.CaptureCheckpoint().Bytes.SequenceEqual(timed),
            "malformed timed tail rolls back entire detached checkpoint");
        var oldTimed = ReplicaCheckpointCompatibilityTests.Protocol80Rows(timed);
        var oldBaseline = ReplicaCheckpointCompatibilityTests.Protocol80Rows(baseline);
        var timedLayout = ReplayReplicaState.InspectCheckpointLayout(new(oldTimed));
        var baselineLayout = ReplayReplicaState.InspectCheckpointLayout(new(oldBaseline));
        Check(timedLayout.Version == 13 && timedLayout.Protocol == 80 && oldTimed[7] == 15
            && timedLayout.HeaderBytes == 8 && timedLayout.PlayerBytes == 173 && timedLayout.IntentBytes == 1296
            && timed.Length - oldTimed.Length == 8 * (19 + 2 + 1),
            "published v13 fixture retains exact timed features15 and historical173/1296 actor widths");
        Check(baselineLayout.Version == 12 && baselineLayout.Protocol == 80 && oldBaseline[7] == 7
            && baselineLayout.HeaderBytes == 8 && baselineLayout.PlayerBytes == 173 && baselineLayout.IntentBytes == 1296
            && baseline.Length - oldBaseline.Length == 8 * (19 + 2 + 1),
            "published v12 fixture retains exact ordinary features7 and historical173/1296 actor widths");
        restored.RestoreCheckpoint(new(oldTimed));
        var recapturedTimed = restored.CaptureCheckpoint().Bytes.ToArray();
        Check(BinaryPrimitives.ReadUInt16LittleEndian(recapturedTimed.AsSpan(4)) == 16
            && recapturedTimed[6] == 85 && recapturedTimed[7] == 95
            && restored.TimedShots.Enabled && restored.TimedShots.Count == 1
            && restored.TimedShots.TryGet(fact.Key, fact.Fire.SourceFrame, 41, 100, out found) && found == fact,
            "published v13 restores its timed fact and recaptures current v16 semantics");
        restored.RestoreCheckpoint(new(oldBaseline));
        var recapturedBaseline = restored.CaptureCheckpoint().Bytes.ToArray();
        Check(!restored.TimedShots.Enabled && restored.TimedShots.Count == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(recapturedBaseline.AsSpan(4)) == 16
            && recapturedBaseline[6] == 85 && recapturedBaseline[7] == 87,
            "published v12 restore clears timed capability and recaptures current ordinary semantics");
    }
    private static ReplayMetadata Metadata(byte version, bool? indexed = null, bool timed = false)
        => new() { FormatVersion = version, ProtocolVersion = 56, TickRate = 60,
            RecordedAtUtc = new DateTime(2026, 10, 13, 12, 34, 56, DateTimeKind.Utc),
            Type = ReplayType.Clip, Recovered = true, MapHash = 0x123456789abcdef0,
            Mode = GameMode.Battle, BuildVersion = "fixture-build", BuildId = "fixture-id", RoomKey = "fixture-room",
            Players = new[] { new ReplayPlayerInfo(2, (byte)Hunter.Samus, -1, "fixture-player", true, 2) },
            OriginRecordingFrame = version >= 4 ? 123u : 0u, LeadInFrames = version >= 4 ? 7u : 0u,
            WorldCheckpoint = version >= 4 ? new byte[] { 9, 8, 7 } : Array.Empty<byte>(),
            IndexedFireContinuation = indexed, TimedShotCertificates = timed };

    // Published v3/v4/v5 metadata contract, deliberately independent of the
    // current encoder. Empty bootstrap avoids any engine or asset dependency.
    private static byte[] PublishedMetadata(byte version, bool indexed = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)60);
        writer.Write(new DateTime(2026, 10, 13, 12, 34, 56, DateTimeKind.Utc).Ticks);
        writer.Write((byte)ReplayType.Clip); writer.Write(true); writer.Write(0x123456789abcdef0ul);
        writer.Write((byte)GameMode.Battle);
        ReplayFormatV3.WriteString(writer, "fixture-build"); ReplayFormatV3.WriteString(writer, "fixture-id");
        ReplayFormatV3.WriteString(writer, "fixture-room");
        writer.Write((byte)1); writer.Write((byte)2); writer.Write((byte)Hunter.Samus); writer.Write((sbyte)-1);
        ReplayFormatV3.WriteString(writer, "fixture-player"); writer.Write(true); writer.Write((byte)2);
        writer.Write((byte)0);
        if (version >= 4) { writer.Write(123u); writer.Write(7u); writer.Write(3); writer.Write(new byte[] { 9, 8, 7 }); }
        if (version == 5) { writer.Write(0x31504650u); writer.Write(indexed); }
        return stream.ToArray();
    }
    private static void RejectMetadata(byte version, byte[] bytes, string label)
    {
        bool rejected = false;
        try { _ = ReplayFormatV3.DecodeMetadata(56, bytes, version); }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { rejected = true; }
        Check(rejected, label);
    }
    private static void MetadataCodec()
    {
        foreach (byte version in new byte[] { 3, 4, 5 })
            foreach (bool indexed in new[] { false, true })
            {
                var metadata = Metadata(version, version == 5 ? indexed : null);
                var bytes = ReplayFormatV3.EncodeMetadata(metadata);
                Check(bytes.AsSpan().SequenceEqual(PublishedMetadata(version, indexed)),
                    "published metadata v" + version + " exact bytes");
                var restored = ReplayFormatV3.DecodeMetadata(56, bytes, version);
                Check(!restored.TimedShotCertificates && restored.IndexedFireContinuation == metadata.IndexedFireContinuation
                    && ReplayFormatV3.EncodeMetadata(restored).AsSpan().SequenceEqual(bytes),
                    "old metadata has no timed capability and roundtrips exact");
                var tail = new byte[bytes.Length + 1]; bytes.CopyTo(tail, 0);
                RejectMetadata(version, tail, "old metadata refuses unexpected tail");
            }
        foreach (bool? indexed in new bool?[] { null, false, true })
        {
            var metadata = Metadata(6, indexed, timed: true);
            var bytes = ReplayFormatV3.EncodeMetadata(metadata);
            var restored = ReplayFormatV3.DecodeMetadata(56, bytes, 6);
            Check(restored.TimedShotCertificates && restored.IndexedFireContinuation == indexed
                && ReplayFormatV3.EncodeMetadata(restored).AsSpan().SequenceEqual(bytes),
                "v6 timed capability preserves optional indexed policy " + indexed);
            RejectMetadata(6, bytes.AsSpan(0, bytes.Length - 1).ToArray(), "v6 truncated capability tail");
            var tail = new byte[bytes.Length + 1]; bytes.CopyTo(tail, 0);
            RejectMetadata(6, tail, "v6 unexpected metadata tail");
            var unknown = (byte[])bytes.Clone(); unknown[unknown.Length - 1] = 255;
            RejectMetadata(6, unknown, "v6 unknown capability or policy value");
        }
        foreach (byte version in new byte[] { 3, 4, 5 })
        {
            bool refused = false;
            try { ReplayFormatV3.EncodeMetadata(Metadata(version, version == 5 ? true : null, timed: true)); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "old format cannot advertise timed certificates");
        }
    }
    private static void LiveIntentSeed()
    {
        var recorder = new ReplayRecorder { ProducesWorldCheckpoints = true };
        var match = SeedMatch(); match.RuleBits = (uint)MatchModifierFlags.ParkourMovement;
        recorder.AcceptMatch(match, 97);
        var roster = RosterPacket.Create();
        roster.MatchId = 7; roster.AuthorityEpoch = 23; roster.Revision = 1; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)Hunter.Samus;
        roster.Names[0] = "SEED";
        recorder.AcceptRoster(roster, 95);
        byte[] snapshot = new byte[1 + SnapshotHeader.Size + PlayerState.Size
            + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        snapshot[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 23, Frame = 1, PlayerCount = 1 }
            .Write(snapshot.AsSpan(1));
        new PlayerState { SlotIndex = 0, SlotGeneration = 1, LifeId = 1, Health = 99,
            Flags = PlayerState.FlagActive | PlayerState.FlagSpawned, Facing = Vector3.UnitZ,
            Crouched = true, Tucked = true }.Write(snapshot.AsSpan(1 + SnapshotHeader.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(1 + SnapshotHeader.Size
            + PlayerState.Size + NetMatchTimeSync.Size), 7);
        recorder.AcceptSnapshot(snapshot, 95, 1);
        recorder.AcceptIntent(0, new IntentPacket { MatchId = 7, AuthorityEpoch = 23,
            SlotGeneration = 1, LifeId = 1, Frame = 97,
            Buttons = IntentButtons.InPlayState | IntentButtons.Crouch | IntentButtons.CrouchedState | IntentButtons.TuckedState }, 97);
        var records = new System.Collections.Generic.List<ReplayTimelineRecord>();
        recorder.SeedWorld(record => { record.Retain(); records.Add(record); });
        try
        {
            var broken = new ReplayReplicaState();
            foreach (var record in records) if (!record.Payload.IsEmpty) broken.Accept(record.Payload, record.RecordingFrame);
            Check(broken.RecordingFrame == 95 && broken.MatchRecordingFrame == 97
                && broken.TryGetIntent(0, out var intent) && intent.Frame == 97,
                "actual cached seed reproduces newer match/intent followed by older baseline");
            bool refused = false;
            try { new ReplayReplicaState().RestoreCheckpoint(broken.CaptureCheckpoint()); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "backward intent frontier remains rejected by the strict decoder");
            var seeded = new ReplayReplicaState();
            ReplayLiveWorld.SeedBootstrap(seeded, records, 98);
            var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(seeded.CaptureCheckpoint());
            Check(restored.RecordingFrame == 98 && restored.MatchRecordingFrame == 97
                && restored.ServerTick == 1 && restored.IntentAge(0) == 1
                && restored.TryGetIntent(0, out var copy) && copy.Frame == 97
                && restored.TryGetPlayer(0, out var player) && player.Crouched && player.Tucked,
                "live seed repairs only recording frontier, preserving accepted life, tucked shape and authority/source clocks");
        }
        finally
        {
            foreach (var record in records) record.Release();
            recorder.Reset();
        }
    }

    private static MatchStatePacket SeedMatch() => new()
        { AuthorityEpoch = 23, MatchId = 7, RoomKey = "fixture-room", Mode = (byte)GameMode.Battle };
    private static void ConstructionSeeds()
    {
        var recorder = new ReplayRecorder(); recorder.AcceptMatch(SeedMatch(), 1000);
        recorder.EnableTimedShotCertificates(41, TimedGrantProofMode.Shadow, 1000);
        var broken = new ReplayReplicaState();
        recorder.SeedWorld(record => { if (!record.Payload.IsEmpty) broken.Accept(record.Payload, record.RecordingFrame); });
        Check(broken.MatchRecordingFrame == 1000 && broken.RecordingFrame == 0,
            "regression reproduces older presentation carrier after newer match in a real recorder seed");
        bool refused = false;
        try { new ReplayReplicaState().RestoreCheckpoint(broken.CaptureCheckpoint()); }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "malformed recording frontier is rejected rather than weakening checkpoint validation");
        var liveRecords = new System.Collections.Generic.List<ReplayTimelineRecord>();
        recorder.SeedWorld(record => { record.Retain(); liveRecords.Add(record); });
        try
        {
            var liveSeed = new ReplayReplicaState();
            ReplayLiveWorld.SeedBootstrap(liveSeed, liveRecords, 999);
            var liveCopy = new ReplayReplicaState(); liveCopy.RestoreCheckpoint(liveSeed.CaptureCheckpoint());
            Check(liveCopy.RecordingFrame == 1000 && liveCopy.MatchRecordingFrame == 1000
                && liveCopy.TimedShots.AuthorityFrame == 1000 && liveCopy.ServerTick == 0,
                "live bootstrap commits complete cached frontier without advancing authority time");
            ReplayLiveWorld.SeedBootstrap(liveSeed, Array.Empty<ReplayTimelineRecord>(), 1005);
            liveCopy.RestoreCheckpoint(liveSeed.CaptureCheckpoint());
            Check(liveCopy.RecordingFrame == 1005 && liveCopy.MatchRecordingFrame == 1000
                && liveCopy.TimedShots.AuthorityFrame == 1000 && liveCopy.ServerTick == 0,
                "quiet live bootstrap advances only recording time beyond older cached facts");
        }
        finally { foreach (var record in liveRecords) record.Release(); }
        var repaired = new ReplayReplicaState(); repaired.RestoreCheckpoint(recorder.CaptureTimedShotSeed(1000));
        Check(repaired.RecordingFrame == 1000 && repaired.MatchRecordingFrame == 1000
            && repaired.TimedShots.Enabled && repaired.TimedShots.AuthorityFrame == 1000,
            "actual seed helper commits monotonic recording frontier without rewriting authority clock");
        var atBootstrap = new ReplayReplicaState();
        atBootstrap.Accept(ReplayTimelineArchive.Construction(repaired), 0);
        var bootstrapCopy = new ReplayReplicaState(); bootstrapCopy.RestoreCheckpoint(atBootstrap.CaptureCheckpoint());
        Check(bootstrapCopy.RecordingFrame == 1000 && bootstrapCopy.MatchRecordingFrame == 1000
            && bootstrapCopy.TimedShots.AuthorityFrame == 1000,
            "frame-zero construction injection retains a complete valid seed and its original authority clock");
        recorder.Reset();

        var state = new ReplayReplicaState();
        var match = SeedMatch(); var matchBytes = new byte[1 + MatchStatePacket.Size];
        matchBytes[0] = (byte)PacketType.MatchState; match.Write(matchBytes.AsSpan(1)); state.Accept(matchBytes, 0);
        state.TimedShots.Begin(23, 7, 41, TimedGrantProofMode.Shadow);
        var entropy = new Random(0x21613);
        for (int slot = 0; slot < 8; slot++) for (uint shot = 1; shot <= 256; shot++)
        {
            var certificate = Certificate(shot, slot, authority: 1000);
            certificate = certificate with { Fire = certificate.Fire with {
                SourceFrame = (uint)entropy.NextInt64(0, uint.MaxValue), AckFrame = (uint)entropy.NextInt64(0, uint.MaxValue),
                Origin = new((float)(entropy.NextDouble() * 10000), (float)(entropy.NextDouble() * 10000), (float)(entropy.NextDouble() * 10000)),
                Aim = new((float)(entropy.NextDouble() * 10000), (float)(entropy.NextDouble() * 10000), (float)(entropy.NextDouble() * 10000)),
                Reticle = new((float)entropy.NextDouble(), (float)entropy.NextDouble()),
                Charge = (byte)entropy.Next(256) },
                Damage = certificate.Damage with { Body = (float)(entropy.NextDouble() * 5000),
                    Head = (float)(entropy.NextDouble() * 5000), Splash = (float)(entropy.NextDouble() * 5000) } };
            var fact = ReplayTimedShotFact.From(certificate, TimedGrantProofMode.Shadow);
            Check(state.TimedShots.Remember(fact, 1000) == TimedShotRetention.Stored,
                "high-entropy seed fills each real fixed cell without eviction");
        }
        state.Advance(1000);
        var checkpoint = state.CaptureCheckpoint();
        Check(checkpoint.Bytes.Length <= ReplayReplicaCheckpoint.MaximumBytes,
            "complete 2048 fact fixture remains within unchanged512KiB detached checkpoint limit");
        var construction = ReplayTimelineArchive.Construction(checkpoint);
        Check(construction.Length > ushort.MaxValue && construction.Length <= ReplayFormatV3.MaxTimedConstruction,
            "high-entropy2048 construction genuinely requires uint length beyond65535 and stays within separate v6 bound");
        var metadata = new ReplayMetadata { FormatVersion = 6, TimedShotCertificates = true, RoomKey = match.RoomKey,
            Mode = GameMode.Battle, Bootstrap = new() { Packets = new[] { construction } } };
        var encoded = ReplayFormatV3.EncodeMetadata(metadata);
        var decoded = ReplayFormatV3.DecodeMetadata(56, encoded, 6);
        Check(decoded.Bootstrap.Packets.Count == 1 && decoded.Bootstrap.Packets[0].AsSpan().SequenceEqual(construction),
            "actual metadata codec preserves larger construction with v6 uint lengths");
        var restored = new ReplayReplicaState(); restored.Accept(decoded.Bootstrap.Packets[0], 0);
        Check(restored.TimedShots.Count == 2048 && restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),
            "actual construction decoder retains exact full high-entropy private state");
        string path = Path.Combine(Path.GetTempPath(), "prime-v6-full-seed-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
        try
        {
            using (var writer = new ReplayWriterV3(path, metadata)) ReplayTimelineArchive.EndFrame(writer, 0);
            using var reader = DemoReader.Open(path) ?? throw new InvalidDataException("Large v6 seed did not reopen.");
            var reopened = new ReplayReplicaState(); reopened.Accept(reader.Metadata!.Bootstrap.Packets[0], 0);
            Check(reader.FormatVersion == 6 && reopened.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),
                "actual file writer/reader preserves all2048 facts and complete detached seed");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".part")) File.Delete(path + ".part"); }
        bool oldRefused = false;
        try { ReplayFormatV3.EncodeMetadata(new ReplayMetadata { FormatVersion = 5, IndexedFireContinuation = true,
            Bootstrap = new() { Packets = new[] { construction } } }); }
        catch (InvalidDataException) { oldRefused = true; }
        Check(oldRefused, "old published formats retain4096 bootstrap budget and ushort contract");
        bool nonConstructionRefused = false;
        var oversizedScope = new byte[NetConfig.MaxSnapshotSize + 1]; oversizedScope[0] = ReplayTimedShotScope.Tag;
        try { ReplayFormatV3.EncodeMetadata(new ReplayMetadata { FormatVersion = 6, TimedShotCertificates = true,
            Bootstrap = new() { Packets = new[] { oversizedScope } } }); }
        catch (InvalidDataException) { nonConstructionRefused = true; }
        Check(nonConstructionRefused, "larger v6 exception cannot expand ordinary or live packet budget");
    }
    internal static int Run()
    {
        _checks = 0;
        try { Codec(); Retention(); Checkpoints(); ScopeRecords(); MetadataCodec(); ConstructionSeeds(); LiveIntentSeed(); Console.WriteLine($"PASS: {_checks} pure timed-shot archival assertions. SCOPE: explicit storage fixtures; no native admission or visual fidelity claim."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine("Completed checks: " + _checks); return 1; }
    }
}
