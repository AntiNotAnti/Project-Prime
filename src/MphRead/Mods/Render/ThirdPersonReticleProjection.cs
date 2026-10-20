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
        /// Match a rendered shoulder reticle to the local view's late-latched
        /// rotation and previous/current translation. A cached 60Hz muzzle-to-
        /// aim line is correct for the authoritative shot, but projecting its
        /// fixed *world* endpoint from a camera that has consumed newer
        /// pointer input makes the HUD reticle race to the screen edge and
        /// spring back on the next 60Hz update. That is perceived as doubling
        /// or ghosting during high-speed turns.
        ///
        /// Convert the canonical ray into simulation-camera space and then
        /// back into this picture's camera basis. Preserve its collision
        /// distance and muzzle parallax, but NEVER use this presentation ray
        /// for firing, aiming authority, targets, or replay recording.
        ///
        /// The renderer already computes inverted draw rotation for light,
        /// billboards and culling. Accept it directly to avoid a per-HUD
        /// inverse or quaternion operation.
        /// </summary>
        internal static bool TryProjectDrawRay(Vector3 muzzle, Vector3 direction,
            float collisionDistance, Vector3 simulationEye, Vector3 drawnNativeEye,
            Matrix4 simulationView, Matrix4 drawInverseRotation, Matrix4 drawView,
            Matrix4 projection, out Vector2 reticle)
        {
            reticle = new Vector2(.5f, .5f);
            if (!Finite(muzzle) || !Finite(direction) || !Finite(simulationEye)
                || !Finite(drawnNativeEye) || !float.IsFinite(collisionDistance)
                || collisionDistance < 0f || collisionDistance > TraceRange + .001f)
                return false;

            Vector3 local = Matrix.Vec3MultMtx3(direction, simulationView);
            Vector3 rotated = Matrix.Vec3MultMtx3(local, drawInverseRotation);
            float lengthSquared = rotated.LengthSquared;
            if (!Finite(rotated) || !float.IsFinite(lengthSquared)
                || lengthSquared <= .000001f)
                return false;
            rotated /= MathF.Sqrt(lengthSquared);

            // The native eye already blends body translation at high refresh.
            // Slide the muzzle by the exact same amount rather than letting
            // the 60Hz cannon origin oscillate against a smoothly moving view.
            Vector3 projectedMuzzle = muzzle + drawnNativeEye - simulationEye;
            Vector3 contact = projectedMuzzle + rotated * collisionDistance;
            if (TryProject(contact, drawView, projection, out reticle))
                return true;

            // Contact can be behind the camera when a Hunter fires into an
            // immediately adjacent wall. A ray farther in the *same visual
            // direction* gives the HUD a stable indication; do not switch to
            // the native reticle timeline halfway through the frame.
            Vector3 far = projectedMuzzle + rotated * TraceRange;
            return TryProject(far, drawView, projection, out reticle);
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
