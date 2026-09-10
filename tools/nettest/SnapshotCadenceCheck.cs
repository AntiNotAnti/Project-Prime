using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Deterministic codec/cadence accounting only. It does not exercise a live
/// Worker, sockets, rendering, or WAN behavior.
/// </summary>
internal static class SnapshotCadenceCheck
{
    private const int SimulationTicks = SnapshotCadence.ServerTickRateHz * 2;

    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: nettest --snapshot-cadence");
            return 2;
        }

        foreach (int rateHz in new[] { 30, 60 })
        foreach (int players in new[] { 2, 4, 8 })
            Report(new SnapshotCadence(rateHz), players);
        return 0;
    }

    private static void Report(SnapshotCadence cadence, int playerCount)
    {
        var players = new SnapshotPlayer[playerCount];
        for (int slot = 0; slot < playerCount; slot++)
            players[slot] = new SnapshotPlayer
            {
                Slot = (byte)slot,
                Hunter = Hunter.Samus,
                TeamIndex = (byte)slot,
                Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
                Health = 99,
                Life = 1,
                ConnectionId = (ulong)slot + 1,
                Position = new Vector3(slot, 0, 0),
                Aim = -Vector3.UnitZ,
                Facing = -Vector3.UnitZ,
                AvailableWeapons = 1
            };
        Span<byte> packet = stackalloc byte[SnapshotPacket.MaxSize];
        long playerPackets = 0;
        long playerBytes = 0;
        long observerPackets = 0;
        long observerBytes = 0;
        uint sequence = 0;
        for (uint tick = 0; tick < SimulationTicks; tick++)
        {
            if (!cadence.IsDue(tick)) continue;
            int length = new SnapshotPacket(tick, sequence, 1, 0, false, 1, 2)
                .Write(packet, players);
            playerPackets += playerCount;
            playerBytes += (long)length * playerCount;
            observerPackets++;
            observerBytes += length;
            sequence++;
        }
        long expectedSnapshots = SimulationTicks / cadence.IntervalTicks;
        if (sequence != expectedSnapshots || observerPackets != expectedSnapshots)
            throw new InvalidOperationException("Snapshot cadence accounting produced an unexpected sequence/count.");
        Console.WriteLine(FormattableString.Invariant($"SNAPSHOTCADENCE rateHz={cadence.RateHz} players={playerCount} simulationTicks={SimulationTicks} playerPackets={playerPackets} playerBytes={playerBytes} observerPackets={observerPackets} observerBytes={observerBytes} sequenceCount={sequence} synthetic=true wanAcceptance=unclaimed"));
    }
}
