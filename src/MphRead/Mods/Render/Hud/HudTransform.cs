using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

/// <summary>Uniform logical units with anchors resolved against the output safe area.</summary>
public readonly struct HudTransform
{
    public readonly Vector2 OutputSize;
    public readonly float UnitScale;
    public readonly float Margin;
    public HudTransform(float width, float height, float safeArea = 0)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        OutputSize = new(width, height);
        UnitScale = MathF.Min(width / 1920f, height / 1080f);
        Margin = HudProfile.Clamp(safeArea, 0, .2f, 0);
    }
    public Vector2 Resolve(HudAnchor anchor, Vector2 offset)
    {
        // Anchors are contiguous. Avoid reflection-backed enum metadata in the draw path.
        int a = (uint)anchor <= (uint)HudAnchor.BottomRight ? (int)anchor : 0;
        Vector2 low = OutputSize * Margin;
        Vector2 span = OutputSize - 2 * low;
        return low + new Vector2(span.X * (a % 3) / 2, span.Y * (a / 3) / 2) + offset * UnitScale;
    }
    public Vector2 OffsetFor(HudAnchor anchor, Vector2 point) => (point - Resolve(anchor, Vector2.Zero)) / UnitScale;
}
