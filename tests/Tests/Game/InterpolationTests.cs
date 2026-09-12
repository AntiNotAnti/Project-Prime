using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class InterpolationTests
    {
        private static SnapshotPlayer Player(float x = 0, byte slot = 0) => new()
        {
            Slot = slot, ConnectionId = 1, Life = 1, Health = 100,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
            Position = new Vector3(x, 0, 0), Speed = new Vector3(2, 0, 0),
            Aim = Vector3.UnitZ, Facing = Vector3.UnitZ, AvailableWeapons = 1
        };

        private static void Add(SnapshotInterpolation history, uint tick, SnapshotPlayer player,
            uint match = 1) => Assert.True(history.Add(new SnapshotPacket(tick, tick, match, 0, false, 0, 0),
                new[] { player }, tick));

        private static void AddEmpty(SnapshotInterpolation history, uint tick, uint match = 1)
            => Assert.True(history.Add(new SnapshotPacket(tick, tick, match, 0, false, 0, 0),
                ReadOnlySpan<SnapshotPlayer>.Empty, tick));

        [Fact]
        public void DefaultDelayInterpolatesTransformsButUsesLatestGameplayState()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(0));
            SnapshotPlayer latest = Player(10);
            latest.Health = 40;
            latest.Weapon = 3;
            latest.Points = 9;
            latest.Aim = latest.Facing = Vector3.UnitX;
            Add(history, 110, latest);
            Assert.True(history.TrySample(0, 111, out SnapshotPlayer state));
            Assert.Equal(5, state.Position.X);
            Assert.Equal(40, state.Health);
            Assert.Equal(3, state.Weapon);
            Assert.Equal(9, state.Points);
            Assert.Equal(1, state.Aim.Length, 5);
            Assert.Equal(MathF.Sqrt(0.5f), state.Aim.X, 5);
            Assert.Equal(state.Aim, state.Facing);
            Assert.Equal(1, history.InterpolatedSamples);
        }

        [Fact]
        public void InterpolatedPresentationReportsVelocityOfTheRenderedTrajectory()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(0));
            Add(history, 110, Player(10));

            Assert.True(history.TryPreparePresentation(111, out SnapshotPresentation frame));
            Assert.True(history.TrySamplePresentation(0, frame,
                out SnapshotPlayerPresentation sample));
            Assert.Equal(5, sample.State.Position.X);
            Assert.Equal(2, sample.VisualSpeed.X, 5);
            // The state still carries newest authoritative gameplay velocity.
            Assert.Equal(2, sample.State.Speed.X);
        }

        [Fact]
        public void InterpolatedNewestEndpointUsesItsPreviousContinuousSegment()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(0));
            Add(history, 110, Player(10));

            // Default delay six ticks: estimated 116 presents exactly tick 110.
            Assert.True(history.TryPreparePresentation(116, out SnapshotPresentation frame));
            Assert.Equal(SnapshotPresentationMode.Interpolated, frame.Mode);
            Assert.True(history.TrySamplePresentation(0, frame,
                out SnapshotPlayerPresentation sample));
            Assert.Equal(10, sample.State.Position.X);
            Assert.Equal(2, sample.VisualSpeed.X, 5);
        }

        [Fact]
        public void ExactEndpointDoesNotDeriveVelocityAcrossSlotEpochs()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(0));
            SnapshotPlayer replacement = Player(10);
            replacement.Life++;
            Add(history, 110, replacement);

            Assert.True(history.TryPreparePresentation(116, out SnapshotPresentation frame));
            Assert.Equal(SnapshotPresentationMode.Interpolated, frame.Mode);
            Assert.True(history.TrySamplePresentation(0, frame,
                out SnapshotPlayerPresentation sample));
            Assert.Equal(replacement.Position, sample.State.Position);
            Assert.Equal(Vector3.Zero, sample.VisualSpeed);
        }

        [Fact]
        public void StartupAndHeldPresentationSamplesHaveNoVisualVelocity()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(10));
            Assert.True(history.TryPreparePresentation(1000, out SnapshotPresentation startup));
            Assert.Equal(SnapshotPresentationMode.Startup, startup.Mode);
            Assert.True(history.TrySamplePresentation(0, startup,
                out SnapshotPlayerPresentation startupSample));
            Assert.Equal(Vector3.Zero, startupSample.VisualSpeed);

            Add(history, 110, Player(20));
            Assert.True(history.TryPreparePresentation(100, out SnapshotPresentation held));
            Assert.Equal(SnapshotPresentationMode.HistoryHold, held.Mode);
            Assert.True(history.TrySamplePresentation(0, held,
                out SnapshotPlayerPresentation heldSample));
            Assert.Equal(Vector3.Zero, heldSample.VisualSpeed);
        }

        [Fact]
        public void ExtrapolationUsesNewestVelocityUntilTheBoundThenStops()
        {
            var history = new SnapshotInterpolation();
            SnapshotPlayer first = Player(0);
            first.Speed = new Vector3(4, 0, 0);
            SnapshotPlayer latest = Player(10);
            latest.Speed = new Vector3(3, 0, 0);
            Add(history, 100, first);
            Add(history, 110, latest);

            Assert.True(history.TryPreparePresentation(118, out SnapshotPresentation moving));
            Assert.Equal(SnapshotPresentationMode.Extrapolated, moving.Mode);
            Assert.True(history.TrySamplePresentation(0, moving,
                out SnapshotPlayerPresentation movingSample));
            Assert.Equal(3, movingSample.VisualSpeed.X);

            Assert.True(history.TryPreparePresentation(120, out SnapshotPresentation held));
            Assert.Equal(SnapshotPresentationMode.ExtrapolationHold, held.Mode);
            Assert.True(history.TrySamplePresentation(0, held,
                out SnapshotPlayerPresentation heldSample));
            Assert.Equal(Vector3.Zero, heldSample.VisualSpeed);
        }

        [Fact]
        public void ExtrapolationUsesEngineVelocityUnitsAndHoldsAtTheBound()
        {
            var history = new SnapshotInterpolation();
            Add(history, 99, Player(9));
            Add(history, 100, Player(10));
            Assert.True(history.TrySample(0, 108, out SnapshotPlayer extrapolated));
            Assert.Equal(12, extrapolated.Position.X);
            Assert.True(history.TrySample(0, 120, out SnapshotPlayer held));
            Assert.Equal(13, held.Position.X);
            Assert.True(history.TrySample(0, 10000, out SnapshotPlayer muchLater));
            Assert.Equal(held.Position, muchLater.Position);
            Assert.Equal(3, history.UnderrunSamples);
            Assert.Equal(1, history.ExtrapolatedSamples);
            Assert.Equal(2, history.HeldSamples);
            Assert.Equal(3, history.MaximumExtrapolationTicks);
        }

        [Fact]
        public void StartupNeverClaimsHistoricalOrExtrapolatedTimeWithoutEstablishedHistory()
        {
            var history = new SnapshotInterpolation();
            Assert.False(history.TryPreparePresentation(100, out _));
            Assert.False(history.TryCaptureViewTick(out _));
            Add(history, 100, Player(10));
            foreach (double estimated in new[] { 0.0, 100.0, 1000.0 })
            {
                Assert.True(history.TryPreparePresentation(estimated, out SnapshotPresentation frame));
                Assert.Equal(100, frame.Tick);
                Assert.Equal(SnapshotPresentationMode.Startup, frame.Mode);
                Assert.True(history.TrySample(0, frame, out SnapshotPlayer state));
                Assert.Equal(10, state.Position.X);
            }
            Assert.True(history.TryCaptureViewTick(out uint fallback));
            Assert.Equal(100u, fallback);
            Assert.False(history.HasPresented);
        }

        [Theory]
        [InlineData(95, 100, SnapshotPresentationMode.HistoryHold)]
        [InlineData(111.75, 105.75, SnapshotPresentationMode.Interpolated)]
        [InlineData(116, 110, SnapshotPresentationMode.Interpolated)]
        [InlineData(118, 112, SnapshotPresentationMode.Extrapolated)]
        [InlineData(119, 113, SnapshotPresentationMode.Extrapolated)]
        [InlineData(120, 113, SnapshotPresentationMode.ExtrapolationHold)]
        [InlineData(10000, 113, SnapshotPresentationMode.ExtrapolationHold)]
        public void SharedPresentationTickDescribesTheSampledOrHeldPosition(double estimated,
            double tick, SnapshotPresentationMode mode)
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(100));
            Add(history, 110, Player(110));
            Assert.True(history.TryPreparePresentation(estimated, out SnapshotPresentation frame));
            Assert.Equal(tick, frame.Tick);
            Assert.Equal(mode, frame.Mode);
            Assert.True(history.TrySample(0, frame, out SnapshotPlayer state));
            Assert.Equal((float)tick, state.Position.X);
            Assert.True(history.MarkPresented(frame));
            Assert.True(history.TryCaptureViewTick(out uint viewTick));
            Assert.Equal((uint)Math.Floor(tick), viewTick);
        }

        [Fact]
        public void PacketArrivalAndUnpresentedPicturesCannotChangeAnInputsDisplayedView()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(100));
            Add(history, 110, Player(110));
            Assert.True(history.TryPreparePresentation(111.75, out SnapshotPresentation displayed));
            Assert.True(history.MarkPresented(displayed));
            Add(history, 120, Player(120));
            Assert.True(history.TryPreparePresentation(123, out SnapshotPresentation upcoming));
            Assert.True(history.TrySample(0, upcoming, out SnapshotPlayer unpresented));
            Assert.Equal(117, unpresented.Position.X);
            // Several catch-up input steps can occur before this picture is
            // displayed; a failed/skipped render leaves the same view in place.
            for (int step = 0; step < 4; step++)
            {
                Assert.True(history.TryCaptureViewTick(out uint tick));
                Assert.Equal(105u, tick);
            }
            Assert.True(history.MarkPresented(upcoming));
            Assert.True(history.TryCaptureViewTick(out uint next));
            Assert.Equal(117u, next);
            // Faster rendering can commit another picture with no input step.
            Assert.True(history.TryPreparePresentation(123.5, out SnapshotPresentation fasterFrame));
            Assert.True(history.MarkPresented(fasterFrame));
            Assert.True(history.TryPreparePresentation(124, out fasterFrame));
            Assert.True(history.MarkPresented(fasterFrame));
            Assert.True(history.TryCaptureViewTick(out next));
            Assert.Equal(118u, next);
        }

        [Fact]
        public void AHistoryResetRejectsPreparedPicturesEvenWhenTheMatchIdIsReused()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(100));
            Assert.True(history.TryPreparePresentation(106, out SnapshotPresentation previous));
            Assert.True(history.MarkPresented(previous));
            history.Reset();
            Assert.False(history.TryCaptureViewTick(out _));
            Add(history, 120, Player(120));
            Assert.False(history.HasPresented);
            Assert.False(history.MarkPresented(previous));
            Assert.False(history.TrySample(0, previous, out _));
            Assert.True(history.TryCaptureViewTick(out uint fallback));
            Assert.Equal(120u, fallback);
            Assert.True(history.TryPreparePresentation(130, out SnapshotPresentation current));
            Add(history, 1, Player(1), match: 2);
            Assert.False(history.MarkPresented(current));
            Assert.False(history.TrySample(0, current, out _));
        }

        [Fact]
        public void SlotDiscontinuityOverridesItsPoseWithoutChangingTheSharedViewTick()
        {
            var history = new SnapshotInterpolation();
            var before = new[] { Player(100), Player(100, 1) };
            var after = new[] { Player(110), Player(500, 1) };
            after[1].Life++;
            Assert.True(history.Add(new SnapshotPacket(100, 100, 1, 0, false, 0, 0), before, 100));
            Assert.True(history.Add(new SnapshotPacket(110, 110, 1, 0, false, 0, 0), after, 110));
            Assert.True(history.TryPreparePresentation(111, out SnapshotPresentation frame));
            Assert.Equal(105, frame.Tick);
            Assert.True(history.TrySample(0, frame, out SnapshotPlayer continuous));
            Assert.True(history.TrySample(1, frame, out SnapshotPlayer spawned));
            Assert.Equal(105, continuous.Position.X);
            Assert.Equal(500, spawned.Position.X);
            Assert.True(history.MarkPresented(frame));
            Assert.True(history.TryCaptureViewTick(out uint view));
            Assert.Equal(105u, view);
        }

        [Fact]
        public void SpireAltAttackEdgeDoesNotBlendAcrossPresentationModes()
        {
            var history = new SnapshotInterpolation();
            SnapshotPlayer before = Player(0);
            before.Hunter = Hunter.Spire;
            before.Flags |= SnapshotPlayerFlags.AltForm;
            SnapshotPlayer attack = before;
            attack.Position = new Vector3(10, 0, 0);
            attack.Flags |= SnapshotPlayerFlags.SpireAltAttack;
            Add(history, 100, before);
            Add(history, 110, attack);

            Assert.True(history.TrySample(0, 111, out SnapshotPlayer sampled));
            Assert.Equal(attack.Position, sampled.Position);
            Assert.True((sampled.Flags & SnapshotPlayerFlags.SpireAltAttack) != 0);
        }

        [Fact]
        public void PublishedViewTickWrapsWithoutChangingTheContinuousPictureTime()
        {
            var history = new SnapshotInterpolation();
            Add(history, UInt32.MaxValue - 4, Player(0));
            Add(history, 5, Player(10));
            Assert.True(history.TryPreparePresentation(6.75, out SnapshotPresentation wrapped));
            Assert.True(history.TryPreparePresentation(4294967302.75, out SnapshotPresentation continuous));
            Assert.Equal(continuous.Tick, wrapped.Tick);
            Assert.Equal(4294967296.75, wrapped.Tick);
            Assert.True(history.MarkPresented(wrapped));
            Assert.True(history.TryCaptureViewTick(out uint view));
            Assert.Equal(0u, view);
        }

        [Fact]
        public void DisablingExtrapolationHoldsTheNewestTickAndRecordsTheUnderrun()
        {
            var history = new SnapshotInterpolation(maxExtrapolationTicks: 0);
            Add(history, 100, Player(100));
            Add(history, 110, Player(110));
            Assert.True(history.TryPreparePresentation(150, out SnapshotPresentation held));
            Assert.Equal(SnapshotPresentationMode.ExtrapolationHold, held.Mode);
            Assert.Equal(110, held.Tick);
            Assert.True(history.TrySample(0, held, out SnapshotPlayer state));
            Assert.Equal(110, state.Position.X);
            Assert.Equal(1, history.UnderrunSamples);
            Assert.Equal(1, history.HeldSamples);
            Assert.Equal(0, history.ExtrapolatedSamples);
        }

        [Theory]
        [InlineData(0)] // connection replacement
        [InlineData(1)] // respawn
        [InlineData(2)] // form completion
        [InlineData(3)] // morph start
        [InlineData(4)] // death
        [InlineData(5)] // spectator transition
        public void DiscreteTransitionsNeverBlendThePreviousPose(int transition)
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(0));
            SnapshotPlayer next = Player(100);
            switch (transition)
            {
                case 0: next.ConnectionId++; break;
                case 1: next.Life++; break;
                case 2: next.Flags |= SnapshotPlayerFlags.AltForm; break;
                case 3: next.Flags |= SnapshotPlayerFlags.Morphing; break;
                case 4: next.Health = 0; break;
                case 5: next.Flags |= SnapshotPlayerFlags.Spectating; break;
            }
            Add(history, 110, next);
            Assert.True(history.TrySample(0, 111, out SnapshotPlayer state));
            Assert.Equal(next.Position, state.Position);
            Assert.Equal(next.Flags, state.Flags);
            Assert.Equal(next.Life, state.Life);
            Assert.Equal(next.ConnectionId, state.ConnectionId);
        }

        [Fact]
        public void RepeatedFormTransitionsAndAbsentSlotsCannotRejoinOldHistory()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player(0));
            SnapshotPlayer alternate = Player(100);
            alternate.Flags |= SnapshotPlayerFlags.AltForm;
            Add(history, 101, alternate);
            Add(history, 102, Player(200));
            Assert.True(history.TrySample(0, 106.5, out SnapshotPlayer afterForm));
            Assert.Equal(200, afterForm.Position.X);
            Assert.True(history.Add(new SnapshotPacket(103, 103, 1, 0, false, 0, 0),
                ReadOnlySpan<SnapshotPlayer>.Empty, 103));
            Assert.False(history.TrySample(0, 110, out _));
            Add(history, 104, Player(300));
            Assert.True(history.TrySample(0, 106.5, out SnapshotPlayer afterReturn));
            Assert.Equal(300, afterReturn.Position.X);
        }

        [Fact]
        public void TickAndSequenceWrapWorkWithEitherClockEpoch()
        {
            var history = new SnapshotInterpolation();
            Add(history, UInt32.MaxValue - 4, Player(0));
            Add(history, 5, Player(10));
            Assert.True(history.TrySample(0, 6, out SnapshotPlayer wrappedClock));
            Assert.True(history.TrySample(0, 4294967302.0, out SnapshotPlayer continuousClock));
            Assert.Equal(5, wrappedClock.Position.X);
            Assert.Equal(wrappedClock.Position, continuousClock.Position);
            Assert.False(history.Add(new SnapshotPacket(UInt32.MaxValue, UInt32.MaxValue, 1, 0, false, 0, 0),
                new[] { Player(900) }, 200));
            Assert.Equal(2, history.Count);
        }

        [Fact]
        public void HistoryIsBoundedAndSlotOrderingDoesNotMatter()
        {
            var history = new SnapshotInterpolation();
            for (uint tick = 0; tick < 1000; tick++)
            {
                Assert.True(history.Add(new SnapshotPacket(tick, tick, 1, 0, false, 0, 0),
                    new[] { Player(tick + 100, 7), Player(tick) }, tick));
            }
            Assert.Equal(SnapshotInterpolation.Capacity, history.Count);
            Assert.Equal(999, history.LatestReceivedAt);
            Assert.True(history.TrySample(0, 1000, out SnapshotPlayer first));
            Assert.True(history.TrySample(7, 1000, out SnapshotPlayer last));
            Assert.Equal(994, first.Position.X);
            Assert.Equal(1094, last.Position.X);
            Assert.False(history.TrySample(1, 1000, out _));
            Assert.True(history.TrySample(0, 20, out SnapshotPlayer beforeOldest));
            Assert.Equal(968, beforeOldest.Position.X);
        }

        [Fact]
        public void OppositeDirectionsDoNotProduceAnInvalidOrientation()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player());
            SnapshotPlayer next = Player(10);
            next.Aim = next.Facing = -Vector3.UnitZ;
            Add(history, 110, next);
            Assert.True(history.TrySample(0, 111, out SnapshotPlayer state));
            Assert.Equal(-Vector3.UnitZ, state.Aim);
            Assert.Equal(-Vector3.UnitZ, state.Facing);
        }

        [Fact]
        public void MatchResetAndMalformedCallsDoNotExposeStalePlayers()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player());
            Assert.False(history.Add(new SnapshotPacket(101, 101, 2, 0, false, 0, 0),
                new[] { Player(), Player() }, 101));
            Assert.Equal(1, history.Count);
            Add(history, 0, Player(70, 7), match: 2);
            Assert.Equal(1, history.Count);
            Assert.False(history.TrySample(0, 10, out _));
            Assert.True(history.TrySample(7, 0, out SnapshotPlayer state));
            Assert.Equal(70, state.Position.X);
            Assert.False(history.TrySample(-1, 10, out _));
            Assert.False(history.TrySample(8, 10, out _));
            Assert.False(history.TrySample(7, Double.NaN, out _));
            history.Reset();
            Assert.False(history.TrySample(7, 0, out _));
            Assert.Equal(0, history.Count);
        }

        [Fact]
        public void SamplingDoesNotAllocate()
        {
            var history = new SnapshotInterpolation();
            Add(history, 100, Player());
            Add(history, 110, Player(10));
            static void Present(SnapshotInterpolation value)
            {
                value.TryPreparePresentation(111, out SnapshotPresentation frame);
                value.TrySample(0, frame, out _);
                value.TrySamplePresentation(0, frame, out SnapshotPlayerPresentation sample);
                value.MarkPresented(frame);
                value.TryCaptureViewTick(out _);
            }
            for (int i = 0; i < 100; i++) Present(history);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) Present(history);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        }

        [Fact]
        public void FrameCountersArePlayerCountNeutralAndCommitOnlyAfterPresentation()
        {
            static SnapshotInterpolation Build(int players)
            {
                var history = new SnapshotInterpolation();
                var first = new SnapshotPlayer[players];
                var second = new SnapshotPlayer[players];
                for (byte slot = 0; slot < players; slot++)
                {
                    first[slot] = Player(slot, slot);
                    second[slot] = Player(slot + 10, slot);
                }
                Assert.True(history.Add(new SnapshotPacket(100, 100, 1, 0, false, 0, 0), first, 100));
                Assert.True(history.Add(new SnapshotPacket(110, 110, 1, 0, false, 0, 0), second, 110));
                Assert.True(history.TryPreparePresentation(111, out SnapshotPresentation frame));
                for (int slot = 0; slot < players; slot++)
                    Assert.True(history.TrySamplePresentation(slot, frame, out _));
                Assert.Equal(0, history.PresentedFrames);
                Assert.True(history.MarkPresented(frame));
                return history;
            }

            SnapshotInterpolation two = Build(2);
            SnapshotInterpolation eight = Build(8);
            Assert.Equal(1, two.PresentedFrames);
            Assert.Equal(1, two.InterpolatedFrames);
            Assert.Equal(two.PresentedFrames, eight.PresentedFrames);
            Assert.Equal(two.InterpolatedFrames, eight.InterpolatedFrames);
            Assert.Equal(0, two.UnderrunFrames);
            Assert.Equal(0, two.HeldFrames);
        }

        [Fact]
        public void FrameFlagsAreInclusiveForExtrapolationHoldsAndResetForAbortedEpochs()
        {
            var extrapolated = new SnapshotInterpolation();
            Add(extrapolated, 100, Player(0));
            Add(extrapolated, 110, Player(10));
            Assert.True(extrapolated.TryPreparePresentation(118, out SnapshotPresentation moving));
            Assert.Equal(SnapshotPresentationMode.Extrapolated, moving.Mode);
            Assert.True(extrapolated.MarkPresented(moving));
            Assert.Equal(1, extrapolated.PresentedFrames);
            Assert.Equal(1, extrapolated.UnderrunFrames);
            Assert.Equal(1, extrapolated.ExtrapolatedFrames);
            Assert.Equal(0, extrapolated.HeldFrames);

            var held = new SnapshotInterpolation(maxExtrapolationTicks: 0);
            Add(held, 100, Player(0));
            Add(held, 110, Player(10));
            Assert.True(held.TryPreparePresentation(118, out SnapshotPresentation endpoint));
            Assert.Equal(SnapshotPresentationMode.ExtrapolationHold, endpoint.Mode);
            Assert.True(held.MarkPresented(endpoint));
            Assert.Equal(1, held.UnderrunFrames);
            Assert.Equal(0, held.ExtrapolatedFrames);
            Assert.Equal(1, held.HeldFrames);

            var empty = new SnapshotInterpolation();
            AddEmpty(empty, 100);
            AddEmpty(empty, 110);
            Assert.True(empty.TryPreparePresentation(111, out SnapshotPresentation zeroPlayer));
            Assert.Equal(0, empty.PresentedFrames);
            Assert.True(empty.MarkPresented(zeroPlayer));
            Assert.Equal(1, empty.PresentedFrames);
            Assert.True(empty.TryPreparePresentation(112, out SnapshotPresentation abandoned));
            Assert.Equal(1, empty.PresentedFrames);
            empty.Reset();
            Assert.Equal(0, empty.PresentedFrames);
            Assert.False(empty.MarkPresented(abandoned));
        }
    }
}
