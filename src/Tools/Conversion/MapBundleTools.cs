using System;
using System.IO;
using System.IO.Compression;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapGen
{
    public static class MapBundleTools
    {
        private const string LevelDirectory = "maps/";

        /// <summary>
        /// Cook a map folder into a bundle: the recipe as it stands, the level
        /// with its unread lumps emptied, and the baked texture pack if the
        /// map names one.
        ///
        /// The recipe inside is rewritten to point at what is beside it in the
        /// bundle rather than at the pk3 it was made from, so a bundle names
        /// nothing that is not in it.
        /// </summary>
        public static string Cook(MapDefinition definition, string recipePath, string? outputPath,
            bool verbose = true)
        {
            MapImport? import = definition.Import;
            string? level = null;
            string? texturePath = null;
            byte[]? trimmed = null;
            string? mapName = null;
            if (import != null)
            {
                if (import.Source.Length == 0)
                {
                    throw new ProgramException($"{definition.Name} has an empty level source.");
                }
                level = import.Resolve();
                if (level == null)
                {
                    throw new ProgramException($"{definition.Name}: its source level {import.Source} "
                        + "is not here, so there is nothing to cook.");
                }
                mapName = import.MapName ?? Path.GetFileNameWithoutExtension(level);
                trimmed = Q3Bsp.Trim(Q3Bsp.ReadLevel(level, import.MapName));
                texturePath = import.ResolveTextures();
                if (texturePath == null && !String.IsNullOrEmpty(import.Textures))
                {
                    // Bake it now, because a bundle cannot be baked from later.
                    // The pack is derived from the level's own art, so it is not
                    // in git and a fresh clone does not have one -- the game bakes
                    // it the first time the map is played. A bundle carries the
                    // level trimmed to the lumps the importer reads, and the art
                    // is in none of them: it lives in the .pk3 beside the recipe,
                    // which the bundle exists not to hand out. So a bundle cooked
                    // where the pack was not already sitting there -- a CI runner,
                    // every time -- shipped a map with no textures, which is a map
                    // with no materials, which crashed the moment it was picked.
                    texturePath = Q3Import.BakeTextures(
                        Q3Bsp.Load(level, import.MapName), import, verbose);
                    if (texturePath == null)
                    {
                        throw new ProgramException($"{definition.Name}: its textures "
                            + $"({import.Textures}) are not beside its recipe and could not be baked "
                            + "from " + Path.GetFileName(level) + ". A bundle without them is a room "
                            + "with no materials, so this is a failure and not a bundle.");
                    }
                }
            }
            else if (definition.Brushes.Count == 0)
            {
                throw new ProgramException($"{definition.Name} has no imported level or brushes to bundle.");
            }
            // The top of maps/ by default, not beside the recipe. The working
            // copy of a map is a folder with somebody's .pk3 in it; the bundle
            // is the one file that goes out, and it goes where every platform
            // already looks -- including Android, whose asset glob does not
            // recurse into folders.
            string path = outputPath ?? Path.Combine(CustomRooms.MapDirectory,
                Path.GetFileNameWithoutExtension(recipePath) + MapBundle.Extension);
            string recipeName = Path.GetFileName(recipePath);
            string textureName = texturePath == null ? "" : Path.GetFileName(texturePath);
            // A copy, because what goes in the bundle names what is in the
            // bundle: the source is the level beside it, not the pk3 it came
            // out of, which the player will not have.
            MapDefinition inside = MapDefinition.Load(recipePath);
            if (inside.Import != null)
            {
                inside.Import.Source = $"{LevelDirectory}{mapName}.bsp";
                inside.Import.MapName = mapName;
                inside.Import.Textures = textureName;
            }
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(archive, recipeName, System.Text.Encoding.UTF8.GetBytes(inside.Serialize()));
                if (trimmed != null)
                {
                    Write(archive, $"{LevelDirectory}{mapName}.bsp", trimmed);
                }
                if (texturePath != null)
                {
                    Write(archive, textureName, File.ReadAllBytes(texturePath));
                }
            }
            File.Move(temporary, path, overwrite: true);
            if (verbose)
            {
                long before = (level == null ? 0 : new FileInfo(level).Length)
                    + (texturePath == null ? 0 : new FileInfo(texturePath).Length);
                Console.WriteLine($"[mapbundle] {definition.Name} -> {path} "
                    + $"({new FileInfo(path).Length / 1024} KiB, from {before / 1024} KiB)");
            }
            return path;
        }

        private static void Write(ZipArchive archive, string name, byte[] bytes)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
            using Stream stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
        }

    }
}
