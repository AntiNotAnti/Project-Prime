using System;
using System.Net;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class QueueBudgetTests
{
    public static int Run()
    {
        try
        {
            TransportBoundaryTests.Run();
            NetArchitectureTests.Check(WireTelemetryTests.Run() == 0, "wire accounting and allocation checks");
            ResultRecoveryTests.Run();
            var endpoint = new IPEndPoint(IPAddress.Loopback, 42);
            var queue = new NetPacketQueue();
            byte[] intent = { (byte)PacketType.Intent }, loaded = { (byte)PacketType.MatchLoaded }, chat = { (byte)PacketType.Chat };
            byte[] cue = { (byte)PacketType.ConfirmedImpact, 123 };
            for (int i = 0; i < 10000; i++) queue.TryEnqueue(new(endpoint, cue, cue.Length));
            NetArchitectureTests.Check(queue.CosmeticCount == 64 && queue.CosmeticHighWater == 64
                && queue.CosmeticDrops == 9936 && queue.Count == 0 && queue.HighWater == 0 && queue.Drops == 0,
                "cosmetic flood has independent bounded capacity and accounting");
            NetArchitectureTests.Check(!queue.CanAccept(PacketType.ConfirmedImpact) && queue.CanAcceptCritical
                && queue.CanAccept(PacketType.Intent), "cosmetic flood cannot deny gameplay admission");
            for (int i = 0; i < 10000; i++) queue.TryEnqueue(new(endpoint, intent, intent.Length));
            NetArchitectureTests.Check(queue.Count == 1920 && queue.Drops == 8080, "realtime flood cannot consume reserve");
            NetArchitectureTests.Check(!queue.CanAccept(PacketType.MatchSemanticEvent)
                && !queue.CanAccept(PacketType.MatchAward) && queue.CanAccept(PacketType.MatchLoaded),
                "reliable admission follows actual priority before acknowledging delivery");
            for (int i = 0; i < 128; i++) NetArchitectureTests.Check(queue.TryEnqueue(new(endpoint, loaded, 1)), "critical reserve survives flood");
            NetArchitectureTests.Check(!queue.TryEnqueue(new(endpoint, loaded, 1)) && !queue.TryEnqueue(new(endpoint, chat, 1))
                && queue.HighWater == 2048, "absolute capacity");
            int cues = 0;
            while (queue.TryDequeue(NetPacketPriority.Cosmetic, out var cuePacket))
            {
                NetArchitectureTests.Check(ReferenceEquals(cuePacket.Data, cue) && cuePacket.Length == cue.Length
                    && cuePacket.Payload[0] == 123, "cosmetic dequeue transfers original payload ownership");
                cues++;
            }
            NetArchitectureTests.Check(cues == 64 && queue.CosmeticCount == 0 && queue.Count == 2048
                && queue.CanAccept(PacketType.ConfirmedImpact) && !queue.CanAcceptCritical,
                "cosmetic drain changes only cosmetic count while gameplay remains full");
            NetArchitectureTests.Check(queue.TryEnqueue(new(endpoint, cue, cue.Length))
                && queue.CosmeticCount == 1 && queue.Count == 2048 && queue.CosmeticDrops == 9936,
                "full gameplay queue cannot deny cosmetic admission");
            NetArchitectureTests.Check(queue.TryDequeue(NetPacketPriority.Cosmetic, out _)
                && !queue.TryDequeue(NetPacketPriority.Cosmetic, out _) && queue.CosmeticCount == 0,
                "empty cosmetic dequeue does not underflow count");
            int controls = 0;
            while (queue.TryDequeue(NetPacketPriority.Critical, out var packet))
            { NetArchitectureTests.Check(packet.Type == PacketType.MatchLoaded, "control priority"); controls++; }
            NetArchitectureTests.Check(controls == 128 && queue.Count == 1920 && queue.CosmeticCount == 0,
                "critical work drains before state backlog with independent cosmetic accounting");
            var buckets = new NetTokenBucket[8];
            int[] admitted = new int[8];
            for (int tick = 0; tick < 600; tick++)
            {
                for (int peer = 0; peer < 8; peer++)
                    for (int n = 0; n < (peer == 0 ? 167 : 1); n++)
                        if (buckets[peer].Take(tick * 1000.0 / 60, 180, 64)) admitted[peer]++;
            }
            NetArchitectureTests.Check(admitted[0] < 1900, "10000pps abusive peer limited");
            for (int peer = 1; peer < 8; peer++) NetArchitectureTests.Check(admitted[peer] == 600, "normal peer not charged to abusive bucket");
            NetArchitectureTests.Check(NetPumpBudget.Default == new NetPumpBudget(128, 256, 32, 8)
                && new NetPumpBudget(1, 2, 3).Cosmetic == 8
                && NetPumpBudget.BeforeSimulation == new NetPumpBudget(128, 256, 0, 0)
                && NetPumpBudget.AfterSimulation == new NetPumpBudget(0, 0, 32, 8), "bounded independent cosmetic pump");
            Console.WriteLine("PASS: independent cosmetic capacity, flood capacity, critical reserve, per-peer rate limits and pump budgets"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
