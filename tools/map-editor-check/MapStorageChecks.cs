using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static class MapStorageChecks
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-map-storage-" + Guid.NewGuid().ToString("N"));
        string previousMaps = CustomRooms.MapDirectory;
        string previousUserMaps = CustomRooms.UserMapDirectory;
        try
        {
            CustomRooms.MapDirectory = Path.Combine(root, "installed");
            CustomRooms.UserMapDirectory = Path.Combine(root, "user-maps");
            string imported = Path.Combine(CustomRooms.UserMapDirectory, "imported", "arena.json");
            Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
            MapTemplates.Create("STORAGE_CHECK", "basic-ffa").Definition.Save(imported);
            foreach (string excluded in new[] { ".autosave", "textures", "audio", "preview" })
            {
                string path = Path.Combine(CustomRooms.UserMapDirectory, excluded, "ignore.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "invalid JSON must not be read");
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(imported)!, ".q3-import.json"), "{}");
            var entries = new MapCatalog(CustomRooms.MapDirectory).Refresh();
            if (entries.Count != 1 || entries.Single().Path != imported
                || entries.Single().Definition?.Name != "STORAGE_CHECK")
                throw new Exception("Nested user imports must be discovered without asset or recovery JSON.");
            if (Directory.Exists(CustomRooms.MapDirectory))
                throw new Exception("Catalog must not write into installed resources.");
            Console.WriteLine("PASS: nested user map storage and metadata exclusion");
        }
        finally
        {
            CustomRooms.MapDirectory = previousMaps;
            CustomRooms.UserMapDirectory = previousUserMaps;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
