using System;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

// Independent property/integration checks of the pure helper. No game runtime,
// authority, native collision, protocol or user-facing qualification is implied.
namespace MphRead.NetTest;

internal static class ParkourGrappleTests
{
    private static int _checks;
    private static void Check(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException(label); }
    private static Vector3 Tangent(Vector3 velocity, Vector3 outward)
        => velocity - Vector3.Dot(velocity, outward) * outward;

    internal static int Run()
    {
        _checks = 0;
        try
        {
            Check(GrappleMovementV1.TicksPerSecond == 60 && GrappleMovementV1.MaxTicks == 180
                && GrappleMovementV1.MinRange == 2 && GrappleMovementV1.MaxRange == 24
                && GrappleMovementV1.MaximumAssistPerTick == .2f,
                "fixed profile range, duration and 60Hz assistance bounds");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                for (int axis = 0; axis < 3; axis++)
                {
                    Vector3 bad = axis == 0 ? new(invalid, 0, 0) : axis == 1 ? new(0, invalid, 0) : new(0, 0, invalid);
                    Check(!GrappleMovementV1.TryConstrainVelocity(bad, Vector3.Zero, 10, Vector3.Zero, 0, out _)
                        && !GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), bad, 10, Vector3.Zero, 0, out _)
                        && !GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, 10, bad, 0, out _),
                        "every nonfinite position, anchor and velocity component refuses");
                }
            foreach (float badLength in new[] { 0, 1.99f, 24.01f, float.NaN, float.PositiveInfinity })
                Check(!GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, badLength,
                    Vector3.Zero, 0, out Vector3 failed) && failed == Vector3.Zero,
                    "invalid accepted rope length returns no applicable candidate");
            Check(!GrappleMovementV1.TryConstrainVelocity(Vector3.Zero, Vector3.Zero, 10, Vector3.Zero, 0, out _)
                && !GrappleMovementV1.TryConstrainVelocity(new(24.01f, 0, 0), Vector3.Zero, 24, Vector3.Zero, 0, out _)
                && !GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, 10, Vector3.Zero, -1, out _),
                "singularity, current over-range and negative force tick refuse");
            var random = new Random(8128);
            for (int i = 0; i < 4000; i++)
            {
                float Next(float minimum, float maximum) => minimum + (maximum - minimum) * (float)random.NextDouble();
                Vector3 direction = new(Next(-1, 1), Next(-1, 1), Next(-1, 1));
                if (direction.LengthSquared < .01f) { i--; continue; }
                direction.Normalize();
                float length = Next(3, 22), distance = i % 2 == 0 ? length : length - .5f;
                Vector3 anchor = new(Next(-40, 40), Next(-40, 40), Next(-40, 40));
                Vector3 position = anchor + direction * distance;
                // Use the actual represented radial direction in all geometric
                // invariants; translated float coordinates introduce rounding.
                Vector3 radial = (position - anchor).Normalized();
                Vector3 before = new(Next(-100, 100), Next(-100, 100), Next(-100, 100));
                Check(GrappleMovementV1.TryConstrainVelocity(position, anchor, length, before,
                    i % GrappleMovementV1.MaxTicks, out Vector3 after), "ordinary finite rope geometry remains admissible");
                float incomingRadial = Vector3.Dot(before, radial), outgoingRadial = Vector3.Dot(after, radial);
                Check((Tangent(before, radial) - Tangent(after, radial)).Length < .0001f,
                    "constraint and assist preserve both tangential degrees of freedom");
                if (i % 2 == 0)
                {
                    Check(outgoingRadial <= .0001f, "a taut rope cannot retain or create outward radial motion");
                    if (incomingRadial <= 0)
                        Check(outgoingRadial <= incomingRadial + .0001f,
                            "taut inward motion cannot be shortened by the constraint");
                }
                else
                    Check((after - before).Length <= GrappleMovementV1.MaximumAssistPerTick + .0001f,
                        "slack rope adds only the bounded per-tick assist");
                // Separate dissipative removal from acceleration. The kinetic
                // energy increase above a no-outward-motion baseline must fit
                // the published acceleration work bound, independent of speed.
                float baseline = before.LengthSquared;
                if (i % 2 == 0 && incomingRadial > 0) baseline -= incomingRadial * incomingRadial;
                float inward = MathF.Max(0, -incomingRadial);
                float maximumEnergyIncrease = 2 * inward * GrappleMovementV1.MaximumAssistPerTick
                    + GrappleMovementV1.MaximumAssistPerTick * GrappleMovementV1.MaximumAssistPerTick;
                Check(after.LengthSquared <= baseline + maximumEnergyIncrease + .02f,
                    "added kinetic energy stays inside the radial acceleration work budget");
            }
            Vector3 velocity = new(0, 7, -3);
            for (int tick = 0; tick < 60; tick++)
                Check(GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, 10,
                    velocity, tick, out velocity), "one-second fixed-direction force calibration stays admissible");
            Check(MathF.Abs(velocity.X + 12) < .0001f && velocity.Y == 7 && velocity.Z == -3,
                "60 accepted applications add 12 units/second inward and preserve tangent");
            int accepted = 0;
            for (int tick = 0; tick < 200; tick++)
                if (GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, 10,
                    Vector3.Zero, tick, out _)) accepted++;
            Check(accepted == 180, "helper permits exactly the declared application window");
            for (int i = 0; i < 20000; i++)
                GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, 10, velocity, 0, out _);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20000; i++)
                GrappleMovementV1.TryConstrainVelocity(new(10, 0, 0), Vector3.Zero, 10, velocity, 0, out _);
            Check(GC.GetAllocatedBytesForCurrentThread() == start, "warmed helper allocates no managed bytes");
            Console.WriteLine($"PASS {_checks} independent grapple invariant checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error); return 1; }
    }
}

