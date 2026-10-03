using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.MapGen
{
    public sealed record MapCatalogEntry(string Path, MapDefinition? Definition, MapValidationResult Validation);

    public sealed class MapCatalog
    {
        private const int CacheVersion = 2;
        private static readonly object CacheGate = new();
        private static readonly HashSet<string> PrunedDirectories = new(
            new[] { "textures", "audio", "preview", "build", "cache", ".studio", ".autosave" },
            StringComparer.OrdinalIgnoreCase);

        private sealed class CachedPackage
        {
            public long Length { get; set; }
            public long LastWriteUtcTicks { get; set; }
            public string Project { get; set; } = "";
            public MapDiagnostic[] Diagnostics { get; set; } = Array.Empty<MapDiagnostic>();
            public MapBudget[] Budgets { get; set; } = Array.Empty<MapBudget>();
        }

        private sealed class PackageCache
        {
            public int Version { get; set; } = CacheVersion;
            public string Build { get; set; } = Update.BuildVersion.Display;
            public Dictionary<string, CachedPackage> Packages { get; set; }
                = new(StringComparer.OrdinalIgnoreCase);
        }

        public string DirectoryPath { get; }
        public IReadOnlyList<MapCatalogEntry> Entries { get; private set; }
            = Array.Empty<MapCatalogEntry>();

        public MapCatalog(string directory) { DirectoryPath = directory; }

        public IReadOnlyList<MapCatalogEntry> Refresh(bool preferPackages = true)
        {
            var clock = Stopwatch.StartNew();
            var entries = new List<MapCatalogEntry>();
            var names = new Dictionary<string, MapCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            var ids = new Dictionary<Guid, MapCatalogEntry>();
            string installed = Path.Combine(DirectoryPath, ".installed");
            string mapRoot = Path.GetFullPath(DirectoryPath);
            string userRoot = Path.GetFullPath(CustomRooms.UserMapDirectory);

            var files = MapFiles(DirectoryPath)
                .Concat(Directory.Exists(installed)
                    ? Directory.EnumerateFiles(installed, "*" + MapBundle.Extension,
                        SearchOption.TopDirectoryOnly)
                    : Array.Empty<string>())
                .Concat(mapRoot == Path.GetFullPath(CustomRooms.MapDirectory)
                    ? MapFiles(CustomRooms.UserMapDirectory)
                    : Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => PreferredPackage(p, installed, userRoot, preferPackages) ? -1
                    : MapBundle.Is(p) == preferPackages ? 0 : 1)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToList();

            PackageCache cache = LoadCache();
            bool cacheDirty = false;
            int packageHits = 0, packageMisses = 0;
            long avoidedBytes = 0;

            foreach (string path in files)
            {
                MapDefinition? definition = null;
                MapValidationResult validation;
                bool package = MapBundle.Is(path);
                try
                {
                    if (package && TryCached(cache, path, out CachedPackage cached))
                    {
                        try
                        {
                            definition = MapDefinition.FromCatalogCache(cached.Project, path);
                            validation = RestoreValidation(cached);
                            packageHits++;
                            avoidedBytes += cached.Length;
                        }
                        catch
                        {
                            cache.Packages.Remove(Path.GetFullPath(path));
                            cacheDirty = true;
                            definition = null;
                            validation = new();
                        }
                    }
                    else
                    {
                        validation = new();
                    }

                    if (definition == null)
                    {
                        definition = package
                            ? MapDefinition.LoadCatalog(path)
                            : MapDefinition.Load(path);
                        // A package's entry table, manifest/project identity,
                        // compatibility metadata and asset references were
                        // already checked by the lightweight reader. Full
                        // geometry/source validation belongs to build/use
                        // boundaries; doing it here can compile thousands of
                        // faces before the launcher has drawn a frame.
                        validation = package
                            ? new MapValidationResult()
                            : MapValidator.Validate(definition, checkSources: true);
                        if (package)
                        {
                            StoreCached(cache, path, definition, validation);
                            cacheDirty = true;
                            packageMisses++;
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException
                    or UnauthorizedAccessException or JsonException or ProgramException
                    or ArgumentException)
                {
                    validation = new();
                    validation.Error("FP-MAP-020", ex.Message);
                    if (package && cache.Packages.Remove(Path.GetFullPath(path)))
                        cacheDirty = true;
                }

                var entry = new MapCatalogEntry(path, definition, validation);
                if (definition != null)
                {
                    if (names.TryGetValue(definition.Name, out MapCatalogEntry? prior))
                    {
                        Guid priorId = prior.Definition!.MapId == Guid.Empty
                            ? MapPackageBuilder.LegacyId(prior.Definition.Name)
                            : prior.Definition.MapId;
                        Guid currentId = definition.MapId == Guid.Empty
                            ? MapPackageBuilder.LegacyId(definition.Name)
                            : definition.MapId;
                        bool sameId = priorId == currentId;
                        if (sameId && (MapBundle.Is(prior.Path) != MapBundle.Is(path)
                            || Path.GetDirectoryName(prior.Path) == installed
                            || Path.GetDirectoryName(path) == installed
                            || Path.GetDirectoryName(prior.Path) == CustomRooms.UserMapDirectory
                            || Path.GetDirectoryName(path) == CustomRooms.UserMapDirectory))
                            continue;
                        validation.Error("FP-MAP-010",
                            $"Duplicate runtime name: {definition.Name}.");
                        prior.Validation.Error("FP-MAP-010",
                            $"Duplicate runtime name: {definition.Name}.");
                    }
                    else names.Add(definition.Name, entry);

                    if (definition.MapId != Guid.Empty)
                    {
                        if (ids.TryGetValue(definition.MapId, out prior))
                        {
                            validation.Error("FP-MAP-010", "Duplicate map ID.");
                            prior.Validation.Error("FP-MAP-010", "Duplicate map ID.");
                        }
                        else ids.Add(definition.MapId, entry);
                    }
                }
                entries.Add(entry);
            }

            if (cacheDirty) SaveCache(cache);
            clock.Stop();
            if (packageHits + packageMisses > 0)
            {
                DebugLog.Line("startup",
                    $"map catalog {files.Count} files in {clock.Elapsed.TotalMilliseconds:0.0} ms; "
                    + $"package cache {packageHits} hit/{packageMisses} miss, "
                    + $"{FormatBytes(avoidedBytes)} package streaming avoided");
            }
            return Entries = entries.AsReadOnly();
        }

        private static bool PreferredPackage(string path, string installed,
            string userRoot, bool preferPackages)
        {
            if (!preferPackages || !MapBundle.Is(path)) return false;
            string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return String.Equals(parent, Path.GetFullPath(installed),
                    StringComparison.OrdinalIgnoreCase)
                || String.Equals(parent, userRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Enumerate only places that can contain map definitions. The previous
        /// recursive EnumerateFiles call walked 4K/8K texture and audio trees
        /// and discarded those paths afterwards, turning catalog discovery into
        /// an asset-library crawl.
        /// </summary>
        private static IEnumerable<string> MapFiles(string root)
        {
            if (!Directory.Exists(root)) yield break;
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                try { files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { continue; }

                foreach (string path in files)
                {
                    string file = Path.GetFileName(path);
                    if (file.Equals("map.build.json", StringComparison.OrdinalIgnoreCase)
                        || file.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (MapBundle.Is(path)
                        || Path.GetExtension(path).Equals(".json",
                            StringComparison.OrdinalIgnoreCase))
                        yield return path;
                }

                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(directory, "*",
                        SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { continue; }

                foreach (string child in directories)
                {
                    string name = Path.GetFileName(child);
                    if (name.StartsWith('.') || PrunedDirectories.Contains(name)) continue;
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { continue; }
                    pending.Push(child);
                }
            }
        }

        private static string CachePath => Path.Combine(
            Platform.AppPaths.UserDataDirectory, "map-catalog-v2.json");

        private static PackageCache LoadCache()
        {
            lock (CacheGate)
            {
                try
                {
                    if (!File.Exists(CachePath)) return new();
                    PackageCache? cache = JsonSerializer.Deserialize<PackageCache>(
                        File.ReadAllBytes(CachePath), MapPackageReader.JsonOptions);
                    if (cache == null || cache.Version != CacheVersion
                        || !String.Equals(cache.Build, Update.BuildVersion.Display,
                            StringComparison.OrdinalIgnoreCase))
                        return new();
                    cache.Packages = new Dictionary<string, CachedPackage>(
                        cache.Packages ?? new Dictionary<string, CachedPackage>(),
                        StringComparer.OrdinalIgnoreCase);
                    return cache;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or JsonException or ArgumentException)
                {
                    DebugLog.Line("startup", "map catalog cache ignored: " + ex.Message);
                    return new();
                }
            }
        }

        private static void SaveCache(PackageCache cache)
        {
            lock (CacheGate)
            {
                try
                {
                    cache.Version = CacheVersion;
                    cache.Build = Update.BuildVersion.Display;
                    // Stale entries cost disk, not correctness. Prune missing
                    // packages and cap the file so moved libraries do not grow
                    // this cache without bound.
                    foreach (string key in cache.Packages.Keys
                        .Where(key => !File.Exists(key)).ToArray())
                        cache.Packages.Remove(key);
                    if (cache.Packages.Count > 1024)
                    {
                        foreach (string key in cache.Packages.Keys
                            .OrderBy(key =>
                            {
                                try { return File.GetLastWriteTimeUtc(key); }
                                catch { return DateTime.MinValue; }
                            })
                            .Take(cache.Packages.Count - 1024).ToArray())
                            cache.Packages.Remove(key);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    AtomicFile.Write(CachePath, JsonSerializer.SerializeToUtf8Bytes(
                        cache, MapPackageReader.JsonOptions));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException)
                {
                    DebugLog.Line("startup", "map catalog cache write skipped: " + ex.Message);
                }
            }
        }

        private static bool TryCached(PackageCache cache, string path,
            out CachedPackage cached)
        {
            string full = Path.GetFullPath(path);
            if (!cache.Packages.TryGetValue(full, out cached!)) return false;
            try
            {
                var info = new FileInfo(full);
                return info.Exists && info.Length == cached.Length
                    && info.LastWriteTimeUtc.Ticks == cached.LastWriteUtcTicks;
            }
            catch
            {
                cached = null!;
                return false;
            }
        }

        private static void StoreCached(PackageCache cache, string path,
            MapDefinition definition, MapValidationResult validation)
        {
            string full = Path.GetFullPath(path);
            var info = new FileInfo(full);
            cache.Packages[full] = new CachedPackage
            {
                Length = info.Length,
                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                Project = definition.Serialize(),
                Diagnostics = validation.Diagnostics.ToArray(),
                Budgets = validation.Budgets.ToArray()
            };
        }

        private static MapValidationResult RestoreValidation(CachedPackage cached)
        {
            var result = new MapValidationResult();
            result.Diagnostics.AddRange(cached.Diagnostics ?? Array.Empty<MapDiagnostic>());
            result.Budgets.AddRange(cached.Budgets ?? Array.Empty<MapBudget>());
            return result;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            { value /= 1024; unit++; }
            return $"{value:0.#} {units[unit]}";
        }
    }
}
