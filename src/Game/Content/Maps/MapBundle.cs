using System;
using System.IO;
using System.Linq;
using System.Text;

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
        public const string ManifestPath = "manifest.json";

        public static bool Is(string path)
        {
            return Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The recipe inside a bundle, or null if it holds none.</summary>
        public static string? ReadRecipe(string bundlePath)
        {
            MapBundleReadResult bundle = new MapBundleReader().Read(bundlePath);
            return Encoding.UTF8.GetString(bundle.ReadDeclaredFile(bundle.Manifest.Recipe));
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
            MapBundleReadResult bundle = new MapBundleReader().Read(bundlePath);
            string canonical = MapBundlePath.Canonicalize(name);
            return bundle.Files.TryGetValue(canonical, out byte[]? bytes)
                ? (byte[])bytes.Clone() : null;
        }

        public static byte[] ReadGeometry(string bundlePath, string? mapName)
        {
            MapBundleReadResult bundle = new MapBundleReader().Read(bundlePath);
            MapManifestFile[] geometry = bundle.Manifest.Files
                .Where(file => file.Role == MapFileRole.Geometry)
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToArray();
            if (geometry.Length == 0)
                throw new MapPackageException("MAP-PKG-006", "Map bundle declares no geometry file.");
            MapManifestFile? selected = mapName == null && geometry.Length == 1
                ? geometry[0]
                : geometry.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Path)
                    .Equals(mapName, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
                throw new MapPackageException("MAP-PKG-006",
                    $"Map bundle does not declare geometry for '{mapName}'.");
            return bundle.ReadDeclaredFile(selected.Path);
        }
    }
}
