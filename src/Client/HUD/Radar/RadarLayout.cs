using System;

namespace MphRead.Hud.Radar;

public readonly record struct RadarLayout(float Left, float Top, float Width, float Height,
    float CenterX, float CenterY, float Radius);

public static class RadarLayoutCalculator
{
    public const float BaseDiameter = 52;
    public const float Margin = 9;
    public const float HudClearance = 3;

    public static RadarLayout Calculate(RadarAnchor anchor, float scale,
        float offsetX, float offsetY, float aspectFix)
    {
        if (!float.IsFinite(scale)) scale = 1;
        if (!float.IsFinite(offsetX)) offsetX = 0;
        if (!float.IsFinite(offsetY)) offsetY = 0;
        if (!float.IsFinite(aspectFix) || aspectFix <= 0) aspectFix = 1;
        scale = Math.Clamp(scale, RadarSettings.MinimumScale, RadarSettings.MaximumScale);

        float height = BaseDiameter * scale;
        float width = height * aspectFix;
        float marginX = Margin * aspectFix;
        float left = anchor switch
        {
            RadarAnchor.TopLeft or RadarAnchor.BottomLeft => marginX,
            RadarAnchor.Custom => 128 - width / 2,
            _ => 256 - marginX - width
        };
        float top = anchor switch
        {
            RadarAnchor.BottomLeft or RadarAnchor.BottomRight => 192 - Margin - height,
            RadarAnchor.Custom => 96 - height / 2,
            _ => Margin
        };
        left += offsetX * aspectFix;
        top += offsetY;
        left = Math.Clamp(left, 0, Math.Max(0, 256 - width));
        top = Math.Clamp(top, 0, Math.Max(0, 192 - height));
        return new RadarLayout(left, top, width, height,
            left + width / 2, top + height / 2, MathF.Min(width / aspectFix, height) / 2 - 3);
    }

    public static int FitVerticalMeterBelow(RadarLayout obstacle, float meterBottom, int originalLength)
    {
        if (originalLength <= 8)
        {
            return originalLength;
        }

        float availableSpan = meterBottom - (obstacle.Top + obstacle.Height + HudClearance);
        int availableTiles = Math.Max(1, (int)MathF.Floor(availableSpan / 8) + 1);
        return Math.Min(originalLength, availableTiles * 8);
    }
}
