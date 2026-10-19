#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using MphRead.Mods.StudioIntegration;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    internal static StudioApplicationInstance? StudioApplication { get; private set; }

    internal static void RunPrimeStudio(string[] args, bool mapPlaceholder = false)
    {
        // Studio has its own process, native context and replay owner. The game
        // passes logical document paths through the authenticated local broker.
        string[] request = mapPlaceholder ? new[] { "--studio" }
            : args.Select(value => value is "-studio" or "-primestudio" or "--primestudio"
                ? "--studio" : value).ToArray();
        using var instance = StudioApplicationInstance.AcquireStudioInstance(request);
        if (!instance.IsPrimary)
        {
            if (!instance.Forwarded)
            {
                Console.Error.WriteLine("[studio] " + (instance.Error ?? "Studio could not be opened."));
                Environment.ExitCode = 1;
            }
            return;
        }
        if (!ClientInstanceGuard.TryAcquireForProcess(TimeSpan.FromSeconds(5), studio: true)) return;
        Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE", "1");
        MapGen.CustomRooms.DeferInitialRegistration = true;
        StudioWindow = true;
        StudioApplication = instance;
        WindowMode.ForceStartup(WindowStartMode.Windowed);
        try { if (!Run()) Environment.ExitCode = 1; }
        finally { StudioApplication = null; StudioWindow = false; }
    }
}
#endif
