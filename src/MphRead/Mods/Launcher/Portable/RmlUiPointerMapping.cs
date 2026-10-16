using System;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// GLFW mouse positions are in window (screen) coordinates. RmlUi is
    /// configured using framebuffer dimensions. Convert exactly once using
    /// the framebuffer / GLFW-window ratio, not the CSS dp density and not
    /// RenderWindow.PointerPixels followed by another multiplier.
    ///
    /// Keep negative and out-of-window positions: captured drags need to
    /// release outside the viewport rather than snapping onto a button.
    /// </summary>
    public static class RmlUiPointerMapping
    {
        public static (int X, int Y) FromWindow(
            double windowX, double windowY,
            float framebufferPerWindowX, float framebufferPerWindowY)
        {
            if (!Double.IsFinite(windowX) || !Double.IsFinite(windowY))
                return (-1, -1);
            double scaleX = Single.IsFinite(framebufferPerWindowX)
                && framebufferPerWindowX > 0f ? framebufferPerWindowX : 1f;
            double scaleY = Single.IsFinite(framebufferPerWindowY)
                && framebufferPerWindowY > 0f ? framebufferPerWindowY : 1f;
            return (SaturatingRound(windowX * scaleX),
                SaturatingRound(windowY * scaleY));
        }

        private static int SaturatingRound(double value)
        {
            if (value <= Int32.MinValue) return Int32.MinValue;
            if (value >= Int32.MaxValue) return Int32.MaxValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
