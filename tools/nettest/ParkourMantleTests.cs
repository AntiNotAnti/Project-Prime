using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ParkourMantleTests
{
    internal static int Run(string? output = null)
    {
        int checks = 0;
        void Check(bool value, string label)
        { checks++; if (!value) throw new InvalidOperationException(label); }
        try
        {
            Check(MantleMovementV1.MaxRise == 1.25f && MantleMovementV1.MaxTravel == 2.5f
                && MantleMovementV1.Ticks == 24, "fixed mantle rise, travel and tick bounds");
            Vector3 minimum = new(-.5f), maximum = new(.5f);
            bool Overlaps(Vector3 a, Vector3 b, Vector3 c)
                => MantleMovementV1.TriangleOverlapsBox(a, b, c, minimum, maximum);
            Check(Overlaps(new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)),
                "triangle crossing the complete box is found even with all vertices outside");
            Check(!Overlaps(new(2, 2, 0), new(2, 0, 2), new(0, 2, 2)),
                "triangle-plane separation rejects overlapping coordinate bounds");
            Check(!Overlaps(new(.5f, -2, -2), new(.5f, 2, -2), new(.5f, 0, 2)),
                "a tangent face does not obstruct an open route boundary");
            Check(Overlaps(new(.49f, -2, -2), new(.49f, 2, -2), new(.49f, 0, 2)),
                "a thin face penetrating the route flank is an obstruction");
            Check(!Overlaps(new(.6f, -2, -2), new(.6f, 2, -2), new(.6f, 0, 2)),
                "separated parallel rail is clear");
            Vector3[] floor = { new(-2, 1, -2), new(-2, 1, 2), new(2, 1, 2), new(2, 1, -2) };
            Check(MantleMovementV1.SupportsDisk(floor, new(0, 1, 0), .5f),
                "broad planar native polygon contains the full foot disk");
            Check(!MantleMovementV1.SupportsDisk(floor, new(1.8f, 1, 0), .5f),
                "center-only support cannot admit a disk crossing the ledge edge");
            Vector3[] strip = { new(-.3f, 1, -2), new(-.3f, 1, 2), new(.3f, 1, 2), new(.3f, 1, -2) };
            Check(!MantleMovementV1.SupportsDisk(strip, new(0, 1, 0), .5f),
                "a narrow center strip cannot support an upright biped");
            Vector3[] sloped = { new(-2, 1, -2), new(-2, 1, 2), new(2, 1.1f, 2), new(2, 1.1f, -2) };
            Check(!MantleMovementV1.SupportsDisk(sloped, new(0, 1, 0), .5f),
                "nonhorizontal support fails closed");
            Vector3[] concave = { new(-2, 1, -2), new(-2, 1, 2), new(0, 1, 0), new(2, 1, 2), new(2, 1, -2) };
            Check(!MantleMovementV1.SupportsDisk(concave, new(0, 1, -1), .2f),
                "ambiguous concave support fails closed");
            Check(!MantleMovementV1.SupportsDisk(Array.Empty<Vector3>(), new(0, 1, 0), .5f)
                && !MantleMovementV1.SupportsDisk(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2 }, Vector3.Zero, .5f),
                "empty and degenerate polygons do not prove support");
            Check(!MantleMovementV1.SupportsDisk(floor, new(float.NaN, 1, 0), .5f)
                && !MantleMovementV1.SupportsDisk(floor, new(0, 1, 0), float.NaN),
                "nonfinite foot or radius cannot prove support");
            foreach (Vector3 invalid in new[] { new Vector3(float.NaN, 0, 0),
                new Vector3(0, float.PositiveInfinity, 0), new Vector3(0, 0, float.NegativeInfinity) })
                Check(Overlaps(invalid, Vector3.UnitX, Vector3.UnitY)
                    && Overlaps(Vector3.UnitX, invalid, Vector3.UnitY)
                    && Overlaps(Vector3.UnitX, Vector3.UnitY, invalid),
                    "nonfinite triangle data fails closed as a route obstruction");
            foreach (float angle in new[] { 0f, .2f, .8f, 1.57f, 3.14f })
                foreach (float mirror in new[] { -1f, 1f })
                {
                    float sine = MathF.Sin(angle), cosine = MathF.Cos(angle);
                    Vector3 Transform(Vector3 v) => new(mirror * (v.X * cosine - v.Z * sine), v.Y,
                        v.X * sine + v.Z * cosine);
                    Vector3[] rotated = Array.ConvertAll(floor, Transform);
                    Check(MantleMovementV1.SupportsDisk(rotated, Transform(new(0, 1, 0)), .5f),
                        "rotated and mirrored broad footprint retains whole-disk support");
                    Check(!MantleMovementV1.SupportsDisk(rotated, Transform(new(1.8f, 1, 0)), .5f),
                        "rotated and mirrored overhanging disk remains unsupported");
                    Vector3 a = Transform(new(-2, 0, -2)), b = Transform(new(2, 0, -2)), c = Transform(new(0, 0, 2));
                    Check(Overlaps(a, b, c) && Overlaps(c, b, a),
                        "SAT crossing survives rotated and mirrored triangles and reversed winding");
                }
            bool supported = false, blocked = false;
            for (int i = 0; i < 20000; i++)
            {
                supported = MantleMovementV1.SupportsDisk(floor, new(0, 1, 0), .5f);
                blocked = Overlaps(new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2));
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20000; i++)
            {
                supported = MantleMovementV1.SupportsDisk(floor, new(0, 1, 0), .5f);
                blocked = Overlaps(new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(supported && blocked && allocated == 0, "warmed geometry helpers allocate zero managed bytes");
            Check(ParkourMovementProfileV1.ProfileVersion == 1 && ParkourMovementProfileV2.ProfileVersion == 2
                && ParkourMovementProfileV3.ProfileVersion == 3 && TraversalMovementProfileV5.ProfileVersion == 5,
                "historical movement profiles retain their identities");
            if (output != null)
            {
                string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { status = "PASS", checks, warmedAllocatedBytes = allocated },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"Parkour mantle: {checks} checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Parkour mantle: " + error); return 1; }
    }
}
