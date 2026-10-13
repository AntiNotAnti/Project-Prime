using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MphRead.Mods.Settings;

/// <summary>Native document and process lifecycle services supplied by platform heads.</summary>
internal static class SettingsArchivePlatform
{
    internal static Func<bool, Task<Stream?>>? PickDocument;
    internal static Action? RestartApplication;
    internal static void Restart()
    {
        if (RestartApplication != null) { RestartApplication(); return; }
#if !ANDROID
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate application executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Resolve beside the loaded application, independently of any
            // working-directory changes made since the original dotnet launch.
            string application = Diagnostics.RuntimeApplicationImage.AssemblyPath(
                typeof(SettingsArchivePlatform).Assembly);
            if (!File.Exists(application))
                throw new InvalidOperationException("Cannot locate application assembly for restart.");
            start.ArgumentList.Add(application);
        }
        start.ArgumentList.Add("-launcher");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not restart Project Prime.");
#if MPHREAD_SHELL
        if (Launcher.Gui.Shell.Active) { Launcher.Gui.Shell.RequestQuit(); return; }
#endif
        Environment.Exit(0);
#else
        throw new InvalidOperationException("Android restart service is unavailable. Close and reopen Project Prime; imported settings remain protected.");
#endif
    }
}
