using System;
using System.IO;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;

namespace MphRead.Droid
{
    /// <summary>
    /// Gets the custom maps out of the APK and into a directory the game can
    /// read.
    ///
    /// The desktop keeps map files beside the executable and the build copies
    /// them there. Neither half of that works here: an APK's own directory is
    /// read only, and its assets are not files at all -- they live inside the
    /// package and only <c>AssetManager</c> can open them, so
    /// <c>Directory.EnumerateFiles</c> finds nothing however the path is
    /// spelled.
    ///
    /// So the maps are unpacked once, into the same external directory the
    /// extracted game files use. That is also the directory a player can reach
    /// over USB, which means a map they wrote themselves can simply be dropped
    /// in beside the ones that shipped -- and it will survive an app update,
    /// because only the names that came out of the package are overwritten.
    /// </summary>
    internal static class AndroidMaps
    {
        private const string AssetFolder = "maps";

        /// <summary>
        /// Unpack the bundled maps and point the game at them. Call before
        /// anything reads the room tables: the map list is loaded once.
        /// </summary>
        public static void Install(Android.Content.Res.AssetManager? assets, string root)
        {
            if (root.Length == 0)
            {
                return;
            }
            string directory = Path.Combine(root, AssetFolder);
            CustomRooms.MapDirectory = directory;
            if (assets == null)
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(directory);
                string[] names = assets.List(AssetFolder) ?? Array.Empty<string>();
                Console.WriteLine($"[android] {names.Length} bundled map files -> {directory}");
                foreach (string name in names)
                {
                    // The map files, and the levels that travel with them:
                    // see maps/README.md for whose they are and under what
                    // terms. A .fpmap is all of it in one file, which is the
                    // shape a map reaches this platform in -- an asset listing
                    // does not recurse, so a map that keeps its level in a
                    // folder of its own never arrives at all.
                    if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && !name.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)
                        && !name.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)
                        && !name.EndsWith(MapBundle.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        string target = Path.Combine(directory, name);
                        using Stream source = assets.Open($"{AssetFolder}/{name}");
                        using var bytes = new MemoryStream();
                        source.CopyTo(bytes);
                        byte[] content = bytes.ToArray();
                        // Written only when it would actually differ.
                        //
                        // The first version of this compared the file's date
                        // against the package's install time, which is the obvious
                        // way and quietly did nothing: whatever
                        // PackageManager.LastUpdateTime returned here, the test
                        // came out "already up to date" and an updated map file
                        // stayed at the version the previous install had unpacked.
                        // Comparing the bytes needs no Android API to be right and
                        // cannot get stuck. The cost is that an edit to a map that
                        // shipped is undone on the next launch -- to keep one,
                        // copy it to a name of your own, which is a new map as far
                        // as the game is concerned.
                        if (File.Exists(target) && File.ReadAllBytes(target).AsSpan()
                            .SequenceEqual(content))
                        {
                            continue;
                        }
                        string temporary = target + ".install-" + Guid.NewGuid().ToString("N")
                            + (name.EndsWith(MapBundle.Extension, StringComparison.OrdinalIgnoreCase)
                                ? MapBundle.Extension : ".tmp");
                        try
                        {
                            File.WriteAllBytes(temporary, content);
                            if (name.EndsWith(MapBundle.Extension, StringComparison.OrdinalIgnoreCase))
                            {
                                MapBundleValidationResult validation = new MapBundleValidator().Validate(temporary);
                                if (!validation.IsValid)
                                    throw new InvalidDataException("Bundled map package failed validation.");
                            }
                            File.Move(temporary, target, overwrite: true);
                        }
                        finally
                        {
                            if (File.Exists(temporary)) File.Delete(temporary);
                        }
                        Console.WriteLine($"[android] unpacked {name}");
                    }
                    catch (Exception ex)
                    {
                        // One map that cannot be written must not stop the rest.
                        Console.WriteLine($"[android] could not unpack {name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[android] could not unpack the bundled maps: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh the immutable catalog after APK maps have been installed.
        /// Compilation is deliberately deferred until a map is selected.
        /// </summary>
        public static void RefreshCatalog()
        {
            try
            {
                if (!GameFiles.Ready)
                {
                    return;
                }
                GameFiles.ApplyPaths();
                CustomRooms.RefreshAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[android] could not refresh the map catalog: {ex.Message}");
            }
        }
    }
}
