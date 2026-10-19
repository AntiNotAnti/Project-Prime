using System;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// Bounds returned from the active native RmlUi document, clipped to the
/// framebuffer before the game's isolated Hunter preview uses its scissor.
/// One coordinate system (top-left framebuffer pixels), no DPI multiplication.
/// </summary>
public readonly record struct RmlUiPreviewViewport(
    float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;

    // The isolated model is composited after native chrome. Until the engine
    // accepts an independent clip rectangle, suppress a partially scrolled
    // model instead of drawing outside its document's overflow clip.
    public static bool IsContained(float x, float y, float width, float height,
        float clipX, float clipY, float clipWidth, float clipHeight)
        => float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height)
            && float.IsFinite(clipX) && float.IsFinite(clipY) && float.IsFinite(clipWidth) && float.IsFinite(clipHeight)
            && width > 0 && height > 0 && clipWidth > 0 && clipHeight > 0
            && x >= clipX && y >= clipY && x + width <= clipX + clipWidth && y + height <= clipY + clipHeight;

    public static bool TryCreate(float x, float y, float width, float height,
        int framebufferWidth, int framebufferHeight, out RmlUiPreviewViewport result)
    {
        result = default;
        if (!float.IsFinite(x) || !float.IsFinite(y)
            || !float.IsFinite(width) || !float.IsFinite(height)
            || framebufferWidth <= 0 || framebufferHeight <= 0
            || width <= 0 || height <= 0)
            return false;

        float left = Math.Clamp(x, 0f, framebufferWidth);
        float top = Math.Clamp(y, 0f, framebufferHeight);
        float right = Math.Clamp(x + width, 0f, framebufferWidth);
        float bottom = Math.Clamp(y + height, 0f, framebufferHeight);
        if (right - left < 64f || bottom - top < 64f)
            return false;
        result = new(left / framebufferWidth, top / framebufferHeight,
            right / framebufferWidth, bottom / framebufferHeight);
        return true;
    }
}
