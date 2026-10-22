using MphRead.Formats;

namespace MphRead.Mods.Render;

internal static class ShadowReceiverPolicy
{
    // Cutouts participate only in the existing opaque alpha==1 pass. Authored
    // mesh actors/platforms use the same policy as room meshes. Procedural and
    // emissive effects, source-GX custom programs and camera-space geometry do not.
    internal static bool Eligible(RenderItem item) => item.Type == RenderItemType.Mesh
        && !item.ViewModel && item.SourceGx == null && item.Alpha >= .999f
        && item.RenderMode is RenderMode.Normal or RenderMode.Translucent
        && item.BillboardMode == BillboardMode.None
        && !item.Wireframe && item.OverrideColor == null && item.PlayerOutlineColor == null
        && item.Cosmetics.Effect == 0 && item.Cosmetics.Dissolve <= 0
        && (!item.Lighting || item.Emission.LengthSquared < .000001f);
}
