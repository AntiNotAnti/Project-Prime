using System;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// Pure projection and visibility rules for the existing native 3D
    /// campaign pause map. Does not own the room models, shaders or UI input.
    /// </summary>
    public static class CampaignMapPresentation
    {
        public const float MinZoom = 0.4375f;
        public const float MaxZoom = 3f;
        public const float DefaultZoom = 0.75f;

        public static float ClampZoom(float zoom)
            => Single.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : DefaultZoom;

        /// <summary>
        /// Preserve the original 192-unit map height, extend the horizontal
        /// view for 16:9 and ultrawide displays, and retain a 4:3 floor on
        /// portrait/phone screens to avoid clipping native legend elements.
        /// </summary>
        public static (float Width, float Height) Projection(int width, int height, float zoom)
        {
            float scale = ClampZoom(zoom);
            float aspect = width > 0 && height > 0
                ? Math.Clamp((float)width / height, 4f / 3f, 21f / 9f)
                : 4f / 3f;
            float mapHeight = 192f * scale;
            return (mapHeight * aspect, mapHeight);
        }

        /// <summary>
        /// MphRead.Matrix.ProjectPosition returns coordinates in 0..1 from
        /// the top-left, not -1..1 clip coordinates. Re-transforming it put
        /// map-room picks and teleporter icons into the wrong quadrant.
        /// </summary>
        public static bool Visible(float x, float y)
            => Single.IsFinite(x) && Single.IsFinite(y)
                && x > 0f && x < 1f && y > 0f && y < 1f;

        public static float CenterDistanceSquared(float x, float y)
        {
            if (!Single.IsFinite(x) || !Single.IsFinite(y)) return Single.PositiveInfinity;
            float dx = 2f * x - 1f;
            float dy = 2f * y - 1f;
            return dx * dx + dy * dy;
        }
    }
}
