using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ShotTimingTests
    {
        private static readonly CombatActor Actor = new(0, 123, 7);
        private static BeamMechanics Mechanics(BeamType beam, bool continuous = false)
            => new(beam, beam, continuous, false, 0, 10, 2);
        private static InputCommand Command(uint sequence, uint view)
            => new(sequence, sequence, view, InputButtons.Shoot, InputButtons.Shoot, Vector3.UnitZ, (byte)BeamType.Imperialist);

        [Fact]
        public void ShotTimingSurvivesLaterCommandsAndRepeatedCollisionQueries()
        {
            var combat = new ServerCombat();
            CombatShot shot;
            using (combat.Enter(100))
            {
                combat.SetCommand(0, Command(55, 90), 150);
                shot = combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            }
            var expected = LagCompensationPolicy.ResolveTick(100, 90, 150);
            using (combat.Enter(104))
            {
                combat.SetCommand(0, Command(99, 104), 1);
                for (int target = 0; target < 8; target++)
                    combat.History.TryGet(target, shot.GetHistoricalTick(104), 999, 1, out _);
            }
            Assert.Equal(55u, shot.CommandSequence);
            Assert.Equal(100u, shot.ProcessedServerTick);
            Assert.Equal(90u, shot.ViewServerTick);
            Assert.Equal(expected.Tick, shot.ActionServerTick);
            Assert.Equal(expected.RewindTicks, shot.RewindTicks);
            Assert.Equal(unchecked(expected.Tick + 4), shot.GetHistoricalTick(104));
            Assert.Equal(1, combat.ShotsConsidered);
            Assert.Equal(1, combat.ShotsEligible);
            Assert.Equal(1, combat.ShotsRewound);
            Assert.Equal(1, combat.ValidatedRewindTicks.Count);
            Assert.Equal(8, combat.History.Queries);
        }

        [Fact]
        public void AttributionAndExcludedBeamsDoNotInflateTimedShotDenominators()
        {
            var combat = new ServerCombat(projectileCatchUpEnabled: false);
            using var scope = combat.Enter(200);
            combat.SetCommand(0, Command(4, 180), 250);
            for (int hit = 0; hit < 20; hit++)
            {
                var attribution = combat.CaptureAttribution(Actor);
                Assert.Equal(200u, attribution.ActionServerTick);
                Assert.Equal(0u, attribution.RewindTicks);
            }
            Assert.Equal(0, combat.ShotsConsidered);
            Assert.False(combat.CaptureShot(default(CombatActor), Mechanics(BeamType.Imperialist, false)).IsValid);
            Assert.Equal(0, combat.ShotsConsidered);
            Assert.Equal(0u, combat.CaptureShot(Actor, Mechanics(BeamType.PowerBeam, false)).RewindTicks);
            Assert.Equal(0u, combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, true)).RewindTicks);
            Assert.Equal(2, combat.ShotsConsidered);
            Assert.Equal(0, combat.ShotsEligible);
            Assert.Equal(0, combat.ValidatedRewindTicks.Count);
            var timed = combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            Assert.Equal(15u, timed.RewindTicks);
            Assert.Equal(3, combat.ShotsConsidered);
            Assert.Equal(1, combat.ShotsEligible);
            Assert.Equal(1, combat.ShotsClamped);
            Assert.Equal(20d, combat.RequestedRewindTicks.Mean);
            Assert.Equal(15d, combat.ValidatedRewindTicks.Mean);
        }

        [Fact]
        public void FutureAndWrappedShotsRemainBoundedAndResetClearsAllMetrics()
        {
            var combat = new ServerCombat();
            using (combat.Enter(3))
            {
                combat.SetCommand(0, Command(1, UInt32.MaxValue - 2), 250);
                var shot = combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
                Assert.InRange(shot.RewindTicks, 0u, 15u);
                Assert.Equal(unchecked(4u - shot.RewindTicks), shot.GetHistoricalTick(4));
                combat.SetCommand(0, Command(2, 4), 250);
                shot = combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
                Assert.Equal(3u, shot.ActionServerTick);
                Assert.Equal(0u, shot.RewindTicks);
                Assert.Equal(0d, combat.RequestedRewindTicks.Last);
                Assert.Equal(2, combat.ValidatedRewindTicks.Count);
                Assert.Equal(1, combat.ShotsClamped);
            }
            combat.Reset();
            Assert.Equal(0, combat.ShotsConsidered);
            Assert.Equal(0, combat.ShotsEligible);
            Assert.Equal(0, combat.ShotsRewound);
            Assert.Equal(0, combat.ShotsClamped);
            Assert.Equal(0, combat.RequestedRewindTicks.Count);
            Assert.Equal(0, combat.ValidatedRewindTicks.Count);
        }

        [Fact]
        public void RootShotResolutionAndMetricsAllocateNoPerShotStorage()
        {
            var combat = new ServerCombat();
            using var scope = combat.Enter(100);
            combat.SetCommand(0, Command(1, 1), 250);
            combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int shot = 0; shot < 10_000; shot++)
                combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(10_001, combat.ShotsConsidered);
            Assert.Equal(10_001, combat.ShotsClamped);
            Assert.Equal(10_001, combat.ValidatedRewindTicks.Count);
        }
    }
}
