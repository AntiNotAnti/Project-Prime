using System;

namespace MphRead.Hud.Radar;

public readonly record struct RadarLayout(float Left, float Top, float Width, float Height,
    float CenterX, float CenterY, float Radius);

public readonly record struct RadarAvoidancePlacement(float Left, float Top, float Scale);

public static class RadarLayoutCalculator
{
    public const float BaseDiameter = 52;
    public const float Margin = 9;
    public const float HudClearance = 3;
    public const int MinimumVerticalMeterLength = 40;
    public const float MinimumHudObjectScale = .25f;

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
        => FitVerticalMeterBelow(obstacle.Top + obstacle.Height, meterBottom, originalLength);

    public static int FitVerticalMeterBelow(float obstacleBottom, float meterBottom, int originalLength)
    {
        if (originalLength <= 8)
        {
            return originalLength;
        }

        float availableSpan = meterBottom - (obstacleBottom + HudClearance);
        int availableTiles = Math.Max(1, (int)MathF.Floor(availableSpan / 8) + 1);
        return Math.Min(originalLength, availableTiles * 8);
    }

    public static float HudObjectBottomLimit(float meterBottom, int originalMeterLength)
    {
        int reservedLength = Math.Min(Math.Max(8, originalMeterLength), MinimumVerticalMeterLength);
        float reservedTop = meterBottom - (reservedLength - 8);
        return reservedTop - HudClearance;
    }

    /// <summary>
    /// Keeps a top-level authored HUD sprite out of the enhanced radar without
    /// changing its source texture or crop. The sprite retains its authored
    /// placement when there is no overlap; otherwise it moves immediately
    /// below the radar and shrinks only when the space before its companion
    /// meter is too small.
    /// </summary>
    public static RadarAvoidancePlacement FitHudObjectBelow(RadarLayout obstacle,
        float left, float top, float width, float height, float lowerEdge)
    {
        if (!float.IsFinite(left)) left = 0;
        if (!float.IsFinite(top)) top = 0;
        if (!float.IsFinite(width) || width <= 0
            || !float.IsFinite(height) || height <= 0)
        {
            return new RadarAvoidancePlacement(left, top, 1);
        }

        float obstacleRight = obstacle.Left + obstacle.Width;
        float obstacleBottom = obstacle.Top + obstacle.Height;
        bool overlaps = left < obstacleRight && left + width > obstacle.Left
            && top < obstacleBottom && top + height > obstacle.Top;
        if (!overlaps)
        {
            return new RadarAvoidancePlacement(left, top, 1);
        }

        float placedTop = obstacleBottom + HudClearance;
        if (!float.IsFinite(lowerEdge))
        {
            lowerEdge = placedTop;
        }
        float availableHeight = Math.Max(0, lowerEdge - placedTop);
        float scale = Math.Clamp(availableHeight / height, MinimumHudObjectScale, 1);
        float placedLeft = Math.Clamp(left, 0, Math.Max(0, 256 - width * scale));
        return new RadarAvoidancePlacement(placedLeft, placedTop, scale);
    }
}
