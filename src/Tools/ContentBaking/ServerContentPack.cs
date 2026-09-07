using static MphRead.Mods.Network.ServerContentPackage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.Network
{
    public static class ServerContentPack
    {
        public const string ManifestName = ServerContentPackage.ManifestName;
        public const string Profile = ServerContentPackage.Profile;
        public const string Amhe1Arm9Sha256 = ServerContentPackage.Amhe1Arm9Sha256;
        public static ServerContentManifest Validate(string directory, string expectedVersion)
            => ServerContentPackage.Validate(directory, expectedVersion);
        public static string[] RetailRooms => ServerContentPackage.RetailRooms;

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
            using var context = ContentEnvironment.PreserveContext(version);
            ServerContentScenario[] scenarios;
            var modelPaths = new HashSet<string>(StringComparer.Ordinal);
            var observed = new SortedDictionary<string, ServerContentFile>(StringComparer.Ordinal);
            ContentEnvironment.Open(source, version);
            using (ContentEnvironment.TraceReads((path, bytes) =>
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
                selectedRooms, probeFrames, observed.Values.ToArray(), scenarios, NetConfig.RoomPlayerCount);
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
                        byte[] projected = Formats.ServerModelData.Project(model);
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
            using var context = ContentEnvironment.PreserveContext(version);
            ContentEnvironment.Open(directory, version);
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
                        roomPlayerCount: NetConfig.RoomPlayerCount);
                    AuthoritativeContent.ValidateRoom(scene);
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

    }
}
