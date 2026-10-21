using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using MphRead.Mods.Input;
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
            Check(idle == 138 && bytes[idle] == 0xCC, "idle uses frozen136-byte prefix plus2-byte nonce and leaves unused capacity untouched");
            packet.NativeTouch = new NativeTouchReport(true, true, true, 100, -25, 7, false);
            int touchIdle = CompactIntentCodec.Write(packet, bytes);
            Check(touchIdle == 138 + NativeTouchReport.WireSize
                && (bytes[CompactIntentCodec.ShotInputOffset] & CompactIntentCodec.TouchPresent) != 0
                && CompactIntentCodec.TryRead(bytes[..touchIdle], out var touchDecoded)
                && touchDecoded.NativeTouch == packet.NativeTouch,
                "optional native touch report roundtrip");
            packet.NativeTouch = default;
            CompactIntentCodec.Write(packet, bytes);
            Check(CompactIntentCodec.TryRead(bytes[..idle], out var decoded) && decoded.HasFireEvents
                && decoded.FireEventCount == 0 && decoded.MatchId == packet.MatchId
                && decoded.AuthorityEpoch == packet.AuthorityEpoch && decoded.SlotGeneration == 2
                && decoded.LifeId == 3 && decoded.Position == packet.Position && decoded.MoveX == -127
                && decoded.ContinuousFireTick == 91, "idle identity and state roundtrip");
            packet.FireEventCount = 2;
            packet.FireEvents[0] = new(uint.MaxValue, 100, 90, 128, FireEventKind.PressFire, 0, 17, 0,
                FireEvent.FlagPose | FireEvent.FlagReticle, new(3, 4, 5), Vector3.UnitZ,
                Vector3.UnitX, Vector3.UnitY, new(.25f, .75f), SourceLife: 2, Target: new NetTargetIdentity(0x84, 7, 8));
            packet.FireEvents[1] = packet.FireEvents[0] with { ShotId = 1, Kind = FireEventKind.ReleaseFire };
            int size = CompactIntentCodec.Write(packet, bytes);
            packet.Write(canonical);
            Check(bytes[..(IntentPacket.LegacyFullSize + 1)].SequenceEqual(canonical[..(IntentPacket.LegacyFullSize + 1)])
                && bytes.Slice(CompactIntentCodec.HeaderSize, size - CompactIntentCodec.HeaderSize - 2)
                    .SequenceEqual(canonical.Slice(IntentPacket.LegacyFullSize + 1, size - CompactIntentCodec.HeaderSize - 2))
                && BinaryPrimitives.ReadUInt16LittleEndian(bytes[(size - 2)..]) == packet.GrappleRequestNonce,
                "compact posed shots preserve canonical base and event bytes around live-only input extension");
            Check(CompactIntentCodec.TryRead(bytes[..size], out decoded)
                && decoded.FireEventCount == 2 && decoded.FireEvents[0].ShotId == uint.MaxValue
                && decoded.FireEvents[1].ShotId == 1 && decoded.FireEvents[0].Origin == new Vector3(3, 4, 5)
                && decoded.FireEvents[0].Direction == Vector3.UnitZ
                && decoded.FireEvents[1].SourceFrame == 100 && decoded.FireEvents[0].SourceLife == 2 && decoded.FireEvents[0].Target == new NetTargetIdentity(0x84, 7, 8), "same-frame shots and wrapping IDs stay distinct");
            Check(!CompactIntentCodec.TryRead(bytes[..(size - 1)], out _)
                && !CompactIntentCodec.TryRead(bytes[..(size + 1)], out _)
                && !CompactIntentCodec.TryRead(bytes[..102], out _), "truncation and trailing bytes rejected");
            CompactIntentCodec.Write(packet, bytes);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[(CompactIntentCodec.HeaderSize + 67)..], 0);
            Check(!CompactIntentCodec.TryRead(bytes[..size], out _), "live event must carry original source life");
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
            CompactIntentCodec.Write(packet, bytes);
            bytes[CompactIntentCodec.HeaderSize + 69] = 4;
            Check(!CompactIntentCodec.TryRead(bytes[..size], out _),
                "target identity without supplied marker rejected");
            packet.FireEventCount = NetFireEvents.LiveCapacity;
            for (int i = 0; i < NetFireEvents.LiveCapacity; i++) packet.FireEvents[i] = packet.FireEvents[0] with { ShotId = (uint)(i + 1) };
            size = CompactIntentCodec.Write(packet, bytes);
            // A maximal combat frame must carry its shots, keep ClassicTouch
            // selected and spend NO old touch sample on the receiver.
            packet.NativeTouch = new NativeTouchReport(true, true, true, 91, 0, 50, false);
            int overflowSafe = CompactIntentCodec.Write(packet, bytes);
            Check(overflowSafe == size
                && (bytes[CompactIntentCodec.ShotInputOffset] & CompactIntentCodec.TouchMode) != 0
                && (bytes[CompactIntentCodec.ShotInputOffset] & CompactIntentCodec.TouchPresent) == 0
                && CompactIntentCodec.TryRead(bytes[..overflowSafe], out var deferred)
                && deferred.NativeTouch.Active && deferred.NativeTouch.Suppressed,
                "full combat history preserves native mode but defers touch sample");
            packet.NativeTouch = default;
            size = CompactIntentCodec.Write(packet, bytes);
            Check(size == 1174 && size + 24 + 1 == 1199
                && CompactIntentCodec.TryRead(bytes[..size], out decoded) && decoded.FireEventCount == NetFireEvents.LiveCapacity,
                "maximum SlotIntent fits 1200-byte datagram");
            packet.Write(canonical);
            var legacy = IntentPacket.Read(canonical);
            Span<byte> rewritten = stackalloc byte[IntentPacket.FullSize];
            legacy.Write(rewritten);
            Check(IntentPacket.FullSize == 1287 && FireEvent.Size == 74 && canonical.SequenceEqual(rewritten),
                "current canonical replay roundtrip preserves source life");
            // Historical canonical records had sixteen 67-byte posed events.
            // Build those bytes directly so a current writer cannot hide a stride regression.
            Span<byte> oldPosed = stackalloc byte[IntentPacket.Protocol42FullSize];
            oldPosed.Clear(); canonical[..IntentPacket.LegacyFullSize].CopyTo(oldPosed);
            oldPosed[IntentPacket.LegacyFullSize] = packet.FireEventCount;
            for (int i = 0; i < packet.FireEventCount; i++)
                canonical.Slice(IntentPacket.LegacyFullSize + 1 + i * FireEvent.Size, FireEvent.PoseSize)
                    .CopyTo(oldPosed.Slice(IntentPacket.LegacyFullSize + 1 + i * FireEvent.PoseSize));
            var oldRead = IntentPacket.Read(oldPosed);
            Check(IntentPacket.Protocol42FullSize == 1175 && oldRead.FireEventCount == packet.FireEventCount
                && oldRead.FireEvents[1].ShotId == 2 && oldRead.FireEvents[1].Origin == new Vector3(3, 4, 5)
                && oldRead.FireEvents[1].SourceLife == packet.LifeId,
                "historical 1175-byte posed intent uses old stride and carrier life fallback");
            byte[] historicalPacket = new byte[2 + oldPosed.Length];
            historicalPacket[0] = (byte)PacketType.SlotIntent; historicalPacket[1] = 1;
            oldPosed.CopyTo(historicalPacket.AsSpan(2));
            foreach (int protocol in new[] { 39, 40, 41, 42 })
            {
                var upgraded = ReplayIdentityCompatibility.Convert(historicalPacket, protocol);
                Check(upgraded.Length == 2 + IntentPacket.TouchCanonicalSize
                    && IntentPacket.Read(upgraded[2..]).FireEvents[1] == oldRead.FireEvents[1]
                    && upgraded[^2..].IndexOfAnyExcept((byte)0) < 0,
                    $"protocol {protocol} replay expands posed intent without changing old event");
            }
            Span<byte> old38 = stackalloc byte[IntentPacket.Protocol38FullSize];
            old38.Clear(); canonical[..IntentPacket.LegacyFullSize].CopyTo(old38);
            old38[IntentPacket.LegacyFullSize] = 2;
            for (int i = 0; i < 2; i++)
                canonical.Slice(IntentPacket.LegacyFullSize + 1 + i * FireEvent.Size, FireEvent.LegacySize)
                    .CopyTo(old38.Slice(IntentPacket.LegacyFullSize + 1 + i * FireEvent.LegacySize));
            var old38Read = IntentPacket.Read(old38);
            Check(old38Read.FireEvents[1].ShotId == 2 && old38Read.FireEvents[1].SourceLife == packet.LifeId
                && old38Read.FireEvents[1].PoseFlags == 0, "protocol 38 retains legacy stride and absent pose");
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
            packet.FireEventCount = 15;
            bool rejected = false;
            try { CompactIntentCodec.Size(packet); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "writer rejects invalid count rather than silently truncating");
            Console.WriteLine("compact intent codec tests passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
