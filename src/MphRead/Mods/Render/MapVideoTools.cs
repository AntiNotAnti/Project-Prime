using System;
using System.IO;

namespace MphRead.Mods.Render;

internal static class MapVideoTools
{
    // Portable handoffs carry their decoder beside the client. Ordinary installs
    // use the same platform PATH lookup as Replay Studio.
    internal static string Resolve(string name)
    {
        string executable = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        string bundled = Path.Combine(AppContext.BaseDirectory, "video", executable);
        return File.Exists(bundled) ? bundled : executable;
    }
}
