using System;
using System.Collections.Generic;

namespace MphRead.Mods.MapGen
{
    // Explicit map preparation capability shared by platform composition and Tools.
    public static class MapPreparation
    {
        public static int GenerateAll(bool force = false, bool verbose = true)
        {
            int count = 0;
            foreach (MapDefinition def in CustomRooms.Definitions)
            {
                if (force || CustomRooms.NeedsGenerating(def))
                {
                    MapPacker.Generate(def, CustomRooms.ArchiveDirectory(def), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose);
                    count++;
                }
            }
            return count;
        }

        public static IReadOnlyList<string> GenerateMissing()
        {
            var failures = new List<string>();
            IReadOnlyList<MapDefinition> definitions;
            try
            {
                definitions = CustomRooms.Definitions;
            }
            catch
            {
                return failures;
            }
            foreach (MapDefinition def in definitions)
            {
                try
                {
                    if (!CustomRooms.NeedsGenerating(def))
                    {
                        continue;
                    }
                    Console.WriteLine($"[mapgen] building {def.Name}");
                    MapPacker.Generate(def, CustomRooms.ArchiveDirectory(def), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[mapgen] {def.Name} could not be built: {ex.Message}");
                    failures.Add($"{def.Name}: {ex.Message}");
                }
            }
            return failures;
        }
    }
}
