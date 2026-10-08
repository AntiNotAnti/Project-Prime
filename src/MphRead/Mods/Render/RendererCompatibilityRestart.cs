#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Render;

internal static class RendererCompatibilityRestart
{
    // A new process guarantees no cached native IDs, UI textures or abandoned
    // driver callbacks enter the GL context. This returns to the launcher; it
    // never reconstructs simulation state or silently rejoins a network match.
    internal static bool Start(string reason, LauncherUiMode? uiChoice = null)
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable is unavailable.");
            var start = CreateStartInfo(executable, uiChoice);
            Console.Error.WriteLine("[render] Device recovery failed; restarting the launcher with OpenGL: " + reason);
            using var process = Process.Start(start);
            return process != null;
        }
        catch (Exception ex)
        {
            string uiArgument = uiChoice switch
            {
                LauncherUiMode.RmlUi => " -ui rmlui", LauncherUiMode.Legacy => " -ui legacy",
                LauncherUiMode.Auto => " -ui auto", _ => ""
            };
            Console.Error.WriteLine("[render] Compatibility restart failed. Launch with -renderer opengl" + uiArgument
                + ", or install a previous/transitional package. " + ex.Message);
            return false;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, LauncherUiMode? uiChoice)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(RendererCompatibilityRestart).Assembly.Location);
        start.ArgumentList.Add("-launcher");
        start.ArgumentList.Add("-renderer");
        start.ArgumentList.Add("opengl");
        // This restart follows a retired native renderer. Its explicit GL choice
        // prevents another automatic modern-renderer restart; the UI retry keeps
        // the durable failure witness until that child's actual presentation.
        if (uiChoice is { } mode)
        {
            string name = mode switch
            {
                LauncherUiMode.Auto => "auto", LauncherUiMode.RmlUi => "rmlui", LauncherUiMode.Legacy => "legacy",
                _ => throw new ArgumentOutOfRangeException(nameof(uiChoice))
            };
            start.ArgumentList.Add("-ui");
            start.ArgumentList.Add(name);
        }
        return start;
    }
}
#endif
