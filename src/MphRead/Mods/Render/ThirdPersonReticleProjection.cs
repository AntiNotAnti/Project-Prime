using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Render-only projection of the existing first-person muzzle-to-aim ray
    /// onto the match's displaced shoulder camera. Never converges or rewrites
    /// a shot: that would change combat/lag compensation and let a camera behind
    /// cover aim around walls. A world trace supplies the actual nearest
    /// terrain contact, while empty space uses a bounded aim distance.
    /// </summary>
    internal static class ThirdPersonReticleProjection
    {
        internal const float TraceRange = 96f;
        internal const float MinViewDepth = 0.05f;
        internal const float ScreenInset = 0.015f;

        internal static bool TryShotRay(Vector3 muzzle, Vector3 aim,
            out Vector3 direction)
        {
            direction = Vector3.Zero;
            if (!Finite(muzzle) || !Finite(aim)) return false;
            Vector3 delta = aim - muzzle;
            float lengthSquared = delta.LengthSquared;
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
                return false;
            direction = delta / MathF.Sqrt(lengthSquared);
            return Finite(direction);
        }

        internal static bool Finite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);

        /// <summary>
        /// Convert the nearest world collision percentage into a real point on
        /// the native projectile ray. Fractions are clamped defensively; a
        /// projectile that starts against a wall must still be projected safely.
        /// </summary>
        internal static Vector3 Target(Vector3 muzzle, Vector3 direction,
            float contactFraction)
        {
            return muzzle + direction * (TraceRange * Math.Clamp(
                float.IsFinite(contactFraction) ? contactFraction : 1f, 0f, 1f));
        }

        internal static bool TryProject(Vector3 target, Matrix4 view,
            Matrix4 perspective, out Vector2 reticle)
        {
            reticle = new Vector2(0.5f, 0.5f);
            if (!Finite(target)) return false;
            // The common project helper returns a positive clip W only for a
            // point in front of the camera. Never use an undefined perspective
            // divide when a nearby wall is behind the shoulder camera.
            float depth = Matrix.ProjectPosition(target, view, perspective,
                out Vector2 position);
            if (!float.IsFinite(depth) || depth <= MinViewDepth
                || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
                return false;

            // Clamp only the HUD indicator, never the underlying gameplay ray.
            // Shooting beside a close wall may legitimately point out of view.
            reticle = new Vector2(
                Math.Clamp(position.X, ScreenInset, 1f - ScreenInset),
                Math.Clamp(position.Y, ScreenInset, 1f - ScreenInset));
            return true;
        }

        /// <summary>
        /// The original 15% ease-per-frame is display-rate dependent (at 240Hz
        /// it relaxes four times as fast). This reproduces 0.15 at 60Hz while
        /// granting the same total ease after one simulated 60Hz interval.
        /// </summary>
        internal static float ReleaseFraction(double elapsedSimulationTicks)
        {
            if (!double.IsFinite(elapsedSimulationTicks)
                || elapsedSimulationTicks <= 0) return 0f;
            float steps = (float)Math.Clamp(elapsedSimulationTicks, 0d, 4d);
            return 1f - MathF.Pow(0.85f, steps);
        }
    }
}
