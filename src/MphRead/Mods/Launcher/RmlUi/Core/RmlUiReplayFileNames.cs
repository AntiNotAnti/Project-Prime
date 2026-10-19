using System;
namespace MphRead.Mods.Launcher.RmlUi.Core;
internal static class RmlUiReplayFileNames
{
    internal const string PortableBundleName="replay.ppreplay.zip";
    internal static bool IsPortableBundle(string path)=>path.EndsWith(".ppreplay.zip",StringComparison.OrdinalIgnoreCase)
        ||path.EndsWith(".ppreplay",StringComparison.OrdinalIgnoreCase);
}
