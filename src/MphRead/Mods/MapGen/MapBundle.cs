using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;

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

        /// <summary>
        /// Validated bounded stream access for package-authoring/fingerprinting.
        /// Preserves the existing whole-package trust check on first access,
        /// but does not allocate a full per-asset byte array. ZIP ownership
        /// stays scoped to the callback so no dangling decompression stream can
        /// outlive the archive. Live runtime MapBundle.ReadEntry is unchanged.
        /// </summary>
        internal static void WithReadEntryStream(string bundlePath, string name,
            Action<Stream, long> consume)
        {
            name = MapPackageReader.CanonicalName(name);
            string full = Path.GetFullPath(bundlePath);
            ValidationStamp before = Stamp(full);
            if (!_validated.TryGetValue(full, out ValidationStamp validated)
                || validated != before)
            {
                using var strict = new MapPackageReader(full);
                if (Stamp(full) != before)
                    throw new IOException("Map package changed during verification.");
                _validated[full] = before;
            }

            using var zip = ZipFile.OpenRead(full);
            ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                ZipArchiveEntry[] matches = zip.Entries.Where(e =>
                    e.FullName.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1)
                    throw new InvalidDataException("Ambiguous package asset reference.");
                entry = matches.SingleOrDefault();
            }
            if (entry == null)
                throw new InvalidDataException("Missing map asset: " + name);
            if (entry.Length is < 0 or > MapPackageReader.MaxEntryBytes)
                throw new InvalidDataException("Map asset is too large.");

            using (Stream input = entry.Open())
                consume(input, entry.Length);
            if (Stamp(full) != before)
            {
                _validated.TryRemove(full, out _);
                throw new IOException("Map package changed while an asset was read.");
            }
        }

        private static ValidationStamp Stamp(string path)
        {
            var file = new FileInfo(path);
            return new(file.Length, file.LastWriteTimeUtc.Ticks);
        }

        internal static void CopyEntry(string bundlePath, string name, Stream destination,
            System.Threading.CancellationToken stop)
        {
            string full = Path.GetFullPath(bundlePath);
            ValidationStamp before = Stamp(full);
            if (!_validated.TryGetValue(full, out ValidationStamp known) || known != before)
            {
                using var package = new MapPackageReader(full);
                if (Stamp(full) != before) throw new IOException("Map changed during validation.");
                _validated[full] = before;
            }
            MapPackageReader.CopyValidatedEntry(full, name, destination, stop);
            if (Stamp(full) != before)
            {
                _validated.TryRemove(full, out _);
                throw new IOException("Map changed during video extraction.");
            }
        }
    }
}
