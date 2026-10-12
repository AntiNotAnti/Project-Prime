using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class TurretFireOutcomeTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Turret outcome: " + label); }
    private static TurretFireOutcome Emitted(uint source = 100, uint authority = 130)
    {
        var key = new ShotKey(9, 2, 3, 4, 5, 6);
        var fire = new FireEvent(key.ShotId, source, 90, 0, FireEventKind.TurretFire,
            (byte)BeamType.Battlehammer, 0, 0, FireEvent.FlagPose, new Vector3(1, 2, 3),
            Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, default, key.LifeId);
        return new(key, authority, TurretFireOutcomeStatus.Emitted, TurretFireOutcomeReason.None,
            fire, new Vector3(4, 5, 6), Vector3.UnitX);
    }
    private static byte[] Encode(TurretFireOutcome outcome)
    { var bytes = new byte[TurretFireOutcome.Size]; outcome.Write(bytes); return bytes; }
    private static void RejectObject(TurretFireOutcome outcome, string label)
    {
        bool rejected = false;
        try { Encode(outcome); } catch (ArgumentException) { rejected = true; }
        Check(rejected, label);
    }

    internal static int Run()
    {
        _checks = 0;
        var original = Emitted(); var bytes = Encode(original);
        Check(TurretFireOutcome.Size == 123 && bytes.Length == 123, "exact payload budget");
        Check(123 + 24 + 4 == 151 && 151 < 1472, "reliable envelope plus submessage fits datagram MTU");
        Check(TurretFireOutcome.TryRead(bytes, out var read) && read == original, "emitted roundtrip");
        Check(read.Fire.SourceFrame == 100 && read.AuthorityFrame == 130,
            "owner source time is independent of authority outcome time");
        foreach (var reason in new[] { TurretFireOutcomeReason.RecoveryExpired, TurretFireOutcomeReason.OwnerRetired,
            TurretFireOutcomeReason.TurretDestroyed, TurretFireOutcomeReason.Capacity })
        {
            var refusal = original with { Status = TurretFireOutcomeStatus.Refused, Reason = reason,
                EmissionOrigin = Vector3.Zero, EmissionDirection = Vector3.Zero };
            Check(TurretFireOutcome.TryRead(Encode(refusal), out read) && read == refusal, "refused roundtrip " + reason);
        }
        for (int size = 0; size < TurretFireOutcome.Size; size++)
            Check(!TurretFireOutcome.TryRead(bytes.AsSpan(0, size), out _), "truncation " + size);
        Check(!TurretFireOutcome.TryRead(new byte[TurretFireOutcome.Size + 1], out _), "trailing byte refused");
        foreach (var clock in new[] { (uint.MaxValue, 0u), (0u, uint.MaxValue), (0u, 0u) })
        {
            var value = Emitted(clock.Item1, clock.Item2);
            Check(TurretFireOutcome.TryRead(Encode(value), out read) && read == value,
                "source/authority clocks retain wrap values without cross-clock age assumptions");
        }
        foreach (var key in new[] { original.Key with { AuthorityEpoch = 0 }, original.Key with { MatchId = 0 },
            original.Key with { ShooterSlot = -1 }, original.Key with { ShooterSlot = 8 },
            original.Key with { Generation = 0 }, original.Key with { LifeId = 0 }, original.Key with { ShotId = 0 } })
            RejectObject(original with { Key = key }, "invalid full identity");
        foreach (var fire in new[] { original.Fire with { ShotId = 7 }, original.Fire with { SourceLife = 6 },
            original.Fire with { Kind = FireEventKind.AutomaticFire }, original.Fire with { Weapon = (byte)BeamType.PowerBeam },
            original.Fire with { Charge = 1 }, original.Fire with { PoseFlags = 0 },
            original.Fire with { Direction = Vector3.Zero }, original.Fire with { Aim = Vector3.Zero } })
            RejectObject(original with { Fire = fire }, "invalid source fire");
        foreach (var value in new[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity })
        {
            RejectObject(original with { EmissionOrigin = new Vector3(value, 0, 0) }, "nonfinite native origin");
            RejectObject(original with { EmissionDirection = new Vector3(value, 0, 1) }, "nonfinite native direction");
            RejectObject(original with { Fire = original.Fire with { Origin = new Vector3(value, 0, 0) } }, "nonfinite source pose");
            foreach (int offset in new[] { 46, 99, 111 })
            {
                var malformed = (byte[])bytes.Clone();
                BinaryPrimitives.WriteSingleLittleEndian(malformed.AsSpan(offset), value);
                Check(!TurretFireOutcome.TryRead(malformed, out var rejected) && rejected == default,
                    "nonfinite wire pose rejected transactionally");
            }
        }
        RejectObject(original with { EmissionOrigin = new Vector3(100000, 0, 0) }, "origin bound");
        RejectObject(original with { EmissionDirection = Vector3.Zero }, "zero native direction");
        RejectObject(original with { EmissionDirection = new Vector3(2, 0, 0) }, "nonunit native direction");
        RejectObject(original with { Status = 0 }, "unknown status");
        RejectObject(original with { Reason = TurretFireOutcomeReason.Capacity }, "emission cannot carry refusal reason");
        RejectObject(original with { Status = TurretFireOutcomeStatus.Refused }, "refusal requires reason and zero emission vectors");
        RejectObject(original with { Status = TurretFireOutcomeStatus.Refused, Reason = (TurretFireOutcomeReason)5,
            EmissionOrigin = Vector3.Zero, EmissionDirection = Vector3.Zero }, "unknown refusal reason");
        foreach (int offset in new[] { 0, 8, 10, 11, 13, 15, 23, 24 })
        {
            var malformed = (byte[])bytes.Clone(); malformed[offset] = 0;
            if (offset == 10) malformed[offset] = 8;
            if (offset == 24) malformed[offset] = 255;
            Check(!TurretFireOutcome.TryRead(malformed, out _), "malformed wire identity/status " + offset);
        }
        foreach (var change in new[] { (25, (byte)7), (92, (byte)6),
            (38, (byte)FireEventKind.AutomaticFire), (39, (byte)BeamType.PowerBeam),
            (40, (byte)1), (45, (byte)0) })
        {
            var malformed = (byte[])bytes.Clone(); malformed[change.Item1] = change.Item2;
            Check(!TurretFireOutcome.TryRead(malformed, out _), "wire source fire/key contract mismatch");
        }
        BatchingAndDelivery(); FilteringPreservesIntent(); ReplayRecordingAndRestore();
        Console.WriteLine($"PASS: turret outcome {_checks} assertions; codec, bounded retained delivery and carrier filtering.");
        return 0;
    }

    private static void BatchingAndDelivery()
    {
        Check(NetReliableChannel.IsReliable(PacketType.TurretFireOutcome)
            && !NetReliableChannel.IsCritical(PacketType.TurretFireOutcome),
            "turret outcomes use reliable ordinary capacity without critical reserve");
        Check(NetFactBatch.Stride(PacketType.TurretFireOutcome) == TurretFireOutcome.Size,
            "batch stride is the complete identity-bearing outcome");
        int rowsPerBatch = NetFactBatch.MaximumPayload / TurretFireOutcome.Size;
        Check(rowsPerBatch * TurretFireOutcome.Size + NetHeader.Size + 4 <= 1200
            && (rowsPerBatch + 1) * TurretFireOutcome.Size + NetHeader.Size + 4 > 1200,
            "largest outcome batch fits the conservative full reliable datagram budget");
        var delivery = new NetRetainedDelivery(rowsPerBatch + 2); var cursor = delivery.Join();
        var expected = new List<TurretFireOutcome>();
        for (int i = 0; i <= rowsPerBatch; i++)
        {
            var value = Emitted();
            var key = value.Key with { ShooterSlot = i % 8, Generation = (ushort)(i + 1),
                LifeId = (ushort)(i + 2), ShotId = (uint)(i + 100) };
            value = value with { Key = key, Fire = value.Fire with { ShotId = key.ShotId, SourceLife = key.LifeId } };
            expected.Add(value); var payload = Encode(value);
            delivery.Append(PacketType.TurretFireOutcome, payload);
            payload[0] = 0; // retained history must own its immutable accepted copy
        }
        ulong initial = cursor.Next; int refusedSize = 0;
        Check(delivery.PumpBatched(cursor, (type, payload) =>
            { refusedSize = payload.Length; return false; }) && cursor.Next == initial
            && refusedSize == rowsPerBatch * TurretFireOutcome.Size,
            "failed transport admission keeps the entire batch pending at the original cursor");
        var observed = new List<TurretFireOutcome>(); var batchSizes = new List<int>();
        Check(delivery.PumpBatched(cursor, (type, payload) =>
        {
            Check(type == PacketType.TurretFireOutcome && NetFactBatch.Validate(type, payload),
                "retry delivers an entirely valid retained outcome batch");
            batchSizes.Add(payload.Length);
            for (int at = 0; at < payload.Length; at += TurretFireOutcome.Size)
            {
                Check(TurretFireOutcome.TryRead(payload.Slice(at, TurretFireOutcome.Size), out var outcome),
                    "each admitted row decodes");
                observed.Add(outcome);
            }
            return true;
        }), "retry admits pending outcomes");
        Check(batchSizes.Count == 2 && batchSizes[0] == refusedSize && batchSizes[1] == TurretFireOutcome.Size
            && cursor.Next == initial + (ulong)expected.Count, "maximum batch and remainder advance only admitted rows");
        Check(observed.Count == expected.Count, "no outcome lost or duplicated during retry");
        for (int i = 0; i < expected.Count; i++)
            Check(observed[i] == expected[i], "retained order and complete epoch/match/slot/generation/life/shot identity preserved");
        int after = observed.Count;
        Check(delivery.PumpBatched(cursor, (_, _) => { observed.Add(default); return true; })
            && observed.Count == after, "empty tail never redelivers admitted facts");

        var corrupt = new NetRetainedDelivery(3); var badCursor = corrupt.Join();
        corrupt.Append(PacketType.TurretFireOutcome, Encode(Emitted()));
        var malformed = Encode(Emitted()); malformed[23] = 0;
        corrupt.Append(PacketType.TurretFireOutcome, malformed);
        int admitted = 0; ulong before = badCursor.Next;
        Check(corrupt.PumpBatched(badCursor, (type, payload) =>
        {
            if (!NetFactBatch.Validate(type, payload)) return false;
            admitted += payload.Length / TurretFireOutcome.Size; return true;
        }) && admitted == 0 && badCursor.Next == before,
            "valid prefix plus malformed row rejects the whole batch without admitting a prefix");
        Check(!NetFactBatch.Validate(PacketType.TurretFireOutcome, Array.Empty<byte>())
            && !NetFactBatch.Validate(PacketType.TurretFireOutcome, new byte[NetFactBatch.MaximumPayload + 1]),
            "empty and oversized outcome batches refused");
        var overrun = new NetRetainedDelivery(2); var slow = overrun.Join();
        for (int i = 0; i < 3; i++) overrun.Append(PacketType.TurretFireOutcome, Encode(Emitted()));
        int callback = 0;
        Check(!overrun.PumpBatched(slow, (_, _) => { callback++; return true; }) && callback == 0
            && slow.Next < overrun.First, "retained history overrun is explicit and cannot silently skip to current outcomes");
        var late = overrun.Join();
        Check(overrun.PumpBatched(late, (_, _) => { callback++; return true; }) && callback == 0,
            "late join starts at the tail without replaying historical outcomes");
    }

    private static void FilteringPreservesIntent()
    {
        var turret = Emitted().Fire;
        var ordinary = turret with { Kind = FireEventKind.AutomaticFire, Weapon = (byte)BeamType.PowerBeam, ShotId = 7 };
        var release = ordinary with { Kind = FireEventKind.ReleaseFire, ShotId = 8 };
        var original = new IntentPacket { Frame = 200, MatchId = 2, AuthorityEpoch = 9,
            SlotGeneration = 4, LifeId = 5, Position = new Vector3(1, 2, 3), Aim = Vector3.UnitZ,
            MoveX = 31, MoveY = -72, HasAnalogMove = true, HasFireEvents = true, FireEventCount = 3 };
        original.FireEvents[0] = ordinary; original.FireEvents[1] = turret; original.FireEvents[2] = release;
        var filtered = NetFireEvents.WithoutTurretOffers(original);
        Check(original.FireEventCount == 3 && original.FireEvents[0] == ordinary
            && original.FireEvents[1] == turret && original.FireEvents[2] == release,
            "filter never mutates the original owner fire history");
        Check(filtered.FireEventCount == 2 && filtered.FireEvents[0] == ordinary && filtered.FireEvents[1] == release,
            "filter removes only turret offers and preserves other fire order");
        Check(filtered.Frame == original.Frame && filtered.MatchId == original.MatchId
            && filtered.AuthorityEpoch == original.AuthorityEpoch && filtered.SlotGeneration == original.SlotGeneration
            && filtered.LifeId == original.LifeId && filtered.Position == original.Position && filtered.Aim == original.Aim
            && filtered.MoveX == original.MoveX && filtered.MoveY == original.MoveY
            && filtered.HasAnalogMove == original.HasAnalogMove && filtered.HasFireEvents == original.HasFireEvents,
            "filter preserves source identity, movement and carrier fields");
        filtered.FireEvents[0] = default;
        Check(original.FireEvents[0] == ordinary, "filtered history remains an independent value after editing");
    }

    private static byte[] ReplayPacket(TurretFireOutcome outcome)
    {
        var bytes = new byte[1 + TurretFireOutcome.Size]; bytes[0] = (byte)PacketType.TurretFireOutcome;
        outcome.Write(bytes.AsSpan(1)); return bytes;
    }
    private static byte[] ReplayMatch(ulong epoch = 9, ushort matchId = 2)
    {
        var bytes = new byte[1 + MatchStatePacket.Size]; bytes[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "", MatchId = matchId,
            AuthorityEpoch = epoch, Mode = (byte)GameMode.Battle }.Write(bytes.AsSpan(1));
        return bytes;
    }
    private static void ReplayRecordingAndRestore()
    {
        var outcome = Emitted();
        var original = new IntentPacket { AuthorityEpoch = 9, MatchId = 2, SlotGeneration = 4,
            LifeId = 5, Frame = 100, Position = new Vector3(1, 2, 3), MoveX = 32,
            MoveY = -64, HasAnalogMove = true, HasFireEvents = true, FireEventCount = 2 };
        original.FireEvents[0] = outcome.Fire;
        original.FireEvents[1] = outcome.Fire with { Kind = FireEventKind.AutomaticFire,
            Weapon = (byte)BeamType.PowerBeam, ShotId = 7 };
        var recorder = new ReplayRecorder();
        recorder.AcceptMatch(MatchStatePacket.Read(ReplayMatch().AsSpan(1)), 0);
        IntentPacket recorded = default; int intents = 0, terminals = 0;
        recorder.Accepted += record =>
        {
            if (record.Payload[0] == (byte)PacketType.SlotIntent)
            { recorded = IntentPacket.Read(record.Payload[2..]); intents++; }
            if (record.Payload[0] == (byte)PacketType.TurretFireOutcome)
            {
                Check(TurretFireOutcome.TryRead(record.Payload[1..], out var terminal) && terminal == outcome,
                    "recorder retains the complete authority terminal outcome");
                terminals++;
            }
        };
        recorder.AcceptIntent(3, original, 100);
        Check(intents == 1 && recorded.FireEventCount == 1
            && recorded.FireEvents[0] == original.FireEvents[1] && recorded.Position == original.Position
            && recorded.MoveX == original.MoveX && recorded.MoveY == original.MoveY,
            "actual recorder stores a filtered carrier preserving movement and ordinary fire");
        Check(original.FireEventCount == 2 && original.FireEvents[0] == outcome.Fire,
            "actual recorder never mutates the caller's owner turret offer");
        recorder.AcceptTurretOutcome(outcome, 130);
        recorder.AcceptTurretOutcome(outcome with { Key = outcome.Key with { AuthorityEpoch = 10 } }, 131);
        Check(terminals == 1, "recorder ignores outcomes outside its exact match/epoch");

        var state = new ReplayReplicaState(); state.Accept(ReplayMatch(), 0);
        var packet = ReplayPacket(outcome); state.Accept(packet, 130);
        Check(state.PendingTurretOutcomeCount == 1, "matching replay outcome queues exactly one presentation");
        long accepted = state.AcceptedPackets; state.Accept(packet, 131);
        Check(state.PendingTurretOutcomeCount == 1 && state.AcceptedPackets == accepted,
            "repeated matching replay terminal is deduplicated");
        bool conflict = false;
        try { state.Accept(ReplayPacket(outcome with { AuthorityFrame = 131 }), 132); }
        catch (InvalidDataException) { conflict = true; }
        Check(conflict && state.PendingTurretOutcomeCount == 1,
            "conflicting terminal for one full shot identity is rejected without adding presentation");
        state.Accept(ReplayPacket(outcome with { Key = outcome.Key with { AuthorityEpoch = 10 } }), 133);
        Check(state.PendingTurretOutcomeCount == 1, "foreign authority stream never queues an outcome");
        var checkpoint = state.CaptureCheckpoint();
        Check(BinaryPrimitives.ReadUInt16LittleEndian(checkpoint.Bytes[4..]) == 9,
            "replica checkpoint explicitly carries the v9 outcome appendix");
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(checkpoint);
        Check(restored.PendingTurretOutcomeCount == 1
            && restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),
            "v9 roundtrip preserves pending outcomes and complete decoder state");
        accepted = restored.AcceptedPackets; restored.Accept(packet, 134);
        Check(restored.PendingTurretOutcomeCount == 1 && restored.AcceptedPackets == accepted,
            "v9 restoration preserves duplicate-terminal suppression");
        var baseline = restored.CaptureCheckpoint();
        var mismatch = checkpoint.Bytes.ToArray();
        // The final row is the pending terminal. Keep its key valid, but make
        // its authority result conflict with the preceding retained dedup row.
        BinaryPrimitives.WriteUInt32LittleEndian(mismatch.AsSpan(mismatch.Length - TurretFireOutcome.Size + 19), 999);
        foreach (var invalid in new[] { mismatch, checkpoint.Bytes[..^1].ToArray() })
        {
            bool rejected = false;
            try { restored.RestoreCheckpoint(new ReplayReplicaCheckpoint(invalid)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && restored.CaptureCheckpoint().Bytes.SequenceEqual(baseline.Bytes),
                "malformed outcome appendix cannot partially replace pending or dedup state");
        }
        restored.Rewind();
        Check(restored.PendingTurretOutcomeCount == 0, "rewind clears queued outcomes");
        restored.Accept(packet, 135);
        Check(restored.PendingTurretOutcomeCount == 1, "rewind clears dedup so a replayed terminal can present again");
        restored.Accept(ReplayMatch(epoch: 10), 136);
        Check(restored.PendingTurretOutcomeCount == 0, "new authority epoch retires pending outcomes");
        restored.Accept(packet, 137);
        Check(restored.PendingTurretOutcomeCount == 0, "retired-stream terminal remains ignored after new match");
        restored.Reset();
        Check(restored.PendingTurretOutcomeCount == 0 && !restored.Match.HasValue,
            "full replica reset clears both outcome state and stream identity");
        recorder.Reset();
    }
}
