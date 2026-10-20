using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class PickupProtocolTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Pickup protocol: " + label); }
    private static byte[] Encode(PickupClaim value) { var bytes = new byte[PickupClaim.Size]; value.Write(bytes); return bytes; }
    private static byte[] Encode(PickupVerdict value) { var bytes = new byte[PickupVerdict.Size]; value.Write(bytes); return bytes; }
    private static byte[] Encode(PickupItemFact value) { var bytes = new byte[PickupItemFact.Size]; value.Write(bytes); return bytes; }
    private static byte[] Encode(PickupReady value) { var bytes = new byte[PickupReady.Size]; value.Write(bytes); return bytes; }
    private static void Reject(Action action, string label)
    { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, label); }
    internal static int Run()
    {
        _checks = 0;
        try
        {
            ReadyCodec();
            var key = new PickupClaimKey(9, 2, 3, 4, 5, 6);
            var claim = new PickupClaim(key, new(9, 2, 7, -1, 8), TimedPowerUp.DoubleDamage, 0, new(1, -2, 3));
            Check(key.IsValid && key.LifeKey == new ShotKey(9, 2, 3, 4, 5, 0), "claim key preserves full life identity");
            var keyBytes = new byte[PickupClaimKey.Size]; key.Write(keyBytes);
            Check(PickupClaimKey.Size == 19 && PickupClaimKey.TryRead(keyBytes, out var readKey) && readKey == key,
                "claim key exact roundtrip");
            Check(PickupClaim.Size == 48 && PickupVerdict.Size == 73 && PickupItemFact.Size == 40, "exact wire sizes");
            foreach (var type in new[] { TimedPowerUp.DoubleDamage, TimedPowerUp.Cloak, TimedPowerUp.DeathAlt })
            {
                var value = claim with { Type = type }; var bytes = Encode(value);
                Check(PickupClaim.TryRead(bytes, out var decoded) && decoded == value, "claim roundtrip " + type);
            }
            var grant = new PickupVerdict(claim, PickupResult.Grant, 100, 90, 12, uint.MaxValue - 100,
                unchecked(uint.MaxValue - 100 + 1800u));
            Check(PickupVerdict.TryRead(Encode(grant), out var verdict) && verdict == grant, "grant lifetime roundtrip across tick wrap");
            foreach (PickupResult result in Enum.GetValues<PickupResult>())
            {
                if (result == PickupResult.Grant) continue;
                var value = new PickupVerdict(claim, result, 100, 90, 0, 0, 0);
                Check(PickupVerdict.TryRead(Encode(value), out verdict) && verdict == value, "terminal refusal roundtrip " + result);
            }
            var fact = new PickupItemFact(claim.Spawn, TimedPowerUp.Cloak, 0, true, claim.Position);
            RegistryAndBudgets(claim, grant, fact);
            foreach (bool available in new[] { false, true })
            {
                var value = fact with { Available = available };
                Check(PickupItemFact.TryRead(Encode(value), out var decoded) && decoded == value, "item availability roundtrip");
            }
            byte[] cb = Encode(claim), vb = Encode(grant), fb = Encode(fact);
            for (int length = 0; length < cb.Length; length++) Check(!PickupClaim.TryRead(cb.AsSpan(0, length), out _), "claim truncation " + length);
            for (int length = 0; length < vb.Length; length++) Check(!PickupVerdict.TryRead(vb.AsSpan(0, length), out _), "verdict truncation " + length);
            for (int length = 0; length < fb.Length; length++) Check(!PickupItemFact.TryRead(fb.AsSpan(0, length), out _), "fact truncation " + length);
            Check(!PickupClaim.TryRead(new byte[49], out _) && !PickupVerdict.TryRead(new byte[74], out _)
                && !PickupItemFact.TryRead(new byte[41], out _), "all trailing payloads rejected");
            foreach (var bad in new[] { key with { Epoch = 0 }, key with { Match = 0 }, key with { Slot = -1 },
                key with { Slot = 8 }, key with { Generation = 0 }, key with { Life = 0 }, key with { Sequence = 0 } })
                Reject(() => Encode(claim with { Key = bad }), "invalid claim identity");
            foreach (var bad in new[] { claim.Spawn with { Epoch = 10 }, claim.Spawn with { Match = 3 },
                claim.Spawn with { Room = 0 }, claim.Spawn with { Generation = 0 }, claim.Spawn with { SpawnId = -2 } })
                Reject(() => Encode(claim with { Spawn = bad }), "invalid or mismatched spawn identity");
            foreach (var position in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0),
                new Vector3(0, 0, float.NegativeInfinity), new Vector3(100000, 0, 0), new Vector3(-100000, 0, 0) })
            {
                Check(!PickupWire.ValidPosition(position), "nonfinite or boundary position rejected");
                Reject(() => Encode(claim with { Position = position }), "invalid claim position");
                Reject(() => Encode(fact with { Position = position }), "invalid item position");
            }
            Reject(() => claim.Write(new byte[49]), "write requires exact claim length");
            Reject(() => grant.Write(new byte[72]), "write requires exact verdict length");
            Reject(() => fact.Write(new byte[39]), "write requires exact fact length");
            Reject(() => Encode(claim with { Type = (TimedPowerUp)3 }), "unknown claim type");
            Reject(() => Encode(fact with { Type = (TimedPowerUp)255 }), "unknown fact type");
            Reject(() => Encode(grant with { GrantSequence = 0 }), "grant requires sequence");
            Reject(() => Encode(grant with { ExpiresAtFrame = 0 }), "grant requires exact native lifetime");
            Reject(() => Encode(grant with { Result = PickupResult.Taken }), "refusal cannot carry grant fields");
            foreach (byte result in new byte[] { 0, 10, 255 })
            { var bytes = (byte[])vb.Clone(); bytes[48] = result; Check(!PickupVerdict.TryRead(bytes, out _), "unknown wire result"); }
            { var bytes = (byte[])fb.Clone(); bytes[27] = 2; Check(!PickupItemFact.TryRead(bytes, out _), "wire bool encoding is strict"); }
            { var bytes = (byte[])cb.Clone(); bytes[10] = 8; Check(!PickupClaim.TryRead(bytes, out _), "wire invalid slot"); }
            { var bytes = (byte[])cb.Clone(); bytes[31] = 3; Check(!PickupClaim.TryRead(bytes, out _), "wire invalid type"); }
            { var bytes = (byte[])cb.Clone(); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(36), float.NaN);
                Check(!PickupClaim.TryRead(bytes, out var rejected) && rejected == default, "wire nonfinite position fails atomically"); }
            Console.WriteLine($"PASS: pickup protocol {_checks} codec assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void ReadyCodec()
    {
        var ready = new PickupReady(0x1020304050607080, 0x1234, 0x5678, 0x90ABCDEF);
        byte[] bytes = Encode(ready);
        Check(PickupReady.Size == 16 && PickupReady.TryRead(bytes, out var decoded) && decoded == ready,
            "readiness exact 16-byte roundtrip");
        // Independent offsets rather than roundtrip-only codec agreement.
        Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes) == 0x1020304050607080
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)) == 0x1234
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)) == 0x5678
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)) == 0x90ABCDEF,
            "readiness independent epoch/match/generation/sequence offsets");
        for (int size = 0; size < PickupReady.Size; size++)
            Check(!PickupReady.TryRead(bytes.AsSpan(0, size), out var rejected) && rejected == default,
                "readiness truncation fails atomically " + size);
        Check(!PickupReady.TryRead(new byte[17], out decoded) && decoded == default,
            "readiness trailing byte rejected atomically");
        Reject(() => ready.Write(new byte[15]), "readiness short write rejected");
        Reject(() => ready.Write(new byte[17]), "readiness long write rejected");
        foreach (var bad in new[] { ready with { Epoch = 0 }, ready with { Match = 0 },
            ready with { Generation = 0 }, ready with { Sequence = 0 } })
            Reject(() => Encode(bad), "readiness zero identity rejected");
        foreach (var field in new[] { (Offset: 0, Size: 8), (Offset: 8, Size: 2),
            (Offset: 10, Size: 2), (Offset: 12, Size: 4) })
        {
            byte[] malformed = (byte[])bytes.Clone(); Array.Clear(malformed, field.Offset, field.Size);
            Check(!PickupReady.TryRead(malformed, out decoded) && decoded == default,
                "readiness zero wire field rejected atomically " + field.Offset);
        }
        var maximum = new PickupReady(ulong.MaxValue, ushort.MaxValue, ushort.MaxValue, uint.MaxValue);
        Check(PickupReady.TryRead(Encode(maximum), out decoded) && decoded == maximum,
            "readiness maximum identity fields roundtrip");
    }

    private static void RegistryAndBudgets(PickupClaim claim, PickupVerdict verdict, PickupItemFact fact)
    {
        Check(NetConfig.ProtocolVersion == 65 && (byte)PacketType.PickupClaim == 73
            && (byte)PacketType.PickupVerdict == 74 && (byte)PacketType.PickupItem == 75
            && (byte)PacketType.PickupReady == 76,
            "protocol 65 preserves pickup packet identities");
        var ids = new HashSet<byte>();
        foreach (string name in Enum.GetNames<PacketType>())
        {
            // The pre-existing HitVerdict spelling intentionally aliases CombatAck.
            if (name == nameof(PacketType.HitVerdict)) continue;
            Check(ids.Add((byte)Enum.Parse<PacketType>(name)), "packet enum has no collision: " + name);
        }
        var reliable = new NetReliableChannel();
        foreach (var row in new[] { (PacketType.PickupClaim, Encode(claim)),
            (PacketType.PickupVerdict, Encode(verdict)), (PacketType.PickupItem, Encode(fact)),
            (PacketType.PickupReady, Encode(new PickupReady(9, 2, 4, 6))) })
        {
            Check(NetReliableChannel.IsReliable(row.Item1) && !NetReliableChannel.IsCritical(row.Item1),
                "ordinary reliable recovery lane: " + row.Item1);
            Check(NetPacketQueue.Priority(row.Item1) == NetPacketPriority.Realtime,
                "explicit realtime queue priority: " + row.Item1);
            Check(reliable.TryQueue(row.Item1, row.Item2, 0, out uint eventId) && eventId != 0,
                "actual reliable channel admits pickup payload: " + row.Item1);
        }
        Check(NetPacketQueue.Priority(PacketType.PickupClaim) == NetPacketQueue.Priority(PacketType.BombIntent)
            && NetPacketQueue.Priority(PacketType.PickupVerdict) == NetPacketQueue.Priority(PacketType.TurretFireOutcome)
            && NetPacketQueue.Priority(PacketType.PickupItem) == NetPacketQueue.Priority(PacketType.TurretFireOutcome),
            "pickup priorities preserve the existing bounded event lanes");
        Check(NetFactBatch.Stride(PacketType.PickupClaim) == 0
            && !NetFactBatch.Validate(PacketType.PickupClaim, Encode(claim)), "claims never use authority fact batching");
        Check(NetFactBatch.Stride(PacketType.PickupVerdict) == 73 && NetFactBatch.Stride(PacketType.PickupItem) == 40,
            "authority pickup fact strides");
        Check(NetFactBatch.Stride(PacketType.PickupReady) == 0
            && !NetFactBatch.Validate(PacketType.PickupReady, Encode(new PickupReady(9, 2, 4, 6))),
            "readiness never uses authority fact batching");
        int envelope = NetHeader.Size + 4;
        Check(PickupReady.Size + envelope == 44, "single reliable readiness includes header and event ID");
        Check(PickupClaim.Size + envelope == 76, "single reliable claim budget includes header and event ID");
        Check(29 * PickupItemFact.Size + envelope == 1188 && 30 * PickupItemFact.Size + envelope > 1200,
            "29 item facts fit while 30 exceed realtime MTU");
        Check(16 * PickupVerdict.Size + envelope == 1196 && 17 * PickupVerdict.Size + envelope > 1200,
            "16 verdicts fit while 17 exceed realtime MTU");
        var items = new byte[29 * PickupItemFact.Size];
        for (int offset = 0; offset < items.Length; offset += PickupItemFact.Size)
            fact.Write(items.AsSpan(offset, PickupItemFact.Size));
        var verdicts = new byte[16 * PickupVerdict.Size];
        for (int offset = 0; offset < verdicts.Length; offset += PickupVerdict.Size)
            verdict.Write(verdicts.AsSpan(offset, PickupVerdict.Size));
        Check(NetFactBatch.Validate(PacketType.PickupItem, items)
            && NetFactBatch.Validate(PacketType.PickupVerdict, verdicts), "production decoder validates maximum bounded batches");
        items[27] = 2;
        Check(!NetFactBatch.Validate(PacketType.PickupItem, items), "one malformed availability invalidates a whole item batch");
        Check(!NetFactBatch.Validate(PacketType.PickupItem, new byte[30 * PickupItemFact.Size])
            && !NetFactBatch.Validate(PacketType.PickupVerdict, new byte[17 * PickupVerdict.Size]),
            "production decoder rejects oversized batches");
    }
}
