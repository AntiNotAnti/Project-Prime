using System;
using System.IO;

namespace MphRead.Mods.Settings;

internal static class SettingsArchiveCommand
{
    internal static int Run(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals("-settingsarchive", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return 1;
        try
        {
            if (index + 2 >= args.Length) throw new ArgumentException("Usage: -settingsarchive export|import|validate ARCHIVE.zip");
            string action = args[index + 1], path = args[index + 2];
            switch (action)
            {
                case "export": SettingsArchive.ExportFile(Launcher.LauncherPrefs.Directory, path, Program.Version.ToString()); break;
                case "import":
                    using (var stream = File.OpenRead(path)) SettingsArchive.Import(Launcher.LauncherPrefs.Directory, stream);
                    break;
                case "validate":
                    using (var stream = File.OpenRead(path)) SettingsArchive.Read(stream);
                    break;
                default: throw new ArgumentException("Choose export, import or validate.");
            }
            Console.WriteLine(action == "import" ? "Settings imported successfully. Start Project Prime to load them." : "Settings archive " + action + " succeeded.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Settings archive failed: " + ex.Message); return 1; }
    }
}
