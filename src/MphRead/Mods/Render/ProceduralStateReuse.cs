using MphRead;

namespace MphRead.Mods.Render;

internal static class ProceduralStateReuse
{
    internal static bool IsProcedural(RenderItemType type) => type is RenderItemType.Particle
        or RenderItemType.TrailSingle or RenderItemType.TrailMulti or RenderItemType.TrailStack;

    // Caller separately proves exact shared material/raster state and adjacency
    // in one pass. These primitives emit only vertices/UVs, never uniforms/color.
    internal static bool CanReuseObjectState(RenderItem a, RenderItem b)
        => IsProcedural(a.Type) && IsProcedural(b.Type)
        && !a.ViewModel && !b.ViewModel && !a.WeightedSkinning && !b.WeightedSkinning
        && a.BillboardMode == BillboardMode.None && b.BillboardMode == BillboardMode.None
        && a.MatrixStackCount == 0 && b.MatrixStackCount == 0
        && a.Transform.Equals(b.Transform) && a.Diffuse == b.Diffuse
        && a.LightInfo.Light1Vector == b.LightInfo.Light1Vector
        && a.LightInfo.Light2Vector == b.LightInfo.Light2Vector
        && a.LightInfo.Light1Color == b.LightInfo.Light1Color
        && a.LightInfo.Light2Color == b.LightInfo.Light2Color;
}
