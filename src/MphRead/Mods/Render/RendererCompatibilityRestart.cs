#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;

namespace MphRead.Mods.Render;

internal static class RendererCompatibilityRestart
{
    // A new process guarantees no cached native IDs, UI textures or abandoned
    // driver callbacks enter the GL context. This returns to the launcher; it
    // never reconstructs simulation state or silently rejoins a network match.
    internal static bool Start(string reason)
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable is unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(RendererCompatibilityRestart).Assembly.Location);
            start.ArgumentList.Add("-launcher");
            start.ArgumentList.Add("-renderer");
            start.ArgumentList.Add("opengl");
            Console.Error.WriteLine("[render] Device recovery failed; restarting the launcher with OpenGL: " + reason);
            using var process = Process.Start(start);
            return process != null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[render] Compatibility restart failed. Launch with -renderer opengl. " + ex.Message);
            return false;
        }
    }
}
#endif
