using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Compatibility facade for callers that still use the original mouse
    /// accumulator name. Storage and lifetime semantics now live in
    /// <see cref="LookPredictionBuffer"/>.
    /// </summary>
    public sealed class RenderLookAccumulator : LookPredictionBuffer
    {
        public const float MaxPendingPixels = MaxPendingDegrees;

        /// <summary>
        /// Convert the original relative mouse units into degrees. Keeping
        /// this helper here preserves existing camera and test call sites.
        /// </summary>
        public static Vector2 AimDegrees(Vector2 raw, float sensitivity,
            bool invertX, bool invertY, float zoomScale = 1)
            => new Vector2(-raw.X * (invertX ? -1 : 1), -raw.Y * (invertY ? -1 : 1))
                * (sensitivity * zoomScale / 4);

        /// <summary>
        /// Apply the same post-source transforms as PlayerEntity's
        /// UpdateAimX/UpdateAimY to a preprocessed local-look delta. This is
        /// used only by render prediction when no desktop raw accumulator is
        /// available (Android stylus/touch); desktop mouse inversion and
        /// sensitivity are already handled by <see cref="AimDegrees"/>.
        /// </summary>
        public static Vector2 ApplySimulationAimTransforms(Vector2 degrees,
            bool invertX, bool invertY, float fovScale = 1)
        {
            if (!float.IsFinite(degrees.X) || !float.IsFinite(degrees.Y))
            {
                return Vector2.Zero;
            }
            if (invertX) degrees.X *= -1;
            if (invertY) degrees.Y *= -1;
            if (!float.IsFinite(fovScale)) fovScale = 1;
            return degrees * fovScale;
        }

        /// <summary>Rotate only a copied render camera; simulation is untouched.</summary>
        public static Matrix4 ApplyCameraLook(Matrix4 camera, Vector2 aim,
            float simulationPitch)
        {
            aim.Y = System.Math.Clamp(simulationPitch + aim.Y, -85, 85) - simulationPitch;
            Vector3 position = camera.Row3.Xyz;
            camera.Row3.Xyz = Vector3.Zero;
            camera = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(aim.Y)) * camera
                * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(aim.X));
            camera.Row3.Xyz = position;
            return camera;
        }
    }
}
