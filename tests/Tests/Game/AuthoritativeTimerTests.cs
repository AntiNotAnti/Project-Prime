using System;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Match baseline globals")]
    public sealed class AuthoritativeTimerTests
    {
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        [InlineData(4)] [InlineData(5)] [InlineData(8)]
        public void NodeScoreTicksMatchRepeatedFloatThresholdCrossing(int nodes)
        {
            float threshold = 150 / 30f;
            for (int count = 2; count <= nodes; count++) threshold -= 45 / 30f;
            float elapsed = 0;
            int ticks = 0;
            do { elapsed += 1 / 60f; ticks++; } while (elapsed < threshold);
            Assert.Equal(ticks, NodeDefenseEntity.ScoreIntervalTicks(nodes));
        }

        [Trait("RequiresGameContent", "true")]
        [Fact]
        public void ActualNodeCapturePauseAndProgressProjectionKeepEveryLegacyFloatBit()
        {
            string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
            using var content = ServerContent.PreserveContext("AMHE1");
            ServerContent.Open(data, "AMHE1");
            using var simulation = new ServerSimulation(new MatchRules(MatchMode.Nodes, "MP1 SANCTORUS"));
            Scene scene = simulation.Scene;
            scene.Match.Phase = MatchPhase.Playing;
            scene.StepHeadlessFrame(advanceMatch: false);
            NodeDefenseEntity? selected = null;
            foreach (NodeDefenseEntity candidate in scene.GetNodeDefenseEntities()) { selected = candidate; break; }
            NodeDefenseEntity node = Assert.IsType<NodeDefenseEntity>(selected);
            PlayerEntity player = scene.Players[0];
            player.ServerActivate(100, Hunter.Samus, 0);
            PlayerEntity opponent = scene.Players[1];
            opponent.ServerActivate(200, Hunter.Kanden, 1);
            PutInside(player, node.Volume);
            MoveTo(opponent, player.Position + Vector3.UnitX * 100);
            float progress = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                if (tick == 100)
                {
                    PutInside(opponent, node.Volume);
                    for (int pause = 0; pause < 13; pause++)
                    {
                        node.Process();
                        Assert.True(node.Contested);
                        Assert.Equal(BitConverter.SingleToInt32Bits(progress), BitConverter.SingleToInt32Bits(node.Progress));
                    }
                    MoveTo(opponent, player.Position + Vector3.UnitX * 100);
                }
                node.Process();
                progress += scene.FrameTime;
                if (tick < 600)
                {
                    Assert.Null(node.CapturedPlayer);
                    Assert.Equal(BitConverter.SingleToInt32Bits(progress), BitConverter.SingleToInt32Bits(node.Progress));
                    Assert.Equal(WorldRecord.Bits(progress), node.CaptureWorldState().C);
                }
            }
            Assert.Same(player, node.CapturedPlayer);
            Assert.Equal(0, node.Progress);
            Assert.Equal(1, scene.Match.NodesCaptured[0]);
            MoveTo(player, player.Position + Vector3.UnitX * 100);
            int before = scene.Match.Points[0];
            Assert.Equal(1, before); // the five-second priming awards a point on the capture tick itself.
            for (int tick = 1; tick < 300; tick++) node.Process();
            Assert.Equal(before, scene.Match.Points[0]);
            node.Process();
            Assert.Equal(before + 1, scene.Match.Points[0]);
        }

        [Fact]
        public void AuthoredProjectileTimelinesMatchEveryLegacyExpirationAndAgeBit()
        {
            float age = 0;
            for (int tick = 0; tick <= LegacyTickProjection.MaximumTicks; tick++)
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(age), BitConverter.SingleToInt32Bits(LegacyTickProjection.Elapsed(tick)));
                age += 1 / 60f;
            }
            foreach (var table in new[] { Weapons.WeaponsMP, Weapons.PlatformWeapons,
                Weapons.Ricochets, Weapons.ForceFieldLockWeapons })
                foreach (WeaponInfo weapon in table)
                {
                    // Every currently authored charged duration is constant across charge fraction.
                    Assert.Equal(weapon.MinChargeLifespan, weapon.ChargedLifespan);
                    foreach (ushort frames in new[] { weapon.UnchargedLifespan, weapon.MinChargeLifespan, weapon.ChargedLifespan })
                    {
                        float remaining = frames * (1 / 30f);
                        var timeline = LegacyTickProjection.ForCountdown(remaining);
                        Assert.Same(timeline, LegacyTickProjection.ForCountdown(remaining));
                        for (int tick = 0; tick <= timeline.Ticks; tick++)
                        {
                            Assert.Equal(BitConverter.SingleToInt32Bits(remaining),
                                BitConverter.SingleToInt32Bits(timeline.Remaining(timeline.Ticks - tick)));
                            Assert.Equal(remaining > 0, tick < timeline.Ticks);
                            remaining -= 1 / 60f;
                        }
                    }
                    foreach (ushort frames in weapon.SpeedDecayTimes)
                    {
                        float duration = frames * (1 / 30f);
                        float elapsed = 0;
                        int end = LegacyTickProjection.LastAtMost(duration);
                        for (int tick = 0; tick <= end + 1; tick++)
                        {
                            Assert.Equal(elapsed <= duration, tick <= end);
                            if (duration > 0)
                                Assert.Equal(BitConverter.SingleToInt32Bits(elapsed / duration),
                                    BitConverter.SingleToInt32Bits(LegacyTickProjection.Elapsed(tick) / duration));
                            elapsed += 1 / 60f;
                        }
                    }
                }
            // Public/custom authored durations retain their fractional countdown as well.
            foreach (float duration in new[] { 0f, 1 / 60f, 0.12345f, 65535 * (1 / 30f) })
            {
                var timeline = LegacyTickProjection.ForCountdown(duration);
                Assert.True(timeline.Ticks <= LegacyTickProjection.MaximumTicks);
                Assert.True(timeline.Remaining(0) <= 0);
            }
        }

        [Trait("RequiresGameContent", "true")]
        [Theory]
        [InlineData(BeamType.Missile)]
        [InlineData(BeamType.VoltDriver)]
        [InlineData(BeamType.OmegaCannon)]
        public void ActualProjectileMotionMatchesFrozenFloatAgeAndDecay(BeamType type)
        {
            string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
            using var content = ServerContent.PreserveContext("AMHE1");
            ServerContent.Open(data, "AMHE1");
            using var simulation = new ServerSimulation(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"));
            Scene scene = simulation.Scene;
            scene.Match.Phase = MatchPhase.Playing;
            PlayerEntity owner = scene.Players[0];
            owner.ServerActivate(100, Hunter.Samus, 0);
            scene.StepHeadlessFrame(advanceMatch: false);
            var equip = new EquipInfo(Weapons.Current[(int)type], new[] { new BeamProjectileEntity(scene) })
                { InfiniteAmmo = true };
            BeamProjectileEntity.Spawn(owner, equip, new Vector3(0, 500, 0), Vector3.UnitZ,
                BeamSpawnFlags.Charged | BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
            BeamProjectileEntity beam = equip.Beams[0];
            Assert.Same(owner, beam.Owner);
            Assert.Null(beam.Target);
            Vector3 position = beam.Position, velocity = beam.Velocity;
            float age = 0, lifespan = beam.Lifespan;
            for (int tick = 1; tick <= 30; tick++)
            {
                age += scene.FrameTime;
                lifespan -= scene.FrameTime;
                position += velocity;
                velocity += beam.Acceleration / 2;
                if (beam.SpeedDecayTime > 0 && age <= beam.SpeedDecayTime)
                {
                    float magnitude = velocity.Length;
                    if (magnitude > 0)
                        velocity *= FrozenInterpolation(beam.SpeedInterpolation, beam.InitialSpeed,
                            beam.FinalSpeed, age / beam.SpeedDecayTime) / magnitude;
                }
                Assert.True(beam.Process());
                Assert.False(beam.Flags.TestFlag(BeamFlags.Collided));
                Assert.Equal(position, beam.Position);
                Assert.Equal(velocity, beam.Velocity);
                Assert.Equal(BitConverter.SingleToInt32Bits(age), BitConverter.SingleToInt32Bits(beam.Age));
                Assert.Equal(BitConverter.SingleToInt32Bits(lifespan), BitConverter.SingleToInt32Bits(beam.Lifespan));
                Assert.Equal(tick, beam.AgeTicks);
            }
        }

        private static float FrozenInterpolation(int type, float from, float to, float ratio)
        {
            if (type == 3) return ratio > 1 ? to : from;
            ratio = Math.Clamp(ratio, 0, 1);
            if (type == 0) return from + (to - from) * ratio;
            if (type == 1) return from + (to - from)
                * ((MathF.Sin(MathHelper.DegreesToRadians(270 - 180 * ratio)) + 1) / 2);
            if (type == 2) return from + (to - from)
                * (MathF.Sin(MathHelper.DegreesToRadians(270 - 90 * ratio)) + 1);
            return 0;
        }

        [Fact]
        public void CollidedBeamExpiresAndReusesItsTimerOnTheExactLegacyTick()
        {
            using Scene scene = Scene.CreateHeadless();
            var beam = new BeamProjectileEntity(scene) { Flags = BeamFlags.Collided };
            foreach (float duration in new[] { 4 * (1 / 30f), 255 * (1 / 30f), 0.12345f })
            {
                beam.Lifespan = duration;
                float remaining = duration;
                int count = 0;
                while (remaining > 0)
                {
                    remaining -= 1 / 60f;
                    Assert.True(beam.Process());
                    Assert.Equal(BitConverter.SingleToInt32Bits(remaining), BitConverter.SingleToInt32Bits(beam.Lifespan));
                    count++;
                }
                Assert.Equal(0, beam.RemainingLifeTicks);
                Assert.Equal(0, beam.AgeTicks); // collided tails never advance motion age.
                Assert.False(beam.Process());
                Assert.True(count > 0);
            }
            beam.Lifespan = 0;
            Assert.False(beam.Process());
        }

        [Fact]
        public void WarmAuthoredTickProjectionDoesNotAllocate()
        {
            float duration = 255 * SimTicks.LegacyFrameSeconds;
            var timeline = LegacyTickProjection.ForCountdown(duration);
            float sum = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 10000; repeat++)
            {
                sum += LegacyTickProjection.ForCountdown(duration).Remaining(repeat % timeline.Ticks);
                sum += LegacyTickProjection.Elapsed(repeat % timeline.Ticks);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.True(sum > 0);
        }

        private static void MoveTo(PlayerEntity player, Vector3 position)
        {
            Vector3 previous = player.Position;
            player.Position = position;
            player.ModRefreshNodeRef(previous);
        }

        private static void PutInside(PlayerEntity player, CollisionVolume volume)
        {
            Vector3 center = volume.Type switch
            {
                VolumeType.Sphere => volume.SpherePosition,
                VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
                _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                    + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
            };
            Vector3 previous = player.Position;
            player.Position = center - (player.Volume.SpherePosition - previous);
            player.ModRefreshNodeRef(previous);
        }
    }
}
