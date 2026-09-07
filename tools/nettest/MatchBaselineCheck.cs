using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>R0 actual entity score mutations on extracted multiplayer content, without sockets/rendering.</summary>
internal static class MatchBaselineCheck
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 3) { Console.Error.WriteLine("Usage: nettest --match-baseline DATA [VERSION]"); return 2; }
        try
        {
            ServerContent.Open(args[1], args.Length == 3 ? args[2] : "AMHE1");
            for (GameMode mode = GameMode.Battle; mode <= GameMode.PrimeHunter; mode++)
            {
                Scene scene = Scene.CreateHeadless();
                try { Check(scene, mode); }
                finally { scene.CloseHeadless(); }
            }
            Console.WriteLine("MATCH_BASELINE PASS modes=12 actual-kills=12 flag-captures=3 nodes=2 defender=2 prime=1");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MATCH_BASELINE FAIL " + error); return 1; }
    }

    private static void Check(Scene scene, GameMode mode)
    {
        string room = SelectRoom(mode);
        scene.LoadServerRoom(room, mode, players: 2, roomPlayerCount: NetLaunch.RoomPlayerCount);
        PlayerEntity player = PlayerEntity.Players[0], opponent = PlayerEntity.Players[1];
        player.ServerActivate(100, Hunter.Samus, 0);
        opponent.ServerActivate(200, Hunter.Kanden, 1);
        PlayerEntity.PlayerCount = 2;
        scene.StepHeadlessFrame(advanceMatch: false);
        if (mode is GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams)
        {
            OctolithFlagEntity? carried = null;
            foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities())
            {
                player.Position = flag.Position.AddY(-1.25f);
                player.PrevPosition = player.Position - Vector3.UnitX * 2;
                opponent.Position = opponent.PrevPosition = flag.Position + Vector3.UnitX * 20;
                flag.Process();
                if (flag.Carrier == player) { carried = flag; break; }
            }
            Require(carried != null, mode + " pickup did not acquire a flag");
            carried!.OnCaptured();
            GameState.UpdateState();
            Require(GameState.Points[0] == 1 && GameState.OctolithScores[0] == 1
                && GameState.TeamPoints[player.TeamIndex] == 1 && carried.Carrier == null,
                mode + " capture must award one point and release the carrier");
        }
        if (mode is GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams)
        {
            NodeDefenseEntity? found = null;
            foreach (NodeDefenseEntity candidate in scene.GetNodeDefenseEntities()) { found = candidate; break; }
            Require(found != null, mode + " has no node objective");
            NodeDefenseEntity node = found!;
            CollisionVolume volume = node.Volume;
            Vector3 center = volume.Type switch
            {
                VolumeType.Sphere => volume.SpherePosition,
                VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
                _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                    + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
            };
            Vector3 old = player.Position;
            player.Position = center - (player.Volume.SpherePosition - old);
            player.ModRefreshNodeRef(old);
            old = opponent.Position;
            opponent.Position = center + Vector3.UnitX * 20;
            opponent.ModRefreshNodeRef(old);
            if (mode is GameMode.Defender or GameMode.DefenderTeams)
            {
                float before = GameState.TeamTime[player.TeamIndex];
                node.Process();
                Require(MathF.Abs(GameState.TeamTime[player.TeamIndex] - before - scene.FrameTime) < 0.00001f,
                    mode + " uncontested occupancy must award exactly one frame of defense time");
                old = opponent.Position;
                opponent.Position = center - (opponent.Volume.SpherePosition - old);
                opponent.ModRefreshNodeRef(old);
                before = GameState.TeamTime[player.TeamIndex];
                node.Process();
                Require(node.Contested && GameState.TeamTime[player.TeamIndex] == before,
                    mode + " contested occupancy must not award time");
            }
            else
            {
                for (int tick = 0; tick < 602; tick++) { node.Process(); }
                Require(node.CapturedPlayer == player && GameState.NodesCaptured[0] == 1,
                    mode + " occupancy must capture the node");
                // Complete seeds the score timer at its threshold, awarding a point on the capture frame.
                Require(GameState.Points[0] == 1, mode + " capture must immediately award one point");
                old = player.Position;
                player.Position = center + Vector3.UnitX * 30;
                player.ModRefreshNodeRef(old);
                for (int tick = 0; tick < 302; tick++) { node.Process(); }
                Require(GameState.Points[0] == 2, mode + " captured node must award another point after five seconds");
                GameState.UpdateState();
                Require(GameState.TeamPoints[player.TeamIndex] == 2, mode + " node points must aggregate to team");
            }
        }
        Array.Clear(GameState.Points); Array.Clear(GameState.Kills); Array.Clear(GameState.Deaths);
        player.Health = 100; opponent.Health = 100;
        opponent.TakeDamage(1000, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, player);
        GameState.UpdateState();
        Require(opponent.Health == 0 && GameState.Deaths[1] == 1 && GameState.Kills[0] == 1,
            mode + " actual lethal damage must record one kill and death");
        int expected = mode is GameMode.Battle or GameMode.BattleTeams ? 1 : 0;
        Require(GameState.Points[0] == expected && GameState.TeamPoints[player.TeamIndex] == expected,
            mode + " kill point award differs from baseline");
        if (mode == GameMode.PrimeHunter)
        {
            Require(GameState.PrimeHunter == 0 && GameState.PrimesKilled[0] == 1,
                "First PrimeHunter kill must establish the holder and count PrimesKilled even without a previous prime");
        }
        Console.WriteLine($"MATCH_BASELINE mode={mode} killPoints={expected} kills=1 deaths=1 PASS");
    }

    private static string SelectRoom(GameMode mode)
    {
        int layer = Metadata.GetMultiplayerEntityLayer(mode, NetLaunch.RoomPlayerCount);
        foreach (RoomMetadata room in Metadata.RoomList)
        {
            if (!room.Multiplayer || room.FirstHunt || room.EntityPath == null) { continue; }
            int flagTeams = 0, baseTeams = 0, spawns = 0;
            bool hasFlag = false, hasBase = false, hasNode = false;
            foreach (Entity entity in Read.GetEntities(room.EntityPath, layer, false))
            {
                if (entity is Entity<OctolithFlagEntityData> flag)
                { hasFlag = true; if (flag.Data.TeamId < 2) { flagTeams |= 1 << (int)flag.Data.TeamId; } }
                else if (entity is Entity<FlagBaseEntityData> flagBase)
                { hasBase = true; if (flagBase.Data.TeamId < 2) { baseTeams |= 1 << (int)flagBase.Data.TeamId; } }
                else if (entity.Type == EntityType.NodeDefense) { hasNode = true; }
                else if (entity is Entity<PlayerSpawnEntityData> spawn && spawn.Data.Active != 0) { spawns++; }
            }
            bool supported = mode switch
            {
                GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams => hasNode,
                GameMode.Bounty or GameMode.BountyTeams => hasFlag && hasBase,
                GameMode.Capture => flagTeams == 3 && baseTeams == 3,
                _ => true
            };
            if (supported && spawns >= 2) { return room.Name; }
        }
        throw new InvalidOperationException($"No extracted multiplayer room supplies {mode} objectives.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
