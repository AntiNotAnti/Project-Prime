using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ParkourWallrunTests
{
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .00001f, label);
    private static bool SameBits(Vector3 a, Vector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    internal static int Run(string? output = null)
    {
        try
        {
            _checks = 0;
            Check(WallrunMovementV1.AirborneTicks == 45, "fixed airborne budget is 45 ticks");
            Near(WallrunMovementV1.GravityScale, .35f, "fixed gravity scale");
            Near(WallrunMovementV1.KickOutwardSpeed, 8, "outward kick floor");
            Near(WallrunMovementV1.KickUpSpeed, 9, "upward kick floor");
            Near(WallrunMovementV1.MinimumTangentSpeed,
                TraversalMovementProfileV1.GroundSpeed * .5f, "entry tangent threshold");
            Check(Quake3MovementProfileV4.ProfileVersion == 4
                && TraversalMovementProfileV5.ProfileVersion == 5,
                "historical profile version constants remain unchanged");
            Near(TraversalMovementProfileV1.AirTurnRadiansPerSecond, 1.8f,
                "historical traversal air turn constant remains unchanged");
            Near(TraversalStance.HeightRatio, .65f, "historical crouch ratio remains unchanged");

            foreach (Vector3 normal in new[] { Vector3.UnitX, -Vector3.UnitX,
                Vector3.UnitZ, -Vector3.UnitZ, Vector3.Normalize(new Vector3(1, 0, 1)),
                Vector3.Normalize(new Vector3(-3, 0, 4)) })
            {
                Vector3 tangent = new(-normal.Z, 0, normal.X);
                foreach (float along in new[] { -23f, 0f, 17f })
                    foreach (float outward in new[] { -12f, 0f, 7f, 8f, 19f })
                        foreach (float up in new[] { -20f, 0f, 9f, 22f })
                        {
                            Vector3 velocity = normal * outward + tangent * along + Vector3.UnitY * up;
                            Vector3 result = WallrunMovementV1.Kick(velocity, normal);
                            Near(Vector3.Dot(result, tangent), along, "kick preserves wall tangent for rotated/mirrored normals");
                            Near(Vector3.Dot(result, normal), MathF.Max(outward, 8),
                                "kick floors outward component without reducing greater incoming velocity");
                            Near(result.Y, MathF.Max(up, 9), "kick floors upward velocity without reducing greater incoming velocity");
                        }
            }
            var ordinary = new Vector3(-2, -3, 11);
            foreach (Vector3 normal in new[] { Vector3.Zero, Vector3.UnitY, Vector3.UnitX * 2,
                Vector3.UnitX * .5f, new Vector3(1, .001f, 0),
                new Vector3(float.NaN, 0, 1), new Vector3(float.PositiveInfinity, 0, 0),
                new Vector3(0, 0, float.NegativeInfinity), new Vector3(float.MaxValue, 0, 0) })
                Check(SameBits(WallrunMovementV1.Kick(ordinary, normal), ordinary),
                    "invalid/sloped/nonfinite/overflowing normal is a bit-preserving no-op");
            foreach (Vector3 invalid in new[] {
                new Vector3(BitConverter.Int32BitsToSingle(0x7fc12345), -0f, 3),
                new Vector3(2, float.PositiveInfinity, -0f),
                new Vector3(float.NegativeInfinity, 2, 3),
                new Vector3(float.MaxValue, 0, 1) })
                Check(SameBits(WallrunMovementV1.Kick(invalid, Vector3.UnitX), invalid),
                    "nonfinite/overflowing velocity preserves every original component bit");

            Vector3 last = default;
            for (int i = 0; i < 20000; i++) last = WallrunMovementV1.Kick(ordinary, Vector3.UnitX);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20000; i++) last = WallrunMovementV1.Kick(ordinary, Vector3.UnitX);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(last == new Vector3(8, 9, 11) && allocated == 0,
                "warmed kick helper allocates zero managed bytes");
            if (output != null)
            {
                string path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    airborneTicks = WallrunMovementV1.AirborneTicks,
                    gravityScale = WallrunMovementV1.GravityScale,
                    warmedAllocatedBytes = allocated }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"Parkour wallrun: {_checks} checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Parkour wallrun: " + ex.Message);
            return 1;
        }
    }
}
