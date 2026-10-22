using System;

namespace MphRead.Mods.Render.Hud;

/// <summary>Pixel bounds shared by HUD sprite drawing and absolute input.</summary>
public readonly record struct HudSpriteBounds(float Left, float Top, float Width, float Height)
{
    public bool Contains(float x, float y) => Single.IsFinite(x) && Single.IsFinite(y)
        && Width > 0 && Height > 0 && x >= Left && y >= Top
        && x < Left + Width && y < Top + Height;

    public static HudSpriteBounds Resolve(float x, float y, float width, float height,
        bool center, int mode, float scale, float viewWidth, float viewHeight,
        float profileScale = 1, float profileX = 0, float profileY = 0,
        float hudScale = 1, float iconScale = 1)
    {
        x = x * profileScale + profileX / 256;
        y = y * profileScale + profileY / 192;
        x = .5f + (x - .5f) * hudScale;
        y = .5f + (y - .5f) * hudScale;
        float factorX = mode == 2 ? viewWidth / 256 : mode == 1 ? viewHeight / 192 : viewWidth / 256;
        float factorY = mode == 2 ? viewHeight / 192 : factorX;
        float totalScale = scale * profileScale * hudScale * iconScale;
        width *= factorX * totalScale;
        height *= factorY * totalScale;
        return new(x * viewWidth - (center ? width / 2 : 0),
            y * viewHeight - (center ? height / 2 : 0), width, height);
    }
}
