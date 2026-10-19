using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

// Metadata eligibility for existing source playtests, never gameplay/bot qualification.
// Called on the existing background catalog worker, or an actual offline launch boundary.
internal static class EchoesOfflinePlaytestCatalog
{
    const string ArchiveSha1 = "b8b92af37dc772ecd8234fb1ce664be0493188f6";
    public static string[] QualifiedKeys(IEnumerable<MapDefinition> validatedDefinitions, string userRoot)
        => validatedDefinitions.Where(d => IsQualifiedInstalled(d, userRoot))
            .Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsQualifiedInstalled(MapDefinition definition, string userRoot)
    {
        if (!definition.LocalSourceOnly || definition.FormatVersion != 3
            || definition.BundlePath != null || definition.Capabilities is not { } caps
            || caps.MinPlayers != 1 || caps.MaxPlayers != 1 || caps.SupportedModes == null
            || caps.SupportedModes.Count != 1 || caps.SupportedModes[0] != "Battle"
            || definition.Spawns.Count == 0 || definition.SourcePath == null) return false;
        try
        {
            string folder = Path.Combine(Path.GetFullPath(userRoot), "local-echoes");
            string file = Path.GetFullPath(definition.SourcePath);
            if (Path.GetDirectoryName(file) != folder || !File.Exists(file)
                || Reparse(folder) || Reparse(Path.GetFullPath(userRoot)) || Reparse(file)) return false;
            foreach (var arena in EchoesArenaSources.All)
            {
                string stem = arena.Key switch
                {
                    "M01_SidehopperStation" => "sidehopper-station",
                    "M02_Spires" => "spires",
                    "M03_CrossfireChaos" => "crossfire-chaos",
                    "M04_Pipeline" => "pipeline",
                    "M05_SpiderComplex" => "spider-complex",
                    "M06_ShootingGallery" => "shooting-gallery",
                    _ => ""
                };
                Guid local = Stable("project-prime/local/echoes/v1/" + arena.Key + "/" + ArchiveSha1 + "/map");
                Guid visual = Stable("echoes/source-render/v1/" + local.ToString("N"));
                string name = arena.Name.ToUpperInvariant();
                if (Match(definition, file, "LOCAL ECHOES " + name, local, stem + ".json")
                    || Match(definition, file, "VISUAL ECHOES " + name, visual, "visual-" + stem + ".json")) return true;
                if (EchoesTunedCannons.CountFor(arena.Key) == 0) continue;
                Guid cannon = Stable("project-prime/echoes/tuned-cannons/v1/" + visual.ToString("N"));
                Guid routed = Stable("project-prime/echoes/routed-v1/" + cannon.ToString("N"));
                if (Match(definition, file, "CANNON ECHOES " + name, cannon, "cannon-" + stem + ".json")
                    || Match(definition, file, "ROUTED ECHOES " + name, routed, "routed-" + stem + ".json")) return true;
                if (arena.Key == "M02_Spires" && Match(definition, file, "STATIC ECHOES SPIRES",
                    Stable("echoes/static-source-visuals/spires/v1/" + routed.ToString("N")), "static-spires.json")) return true;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }
    }
    static bool Reparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    static bool Match(MapDefinition d, string path, string name, Guid id, string file)
        => d.Name == name && d.MapId == id && Path.GetFileName(path) == file;
    static Guid Stable(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
