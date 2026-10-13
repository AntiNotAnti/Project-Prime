#if !MPHREAD_SERVER
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class DepthReplayAttributes
{
    // Also proves that the first opaque pass and its replay have identical
    // vertex positions and alpha coverage despite intervening current attributes.
    internal static bool TryResolve(IReadOnlyList<RetainedDrawPacket> packets, bool showTextures,
        out DisplayListAttributeEffect result)
    {
        result = default;
        if (!DepthSnapshotEligibility.CanUse(packets)) return false;
        for (int i = 0; i < packets.Count; i++)
        {
            RenderItem item = packets[i].Item;
            DisplayListAttributeEffect effects;
            if (item.Type == RenderItemType.Mesh)
            {
                if (!GraphicsApi.TryGetDisplayListAttributeEffects(item.ListId, out effects)
                    || effects.ReadsInheritedTexcoord) return false;
                if (item.HasTexture && showTextures && item.TexgenMode == TexgenMode.Normal
                    && item.PolygonMode != PolygonMode.Decal && effects.ReadsInheritedNormal) return false;
            }
            else
            {
                if (item.WeightedSkinning || item.HasTexture && showTextures
                    && item.TexgenMode == TexgenMode.Normal && item.PolygonMode != PolygonMode.Decal) return false;
                int count;
                if (item.Type == RenderItemType.TrailMulti)
                {
                    if (item.ItemCount < 4 || item.ItemCount % 2 != 0) return false;
                    count = item.ItemCount / 2;
                }
                else if (item.Type == RenderItemType.TrailStack)
                {
                    if (item.ItemCount <= 0 || item.ItemCount > ProceduralFrameGeometry.MaxVertices / 4) return false;
                    count = item.ItemCount * 4;
                }
                else count = 4;
                if (count <= 0 || item.Points == null || count > item.Points.Length / 2) return false;
                Vector3 uv = item.Points[(count - 1) * 2];
                if (item.Type == RenderItemType.Particle) uv = new(uv.X * item.ScaleS, uv.Y * item.ScaleT, 0);
                effects = new DisplayListAttributeEffect(false, default, false, default, true, uv, false, false);
            }
            // RenderItem always starts each geometry with its own diffuse color.
            result = result.Merge(new DisplayListAttributeEffect(true, new Vector4(item.Diffuse, 1),
                false, default, false, default, false, false)).Merge(effects);
        }
        return true;
    }

    internal static void Apply(DisplayListAttributeEffect effects)
    {
        if (effects.WritesColor) GraphicsApi.Color4(effects.Color.X, effects.Color.Y, effects.Color.Z, effects.Color.W);
        if (effects.WritesNormal) GraphicsApi.Normal3(effects.Normal.X, effects.Normal.Y, effects.Normal.Z);
        if (effects.WritesTexcoord) GraphicsApi.TexCoord3(effects.Texcoord);
    }
}
#endif
