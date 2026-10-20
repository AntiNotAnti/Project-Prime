using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class ReplayDirectorShotTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidDataException(message); _checks++; }
    private static float Degrees(Vector3 a, Vector3 b)
        => MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1, 1)));
    public static int Run()
    {
        _checks = 0;
        try
        {
            var shots = new ReplayDirectorShot[ReplayDirectorShots.MaximumCandidates];
            int Generate(Func<Vector3, Vector3, bool> clear, ReplayDirectorPov? victim = null, Vector3? fixedPosition = null,
                ReplayDirectorShotKind? previous = null, float fov = 80)
                => ReplayDirectorShots.Generate(shots, Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY,
                    90, ReplayCameraMode.Chase, fov, previous, clear, victim, fixedPosition);
            int count = Generate((_, _) => true);
            Check(count == 5 && shots.Take(count).All(s => s.Kind != ReplayDirectorShotKind.VictimPov && s.Kind != ReplayDirectorShotKind.Fixed),
                "unknown victim and arena camera must not be fabricated");
            var original = shots.Take(count).ToArray();
            Check(Generate((_, _) => true) == count && shots.Take(count).SequenceEqual(original), "same frame and style must be deterministic");
            Check(original.OrderByDescending(s => s.Score).First().Kind == ReplayDirectorShotKind.Chase, "requested chase style should prefer chase");
            Generate((_, _) => true, previous: ReplayDirectorShotKind.Shoulder);
            Check(shots.Single(s => s.Kind == ReplayDirectorShotKind.Shoulder).Score == original.Single(s => s.Kind == ReplayDirectorShotKind.Shoulder).Score + 8,
                "previous valid shot gets explicit hysteresis bonus");
            Check(Generate((_, _) => false) == 1 && shots[0].Kind == ReplayDirectorShotKind.KillerPov,
                "fully blocked cinematic candidates leave canonical POV");
            int rays = 0;
            Check(Generate((a, b) => { rays++; return (a.Y == 1 && b.Y == 1); }) == 1 && rays > 4,
                "center LOS alone cannot pass offset camera clearance");
            count = Generate((_, _) => true, new ReplayDirectorPov(Vector3.UnitX, Vector3.UnitZ), new Vector3(8, 10, 8), fov: 500);
            Check(count == 7 && shots.All(s => s.FovDegrees == 120), "explicit victim/fixed cameras complete seven candidates with clamped FOV");
            count = Generate((_, _) => true, new ReplayDirectorPov(Vector3.Zero, Vector3.Zero), new Vector3(float.NaN), fov: -20);
            Check(count == 5 && shots.Take(count).All(s => s.FovDegrees == 40), "degenerate optional cameras refused, low FOV clamped");
            count = ReplayDirectorShots.Generate(shots, Vector3.Zero, Vector3.Zero, Vector3.One, 0,
                ReplayCameraMode.Chase, 80, null, (_, _) => true);
            Check(count == 0, "invalid actor direction refuses candidates");
            count = ReplayDirectorShots.Generate(shots, Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 90,
                ReplayCameraMode.Chase, 80, null, (_, _) => true,
                new ReplayDirectorPov(Vector3.UnitX, Vector3.UnitZ), preferVictim: true);
            Check(shots.Take(count).OrderByDescending(s => s.Score).First().Kind == ReplayDirectorShotKind.VictimPov,
                "explicit valid victim preference can select that POV");
            Check(!ReplayDirectorShots.IsClear(Vector3.Zero, Vector3.UnitZ, (a, b) => a.X == 0 && b.X == 0)
                && !ReplayDirectorShots.SweepClear(Vector3.Zero, Vector3.UnitZ, (a, b) => a.X == 0 && b.X == 0),
                "smoothed endpoint and movement sweep must preserve offset footprint clearance");
            Check(ReplayDirectorShots.SweepClear(Vector3.Zero, Vector3.Zero, (_, _) => true)
                && ReplayDirectorShots.SweepClear(Vector3.Zero, Vector3.UnitY, (_, _) => true)
                && !ReplayDirectorShots.SweepClear(new Vector3(float.NaN), Vector3.Zero, (_, _) => true),
                "stationary and vertical sweeps stay finite while invalid input is refused");
            Vector3 limited = ReplayDirectorShots.LimitFacing(Vector3.UnitZ, Vector3.UnitX, 1);
            Check(Math.Abs(Degrees(Vector3.UnitZ, limited) - 2) < .002f && Math.Abs(limited.Length - 1) < .00001f,
                "one canonical frame limits angular speed to two degrees");
            Check(ReplayDirectorShots.LimitFacing(Vector3.UnitZ, Vector3.UnitX, 0) == Vector3.UnitZ
                && ReplayDirectorShots.LimitFacing(Vector3.UnitZ, Vector3.UnitX, -1) == Vector3.UnitZ
                && ReplayDirectorShots.LimitFacing(Vector3.UnitZ, Vector3.UnitX, double.NaN) == Vector3.UnitZ,
                "paused reversed and invalid clocks hold orientation");
            Check(ReplayDirectorShots.LimitFacing(Vector3.UnitZ, Vector3.UnitX, 60) == Vector3.UnitX, "sufficient elapsed time reaches desired orientation");
            limited = ReplayDirectorShots.LimitFacing(Vector3.UnitZ, -Vector3.UnitZ, 1);
            Check(Math.Abs(Degrees(Vector3.UnitZ, limited) - 2) < .002f && float.IsFinite(limited.X), "opposite directions have deterministic finite bounded rotation");
            Check(ReplayDirectorShots.LimitFacing(Vector3.Zero, Vector3.Zero, 1) == -Vector3.UnitZ
                && ReplayDirectorShots.LimitFacing(Vector3.UnitX, new Vector3(float.NaN), 1) == Vector3.UnitX,
                "degenerate directions never produce invalid view vectors");
            Console.WriteLine($"Replay director shots: PASS {_checks}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Replay director shots: FAIL " + ex); return 1; }
    }
}
