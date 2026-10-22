using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-free contracts only. No model, pose, renderer or gameplay qualification.
internal static class TraversalWalkPresentationTests
{
    private const float Reach = .8f;
    private static int _checks;
    private static Vector3 _allocationSink;
    private static void Check(bool good, string message)
    { _checks++; if (!good) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message, float tolerance = .00002f)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
            $"{message}: {actual} != {expected}");
    private static void Same(Vector3 actual, Vector3 expected, string message, float tolerance = .00002f)
        => Near((actual - expected).Length, 0, message, tolerance);
    private static void Same(TraversalWalkSample actual, TraversalWalkSample expected, string message)
    {
        Near(actual.Phase, expected.Phase, message + " phase");
        Near(actual.Amount, expected.Amount, message + " amount");
        Same(actual.Direction, expected.Direction, message + " direction");
        Same(actual.Normal, expected.Normal, message + " normal");
    }

    internal static int CheckMath()
    {
        _checks = 0;
        void Case(string name, Action run)
        {
            try { run(); }
            catch (Exception error) { throw new InvalidOperationException("TRAVERSAL WALK " + name + ": " + error.Message, error); }
        }
        Case("completed-distance-and-six-tick-blends", DistanceAndBlend);
        Case("first-six-ticks-seed-accepted-heading", StartupHeading);
        Case("planted-forward-constant-speed", () => PlantedFeet(Vector3.UnitZ, false));
        Case("planted-forward-variable-speed", () => PlantedFeet(Vector3.UnitZ, true));
        Case("planted-sideways-constant-speed", () => PlantedFeet(Vector3.UnitX, false));
        Case("planted-diagonal-variable-speed", () => PlantedFeet(new Vector3(1, 0, 1).Normalized(), true));
        Case("support-swing-and-both-foot-wraps", StepContract);
        Case("local-node-rigid-blend", LocalNodeBlend);
        Case("direction-reversal-finite-and-bounded", Reversal);
        Case("duplicate-discontinuity-reset-and-alpha", Lifecycle);
        Case("ground-normal-fallback", GroundNormal);
        Case("held-walking-and-batched-arrival-cadence", HeldWalkingCadence);
        Case("feasible-knee-floor-circle-preserves-segment-lengths", KneeFloor);
        Case("warmed-capture-sample-step-zero-allocation", Allocation);
        return _checks;
    }

    private static void DistanceAndBlend()
    {
        var walk = new TraversalWalkPresentation();
        Vector3 position = Vector3.Zero; double accepted = 0;
        walk.Capture(1, position, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Amount, 0, "initial capture cannot invent a completed step");
        for (ulong frame = 2; frame <= 61; frame++)
        {
            double previousAccepted = accepted;
            Vector3 previous = position;
            position += Vector3.UnitZ * (.007f + (frame % 5) * .003f);
            accepted += (position - previous).Xz.Length;
            walk.Capture(frame, position, true, Vector3.UnitY, Reach);
            var sample = walk.Sample(1);
            double phase = accepted / (Reach * TraversalWalkPresentation.CycleReach);
            phase -= Math.Floor(phase);
            Near(sample.Phase, (float)phase, "phase follows completed accepted distance", .000002f);
            foreach (float alpha in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                double interpolated = (previousAccepted + (accepted - previousAccepted) * alpha)
                    / (Reach * TraversalWalkPresentation.CycleReach);
                interpolated -= Math.Floor(interpolated);
                Near(walk.Sample(alpha).Phase, (float)interpolated,
                    "phase interpolation follows accepted distance through cycle wraps", .000002f);
            }
            Near(sample.Amount, Math.Min((frame - 1) / 6f, 1), "six moving ticks reach full amount");
            if (frame <= 7)
                Near(walk.Sample(.5f).Amount, ((frame - 2) / 6f + sample.Amount) * .5f,
                    "render interpolation blends adjacent amounts");
        }
        float completedPhase = walk.Sample(1).Phase;
        for (ulong frame = 62; frame <= 67; frame++)
        {
            walk.Capture(frame, position, false, Vector3.UnitY, Reach);
            Near(walk.Sample(1).Amount, Math.Max(1 - (frame - 61) / 6f, 0), "six exit ticks reach zero amount");
            Near(walk.Sample(1).Phase, completedPhase, "no accepted walking travel leaves phase unchanged");
        }
        walk.Capture(68, position, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Amount, 0, "held walking flag without accepted travel cannot animate a step");
        position += Vector3.UnitZ * .02f;
        walk.Capture(69, position, true, Vector3.UnitY, float.NaN);
        Near(walk.Sample(1).Phase, completedPhase, "invalid leg reach cannot advance phase");
        Near(walk.Sample(1).Amount, 0, "invalid leg reach cannot start the walk blend");
    }

    private static void StartupHeading()
    {
        foreach (Vector3 heading in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX,
            new Vector3(1, 0, 1).Normalized() })
        {
            var walk = new TraversalWalkPresentation(); Vector3 position = Vector3.Zero;
            walk.Capture(1, position, true, Vector3.UnitY, Reach);
            for (ulong frame = 2; frame <= 7; frame++)
            {
                position += heading * .012f;
                walk.Capture(frame, position, true, Vector3.UnitY, Reach);
                Same(walk.Sample(1).Direction, heading, "startup direction follows the first accepted travel heading");
                Same(walk.Sample(.5f).Direction, heading, "partially blended startup does not rotate in from a default heading");
                Near(walk.Sample(1).Amount, (frame - 1) / 6f, "accepted heading seed retains the six-tick amount blend");
                if (frame == 2)
                    Near(walk.Sample(0).Amount, 0, "seeding the previous direction occurs only at invisible previous weight");
            }
        }
    }

    private static void HeldWalkingCadence()
    {
        const float distance = .04f;
        foreach (float[] arrivals in new[] {
            new[] { 0f, 0f, 2f, 2f, 4f, 4f, 4f, 7f, 7f, 10f },
            new[] { 0f, 0f, 0f, 0f, 4f, 4f, 4f, 4f, 8f, 8f, 8f, 12f } })
        {
            var walk = new TraversalWalkPresentation(); Vector3 position = Vector3.Zero;
            ulong frame = 1; double completedDistance = 0;
            walk.Capture(frame, position, true, Vector3.UnitY, Reach);
            // Establish an accepted nondefault direction through unchanged local
            // captures before the remote cadence starts holding/batching positions.
            for (int tick = 0; tick < 6; tick++)
            {
                Vector3 before = position; position += Vector3.UnitX * distance;
                completedDistance += (position - before).Xz.Length;
                walk.Capture(++frame, position, true, Vector3.UnitY, Reach);
            }
            Near(walk.Sample(1).Amount, 1, "cadence fixture begins with a fully blended actual local walk");
            Vector3 origin = position;
            foreach (float arrival in arrivals)
            {
                Vector3 before = position; TraversalWalkSample previous = walk.Sample(1);
                position = origin + Vector3.UnitX * (arrival * distance);
                float accepted = (position - before).Xz.Length; completedDistance += accepted;
                walk.Capture(++frame, position, true, Vector3.UnitY, Reach, heldWalking: true);
                TraversalWalkSample current = walk.Sample(1);
                Near(current.Amount, 1, "held walking pose survives missing and batched positional arrivals");
                double phase = completedDistance / (Reach * TraversalWalkPresentation.CycleReach);
                phase -= Math.Floor(phase);
                Near(current.Phase, (float)phase, "cadence advances phase only by actually accepted completed position distance", .000002f);
                if (accepted <= .00001f)
                {
                    Near(current.Phase, previous.Phase, "a held arrival never invents walking distance");
                    Same(current.Direction, previous.Direction, "a held arrival cannot turn the last accepted travel direction");
                }
                TraversalWalkPresentation captured = walk;
                for (int repeat = 0; repeat < 3; repeat++)
                    Same(walk.Sample(1), current, "repeated cadence pictures do not advance the completed walk sample");
                Check(walk.Equals(captured), "walk sampling cannot mutate held or batched capture state");
                walk.Capture(frame, new(100, 100, 100), false, Vector3.UnitX, 0, heldWalking: false);
                Check(walk.Equals(captured), "duplicate cadence capture cannot replace pose level, direction or phase");
            }
            float heldPhase = walk.Sample(1).Phase; Vector3 heldDirection = walk.Sample(1).Direction;
            for (int tick = 1; tick <= 6; tick++)
            {
                // A held hint does not override an explicitly false walking level.
                position += Vector3.UnitZ * .01f;
                walk.Capture(++frame, position, false, Vector3.UnitY, Reach, heldWalking: true);
                Near(walk.Sample(1).Amount, Math.Max(1 - tick / 6f, 0), "false walking level ends the held pose in six ticks");
                Near(walk.Sample(1).Phase, heldPhase, "nonwalking movement does not spend a footwork cycle");
                Same(walk.Sample(1).Direction, heldDirection, "nonwalking movement cannot turn the footwork heading");
            }
            for (int tick = 1; tick <= 6; tick++)
            {
                walk.Capture(++frame, position, true, Vector3.UnitY, Reach, heldWalking: true);
                Near(walk.Sample(1).Amount, tick / 6f, "accepted held walking level can rebuild the pose without new distance");
                Near(walk.Sample(1).Phase, heldPhase, "held pose entry cannot manufacture distance phase");
                Same(walk.Sample(1).Direction, heldDirection, "held pose entry retains the last accepted direction");
            }
            for (int tick = 1; tick <= 6; tick++)
            {
                walk.Capture(++frame, position, true, Vector3.UnitY, Reach, heldWalking: false);
                Near(walk.Sample(1).Amount, Math.Max(1 - tick / 6f, 0), "no held speed and no accepted travel ends the pose");
                Near(walk.Sample(1).Phase, heldPhase, "zero-speed fade does not spend footwork phase");
            }
            foreach (float invalidReach in new[] { 0f, .01f, -1f, float.NaN, float.PositiveInfinity })
            {
                walk.Capture(++frame, position, true, Vector3.UnitY, invalidReach, heldWalking: true);
                Near(walk.Sample(1).Amount, 0, "held walking cannot bypass the finite positive leg-reach gate");
                Near(walk.Sample(1).Phase, heldPhase, "invalid held reach cannot poison completed distance phase");
            }
            walk.Capture(frame + 2, position, true, Vector3.UnitY, Reach, heldWalking: true);
            Near(walk.Sample(1).Amount, 0, "held walking cannot bridge a missing simulation capture");
            Near(walk.Sample(1).Phase, 0, "held capture discontinuity resets phase rather than inventing a stride");
        }

        var fresh = new TraversalWalkPresentation();
        fresh.Capture(1, Vector3.Zero, true, Vector3.UnitY, Reach, heldWalking: true);
        for (ulong frame = 2; frame <= 7; frame++)
        {
            fresh.Capture(frame, Vector3.Zero, true, Vector3.UnitY, Reach, heldWalking: true);
            Near(fresh.Sample(1).Amount, (frame - 1) / 6f, "fresh accepted held walking builds the amount across repeated positions");
            Near(fresh.Sample(1).Phase, 0, "fresh held entry has no invented initial stride phase");
            Same(fresh.Sample(1).Direction, Vector3.UnitZ, "without accepted direction travel the held entry stays finite and unchanged");
        }
        fresh.Capture(8, Vector3.UnitX * .02f, true, Vector3.UnitY, Reach, heldWalking: true);
        Same(fresh.Sample(1).Direction, Vector3.UnitX, "first arriving travel seeds its heading even after held entry reaches full weight");
        Same(fresh.Sample(.5f).Direction, Vector3.UnitX, "first arriving heading does not turn in from an invented default");
    }

    private static float FootPhase(float phase, bool right)
    {
        float p = phase + (right ? .5f : 0);
        return p - MathF.Floor(p);
    }

    private static Vector3 FootWorld(Vector3 body, TraversalWalkSample sample, bool right)
    {
        Vector2 step = TraversalWalkPresentation.Step(sample.Phase, right, Reach);
        return body + (sample.Direction * step.X + sample.Normal * step.Y) * sample.Amount;
    }

    private static void PlantedFeet(Vector3 heading, bool variable)
    {
        var walk = new TraversalWalkPresentation(); Vector3 position = Vector3.Zero;
        walk.Capture(1, position, true, Vector3.UnitY, Reach);
        int leftPairs = 0, rightPairs = 0;
        for (ulong frame = 2; frame < 700; frame++)
        {
            Vector3 before = position; TraversalWalkSample previous = walk.Sample(1);
            float distance = variable ? .004f + (frame % 7) * .002f : .012f;
            position += heading * distance;
            walk.Capture(frame, position, true, Vector3.UnitY, Reach);
            TraversalWalkSample current = walk.Sample(1);
            if (previous.Amount != 1 || current.Amount != 1) continue;
            foreach (bool right in new[] { false, true })
            {
                float a = FootPhase(previous.Phase, right), b = FootPhase(current.Phase, right);
                if (b < a || a > TraversalWalkPresentation.SupportFraction
                    || b > TraversalWalkPresentation.SupportFraction) continue;
                Same(FootWorld(position, current, right), FootWorld(before, previous, right),
                    $"fully blended {(right ? "right" : "left")} support foot stays planted at frame {frame}");
                foreach (float alpha in new[] { .25f, .5f, .75f })
                    Same(FootWorld(Vector3.Lerp(before, position, alpha), walk.Sample(alpha), right),
                        FootWorld(before, previous, right), "fractional pictures retain the same straight planted support foot");
                if (right) rightPairs++; else leftPairs++;
            }
        }
        Check(leftPairs > 100 && rightPairs > 100, "both feet demonstrate sustained planted intervals over multiple cycles");
    }

    private static void StepContract()
    {
        float cycle = Reach * TraversalWalkPresentation.CycleReach;
        float span = cycle * TraversalWalkPresentation.SupportFraction;
        const float epsilon = .00001f;
        foreach (bool right in new[] { false, true })
        {
            float origin = right ? .5f : 0;
            Vector2 start = TraversalWalkPresentation.Step(origin, right, Reach);
            Vector2 endSupport = TraversalWalkPresentation.Step(origin + TraversalWalkPresentation.SupportFraction, right, Reach);
            Near(start.X, span * .5f, "support starts forward"); Near(start.Y, 0, "support begins on the floor");
            Near(endSupport.X, -span * .5f, "support ends behind"); Near(endSupport.Y, 0, "support ends on the floor");
            Vector2 beforeSupport = TraversalWalkPresentation.Step(origin + TraversalWalkPresentation.SupportFraction - epsilon, right, Reach);
            Vector2 afterSupport = TraversalWalkPresentation.Step(origin + TraversalWalkPresentation.SupportFraction + epsilon, right, Reach);
            Near((beforeSupport - afterSupport).Length, 0, "support/swing endpoint is continuous", .00005f);
            Vector2 beforeWrap = TraversalWalkPresentation.Step(origin - epsilon, right, Reach);
            Vector2 afterWrap = TraversalWalkPresentation.Step(origin + epsilon, right, Reach);
            Near((beforeWrap - afterWrap).Length, 0, "each foot wraps continuously from swing into support", .00005f);
            // One-sided finite differences independently require the same stance
            // velocity on either side of both joins, including zero lift speed.
            const float derivativeStep = .0005f;
            foreach (float join in new[] { origin, origin + TraversalWalkPresentation.SupportFraction })
            {
                Vector2 at = TraversalWalkPresentation.Step(join, right, Reach);
                Vector2 incoming = (at - TraversalWalkPresentation.Step(join - derivativeStep, right, Reach)) / derivativeStep;
                Vector2 outgoing = (TraversalWalkPresentation.Step(join + derivativeStep, right, Reach) - at) / derivativeStep;
                Near(incoming.X, -cycle, "incoming endpoint velocity matches planted support", .003f);
                Near(outgoing.X, -cycle, "outgoing endpoint velocity matches planted support", .003f);
                Near(incoming.Y, 0, "incoming vertical endpoint velocity vanishes", .003f);
                Near(outgoing.Y, 0, "outgoing vertical endpoint velocity vanishes", .003f);
            }
            float peakPhase = origin + TraversalWalkPresentation.SupportFraction
                + (1 - TraversalWalkPresentation.SupportFraction) * .5f;
            Vector2 peak = TraversalWalkPresentation.Step(peakPhase, right, Reach);
            Near(peak.X, 0, "swing passes through the middle");
            Near(peak.Y, Reach * TraversalWalkPresentation.FootLift, "swing has the specified low lift");
            // The extrema solve 30*s²*(1-s)² = 1-SupportFraction.
            // At the declared .62 support fraction their follow-through is
            // below .04 cycle lengths; support itself keeps its exact span.
            for (int index = -120; index <= 240; index++)
            {
                float phase = index / 120f;
                Vector2 step = TraversalWalkPresentation.Step(phase, right, Reach);
                Vector2 repeated = TraversalWalkPresentation.Step(phase + 1, right, Reach);
                Near((step - repeated).Length, 0, "step is periodic for negative and wrapped phases");
                Check(float.IsFinite(step.LengthSquared) && MathF.Abs(step.X) <= span * .5f + cycle * .04f + .000001f
                    && step.Y >= -.000001f && step.Y <= Reach * TraversalWalkPresentation.FootLift + .000001f,
                    "step remains finite, within bounded airborne follow-through, above the floor and below the lift bound");
                if (FootPhase(phase, right) <= TraversalWalkPresentation.SupportFraction)
                {
                    Near(step.Y, 0, "support interval never lifts the planted foot");
                    Near(step.X, span * .5f - cycle * FootPhase(phase, right),
                        "support stays exactly linear despite airborne follow-through");
                }
            }
        }
    }

    private static void LocalNodeBlend()
    {
        Matrix4 from = Matrix4.CreateScale(1, 2, 3) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(170))
            * Matrix4.CreateTranslation(2, -4, 6);
        Matrix4 to = Matrix4.CreateScale(3, 4, 5) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(-170))
            * Matrix4.CreateTranslation(-6, 8, 10);
        Check(Model.BlendLocalNode(from, to, 0) == from && Model.BlendLocalNode(from, to, -1) == from,
            "zero and clamped start return the exact native matrix");
        Check(Model.BlendLocalNode(from, to, 1) == to && Model.BlendLocalNode(from, to, 2) == to,
            "one and clamped end return the exact neutral matrix");
        foreach (Matrix4 optional in new[] { Matrix4.CreateScale(0, 2, 3), Matrix4.CreateScale(-1, 2, 3) })
        {
            Check(Model.BlendLocalNode(optional, to, 0) == optional
                && Model.BlendLocalNode(from, optional, 1) == optional,
                "collapsed and reflected rig endpoints remain exact");
            foreach (float weight in new[] { .00001f, .5f, .99999f })
                foreach (Matrix4 result in new[] { Model.BlendLocalNode(optional, to, weight), Model.BlendLocalNode(from, optional, weight) })
                {
                    foreach (Vector4 row in new[] { result.Row0, result.Row1, result.Row2, result.Row3 })
                        Check(float.IsFinite(row.X) && float.IsFinite(row.Y) && float.IsFinite(row.Z) && float.IsFinite(row.W),
                            "optional collapsed/reflected rig blend stays finite in both directions");
                    Near(result.Row0.W, 0, "optional blend keeps affine first row");
                    Near(result.Row1.W, 0, "optional blend keeps affine second row");
                    Near(result.Row2.W, 0, "optional blend keeps affine third row");
                    Near(result.Row3.W, 1, "optional blend keeps affine translation row");
                }
        }
        foreach (float amount in new[] { .00001f, .25f, .5f, .75f, .99999f })
        {
            Matrix4 result = Model.BlendLocalNode(from, to, amount);
            Same(result.Row3.Xyz, new Vector3(2 - 8 * amount, -4 + 12 * amount, 6 + 4 * amount),
                "translation follows the declared blend weight");
            Vector3 x = result.Row0.Xyz, y = result.Row1.Xyz, z = result.Row2.Xyz;
            Near(x.Length, 1 + 2 * amount, "first scale interpolates without shrinkage");
            Near(y.Length, 2 + 2 * amount, "second scale interpolates without shrinkage");
            Near(z.Length, 3 + 2 * amount, "third scale interpolates without shrinkage");
            x.Normalize(); y.Normalize(); z.Normalize();
            Near(Vector3.Dot(x, y), 0, "rotation keeps x/y orthogonal");
            Near(Vector3.Dot(y, z), 0, "rotation keeps y/z orthogonal");
            Near(Vector3.Dot(z, x), 0, "rotation keeps z/x orthogonal");
            Near(Vector3.Dot(Vector3.Cross(x, y), z), 1, "rotation preserves handedness");
            // Independent expected axis: the short arc crosses 180 degrees,
            // while the long arc incorrectly passes through identity.
            Matrix4 expected = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(170 + 20 * amount));
            Same(x, expected.Row0.Xyz, "rotation follows the short arc across the angle wrap");
            Same(z, expected.Row2.Xyz, "short-arc rotation preserves the third axis");
        }
    }

    private static void Reversal()
    {
        var walk = new TraversalWalkPresentation(); Vector3 position = Vector3.Zero;
        walk.Capture(1, position, true, Vector3.UnitY, Reach);
        for (ulong frame = 2; frame < 100; frame++)
        {
            Vector3 previousDirection = walk.Sample(1).Direction;
            position += (frame < 40 ? Vector3.UnitZ : -Vector3.UnitZ) * .02f;
            walk.Capture(frame, position, true, Vector3.UnitY, Reach);
            Vector3 currentDirection = walk.Sample(1).Direction;
            Near(currentDirection.Length, 1, "reversal direction stays unit length");
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(previousDirection, currentDirection), -1, 1));
            Check(float.IsFinite(angle) && angle <= MathF.PI * .35f + .00001f,
                "direction reversal has the specified bounded turn per accepted tick");
            foreach (float alpha in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                TraversalWalkSample sample = walk.Sample(alpha);
                Check(float.IsFinite(sample.Direction.LengthSquared) && float.IsFinite(sample.Normal.LengthSquared),
                    "interpolated reversal is finite");
                Near(sample.Direction.Length, 1, "interpolated direction is normalized");
                foreach (bool right in new[] { false, true })
                    Check((FootWorld(position, sample, right) - position).Length <= Reach,
                        "reversal cannot place either foot outside the bounded leg reach");
            }
        }
    }

    private static void Lifecycle()
    {
        var walk = new TraversalWalkPresentation(); Vector3 position = Vector3.Zero;
        walk.Capture(1, position, true, Vector3.UnitY, Reach);
        for (ulong frame = 2; frame <= 9; frame++)
        { position += Vector3.UnitZ * .03f; walk.Capture(frame, position, true, Vector3.UnitY, Reach); }
        TraversalWalkPresentation baseline = walk;
        TraversalWalkSample accepted = walk.Sample(1);
        walk.Capture(9, new(100, 100, 100), false, Vector3.UnitX, 0);
        Same(walk.Sample(1), accepted, "duplicate frame cannot replace accepted presentation state");
        position += Vector3.UnitZ * .04f;
        walk.Capture(10, position, true, Vector3.UnitY, Reach);
        baseline.Capture(10, position, true, Vector3.UnitY, Reach);
        Same(walk.Sample(1), baseline.Sample(1), "duplicate position cannot contaminate the next accepted delta");
        Same(walk.Sample(-1), walk.Sample(0), "negative alpha clamps to previous state");
        Same(walk.Sample(2), walk.Sample(1), "alpha above one clamps to current state");
        foreach (float alpha in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Same(walk.Sample(alpha), walk.Sample(1), "invalid alpha falls back to current state");
        walk.Capture(12, position + Vector3.UnitZ * .01f, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Phase, 0, "missing simulation frame resets phase");
        Near(walk.Sample(1).Amount, 0, "missing simulation frame resets blend");
        walk.Capture(13, position + Vector3.UnitZ * 4.01f, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Amount, 0, "four-unit discontinuity cannot be interpreted as accepted step distance");
        walk.Capture(ulong.MaxValue, Vector3.Zero, true, Vector3.UnitY, Reach);
        walk.Capture(0, Vector3.UnitZ * .01f, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Amount, 0, "frame-number wrap resets continuity");
        walk.Reset();
        TraversalWalkSample reset = walk.Sample(1);
        Near(reset.Amount, 0, "explicit reset clears blend"); Near(reset.Phase, 0, "explicit reset clears phase");
        Same(reset.Direction, Vector3.UnitZ, "reset supplies a finite default direction");
        Same(reset.Normal, Vector3.UnitY, "reset supplies a finite default ground normal");
        walk.Capture(1, new(float.NaN, 0, 0), true, Vector3.UnitY, Reach);
        walk.Capture(2, Vector3.Zero, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Amount, 0, "nonfinite prior position cannot manufacture a completed delta");
        walk.Capture(3, Vector3.UnitZ * .01f, true, Vector3.UnitY, Reach);
        Near(walk.Sample(1).Amount, 1f / 6, "finite continuity can resume after a rejected positional delta");
    }

    private static void GroundNormal()
    {
        var walk = new TraversalWalkPresentation(); ulong frame = 1;
        foreach (Vector3 normal in new[] { Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitY * 2,
            new Vector3(float.NaN, 1, 0), new Vector3(0, float.PositiveInfinity, 0),
            new Vector3(0, .49f, MathF.Sqrt(1 - .49f * .49f)) })
        {
            walk.Capture(frame, Vector3.UnitZ * (frame * .01f), true, normal, Reach); frame++;
            Same(walk.Sample(1).Normal, Vector3.UnitY, "absent, steep, nonunit or nonfinite normal falls back to flat ground");
        }
        Vector3 slope = new Vector3(0, 4, -1).Normalized();
        walk.Capture(frame, Vector3.UnitZ * (frame * .01f), true, slope, Reach);
        Same(walk.Sample(1).Normal, slope, "valid grounded normal retained");
        Near(walk.Sample(.5f).Normal.Length, 1, "interpolated ground normal stays normalized");
    }

    private static void Allocation()
    {
        var walk = new TraversalWalkPresentation(); ulong frame = 1; Vector3 position = Vector3.Zero;
        walk.Capture(frame, position, true, Vector3.UnitY, Reach);
        for (int index = 0; index < 2048; index++) AllocationStep(ref walk, ref frame, ref position);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 2048; index++) AllocationStep(ref walk, ref frame, ref position);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(float.IsFinite(_allocationSink.LengthSquared), "allocation loop retains computed presentation and both step outputs");
        Check(bytes == 0, "warmed Capture, Sample and both-foot Step paths allocate zero managed bytes");
    }

    private static void KneeFloor()
    {
        int adjusted = 0;
        foreach (Vector3 normal in new[] { Vector3.UnitY, new Vector3(0, 4, -1).Normalized() })
        foreach (var leg in new[] {
            (Hip: new Vector3(0, .25f, 0), Ankle: new Vector3(.5f, 0, 0), Thigh: .6f, Shin: .6f),
            (Hip: new Vector3(.2f, .5f, -.3f), Ankle: new Vector3(.9f, .05f, .2f), Thigh: .7f, Shin: .75f) })
        foreach (Vector3 pole in new[] { -normal, Vector3.UnitZ, Vector3.UnitX })
        {
            Vector3 axis = leg.Ankle - leg.Hip;
            float distance = axis.Length;
            axis /= distance;
            float along = (leg.Thigh * leg.Thigh - leg.Shin * leg.Shin + distance * distance) / (2 * distance);
            Vector3 center = leg.Hip + axis * along;
            float radius = MathF.Sqrt(leg.Thigh * leg.Thigh - along * along);
            float projected = MathF.Sqrt(MathF.Max(0, 1 - MathF.Pow(Vector3.Dot(normal, axis), 2)));
            Check(Vector3.Dot(normal, center - leg.Ankle) + radius * projected > .02f,
                "floor-clearance contract uses a genuinely feasible fixed hip-ankle circle");
            Vector3 before = PlayerEntity.ModCrouchKnee(leg.Hip, leg.Ankle, leg.Thigh, leg.Shin, pole);
            Vector3 knee = PlayerEntity.ModCrouchKneeAboveFloor(leg.Hip, leg.Ankle, leg.Thigh, leg.Shin,
                pole, normal, leg.Ankle);
            Near((knee - leg.Hip).Length, leg.Thigh, "floor pole adjustment preserves thigh length");
            Near((leg.Ankle - knee).Length, leg.Shin, "floor pole adjustment preserves shin length");
            Check(float.IsFinite(knee.LengthSquared) && Vector3.Dot(normal, knee - leg.Ankle) >= .015f - .00002f,
                "feasible low knee clears the foot plane without changing the ankle");
            if (Vector3.Dot(normal, before - leg.Ankle) < .015f) adjusted++;
            else Same(knee, before, "a naturally clear knee keeps its original canonical bend pole");
            Same(knee, PlayerEntity.ModCrouchKneeAboveFloor(leg.Hip, leg.Ankle, leg.Thigh, leg.Shin,
                pole, normal, leg.Ankle), "repeated knee-floor solving is deterministic");
        }
        Check(adjusted >= 2, "the fixture actually exercises below-floor poles rather than only pass-through knees");
    }

    private static void AllocationStep(ref TraversalWalkPresentation walk, ref ulong frame, ref Vector3 position)
    {
        position += Vector3.UnitZ * .01f;
        walk.Capture(++frame, position, true, Vector3.UnitY, Reach);
        TraversalWalkSample sample = walk.Sample(.5f);
        Vector2 left = TraversalWalkPresentation.Step(sample.Phase, false, Reach);
        Vector2 right = TraversalWalkPresentation.Step(sample.Phase, true, Reach);
        _allocationSink = sample.Direction * (left.X + right.X) + sample.Normal * (left.Y + right.Y + sample.Amount);
    }
}
