using System.Collections.Generic;

namespace MphRead.Mods.Render;

internal static class DepthReplayPolicy
{
    // MarkTranslucent retains depth writes. Reuse is legal only when it cannot
    // pass its alpha<1 test, and no preceding decal could have changed depth.
    internal static bool CanKeepOpaqueDepth(int decalCount, IReadOnlyList<RetainedDrawPacket> translucent,
        bool showTextures, bool drawingOutline, bool forceLegacy = false)
    {
        if (forceLegacy || drawingOutline || decalCount != 0) return false;
        for (int i = 0; i < translucent.Count; i++)
            if (!RejectsTranslucentAlpha(translucent[i].Item, showTextures, drawingOutline)) return false;
        return true;
    }

    internal static bool RejectsTranslucentAlpha(RenderItem item, bool showTextures, bool drawingOutline)
        => !drawingOutline && (item.Type == RenderItemType.Mesh || ProceduralStateReuse.IsProcedural(item.Type))
        && float.IsFinite(item.Alpha) && item.Alpha >= 1f
        && item.OverrideColor == null && item.PaletteOverride == null && item.PlayerOutlineColor == null
        && !item.TexturedPlayerSkin && item.Cosmetics == default && item.CosmeticMaterial == default
        // The canonical vertex shader always produces color.a=1. Decal mode
        // preserves it regardless of sampled alpha; other modes sample alpha.
        && (!showTextures || !item.HasTexture || item.PolygonMode == PolygonMode.Decal);
}
