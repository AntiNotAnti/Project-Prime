using System;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-free mathematical contracts. Scene acceptance belongs to GroundSlopeSceneTests.
internal static class GroundSlopeTests
{
    private static int _checks;
    private static Vector3 _allocationSink;
    private static void Check(bool good, string message)
    { _checks++; if (!good) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message, float tolerance = .00002f)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
            $"{message}: {actual} != {expected}");
    private static void Same(Vector3 actual, Vector3 expected, string message)
        => Near((actual - expected).Length, 0, message);
    private static void Reject(Action work, string message)
    {
        try { work(); }
        catch (ArgumentException) { Check(true, message); return; }
        Check(false, message);
    }

    internal static int Run()
    {
        _checks = 0;
        try
        {
            Vector3 normal = new Vector3(0, 4, -1).Normalized();
            Vector3 diagonal = new Vector3(-1, 2, -1).Normalized();
            Check(GroundSlopeMovement.Walkable(normal) && GroundSlopeMovement.Walkable(Vector3.UnitY),
                "unit walkable slopes and flat floors accepted");
            foreach (Vector3 excluded in new[] { Vector3.Zero, Vector3.UnitX, -Vector3.UnitY,
                new Vector3(0, .5f, MathF.Sqrt(.75f)), new Vector3(0, .49f, MathF.Sqrt(1 - .49f * .49f)),
                Vector3.UnitY * 2, new Vector3(float.NaN, 1, 0), new Vector3(0, float.PositiveInfinity, 0) })
                Check(!GroundSlopeMovement.Walkable(excluded), "steep, nonunit and nonfinite support rejected");

            foreach (Vector3 support in new[] { Vector3.UnitY, normal, diagonal })
            foreach (Vector3 heading in new[] { Vector3.Zero, new Vector3(0, 0, 7.2f),
                new Vector3(0, 0, -7.2f), new Vector3(3, 0, 4), new Vector3(-12, 0, 7) })
            {
                Vector3 surface = GroundSlopeMovement.Follow(heading, support);
                Near(Vector3.Dot(surface, support), 0, "follow lies in support plane");
                Near(surface.Length, heading.Length, "follow preserves surface speed");
                Same(GroundSlopeMovement.Flatten(surface), heading, "tangent flatten/follow round trip");
                Same(GroundSlopeMovement.Follow(GroundSlopeMovement.Flatten(surface), support), surface,
                    "repeated support is idempotent");
                if (support == Vector3.UnitY) Same(surface, heading, "flat travel unchanged");
            }
            Reject(() => GroundSlopeMovement.Follow(Vector3.UnitZ, Vector3.UnitX), "wall follow refused");
            Reject(() => GroundSlopeMovement.Follow(new(float.NaN, 0, 0), normal), "nonfinite follow refused");

            Vector3 incoming = new(2, 0, 7);
            Vector3 clipped = GroundSlopeMovement.Clip(incoming, normal);
            Near(clipped.Length, incoming.Length, "qualified ramp clip preserves incoming magnitude");
            Near(Vector3.Dot(clipped, normal), 0, "clip removes inward normal component");
            Same(GroundSlopeMovement.Clip(clipped, normal), clipped, "coplanar clip cannot compound speed");
            Same(GroundSlopeMovement.Clip(Vector3.UnitY, normal), Vector3.UnitY, "separating impulse preserved");
            Same(GroundSlopeMovement.Clip(-normal * 3, normal), Vector3.Zero, "normal-only collision cannot invent tangent direction");
            // A preceding wall already removed X. The ramp may retain only the remaining speed.
            Vector3 afterWall = incoming.WithX(0);
            Near(GroundSlopeMovement.Clip(afterWall, normal).Length, afterWall.Length,
                "ramp does not restore speed removed by a preceding wall");

            const float dt = 1f / 60;
            Vector3 uphill = GroundSlopeMovement.Follow(Vector3.UnitZ * 7.2f, normal);
            Vector3 downhill = -uphill;
            Vector3 upNext = GroundSlopeMovement.Slide(uphill, normal, dt);
            Vector3 downNext = GroundSlopeMovement.Slide(downhill, normal, dt);
            Check(upNext.Length < uphill.Length && downNext.Length > downhill.Length,
                "slope force gradually slows uphill and increases downhill surface speed");
            Near(Vector3.Dot(upNext, normal), 0, "slide force remains tangent");
            Check((upNext - uphill).Length <= GroundSlopeMovement.SlideGravity * dt + .00001f,
                "one fixed tick bounds slope-force change");
            Same(GroundSlopeMovement.Slide(incoming, Vector3.UnitY, dt), incoming, "flat slide adds no force");
            Same(GroundSlopeMovement.Slide(uphill, normal, 0), uphill, "zero duration adds no force");
            Reject(() => GroundSlopeMovement.Slide(uphill, Vector3.UnitX, dt), "steep slide refused");
            foreach (float seconds in new[] { -dt, dt * 2, float.NaN, float.PositiveInfinity })
                Reject(() => GroundSlopeMovement.Slide(uphill, normal, seconds), "invalid or multiple-tick force refused");

            // Compare a sustained walk against the same horizontal solver on flat ground.
            // Equality here detects repeated uphill acceleration or geometric speed loss.
            var input = new MomentumInput(Vector2.UnitY, Vector3.UnitZ, Vector3.UnitX,
                Quake3MovementProfileV4.GroundSpeed, true, dt);
            Vector3 flat = Vector3.Zero, slope = Vector3.Zero;
            for (int tick = 0; tick < 240; tick++)
            {
                flat = Quake3MovementProfileV4.Step(flat, input).Velocity;
                Vector3 solved = Quake3MovementProfileV4.Step(GroundSlopeMovement.Flatten(slope), input).Velocity;
                slope = GroundSlopeMovement.Follow(solved, normal);
                Near(slope.Length, flat.Length, "sustained uphill surface speed equals ordinary ground solve");
            }
            Vector3 value = uphill;
            for (int tick = 0; tick < 2048; tick++)
                value = GroundSlopeMovement.Follow(GroundSlopeMovement.Flatten(value), normal);
            Near(value.Length, uphill.Length, "long repeated slope redirection has no speed gain", .0002f);

            Vector3 allocationValue = uphill;
            for (int tick = 0; tick < 2048; tick++) allocationValue = AllocationStep(allocationValue, normal);
            _allocationSink = allocationValue;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 2048; tick++) allocationValue = AllocationStep(allocationValue, normal);
            _allocationSink = allocationValue;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(float.IsFinite(_allocationSink.LengthSquared), "allocation loop retains its computed result");
            Check(bytes == 0, "warmed valid slope math allocates zero managed bytes");
            Console.WriteLine($"GROUND SLOPE PASS {_checks} assertions; asset-free math only");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("GROUND SLOPE FAIL " + error); return 1; }
    }

    private static Vector3 AllocationStep(Vector3 velocity, Vector3 normal)
        => GroundSlopeMovement.Slide(GroundSlopeMovement.Clip(
            GroundSlopeMovement.Follow(GroundSlopeMovement.Flatten(velocity), normal), normal), normal, 1f / 60);
}
