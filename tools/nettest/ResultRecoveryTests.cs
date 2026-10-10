using System;
using System.Net;
using System.Reflection;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ResultRecoveryTests
{
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var current = typeof(GameState).GetProperty("Current", flags)!;
        var match = typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!;
        object? previousState = current.GetValue(null), previousMatch = match.GetValue(null);
        var scene = new Scene(new Vector2i(256, 192),
            MphRead.Mods.Input.SyntheticInput.CreateKeyboard(), MphRead.Mods.Input.SyntheticInput.CreateMouse(),
            _ => { }, () => { }, initializeRuntime: false);
        try
        {
            current.SetValue(null, scene.GameState);
            GameState.Mode = GameMode.Capture;
            match.SetValue(null, new MatchStatePacket { MatchId = 42 });
            var endpoint = new IPEndPoint(IPAddress.Loopback, 42);
            var queue = new NetPacketQueue();
            PacketType[] background = { PacketType.MatchSemanticEvent, PacketType.MatchAward, PacketType.ReplayShotFact };
            for (int i = 0; i < 1920; i++)
                NetArchitectureTests.Check(queue.TryEnqueue(new(endpoint, new[] { (byte)background[i % 3] }, 1)), "fill cosmetic/replay inbox");
            NetArchitectureTests.Check(!queue.CanAccept(PacketType.ReplayShotFact), "background lane saturated");
            var report = PostMatchReportPacket.Create();
            report.MatchId = 42; report.Count = 1; report.Slots[0] = 0; report.Generations[0] = 1;
            report.Names[0] = "Recovered"; report.Teams[0] = 1;
            report.Kills[0] = 9; report.Deaths[0] = 3; report.Headshots[0] = 2;
            report.LongestKillStreaks[0] = 4; report.ShotsFired[0] = 80; report.ShotsHit[0] = 40;
            report.DamageDealt[0] = 700; report.DamageTaken[0] = 300;
            report.ObjectiveA[0] = 2; report.ObjectiveB[0] = 3; report.ObjectiveC[0] = 4; report.ObjectiveD[0] = 5;
            byte[] bytes = new byte[PostMatchReportPacket.Size + 1];
            bytes[0] = (byte)PacketType.PostMatchReport; report.Write(bytes.AsSpan(1));
            // The first publication is lost. The server's periodic full-state repeat is identical.
            NetArchitectureTests.Check(GameState.Kills[0] == 0, "lost initial report does not apply");
            NetArchitectureTests.Check(queue.TryEnqueue(new(endpoint, bytes, bytes.Length)), "periodic result repeat uses critical reserve");
            NetArchitectureTests.Check(queue.TryDequeue(NetPacketPriority.Critical, out var recovered)
                && recovered.Type == PacketType.PostMatchReport, "result drains ahead of saturated cosmetics/replay");
            NetArchitectureTests.Check(PostMatchReportPacket.TryRead(recovered.Payload, out var decoded), "repeated result decodes");
            var apply = typeof(NetSession).GetMethod("ApplyPostMatchReport", flags)!;
            apply.Invoke(null, new object[] { decoded });
            NetArchitectureTests.Check(GameState.Kills[0] == 9 && GameState.Deaths[0] == 3
                && GameState.HeadshotKills[0] == 2 && GameState.LongestKillStreak[0] == 4
                && GameState.ShotsFired[0] == 80 && GameState.ShotsHit[0] == 40
                && GameState.MatchDamageDealt[0] == 700 && GameState.MatchDamageTaken[0] == 300
                && GameState.Nicknames[0] == "Recovered", "actual client restores authoritative result counters");
            NetArchitectureTests.Check(scene.GameState.OctolithScores[0] == 2 && scene.GameState.OctolithDrops[0] == 3
                && scene.GameState.OctolithStops[0] == 4 && scene.GameState.ObjectiveSeconds[0] == 5,
                "actual client restores objective results");
            decoded.MatchId = 41; decoded.Kills[0] = 99;
            apply.Invoke(null, new object[] { decoded });
            NetArchitectureTests.Check(GameState.Kills[0] == 9, "wrong-match report cannot replace recovered results");
            NetArchitectureTests.Check(queue.Count == 1920 && queue.HighWater == 1921
                && !NetReliableChannel.IsReliable(PacketType.PostMatchReport), "recovery preserves queue bounds and existing periodic wire semantics");
        }
        finally
        {
            typeof(NetSession).GetMethod("ClearPostMatchReport", flags)!.Invoke(null, null);
            current.SetValue(null, previousState); match.SetValue(null, previousMatch);
        }
    }
}
