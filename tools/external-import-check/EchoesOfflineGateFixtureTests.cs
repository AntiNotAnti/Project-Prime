using System;
using MphRead.Mods.Import;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.MapGen;

internal static class EchoesOfflineGateFixtureTests
{
    public static void Run()
    {
        var source = new MapDefinition
        {
            Name = "LOCAL ECHOES SPIRES",
            LocalSourceOnly = true,
            Capabilities = new MapCapabilities
            {
                SupportedModes = new System.Collections.Generic.List<string> { "Battle" },
                MinPlayers = 1, MaxPlayers = 1
            }
        };
        Check(!MapModeCapabilities.AllowsLocalSource(source, offlinePreview: false,
            out string why) && why.Contains("cannot be hosted", StringComparison.Ordinal),
            "default network and dedicated-server path refuses all source-derived maps");
        Check(MapModeCapabilities.AllowsLocalSource(source, offlinePreview: true, out why) &&
            why == "", "explicit offline preview may pass the local-source gate");
        var normal = new MapDefinition { Name = "MP3 PROVING GROUND", LocalSourceOnly = false };
        Check(MapModeCapabilities.AllowsLocalSource(normal, offlinePreview: false, out why),
            "original/normal custom maps stay network eligible");
        Check(MapModeCapabilities.AllowsLocalSource(null, offlinePreview: false, out why),
            "vanilla room metadata unaffected");
        var visual = MapProjectSerializer.Clone(source);
        visual.Name = "VISUAL ECHOES SPIRES";
        Check(!MapModeCapabilities.AllowsLocalSource(visual, false, out _),
            "visual-tier source map cannot be hosted");
        visual.Name = "CANNON ECHOES SPIRES";
        Check(!MapModeCapabilities.AllowsLocalSource(visual, false, out _),
            "tuned-cannon source map cannot be hosted");
    }

    private static void Check(bool yes, string why)
    {
        if (!yes) throw new InvalidOperationException("Echoes network guard: " + why);
    }
}
