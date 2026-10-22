using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Fidelity;

/// <summary>Fixed-tick probes of actual objective handlers; no duplicate scoring implementation.</summary>
internal static class FidelityMatchScenarios
{
    internal static readonly FidelityScenario[] All = new[] { "battle", "survival", "capture", "nodes", "prime" }
        .Select(id => new FidelityScenario("match." + id, 1, 720, 123456,
            "extracted:AMHE1:first-supported-native-room:v1", 10, FidelityTier.F2,
            "Actual offline match/objective handlers; scripted placement, damage and victory threshold. Captures normalized state at 60 Hz; no overtime rule exists.")).ToArray();

    internal static List<FidelityCheckpoint> Capture(FidelityScenario scenario, int hz)
    {
        if (NetSession.Active) throw new InvalidOperationException("Match oracle requires a fresh offline process.");
        if (hz is < 30 or > 1000) throw new ArgumentOutOfRangeException(nameof(hz));
        Paths.MphKey = Ver.AMHE1;
        if (!ServerSim.Available(out string reason)) throw new InvalidOperationException(reason);
        Headless.Enter();
        GameMode mode = scenario.Id switch { "match.battle" => GameMode.Battle, "match.survival" => GameMode.Survival,
            "match.capture" => GameMode.Capture, "match.nodes" => GameMode.Nodes, "match.prime" => GameMode.PrimeHunter,
            _ => throw new ArgumentException("Unknown match scenario") };
        var rooms = ThumbnailGenerator.MultiplayerRooms().ToArray();
        int roomIndex = Array.FindIndex(rooms, room => MapModeCapabilities.Supports(room, mode, MatchWorldProfile.Resolve(2), out _));
        if (roomIndex < 0) throw new InvalidOperationException("No compatible native room.");
        bool force = MapAudit.ForceEveryone; MapAudit.ForceEveryone = true;
        Scene? scene = null;
        try
        {
            scene = new Scene(new(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {});
            scene.AddPlayer(Hunter.Samus); scene.AddPlayer(Hunter.Samus);
            scene.Players.PlayerCount = 2; scene.Players.MainPlayerIndex = 0;
            foreach (var player in scene.Players.Items) player.IsBot = false;
            scene.AddRoom(rooms[roomIndex], mode, playerCount: 2); scene.OnLoad();
            var state = scene.GameState; state.MatchTime = -1;
            scene.Random.SetRng1(scenario.Seed); scene.Random.SetRng2(scenario.Seed ^ 0xa5a5a5a5);
            var actor = scene.Players.Items[0]; var target = scene.Players.Items[1];
            actor.TeamIndex = 0; target.TeamIndex = 1;
            for (int warmup = 0; warmup < 120; warmup++) { NetTestScript.Rest(target, true); scene.OnSimulationFrame(); }
            if (actor.Health == 0 || target.Health == 0) throw new InvalidOperationException("Match actors did not spawn.");
            var flags = new List<OctolithFlagEntity>();
            foreach (var flag in scene.GetOctolithFlagEntities()) flags.Add(flag);
            var nodes = new List<NodeDefenseEntity>();
            foreach (var value in scene.GetNodeDefenseEntities()) nodes.Add(value);
            var touch = typeof(OctolithFlagEntity).GetMethod("OnTouched", BindingFlags.Instance | BindingFlags.NonPublic)!;
            OctolithFlagEntity? objective = flags.FirstOrDefault(f => f.Data.TeamId != actor.TeamIndex);
            NodeDefenseEntity? node = nodes.FirstOrDefault();
            void Place(PlayerEntity player, Vector3 center)
            {
                Vector3 offset = player.Volume.SpherePosition - player.Position;
                player.Spawn(center - offset - Vector3.UnitY, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
            }
            Vector3 nodeCenter = node?.Volume.GetCenter() ?? Vector3.Zero;
            var output = new List<FidelityCheckpoint>();
            Render.FrameTiming.Reset(); Render.FrameTiming.ResetDiagnostics();
            int tick = 0, frames = 0;
            while (tick < scenario.Ticks && frames++ < hz * 14)
            {
                int steps = Render.FrameTiming.Advance(1.0 / hz);
                for (int step = 0; step < steps && tick < scenario.Ticks; step++)
                {
                    tick++;
                    NetTestScript.Rest(target, true);
                    if (tick == 10)
                    {
                        if (mode is GameMode.Battle or GameMode.PrimeHunter or GameMode.Survival)
                            target.TakeDamage(500, DamageFlags.IgnoreInvuln, null, actor);
                        if (mode == GameMode.Capture)
                        {
                            if (objective == null || !(bool)touch.Invoke(objective, new object[] { actor })!)
                                throw new InvalidOperationException("Capture oracle did not pick up flag.");
                        }
                        if (mode == GameMode.Nodes)
                        {
                            if (node == null) throw new InvalidOperationException("Nodes oracle lacks node.");
                            Place(actor, nodeCenter); Place(target, nodeCenter + new Vector3(100,100,100));
                        }
                    }
                    if (mode == GameMode.Capture && tick == 60) objective!.OnCaptured();
                    if (mode == GameMode.Nodes && tick == 30) Place(target, nodeCenter);
                    if (mode == GameMode.Nodes && tick == 50) Place(target, nodeCenter + new Vector3(100,100,100));
                    if (mode == GameMode.PrimeHunter && tick == 100)
                    {
                        target.Spawn(target.Position, Vector3.UnitZ, Vector3.UnitY, target.NodeRef, respawn: true);
                        actor.TakeDamage(500, DamageFlags.IgnoreInvuln, null, target);
                    }
                    if (mode == GameMode.Battle && tick == 100) actor.TakeDamage(500, DamageFlags.IgnoreInvuln, null, null);
                    if (tick == 680)
                    {
                        if (mode == GameMode.Survival) { target.Health = 0; state.TeamDeaths[1] = state.PointGoal + 1; }
                        else if (mode == GameMode.PrimeHunter) { state.PrimeHunter = 1; state.Time[1] = state.TimeGoal; }
                        else state.TeamPoints[0] = state.PointGoal;
                        state.ModeState(scene);
                    }
                    scene.OnSimulationFrame();
                    if (tick % scenario.CaptureInterval == 0)
                    {
                        var point = FidelityOracle.Point(tick, ("roomIndex", roomIndex), ("mode", (int)mode),
                            ("matchState", (int)state.MatchState), ("matchTime4096", FidelityOracle.Normalize(state.MatchTime)),
                            ("prime", state.PrimeHunter), ("rng1", scene.Random.Rng1), ("rng2", scene.Random.Rng2));
                        for (int slot = 0; slot < 2; slot++)
                        {
                            string key = "player." + slot + ".";
                            point.Values.Add(key + "health", scene.Players.Items[slot].Health);
                            point.Values.Add(key + "kills", state.Kills[slot]); point.Values.Add(key + "deaths", state.Deaths[slot]);
                            point.Values.Add(key + "points", state.Points[slot]); point.Values.Add(key + "teamPoints", state.TeamPoints[slot]);
                            point.Values.Add(key + "time4096", FidelityOracle.Normalize(state.Time[slot]));
                            point.Values.Add(key + "captures", state.OctolithScores[slot]); point.Values.Add(key + "nodes", state.NodesCaptured[slot]);
                        }
                        if (objective != null) { point.Values.Add("flag.carrier", objective.Carrier?.SlotIndex ?? -1); point.Values.Add("flag.atBase", objective.AtBase ? 1 : 0); }
                        if (node != null) { point.Values.Add("node.owner", node.CurrentTeam); point.Values.Add("node.contested", node.Contested ? 1 : 0); point.Values.Add("node.progress4096", FidelityOracle.Normalize(node.Progress)); }
                        output.Add(point);
                    }
                }
            }
            if (tick != scenario.Ticks || Render.FrameTiming.DroppedSteps != 0 || Render.FrameTiming.Stalls != 0)
                throw new InvalidOperationException("Match oracle clock failed.");
            if (mode == GameMode.Capture && !output.Exists(p => p.Values["player.0.captures"] > 0)) throw new InvalidOperationException("No capture.");
            if (mode == GameMode.Nodes && !output.Exists(p => p.Values["player.0.nodes"] > 0)) throw new InvalidOperationException("No node capture.");
            if (mode == GameMode.PrimeHunter && (!output.Exists(p => p.Values["prime"] == 0) || !output.Exists(p => p.Values["prime"] == 1))) throw new InvalidOperationException("No Prime transfer.");
            if (!output.Exists(p => p.Values["matchState"] != output[0].Values["matchState"])) throw new InvalidOperationException("No match completion.");
            return output;
        }
        finally { Render.FrameTiming.Reset(); Render.FrameTiming.ResetDiagnostics(); MapAudit.ForceEveryone = force; scene?.DoCleanup(); Read.ClearCache(); }
    }
}
