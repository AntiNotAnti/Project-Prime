using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// One previous caster's exact preparation state, scoped to one shadow pass.
// Geometry, visibility and animated material/palette snapshots stay current.
internal sealed class ShadowCasterPreparationCache
{
    internal readonly record struct Reuse(bool Shared, bool Object);
    private readonly record struct Snapshot(RetainedBatchState Shared, Matrix4 Transform,
        Vector3 Light1Vector, Vector3 Light2Vector, Vector3 Light1Color, Vector3 Light2Color,
        int PaletteCount, int PaletteLength, object? PaletteSource, long PaletteVersion, float PaletteScale);
    private Snapshot _previous;
    private bool _valid;
    internal void Clear() { _valid = false; _previous = default; }
    internal Reuse Prepare(RenderItem item)
    {
        if (!item.RetainedRoomOwned || item.Type != RenderItemType.Mesh || item.ViewModel
            || item.WeightedSkinning || item.BillboardMode != BillboardMode.None
            || item.OverrideColor != null || item.PaletteOverride != null || item.PlayerOutlineColor != null
            || item.TexturedPlayerSkin || item.Cosmetics != default || item.CosmeticMaterial != default)
        { Clear(); return default; }
        var current = new Snapshot(new(item.CullingMode, item.Wireframe, item.RenderMode,
            RetainedMaterialDescriptor.From(item)), item.Transform, item.LightInfo.Light1Vector,
            item.LightInfo.Light2Vector, item.LightInfo.Light1Color, item.LightInfo.Light2Color,
            item.MatrixStackCount, item.MatrixStack.Length, item.MatrixPaletteSource, item.MatrixPaletteVersion, item.MatrixPaletteScale);
        bool shared = _valid && current.Shared == _previous.Shared;
        bool palette = current.PaletteCount == 0 && _previous.PaletteCount == 0
            || current.PaletteCount > 0 && current.PaletteCount <= 32 && current.PaletteCount == _previous.PaletteCount
            && current.PaletteLength >= current.PaletteCount * 16 && _previous.PaletteLength >= _previous.PaletteCount * 16
            && MatrixPaletteRevision.Enabled && current.PaletteVersion > 0
            && current.PaletteVersion == _previous.PaletteVersion && current.PaletteSource != null
            && ReferenceEquals(current.PaletteSource, _previous.PaletteSource)
            && current.PaletteScale == _previous.PaletteScale;
        bool objectState = shared && palette && current.Transform.Equals(_previous.Transform)
            && current.Light1Vector == _previous.Light1Vector && current.Light2Vector == _previous.Light2Vector
            && current.Light1Color == _previous.Light1Color && current.Light2Color == _previous.Light2Color;
        _previous = current; _valid = true;
        return new(shared, objectState);
    }
}
