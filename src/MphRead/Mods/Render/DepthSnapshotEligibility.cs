using System.Collections.Generic;

namespace MphRead.Mods.Render;

internal static class DepthSnapshotEligibility
{
    // Necessary shader/geometry proof only. Caller must separately prove mesh
    // UV coverage does not inherit changed current attributes, and emulate the
    // final replay attribute writes using verified display-list metadata.
    internal static bool CanUse(IReadOnlyList<RetainedDrawPacket> opaque)
    {
        for (int i = 0; i < opaque.Count; i++)
        {
            RenderItem item = opaque[i].Item;
            if (!DepthCoveragePreparation.CanUse(item, true, false, procedural: true)
                || item.PlayerOutlineColor != null || item.TexturedPlayerSkin || item.PaletteOverride != null
                || !float.IsFinite(item.Alpha)) return false;
        }
        return true;
    }
}
