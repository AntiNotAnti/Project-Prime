using System;
using System.Collections.Concurrent;
using System.IO;

namespace MphRead.Mods.MapGen
{
    /// <summary>Compatibility entry point for legacy callers and v1 bundles.</summary>
    public static class MapBundle
    {
        private readonly record struct ValidationStamp(long Length, long LastWriteUtcTicks);
        private static readonly ConcurrentDictionary<string, ValidationStamp> _validated = new(StringComparer.Ordinal);

        public const string Extension = ".ppmap";
        public static bool Is(string path) => Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
        public static string Cook(MapDefinition definition, string recipePath, string? outputPath, bool verbose = true)
        {
            string path = MapPackageBuilder.Build(definition, outputPath ?? Path.Combine(CustomRooms.UserMapDirectory,
                Path.GetFileNameWithoutExtension(recipePath) + Extension));
            if (verbose) Console.WriteLine($"[mappackage] {definition.Name} -> {path}");
            return path;
        }
        public static string? ReadRecipe(string bundlePath)
        {
            string full = Path.GetFullPath(bundlePath);
            ValidationStamp before = Stamp(full);
            using var package = new MapPackageReader(full);
            string project = package.ReadProject();
            ValidationStamp after = Stamp(full);
            if (before == after) _validated[full] = after;
            else _validated.TryRemove(full, out _);
            return project;
        }

        public static byte[]? ReadEntry(string bundlePath, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string full = Path.GetFullPath(bundlePath);
            ValidationStamp before = Stamp(full);
            if (_validated.TryGetValue(full, out ValidationStamp validated) && validated == before)
            {
                byte[]? bytes = MapPackageReader.ReadValidatedEntry(full, name);
                if (Stamp(full) == before) return bytes;
                _validated.TryRemove(full, out _);
                throw new IOException("Map package changed while an asset was being read.");
            }

            using var package = new MapPackageReader(full);
            byte[]? result = package.Read(name);
            ValidationStamp after = Stamp(full);
            if (before == after) _validated[full] = after;
            else _validated.TryRemove(full, out _);
            return result;
        }

        private static ValidationStamp Stamp(string path)
        {
            var file = new FileInfo(path);
            return new(file.Length, file.LastWriteTimeUtc.Ticks);
        }
    }
}
