using System;
using MphRead.Entities;

namespace MphRead.Mods.Input
{
    internal static class NativeTouchChecks
    {
        private static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException("Native touch: " + detail);
        }

        internal static void Run()
        {
            NativeTouchState state = new();
            state.UpdateRelative(true, 0, 0);
            state.UpdateRelative(true, 20, 10);
            state.UpdateRelative(true, 30, 15);
            state.UpdateRelative(true, -10, -5);
            Check(state.Continued && state.Delta4X == 40 && state.Delta4Y == 20, "four delta sum");
            state.UpdateRelative(false, 0, 0);
            Check(!state.Down && state.Delta4X == 0, "release clears history");

            NativeTouchSample sample = new();
            for (int i = 0; i < 4; i++) sample.StepAbsolute(true, 0, 0);
            sample.StepAbsolute(true, 100, 0);
            NativeTouchReport report = sample.Current;
            Check(report.Delta4X == 100 && !report.SecondStep, "30 Hz sample captures motion");
            float first = sample.TakeRollShare();
            sample.StepAbsolute(true, 100, 0);
            float second = sample.TakeRollShare();
            Check(first == 0.5f && second == 0.5f && sample.Current.SecondStep, "two half impulses");
            (float x, float z) half = MorphBallTouchRules.Roll(100, 0, first,
                1, 0, 1, 1, 0);
            Check(MathF.Abs(2 * half.x + 100 * MorphBallTouchRules.RollPerDsPixel) < 0.00001f,
                "native integrated roll");
            Check(MorphBallTouchRules.Arbitrate(false, true,
                new(true, true, true, 90, 0, 1, false)) != MorphBallTouchRules.Branch.Touch,
                "strict threshold");
            Check(MorphBallTouchRules.Arbitrate(false, true,
                new(true, true, true, 91, 0, 1, false)) == MorphBallTouchRules.Branch.Touch,
                "touch boost threshold");

            MorphBallBoostStateMachine machine = new();
            MorphBallBoostStateMachine.State boost = new() { CanTouchBoost = true, Charge = 15 };
            var swipe = new NativeTouchReport(true, true, true, 100, 0, 8, false);
            var firstBoost = machine.Advance(ref boost, swipe, true, 10, 60, false);
            var sibling = machine.Advance(ref boost, swipe with { SecondStep = true }, true, 10, 60, false);
            Check(firstBoost.Kind == MorphBallBoostStateMachine.Fired.Touch
                && sibling.Kind == MorphBallBoostStateMachine.Fired.None && boost.Charge == 15,
                "touch fires once and preserves shoulder charge");
            NativeTouchSample remote = new();
            remote.ApplyRemote(swipe, 1, 1);
            Check(remote.TakeRollShare() == 0.5f, "first report");
            remote.ApplyRemote(swipe, 1, 1);
            Check(remote.TakeRollShare() == 0, "duplicate suppressed");
            remote.ApplyRemote(swipe with { SecondStep = true }, 1, 1);
            Check(remote.TakeRollShare() == 0.5f, "sibling report");
            remote.ApplyRemote(swipe with { SampleSequence = 7 }, 1, 1);
            Check(remote.TakeRollShare() == 0, "stale report");
            Span<byte> bytes = stackalloc byte[NativeTouchReport.WireSize];
            swipe.Write(bytes);
            Check(NativeTouchReport.Read(bytes) == swipe, "wire roundtrip");
            Console.WriteLine("NATIVE TOUCH PASS");
        }
    }
}
