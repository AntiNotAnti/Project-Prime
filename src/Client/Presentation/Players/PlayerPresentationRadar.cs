using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Hud.Radar;
using MphRead.Text;

namespace MphRead.Entities;

public partial class PlayerPresentation
{
    private readonly RadarFrame _radarFrame = new();

    private void DrawEnhancedRadar()
    {
        const float centerX = 29, centerY = 48, radius = 21;
        DrawText2D(centerX, centerY - radius - 8, Align.Center, 0,
            RadarSettings.Orientation == RadarOrientation.North ? "N" : "RADAR", scale: .6f);
        DrawText2D(centerX, centerY, Align.Center, 0, "+", scale: .6f);
        DrawText2D(centerX - radius, centerY, Align.Center, 0, "[", scale: .6f);
        DrawText2D(centerX + radius, centerY, Align.Center, 0, "]", scale: .6f);
        foreach (RadarContact contact in _radarFrame.Contacts)
        {
            RadarPoint point = RadarWidget.Project(contact, _radarFrame.Origin, _radarFrame.Facing, RadarSettings.Orientation);
            float x = centerX + point.RelativePosition.X * radius;
            float y = centerY + point.RelativePosition.Y * radius;
            byte alpha = (byte)(255 * contact.Visibility * (contact.AgeTicks > 0 ? .5f : 1));
            ColorRgba color = contact.Type == RadarContactType.Enemy ? new(255, 90, 90, alpha)
                : contact.Type == RadarContactType.Teammate ? new(100, 200, 255, alpha)
                : new(255, 220, 90, alpha);
            DrawText2D(x, y, Align.Center, 0, RadarWidget.Symbol(contact), color, scale: .65f);
            if (point.Elevation != RadarElevation.Same)
                DrawText2D(x + 4, y - 3, Align.Center, 0, point.Elevation == RadarElevation.Above ? "^" : "v", color, scale: .5f);
            if (point.Clamped)
                DrawText2D(x, y + 4, Align.Center, 0, ".", color, scale: .5f);
        }
    }
}
