using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MphRead.Mods.Network;
namespace MphRead.NetTest;
internal static class Protocol18Tests
{
    // Keep the measuring loop out of tiered on-stack replacement. The unchanged
    // reference binary fails the old loop under default tiering and passes with
    // tiering disabled; this observes warmed production calls with a hot helper.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureDecode(NetReplicationReceiver receiver, byte[] fast, int fastLength,
        byte[] decoded, out int length)
    {
        length = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            length = receiver.Assemble(fast.AsSpan(0, fastLength), decoded, 65535, ulong.MaxValue);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public static int Run()
    {
        try
        {
            NetArchitectureTests.Check(SnapshotFast.MaximumEncodedSize <= 1200, "worst-case fast datagram MTU");
            int maximumLiveIntentSize = CompactIntentCodec.Size(new IntentPacket { FireEventCount = NetFireEvents.LiveCapacity });
            NetArchitectureTests.Check(NetHeader.Size + maximumLiveIntentSize <= 1200
                && NetHeader.Size + 1 + maximumLiveIntentSize <= 1200
                && NetHeader.Size + 1 + HitClaimPacket.MaxPerPacket * HitClaimPacket.Size <= 1200
                && NetHeader.Size + HitVerdictPacket.HeaderSize + HitVerdictPacket.MaxPerPacket * HitVerdictPacket.EntrySize <= 1200,
                "intent/slot-intent, full claim batch and full verdict batch MTU");
            int time = SnapshotHeader.Size + 8 * PlayerState.Size, world = time + 64;
            byte[] canonical = new byte[world + NetHealthSync.HeaderSize + NetHealthSync.MaxSpawns * NetHealthSync.EntrySize];
            new SnapshotHeader { MatchId = 65535, AuthorityEpoch = ulong.MaxValue, Frame = uint.MaxValue, PlayerCount = 8, Rng1 = 3, Rng2 = 4 }.Write(canonical);
            for (int slot = 0; slot < 8; slot++)
                new PlayerState { SlotIndex = (byte)slot, Flags = 255, SlotGeneration = 65535, LifeId = 65535,
                    Points = short.MaxValue, Kills = 65535, Deaths = 65535, Team = (byte)slot,
                    DamageEventId = 65535, Damage0 = new DamageEvent { Damage = 65535, EventId = 65535, Flags = 255 },
                    Damage1 = new DamageEvent { EventId = 65534 }, Damage2 = new DamageEvent { EventId = 65533 }, Damage3 = new DamageEvent { EventId = 65532 } }
                    .Write(canonical.AsSpan(SnapshotHeader.Size + slot * PlayerState.Size));
            BinaryPrimitives.WriteUInt16LittleEndian(canonical.AsSpan(world), 65535); canonical[world + 2] = 56;
            for (int i = 0; i < 56; i++) BinaryPrimitives.WriteInt16LittleEndian(canonical.AsSpan(world + 3 + i * 7), (short)i);
            var lanes = new NetReplicationLanes(); lanes.Prepare(canonical);
            NetArchitectureTests.Check(lanes.SlowLength + NetHeader.Size <= 1200 && lanes.WorldLength + NetHeader.Size <= 1200
                && SnapshotFast.BootstrapMaximumEncodedSize + WorldBootstrapIdentity.Size + 1 + 4 <= 1200,
                "all lanes and reliable bootstrap envelope fit the MTU budget");
            var receiver = new NetReplicationReceiver();
            NetArchitectureTests.Check(receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 65535, ulong.MaxValue), "world independent");
            NetArchitectureTests.Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 65535, ulong.MaxValue), "slow independent");
            NetArchitectureTests.Check(!receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 65535, ulong.MaxValue), "duplicate revision refused");
            var decoded = new byte[NetConfig.MaxSnapshotSize]; int length = receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 65535, ulong.MaxValue);
            NetArchitectureTests.Check(canonical.AsSpan().SequenceEqual(decoded.AsSpan(0, length)), "all status/damage/lifecycle/world/score bytes preserved");
            for (int i = 0; i < 100; i++) receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 65535, ulong.MaxValue);
            long warmupBytes = MeasureDecode(receiver, lanes.Fast, lanes.FastLength, decoded, out _);
            Console.WriteLine($"[protocol18] fixed measurement helper warmup: {warmupBytes} bytes");
            long allocated = MeasureDecode(receiver, lanes.Fast, lanes.FastLength, decoded, out int measuredLength);
            NetArchitectureTests.Check(measuredLength == length && canonical.AsSpan().SequenceEqual(decoded.AsSpan(0, measuredLength)),
                "measured fast decode preserves the full canonical snapshot");
            NetArchitectureTests.Check(allocated == 0, $"warmed fast decode allocation-free (actual {allocated} bytes)");
            Console.WriteLine($"PASS: protocol 18 lane round-trip, revision fencing, 0 decode allocations, {SnapshotFast.MaximumEncodedSize}B worst-case fast datagram"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
