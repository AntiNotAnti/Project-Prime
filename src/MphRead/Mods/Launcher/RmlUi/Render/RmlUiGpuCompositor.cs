#if MPHREAD_RMLUI_POC && !MPHREAD_SERVER
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.RmlUi.Render;

// Owns no graphics device, queue, window, or swapchain. The engine calls this
// terminal overlay after scene/HUD composition and before its normal Present.
internal static class RmlUiGpuCompositor
{
    private static readonly RmlUiDrawListReader Reader = new();
    internal static void DrawNativeFrame(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        ModernGraphicsCompat.DrawRmlUi(Reader.Capture(), width, height);
    }
    internal static void ReleaseNativeFrame()
    {
        Reader.Forget();
        ModernGraphicsCompat.ReleaseRmlUiFrames();
    }
}
#endif
