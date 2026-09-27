using System;

namespace MphRead.Sound;

/// <summary>Release native audio before the CLR starts shutting down callbacks.</summary>
internal static class AudioLifetime
{
    // Registered only when music initializes: exiting a server or a silent
    // launcher must not open an audio device just to close it.
    internal static Action? StopMusic { get; set; }

    internal static void Shutdown()
    {
        try
        {
            Mods.DebugLog.Line("shutdown", "stopping sound effects");
            Sfx.ShutDown();
            Mods.DebugLog.Line("shutdown", "waiting for OpenAL device cleanup");
            if (!Sfx.ShutdownCompletion.Wait(TimeSpan.FromSeconds(10)))
                Console.Error.WriteLine("[shutdown] OpenAL device cleanup exceeded 10 seconds.");
        }
        catch (Exception ex) { Console.Error.WriteLine($"[shutdown] sound effects: {ex}"); }
        try
        {
            var stop = StopMusic;
            StopMusic = null;
            Mods.DebugLog.Line("shutdown", "stopping music engine");
            stop?.Invoke();
            Mods.DebugLog.Line("shutdown", "audio cleanup complete");
        }
        catch (Exception ex) { Console.Error.WriteLine($"[shutdown] music: {ex}"); }
    }
}
