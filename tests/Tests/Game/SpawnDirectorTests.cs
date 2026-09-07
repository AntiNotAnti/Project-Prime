using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Match baseline globals")]
    public sealed class SpawnDirectorTests
    {
        [Fact]
        public void EnhancedSelectionUsesRealSpawnsAndRecordsDiagnostics()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            Assert.True(spawns.Count >= 10);

            PlayerEntity requester = PlayerEntity.Players[0];
            requester.TeamIndex = -1;
            const uint seed = 0x51A7;
            scene.SpawnDirector.Reset(seed);
            PlayerSpawnEntity? selected = scene.SpawnDirector.Select(requester);

            Assert.NotNull(selected);
            Assert.True(scene.SpawnDirector.LastSelection.HasValue);
            SpawnCandidate diagnostics = scene.SpawnDirector.LastSelection!.Value;
            Assert.Equal(selected!.Id, diagnostics.EntityId);
            Assert.True(Single.IsFinite(diagnostics.Score));
            Assert.True(Single.IsFinite(diagnostics.NearestEnemyDistanceSquared));
            Assert.Equal(1, scene.SpawnDirector.SpawnHistoryCount);
            Assert.True(selected.Cooldown > 0);
            Assert.NotEqual(seed, scene.SpawnDirector.RandomState);
        }

        [Fact]
        public void TeamSelectionRejectsOppositeTeamAndReportsNoEligibleRealSpawns()
        {
            using var fixture = Open(MatchMode.TeamBattle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            PlayerEntity requester = PlayerEntity.Players[0];
            requester.TeamIndex = 0;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            Assert.Contains(spawns, spawn => spawn.IsActive && spawn.Data.TeamIndex == 1);
            List<PlayerSpawnEntity> eligible = spawns.Where(spawn => spawn.IsActive
                && (spawn.Data.TeamIndex == -1 || spawn.Data.TeamIndex == requester.TeamIndex)).ToList();
            Assert.NotEmpty(eligible);

            scene.SpawnDirector.Reset(17);
            PlayerSpawnEntity? selected = scene.SpawnDirector.Select(requester);
            Assert.NotNull(selected);
            Assert.NotEqual((sbyte)1, selected!.Data.TeamIndex);

            // Leave only the opposite-team points active. The cooldown fallback
            // may relax cooldown, but it must not relax the hard team filter.
            foreach (PlayerSpawnEntity spawn in eligible)
            {
                spawn.HandleMessage(new MessageInfo(Message.SetActive, spawn, spawn, 0, 0,
                    scene.FrameCount, scene.FrameCount));
            }
            scene.SpawnDirector.Reset(17);
            Assert.Null(scene.SpawnDirector.Select(requester));
            Assert.False(scene.SpawnDirector.LastSelection.HasValue);
        }

        [Fact]
        public void RealRoomDiagnosticsExposeCrowdingAndOcclusion()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            PlayerEntity requester = PlayerEntity.Players[0];
            requester.ServerActivate(0x5100, Hunter.Samus, -1);
            scene.SpawnDirector.Reset(31);
            foreach (PlayerSpawnEntity spawn in spawns) { spawn.Cooldown = 0; }
            PlayerSpawnEntity target = spawns[0];
            SpawnCandidate empty = scene.SpawnDirector.Evaluate(target, requester);

            for (int slot = 1; slot < PlayerEntity.SlotCapacity; slot++)
            {
                PlayerEntity enemy = PlayerEntity.Players[slot];
                enemy.ServerActivate((ulong)(0x5100 + slot), (Hunter)(slot % 7), -1);
                enemy.Position = target.Position + new Vector3((slot % 3) * 0.5f, 0, (slot % 4) * 0.5f);
                enemy.PrevPosition = enemy.Position;
                enemy.Health = 100;
                enemy.LoadFlags |= LoadFlags.Active;
            }
            scene.SpawnDirector.Reset(31);
            SpawnCandidate crowded = scene.SpawnDirector.Evaluate(target, requester);
            Assert.True(crowded.NearbyEnemies >= PlayerEntity.SlotCapacity - 1);
            Assert.True(crowded.Score < empty.Score);

            for (int slot = 2; slot < PlayerEntity.SlotCapacity; slot++)
            {
                PlayerEntity.Players[slot].ServerDeactivate();
            }
            PlayerEntity occlusionEnemy = PlayerEntity.Players[1];
            bool foundOccluded = false;
            foreach (PlayerSpawnEntity candidate in spawns)
            {
                foreach (PlayerSpawnEntity enemyPoint in spawns)
                {
                    if (candidate == enemyPoint) { continue; }
                    occlusionEnemy.Position = enemyPoint.Position;
                    occlusionEnemy.PrevPosition = enemyPoint.Position;
                    SpawnCandidate diagnostics = scene.SpawnDirector.Evaluate(candidate, requester);
                    if (diagnostics.NearestEnemyDistanceSquared > 0 && diagnostics.VisibleEnemies == 0)
                    {
                        foundOccluded = true;
                        break;
                    }
                }
                if (foundOccluded) { break; }
            }
            Assert.True(foundOccluded, "The AMHE1 MP1 collision layout did not expose an occluded spawn pair.");
        }

        [Fact]
        public void HistoryIsBoundedExpiresAtSimulationTimeAndResetClearsIt()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            PlayerEntity requester = PlayerEntity.Players[0];
            requester.TeamIndex = -1;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            PlayerSpawnEntity target = spawns[0];
            const uint seed = 0xC0FFEE;
            scene.SpawnDirector.Reset(seed);

            for (int i = 0; i < 70; i++) { scene.SpawnDirector.RecordDeath(target.Position); }
            Assert.Equal(64, scene.SpawnDirector.DeathHistoryCount);

            foreach (PlayerSpawnEntity spawn in spawns)
            {
                spawn.Cooldown = spawn == target ? (ushort)0 : (ushort)1000;
            }
            for (int i = 0; i < 140; i++)
            {
                PlayerSpawnEntity? selected = scene.SpawnDirector.Select(requester);
                Assert.Same(target, selected);
                target.Cooldown = 0;
            }
            Assert.Equal(128, scene.SpawnDirector.SpawnHistoryCount);

            scene.SpawnDirector.Reset(seed);
            Assert.Equal(seed, scene.SpawnDirector.RandomState);
            Assert.Equal(0, scene.SpawnDirector.DeathHistoryCount);
            Assert.Equal(0, scene.SpawnDirector.SpawnHistoryCount);
            Assert.False(scene.SpawnDirector.LastSelection.HasValue);

            scene.SpawnDirector.RecordDeath(target.Position);
            SpawnCandidate immediate = scene.SpawnDirector.Evaluate(target, requester);
            Assert.True(immediate.DeathPenalty > 0);
            scene.Match.Phase = MatchPhase.Playing;
            for (int frame = 0; frame < 10 * SimTicks.Hz; frame++)
            {
                scene.StepHeadlessFrame(advanceMatch: false);
            }
            SpawnCandidate expired = scene.SpawnDirector.Evaluate(target, requester);
            Assert.Equal(0, expired.DeathPenalty);
        }

        [Fact]
        public void EqualRealSpawnScoresUseOnlyTheSeededDirectorState()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            PlayerEntity requester = PlayerEntity.Players[0];
            requester.TeamIndex = -1;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            foreach (PlayerSpawnEntity spawn in spawns) { spawn.Cooldown = 0; }
            const uint seed = 42;

            scene.SpawnDirector.Reset(seed);
            PlayerSpawnEntity first = Assert.IsType<PlayerSpawnEntity>(scene.SpawnDirector.Select(requester));
            uint firstState = scene.SpawnDirector.RandomState;
            scene.SpawnDirector.Reset(seed);
            foreach (PlayerSpawnEntity spawn in spawns) { spawn.Cooldown = 0; }
            PlayerSpawnEntity second = Assert.IsType<PlayerSpawnEntity>(scene.SpawnDirector.Select(requester));

            Assert.Equal(first.Id, second.Id);
            Assert.NotEqual(seed, firstState);
            Assert.Equal(firstState, scene.SpawnDirector.RandomState);
        }

        private static List<PlayerSpawnEntity> Spawns(Scene scene)
        {
            var result = new List<PlayerSpawnEntity>();
            foreach (PlayerSpawnEntity spawn in scene.GetPlayerSpawnEntities()) { result.Add(spawn); }
            return result;
        }

        private static SimulationFixture Open(MatchMode mode, SpawnPolicy policy)
        {
            string data = FindAmhe1();
            return new SimulationFixture(data, mode, policy);
        }

        private static string FindAmhe1()
        {
            string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
            string[] starts = configured is null
                ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                : new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (string start in starts)
            {
                DirectoryInfo? directory = new(Path.GetFullPath(start));
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, "AMHE1");
                    if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                        && Directory.Exists(Path.Combine(candidate, "models"))
                        && Directory.Exists(Path.Combine(candidate, "levels")))
                    {
                        return candidate;
                    }
                    directory = directory.Parent;
                }
            }
            throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
        }

        private sealed class SimulationFixture : IDisposable
        {
            private readonly IDisposable _content;
            private readonly ServerSimulation _simulation;

            public Scene Scene => _simulation.Scene;

            public SimulationFixture(string data, MatchMode mode, SpawnPolicy policy)
            {
                _content = ServerContent.PreserveContext("AMHE1");
                try
                {
                    ServerContent.Open(data, "AMHE1");
                    var rules = new MatchRules(mode, "MP1 SANCTORUS", spawnPolicy: policy);
                    _simulation = new ServerSimulation(rules);
                    _simulation.Scene.Match.Phase = MatchPhase.Playing;
                    _simulation.Scene.StepHeadlessFrame(advanceMatch: false);
                }
                catch
                {
                    _content.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                _simulation.Dispose();
                _content.Dispose();
            }
        }
    }
}
