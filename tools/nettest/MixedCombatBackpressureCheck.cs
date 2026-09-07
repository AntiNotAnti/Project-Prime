using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

/// <summary>Real loopback admission, followed by deterministic reliable-queue saturation; no game assets.</summary>
internal static class MixedCombatBackpressureCheck
{
    public static int Run()
    {
        try
        {
            using var transport = new NetTransport(0);
            using var firstSocket = new NetTransport(0);
            using var secondSocket = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            var network = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var first = new NetClient(firstSocket, endpoint, "FULL", Hunter.Samus);
            using var second = new NetClient(secondSocket, endpoint, "HEALTHY", Hunter.Samus);
            void PumpUntil(Func<bool> done)
            {
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed.TotalSeconds < 5)
                {
                    network.Poll((uint)(timer.Elapsed.TotalSeconds * 60)); first.Poll(); second.Poll();
                    if (done()) return;
                    Thread.Sleep(2);
                }
                throw new InvalidOperationException("Loopback admission timed out.");
            }
            PumpUntil(() => first.Connection != null && second.Connection != null && network.Count == 2);
            Require(first.Ready(network.MatchId) && second.Ready(network.MatchId), "Ready admission failed.");
            ServerPeer stalled = network.Find(first.Connection!.Id)!;
            ServerPeer healthy = network.Find(second.Connection!.Id)!;
            PumpUntil(() => stalled.Connection.State == NetConnectionState.Ready
                && healthy.Connection.State == NetConnectionState.Ready
                && stalled.Connection.Reliable.PendingCount == 0 && healthy.Connection.Reliable.PendingCount == 0);
            stalled.Connection.StartPlaying(); healthy.Connection.StartPlaying();
            // No socket polling between fill and dispatch: exhaustion and admission order are deterministic.
            for (int i = 0; i < ReliableChannel.Capacity; i++)
                Require(network.TrySendEvent(stalled, ReliableEventType.Combat, new byte[] { 0 }), "Queue filled prematurely.");
            Require(stalled.Connection.Reliable.PendingCount == ReliableChannel.Capacity, "Expected bounded full queue.");
            Require(MixedCombatSoak.SendCombatBatch(network, new byte[] { 41 }) == 1, "Refusal was not counted exactly once.");
            Require(network.Count == 1 && network.Find(stalled.Connection.Id) == null
                && stalled.Connection.State == NetConnectionState.Disconnecting, "Saturated peer remained admitted.");
            Require(network.Find(healthy.Connection.Id) == healthy && healthy.Connection.State == NetConnectionState.Playing,
                "Healthy peer was affected by another peer's saturation.");
            Require(MixedCombatSoak.SendCombatBatch(network, new byte[] { 42 }) == 0, "Later healthy admission failed.");
            Require(healthy.Connection.Reliable.PendingCount == 2, "Shared batch was duplicated or lost for healthy peer.");
            uint? firstId = null, secondId = null;
            for (int i = 0; i < 2; i++)
            {
                Require(healthy.Connection.Reliable.TryGetDue(0, out uint id, out ReliableEventType type, out var payload),
                    "Healthy event missing.");
                Require(type == ReliableEventType.Combat && payload.Length == 5
                    && BinaryPrimitives.ReadUInt32LittleEndian(payload.Span) == network.MatchId
                    && payload.Span[4] is 41 or 42, "Admission payload changed.");
                // Reliable sending is round-robin, not FIFO; validate admission sequence IDs,
                // without incorrectly imposing delivery ordering on the existing protocol.
                if (payload.Span[4] == 41) { Require(firstId == null, "Duplicate first batch."); firstId = id; }
                else { Require(secondId == null, "Duplicate second batch."); secondId = id; }
                healthy.Connection.Reliable.MarkSent(id, (uint)i, 1);
            }
            Require(firstId.HasValue && secondId == unchecked(firstId.Value + 1), "Admission sequence was duplicated or reordered.");
            Require(!healthy.Connection.Reliable.TryGetDue(0, out _, out _, out _), "Unexpected duplicate admission.");
            Console.WriteLine("MIXEDBACKPRESSURE PASS: bounded refusal disconnects only saturated peer; healthy batches admitted once in order.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MIXEDBACKPRESSURE FAIL " + error); return 1; }
    }

    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
