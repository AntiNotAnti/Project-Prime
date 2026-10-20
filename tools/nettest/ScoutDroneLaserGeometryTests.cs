using System;
using System.Collections.Generic;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Source-free tests for the ScoutDroneLaserRay helper only.
// Invented coordinates: no Scene, assets, collision, damage, RNG, timers or GL.
// The literal targets below are independent source-pattern/frame landmarks;
// they do not call the production interpolation or reproduce its branch formula.
// Frames56/123 test geometric clamping, NOT laser visibility/payment admission.
internal static class ScoutDroneLaserGeometryTests
{
    private readonly record struct D(double X, double Y, double Z)
    {
        internal D Minus(D other) => new(X - other.X, Y - other.Y, Z - other.Z);
        internal D Unit()
        {
            double length = Math.Sqrt(X * X + Y * Y + Z * Z);
            return new(X / length, Y / length, Z / length);
        }
    }
    private readonly record struct Ray(Vector3 Origin, Vector3 Direction, Vector3 Up);
    private sealed record Landmark(int Frame, D[] Targets);
    // Target plane is Z=10, center=(0,0,0), aim=+Z. Each row lists
    // patterns0,1,2,3 in the donor order. Explicit decimal landmarks avoid
    // deriving expected results from production constants, sign branches or t.
    private static readonly Landmark[] Landmarks =
    {
        new(56, new[] { new D(2.4,-.65,10), new D(2.4,.65,10), new D(-2.4,-.65,10), new D(-2.4,.65,10) }),
        new(57, new[] { new D(2.4,-.65,10), new D(2.4,.65,10), new D(-2.4,-.65,10), new D(-2.4,.65,10) }),
        new(87, new[] { new D(0,0,10), new D(0,0,10), new D(0,0,10), new D(0,0,10) }),
        new(113, new[] { new D(-2.08,.563333333333333333,10), new D(-2.08,-.563333333333333333,10),
            new D(2.08,.563333333333333333,10), new D(2.08,-.563333333333333333,10) }),
        new(117, new[] { new D(-2.4,.65,10), new D(-2.4,-.65,10), new D(2.4,.65,10), new D(2.4,-.65,10) }),
        new(122, new[] { new D(-2.4,.65,10), new D(-2.4,-.65,10), new D(2.4,.65,10), new D(2.4,-.65,10) }),
        new(123, new[] { new D(-2.4,.65,10), new D(-2.4,-.65,10), new D(2.4,.65,10), new D(2.4,-.65,10) })
    };
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Scout Drone laser geometry: " + label); }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Near(Vector3 actual, D expected, double tolerance = .000003)
        => Math.Abs(actual.X - expected.X) <= tolerance && Math.Abs(actual.Y - expected.Y) <= tolerance
            && Math.Abs(actual.Z - expected.Z) <= tolerance;
    private static Ray Actual(Vector3 center, Vector3 aim, Vector3 facing, int pattern, float distance, int frame)
    {
        PlayerEntity.ScoutDroneLaserRay(center, aim, facing, pattern, distance, frame, out var origin, out var direction, out var up);
        return new(origin, direction, up);
    }
    private static Ray Expect(Vector3 center, Vector3 aim, Vector3 facing, int pattern, float distance, int frame,
        D origin, D target, D up, string label)
    {
        var actual = Actual(center, aim, facing, pattern, distance, frame);
        // Independent double scalar normalization from the literal ray endpoints.
        D expectedDirection = target.Minus(origin).Unit();
        Check(Finite(actual.Origin) && Finite(actual.Direction) && Finite(actual.Up), label + ": finite outputs");
        Check(Near(actual.Origin, origin), label + ": literal emitter position");
        Check(Near(actual.Direction, expectedDirection), label + ": independently normalized endpoint direction");
        Check(Near(actual.Up, up), label + ": literal handed basis up");
        Check(Math.Abs(actual.Direction.LengthSquared - 1) < .000004 && Math.Abs(actual.Up.LengthSquared - 1) < .000004,
            label + ": direction/up remain unit length");
        Check(Vector3.Dot(actual.Direction, new Vector3((float)expectedDirection.X, (float)expectedDirection.Y, (float)expectedDirection.Z)) > .99999f,
            label + ": forward direction never reverses the intended ray");
        return actual;
    }
    private static void CrossZ(Ray actual, D target, string label)
    {
        Check(actual.Direction.Z > 0, label + ": forward target-plane crossing exists");
        double travel = (target.Z - actual.Origin.Z) / actual.Direction.Z;
        var crossing = new D(actual.Origin.X + actual.Direction.X * travel,
            actual.Origin.Y + actual.Direction.Y * travel, actual.Origin.Z + actual.Direction.Z * travel);
        Check(Math.Abs(crossing.X - target.X) < .00002 && Math.Abs(crossing.Y - target.Y) < .00002
            && Math.Abs(crossing.Z - target.Z) < .00002, label + ": ray actually intersects its independent target-plane point");
    }

    internal static int Run()
    {
        _checks = 0;
        try
        {
            var rays = new Dictionary<(int Pattern, int Frame), Ray>();
            foreach (var landmark in Landmarks)
            for (int pattern = 0; pattern < 4; pattern++)
            {
                string label = "pattern" + pattern + "/frame" + landmark.Frame;
                var ray = Expect(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, pattern, 10, landmark.Frame,
                    new(0,-.17,.16), landmark.Targets[pattern], new(0,1,0), label);
                rays.Add((pattern, landmark.Frame), ray); CrossZ(ray, landmark.Targets[pattern], label);
            }
            for (int pattern = 0; pattern < 4; pattern++)
            {
                Check(rays[(pattern,56)] == rays[(pattern,57)], "pre-start geometry clamps to first endpoint, pattern" + pattern);
                Check(rays[(pattern,117)] == rays[(pattern,122)] && rays[(pattern,122)] == rays[(pattern,123)],
                    "endpoint geometry holds through visual-only tail and beyond; scheduler owns visibility, pattern" + pattern);
                Check(rays[(pattern,87)].Direction.Y > 0, "center-plane crossing accounts for emitter below aim, pattern" + pattern);
            }
            Check(rays[(0,57)].Direction.X > 0 && rays[(2,57)].Direction.X < 0
                && rays[(0,57)].Direction.Y < 0 && rays[(1,57)].Direction.Y > 0,
                "literal four-pattern ordering has independent horizontal/vertical start signs");
            Check(rays[(0,113)].Direction.X < 0 && rays[(2,113)].Direction.X > 0
                && rays[(0,113)].Direction.Y > 0 && rays[(1,113)].Direction.Y < 0,
                "last scheduled-frame geometry has crossed both sweep axes before endpoint hold");

            // The source captures range at activation, but the helper consumes a
            // supplied distance. Test both admitted range boundaries independently.
            foreach (float distance in new[] { 4f, 40f })
            foreach (int frame in new[] { 57, 113, 117 })
            for (int pattern = 0; pattern < 4; pattern++)
            {
                D old = Array.Find(Landmarks, value => value.Frame == frame)!.Targets[pattern];
                D target = new(old.X, old.Y, distance);
                string label = "distance" + distance + "/pattern" + pattern + "/frame" + frame;
                var ray = Expect(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, pattern, distance, frame,
                    new(0,-.17,.16), target, new(0,1,0), label); CrossZ(ray, target, label);
            }
            var close = Actual(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, 0, 4, 57);
            var far = Actual(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, 0, 40, 57);
            Check(close.Origin == far.Origin && close.Up == far.Up && Math.Abs(close.Direction.X) > Math.Abs(far.Direction.X),
                "capture distance changes convergence angle, not muzzle offset or handed basis");

            // Independent right-handed basis examples, with literal world-space
            // targets rather than a second copy of the production basis builder.
            Expect(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0, 10, 57,
                new(.16,-.17,0), new(10,-.65,-2.4), new(0,1,0), "live aim turns ninety degrees");
            Expect(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 0, 10, 57,
                new(0,-.17,-.16), new(-2.4,-.65,-10), new(0,1,0), "live aim turns opposite");
            Expect(Vector3.Zero, new(3,4,0), new(1,8,7), 0, 10, 57,
                new(.232,.026,0), new(6.52,7.61,-2.4), new(-.8,.6,0), "nonunit tilted live aim preserves plane geometry");
            Expect(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, 0, 10, 57,
                new(-.17,.16,0), new(-.65,10,2.4), new(1,0,0), "upward aim uses horizontal facing fallback");
            Expect(Vector3.Zero, -Vector3.UnitY, Vector3.UnitZ, 0, 10, 57,
                new(.17,-.16,0), new(.65,-10,2.4), new(-1,0,0), "downward aim retains independent handed basis");
            Expect(Vector3.Zero, Vector3.UnitY, Vector3.Zero, 0, 10, 57,
                new(0,.16,.17), new(2.4,10,.65), new(0,0,-1), "zero facing fallback remains finite for vertical aim");
            Expect(Vector3.Zero, Vector3.UnitY, Vector3.UnitY, 0, 10, 57,
                new(0,.16,.17), new(2.4,10,.65), new(0,0,-1), "pure vertical facing is flattened then safely falls back");
            Expect(Vector3.Zero, Vector3.UnitY, new(3,4,4), 0, 10, 57,
                new(-.136,.16,.102), new(.92,10,2.31), new(.8,0,-.6), "nonunit tilted facing is horizontally normalized");
            Expect(Vector3.Zero, Vector3.Zero, new(9,4,-2), 0, 10, 57,
                new(0,-.17,.16), new(2.4,-.65,10), new(0,1,0), "zero live aim falls back to forward before basis selection");
            var ignoredFacing = Actual(Vector3.Zero, Vector3.UnitZ, new(9,4,-2), 0, 10, 57);
            Check(ignoredFacing == rays[(0,57)], "finite facing fallback cannot rotate a nonvertical live aim");
            var scaleInvariant = Actual(Vector3.Zero, new(0,0,12), Vector3.UnitX, 0, 10, 57);
            Check(scaleInvariant == rays[(0,57)], "nonunit live aim magnitude never changes target geometry");

            var translated = Expect(new(17.25f,-3.5f,9.75f), Vector3.UnitZ, Vector3.UnitX, 0, 10, 57,
                new(17.25,-3.67,9.91), new(19.65,-4.15,19.75), new(0,1,0), "moving native center translates current ray");
            Check(Near(translated.Direction, new(rays[(0,57)].Direction.X, rays[(0,57)].Direction.Y, rays[(0,57)].Direction.Z)),
                "moving center changes emitter/target together without changing captured-pattern direction");
            var turned = Actual(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0, 10, 57);
            Check((turned.Direction - rays[(0,57)].Direction).LengthSquared > 1,
                "same captured pattern/range responds to current live aim rather than an activation-frozen direction");
            Console.WriteLine("SCOUT DRONE LASER GEOMETRY PASS " + _checks
                + " invented literal geometry assertions; helper only, no timeline/visibility/damage/collision/render/source acceptance");
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine("SCOUT DRONE LASER GEOMETRY FAIL " + error); return 1; }
    }
}
