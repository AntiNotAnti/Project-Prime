#if MPHREAD_SHELL && !ANDROID
namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    internal static void BeforePresent(RenderWindow window)
    {
#if MPHREAD_AVALONIA
        if (LauncherUiPerformance.Enabled
#if MPHREAD_RMLUI_POC
            && !RmlUiPrototype.Visible
#endif
            ) _front?.BeginPerformanceHome();
#endif
        if (LauncherUiPerformance.Enabled && _performanceTrace)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastPerformanceTrace == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(_lastPerformanceTrace, now).TotalSeconds >= 5)
            {
                _lastPerformanceTrace = now;
                System.Console.WriteLine($"[ui-perf] present heartbeat home={PerformanceHomeReadyState(window)} visible={UiVisible} scene={window.HasScene}");
            }
        }
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
        if (!presented)
        {
            if (LauncherUiPerformance.Enabled && _performanceTrace && !_reportedFailedPerformancePresent && _lastPerformanceHomeReady == true)
            {
                _reportedFailedPerformancePresent = true;
                System.Console.WriteLine("[ui-perf] surface presentation failed after verified Home");
            }
            return;
        }
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
            RmlUiPrototype.Visible,
#else
            false,
#endif
            LauncherUiPerformance.Enabled && PerformanceHomeReady(window)
        );
        if (LauncherUiPerformance.ExitRequested) RequestQuit();
    }

    private static readonly bool _performanceTrace = System.Environment.GetEnvironmentVariable("PROJECT_PRIME_UI_PERF_TRACE") == "1";
    private static long _lastPerformanceTrace;
    private static bool _reportedFailedPerformancePresent;
    private static bool? _lastPerformanceHomeReady;
    private static bool PerformanceHomeReady(RenderWindow window)
    {
        bool ready = PerformanceHomeReadyState(window);
        if (_lastPerformanceHomeReady != ready)
        {
            _lastPerformanceHomeReady = ready;
            string state = $"visible={UiVisible} scene={window.HasScene}";
#if MPHREAD_RMLUI_POC
            var pages = RmlUiPrototype.Pages;
            state += $" native={RmlUiPrototype.Visible} failed={RmlUiPrototype.Failed} setup={_nativeSetup != null}"
                + $" page={pages?.Manager.PageKey} modals={pages?.Manager.ModalCount}"
                + $" inputIsPage={pages != null && RmlUiPrototype.Runtime.Active && RmlUiPrototype.Runtime.CurrentInputDocument == pages.Manager.Page}";
#endif
#if MPHREAD_AVALONIA
            state += $" legacy={UiSurface.Current?.Visible == true} legacyHome={_front?.PerformanceHomeReady == true}";
#endif
            System.Console.WriteLine($"[ui-perf] Home eligibility {ready}: {state}");
        }
        return ready;
    }

    private static bool PerformanceHomeReadyState(RenderWindow window)
    {
        if (!UiVisible || window.HasScene) return false;
#if MPHREAD_RMLUI_POC
        if (RmlUiPrototype.Visible)
            return !RmlUiPrototype.Failed && _nativeSetup == null
                && RmlUiPrototype.Pages?.Manager.PageKey == "home"
                && RmlUiPrototype.Pages.Manager.ModalCount == 0
                && RmlUiPrototype.Runtime.CurrentInputDocument == RmlUiPrototype.Pages.Manager.Page;
#endif
#if MPHREAD_AVALONIA
        return UiSurface.Current?.Visible == true && _front?.PerformanceHomeReady == true;
#else
        return false;
#endif
    }
}
#endif
