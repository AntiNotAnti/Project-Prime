using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen
{
    // Explicit map preparation capability shared by platform composition and Tools.
    public static class MapPreparation
    {
        private static IMapBuildScheduler Builds => MapPlatformService.Shared.Builds;
        public static int GenerateAll(bool force = false, bool verbose = true)
        {
            int count = 0;
            foreach (MapDefinition def in CustomRooms.Definitions)
            {
                if (force || CustomRooms.NeedsGenerating(def))
                {
                    PrepareLegacyRuntime(def, force, verbose);
                    count++;
                }
            }
            return count;
        }

        public static IReadOnlyList<string> GenerateMissing()
        {
            var failures = new List<string>();
            IReadOnlyList<MapDefinition> definitions;
            try
            {
                definitions = CustomRooms.Definitions;
            }
            catch
            {
                return failures;
            }
            foreach (MapDefinition def in definitions)
            {
                try
                {
                    if (!CustomRooms.NeedsGenerating(def))
                    {
                        continue;
                    }
                    Console.WriteLine($"[mapgen] building {def.Name}");
                    PrepareLegacyRuntime(def, force: false, verbose: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[mapgen] {def.Name} could not be built: {ex.Message}");
                    failures.Add($"{def.Name}: {ex.Message}");
                }
            }
            return failures;
        }

        public static MapBuildResult Prepare(string roomName, bool force = false, bool verbose = false)
        {
            MapDefinition definition = CustomRooms.Definitions.FirstOrDefault(definition =>
                definition.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase))
                ?? throw new MapCompilationException($"Unknown custom map '{roomName}'.");
            return Prepare(definition, force, verbose);
        }

        public static MapBuildResult Prepare(MapDefinition definition, bool force = false, bool verbose = false)
            => PrepareAsync(definition, force, verbose, CancellationToken.None).GetAwaiter().GetResult();

        public static async Task<MapBuildResult> PrepareAsync(MapDefinition definition, bool force,
            bool verbose, CancellationToken cancellationToken)
        {
            MapBuildResult result = await CompileAsync(definition, force, verbose, cancellationToken)
                .ConfigureAwait(false);
            if (result.CachePath == null)
                throw new MapCompilationException($"Map {definition.Name} produced no cache path.", result.Diagnostics);
            return result;
        }

        /// <summary>
        /// Explicit compatibility path for old tools that still require
        /// AMHE1-style generated files. Client, editor, and server runtime
        /// paths compile into the content-addressed cache and mount overlays.
        /// </summary>
        public static MapBuildResult PrepareLegacyRuntime(MapDefinition definition,
            bool force = false, bool verbose = false)
        {
            MapBuildResult result = Prepare(definition, force, verbose);
            MaterializeLegacyRuntime(definition, result.CachePath!);
            return result;
        }

        public static async Task<MapBuildResult> CompileAsync(MapDefinition definition, bool force,
            bool verbose, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return await CompileAsync(MapProject.FromLegacy(definition), force, verbose,
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task<MapBuildResult> CompileAsync(MapProject project, bool force,
            bool verbose, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            string baseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash;
            MapBuildResult result = await Builds.BuildAsync(project, new MapBuildOptions
            {
                CacheDirectory = MapStoragePaths.MapCache,
                BaseContentIdentity = baseContentIdentity,
                Force = force,
                Verbose = verbose
            }, cancellationToken).ConfigureAwait(false);
            CustomRooms.PublishBuildResult(project, result);
            if (!result.Success || result.CachePath == null)
                throw new MapCompilationException(
                    result.Diagnostics.FirstOrDefault(d => d.Severity == MapDiagnosticSeverity.Error)?.Message
                        ?? $"Map {project.Map.Name} could not be compiled.", result.Diagnostics);
            return result;
        }

        public static async Task<MatchContentSnapshot> CompileAndMountAsync(MapDefinition definition,
            string gameplayIdentity, CancellationToken cancellationToken)
        {
            return await CompileAndMountAsync(MapProject.FromLegacy(definition), gameplayIdentity,
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task<MatchContentSnapshot> CompileAndMountAsync(MapProject project,
            string gameplayIdentity, CancellationToken cancellationToken)
        {
            MapBuildResult result = await CompileAsync(project, force: false, verbose: false,
                cancellationToken).ConfigureAwait(false);
            return ContentEnvironment.MountMap(result.ContentIdentity!, result.BuildFingerprint,
                result.CachePath!, project.Map, gameplayIdentity);
        }

        /// <summary>Compiles and mounts one cataloged map; base-game rooms return null.</summary>
        public static async Task<MatchContentSnapshot?> CompileAndMountRoomAsync(string roomName,
            string gameplayIdentity, CancellationToken cancellationToken)
        {
            InstalledMap? installed = CustomRooms.Find(roomName);
            if (installed == null) return null;
            return await CompileAndMountAsync(installed.Project, gameplayIdentity, cancellationToken)
                .ConfigureAwait(false);
        }

        private static void MaterializeLegacyRuntime(MapDefinition definition, string cachePath)
        {
            string prefix = definition.Name.ToLowerInvariant();
            (string Source, string Destination)[] files =
            {
                ("Model.bin", Path.Combine(CustomRooms.ArchiveDirectory(definition), $"{prefix}_Model.bin")),
                ("Anim.bin", Path.Combine(CustomRooms.ArchiveDirectory(definition), $"{prefix}_Anim.bin")),
                ("Collision.bin", Path.Combine(CustomRooms.ArchiveDirectory(definition), $"{prefix}_Collision.bin")),
                ("Ent.bin", Path.Combine(CustomRooms.EntityDirectory(), $"{prefix}_Ent.bin")),
                ("Node.bin", Path.Combine(CustomRooms.NodeDirectory(), $"{prefix}_Node.bin"))
            };
            foreach ((string sourceName, string destination) in files)
            {
                string source = Path.Combine(cachePath, sourceName);
                byte[] bytes = File.ReadAllBytes(source);
                if (File.Exists(destination) && MapJson.Sha256(File.ReadAllBytes(destination)) == MapJson.Sha256(bytes))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temporary = destination + ".map-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temporary, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
    }
}
