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

    /// <summary>
    /// Reproducible, integrity-checked package of observed headless dependencies.
    /// Format 1 projects loaded models onto their simulation sections and
    /// excludes texture, palette and render-command payloads.
    /// </summary>
    public static class ServerContentPack
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

        private static bool IsRetailMultiplayer(RoomMetadata room) => room.Id is >= 93 and <= 118
            && room.Multiplayer && !room.FirstHunt && !room.Hybrid;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static ServerContentManifest Bake(string source, string destination, string version,
            IEnumerable<string> rooms, int probeFrames = 600)
        {
            if (version != "AMHE1" || probeFrames < 1 || probeFrames > 216000)
            {
                throw new ProgramException("Content baking currently requires AMHE1 and 1–216000 probe frames.");
            }
            source = ResolveDirectory(source);
            destination = ResolveDirectory(destination);
            if (Directory.Exists(destination) || File.Exists(destination)
                || IsWithin(source, destination) || IsWithin(destination, source))
            {
                throw new ProgramException("Use a new output directory outside the source content tree.");
            }
            string[] selectedRooms = rooms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (selectedRooms.Length is < 1 or > 64 || selectedRooms.Any(String.IsNullOrWhiteSpace))
            {
                throw new ProgramException("Specify 1–64 room names for content baking.");
            }
            foreach (string room in selectedRooms)
            {
                (RoomMetadata? metadata, _) = Metadata.GetRoomByName(room);
                if (metadata == null || !IsRetailMultiplayer(metadata))
                {
                    throw new ProgramException("Content baking requires a retail multiplayer room: " + room);
                }
            }
            string arm9 = HashFile(Path.Combine(source, "_bin", "arm9.bin"));
            if (arm9 != Amhe1Arm9Sha256)
            {
                throw new ProgramException("Extracted arm9.bin does not match the supported AMHE1 revision.");
            }
            using var context = ServerContent.PreserveContext(version);
            ServerContentScenario[] scenarios;
            var modelPaths = new HashSet<string>(StringComparer.Ordinal);
            var observed = new SortedDictionary<string, ServerContentFile>(StringComparer.Ordinal);
            ServerContent.Open(source, version);
            using (ServerContent.TraceReads((path, bytes) =>
            {
                if (!IsWithin(source, path))
                {
                    throw new ProgramException("Content probe read outside its source directory: " + path);
                }
                string relative = Path.GetRelativePath(source, path).Replace('\\', '/');
                CheckRelativePath(relative);
                var entry = new ServerContentFile(relative, bytes.LongLength, Hash(bytes));
                if (observed.TryGetValue(relative, out ServerContentFile? previous) && previous != entry)
                {
                    throw new ProgramException("Content changed during the probe: " + relative);
                }
                observed[relative] = entry;
            }, path => modelPaths.Add(Path.GetRelativePath(source, path).Replace('\\', '/'))))
            {
                scenarios = ProbeScenarios(selectedRooms.SelectMany(room =>
                    Enumerable.Range((int)GameMode.Battle, (int)GameMode.PrimeHunter - (int)GameMode.Battle + 1)
                        .Select(mode => new ServerContentScenario(room, (GameMode)mode))), probeFrames, discover: true);
            }
            var manifest = new ServerContentManifest(1, Profile, version, arm9,
                selectedRooms, probeFrames, observed.Values.ToArray(), scenarios, NetLaunch.RoomPlayerCount);
            string staging = destination + ".building-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                for (int index = 0; index < manifest.Files.Length; index++)
                {
                    ServerContentFile entry = manifest.Files[index];
                    string input = SafeFile(source, entry.Path);
                    string output = Path.Combine(staging, entry.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    if (HashFile(input) != entry.Sha256)
                    {
                        throw new ProgramException("Content changed after the probe: " + entry.Path);
                    }
                    if (modelPaths.Contains(entry.Path))
                    {
                        byte[] model = File.ReadAllBytes(input);
                        if (Hash(model) != entry.Sha256)
                        {
                            throw new ProgramException("Model changed during baking: " + entry.Path);
                        }
                        byte[] projected = Server.ServerModelData.Project(model);
                        File.WriteAllBytes(output, projected);
                        manifest.Files[index] = entry with { Bytes = projected.LongLength, Sha256 = Hash(projected), Kind = "cpu-model" };
                    }
                    else { File.Copy(input, output, overwrite: false); }
                }
                File.WriteAllText(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions) + "\n");
                Verify(staging, version, probeFrames);
                Directory.Move(staging, destination);
                return manifest;
            }
            finally
            {
                if (Directory.Exists(staging)) { Directory.Delete(staging, recursive: true); }
            }
        }

        /// <summary>Validate hashes, then replay the requested bounded all-mode probe from the package.</summary>
        public static void Verify(string directory, string version, int frames = 600)
        {
            if (frames is < 1 or > 216000) { throw new ProgramException("Probe frames must be between 1 and 216000."); }
            ServerContentManifest manifest = Validate(directory, version);
            using var context = ServerContent.PreserveContext(version);
            ServerContent.Open(directory, version);
            ProbeScenarios(manifest.Scenarios, frames, discover: false);
        }

        private static ServerContentScenario[] ProbeScenarios(IEnumerable<ServerContentScenario> scenarios,
            int frames, bool discover)
        {
            var supported = new List<ServerContentScenario>();
            foreach (ServerContentScenario scenario in scenarios)
            {
                Read.ClearCache();
                Rng.SetRng1(Rng.Rng1StartValue);
                Rng.SetRng2(Rng.Rng2StartValue);
                Scene scene = Scene.CreateHeadless();
                int spawned = 0;
                bool objectives = false;
                try
                {
                    scene.LoadServerRoom(scenario.Room, scenario.Mode, players: 8, bots: true,
                        roomPlayerCount: NetLaunch.RoomPlayerCount);
                    WorldStateCapture.ValidateRoom(scene);
                    objectives = HasObjectives(scene, scenario.Mode);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        scene.StepHeadlessFrame();
                        foreach (Entities.PlayerEntity player in scene.GetPlayerEntities())
                        {
                            var position = player.Position;
                            if (!Single.IsFinite(position.X) || !Single.IsFinite(position.Y) || !Single.IsFinite(position.Z))
                            {
                                throw new ProgramException($"Content probe produced non-finite position: {scenario.Room}/{scenario.Mode}/{frame}.");
                            }
                            if (player.Health > 0) { spawned |= 1 << player.SlotIndex; }
                        }
                    }
                }
                finally { scene.CloseHeadless(); }
                if (spawned == 0xFF && objectives) { supported.Add(scenario); }
                else if (discover)
                {
                    Console.WriteLine($"[server-content] unsupported configuration: {scenario.Room}/{scenario.Mode} (spawned=0x{spawned:X2}, objectives={objectives})");
                }
                else
                {
                    throw new ProgramException($"Content probe failed: {scenario.Room}/{scenario.Mode} (spawned=0x{spawned:X2}, objectives={objectives}).");
                }
            }
            return supported.ToArray();
        }

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
                || manifest.RoomPlayerCount != NetLaunch.RoomPlayerCount
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

        private static string ResolveDirectory(string directory)
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

        private static bool IsWithin(string root, string path)
        {
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison)
                || String.Equals(root, path, comparison);
        }

        private static void CheckRelativePath(string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || relative.Length > 240
                || relative.Any(c => c is '\\' or ':' || Char.IsControl(c))
                || relative.Split('/').Any(part => part is "" or "." or "..")
                || Path.IsPathRooted(relative))
            {
                throw new ProgramException("Invalid relative server content path.");
            }
        }

        private static string SafeFile(string root, string relative)
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

        private static string HashFile(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
