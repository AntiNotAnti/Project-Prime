// Dimensionless equation fixtures. These numbers are not proposed gameplay tuning.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class MomentumMathTests
{
    private static readonly MomentumTuning Tuning = new(2, 1, 2, 2, 1);
    private static MomentumInput Input(Vector2 axes, bool grounded = true, float speed = 4, float dt = .25f)
        => new(axes, Vector3.UnitZ, Vector3.UnitX, speed, grounded, dt);
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(Vector3 actual, Vector3 expected, string label, float tolerance = .00002f)
        => Check((actual - expected).Length <= tolerance, $"{label}: {actual} != {expected}");
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (ArgumentException) { checks++; return; }
        throw new InvalidOperationException("Expected ArgumentException: " + label);
    }
    private static Vector3 Step(Vector3 velocity, MomentumInput input, MomentumTuning? tuning = null)
        => MomentumMath.Step(velocity, input, tuning ?? Tuning);

    internal static int Run()
    {
        try
        {
            checks = 0;
            Near(Step(Vector3.Zero, Input(Vector2.UnitY)), new(0, 0, 2), "ground acceleration");
            Near(Step(new(10, 3, 0), Input(Vector2.Zero)), new(7.5f, 3, 0), "friction before acceleration");
            Near(Step(new(1, -2, 0), Input(Vector2.Zero)), new(.5f, -2, 0), "stop-speed friction floor");
            Near(Step(new(.1f, 1, 0), Input(Vector2.Zero)), new(0, 1, 0), "friction cannot reverse");
            Near(Step(new(10, 0, 0), Input(-Vector2.UnitX)), new(5.5f, 0, 0), "counterstrafe after friction");
            Vector3 reversing = new(10, 0, 0);
            for (int i = 1; i <= 8; i++)
            {
                reversing = Step(reversing, Input(-Vector2.UnitX), Tuning with { GroundFriction = 0 });
                Near(reversing, new(MathF.Max(-4, 10 - 2 * i), 0, 0), "gradual reversal and opposing target " + i);
            }
            Near(Step(new(0, 0, 3), Input(Vector2.UnitY)), new(0, 0, 4), "acceleration limited by projection room");
            Near(Step(new(0, 0, 8), Input(Vector2.UnitY), Tuning with { GroundFriction = 0 }), new(0, 0, 8), "external above target preserved");
            Near(Step(new(5, 7, 0), Input(Vector2.UnitY, false)), new(5, 7, 1), "air projected cap and perpendicular growth");
            Near(Step(new(5, 7, 1), Input(Vector2.UnitY, false)), new(5, 7, 1), "air cap does not clamp total speed");
            Near(Step(new(0, 0, -2), Input(Vector2.UnitY, false)), Vector3.Zero, "air acceleration uses uncapped wish speed");
            Near(Step(new(3, -4, 5), Input(Vector2.Zero, false)), new(3, -4, 5), "no air friction");
            Near(Step(Vector3.Zero, Input(new(0, .1f), false, dt: 1)), new(0, 0, .4f), "air projection target respects partial wish below cap");
            Near(Step(new(3, -4, 5), Input(Vector2.One, dt: 0)), new(3, -4, 5), "zero delta identity");
            Near(Step(new(3, -4, 5), Input(Vector2.One), default(MomentumTuning)), new(3, -4, 5), "zero tuning valid");
            foreach (float magnitude in new[] { .25f, .5f, .75f, 1f })
            {
                Near(Step(Vector3.Zero, Input(new(0, magnitude), speed: 8)), new(0, 0, 4 * magnitude), "analog axial " + magnitude);
                float component = magnitude / MathF.Sqrt(2);
                Near(Step(Vector3.Zero, Input(new(component, component), speed: 8)), new(4 * component, 0, 4 * component), "analog diagonal " + magnitude);
            }
            Near(Step(Vector3.Zero, Input(new(.3f, .4f), speed: 8)), new(1.2f, 0, 1.6f), "asymmetric partial axes");
            Near(Step(Vector3.Zero, Input(new(3, 4), speed: 8)), new(2.4f, 0, 3.2f), "radial clamp");
            var scaledBasis = Input(Vector2.One) with { FlatForward = new(0, 13, 8), FlatRight = new(3, -9, 0) };
            Near(Step(Vector3.Zero, scaledBasis), Step(Vector3.Zero, Input(Vector2.One)), "independent planar basis normalization");
            Near(Step(Vector3.Zero, Input(new(.3f, .4f), speed: 8) with { FlatForward = Vector3.UnitX, FlatRight = -Vector3.UnitZ }),
                new(1.6f, 0, -1.2f), "rotated planar basis preserves signed strafe and forward");
            float vertical = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
            var preserved = Step(new(0, vertical, 0), Input(Vector2.UnitY));
            Check(BitConverter.SingleToInt32Bits(preserved.Y) == BitConverter.SingleToInt32Bits(vertical), "vertical negative-zero bits preserved");
            preserved = Step(new(0, float.MaxValue, 0), Input(Vector2.UnitY));
            Check(preserved.Y == float.MaxValue, "vertical max preserved");
            Near(Step(Vector3.Zero, Input(Vector2.UnitY, speed: float.MaxValue), Tuning with { GroundAcceleration = 0 }), Vector3.Zero, "finite extreme target with zero rate");
            Near(Step(Vector3.Zero, Input(Vector2.Zero, dt: float.MaxValue)), Vector3.Zero, "finite extreme delta zero work");
            Near(Step(Vector3.Zero, Input(Vector2.UnitY, speed: float.MaxValue, dt: 1), Tuning with { GroundAcceleration = float.MaxValue }), new(0, 0, float.MaxValue), "huge acceleration bounded by projection", 0);
            Near(Step(Vector3.Zero, Input(new(float.MaxValue, float.MaxValue))), Step(Vector3.Zero, Input(Vector2.One)), "extreme finite radial clamp");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Reject(() => Step(new(invalid, 0, 0), Input(Vector2.Zero)), "velocity X");
                Reject(() => Step(new(0, invalid, 0), Input(Vector2.Zero)), "velocity Y");
                Reject(() => Step(new(0, 0, invalid), Input(Vector2.Zero)), "velocity Z");
                Reject(() => Step(Vector3.Zero, Input(new(invalid, 0))), "axes X");
                Reject(() => Step(Vector3.Zero, Input(new(0, invalid))), "axes Y");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero) with { FlatForward = new(0, invalid, 1) }), "basis nonfinite");
            }
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero, speed: invalid)), "desired speed");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero, dt: invalid)), "delta");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero), Tuning with { GroundAcceleration = invalid }), "ground acceleration");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero), Tuning with { GroundFriction = invalid }), "friction");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero), Tuning with { StopSpeed = invalid }), "stop speed");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero), Tuning with { AirAcceleration = invalid }), "air acceleration");
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero), Tuning with { AirWishSpeedCap = invalid }), "air cap");
            }
            foreach (Vector3 basis in new[] { Vector3.Zero, Vector3.UnitY, Vector3.UnitX, new Vector3(1, 0, 1) })
                Reject(() => Step(Vector3.Zero, Input(Vector2.Zero) with { FlatForward = basis }), "degenerate/nonorthogonal basis");
            Reject(() => Step(Vector3.Zero, Input(Vector2.Zero) with { FlatRight = Vector3.Zero }), "zero right basis");
            Reject(() => Step(new(float.MaxValue, 0, 0), Input(Vector2.One, speed: float.MaxValue, dt: 1),
                Tuning with { GroundFriction = 0, GroundAcceleration = float.MaxValue }), "unrepresentable result");
            UnitsAndCadence();
            Console.WriteLine($"MOMENTUM MATH PASS {checks} equation/validation checks; dimensionless fixtures, no gameplay tuning claim.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM MATH FAIL: " + ex); return 1; }
    }

    private static void UnitsAndCadence()
    {
        var input = Input(new(.3f, .4f), speed: 7, dt: 1 / 60f);
        Vector3 velocity = new(2, -3, 4);
        var scaled = Tuning with { StopSpeed = Tuning.StopSpeed * 10, AirWishSpeedCap = Tuning.AirWishSpeedCap * 10 };
        Near(Step(velocity * 10, input with { DesiredSpeed = input.DesiredSpeed * 10 }, scaled), Step(velocity, input) * 10, "spatial units rescale");
        Vector3 Reference(int framesPerSecond)
        {
            FrameTiming.Reset();
            Vector3 value = new(2, -3, 4);
            int ticks = 0;
            // One nanosecond closes the terminal boundary despite repeated-double rounding
            // (notably 144Hz); all solver steps remain exactly 1/60 second.
            for (int frame = 0; frame < framesPerSecond * 3; frame++)
                for (int step = 0, count = FrameTiming.Advance(1.0 / framesPerSecond + (frame == 0 ? 1e-9 : 0)); step < count; step++)
                {
                    Vector2 axes = ticks < 60 ? Vector2.UnitY : ticks < 120 ? Vector2.UnitX : -Vector2.UnitY;
                    value = Step(value, Input(axes, ticks < 90, dt: (float)FrameTiming.StepSeconds)); ticks++;
                }
            Check(ticks == 180, "fixed 60Hz tick count under " + framesPerSecond + "Hz host");
            return value;
        }
        try
        {
            Vector3 reference = Reference(60);
            foreach (int cadence in new[] { 30, 90, 120, 144, 240 }) Near(Reference(cadence), reference, "render cadence " + cadence, 0);
        }
        finally { FrameTiming.Reset(); }
    }

    internal static int RunBenchmark(string? jsonPath)
    {
        const int players = 8, ticksPerBatch = 512, batches = 200;
        var states = new Vector3[players];
        var inputs = new MomentumInput[players * 4];
        for (int i = 0; i < players; i++)
        {
            states[i] = new(i, -i, i / 2f);
            inputs[i * 4] = Input(new(.3f, .4f), i % 2 == 0, dt: 1 / 60f);
            inputs[i * 4 + 1] = Input(new(-.3f, -.4f), i % 2 == 0, dt: 1 / 60f);
            inputs[i * 4 + 2] = Input(Vector2.UnitX, i % 2 != 0, dt: 1 / 60f);
            inputs[i * 4 + 3] = Input(-Vector2.UnitY, i % 2 != 0, dt: 1 / 60f);
        }
        void Batch()
        {
            for (int tick = 0; tick < ticksPerBatch; tick++)
                for (int player = 0; player < players; player++) states[player] = MomentumMath.Step(states[player], inputs[player * 4 + ((tick / 16) & 3)], Tuning);
        }
        for (int i = 0; i < 30; i++) Batch();
        var samples = new double[batches];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < batches; i++)
        {
            long started = Stopwatch.GetTimestamp(); Batch();
            samples[i] = (Stopwatch.GetTimestamp() - started) * (1e9 / Stopwatch.Frequency) / (players * ticksPerBatch);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Array.Sort(samples);
        double sum = 0; foreach (double sample in samples) sum += sample;
        var result = new { schema = 1, workload = "eight scalar player values; precomputed wish reversals/ground-air phases; warmed batches; no scene/network/gameplay integration",
            runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            players, ticksPerBatch, batches, playerSteps = players * ticksPerBatch * batches, allocatedBytes = allocated,
            averageNanosecondsPerPlayerStep = sum / batches, p95NanosecondsPerPlayerStep = samples[(int)Math.Ceiling(batches * .95) - 1],
            p99NanosecondsPerPlayerStep = samples[(int)Math.Ceiling(batches * .99) - 1], checksum = states[0].X + states[7].Z };
        string json = JsonSerializer.Serialize(result);
        Console.WriteLine("MOMENTUM BENCHMARK " + json);
        if (jsonPath != null) { string path = Path.GetFullPath(jsonPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json + Environment.NewLine); }
        if (allocated != 0) { Console.Error.WriteLine("MOMENTUM BENCHMARK FAIL: solver workload allocated"); return 1; }
        return 0;
    }
}
