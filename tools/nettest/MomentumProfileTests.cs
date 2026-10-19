using System;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class MomentumProfileTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .00001f, $"{message}: {actual} != {expected}");
    private static MomentumInput Input(Vector2 axes, bool grounded = false) =>
        new(axes, Vector3.UnitZ, Vector3.UnitX, MomentumProfileV1.GroundSpeed, grounded, 1f / 60);
    private static float Speed(Vector3 velocity) => MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
    private static MomentumInput Perpendicular(Vector3 velocity)
    {
        Vector3 direction = new(-velocity.Z, 0, velocity.X);
        direction = direction.LengthSquared > 0 ? direction.Normalized() : Vector3.UnitZ;
        return Input(Vector2.UnitY) with { FlatForward = direction, FlatRight = new(direction.Z, 0, -direction.X) };
    }

    internal static int Run()
    {
        try
        {
            _checks = 0;
            Check(MomentumProfileV1.MomentumProfileVersion == 1, "profile identity");
            Near(MomentumProfileV1.GroundSpeed, 7.19970703125f, "stock scale");
            Near(MomentumProfileV1.Tuning.GroundFriction, 9.60205078125f, "native neutral decay rate");
            Check(MomentumProfileV1.Tuning.StopSpeed == 0, "no unmeasured braking floor");
            Check(MomentumProfileV1.Contact(true, true, 0, false) == MomentumContactPhase.Grounded, "resting contact");
            Check(MomentumProfileV1.Contact(true, false, 0, false) == MomentumContactPhase.Landing, "native newly standing contact");
            Check(MomentumProfileV1.Contact(false, true, -1, false) == MomentumContactPhase.Airborne, "leaving contact");
            Check(MomentumProfileV1.Contact(true, true, .3f, false) == MomentumContactPhase.Airborne, "upward residual impulse is airborne");
            Check(MomentumProfileV1.Contact(true, true, .3f, true) == MomentumContactPhase.LegalJump, "accepted ground jump");
            Check(MomentumProfileV1.Contact(false, false, .3f, true) == MomentumContactPhase.LegalJump, "accepted native multi-jump");
            foreach (MomentumContactPhase phase in Enum.GetValues<MomentumContactPhase>())
                Check(MomentumProfileV1.UsesGroundFriction(phase) == (phase is MomentumContactPhase.Grounded or MomentumContactPhase.Landing), "explicit friction ownership");
            bool invalid = false;
            try { MomentumProfileV1.Contact(true, true, float.NaN, false); }
            catch (ArgumentOutOfRangeException) { invalid = true; }
            Check(invalid, "invalid contact rejected");

            var held = new Vector3(2, -0f, 0);
            var landing = MomentumProfileV1.Step(held, Input(Vector2.Zero, true));
            Near(landing.Velocity.X, held.X * .8399658203125f, "non-hop landing receives normal friction");
            var takeoff = MomentumProfileV1.Step(held, Input(Vector2.Zero));
            Near(takeoff.Velocity.X, 2, "legal takeoff skips ground friction");
            Check(BitConverter.SingleToInt32Bits(takeoff.Velocity.Y) == BitConverter.SingleToInt32Bits(held.Y), "profile preserves negative zero Y");

            foreach (float magnitude in new[] { .25f, .5f, .75f, 1f })
            {
                Vector3 forward = Vector3.Zero, diagonal = Vector3.Zero;
                for (int i = 0; i < 120; i++)
                {
                    forward = MomentumProfileV1.Step(forward, Input(new(0, magnitude), true)).Velocity;
                    float axis = magnitude / MathF.Sqrt(2);
                    diagonal = MomentumProfileV1.Step(diagonal, Input(new(axis, axis), true)).Velocity;
                }
                Near(Speed(forward), MomentumProfileV1.GroundSpeed * magnitude, "partial analog ground target");
                Near(Speed(diagonal), Speed(forward), "equal radial analog magnitude");
            }
            Vector3 reverse = new(0, 0, MomentumProfileV1.GroundSpeed);
            reverse = MomentumProfileV1.Step(reverse, Input(-Vector2.UnitY, true)).Velocity;
            Check(reverse.Z > 0 && reverse.Z < MomentumProfileV1.GroundSpeed, "counter-strafe decelerates before reversing");
            for (int i = 0; i < 30; i++) reverse = MomentumProfileV1.Step(reverse, Input(-Vector2.UnitY, true)).Velocity;
            Near(reverse.Z, -MomentumProfileV1.GroundSpeed, "eventual reversal");

            // Repeated perpendicular air wishes deliberately create cumulative
            // gain; this is a stress equation fixture, not a real input-device run.
            Vector3 circle = new(0, 0, MomentumProfileV1.GroundSpeed);
            int limited = 0;
            for (int i = 0; i < 10000; i++)
            {
                var result = MomentumProfileV1.Step(circle, Perpendicular(circle));
                circle = result.Velocity;
                if (result.SpeedLimited) limited++;
                Check(!result.IncomingOverCap && Speed(circle) <= MomentumProfileV1.RetentionSpeedCap + .00001f
                    && float.IsFinite(circle.X) && float.IsFinite(circle.Z), "regular air motion remains finite/bounded without ratcheting its budget");
            }
            Check(limited > 1000, "ceiling exercised under sustained air gain");
            Near(Speed(circle), MomentumProfileV1.RetentionSpeedCap, "regular speed-retention ceiling");
            Vector3 nativeCircle = new(0, 0, MomentumProfileV1.GroundSpeed / 30);
            for (int i = 0; i < 10000; i++)
            {
                Vector3 physical = nativeCircle * 30;
                var result = MomentumProfileV1.Step(physical, Perpendicular(physical));
                nativeCircle = result.Velocity / 30;
                Check(Speed(nativeCircle * 30) <= MomentumProfileV1.RetentionSpeedCap + .00001f,
                    "native unit round trips do not accumulate regular overspeed");
            }
            Vector3 impulse = new(0, 13, MomentumProfileV1.RetentionSpeedCap * 2);
            var neutralImpulse = MomentumProfileV1.Step(impulse, Input(Vector2.Zero));
            Check(neutralImpulse.Velocity == impulse && neutralImpulse.IncomingOverCap && !neutralImpulse.SpeedLimited, "residual external momentum is not flattened");
            var steerImpulse = MomentumProfileV1.Step(impulse, Perpendicular(impulse));
            Check(steerImpulse.IncomingOverCap && steerImpulse.SpeedLimited, "steering growth above incoming overspeed is limited");
            Near(Speed(steerImpulse.Velocity), Speed(impulse), "incoming overspeed budget preserved");
            Near(steerImpulse.Velocity.Y, impulse.Y, "impulse Y preserved");

            Vector3 expected = Cadence(60);
            foreach (int hz in new[] { 30, 60, 120, 144, 240 })
                Check(Cadence(hz) == expected, "fixed tick sequence independent of synthetic host cadence " + hz);

            var samples = new MomentumInput[256];
            for (int i = 0; i < samples.Length; i++) samples[i] = Input(i % 2 == 0 ? Vector2.UnitX : Vector2.UnitY, i % 3 == 0);
            Vector3 warm = Vector3.Zero;
            for (int i = 0; i < 100000; i++) warm = MomentumProfileV1.Step(warm, samples[i % samples.Length]).Velocity;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++) warm = MomentumProfileV1.Step(warm, samples[i % samples.Length]).Velocity;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(allocated == 0 && float.IsFinite(warm.X), "zero allocations over 100000 warmed profile steps");
            Console.WriteLine($"MOMENTUM PROFILE PASS {_checks} checks; v1 private candidate, zero allocations/100000 warmed steps; synthetic cadence, not device acceptance.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM PROFILE FAIL " + ex); return 1; }
    }

    private static Vector3 Cadence(int hz)
    {
        double accumulator = 0;
        int tick = 0;
        Vector3 velocity = Vector3.Zero;
        for (int frame = 0; frame < hz * 3; frame++)
        {
            accumulator += 1.0 / hz;
            while (accumulator + 1e-12 >= 1.0 / 60)
            {
                accumulator -= 1.0 / 60;
                var input = Input(tick % 30 < 15 ? Vector2.UnitX : Vector2.UnitY, tick % 65 == 0);
                velocity = MomentumProfileV1.Step(velocity, input).Velocity;
                tick++;
            }
        }
        Check(tick == 180, "exactly 180 simulation steps");
        return velocity;
    }
}
