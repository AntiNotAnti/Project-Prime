#if MPHREAD_RMLUI && !MPHREAD_AVALONIA && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.Native;

/// <summary>Opt-in desktop entry preserves the process lease and paired Studio broker.</summary>
public static class NativeClientEntry
{
    public static bool TryRun()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()
            && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return false;
        if (!LauncherUiRuntime.UseNative) return false;
        if (!ClientInstanceGuard.TryAcquireForProcess(TimeSpan.FromSeconds(5))) return true;
        LauncherUiRuntime.ApplyAutomaticRendererRollback();
        LauncherUiRuntime.BeginNativeAttempt();
        MapGen.CustomRooms.DeferInitialRegistration = true;
        try
        {
            StudioIntegration.GameStudioIntegration.Start();
            bool ran = Gui.Shell.Run();
            if (!ran) { StudioIntegration.GameStudioIntegration.Stop(); MapGen.CustomRooms.RestoreDeferredRegistration(); }
            return ran;
        }
        catch (Exception error)
        {
            if (!Gui.RmlUiPrototype.Failed) LauncherUiRuntime.RecordNativeFailure(LauncherUiFailure.Initialization);
            StudioIntegration.GameStudioIntegration.Stop(); MapGen.CustomRooms.RestoreDeferredRegistration();
            DebugLog.Exception("native-launcher", error); return false;
        }
    }
}
#endif
