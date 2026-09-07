using System;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class LagCompensationTests
    {
        private static LagCompensationState Player(int slot = 0) => new()
        {
            Slot = slot,
            ConnectionId = 13,
            LifeId = 2,
            Hunter = Hunter.Samus,
            Alive = true,
            Position = Vector3.Zero,
            Facing = -Vector3.UnitZ,
            SpherePosition = Vector3.UnitY,
            SphereRadius = 0.5f,
            MinPickupHeight = 0.2f,
            MaxPickupHeight = 1.8f
        };

        [Fact]
        public void TimingHintCannotExceedMeasuredBudgetAcrossTickWrap()
        {
            Assert.Equal(new LagCompensationTime(94, 6, 6, true), LagCompensationPolicy.ResolveTick(100, 90, 0));
            Assert.Equal(new LagCompensationTime(89, 11, 11, true), LagCompensationPolicy.ResolveTick(100, 1, 100));
            Assert.Equal(new LagCompensationTime(98, 2, 11, false), LagCompensationPolicy.ResolveTick(100, 98, 100));
            Assert.Equal(new LagCompensationTime(100, 0, 11, true), LagCompensationPolicy.ResolveTick(100, 101, 100));
            Assert.Equal(new LagCompensationTime(85, 15, 15, true), LagCompensationPolicy.ResolveTick(100, 1, Double.MaxValue));
            var wrapped = LagCompensationPolicy.ResolveTick(3, UInt32.MaxValue - 2, 250);
            Assert.Equal(UInt32.MaxValue - 2, wrapped.Tick);
            Assert.Equal(6u, wrapped.RewindTicks);
            Assert.False(wrapped.Clamped);
            foreach (double invalid in new[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -1d })
            {
                Assert.Equal(3u, LagCompensationPolicy.ResolveTick(3, 0, invalid).RewindTicks);
            }
            Assert.Equal(0u, LagCompensationPolicy.ResolveTick(0, 0x80000000u, 250).RewindTicks);
            var random = new Random(761);
            for (int i = 0; i < 10_000; i++)
            {
                uint now = (uint)random.NextInt64(0, 1L << 32);
                uint hint = (uint)random.NextInt64(0, 1L << 32);
                var result = LagCompensationPolicy.ResolveTick(now, hint, random.NextDouble() * 20_000);
                Assert.InRange(result.RewindTicks, 0u, LagCompensationPolicy.MaxRewindTicks);
                Assert.True(result.RewindTicks <= result.AllowedTicks);
                Assert.Equal(result.RewindTicks, unchecked(now - result.Tick));
            }
        }

        [Fact]
        public void HistorySeparatesConnectionsLivesSlotsAndExactWrappedTicks()
        {
            var history = new LagCompensationHistory();
            for (int slot = 0; slot < 8; slot++)
            {
                for (uint offset = 0; offset < 40; offset++)
                {
                    history.Record(unchecked(UInt32.MaxValue - 20 + offset), Player(slot) with
                    {
                        Position = new Vector3(slot, offset, 0)
                    });
                }
            }
            Assert.False(history.TryGet(0, UInt32.MaxValue - 20, 13, 2, out _));
            Assert.True(history.TryGet(7, 0, 13, 2, out var state));
            Assert.Equal(new Vector3(7, 21, 0), state.Position);
            Assert.False(history.TryGet(7, 0, 14, 2, out _));
            Assert.False(history.TryGet(7, 0, 13, 3, out _));
            Assert.False(history.TryGet(-1, 0, 13, 2, out _));
            Assert.False(history.TryGet(8, 0, 13, 2, out _));
            history.Record(1, Player(7) with { LifeId = 3 });
            Assert.True(history.TryGet(7, 1, 13, 3, out _));
            Assert.False(history.TryGet(7, 1, 13, 2, out _));
            history.Clear();
            Assert.False(history.TryGet(7, 1, 13, 3, out _));
        }

        [Fact]
        public void RepeatedCaptureAndLookupUseFixedStorage()
        {
            var history = new LagCompensationHistory();
            var state = Player();
            history.Record(0, state);
            history.TryGet(0, 0, 13, 2, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint tick = 1; tick <= 10_000; tick++)
            {
                history.Record(tick, state);
                history.TryGet(0, tick, 13, 2, out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(10_001, history.Queries);
            Assert.Equal(0, history.Missing);
            history.Clear();
            Assert.Equal(0, history.Queries);
            Assert.Equal(0, history.Missing);
        }

        [Fact]
        public void ViewTimestampAlreadyContainsDelayAndIsNotRewoundTwice()
        {
            // 100 ms RTT => sampled three ticks ago; targets were rendered six
            // further ticks behind that sampling instant.
            Assert.Equal(new LagCompensationTime(91, 9, 11, false), LagCompensationPolicy.ResolveTick(100, 91, 100));
            Assert.Equal(new LagCompensationTime(100, 0, 6, false), LagCompensationPolicy.ResolveTick(100, 100, 0));
            Assert.Equal(new LagCompensationTime(85, 15, 15, true), LagCompensationPolicy.ResolveTick(100, 1, 250));
        }

        [Theory]
        [InlineData(0, 6)]
        [InlineData(50, 10)]
        [InlineData(100, 11)]
        [InlineData(150, 13)]
        [InlineData(250, 15)]
        public void RenderedViewClaimsStayWithinTheMeasuredNetworkBudget(double rtt, uint allowance)
        {
            uint now = 1000;
            Assert.Equal(new LagCompensationTime(now - allowance, allowance, allowance, false),
                LagCompensationPolicy.ResolveTick(now, now - allowance, rtt));
            Assert.Equal(new LagCompensationTime(now, 0, allowance, true),
                LagCompensationPolicy.ResolveTick(now, now + 1, rtt));
            Assert.Equal(new LagCompensationTime(now - allowance, allowance, allowance, true),
                LagCompensationPolicy.ResolveTick(now, 1, rtt));
        }

        [Fact]
        public void HistoricalPositionAndFormDetermineHitWithoutMovingTheLiveCollider()
        {
            var historical = Player();
            var current = historical with { Position = new Vector3(8, 0, 0), SpherePosition = new Vector3(8, 1, 0) };
            var back = new Vector3(0, 1.7f, -5);
            var front = new Vector3(0, 1.7f, 5);
            CollisionResult hit = default;
            Assert.True(historical.CheckPlayer(back, front, 0.1f, ref hit));
            Assert.True(historical.IsHeadshot(hit.Position));
            Assert.False(current.CheckPlayer(back, front, 0.1f, ref hit));
            Assert.Equal(new Vector3(8, 0, 0), current.Position);
            var morphed = historical with { AltForm = true, SpherePosition = new Vector3(0, 0.5f, 0) };
            Assert.False(morphed.CheckPlayer(back, front, 0.1f, ref hit));
            Assert.False(morphed.IsHeadshot(new Vector3(0, 100, 0)));
            Assert.False((historical with { Alive = false }).CheckPlayer(back, front, 0.1f, ref hit));
            Assert.False((historical with { Spectating = true }).CheckPlayer(back, front, 0.1f, ref hit));
        }

        [Fact]
        public void HistoricalTargetBehindCurrentWallStillLosesNearestCollisionTest()
        {
            Vector3 back = new(0, 1, -5), front = new(0, 1, 5);
            CollisionResult wall = default, player = default;
            Assert.True(CollisionDetection.CheckCylinderIntersectPlane(back, front, new Vector4(0, 0, -1, 2), ref wall));
            Assert.True(Player().CheckPlayer(back, front, 0.1f, ref player));
            Assert.True(wall.Distance < player.Distance);
        }

        [Fact]
        public void AlternateFormsUseEngineSphereSegmentsAndSeparateTurretGeometry()
        {
            var kanden = Player() with
            {
                Hunter = Hunter.Kanden,
                AltForm = true,
                SpherePosition = new Vector3(3, 1, 0),
                KandenSegment1 = new Vector3(2, 1, 0),
                KandenSegment2 = new Vector3(1, 1, 0),
                KandenSegment3 = new Vector3(0, 1, 0)
            };
            Vector3 back = new(0, 1, -5), front = new(0, 1, 5);
            CollisionResult hit = default, expected = default;
            Assert.True(kanden.CheckPlayer(back, front, 0.1f, ref hit));
            Assert.True(CollisionDetection.CheckCylinderOverlapSphere(back, front, kanden.KandenSegment3, 0.6f, ref expected));
            Assert.Equal(expected.Position, hit.Position);
            Assert.Equal(expected.Distance, hit.Distance);
            var broadPhaseMiss = kanden with { KandenSegment2 = new Vector3(3, 1, 0) };
            Assert.False(broadPhaseMiss.CheckPlayer(back, front, 0.1f, ref hit));
            var weavel = Player() with { Hunter = Hunter.Weavel, HasHalfturret = true, HalfturretPosition = Vector3.UnitY };
            Assert.True(weavel.CheckHalfturret(back, front, 0.1f, ref hit));
            Assert.False((weavel with { HasHalfturret = false }).CheckHalfturret(back, front, 0.1f, ref hit));
            Assert.False((weavel with { Alive = false }).CheckHalfturret(back, front, 0.1f, ref hit));
        }

        [Fact]
        public void StandardHistoricalShapesMatchExistingEngineQueries()
        {
            var random = new Random(128);
            for (int i = 0; i < 1_000; i++)
            {
                Vector3 back = new((float)random.NextDouble() * 3 - 1.5f, (float)random.NextDouble() * 3, -5);
                Vector3 front = back + new Vector3((float)random.NextDouble() - 0.5f, 0.1f, 10);
                var state = Player() with { AltForm = i % 2 == 0 };
                CollisionResult actual = default, expected = default;
                bool expectedHit = state.AltForm
                    ? CollisionDetection.CheckCylinderOverlapSphere(back, front, state.SpherePosition, 0.6f, ref expected)
                    : CollisionDetection.CheckCylindersOverlap(back, front, state.Position.AddY(0.2f), Vector3.UnitY, 1.6f, 0.6f, ref expected);
                Assert.Equal(expectedHit, state.CheckPlayer(back, front, 0.1f, ref actual));
                if (expectedHit)
                {
                    Assert.Equal(expected.Distance, actual.Distance, 5);
                    Assert.Equal(expected.Position.X, actual.Position.X, 5);
                    Assert.Equal(expected.Position.Y, actual.Position.Y, 5);
                    Assert.Equal(expected.Position.Z, actual.Position.Z, 5);
                }
            }
        }

        [Fact]
        public void TraceAndOrdinaryProjectilePoliciesExcludeUnsupportedMechanics()
        {
            foreach (BeamType type in Enum.GetValues<BeamType>())
            {
                var mechanics = new BeamMechanics(type, type, false, false, 0, 10, 1);
                var expected = (uint)type > 8 ? LagCompensationMode.None : type == BeamType.Imperialist
                    ? LagCompensationMode.HistoricalTrace : LagCompensationMode.ProjectileCatchUp;
                Assert.Equal(expected, LagCompensationPolicy.GetMode(mechanics));
                Assert.Equal(LagCompensationMode.None, LagCompensationPolicy.GetMode(mechanics with { Continuous = true }));
                Assert.Equal(LagCompensationMode.None, LagCompensationPolicy.GetMode(mechanics with { InstantArea = true }));
                Assert.Equal(LagCompensationMode.None, LagCompensationPolicy.GetMode(mechanics with { Homing = 1 }));
            }
            Assert.Equal(15u, LagCompensationPolicy.MaxProjectileFastForwardTicks);
        }
    }
}
