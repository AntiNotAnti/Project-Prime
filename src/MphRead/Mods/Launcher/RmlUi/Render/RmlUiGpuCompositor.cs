#if MPHREAD_RMLUI_POC && !MPHREAD_SERVER
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.RmlUi.Render;

// Owns no graphics device, queue, window, or swapchain. The engine calls this
// terminal overlay after scene/HUD composition and before its normal Present.
internal static class RmlUiGpuCompositor
{
    private static readonly RmlUiDrawListReader Reader = new();
    internal static (long Requests, long Captured, long Reused) CaptureMetrics =>
        (Reader.CaptureRequests, Reader.CapturedFrames, Reader.ReusedFrames);
    internal static void DrawNativeFrame(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        long capture=LauncherUiPerformance.Start();
        var frame=Reader.Capture();
        LauncherUiPerformance.RecordDrawListCapture(Reader.CaptureRequests,Reader.CapturedFrames,Reader.ReusedFrames);
        LauncherUiPerformance.RecordPhase("drawListCapture",capture);
        long submit=LauncherUiPerformance.Start();
        ModernGraphicsCompat.DrawRmlUi(frame,width,height);
        LauncherUiPerformance.RecordPhase("gpuCommandAuthoring",submit);
    }
    internal static void ReleaseNativeFrame()
    {
        Reader.Forget();
        ModernGraphicsCompat.ReleaseRmlUiFrames();
    }
}
#endif
