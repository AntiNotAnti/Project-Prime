using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Match baseline globals")]
    [Trait("RequiresGameContent", "true")]
    public sealed class SpawnDirectorTests
    {
        [Fact]
        public void EnhancedSelectionUsesRealSpawnsAndRecordsDiagnostics()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            Assert.True(spawns.Count >= 10);

            PlayerEntity requester = scene.Players[0];
            requester.TeamIndex = -1;
            const uint seed = 0x51A7;
            scene.SpawnDirector.Reset(seed);
            PlayerSpawnEntity? selected = scene.SpawnDirector.Select(requester);

            Assert.NotNull(selected);
            Assert.True(scene.SpawnDirector.LastSelection.HasValue);
            SpawnCandidate diagnostics = scene.SpawnDirector.LastSelection!.Value;
            Assert.Equal(selected!.Id, diagnostics.EntityId);
            Assert.True(scene.SpawnDirector.TryGetLastSelection(requester.SlotIndex,
                selected.Position.AddY(1), scene.FrameCount, out SpawnCandidate correlated));
            Assert.Equal(diagnostics, correlated);
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
            PlayerEntity requester = scene.Players[0];
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
            PlayerEntity requester = scene.Players[0];
            requester.ServerActivate(0x5100, Hunter.Samus, -1);
            scene.SpawnDirector.Reset(31);
            foreach (PlayerSpawnEntity spawn in spawns) { spawn.Cooldown = 0; }
            PlayerSpawnEntity target = spawns[0];
            SpawnCandidate empty = scene.SpawnDirector.Evaluate(target, requester);

            for (int slot = 1; slot < PlayerEntity.SlotCapacity; slot++)
            {
                PlayerEntity enemy = scene.Players[slot];
                enemy.ServerActivate((ulong)(0x5100 + slot),
                    PlayableHunterCatalog.FromIndex(slot % PlayableHunterCatalog.Count), -1);
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
                scene.Players[slot].ServerDeactivate();
            }
            PlayerEntity occlusionEnemy = scene.Players[1];
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
        public void LiveHostileBeamAndBombMarkImmediateSpawnHazards()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            PlayerEntity requester = scene.Players[0];
            requester.ServerActivate(0x7100, Hunter.Samus, -1);
            PlayerEntity enemy = scene.Players[1];
            enemy.ServerActivate(0x7101, Hunter.Kanden, -1);
            PlayerSpawnEntity target = Spawns(scene).First(spawn => spawn.IsActive);
            enemy.Position = target.Position + Vector3.UnitX * 40;
            enemy.PrevPosition = enemy.Position;

            Vector3 body = target.Position.AddY(1.5f);
            var beam = new BeamProjectileEntity(scene)
            {
                Owner = enemy,
                BackPosition = body - Vector3.UnitZ,
                Position = body + Vector3.UnitZ,
                CylinderRadius = 0.25f,
                Lifespan = 1
            };
            scene.AddEntity(beam);
            SpawnCandidate beamDanger = scene.SpawnDirector.Evaluate(target, requester);
            Assert.True(beamDanger.ImmediateHazard);
            Assert.True(beamDanger.HazardPenalty >= 2500);

            scene.RemoveEntity(beam);
            BombEntity bomb = Assert.IsType<BombEntity>(BombEntity.Spawn(enemy,
                Matrix4.CreateTranslation(body), scene));
            SpawnCandidate bombDanger = scene.SpawnDirector.Evaluate(target, requester);
            Assert.True(bombDanger.ImmediateHazard);
            Assert.True(bombDanger.HazardPenalty >= 3000);
        }

        [Fact]
        public void SameTickReservationsAreDeterministicAndSpatiallyBounded()
        {
            var history = new SpawnDangerHistory();
            Vector3 origin = new(10, 2, -4);
            history.RecordSpawn(1, slot: 0, team: 2, origin, tick: 99);

            float teammate = history.ReservationDanger(origin, team: 2, tick: 99);
            float opponent = history.ReservationDanger(origin, team: 1, tick: 99);
            Assert.Equal(0.75f, teammate);
            Assert.Equal(1, opponent);
            Assert.Equal(0, history.ReservationDanger(origin + Vector3.UnitX * 4,
                team: 2, tick: 99));
            Assert.Equal(0, history.ReservationDanger(origin, team: 2, tick: 100));
        }

        [Fact]
        public void HistoryIsBoundedExpiresAtSimulationTimeAndResetClearsIt()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Enhanced);
            Scene scene = fixture.Scene;
            PlayerEntity requester = scene.Players[0];
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
            PlayerEntity requester = scene.Players[0];
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

        [Theory]
        [InlineData(MatchMode.Battle)]
        [InlineData(MatchMode.Capture)]
        public void ClassicMatchesFrozenSelectorAcrossFrameCrowdingCooldownAndTeamFallback(MatchMode mode)
        {
            using var fixture = Open(mode, SpawnPolicy.Classic);
            Scene scene = fixture.Scene;
            List<PlayerSpawnEntity> spawns = Spawns(scene);
            PlayerSpawnEntity template = spawns.First(p => p.IsActive
                && SpawnGeometry.IsSafe(scene, p));
            // Extend the real authored list beyond 25 to exercise its exact legacy bound.
            while (spawns.Count < 30)
            {
                byte[] bytes = new byte[Marshal.SizeOf<PlayerSpawnEntityData>()];
                PlayerSpawnEntityData data = template.Data;
                MemoryMarshal.Write(bytes, in data);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), (short)(30000 + spawns.Count));
                bytes[40] = 0; bytes[41] = 1; bytes[42] = 1;
                var extra = new PlayerSpawnEntity(MemoryMarshal.Read<PlayerSpawnEntityData>(bytes), "", scene);
                // Synthetic entities do not resolve a node name during initialization;
                // retain the cloned marker's authored room node so the geometry gate
                // exercises Classic's legacy fallback rather than rejecting test data.
                scene.AddEntity(extra);
                extra.NodeRef = template.NodeRef;
                spawns.Add(extra);
            }
            PlayerEntity requester = scene.Players[0];
            requester.TeamIndex = 0;
            scene.ResetFrameCount();
            const uint seed = 0x12345678;
            for (int frame = 0; frame < 6; frame++)
            {
                for (int scenario = 0; scenario < 5; scenario++)
                {
                    foreach (PlayerSpawnEntity point in spawns)
                    {
                        SetActive(scene, point, true);
                        point.Cooldown = scenario == 2 ? (ushort)500 : (ushort)0;
                    }
                    for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
                    {
                        PlayerEntity player = scene.Players[slot];
                        player.Health = scenario == 1 ? 99 : 0;
                        player.Position = spawns[slot % spawns.Count].Position;
                    }
                    if (scenario == 3)
                    {
                        foreach (PlayerSpawnEntity point in spawns) SetActive(scene, point, false);
                        // Opposite team, outside the first 25 and on cooldown: Classic still falls back.
                        SetActive(scene, spawns[27], true);
                        spawns[27].Cooldown = 500;
                    }
                    if (scenario == 4)
                        foreach (PlayerSpawnEntity point in spawns) SetActive(scene, point, false);
                    PlayerSpawnEntity? expected = FrozenClassic(scene, requester);
                    uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
                    scene.SpawnDirector.Reset(seed);
                    PlayerSpawnEntity? actual = scene.SpawnDirector.Select(requester);
                    Assert.True(ReferenceEquals(expected, actual),
                        $"Classic selection diverged at frame {frame}, scenario {scenario}; "
                        + $"expected geometry safe: {expected == null || SpawnGeometry.IsSafe(scene, expected)}.");
                    Assert.Equal(seed, scene.SpawnDirector.RandomState);
                    Assert.Equal(rng1, scene.Random.Rng1); Assert.Equal(rng2, scene.Random.Rng2);
                    if (actual != null) Assert.Equal((ushort)4, actual.Cooldown);
                    if (scenario == 3) Assert.Same(spawns[27], actual);
                }
                foreach (PlayerEntity player in scene.GetPlayerEntities()) player.Health = 0;
                fixture.Scene.StepHeadlessFrame(advanceMatch: false);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActualBeamAndNoAmmoFireCancelProtectionOnlyWhenEnabledAndSpawned(bool cancel)
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Samus, cancel, alt: false);
            var observer = new FireObserver(fixture.Scene.Services);
            fixture.Scene.Services = observer;
            player.ModSetAmmo(0, 10);
            player.ModSetWeapon(BeamType.Missile);
            Assert.Equal(BeamType.Missile, player.CurrentWeapon);
            player.ModSetAmmo(0, 0);
            AssertProtected(player, true);
            // Normal ProcessPlayer auto-equips an affordable weapon before input.
            // Invoke the actual firing branch to exercise its NoSpawn return without
            // replacing weapon math or mutating the protection timer.
            var fire = typeof(PlayerEntity).GetMethod("TryFireWeapon",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.False((bool)fire.Invoke(player, null)!);
            Assert.Equal(1, observer.Attempts); // NoteFired is reached even on BeamResultFlags.NoSpawn.
            Assert.Equal(BeamType.Missile, player.CurrentWeapon);
            Assert.Equal(0, player.ModAmmo.Missiles);
            AssertProtected(player, true);
            player.ModSetAmmo(0, 10);
            fixture.Input(player, InputButtons.None);
            fixture.Input(player, InputButtons.Shoot, InputButtons.Shoot);
            Assert.Equal(2, observer.Attempts);
            Assert.Equal((ushort)0, player.TimeSinceShot);
            Assert.True(player.ModAmmo.Missiles < 10);
            AssertProtected(player, !cancel);
        }

        [Fact]
        public void ActualBombAllocationFailureKeepsProtectionAndSuccessfulDropClearsIt()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Samus, true, alt: true);
            var reserved = new List<BombEntity>();
            while (fixture.Scene.InitBomb() is BombEntity unused) reserved.Add(unused);
            Assert.NotEmpty(reserved);
            fixture.Input(player, InputButtons.AltAttack, InputButtons.AltAttack);
            Assert.Empty(Bombs(fixture.Scene));
            AssertProtected(player, true);
            foreach (BombEntity unused in reserved) fixture.Scene.UnlinkBomb(unused);
            fixture.Input(player, InputButtons.None);
            fixture.Input(player, InputButtons.AltAttack, InputButtons.AltAttack);
            Assert.Single(Bombs(fixture.Scene));
            AssertProtected(player, false);
        }

        [Theory]
        [InlineData(Hunter.Spire)]
        [InlineData(Hunter.Trace)]
        [InlineData(Hunter.Weavel)]
        public void AcceptedAltAttacksClearProtection(Hunter hunter)
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, hunter, true, alt: true);
            AssertProtected(player, true);
            fixture.Input(player, InputButtons.AltAttack, InputButtons.AltAttack);
            Assert.True(player.Flags2.TestFlag(PlayerFlags2.AltAttack));
            AssertProtected(player, false);
        }

        [Fact]
        public void NoxusWindupKeepsProtectionUntilTheActualAttackBegins()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Noxus, true, alt: true);
            int startup = SimTicks.From30HzFrames(player.Values.AltAttackStartup);
            Assert.True(startup < SimTicks.From30HzFrames(player.Values.SpawnInvulnerability));
            fixture.Input(player, InputButtons.AltAttack, InputButtons.AltAttack);
            Assert.False(player.Flags2.TestFlag(PlayerFlags2.AltAttack));
            AssertProtected(player, true);
            for (int tick = 1; tick < startup - 1; tick++) fixture.Input(player, InputButtons.AltAttack);
            Assert.False(player.Flags2.TestFlag(PlayerFlags2.AltAttack));
            AssertProtected(player, true);
            fixture.Input(player, InputButtons.AltAttack);
            Assert.True(player.Flags2.TestFlag(PlayerFlags2.AltAttack));
            AssertProtected(player, false);
        }

        [Fact]
        public void SamusBoostChargeKeepsProtectionAndReleasedBoostClearsIt()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Samus, true, alt: true);
            int charge = SimTicks.From30HzFrames(player.Values.BoostChargeMin) + 2;
            for (int tick = 0; tick < charge; tick++)
                fixture.Input(player, InputButtons.Boost, tick == 0 ? InputButtons.Boost : InputButtons.None);
            Assert.False(player.Flags1.TestFlag(PlayerFlags1.Boosting));
            AssertProtected(player, true);
            fixture.Input(player, InputButtons.None);
            AssertProtected(player, false); // low-speed collision can clear Boosting again during this same tick.
        }

        [Fact]
        public void SamusFlickUsesFullChargeCooldownAndIgnoresVelocityBoostingFlag()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Samus, true, alt: true);
            Assert.True(BoostIntent.TryCreateFlick(new Vector2(0, -1),
                out BoostIntent flick));
            // Build a partial held charge, then prove a simultaneous flick
            // takes precedence and uses the compatibility full charge.
            fixture.Input(player, InputButtons.Boost);
            fixture.Input(player, InputButtons.Boost);
            fixture.Input(player, InputButtons.Boost, boostIntent: flick);
            InputCommand captured = player.CaptureNetworkInput(99, 77);

            ushort cooldown = (ushort)SimTicks.From30HzFrames(
                player.Values.AltAttackCooldown);
            Assert.Equal(flick, captured.BoostRequest);
            Assert.Equal(cooldown, player._altAttackCooldown);
            Assert.Equal(player.Values.AltAttackDamage, BoostDamage(player));

            fixture.Input(player, InputButtons.None, boostIntent: flick);
            Assert.Equal(cooldown - 1, player._altAttackCooldown);

            // Boosting is velocity-lived and deliberately not a flick gate.
            player._altAttackCooldown = 0;
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!
                .SetValue(player, player.Flags1 | PlayerFlags1.Boosting);
            fixture.Input(player, InputButtons.None, boostIntent: flick);
            Assert.Equal(cooldown, player._altAttackCooldown);
        }

        [Fact]
        public void IllegalFlickIsConsumedInsteadOfDeferredAcrossBipedAndThaw()
        {
            Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitX, out BoostIntent flick));
            using (var bipedFixture = Open(MatchMode.Battle, SpawnPolicy.Classic))
            {
                PlayerEntity player = Activate(bipedFixture, Hunter.Samus, true, alt: false);
                bipedFixture.Input(player, InputButtons.None, boostIntent: flick);
                Assert.Equal(flick, player.Input.ConsumedBoostIntent);
                Assert.Equal(0, player._altAttackCooldown);
                bipedFixture.Input(player, InputButtons.None);
                Assert.Equal(BoostIntent.None, player.Input.ConsumedBoostIntent);
                Assert.Equal(0, player._altAttackCooldown);
            }

            using (var frozenFixture = Open(MatchMode.Battle, SpawnPolicy.Classic))
            {
                PlayerEntity player = Activate(frozenFixture, Hunter.Samus, true, alt: true);
                SetPrivateUShort(player, "_frozenTimer", 2);
                frozenFixture.Input(player, InputButtons.None, boostIntent: flick);
                Assert.Equal(flick, player.Input.ConsumedBoostIntent);
                Assert.Equal(0, player._altAttackCooldown);
                frozenFixture.Input(player, InputButtons.None);
                Assert.Equal(BoostIntent.None, player.Input.ConsumedBoostIntent);
                Assert.Equal(0, player._altAttackCooldown);
            }
        }

        [Fact]
        public void ChargedBoostMinimumBoundaryAndMaximumCapRemainUnchanged()
        {
            using (var exactFixture = Open(MatchMode.Battle, SpawnPolicy.Classic))
            {
                PlayerEntity player = Activate(exactFixture, Hunter.Samus, true, alt: true);
                int minimum = SimTicks.From30HzFrames(player.Values.BoostChargeMin);
                for (int tick = 0; tick < minimum; tick++)
                    exactFixture.Input(player, InputButtons.Boost);
                Assert.Equal(minimum, BoostCharge(player));
                exactFixture.Input(player, InputButtons.None);
                Assert.Equal(0, BoostCharge(player));
                Assert.Equal(0, player._altAttackCooldown);
            }

            using (var aboveFixture = Open(MatchMode.Battle, SpawnPolicy.Classic))
            {
                PlayerEntity player = Activate(aboveFixture, Hunter.Samus, true, alt: true);
                int minimum = SimTicks.From30HzFrames(player.Values.BoostChargeMin);
                for (int tick = 0; tick <= minimum; tick++)
                    aboveFixture.Input(player, InputButtons.Boost);
                aboveFixture.Input(player, InputButtons.None);
                Assert.True(player._altAttackCooldown > 0);
            }

            using (var cappedFixture = Open(MatchMode.Battle, SpawnPolicy.Classic))
            {
                PlayerEntity player = Activate(cappedFixture, Hunter.Samus, true, alt: true);
                int maximum = SimTicks.From30HzFrames(player.Values.BoostChargeMax);
                for (int tick = 0; tick < maximum + 5; tick++)
                    cappedFixture.Input(player, InputButtons.Boost);
                Assert.Equal(maximum, BoostCharge(player));
            }
        }

        [Fact]
        public void SyluxSuccessfulBombDropClearsProtection()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Sylux, true, alt: true);
            AssertProtected(player, true);
            fixture.Input(player, InputButtons.AltAttack, InputButtons.AltAttack);
            Assert.Single(Bombs(fixture.Scene));
            Assert.Equal(1, player.SyluxBombCount);
            AssertProtected(player, false);
        }

        [Fact]
        public void SyluxFullBombInventoryRejectsAdditionalAttackAndKeepsProtection()
        {
            using var fixture = Open(MatchMode.Battle, SpawnPolicy.Classic);
            PlayerEntity player = Activate(fixture, Hunter.Sylux, true, alt: true);
            var bombs = new List<BombEntity>();
            for (int index = 0; index < 3; index++)
            {
                Matrix4 transform = Matrix4.CreateTranslation(player.Position + new Vector3(index * 0.25f, 0, 0));
                BombEntity bomb = Assert.IsType<BombEntity>(BombEntity.Spawn(player, transform, fixture.Scene));
                Assert.True(player.TryRegisterLockjawBomb(bomb));
                bombs.Add(bomb);
            }
            AssertProtected(player, true);
            // The inventory refresh sets bomb ammo to zero before input. The legacy
            // three-bomb detonation branch inside SpawnBomb is therefore not reachable
            // through this fourth input; autonomous linking is a separate bomb pass.
            fixture.Input(player, InputButtons.AltAttack, InputButtons.AltAttack, playerOnly: true);
            Assert.All(bombs, bomb => Assert.True(bomb.Countdown > 0));
            AssertProtected(player, true);
        }

        private static PlayerEntity Activate(SimulationFixture fixture, Hunter hunter, bool cancel, bool alt)
        {
            fixture.Scene.Match.ApplyRules(fixture.Scene.Match.Rules.With(cancelSpawnProtectionOnOffensiveAction: cancel));
            PlayerEntity player = fixture.Scene.Players[0];
            player.ServerActivate(0xABCD, hunter, -1);
            if (alt) player.ModForceForm(true); // real form transition setup; the tested attack still uses normalized input.
            Assert.Equal(alt, player.IsAltForm);
            return player;
        }

        private static void AssertProtected(PlayerEntity player, bool expected)
        {
            int health = player.Health;
            player.TakeDamage(1, DamageFlags.NoSfx | DamageFlags.NoDmgInvuln, null, null);
            Assert.Equal(expected ? health : health - 1, player.Health);
        }

        private static ushort BoostCharge(PlayerEntity player)
            => (ushort)typeof(PlayerEntity).GetField("_boostCharge",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.GetValue(player)!;

        private static ushort BoostDamage(PlayerEntity player)
            => (ushort)typeof(PlayerEntity).GetField("_boostDamage",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.GetValue(player)!;

        private static void SetPrivateUShort(PlayerEntity player, string field, ushort value)
            => typeof(PlayerEntity).GetField(field,
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.SetValue(player, value);

        private static List<BombEntity> Bombs(Scene scene)
        {
            var result = new List<BombEntity>();
            foreach (BombEntity bomb in scene.GetBombEntities()) result.Add(bomb);
            return result;
        }

        private static void SetActive(Scene scene, PlayerSpawnEntity point, bool active)
            => point.HandleMessage(new MessageInfo(Message.SetActive, point, point, active ? 1 : 0, 0,
                scene.FrameCount, scene.FrameCount));

        // Frozen pre-director scoring from b31bc57 plus the intentional hard
        // geometry gate. Classic keeps its selection order and RNG behavior,
        // but an invalid volume can no longer create an unusable life.
        private static PlayerSpawnEntity? FrozenClassic(Scene scene, PlayerEntity requester)
        {
            int limit = 0;
            var valid = new List<PlayerSpawnEntity>();
            PlayerSpawnEntity? best = null;
            float bestDistance = 0;
            foreach (PlayerSpawnEntity candidate in scene.GetPlayerSpawnEntities())
            {
                if (limit >= 25) break;
                if (!candidate.IsActive || candidate.Cooldown != 0
                    || scene.FrameCount == 0 && candidate.Availability
                    || !SpawnGeometry.IsSafe(scene, candidate))
                { limit++; continue; }
                if (scene.Match.Rules.Mode == MatchMode.Capture && candidate.Data.TeamIndex != -1
                    && candidate.Data.TeamIndex != requester.TeamIndex)
                { limit++; continue; }
                float minimum = 100;
                foreach (PlayerEntity player in scene.GetPlayerEntities())
                    if (player.Health > 0)
                    {
                        Vector3 between = candidate.Position - player.Position;
                        float distance = Vector3.Dot(between, between);
                        if (distance < minimum) minimum = distance;
                    }
                if (minimum >= 100) valid.Add(candidate);
                else if (minimum > bestDistance) { bestDistance = minimum; best = candidate; }
                limit++;
            }
            PlayerSpawnEntity? chosen = valid.Count > 0 ? valid[(int)(scene.FrameCount % (ulong)valid.Count)] : best;
            if (chosen == null)
                foreach (PlayerSpawnEntity fallback in scene.GetPlayerSpawnEntities())
                    if (fallback.IsActive && SpawnGeometry.IsSafe(scene, fallback))
                    { chosen = fallback; break; }
            return chosen;
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

        private sealed class FireObserver : ISceneServices
        {
            private readonly ISceneServices _inner;
            public int Attempts { get; private set; }
            public FireObserver(ISceneServices inner) => _inner = inner;
            public ICombatAuthority? Combat => _inner.Combat;
            public bool ShouldLeaveAfterMatch => _inner.ShouldLeaveAfterMatch;
            public bool KeepSlotAlive(PlayerEntity player) => _inner.KeepSlotAlive(player);
            public void NoteFired(PlayerEntity shooter, Vector3 shot, Vector3 aim)
            {
                Attempts++;
                _inner.NoteFired(shooter, shot, aim);
            }
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

            public void Input(PlayerEntity player, InputButtons held,
                InputButtons pressed = InputButtons.None, bool playerOnly = false,
                BoostIntent boostIntent = default)
            {
                uint tick = unchecked((uint)Scene.FrameCount);
                _simulation.Combat.BeginTick(tick);
                player.CaptureServerState();
                InputCommand command = boostIntent.Activation == BoostActivation.None
                    ? new InputCommand(tick, tick, tick, held, pressed,
                        player.FacingVector, 255)
                    : new InputCommand(tick, tick, tick, held, pressed,
                        player.FacingVector, 255, boostIntent);
                player.ApplyNetworkInput(command);
                if (playerOnly) player.Process();
                else Scene.StepHeadlessFrame(advanceMatch: false);
            }

            public void Dispose()
            {
                _simulation.Dispose();
                _content.Dispose();
            }
        }
    }
}
