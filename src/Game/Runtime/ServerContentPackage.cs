using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.Network
{
    public sealed record ServerContentFile(string Path, long Bytes, string Sha256, string Kind = "data");
    public sealed record ServerContentScenario(string Room, GameMode Mode);
    public sealed record ServerContentManifest(int Format, string Profile, string Version,
        string SourceArm9Sha256, string[] Rooms, int ProbeFrames, ServerContentFile[] Files, ServerContentScenario[] Scenarios, int RoomPlayerCount);

    public static class ServerContentPackage
    {
        public const string ManifestName = "server-content.json";
        public const string Profile = "headless-cpu-v1";
        // Extracted executable from the supplied USA Rev 1 cartridge. This is
        // an identity check, not a license check or a substitute for file hashes.
        public const string Amhe1Arm9Sha256 = "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b";
        private const int MaximumFiles = 4096;
        private const long MaximumFileBytes = 64L * 1024 * 1024;
        private const long MaximumTotalBytes = 256L * 1024 * 1024;
        // The shipped multiplayer section of the retail room table. Later
        // entries are unused rooms, First Hunt and appended custom definitions.
        public static string[] RetailRooms => Metadata.RoomList.Where(IsRetailMultiplayer)
            .Select(room => room.Name).Order(StringComparer.Ordinal).ToArray();

        internal static bool IsRetailMultiplayer(RoomMetadata room) => room.Id is >= 93 and <= 118
            && room.Multiplayer && !room.FirstHunt && !room.Hybrid;


        internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        internal static bool HasObjectives(Scene scene, GameMode mode)
        {
            int flagTeams = 0;
            int baseTeams = 0;
            bool hasFlag = false, hasBase = false, hasNode = false;
            foreach (Entities.EntityBase entity in scene.Entities)
            {
                if (entity is Entities.OctolithFlagEntity flag)
                {
                    hasFlag = true;
                    if (flag.Data.TeamId < 2) { flagTeams |= 1 << (int)flag.Data.TeamId; }
                }
                else if (entity is Entities.FlagBaseEntity flagBase)
                {
                    hasBase = true;
                    if (flagBase.Data.TeamId < 2) { baseTeams |= 1 << (int)flagBase.Data.TeamId; }
                }
                else if (entity is Entities.NodeDefenseEntity) { hasNode = true; }
            }
            return mode switch
            {
                GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams => hasNode,
                GameMode.Bounty or GameMode.BountyTeams => hasFlag && hasBase,
                GameMode.Capture => flagTeams == 3 && baseTeams == 3,
                _ => true
            };
        }

        public static ServerContentManifest Validate(string directory, string expectedVersion)
        {
            directory = Path.GetFullPath(directory);
            string manifestPath = SafeFile(directory, ManifestName);
            if (new FileInfo(manifestPath).Length > 2 * 1024 * 1024)
            {
                throw new ProgramException("Server content manifest exceeds 2 MiB.");
            }
            ServerContentManifest? manifest;
            try { manifest = JsonSerializer.Deserialize<ServerContentManifest>(File.ReadAllText(manifestPath)); }
            catch (JsonException ex) { throw new ProgramException("Invalid server content manifest: " + ex.Message); }
            if (manifest == null || manifest.Format != 1 || manifest.Profile != Profile
                || expectedVersion != "AMHE1" || manifest.Version != expectedVersion
                || manifest.SourceArm9Sha256 != Amhe1Arm9Sha256
                || manifest.Rooms == null || manifest.Rooms.Length is < 1 or > 64
                || manifest.Rooms.Any(String.IsNullOrWhiteSpace)
                || manifest.ProbeFrames is < 1 or > 216000
                || manifest.RoomPlayerCount != NetConfig.RoomPlayerCount
                || manifest.Files == null || manifest.Files.Length is < 1 or > MaximumFiles
                || manifest.Scenarios == null || manifest.Scenarios.Length is < 1 or > 768
                || manifest.Scenarios.Any(s => s == null || s.Mode < GameMode.Battle || s.Mode > GameMode.PrimeHunter
                    || !manifest.Rooms.Contains(s.Room, StringComparer.Ordinal))
                || manifest.Scenarios.Distinct().Count() != manifest.Scenarios.Length
                || manifest.Rooms.Any(room => !manifest.Scenarios.Any(s => s.Room == room)))
            {
                throw new ProgramException("Unsupported or incomplete server content manifest.");
            }
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
            long total = 0;
            foreach (ServerContentFile entry in manifest.Files)
            {
                if (entry == null || !expected.Add(entry.Path) || entry.Bytes < 0
                    || entry.Bytes > MaximumFileBytes || (total += entry.Bytes) > MaximumTotalBytes
                    || entry.Kind is not ("data" or "cpu-model"))
                {
                    throw new ProgramException("Invalid or duplicate server content entry.");
                }
                string path = SafeFile(directory, entry.Path);
                if (!File.Exists(path) || new FileInfo(path).Length != entry.Bytes || HashFile(path) != entry.Sha256)
                {
                    throw new ProgramException("Server content integrity check failed: " + entry.Path);
                }
            }
            foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ProgramException("Server content cannot contain symbolic links.");
                }
                if (File.Exists(path) && !expected.Contains(Path.GetRelativePath(directory, path).Replace('\\', '/')))
                {
                    throw new ProgramException("Unlisted file in server content: " + Path.GetFileName(path));
                }
            }
            return manifest;
        }

        internal static string ResolveDirectory(string directory)
        {
            string full = Path.GetFullPath(directory);
            string root = Path.GetPathRoot(full)!;
            string current = root;
            foreach (string part in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget != null)
                {
                    current = ResolveDirectory(new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true)!.FullName);
                }
            }
            return Path.GetFullPath(current);
        }

        internal static bool IsWithin(string root, string path)
        {
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison)
                || String.Equals(root, path, comparison);
        }

        internal static void CheckRelativePath(string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || relative.Length > 240
                || relative.Any(c => c is '\\' or ':' || Char.IsControl(c))
                || relative.Split('/').Any(part => part is "" or "." or "..")
                || Path.IsPathRooted(relative))
            {
                throw new ProgramException("Invalid relative server content path.");
            }
        }

        internal static string SafeFile(string root, string relative)
        {
            CheckRelativePath(relative);
            string current = root;
            if (new DirectoryInfo(root).LinkTarget != null)
            {
                throw new ProgramException("Server content root cannot be a symbolic link.");
            }
            foreach (string part in relative.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current))
                    && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ProgramException("Server content cannot contain symbolic links.");
                }
            }
            return current;
        }

        internal static string HashFile(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
