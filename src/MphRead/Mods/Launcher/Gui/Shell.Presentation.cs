#if MPHREAD_SHELL && !ANDROID
namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    internal static void BeforePresent(RenderWindow window)
    {
        string? path = LauncherUiPerformance.TakeWarmupScreenshotPath();
        if (path is null) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            bool saved = Mods.ScreenCapture.SaveWindow(window.FramebufferSize.X, window.FramebufferSize.Y, path);
            if (saved)
                Mods.Render.FinalCompositeCapture.WriteEvidence(path, window.FramebufferSize.X,
                    window.FramebufferSize.Y, "Matched UI performance warmup: final composite before Present; excluded from measurement");
            System.Console.WriteLine(saved ? $"[ui-perf] warmup screenshot: {path}" : "[ui-perf] warmup screenshot capture failed");
        }
        catch (System.Exception exception)
        {
            System.Console.WriteLine($"[ui-perf] warmup screenshot failed: {exception.GetType().Name}");
        }
    }

    internal static void AfterPresent(RenderWindow window, bool presented)
    {
#if MPHREAD_RMLUI_POC
        _nativeSettings?.ObservePresentedFrame(presented && RmlUiPrototype.Visible && !RmlUiPrototype.Failed);
#endif
        if (!presented) return;
        if (UiVisible)
            LauncherUiRuntime.ObservePresented(
#if MPHREAD_RMLUI_POC
                RmlUiPrototype.Visible,
#else
                false,
#endif
                MphRead.Mods.Render.ModernGraphicsCompat.Active
                    ? MphRead.Mods.Render.ModernGraphicsCompat.DeviceIdentity.Backend.ToString() : "opengl");
        LauncherUiPerformance.Presented(window.FramebufferSize.X, window.FramebufferSize.Y,
#if MPHREAD_RMLUI_POC
            RmlUiPrototype.Visible
#else
            false
#endif
        );
        if (LauncherUiPerformance.ExitRequested) RequestQuit();
    }
}
#endif
