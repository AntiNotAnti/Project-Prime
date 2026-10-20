using System;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Stateless shape/profile contracts. Real keyboard, clearance and slide lifetime
// are exercised separately by TraversalStanceSceneTests.
internal static class TraversalStanceTests
{
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float tolerance = .000002f)
        => Check(float.IsFinite(a) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} != {b}");
    private static MomentumInput Input(Vector2 axes, bool grounded = true)
        => new(axes, Vector3.UnitZ, Vector3.UnitX, TraversalMovementProfileV2.GroundSpeed, grounded, 1f / 60);

    internal static int Run()
    {
        try
        {
            _checks = 0;
            Check(TraversalStance.HeightRatio == .65f && TraversalMovementProfileV2.ProfileVersion == 2
                && TraversalMovementProfileV1.ProfileVersion == 1,
                "new stance profile preserves immutable historical Traversal v1");
            Check(MovementModelRules.ProfileVersion(MovementModel.Traversal) == 5,
                "live Traversal selects profile five while historical stance profiles remain immutable");
            Check(TraversalMovementProfileV2.SlideTicks == 48
                && TraversalMovementProfileV2.SlideMinimumSpeed == TraversalMovementProfileV2.GroundSpeed * 1.1f
                && TraversalMovementProfileV2.CrouchSpeed == TraversalMovementProfileV2.GroundSpeed * .5f,
                "reviewed finite slide window, earned entry threshold and crouch speed");
            Check(TraversalMovementProfileV3.ProfileVersion == 3
                && TraversalMovementProfileV3.SlideTicks == TraversalMovementProfileV2.SlideTicks
                && TraversalMovementProfileV3.SlideEntryTicks == 6
                && TraversalMovementProfileV3.SlideMinimumSpeed == TraversalMovementProfileV3.GroundSpeed * .9f
                && TraversalMovementProfileV3.CrouchSpeed == TraversalMovementProfileV2.CrouchSpeed
                && TraversalMovementProfileV3.Tuning == TraversalMovementProfileV2.Tuning,
                "V3 admits earned running slides and a six-tick entry buffer without changing established solver coefficients");
            Check(TraversalMovementProfileV4.ProfileVersion == 4 && TraversalMovementProfileV4.SlideTicks == 72
                && TraversalMovementProfileV4.CrouchTuning(true).GroundFriction == .45f
                && TraversalMovementProfileV4.SlideEntryTicks == TraversalMovementProfileV3.SlideEntryTicks
                && TraversalMovementProfileV4.SlideMinimumSpeed == TraversalMovementProfileV3.SlideMinimumSpeed
                && TraversalMovementProfileV4.CrouchSpeed == TraversalMovementProfileV3.CrouchSpeed
                && TraversalMovementProfileV4.Tuning == TraversalMovementProfileV3.Tuning,
                "V4 selects a 72-tick slide with lower friction and preserves the prior entry, crouch and standing policy");
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
                Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                var values = Metadata.PlayerValues[(int)hunter];
                float bottom = Fixed.ToFloat(values.MinPickupHeight), top = Fixed.ToFloat(values.MaxPickupHeight);
                float radius = Fixed.ToFloat(values.BipedColRadius), eye = Fixed.ToFloat(values.AimYOffset);
                Check(TraversalStance.Top(bottom, top, radius, false) == top
                    && TraversalStance.EyeOffset(bottom, top, radius, eye, false) == eye,
                    hunter + " standing shape and eye are bit-exact native values");
                float crouched = TraversalStance.Top(bottom, top, radius, true);
                Near(crouched - bottom, MathF.Max(radius * 2, (top - bottom) * .65f),
                    hunter + " crouch scales height around unchanged feet");
                Check(crouched < top && crouched - bottom >= radius * 2,
                    hunter + " shortened collision shape retains a valid sphere diameter");
                float crouchedEye = TraversalStance.EyeOffset(bottom, top, radius, eye, true);
                Check(crouchedEye < eye && crouchedEye > bottom && crouchedEye <= crouched,
                    hunter + " eye moves down inside accepted physical shape");
                Near((crouchedEye - bottom) / (eye - bottom), (crouched - bottom) / (top - bottom),
                    hunter + " eye and collision height share the same feet anchor");
            }
            Near(TraversalStance.Top(-.5f, .1f, .4f, true), .3f,
                "capsule diameter is the lower bound for very short shapes");
            Check(TraversalStance.CustomMuzzleOffset(false) == .4f
                && TraversalStance.CustomTraceOffset(false) == .2f, "standing custom source offsets remain exact");
            Near(TraversalStance.CustomMuzzleOffset(true), -.5f + .9f * .65f,
                "crouched custom muzzle follows accepted feet-anchored stance");
            Near(TraversalStance.CustomTraceOffset(true), -.5f + .7f * .65f,
                "crouched custom trace follows the same accepted stance");
            Profile(); Allocation(); _checks += TraversalCrouchPoseTests.CheckMath();
            Console.WriteLine($"TRAVERSAL STANCE RULES PASS {_checks} assertions; asset-free shape and profile contracts");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("TRAVERSAL STANCE RULES FAIL " + error); return 1; }
    }

    private static void Profile()
    {
        var neutral = Input(Vector2.Zero);
        Vector3 incoming = new(TraversalMovementProfileV2.SlideMinimumSpeed * 1.5f, -.125f, 3);
        var slide = TraversalMovementProfileV2.Step(incoming, neutral, true, true);
        var standing = TraversalMovementProfileV2.Step(incoming, neutral);
        Check(slide.Velocity.Xz.Length < incoming.Xz.Length
            && slide.Velocity.Xz.Length > standing.Velocity.Xz.Length,
            "slide reduces friction without a speed bonus");
        Check(!slide.SpeedLimited && !slide.IncomingOverCap, "slide preserves existing uncapped profile diagnostics");
        Near(slide.Velocity.X, incoming.X * (1 - .8f / 60), "neutral slide uses finite reviewed friction");
        Near(slide.Velocity.Z, incoming.Z * (1 - .8f / 60), "slide friction scales both planar axes");
        Check(TraversalMovementProfileV2.Step(incoming, Input(new Vector2(0, 1)), true, true).Velocity == slide.Velocity,
            "held movement cannot accelerate or steer a grounded slide");
        Vector3 rest = Vector3.Zero;
        for (int i = 0; i < 300; i++) rest = TraversalMovementProfileV2.Step(rest,
            Input(new Vector2(0, 1)), true).Velocity;
        Near(rest.Xz.Length, TraversalMovementProfileV2.CrouchSpeed, "crouch walking reaches only its configured speed");
        foreach (bool grounded in new[] { false, true })
        foreach (Vector2 axes in new[] { Vector2.Zero, new Vector2(-1, 1), new Vector2(.25f, -.5f) })
        foreach (float y in new[] { -.125f, .375f, 0f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) })
        {
            Vector3 speed = incoming.WithY(y); var input = Input(axes, grounded);
            Check(TraversalMovementProfileV2.Step(speed, input).Velocity
                == TraversalMovementProfileV1.Step(speed, input).Velocity, "standing v2 retains exact v1 horizontal behavior");
            foreach (bool sliding in new[] { false, true })
            {
                Vector3 result = TraversalMovementProfileV2.Step(speed, input, true, sliding).Velocity;
                Check(TraversalMovementProfileV3.Step(speed, input, true, sliding)
                    == TraversalMovementProfileV2.Step(speed, input, true, sliding),
                    "V3 crouch and slide solver results retain exact V2 coefficients and diagnostics");
                Check(BitConverter.SingleToInt32Bits(result.Y) == BitConverter.SingleToInt32Bits(y),
                    "crouch and slide never change native jump/gravity bits");
                if (!grounded) Check(result == TraversalMovementProfileV1.Step(speed, input).Velocity,
                    "crouch and slide introduce no airborne acceleration or jump boost");
            }
            Check(TraversalMovementProfileV3.Step(speed, input) == TraversalMovementProfileV2.Step(speed, input),
                "V3 standing movement remains exact V2 behavior");
            Check(TraversalMovementProfileV4.Step(speed, input) == TraversalMovementProfileV3.Step(speed, input),
                "V4 standing movement remains exact V3 behavior");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool refused = false;
            try { TraversalMovementProfileV2.Step(new Vector3(invalid, 0, 0), neutral, true, true); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check(refused, "new slide solver retains finite incoming-velocity validation");
        }
    }

    private static void Allocation()
    {
        Vector3 speed = new(17, -.125f, 3); var input = Input(Vector2.Zero);
        for (int i = 0; i < 10000; i++) speed = TraversalMovementProfileV2.Step(speed, input, true, true).Velocity;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) speed = TraversalMovementProfileV2.Step(speed, input, true, true).Velocity;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && float.IsFinite(speed.LengthSquared), "warmed stance solver valid path allocates zero bytes");
    }
}
