using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen
{
    public sealed class MapPackageManifest
    {
        public int Format { get; set; } = 2;
        public Guid MapId { get; set; }
        public string? MapVersion { get; set; }
        public string Name { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Author { get; set; }
        public string ContentHash { get; set; } = "";
        public string Project { get; set; } = "project.json";
        public string? Preview { get; set; }
        public int MinimumProtocol { get; set; }
        public string? MinimumGameVersion { get; set; }
        public string[] SupportedModes { get; set; } = Array.Empty<string>();
        public int MinPlayers { get; set; } = 1;
        public int MaxPlayers { get; set; } = 8;
    }

    public sealed class MapPackageReader : IDisposable
    {
        public const long MaxArchiveBytes = 512L * 1024 * 1024;
        public const long MaxExpandedBytes = 1024L * 1024 * 1024;
        public const long MaxEntryBytes = 256L * 1024 * 1024;
        public const int MaxEntries = 2048;
        public static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        private readonly ZipArchive _archive;
        private readonly Dictionary<string, ZipArchiveEntry> _entries;
        public MapPackageManifest? Manifest { get; }
        public string ProjectEntry { get; }

        /// <summary>
        /// Strict package reader. This verifies the content digest and therefore
        /// streams every package asset. Use it at install/share/runtime trust
        /// boundaries, not while merely populating launcher catalog rows.
        /// </summary>
        public MapPackageReader(string path)
        {
            RequireArchiveSize(path);
            _archive = ZipFile.OpenRead(path);
            try
            {
                _entries = ScanEntries(_archive);
                if (_entries.ContainsKey("manifest.json"))
                {
                    Manifest = ReadManifest(_entries);
                    ValidateManifest(Manifest);
                    ProjectEntry = Manifest.Project;
                    RequireProjectShape(_entries);
                    string hash = ContentHash(_entries.Keys.Where(n => n != "manifest.json"),
                        n => ReadRequired(_entries, n));
                    if (!hash.Equals(Manifest.ContentHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Package content hash does not match.");
                    MapDefinition definition = ReadDefinition(_entries, ProjectEntry, legacy: false);
                    ValidateManifestProject(Manifest, definition);
                    if (Manifest.Preview != null && !_entries.ContainsKey(CanonicalName(Manifest.Preview)))
                        throw new InvalidDataException("Packaged preview is missing.");
                    ValidateReferences(_entries, definition);
                }
                else
                {
                    var recipes = _entries.Keys.Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (recipes.Length != 1)
                        throw new InvalidDataException("Legacy package requires exactly one recipe.");
                    ProjectEntry = recipes[0];
                    MapDefinition definition = ReadDefinition(_entries, ProjectEntry, legacy: true);
                    MapValidator.RequireRuntimeName(definition.Name);
                    ValidateReferences(_entries, definition);
                }
            }
            catch
            {
                _archive.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Read the catalog project without hashing/decompressing unrelated assets.
        /// ZIP structure, entry budgets, compatibility metadata, project identity
        /// and referenced entry names are still validated. The strict constructor
        /// remains the gate for using or sharing the package.
        /// </summary>
        public static string ReadProjectForCatalog(string path)
        {
            RequireArchiveSize(path);
            using ZipArchive archive = ZipFile.OpenRead(path);
            Dictionary<string, ZipArchiveEntry> entries = ScanEntries(archive);
            string projectEntry;
            if (entries.ContainsKey("manifest.json"))
            {
                MapPackageManifest manifest = ReadManifest(entries);
                ValidateManifest(manifest);
                projectEntry = manifest.Project;
                RequireProjectShape(entries);
                byte[] projectBytes = ReadRequired(entries, projectEntry, 8 * 1024 * 1024);
                MapDefinition definition = DeserializeDefinition(projectBytes, legacy: false);
                ValidateManifestProject(manifest, definition);
                if (manifest.Preview != null && !entries.ContainsKey(CanonicalName(manifest.Preview)))
                    throw new InvalidDataException("Packaged preview is missing.");
                ValidateReferences(entries, definition);
                return Encoding.UTF8.GetString(projectBytes);
            }

            var recipes = entries.Keys.Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (recipes.Length != 1)
                throw new InvalidDataException("Legacy package requires exactly one recipe.");
            projectEntry = recipes[0];
            byte[] legacyBytes = ReadRequired(entries, projectEntry, 8 * 1024 * 1024);
            MapDefinition legacyDefinition = DeserializeDefinition(legacyBytes, legacy: true);
            MapValidator.RequireRuntimeName(legacyDefinition.Name);
            ValidateReferences(entries, legacyDefinition);
            return Encoding.UTF8.GetString(legacyBytes);
        }

        /// <summary>
        /// Read one bounded package entry for presentation-only work such as a
        /// launcher preview. This deliberately does not calculate the package
        /// digest; callers must not use it as an install/share verification gate.
        /// </summary>
        public static byte[]? ReadCatalogEntry(string path, string name,
            long limit = 32L * 1024 * 1024)
        {
            RequireArchiveSize(path);
            using ZipArchive archive = ZipFile.OpenRead(path);
            Dictionary<string, ZipArchiveEntry> entries = ScanEntries(archive);
            string? found = Find(entries, name);
            if (found == null) return null;
            return ReadRequired(entries, found, Math.Min(Math.Max(1, limit), MaxEntryBytes));
        }

        private static void RequireArchiveSize(string path)
        {
            long length = new FileInfo(path).Length;
            if (length < 0 || length > MaxArchiveBytes)
                throw new InvalidDataException("Package exceeds the 512 MiB limit.");
        }

        private static Dictionary<string, ZipArchiveEntry> ScanEntries(ZipArchive archive)
        {
            if (archive.Entries.Count > MaxEntries)
                throw new InvalidDataException("Package contains too many entries.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = CanonicalName(entry.FullName);
                if (!entries.TryAdd(name, entry))
                    throw new InvalidDataException("Duplicate package path: " + name);
                if (entry.Length < 0 || entry.Length > MaxEntryBytes
                    || (total += entry.Length) > MaxExpandedBytes)
                    throw new InvalidDataException("Package expanded size exceeds the limit.");
                string ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not (".json" or ".bsp" or ".obj" or ".tex" or ".png"
                    or ".jpg" or ".jpeg" or ".tga" or ".ogg" or ".wav" or ".mp3"))
                    throw new InvalidDataException("Unsupported package asset: " + name);
            }
            return entries;
        }

        private static MapPackageManifest ReadManifest(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            return JsonSerializer.Deserialize<MapPackageManifest>(
                ReadRequired(entries, "manifest.json", 64 * 1024), JsonOptions)
                ?? throw new InvalidDataException("Missing package manifest.");
        }

        private static void ValidateManifest(MapPackageManifest manifest)
        {
            if (manifest.Format != 2 || manifest.MapId == Guid.Empty
                || !MapValidator.ValidRuntimeName(manifest.Name)
                || manifest.Project != "project.json" || manifest.ContentHash?.Length != 64)
                throw new InvalidDataException("Invalid package manifest or identity.");
            if (manifest.MinimumProtocol < 0
                || manifest.MinimumProtocol > Network.NetConfig.ProtocolVersion)
                throw new InvalidDataException("This map requires a newer network protocol.");
            if (manifest.MinimumGameVersion?.Length > 64 || manifest.SupportedModes == null
                || manifest.SupportedModes.Length > 32
                || manifest.SupportedModes.Any(m => m == null
                    || !Enum.TryParse<GameMode>(m, out var mode) || !Enum.IsDefined(mode))
                || manifest.MinPlayers < 1 || manifest.MaxPlayers > 8
                || manifest.MinPlayers > manifest.MaxPlayers)
                throw new InvalidDataException("Invalid map compatibility metadata.");
        }

        private static void RequireProjectShape(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            if (entries.Keys.Count(n => n.EndsWith(".json",
                StringComparison.OrdinalIgnoreCase)) != 2)
                throw new InvalidDataException("Package must have exactly one manifest and one project.");
        }

        private static MapDefinition ReadDefinition(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries, string project, bool legacy)
            => DeserializeDefinition(ReadRequired(entries, project, 8 * 1024 * 1024), legacy);

        private static MapDefinition DeserializeDefinition(byte[] bytes, bool legacy)
        {
            JsonSerializerOptions options = legacy
                ? new JsonSerializerOptions(JsonOptions)
                    { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
                : JsonOptions;
            return JsonSerializer.Deserialize<MapDefinition>(bytes, options)
                ?? throw new InvalidDataException(legacy
                    ? "Invalid legacy recipe." : "Invalid package project.");
        }

        private static void ValidateManifestProject(
            MapPackageManifest manifest, MapDefinition definition)
        {
            if (definition.MapId != manifest.MapId || definition.Name != manifest.Name
                || definition.FormatVersion != 2)
                throw new InvalidDataException("Manifest and project identities differ.");
            if (definition.Version != manifest.MapVersion
                || definition.InGameName != manifest.DisplayName
                || definition.Author != manifest.Author)
                throw new InvalidDataException("Manifest and project metadata differ.");
        }

        private static void ValidateReferences(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries, MapDefinition definition)
        {
            if (definition.Assets == null)
                throw new InvalidDataException("Missing asset list.");
            foreach (var asset in definition.Assets)
                if (asset == null || !entries.ContainsKey(CanonicalName(asset.Path)))
                    throw new InvalidDataException("Packaged asset is missing.");
            if (definition.Materials == null || definition.Materials.Any(m => m == null))
                throw new InvalidDataException("Missing material list.");
            foreach (string asset in MapDependencyAnalyzer.PackageAssets(definition))
                if (!entries.ContainsKey(CanonicalName(asset)))
                    throw new InvalidDataException("Packaged asset is missing.");
            if (definition.Collision is { } collision)
            {
                if (string.IsNullOrEmpty(collision.Source)
                    || !collision.Source.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                    || !entries.ContainsKey(CanonicalName(collision.Source)))
                    throw new InvalidDataException("Packaged collision mesh is missing or invalid.");
            }
            if (definition.Import is { } import)
            {
                if (!entries.ContainsKey(CanonicalName(import.Source)))
                    throw new InvalidDataException("Packaged BSP is missing.");
                if (!import.Source.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Package import must reference a BSP.");
                if (!string.IsNullOrEmpty(import.Textures)
                    && Find(entries, import.Textures) == null)
                    throw new InvalidDataException("Packaged texture pack is missing.");
            }
        }

        public static string CanonicalName(string name)
        {
            // Package components are file names, not runtime room names. In particular,
            // content-addressed texture names contain a 64-character SHA-256 digest and
            // legitimately exceed the runtime room-name limit. Keep the traversal/device
            // protections here without applying that unrelated 40-character constraint.
            if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Contains('\\')
                || name.StartsWith('/') || name.Any(c => c < 32 || ":<>\"|?*".Contains(c)))
                throw new InvalidDataException("Unsafe package path.");
            foreach (string part in name.Split('/'))
            {
                if (part is "" or "." or ".." || part.Trim() != part || part.EndsWith('.')
                    || !Char.IsAsciiLetterOrDigit(part[0])
                    || part.Any(c => !(Char.IsAsciiLetterOrDigit(c)
                        || c is ' ' or '_' or '-' or '.')))
                    throw new InvalidDataException("Unsafe package path: " + name);
                string stem = part.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
                if (stem is "CON" or "PRN" or "AUX" or "NUL"
                    || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                        || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] is >= '1' and <= '9')
                    throw new InvalidDataException("Unsafe package path: " + name);
            }
            return name;
        }

        private static string? Find(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries, string name)
        {
            name = CanonicalName(name);
            if (entries.ContainsKey(name)) return name;
            var matches = entries.Keys.Where(n => n.EndsWith("/" + name,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException("Ambiguous package reference.");
            return matches.SingleOrDefault();
        }

        private string? Find(string name) => Find(_entries, name);

        public byte[]? Read(string name)
            => Find(name) is { } found ? ReadRequired(_entries, found) : null;

        public string ReadProject()
            => Encoding.UTF8.GetString(ReadRequired(_entries, ProjectEntry, 8 * 1024 * 1024));

        private static byte[] ReadRequired(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries, string name,
            long limit = MaxEntryBytes)
        {
            if (!entries.TryGetValue(name, out ZipArchiveEntry? entry) || entry == null
                || entry.Length < 0 || entry.Length > limit)
                throw new InvalidDataException("Missing or oversized entry: " + name);
            using Stream input = entry.Open();
            using var output = new MemoryStream(
                entry.Length <= Int32.MaxValue ? (int)entry.Length : 0);
            byte[] buffer = new byte[65536];
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                if (output.Length + count > limit || output.Length + count > entry.Length)
                    throw new InvalidDataException("Entry exceeds declared size.");
                output.Write(buffer, 0, count);
            }
            if (output.Length != entry.Length)
                throw new InvalidDataException("Truncated package entry.");
            return output.ToArray();
        }

        public static string ContentHash(IEnumerable<string> names, Func<string, byte[]> read)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string name in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                byte[] path = Encoding.UTF8.GetBytes(name), data = read(name);
                using var buffer = new MemoryStream();
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                {
                    writer.Write(path.Length);
                    writer.Write(path);
                    writer.Write((long)data.Length);
                }
                hash.AppendData(buffer.ToArray());
                hash.AppendData(data);
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        public void Dispose() => _archive.Dispose();
    }
}
