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

            // A mouse gesture with the boost preference off must still move
            // the ball. Filter only the canonical boost-eligibility bit, before
            // this same sample is consumed locally, sent over the network or
            // saved in the replay checkpoint.
            NativeTouchSample mouseWithBoostOff = new();
            mouseWithBoostOff.StepRelative(true, 100, 0, 1, allowMovementBoost: false);
            mouseWithBoostOff.StepRelative(true, 0, 0, 1, allowMovementBoost: false);
            mouseWithBoostOff.StepRelative(true, 400, 0, 1, allowMovementBoost: false);
            NativeTouchReport noMouseBoost = mouseWithBoostOff.Current;
            Check(noMouseBoost.Active && noMouseBoost.Down && !noMouseBoost.Continued
                && noMouseBoost.Delta4X == 100 && mouseWithBoostOff.TakeRollShare() == .5f,
                "disabled mouse boost preserves the native rolling motion");
            MorphBallBoostStateMachine.State mouseBoostState = new() { CanTouchBoost = true };
            Check(new MorphBallBoostStateMachine().Advance(ref mouseBoostState, noMouseBoost,
                    buttonHeld: false, min: 10, max: 60, alwaysFull: false).Kind
                    == MorphBallBoostStateMachine.Fired.None && !mouseBoostState.Boosting,
                "disabled mouse movement cannot fire native boost");
            Span<byte> gatedWire = stackalloc byte[NativeTouchReport.WireSize];
            noMouseBoost.Write(gatedWire);
            NativeTouchReport decodedGate = NativeTouchReport.Read(gatedWire);
            NativeTouchSample remoteGate = new();
            remoteGate.ApplyRemote(decodedGate, 2, 1);
            Check(decodedGate == noMouseBoost && !remoteGate.Current.Continued
                && remoteGate.Current.Delta4X == 100 && remoteGate.TakeRollShare() == .5f,
                "disabled native boost propagates over the existing wire without losing roll");
            MorphBallBoostStateMachine.State manual = new() { CanTouchBoost = true, Charge = 15 };
            Check(new MorphBallBoostStateMachine().Advance(ref manual, decodedGate,
                    buttonHeld: false, min: 10, max: 60, alwaysFull: false).Kind
                    == MorphBallBoostStateMachine.Fired.Shoulder,
                "manual boost release remains usable when movement boost is disabled");
            mouseWithBoostOff.StepRelative(true, 0, 0, 1, allowMovementBoost: true);
            mouseWithBoostOff.StepRelative(true, 400, 0, 1, allowMovementBoost: true);
            NativeTouchReport enabledMouseBoost = mouseWithBoostOff.Current;
            MorphBallBoostStateMachine.State enabled = new() { CanTouchBoost = true };
            Check(enabledMouseBoost.Continued
                && new MorphBallBoostStateMachine().Advance(ref enabled, enabledMouseBoost,
                    buttonHeld: false, min: 10, max: 60, alwaysFull: false).Kind
                    == MorphBallBoostStateMachine.Fired.Touch,
                "re-enabling movement boost restores classic swipe boosting");

            NativeTouchSample penWithBoostOff = new();
            penWithBoostOff.StepAbsolute(true, 0, 0, allowMovementBoost: false);
            penWithBoostOff.StepAbsolute(true, 0, 0, allowMovementBoost: false);
            penWithBoostOff.StepAbsolute(true, 150, 0, allowMovementBoost: false);
            Check(penWithBoostOff.Current.Delta4X == 150
                    && !penWithBoostOff.Current.Continued
                    && penWithBoostOff.TakeRollShare() == .5f,
                "stylus boost setting independently suppresses classic pen flicks");

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
            
            // Boundary and lifecycle checks are intentionally over the real sample
            // object, not over a hand-authored approximation of its network state.
            remote.ApplyRemote(swipe with { SampleSequence = 9, SecondStep = false }, 1, 1);
            Check(remote.TakeRollShare() == 0.5f, "next sample");
            remote.ApplyRemote(swipe with { SampleSequence = 10, SecondStep = false }, 1, 2);
            Check(remote.TakeRollShare() == 0.5f, "new life accepts fresh sample");
            remote.ApplyRemote(swipe with { SampleSequence = 9, SecondStep = true }, 1, 2);
            Check(remote.TakeRollShare() == 0, "stale within current life");
            remote.ApplyRemote(swipe with { SampleSequence = 1, SecondStep = false }, 2, 1);
            Check(remote.TakeRollShare() == 0.5f, "slot replacement resets report identity");
            NativeTouchSample wrapped = new();
            wrapped.ApplyRemote(swipe with { SampleSequence = uint.MaxValue, SecondStep = false }, 4, 2);
            wrapped.TakeRollShare();
            wrapped.ApplyRemote(swipe with { SampleSequence = 0, SecondStep = false }, 4, 2);
            Check(wrapped.TakeRollShare() == 0.5f, "sequence rollover remains admissible");
            wrapped.ApplyRemote(swipe with { SampleSequence = uint.MaxValue, SecondStep = false }, 4, 2);
            Check(wrapped.TakeRollShare() == 0, "pre-wrap packet cannot rewind sample");
            state.UpdateAbsolute(true, 255, 191);
            state.UpdateAbsolute(true, 0, 0);
            Check(state.Delta4X == -255 && state.Delta4Y == -191,
                "absolute coordinates clamp and reverse correctly");
            foreach (float factor in new[] { 0f, .35f, 1f })
            {
                (float X, float Z) contribution = MorphBallTouchRules.Roll(15, -20, .5f, factor,
                    1, 0, 0, 1);
                (float X, float Z) native = MorphBallTouchRules.Roll(15, -20, 1f, factor,
                    1, 0, 0, 1);
                Check(MathF.Abs(2 * contribution.X - native.X) < 0.00001f
                    && MathF.Abs(2 * contribution.Z - native.Z) < 0.00001f,
                    "jump-pad scaling conserves native impulse");
            }
            NativeTouchSample throttled = new();
            throttled.ApplyRemote(swipe with { SampleSequence = 50, SecondStep = false }, 9, 2, 100);
            Check(throttled.TakeRollShare() == .5f, "valid first half");
            throttled.ApplyRemote(swipe with { SampleSequence = 51, SecondStep = false }, 9, 2, 101);
            Check(throttled.TakeRollShare() == 0, "new sample every 60 Hz frame rejected");
            throttled.ApplyRemote(swipe with { SampleSequence = 51, SecondStep = false }, 9, 2, 102);
            Check(throttled.TakeRollShare() == .5f, "valid next native sample");
            throttled.ApplyRemote(swipe with { SampleSequence = 52, SecondStep = true }, 9, 2, 103);
            Check(throttled.TakeRollShare() == 0, "second-only new sample cannot immediately follow first");
            throttled.ApplyRemote(swipe with { SampleSequence = 52, SecondStep = true }, 9, 2, 104);
            Check(throttled.TakeRollShare() == .5f, "second-only recovery after lost first");
            Span<byte> malicious = stackalloc byte[NativeTouchReport.WireSize];
            new NativeTouchReport(true, true, true, 2000, 0, 2, false).Write(malicious);
            Check(!NativeTouchReport.Read(malicious).Active, "oversized forged touch delta rejected");
            Console.WriteLine("NATIVE TOUCH PASS");
        }
    }
}
