using System;

namespace MphRead.Sound;

/// <summary>Retain the engine only after its playback-device initialization succeeds.</summary>
internal static class MusicStartup
{
    internal static TDevice InitializeDevice<TDevice>(IDisposable engine, Func<TDevice> initializeDevice)
    {
        try
        {
            return initializeDevice();
        }
        catch
        {
            // The engine constructor completed, so it owns native state even
            // when playback-device creation fails. Preserve the startup error
            // for MusicPlayer's existing silent fallback if disposal also fails.
            try { engine.Dispose(); }
            catch (Exception ex)
            { Console.Error.WriteLine("[sound] failed music startup cleanup: " + ex.Message); }
            throw;
        }
    }
}
