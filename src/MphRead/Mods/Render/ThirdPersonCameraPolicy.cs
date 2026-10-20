using System;
using OpenTK.Mathematics;
using MphRead.Entities;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Display-only shoulder-camera policies. Never changes player camera,
    /// simulation collision, shot direction or server-owned match state.
    /// </summary>
    internal static class ThirdPersonCameraPolicy
    {
        internal const float LocalBodyFadeStart = 0.30f;
        internal const float LocalBodyFadeEnd = 1.05f;

        /// <summary>
        /// The shoulder view must become active as soon as the first spawned
        /// biped frame is drawable, even if a spawn pad or wall constrains its
        /// camera. Distance affects local-body visibility, not camera mode.
        /// </summary>
        internal static bool ShowShoulder(bool isLiveMatchView, CameraType type,
            bool isAltForm) =>
            isLiveMatchView && type == CameraType.First && !isAltForm;

        /// <summary>
        /// Close geometry: hide rather than clip the local Hunter into the lens.
        /// Smooth fade replaces the old binary 0.8-unit first-person fallback.
        /// The first-person arm cannon must remain hidden while this is active.
        /// </summary>
        internal static float LocalBodyOpacity(float collisionDistance)
        {
            if (!float.IsFinite(collisionDistance))
                return 0f;
            float t = Math.Clamp(
                (collisionDistance - LocalBodyFadeStart) /
                (LocalBodyFadeEnd - LocalBodyFadeStart), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
