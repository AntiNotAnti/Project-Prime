using System;
using System.Threading;

namespace MphRead.Sound;

/// <summary>Release native audio before the CLR starts shutting down callbacks.</summary>
internal static class AudioLifetime
{
    // Registered only when music initializes: exiting a server or a silent
    // launcher must not open an audio device just to close it.
    internal static Action? StopMusic { get; set; }
    internal static Action? RequestMusicShutdown { get; set; }
    private static int _shutdownRequested;

    /// <summary>
    /// Cancel work that can be cancelled as soon as the user commits to exit.
    /// Native devices stay alive until scene/final cleanup so an in-flight draw
    /// cannot race a disposed audio backend.
    /// </summary>
    internal static void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0) return;
        try { RequestMusicShutdown?.Invoke(); }
        catch (Exception ex) { Console.Error.WriteLine($"[shutdown] music cancel: {ex}"); }
        Mods.LifecycleTiming.Shutdown("audio shutdown requested");
    }

    internal static void Shutdown()
    {
        BeginShutdown();
        try
        {
            Mods.DebugLog.Line("shutdown", "stopping sound effects");
            Sfx.ShutDown();
            Mods.DebugLog.Line("shutdown", "waiting briefly for OpenAL device cleanup");
            if (!Sfx.ShutdownCompletion.Wait(TimeSpan.FromSeconds(2)))
                Console.Error.WriteLine("[shutdown] OpenAL cleanup exceeded 2 seconds; process exit will reclaim it.");
            Mods.LifecycleTiming.Shutdown("sound effects cleanup complete");
        }
        catch (Exception ex) { Console.Error.WriteLine($"[shutdown] sound effects: {ex}"); }
        try
        {
            var stop = StopMusic;
            StopMusic = null;
            RequestMusicShutdown = null;
            Mods.DebugLog.Line("shutdown", "stopping music engine");
            stop?.Invoke();
            Mods.DebugLog.Line("shutdown", "audio cleanup complete");
            Mods.LifecycleTiming.Shutdown("music cleanup complete");
        }
        catch (Exception ex) { Console.Error.WriteLine($"[shutdown] music: {ex}"); }
    }
}
