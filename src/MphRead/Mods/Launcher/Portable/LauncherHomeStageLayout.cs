using System;

namespace MphRead.Mods.Launcher;

/// <summary>One Home composition shared by the model viewport and all four pedestals.</summary>
internal readonly record struct LauncherHomeStageLayout(
    float CenterX, float Scale, float Left, float Right, float StageTop, float StageBottom,
    float HunterLeft, float HunterTop, float HunterRight, float HunterBottom, float RearY)
{
    internal static bool TryCreate(float left, float right, float top, float bottom,
        MenuStageProfile profile, out LauncherHomeStageLayout layout)
    {
        layout = default;
        if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(top) || !float.IsFinite(bottom)
            || left < 0 || right > 1 || top < 0 || bottom > 1 || right - left < .04f || bottom - top < .10f)
            return false;
        float gutter = Math.Min(.012f, (right - left) * .06f);
        // Leave vertical breathing room for the far party platform above the
        // local silhouette while retaining a uniformly scaled model viewport.
        float scale = Math.Min(.80f, (right - left - 2 * gutter) / .50f);
        float floor = Math.Min(profile.HunterBottom, bottom - .015f);
        // Keep the local model below the header. The rear pedestal belongs to
        // the floor depth rows, rather than the empty band above the model.
        float reservedTop = top + .030f;
        float spanY = profile.HunterBottom - profile.HunterTop;
        float spanX = profile.HunterRight - profile.HunterLeft;
        if (spanY <= 0 || spanX <= 0) return false;
        scale = Math.Min(scale, (floor - reservedTop) / spanY);
        if (scale <= .01f) return false;
        float center = (left + right) * .5f;
        layout = new(center, scale, left, right, top, bottom,
            center - spanX * scale * .5f, floor - spanY * scale,
            center + spanX * scale * .5f, floor,
            Math.Max(top + .034f + .32f * scale, floor - .62f * scale));
        return true;
    }

    // UV coordinates use the same top-left origin as the native document.
    // The viewport scales uniformly about its foot line to retain camera aspect.
    internal (float X, float Y, float RadiusX, float RadiusY) Pad(int index) => index switch
    {
        0 => (CenterX, HunterBottom + (.805f - .94f) * Scale, .235f * Scale, .060f * Scale),
        1 => (CenterX, RearY, .048f * Scale, .016f * Scale),
        2 => (CenterX - .160f * Scale, HunterBottom + (.620f - .94f) * Scale, .060f * Scale, .020f * Scale),
        3 => (CenterX + .160f * Scale, HunterBottom + (.620f - .94f) * Scale, .060f * Scale, .020f * Scale),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
