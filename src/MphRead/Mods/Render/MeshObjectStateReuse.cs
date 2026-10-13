using System;
using OpenTK.Mathematics;
namespace MphRead.Mods.Render;

internal static class MeshObjectStateReuse
{
    // Only uniform state is reused. RenderItem still restores current color for
    // every mesh because a display list may leave its last vertex color active.
    internal static bool CanReuse(RenderItem a, RenderItem b)
        => a.Type == RenderItemType.Mesh && b.Type == RenderItemType.Mesh
        && DepthCoveragePreparation.CanUse(a, true, false) == DepthCoveragePreparation.CanUse(b, true, false)
        && a.ViewModel == b.ViewModel && a.WeightedSkinning == b.WeightedSkinning
        && a.BillboardMode == b.BillboardMode
        && a.Transform.Equals(b.Transform)
        && a.LightInfo.Light1Vector == b.LightInfo.Light1Vector
        && a.LightInfo.Light2Vector == b.LightInfo.Light2Vector
        && a.LightInfo.Light1Color == b.LightInfo.Light1Color
        && a.LightInfo.Light2Color == b.LightInfo.Light2Color
        && (a.MatrixStackCount == 0 && b.MatrixStackCount == 0
            || a.MatrixStackCount > 0 && a.MatrixStackCount <= 32
            && a.MatrixStackCount == b.MatrixStackCount
            && a.MatrixStack.Length >= a.MatrixStackCount * 16
            && b.MatrixStack.Length >= b.MatrixStackCount * 16
            && MatrixPaletteRevision.Enabled && a.MatrixPaletteVersion > 0
            && a.MatrixPaletteVersion == b.MatrixPaletteVersion && a.MatrixPaletteSource != null
            && ReferenceEquals(a.MatrixPaletteSource, b.MatrixPaletteSource)
            && a.MatrixPaletteScale == b.MatrixPaletteScale);

    internal static int Check()
    {
        int failures = 0;
        void Test(bool pass, string name) { Console.WriteLine($"[meshobjectcheck] {(pass ? "PASS" : "FAIL")}: {name}"); if (!pass) failures++; }
        var a = new RenderItem(); var b = new RenderItem();
        Test(CanReuse(a, b), "identical mesh uniform state reuses");
        b.ViewModel = true; Test(!CanReuse(a, b), "viewmodel transition invalidates projection reuse"); b.ViewModel = false;
        b.Transform = Matrix4.Identity; Test(!CanReuse(a, b), "current transform change invalidates reuse"); b.Transform = default;
        b.WeightedSkinning = true; Test(!CanReuse(a, b), "weighted skinning transition invalidates reuse"); b.WeightedSkinning = false;
        b.BillboardMode = BillboardMode.Sphere; Test(!CanReuse(a, b), "billboard transition invalidates reuse"); b.BillboardMode = BillboardMode.None;
        a.MatrixStackCount = b.MatrixStackCount = 1; Test(!CanReuse(a, b), "unknown mutable palettes never reuse");
        a.MatrixPaletteSource = b.MatrixPaletteSource = new object(); a.MatrixPaletteVersion = b.MatrixPaletteVersion = 1;
        Test(CanReuse(a, b) == MatrixPaletteRevision.Enabled, "known exact palette revision follows policy");
        b.MatrixPaletteVersion++; Test(!CanReuse(a, b), "changed palette revision invalidates reuse");
        return failures;
    }
}
