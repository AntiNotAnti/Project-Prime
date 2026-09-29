using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ReplicationDecodeBenchmark
{
    public static int Run(string[] args)
    {
        var results = new object[3];
        int result = 0;
        foreach (int count in new[] { 2, 4, 8 })
        {
            byte[] canonical = new byte[4096], assembled = new byte[4096];
            new SnapshotHeader { MatchId = 1, AuthorityEpoch = 1, Frame = 10, PlayerCount = (byte)count }.Write(canonical);
            for (int i = 0; i < count; i++)
                new PlayerState { SlotIndex = (byte)i, SlotGeneration = 1, LifeId = 1, Health = 99 }
                    .Write(canonical.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
            int length = SnapshotHeader.Size + count * PlayerState.Size + 64 + NetHealthSync.HeaderSize;
            var lanes = new NetReplicationLanes();
            lanes.Prepare(canonical.AsSpan(0, length));
            var receiver = new NetReplicationReceiver();
            receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 1, 1);
            var players = new PlayerState[8];
            const int iterations = 200000;
            long RunLoop(bool direct, int n)
            {
                long checksum = 0;
                for (int j = 0; j < n; j++)
                {
                    if (direct)
                    {
                        if (!receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 1, 1, out _))
                            throw new InvalidOperationException("decode refused benchmark packet");
                        for (int i = 0; i < count; i++) checksum += players[i].Health;
                    }
                    else
                    {
                        if (receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), assembled, 1, 1) == 0)
                            throw new InvalidOperationException("assembly refused benchmark packet");
                        for (int i = 0; i < count; i++)
                            players[i] = PlayerState.Read(assembled.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
                        for (int i = 0; i < count; i++) checksum += players[i].Health;
                    }
                }
                return checksum;
            }
            RunLoop(false, 10000); RunLoop(true, 10000);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            long oldSum = RunLoop(false, iterations);
            double oldUs = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
            long oldAllocation = GC.GetAllocatedBytesForCurrentThread() - bytes;
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            long newSum = RunLoop(true, iterations);
            double newUs = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
            long newAllocation = GC.GetAllocatedBytesForCurrentThread() - bytes;
            if (oldSum != newSum) throw new InvalidOperationException("benchmark results differ");
            results[result++] = new { players = count, iterations, canonicalAssembleAndParseUs = oldUs,
                directDecodeUs = newUs, canonicalBytesCopiedPerFrame = length, directCanonicalBytesCopiedPerFrame = 0,
                canonicalAllocationPerFrame = (double)oldAllocation / iterations,
                directAllocationPerFrame = (double)newAllocation / iterations };
        }
        string json = JsonSerializer.Serialize(new { protocol = NetConfig.ProtocolVersion,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            cpu = NetworkBenchmark.CpuDescription(), results }, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 1) File.WriteAllText(args[1], json);
        Console.WriteLine(json); return 0;
    }
}
