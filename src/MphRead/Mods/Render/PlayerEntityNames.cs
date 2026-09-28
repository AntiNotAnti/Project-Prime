using System;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using MphRead.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private Vector2 DrawPlayerName(float x, float y, Align align, int palette, string name,
        ColorRgba? color = null, float alpha = 1, float fontSpacing = -1, float scale = 1,
        float maxWidth = 90, bool useHudTextScale = true)
    {
        float profileScale = HudProfiles.Runtime.Mode == HudMode.Custom
            ? Math.Max(0.01f, HudProfiles.Runtime.TextScale) : 1;
        if (!useHudTextScale) scale /= profileScale;
        var fit = PlayerNameLayout.Fit(name, maxWidth / profileScale, scale, scale * 0.75f);
        return DrawText2D(x, y, align, palette, fit.Text, color, alpha, fontSpacing, scale: fit.Scale);
    }
}
