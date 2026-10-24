using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Oddball;

/// <summary>Loads actual available maps; not a Windows, remote-network or balance certification.</summary>
internal static class OddballMapChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>(); int passed = 0, failed = 0;
        string? filter = Environment.GetEnvironmentVariable("ODDBALL_MAP_FILTER");
        bool bots = Environment.GetEnvironmentVariable("ODDBALL_MAP_BOTS") == "1";
        var rooms = ThumbnailGenerator.MultiplayerRooms().Where(r => filter == null || r == filter).Distinct().ToArray();
        foreach (string room in rooms)
        {
            var sim = new ServerSim();
            try
            {
                foreach (int size in new[] { 1, 2, 4, 8 })
                {
                    Require(MapModeCapabilities.Supports(room, GameMode.Oddball, MatchWorldProfile.Resolve(size), out string why, size), "capability " + size + ": " + why);
                    var meta = Metadata.RoomMetadata[room];
                    int layer = SceneSetup.GetMultiplayerEntityLayer(OddballMap.IsCombatHall(room) ? GameMode.Oddball : GameMode.Battle,
                        MatchWorldProfile.Resolve(size).EntityLayerPlayers, MatchWorldProfile.Resolve(size).Resources);
                    var raw = MapResourceRules.Resolve(meta, MatchWorldProfile.Resolve(size).Resources,
                        Read.GetEntities(meta.EntityPath!, layer, false, allowHook: true));
                    var overlay = OddballMap.Resolve(meta, raw);
                    Require(overlay.Select(e => e.EntityId).Distinct().Count() == overlay.Count, "unique overlay IDs");
                    Require(raw.Where(e => e.Type is not (EntityType.PlayerSpawn or EntityType.FlagBase or EntityType.OctolithFlag))
                        .All(e => overlay.Contains(e)), "existing pickups, geometry entities and triggers unchanged");
                }
                const int count = 8;
                var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Oddball, PointGoal = 100, TimeLimitSeconds = 300 };
                var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = count,
                    Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(count) };
                var roster = RosterPacket.Create(); roster.Count = count; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
                for (byte i = 0; i < count; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)(i % 7); roster.Teams[i] = (sbyte)(i % 2); roster.Names[i] = "MAP CHECK " + i; if (bots && i > 0) { roster.Flags[i] = 1; roster.BotLevels[i] = 1; roster.ContainsBots = true; } }
                Require(sim.Start(room, GameMode.Oddball, count, _ => { }, () => { }, roster, session), "actual native world loads");
                NetSlotManager.Sync();
                var a = PlayerEntity.Players[0]; var scene = a.OwningScene; scene.GameState.SpawnProtection = false;
                var flags = new List<OctolithFlagEntity>(); foreach (var f in scene.GetOctolithFlagEntities()) flags.Add(f);
                Require(flags.Count == 1, "exactly one skull"); var ball = flags[0];
                Require(OddballMap.TryFloor(scene, ball.BasePosition, out Vector3 actualFloor), "skull on safe collision floor");
                Require(Vector3.DistanceSquared(actualFloor, ball.BasePosition) < .05f, "reset anchor agrees with actual floor");
                var starts = new List<PlayerSpawnEntity>(); foreach (var s in scene.GetPlayerSpawnEntities()) if (s.IsActive) starts.Add(s);
                Require(starts.Count(s => s.Data.TeamIndex == 0) >= 4 && starts.Count(s => s.Data.TeamIndex == 1) >= 4, "four starting lanes per team");
                int nativeTicks = bots ? 3600 : 240;
                for (int i = 0; i < nativeTicks; i++) sim.Step();
                int botPickups = 0, botHoldTicks = 0;
                for (int i = 1; i < 8; i++) { botPickups += scene.GameState.ObjectivePickups[i]; botHoldTicks += scene.GameState.Oddball!.HoldTicks[i]; }
                if (bots && botPickups == 0)
                {
                    Console.WriteLine($"BOT ROUTE DIAGNOSTICS {room} skull={ball.BasePosition}, closest={ball.BaseClosestNode?.Id}/{ball.BaseClosestNode?.Position}");
                    foreach (var bot in PlayerEntity.Players.Take(8)) Console.WriteLine($"slot={bot.SlotIndex} bot={bot.IsBot} hp={bot.Health} pos={bot.Position} alt={bot.IsAltForm} nearest={bot.ClosestNode?.Id} speed={bot.Speed}");
                }
                if (bots) Require(botPickups > 0 && botHoldTicks > 0, "real bots navigate to skull and score within 60 seconds");
                Require(sim.StepFailures == 0, "full eight-slot world simulation does not throw");
                Require(PlayerEntity.Players.Take(8).All(p => OddballMap.Finite(p.Position)), "all player positions finite");
                scene.GameState.Oddball = new OddballMatch();
                for (int i = 0; i < 180; i++) scene.GameState.Oddball.Step(-1, -1);
                ball.OddballReset(); a.LoadFlags |= LoadFlags.Active; a.TeamIndex = 0;
                a.Spawn(ball.BasePosition, Vector3.UnitZ, Vector3.UnitY, a.NodeRef, true);
                Require(ball.OddballTryPickup(a, false), "pickup works on chosen map floor");
                int clock = scene.GameState.Oddball.RemainingTicks;
                for (int i = 0; i < 60; i++) scene.GameState.Oddball.Tick(scene);
                Require(scene.GameState.Oddball.Score(0) == 1 && scene.GameState.Oddball.RemainingTicks == clock, "one-second scoring and paused clock");
                ball.OddballDrop(); Require(ball.Carrier == null, "drop releases carrier");
                ball.Position = new Vector3(ball.BasePosition.X, scene.Room!.Meta.KillHeight - 10, ball.BasePosition.Z);
                ball.Process(); Require(ball.Position == ball.BasePosition.AddY(1.25f), "out-of-bounds skull returns to this map's anchor");
                ball.OddballReset(); Require(ball.Carrier == null && !a.ModOddballCarrying, "reset clears ownership");
                reports.Add(new { room, status = "PASS", profiles = new[] { 1, 2, 4, 8 }, players = 8,
                    skull = new[] { ball.BasePosition.X, ball.BasePosition.Y, ball.BasePosition.Z }, starts = starts.Count,
                    nativeTicks, botPickups, botHoldTicks, navigation = scene.Room.NodeData != null });
                Console.WriteLine($"MAP PASS {room}: skull={ball.BasePosition}, starts={starts.Count}"); passed++;
            }
            catch (Exception ex)
            {
                reports.Add(new { room, status = "FAIL", error = ex.ToString() });
                Console.WriteLine($"MAP FAIL {room}: {ex}"); failed++;
            }
            finally { sim.Stop(); }
            File.WriteAllText(Path.Combine(output, "map-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"ODDBALL MAP CHECK: {passed} passed, {failed} failed, {rooms.Length} available maps.");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
    private static void Require(bool ok, string description)
    { if (!ok) throw new InvalidOperationException(description); }
}
