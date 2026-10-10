using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class CompactIntentCodecTests
{
    private static void Check(bool ok, string label) => NetArchitectureTests.Check(ok, label);

    public static int Run()
    {
        try
        {
            Span<byte> bytes = stackalloc byte[IntentPacket.FullSize + 1];
            Span<byte> canonical = stackalloc byte[IntentPacket.FullSize];
            var packet = new IntentPacket { MatchId = 9, AuthorityEpoch = 0x1020304050607080,
                SlotGeneration = 2, LifeId = 3, Frame = 100, Aim = Vector3.UnitZ,
                Position = new(1, 2, 3), Target = NetTargetIdentity.None,
                MoveX = -127, MoveY = 127, ChargeLevel = 17, ContinuousFireTick = 91 };
            bytes.Fill(0xCC);
            int idle = CompactIntentCodec.Write(packet, bytes);
            Check(idle == 103 && bytes[idle] == 0xCC, "idle uses 103 bytes and leaves unused capacity untouched");
            Check(CompactIntentCodec.TryRead(bytes[..idle], out var decoded) && decoded.HasFireEvents
                && decoded.FireEventCount == 0 && decoded.MatchId == packet.MatchId
                && decoded.AuthorityEpoch == packet.AuthorityEpoch && decoded.SlotGeneration == 2
                && decoded.LifeId == 3 && decoded.Position == packet.Position && decoded.MoveX == -127
                && decoded.ContinuousFireTick == 91, "idle identity and state roundtrip");
            packet.FireEventCount = 2;
            packet.FireEvents[0] = new(uint.MaxValue, 100, 90, 128, FireEventKind.PressFire, 0, 17, 0,
                FireEvent.FlagPose | FireEvent.FlagReticle, new(3, 4, 5), Vector3.UnitZ,
                Vector3.UnitX, Vector3.UnitY, new(.25f, .75f));
            packet.FireEvents[1] = packet.FireEvents[0] with { ShotId = 1, Kind = FireEventKind.ReleaseFire };
            int size = CompactIntentCodec.Write(packet, bytes);
            packet.Write(canonical);
            Check(bytes[..size].SequenceEqual(canonical[..size]), "compact posed shots preserve canonical event bytes");
            Check(CompactIntentCodec.TryRead(bytes[..size], out decoded)
                && decoded.FireEventCount == 2 && decoded.FireEvents[0].ShotId == uint.MaxValue
                && decoded.FireEvents[1].ShotId == 1 && decoded.FireEvents[0].Origin == new Vector3(3, 4, 5)
                && decoded.FireEvents[0].Direction == Vector3.UnitZ
                && decoded.FireEvents[1].SourceFrame == 100, "same-frame shots and wrapping IDs stay distinct");
            Check(!CompactIntentCodec.TryRead(bytes[..(size - 1)], out _)
                && !CompactIntentCodec.TryRead(bytes[..(size + 1)], out _)
                && !CompactIntentCodec.TryRead(bytes[..102], out _), "truncation and trailing bytes rejected");
            bytes[102] = 17;
            Check(!CompactIntentCodec.TryRead(bytes[..size], out _), "future event count rejected");
            CompactIntentCodec.Write(packet, bytes);
            bytes[CompactIntentCodec.HeaderSize + 20] = 0x80;
            Check(!CompactIntentCodec.TryRead(bytes[..size], out _), "unknown pose flags rejected");
            CompactIntentCodec.Write(packet, bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes[(CompactIntentCodec.HeaderSize + 21)..], float.NaN);
            Check(!CompactIntentCodec.TryRead(bytes[..size], out _), "nonfinite shot pose rejected");
            CompactIntentCodec.Write(packet, bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], float.NaN);
            Check(!CompactIntentCodec.TryRead(bytes[..size], out _), "nonfinite base aim rejected");
            packet.FireEventCount = 16;
            for (int i = 0; i < 16; i++) packet.FireEvents[i] = packet.FireEvents[0] with { ShotId = (uint)(i + 1) };
            size = CompactIntentCodec.Write(packet, bytes);
            Check(size == 1175 && size + 24 + 1 == 1200
                && CompactIntentCodec.TryRead(bytes[..size], out decoded) && decoded.FireEventCount == 16,
                "maximum SlotIntent fits 1200-byte datagram");
            packet.Write(canonical);
            var legacy = IntentPacket.Read(canonical);
            Span<byte> rewritten = stackalloc byte[IntentPacket.FullSize];
            legacy.Write(rewritten);
            Check(IntentPacket.FullSize == 1175 && FireEvent.Size == 67 && canonical.SequenceEqual(rewritten),
                "fixed protocol 41 replay roundtrip remains unchanged");
            // Warm static validation state before measuring the live hot path.
            for (int i = 0; i < 10; i++) { CompactIntentCodec.Write(packet, bytes); CompactIntentCodec.TryRead(bytes[..size], out _); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool allRead = true;
            for (int i = 0; i < 1000; i++)
            {
                CompactIntentCodec.Write(packet, bytes);
                allRead &= CompactIntentCodec.TryRead(bytes[..size], out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allRead && allocated == 0, "compact write/read hot path allocates zero bytes");
            packet.FireEventCount = 17;
            bool rejected = false;
            try { CompactIntentCodec.Size(packet); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "writer rejects invalid count rather than silently truncating");
            Console.WriteLine("compact intent codec tests passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
