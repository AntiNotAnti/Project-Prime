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
            combat.BeginTick(100);
            {
                combat.SetCommand(0, Command(55, 90), 150);
                shot = combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            }
            var expected = LagCompensationPolicy.ResolveTick(100, 90, 150);
            combat.BeginTick(104);
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
            combat.BeginTick(200);
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
            combat.BeginTick(3);
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
        public void RootShotResolutionAndMetricsReuseFixedStorageAfterWarmup()
        {
            var combat = new ServerCombat();
            combat.BeginTick(100);
            combat.SetCommand(0, Command(1, 1), 250);
            combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            // The full test runner can perform order-dependent runtime/JIT
            // initialization on this thread. Exact steady-state zero B/op is
            // enforced by nettest --performance-baseline after controlled
            // warmup; this unit test owns the fixed-storage correctness.
            for (int shot = 0; shot < 10_000; shot++)
                combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist, false));
            Assert.Equal(10_001, combat.ShotsConsidered);
            Assert.Equal(10_001, combat.ShotsClamped);
            Assert.Equal(10_001, combat.ValidatedRewindTicks.Count);
        }

        [Fact]
        public void ClampedShotMeasuresIdentityFencedSpatialErrorWithoutGameplayQueries()
        {
            var combat = new ServerCombat();
            combat.BeginTick(100);
            combat.History.Record(0, State(1, 456, 3, new Vector3(0, 1, 0), .25f));
            combat.History.Record(94, State(1, 456, 3, Vector3.Zero, .25f));
            long queriesBefore = combat.History.Queries;
            combat.SetCommand(0, Command(6, 0), 0);

            CombatShot shot = combat.CaptureShot(Actor, Mechanics(BeamType.Imperialist));

            Assert.Equal(queriesBefore, combat.History.Queries);
            Assert.Equal(1, combat.ClampPositionError.Count);
            Assert.Equal(1d, combat.ClampPositionError.Mean);
            Assert.Equal(1d, combat.ClampVerticalError.Mean);
            Assert.Equal(0d, combat.ClampHorizontalError.Mean);
            Assert.Equal(1, combat.ClampPositionErrorByWeapon[(int)BeamType.Imperialist].Count);
            Assert.Equal(1, combat.ClampVerticalErrorOverHeadshotBand);
            Assert.Equal(1, combat.ClampTotalErrorOverPlayerRadius);
        }

        [Fact]
        public void ClampedSpatialDiagnosticsDistinguishMissingAndFutureHistory()
        {
            var requestedMissing = new ServerCombat();
            requestedMissing.BeginTick(100);
            requestedMissing.History.Record(94, State(1, 456, 3, Vector3.Zero, 1));
            requestedMissing.SetCommand(0, Command(7, 0), 0);
            requestedMissing.CaptureShot(Actor, Mechanics(BeamType.Imperialist));
            Assert.Equal(1, requestedMissing.ClampRequestedHistoryMissing);
            Assert.Equal(0, requestedMissing.ClampServedHistoryMissing);
            Assert.Equal(1, requestedMissing.ClampHistoryUnavailable);

            var servedMissing = new ServerCombat();
            servedMissing.BeginTick(100);
            servedMissing.History.Record(0, State(1, 456, 3, Vector3.Zero, 1));
            servedMissing.SetCommand(0, Command(8, 0), 0);
            servedMissing.CaptureShot(Actor, Mechanics(BeamType.Imperialist));
            Assert.Equal(0, servedMissing.ClampRequestedHistoryMissing);
            Assert.Equal(1, servedMissing.ClampServedHistoryMissing);
            Assert.Equal(1, servedMissing.ClampHistoryUnavailable);

            var future = new ServerCombat();
            future.BeginTick(100);
            future.History.Record(100, State(1, 456, 3, Vector3.Zero, 1));
            future.SetCommand(0, Command(9, 101), 0);
            long queriesBefore = future.History.Queries;
            future.CaptureShot(Actor, Mechanics(BeamType.Imperialist));
            Assert.Equal(1, future.ClampFutureRequests);
            Assert.Equal(0, future.ClampHistoryUnavailable);
            Assert.Equal(queriesBefore, future.History.Queries);
        }

        private static LagCompensationState State(int slot, ulong connectionId, uint life,
            Vector3 position, float radius)
            => new()
            {
                Slot = slot,
                ConnectionId = connectionId,
                LifeId = life,
                Alive = true,
                Hunter = Hunter.Samus,
                Position = position,
                SphereRadius = radius
            };
    }
}
