using System;
using System.IO;

namespace MphRead.Mods.MapGen;

public static class MapStoragePaths
{
    public static string DataRoot
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("PRIME_DATA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ProjectPrime");
        }
    }

    public static string MapCache => Path.Combine(DataRoot, "map-cache");
    public static string InstalledMaps => Path.Combine(DataRoot, "maps", "installed");
    public static string Projects => Path.Combine(DataRoot, "maps", "projects");
}
