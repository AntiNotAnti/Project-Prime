using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Repeatable asset-backed bot skill and performance workload. All participants
/// are real authority-driven bots, with identical difficulty and fixed roster
/// ordering between runs. Reports observations, never inferred skill guarantees.
/// </summary>
internal static class BotSkillBenchmark
{
    internal static int Run(string assetPath, string room, int difficulty,
        int bots, int frames, GameMode mode, string outputPath)
    {
        string assets = Path.GetFullPath(assetPath);
        string output = Path.GetFullPath(outputPath);
        if (!Directory.Exists(assets) || File.Exists(output)
            || !Directory.Exists(Path.GetDirectoryName(output)!))
        {
            Console.Error.WriteLine("[bot-skill] Existing AMHE1 assets and a fresh JSON output path are required.");
            return 1;
        }
        Paths.MphKey = "AMHE1";
        Paths.SetPath(Paths.MphKey, assets);
        if (!ServerSim.Available(out string reason))
        {
            Console.Error.WriteLine("[bot-skill] " + reason);
            return 1;
        }

        var sim = new ServerSim();
        try
        {
            if (!sim.Start(room, mode, 8, _ => { }, () => { }))
                throw new InvalidOperationException("Authority room did not load.");
            NetSession.ApplyMatchState(new MatchStatePacket
            {
                MatchId = 1, AuthorityEpoch = 1, RoomKey = room,
                Mode = (byte)mode
            }, false);

            bool team = mode is GameMode.BattleTeams or GameMode.NodesTeams
                or GameMode.HardpointTeams or GameMode.DefenderTeams;
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            roster.Count = (byte)bots; roster.ContainsBots = true;
            for (byte slot = 0; slot < bots; slot++)
            {
                roster.Slots[slot] = slot;
                roster.Generations[slot] = 1;
                roster.Teams[slot] = team ? (sbyte)(slot % 2) : (sbyte)-1;
                roster.Hunters[slot] = (byte)(slot % 7);
                roster.Names[slot] = "BENCH-BOT-" + slot;
                roster.Flags[slot] = 1;
                roster.BotLevels[slot] = (byte)difficulty;
            }
            NetSession.ApplyRoster(roster);
            NetSlotManager.Sync();
            GameState.PointGoal = 100000;
            GameState.MatchTime = 3600;

            const int warmupFrames = 180;
            for (int tick = 0; tick < warmupFrames; tick++) sim.Step();
            int[] startShots = GameState.ShotsFired.ToArray();
            int[] startHits = GameState.ShotsHit.ToArray();
            int[] startDamage = GameState.MatchDamageDealt.ToArray();
            int[] startTaken = GameState.MatchDamageTaken.ToArray();
            int[] startKills = GameState.Kills.ToArray();
            int[] startDeaths = GameState.Deaths.ToArray();
            int[] startNodes = GameState.NodesCaptured.ToArray();
            float[] startObjectiveSeconds = GameState.ObjectiveSeconds.ToArray();

            var previous = new Vector3[bots];
            var hadPosition = new bool[bots];
            var travel = new double[bots];
            var activeFrames = new int[bots];
            for (int tick = 0; tick < frames; tick++)
            {
                sim.Step();
                for (int slot = 0; slot < bots; slot++)
                {
                    PlayerEntity player = PlayerEntity.Players[slot];
                    if (!player.IsBot || !player.ModInPlay || player.Health <= 0)
                    {
                        hadPosition[slot] = false;
                        continue;
                    }
                    activeFrames[slot]++;
                    if (hadPosition[slot])
                    {
                        float displacement = (player.Position - previous[slot]).Length;
                        // Exclude spawn/teleporter discontinuities from travel.
                        if (float.IsFinite(displacement) && displacement <= 2f)
                            travel[slot] += displacement;
                    }
                    previous[slot] = player.Position;
                    hadPosition[slot] = true;
                }
            }

            if (sim.StepFailures != 0)
                throw new InvalidOperationException("Authority simulation threw "
                    + sim.StepFailures + " times.");

            var perBot = new List<object>(bots);
            int allShots = 0, allHits = 0, allKills = 0;
            for (int slot = 0; slot < bots; slot++)
            {
                int shots = Math.Max(0, GameState.ShotsFired[slot] - startShots[slot]);
                int hits = Math.Max(0, GameState.ShotsHit[slot] - startHits[slot]);
                int kills = GameState.Kills[slot] - startKills[slot];
                allShots += shots; allHits += hits; allKills += kills;
                perBot.Add(new
                {
                    slot,
                    hunter = PlayerEntity.Players[slot].Hunter.ToString(),
                    difficulty,
                    team = team ? slot % 2 : -1,
                    shotsFired = shots,
                    shotsHit = hits,
                    hitRatio = shots == 0 ? (double?)null : hits / (double)shots,
                    damageDealt = GameState.MatchDamageDealt[slot] - startDamage[slot],
                    damageTaken = GameState.MatchDamageTaken[slot] - startTaken[slot],
                    kills,
                    deaths = GameState.Deaths[slot] - startDeaths[slot],
                    nodesCaptured = GameState.NodesCaptured[slot] - startNodes[slot],
                    objectiveSeconds = GameState.ObjectiveSeconds[slot] - startObjectiveSeconds[slot],
                    activeFrames = activeFrames[slot],
                    travelDistanceExcludingTeleports = Math.Round(travel[slot], 3)
                });
            }

            var result = new
            {
                schema = 1,
                purpose = "Bot AI skill and authority performance observation, not a skill guarantee",
                room,
                mode = mode.ToString(),
                difficulty,
                bots,
                warmupFrames,
                measuredFrames = frames,
                gameVersion = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unidentified",
                aggregate = new
                {
                    shotsFired = allShots,
                    shotsHit = allHits,
                    hitRatio = allShots == 0 ? (double?)null : allHits / (double)allShots,
                    kills = allKills,
                    serverMeanMilliseconds = sim.Frames == 0 ? 0d : sim.StepSeconds / sim.Frames * 1000d,
                    serverP95Milliseconds = sim.StepPercentile(.95),
                    serverP99Milliseconds = sim.StepPercentile(.99),
                    serverWorstMilliseconds = sim.WorstStepSeconds * 1000d,
                    serverOverrunSteps = sim.OverrunSteps,
                    allocationsPerStep = sim.Frames == 0 ? 0d : sim.StepAllocatedBytes / (double)sim.Frames,
                    simulationFailures = sim.StepFailures
                },
                participants = perBot,
                limitations = "Same roster, map and tick budget but source RNG may differ between runs. " +
                    "Aim can be affected by target movement, cover, match mode and weapon mix. " +
                    "Shot hit ratio may exceed 1 when a fired source hits multiple targets. " +
                    "Compare several independent runs and objective-specific maps."
            };
            File.WriteAllText(output, JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("[bot-skill] Saved " + output);
            Console.WriteLine($"[bot-skill] mode={mode} difficulty={difficulty} bots={bots} " +
                $"frames={frames} shots={allShots} hits={allHits} kills={allKills} " +
                $"p99={sim.StepPercentile(.99):0.00}ms");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[bot-skill] " + ex);
            return 1;
        }
        finally
        {
            sim.Stop();
        }
    }
}
