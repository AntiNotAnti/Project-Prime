using System;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>Actual camera poses before any simulation update; no GL or game assets.</summary>
    internal static class CameraStartupCheck
    {
        internal static int Run()
        {
            int failures = 0;
            int checks = 0;
            void Check(bool ok, string name)
            {
                checks++;
                if (!ok) failures++;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} camera startup {name}");
            }

            var random = new MatchRandom();
            random.SetRng1(0x12345678);
            random.SetRng2(0x87654321);
            var camera = new CameraInfo { Random = random };
            camera.Reset();
            Check(ValidView(camera) && Near(camera.Facing, -Vector3.UnitZ)
                && Near(camera.TrueUp, Vector3.UnitY), "fresh reset has an invertible view before Update");
            camera.Shake = 2;
            camera.RebuildView();
            Check(camera.Shake == 2 && ShakeTick(camera)
                && random.Rng1 == 0x12345678 && random.Rng2 == 0x87654321,
                "view refresh preserves an armed shake tick");

            // Reuse must replace the old eye/basis, without stepping shake.
            camera.Position = new Vector3(30, 20, -10);
            camera.Target = camera.Position + Vector3.UnitX;
            camera.Shake = 2;
            camera.Update();
            bool shakeTick = ShakeTick(camera);
            uint rng1 = random.Rng1;
            uint rng2 = random.Rng2;
            float shake = camera.Shake;
            camera.ViewMatrix = default;
            camera.Reset();
            Check(ValidView(camera) && Near(camera.Position, Vector3.UnitZ)
                && Near(camera.Target, Vector3.Zero) && Near(camera.Facing, -Vector3.UnitZ),
                "reused reset replaces stale view and basis");
            Check(camera.Shake == shake && ShakeTick(camera) == shakeTick
                && random.Rng1 == rng1 && random.Rng2 == rng2,
                "reset does not advance shake or either RNG stream");

            // This is the Initialize sequence after its nonorigin pose is ready.
            camera.Position = new Vector3(123.25f, -7.5f, 49);
            camera.Target = camera.Position + new Vector3(.3f, .4f, -.8f);
            camera.UpVector = Vector3.UnitY;
            camera.RebuildView();
            Check(ValidView(camera) && Near(camera.Facing,
                (camera.Target - camera.Position).Normalized()), "prepared pose owns the inverse eye");
            Check(camera.Shake == shake && ShakeTick(camera) == shakeTick
                && random.Rng1 == rng1 && random.Rng2 == rng2,
                "prepared view refresh preserves shake phase and RNG");
            camera.ModResetDrawState();
            foreach (double alpha in new[] { 0.0, .5, 1.0 })
            {
                bool pose = camera.ModGetDrawPose(alpha, out Vector3 eye,
                    out Vector3 target, out Vector3 up, out float fov);
                Check(pose && Near(eye, camera.Position) && Near(target, camera.Target)
                    && Near(up, Vector3.UnitY) && fov == camera.Fov
                    && Near(camera.ModGetDrawView(alpha), camera.ViewMatrix),
                    $"prepared draw history is rebased at alpha {alpha}");
            }

            // Rebuilding is also safe for the same invalid poses Update accepts.
            camera.PrevPosition = new Vector3(4, 5, 6);
            camera.Position = new Vector3(float.NaN, 0, 0);
            camera.Target = new Vector3(float.NaN, 0, 0);
            camera.Facing = Vector3.UnitZ;
            camera.UpVector = Vector3.UnitZ;
            camera.RebuildView();
            Check(ValidView(camera) && Near(camera.Position, camera.PrevPosition)
                && Near(camera.Facing, Vector3.UnitZ), "invalid pose and parallel up rebuild finitely");
            Check(camera.Shake == shake && ShakeTick(camera) == shakeTick
                && random.Rng1 == rng1 && random.Rng2 == rng2,
                "invalid pose repair remains free of simulation side effects");

            // Keep the existing alternating three-draw shake behavior and its
            // numeric target/decay result, rather than calling Update at startup.
            var expectedRandom = new MatchRandom();
            expectedRandom.SetRng2(0x13579BDF);
            random.SetRng2(expectedRandom.Rng2);
            camera = new CameraInfo { Random = random };
            camera.Reset();
            camera.Position = new Vector3(7, -2, 11);
            camera.UpVector = Vector3.UnitY;
            camera.Shake = .4f;
            for (int step = 0; step < 3; step++)
            {
                Vector3 unshaken = new Vector3(.5f, .25f, -1);
                camera.Target = camera.Position + unshaken;
                Vector3 expectedTarget = camera.Target;
                float expectedShake = camera.Shake;
                if (step != 1)
                {
                    expectedTarget.X += Fixed.ToFloat(expectedRandom.GetRandomInt2(Fixed.ToInt(expectedShake))) - expectedShake / 2;
                    expectedTarget.Y += Fixed.ToFloat(expectedRandom.GetRandomInt2(Fixed.ToInt(expectedShake))) - expectedShake / 2;
                    expectedTarget.Z += Fixed.ToFloat(expectedRandom.GetRandomInt2(Fixed.ToInt(expectedShake))) - expectedShake / 2;
                    expectedShake *= .85f;
                }
                camera.Update();
                Vector3 facing = (expectedTarget - camera.Position).Normalized();
                Vector3 expectedUp = Vector3.Cross(Vector3.Cross(facing, Vector3.UnitY), facing).Normalized();
                Check(Near(camera.Target, expectedTarget) && camera.Shake == expectedShake
                    && random.Rng2 == expectedRandom.Rng2 && ShakeTick(camera) == (step == 1)
                    && Near(camera.Facing, facing) && Near(camera.TrueUp, expectedUp)
                    && Near(camera.ViewMatrix, Matrix4.LookAt(camera.Position, expectedTarget, expectedUp))
                    && ValidView(camera), $"Update retains shake target, RNG, phase and view at step {step}");
            }
            Console.WriteLine($"FRAMETIMING camera startup {checks} checks, {failures} failures");
            return failures;
        }

        private static bool ShakeTick(CameraInfo camera) => (bool)typeof(CameraInfo)
            .GetField("_shake", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(camera)!;

        private static bool ValidView(CameraInfo camera)
        {
            Matrix4 view = camera.ViewMatrix;
            if (!Finite(view.Row0) || !Finite(view.Row1) || !Finite(view.Row2) || !Finite(view.Row3)
                || !float.IsFinite(view.Determinant) || MathF.Abs(view.Determinant) < .5f)
                return false;
            Matrix4 inverse = view.Inverted();
            return Finite(inverse.Row0) && Finite(inverse.Row1) && Finite(inverse.Row2)
                && Finite(inverse.Row3) && Near(inverse.Row3.Xyz, camera.Position);
        }

        private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y)
            && float.IsFinite(v.Z) && float.IsFinite(v.W);
        private static bool Near(Vector3 a, Vector3 b) => (a - b).LengthSquared < .00000001f;
        private static bool Near(Matrix4 a, Matrix4 b) => (a.Row0 - b.Row0).LengthSquared < .00000001f
            && (a.Row1 - b.Row1).LengthSquared < .00000001f
            && (a.Row2 - b.Row2).LengthSquared < .00000001f
            && (a.Row3 - b.Row3).LengthSquared < .00000001f;
    }
}
