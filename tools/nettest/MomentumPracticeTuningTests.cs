using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Independent equation/configuration fixtures, not a claim of exact Quake/Quake Live physics.
internal static class MomentumPracticeTuningTests
{
    private static readonly MomentumPracticeTuning Draft = MomentumPracticeTuning.Quake3Default;
    private const float Dt = 1f / 60;
    private static int checks;
    private static MomentumInput Input(Vector2 axes, bool grounded = false)
        => new(axes, Vector3.UnitZ, -Vector3.UnitX, Draft.GroundSpeed, grounded, Dt);
    private static double Speed(Vector3 value) => Math.Sqrt((double)value.X * value.X + (double)value.Z * value.Z);
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new InvalidOperationException(label); }
    private static void Near(double actual, double expected, string label, double tolerance = .00002)
        => Check(Math.Abs(actual - expected) <= tolerance, $"{label}: {actual} != {expected}");
    private static void Near(Vector3 actual, Vector3 expected, string label, float tolerance = .00002f)
        => Check((actual - expected).Length <= tolerance, $"{label}: {actual} != {expected}");
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
        { checks++; return; }
        throw new InvalidOperationException("Expected invalid tuning rejection: " + label);
    }

    internal static int Run()
    {
        try
        {
            checks = 0;
            Draft.Validate();
            Near(Draft.GroundSpeed, MomentumProfileV1.GroundSpeed, "native-scaled default ground speed", 0);
            Near(Draft.Tuning.GroundAcceleration, 10, "ground acceleration", 0);
            Near(Draft.Tuning.GroundFriction, 6, "ground friction", 0);
            Near(Draft.Tuning.StopSpeed, Draft.GroundSpeed * 100f / 320, "scaled stop speed");
            Near(Draft.Tuning.AirAcceleration, 1, "air acceleration", 0);
            Near(Draft.Tuning.AirWishSpeedCap, Draft.GroundSpeed, "full air wish speed", 0);
            Near(Draft.Step(Vector3.Zero, Input(Vector2.UnitY) with { DesiredSpeed = 1 }),
                Draft.Step(Vector3.Zero, Input(Vector2.UnitY)), "configuration owns desired speed", 0);
            AirEquations();
            GroundAnalogAndVertical();
            FixedCadence();
            AllocationContract();
            JsonContract();
            Console.WriteLine($"MOMENTUM PRACTICE TUNING PASS {checks} equation/configuration checks; private offline draft, no scene or timing claim.");
            return 0;
        }
        catch (Exception ex)
        { Console.Error.WriteLine("MOMENTUM PRACTICE TUNING FAIL: " + ex); return 1; }
    }

    private static void AirEquations()
    {
        float ground = Draft.GroundSpeed;
        double increment = ground * (double)Dt;
        Vector3 straight = Vector3.Zero, oldStraight = Vector3.Zero;
        for (int tick = 1; tick <= 90; tick++)
        {
            straight = Draft.Step(straight, Input(Vector2.UnitY));
            oldStraight = MomentumProfileV1.Step(oldStraight, Input(Vector2.UnitY)).Velocity;
            if (tick == 1 || tick == 30 || tick == 60 || tick == 90)
                Near(straight.Z, Math.Min(tick * increment, ground), "straight air ramp " + tick, .00003);
            if (tick == 12) Check(straight.Z > oldStraight.Z, "full wish passes old v1 small projection plateau");
        }
        Check(oldStraight.Z < ground / 2, "v1 straight air remains limited by its small wish target");
        Vector3 running = new(0, 3, ground);
        MomentumInput diagonal = Input(new(-1, 1));
        Vector3 first = Draft.Step(running, diagonal);
        Near(first, new((float)(increment / Math.Sqrt(2)), 3,
            ground + (float)(increment / Math.Sqrt(2))), "running W+A immediate gain");
        Check(Speed(first) > ground, "W+A grows speed at native running speed");
        Near(MomentumProfileV1.Step(running, diagonal).Velocity, running, "v1 W+A has no projection room at running speed", 0);
        Vector3 held = running;
        for (int tick = 0; tick < 120; tick++) held = Draft.Step(held, diagonal);
        Near(Speed(held), ground * Math.Sqrt(1.5), "fixed-facing diagonal eventually saturates", .00005);
        Near(Draft.Step(held, diagonal), held, "fixed-facing W+A requires further turning for further gain");

        Vector3 left = OptimalAir(1, 60), right = OptimalAir(-1, 60);
        double expectedSquared = ground * (double)ground + 60 * (2 * ground * increment - increment * increment);
        Near(Speed(left) * Speed(left), expectedSquared, "optimal full-wish analytic squared-speed gain", .002);
        Check(Speed(left) > 1.5 * ground, "ordinary optimal wish directions exceed old 1.5 ground-speed retention cap");
        Near(left.X, -right.X, "mirrored air strafe X", .0001);
        Near(left.Z, right.Z, "mirrored air strafe Z", .0001);
        Vector3 overspeed = new(0, -3, ground * 2);
        Near(Draft.Step(overspeed, Input(Vector2.UnitY)), overspeed, "straight overspeed retained", 0);
        Near(Draft.Step(new(4, -3, 17), Input(Vector2.Zero)), new(4, -3, 17), "air no-input retention", 0);
    }

    private static Vector3 OptimalAir(int mirror, int ticks)
    {
        Vector3 value = new(0, -2, Draft.GroundSpeed), old = value;
        double increment = Draft.GroundSpeed * (double)Dt;
        for (int tick = 0; tick < ticks; tick++)
        {
            // Projection G-a leaves precisely one full increment. This authors wish intent;
            // it is a pure math fixture, not the separate raw-mouse scene controller.
            double angle = Math.Acos(Math.Clamp((Draft.GroundSpeed - increment) / Speed(value), -1, 1));
            Vector3 direction = new(value.X / (float)Speed(value), 0, value.Z / (float)Speed(value));
            Vector3 wish = Rotate(direction, mirror * angle);
            Vector3 forward = Rotate(wish, -mirror * Math.PI / 4);
            var input = Input(new(-mirror, 1)) with
            { FlatForward = forward, FlatRight = new(-forward.Z, 0, forward.X) };
            value = Draft.Step(value, input);
            old = MomentumProfileV1.Step(old, input).Velocity;
            Check(BitConverter.SingleToInt32Bits(value.Y) == BitConverter.SingleToInt32Bits(-2f), "optimal air preserves native Y " + tick);
        }
        Check(Speed(value) > Speed(old), "new full-wish steering grows beyond v1 under identical offered wishes");
        return value;
    }

    private static Vector3 Rotate(Vector3 value, double angle)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        return new((float)(value.X * cos + value.Z * sin), value.Y,
            (float)(value.Z * cos - value.X * sin));
    }

    private static void GroundAnalogAndVertical()
    {
        float ground = Draft.GroundSpeed;
        Near(Draft.Step(new(0, 5, ground), Input(Vector2.Zero, true)),
            new(0, 5, ground * (1 - 6 * Dt)), "ground friction per-second calibration");
        Near(Draft.Step(new(0, -5, .01f), Input(Vector2.Zero, true)), new(0, -5, 0), "stop floor stops without reversal", 0);
        Near(Draft.Step(Vector3.Zero, Input(Vector2.UnitY, true)), new(0, 0, ground * 10 * Dt), "ground rate calibration");
        foreach (float analog in new[] { .25f, .5f, .75f, 1f })
        {
            Near(Speed(Draft.Step(Vector3.Zero, Input(new(0, analog)))), ground * analog * Dt, "analog magnitude " + analog);
            float component = analog / MathF.Sqrt(2);
            Near(Speed(Draft.Step(Vector3.Zero, Input(new(-component, component)))), ground * analog * Dt, "analog diagonal magnitude " + analog);
        }
        Near(Speed(Draft.Step(Vector3.Zero, Input(new(-1, 1)))), ground * Dt, "digital diagonal no square-root-two bonus");
        Near(Draft.Step(Vector3.Zero, Input(new(-.3f, .4f))), new(ground * Dt * .3f, 0, ground * Dt * .4f), "asymmetric analog retains magnitude");
        var input = Input(new(-.3f, .4f));
        Vector3 initial = new(2, -3, 4);
        Near(Draft.Step(Rotate(initial, .73), input with
            { FlatForward = Rotate(input.FlatForward, .73), FlatRight = Rotate(input.FlatRight, .73) }),
            Rotate(Draft.Step(initial, input), .73), "rotation covariance");
        foreach (float vertical in new[] { -0f, -123.25f, 19.5f, float.MaxValue })
        {
            Vector3 result = Draft.Step(new(2, vertical, 4), input);
            Check(BitConverter.SingleToInt32Bits(result.Y) == BitConverter.SingleToInt32Bits(vertical), "Y bits untouched " + vertical);
        }
        Near(Draft.Step(initial, input with { DeltaSeconds = 0 }), initial, "zero step identity", 0);
        var doubled = Draft with { GroundSpeed = ground * 2 };
        doubled.Validate();
        Near(doubled.Step(initial * 2, input), Draft.Step(initial, input) * 2, "dimensionless rates scale with ground speed");
    }

    private static void FixedCadence()
    {
        Vector3 AtCadence(int framesPerSecond)
        {
            FrameTiming.Reset();
            Vector3 value = new(0, -3, Draft.GroundSpeed);
            int ticks = 0;
            for (int frame = 0; frame < framesPerSecond * 3; frame++)
                for (int step = 0, count = FrameTiming.Advance(1.0 / framesPerSecond + (frame == 0 ? 1e-9 : 0)); step < count; step++)
                {
                    var input = Input(ticks < 60 ? new(-1, 1) : ticks < 120 ? Vector2.Zero : Vector2.UnitY, ticks >= 120);
                    value = Draft.Step(value, input with { DeltaSeconds = (float)FrameTiming.StepSeconds });
                    ticks++;
                }
            Check(ticks == 180, "fixed60 tick count at host " + framesPerSecond);
            return value;
        }
        try
        {
            Vector3 reference = AtCadence(60);
            foreach (int cadence in new[] { 30, 120, 240 }) Near(AtCadence(cadence), reference, "host cadence identity " + cadence, 0);
        }
        finally { FrameTiming.Reset(); }
    }

    private static void AllocationContract()
    {
        var input = Input(new(-1, 1));
        Vector3 value = new(0, -3, Draft.GroundSpeed);
        for (int tick = 0; tick < 10000; tick++) value = Draft.Step(value, input);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 100000; tick++) value = Draft.Step(value, input);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "valid configured Step allocates zero managed bytes");
        Check(float.IsFinite(value.X) && float.IsFinite(value.Z), "allocation loop retains finite result");
    }

    private static void JsonContract()
    {
        string directory = Path.Combine(Path.GetTempPath(), "momentum-practice-tuning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "tuning.json");
        MomentumPracticeTuning Load(string json) { File.WriteAllText(path, json); return MomentumPracticeTuning.Load(path); }
        try
        {
            Check(Load("{}") == Draft, "empty object uses documented defaults");
            Check(Load("{\"groundSpeed\":7.199707,\"groundAcceleration\":10,\"groundFriction\":6,\"stopSpeedFraction\":0.3125,\"airAcceleration\":1,\"airWishSpeedFraction\":1}") == Draft,
                "six camelCase starter values round-trip exactly");
            var custom = Load("{\"groundSpeed\":8,\"airAcceleration\":2,\"airWishSpeedFraction\":0.5}");
            Near(custom.GroundSpeed, 8, "ground speed override", 0);
            Near(custom.Tuning.AirAcceleration, 2, "air rate override", 0);
            Near(custom.Tuning.AirWishSpeedCap, 4, "wish fraction override", 0);
            Near(custom.Tuning.GroundFriction, 6, "missing optional field retains default", 0);
            Near(Draft.GroundSpeed, MomentumProfileV1.GroundSpeed, "loaded records do not mutate shared default", 0);
            foreach (string invalid in new[]
            {
                "[]", "null", "1", "{", "{\"unknown\":1}", "{\"GroundSpeed\":8}",
                "{\"groundSpeed\":8,\"groundSpeed\":9}", "{\"groundSpeed\":0}", "{\"groundSpeed\":-1}",
                "{\"groundSpeed\":1e100}", "{\"groundSpeed\":\"NaN\"}", "{\"groundSpeed\":null}",
                "{\"airAcceleration\":true}", "{\"groundFriction\":-1}", "{\"stopSpeedFraction\":-1}",
                "{\"airWishSpeedFraction\":-1}", "{\"groundAcceleration\":-1}", "{\"airAcceleration\":-1}",
                "{\"groundSpeed\":3e38,\"groundAcceleration\":2}",
                "{\"stopSpeedFraction\":3e38}", "{\"airWishSpeedFraction\":3e38}",
                "{\"groundSpeed\":1,\"stopSpeedFraction\":1e38,\"groundFriction\":10}"
            }) Reject(() => Load(invalid), invalid);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            {
                Reject(() => (Draft with { GroundSpeed = invalid }).Validate(), "ground speed finite/nonnegative");
                Reject(() => (Draft with { GroundAcceleration = invalid }).Validate(), "ground acceleration finite/nonnegative");
                Reject(() => (Draft with { GroundFriction = invalid }).Validate(), "friction finite/nonnegative");
                Reject(() => (Draft with { StopSpeedFraction = invalid }).Validate(), "stop fraction finite/nonnegative");
                Reject(() => (Draft with { AirAcceleration = invalid }).Validate(), "air acceleration finite/nonnegative");
                Reject(() => (Draft with { AirWishSpeedFraction = invalid }).Validate(), "air wish fraction finite/nonnegative");
            }
            Reject(() => (Draft with { GroundSpeed = 0 }).Validate(), "zero configured ground speed");
            var zeroRates = Draft with { GroundAcceleration = 0, GroundFriction = 0, StopSpeedFraction = 0, AirAcceleration = 0, AirWishSpeedFraction = 0 };
            zeroRates.Validate();
            Near(zeroRates.Step(new(1, -2, 3), Input(Vector2.One, true)), new(1, -2, 3), "zero coefficients valid and disable terms", 0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
