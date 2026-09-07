using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MphRead.Mods.MapGen
{
    /// <summary>
    /// One file that is a whole custom map: the recipe, the level it converts
    /// and the textures baked from it.
    ///
    /// A map used to be a folder of three files, one of them somebody else's
    /// `.pk3` -- 2.7 MB of Quake level of which this importer reads 1.1, the
    /// rest being lightmaps, light volumes and a BSP tree nothing here opens.
    /// A bundle is those three cooked into a zip with the level trimmed to the
    /// lumps <see cref="Q3Bsp.UsedLumps"/> names: de_dust2 comes out at 386 KB
    /// against the 2.8 MB the folder shipped, and it is one file, which is
    /// what makes a map something you can hand somebody. Handing them out is
    /// the point: the plan is for a server to offer its maps to players who do
    /// not have them, and a downloader wants one file with everything in it,
    /// not a folder to reassemble.
    ///
    /// It is a zip because <see cref="Q3Bsp.Load"/> already opens one and
    /// finds the level inside it by name -- that is how a .pk3 is read -- so
    /// the level half of this format cost nothing to support.
    ///
    /// What a bundle does *not* settle is whether a level may be handed out at
    /// all. Cooking somebody's level into a smaller container leaves it their
    /// level; that judgement belongs to whoever publishes the bundle.
    /// </summary>
    public static class MapBundle
    {
        public const string Extension = ".fpmap";

        public static bool Is(string path)
        {
            return Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The recipe inside a bundle, or null if it holds none.</summary>
        public static string? ReadRecipe(string bundlePath)
        {
            using ZipArchive archive = ZipFile.OpenRead(bundlePath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                return null;
            }
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// One file out of a bundle, by name or by the end of a name -- the
        /// recipe names "dust2.tex" and the entry is "dust2.tex", but a
        /// bundle cooked from a folder layout may carry a path.
        /// </summary>
        public static byte[]? ReadEntry(string bundlePath, string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return null;
            }
            using ZipArchive archive = ZipFile.OpenRead(bundlePath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(
                    e => e.FullName.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                return null;
            }
            using Stream stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
