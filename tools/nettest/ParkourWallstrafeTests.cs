using System;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ParkourWallstrafeTests
{
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .000002f, label);
    private static readonly Vector3 Wish = Vector3.Normalize(new Vector3(-1, 0, 1));
    private static Vector3 Step(Vector3 velocity) => WallstrafeMovementV1.Step(
        velocity, 1, 1, Wish, Vector3.UnitX, true, true, false);

    internal static int Run()
    {
        try
        {
            _checks = 0;
            var v = new Vector3(.125f, -0f, 2);
            var moved = Step(v);
            Near(moved.Z, 2.5f, "fixed tick bounded tangent acceleration");
            Check(BitConverter.SingleToInt32Bits(moved.Y) == BitConverter.SingleToInt32Bits(v.Y),
                "negative zero Y bits preserved");
            Check(moved.X == v.X, "normal velocity preserved");
            var mirrored = WallstrafeMovementV1.Step(new Vector3(-v.X, v.Y, v.Z),
                1, -1, new Vector3(-Wish.X, 0, Wish.Z), -Vector3.UnitX, true, true, false);
            Check(mirrored.X == -moved.X && mirrored.Z == moved.Z, "mirrored wall symmetry");
            for (int gate = 0; gate < 3; gate++)
                Check(WallstrafeMovementV1.Step(v, 1, 1, Wish, Vector3.UnitX,
                    gate != 0, gate != 1, gate == 2) == v, "eligibility/contact/corner gate");
            foreach (var wish in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ })
                Check(WallstrafeMovementV1.Step(v, 1, 1, wish, Vector3.UnitX,
                    true, true, false) == v, "away/head-on/parallel input rejected");
            Check(WallstrafeMovementV1.Step(v, 0, 1, Wish, Vector3.UnitX, true, true, false) == v,
                "forward required");
            Check(WallstrafeMovementV1.Step(v, 1, 0, Wish, Vector3.UnitX, true, true, false) == v,
                "strafe required");
            var over = new Vector3(0, 7, WallstrafeMovementV1.MaxTangentSpeed + 1);
            Check(Step(over) == over, "incoming over-cap remains unchanged");
            var opposed = new Vector3(0, -2, -1);
            Check(Step(opposed) == opposed, "opposing velocity receives no reversal bonus");
            Near(Step(new Vector3(0, 0, WallstrafeMovementV1.MaxTangentSpeed - .125f)).Z,
                WallstrafeMovementV1.MaxTangentSpeed, "last increment limited by allowance");
            foreach (var normal in new[] { Vector3.Zero, Vector3.UnitX * 2, Vector3.UnitY,
                new Vector3(float.NaN, 0, 0), Vector3.Normalize(new Vector3(1, .01f, 0)) })
                Check(WallstrafeMovementV1.Step(v, 1, 1, Wish, normal, true, true, false) == v,
                    "invalid or sloped normal rejected");
            Check(WallstrafeMovementV1.Step(v, float.NaN, 1, Wish, Vector3.UnitX,
                true, true, false) == v, "nonfinite input rejected");
            var invalid = new Vector3(float.PositiveInfinity, 2, 3);
            Check(Step(invalid) == invalid, "nonfinite velocity untouched");
            var nanVelocity = new Vector3(BitConverter.Int32BitsToSingle(0x7fc12345), -0f, 3);
            var refusedNan = Step(nanVelocity);
            Check(BitConverter.SingleToInt32Bits(refusedNan.X) == BitConverter.SingleToInt32Bits(nanVelocity.X)
                && BitConverter.SingleToInt32Bits(refusedNan.Y) == BitConverter.SingleToInt32Bits(nanVelocity.Y)
                && refusedNan.Z == nanVelocity.Z, "nonfinite velocity payload and untouched bits preserved");
            Vector3 diagonalNormal = Vector3.Normalize(new Vector3(1, 0, 1));
            Vector3 diagonalWish = Vector3.Normalize(new Vector3(-1, 0, 0));
            var diagonal = WallstrafeMovementV1.Step(v, 1, 1, diagonalWish, diagonalNormal,
                true, true, false);
            Near(Vector3.Dot(diagonal, diagonalNormal), Vector3.Dot(v, diagonalNormal),
                "rotated wall normal projection preserved");
            for (int i = 0; i < 10000; i++) _ = Step(v);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Vector3 last = default;
            for (int i = 0; i < 10000; i++) last = Step(v);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(last == moved && allocated == 0, "warm helper allocates zero managed bytes");
            Console.WriteLine($"Parkour wallstrafe: {_checks} checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Parkour wallstrafe: " + ex.Message);
            return 1;
        }
    }
}
