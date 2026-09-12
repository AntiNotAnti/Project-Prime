using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen
{
    /// <summary>
    /// Makes custom maps into rooms the rest of the game already knows how to
    /// handle: the launcher lists them, -maptest loads them, the server can
    /// run them.
    ///
    /// A map may be editable source or an installed package. Compiled binaries
    /// live in the content-addressed map cache and are exposed through a
    /// selected-map overlay; the extracted base game remains immutable.
    /// </summary>
    public static class CustomRooms
    {
        private static IReadOnlyList<MapDefinition>? _definitions;
        private static MapCatalog? _catalog;
        private static string? _catalogDirectory;
        private static bool _includeUserProjects = true;
        // Android builds the map binaries on a background thread while the
        // front screen is listing rooms on another, and both go through here.
        private static readonly object _lock = new object();

        /// <summary>
        /// Where the map files are. Beside the executable on the desktop; the
        /// Android head moves it, because the package directory there is read
        /// only and the maps have to live where the extracted game files
        /// already do. Set it before anything reads <see cref="Definitions"/>:
        /// the immutable catalog is refreshed explicitly when content changes.
        /// </summary>
        public static string ContentRoot { get; set; } = AppContext.BaseDirectory;

        private static string _mapDirectory = Path.Combine(AppContext.BaseDirectory, "maps");
        public static string MapDirectory
        {
            get => _mapDirectory;
            set => ConfigureMapDirectory(value, includeUserProjects: true);
        }

        /// <summary>
        /// Selects an explicit build input directory without also discovering
        /// per-user editor projects. Release/package tooling must be a pure
        /// function of the supplied repository directory.
        /// </summary>
        internal static void SetBuildMapDirectory(string value)
            => ConfigureMapDirectory(value, includeUserProjects: false);

        private static void ConfigureMapDirectory(string value, bool includeUserProjects)
        {
            lock (_lock)
            {
                string full = Path.GetFullPath(value);
                if (_mapDirectory == full && _includeUserProjects == includeUserProjects) return;
                _mapDirectory = full;
                _includeUserProjects = includeUserProjects;
                _definitions = null;
                _catalog?.Dispose();
                _catalog = null;
                _catalogDirectory = null;
                RuntimeRoomRegistry.Shared.Synchronize(MapCatalogSnapshot.Empty,
                    static (map, id) => MakeMetadata(map.Project.Map, id));
            }
        }

        public static IMapCatalog Catalog
        {
            get
            {
                lock (_lock)
                {
                    EnsureCatalog();
                    return _catalog!;
                }
            }
        }

        public static IReadOnlyList<MapDefinition> Definitions
        {
            get
            {
                lock (_lock)
                {
                    _definitions ??= LoadDefinitions();
                    return _definitions;
                }
            }
        }

        /// <summary>
        /// Every map file: a loose recipe, one in a folder of its own with the
        /// level it converts beside it, or a bundle, which is all of that in
        /// one file (see <see cref="MapBundle"/>).
        ///
        /// A bundle wins where both exist, and it is the same map either way:
        /// the working copy of a map is a folder with somebody's .pk3 in it,
        /// and the bundle is what that folder is cooked into to be shipped or
        /// handed out, so a checkout that has both would otherwise register
        /// the same room twice.
        /// </summary>
        private static IEnumerable<string> MapFiles()
        {
            if (!Directory.Exists(MapDirectory))
            {
                return Enumerable.Empty<string>();
            }
            var bundles = Directory.EnumerateFiles(MapDirectory, $"*{MapBundle.Extension}",
                SearchOption.AllDirectories)
                .Where(path => !IsAppleDouble(path)).ToList();
            var names = new HashSet<string>(bundles.Select(
                p => Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase);
            return bundles.Concat(Directory
                .EnumerateFiles(MapDirectory, "*.json", SearchOption.AllDirectories)
                .Where(path => !IsAppleDouble(path))
                .Where(p => !names.Contains(Path.GetFileNameWithoutExtension(p))));
        }

        private static bool IsAppleDouble(string path)
            => Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);

        private static IReadOnlyList<MapDefinition> LoadDefinitions()
        {
            EnsureCatalog();
            _catalog!.RefreshAsync().AsTask().GetAwaiter().GetResult();
            SynchronizeRuntimeRooms(_catalog.Snapshot);
            foreach (MapDiagnostic diagnostic in _catalog.Snapshot.Diagnostics)
            {
                Console.WriteLine($"Ignoring map {Path.GetFileName(diagnostic.SourcePath)}: {diagnostic.Message}");
            }
            return _catalog.Snapshot.Maps
                .Where(map => map.BuildState is MapBuildState.NeedsBuild or MapBuildState.Ready)
                .OrderByDescending(map => map.Source is MapInstallSource.InstalledPackage
                    or MapInstallSource.BundledPackage or MapInstallSource.LegacyPackage)
                .ThenBy(map => map.SourcePath, StringComparer.Ordinal)
                .Select(map => map.Project.Map)
                .Where(definition => definition.Import == null || definition.Import.Resolve() != null)
                .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(definition => definition.SourcePath, StringComparer.Ordinal)
                .ToArray();
        }

        public static async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            MapCatalog catalog;
            lock (_lock)
            {
                EnsureCatalog();
                catalog = _catalog!;
            }
            await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _definitions = BuildDefinitions(catalog.Snapshot);
                SynchronizeRuntimeRooms(catalog.Snapshot);
            }
        }

        public static async ValueTask<InstalledMap> InstallAsync(string packagePath,
            CancellationToken cancellationToken = default)
        {
            MapCatalog catalog = (MapCatalog)Catalog;
            InstalledMap map = await catalog.InstallAsync(packagePath, cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _definitions = BuildDefinitions(catalog.Snapshot);
                SynchronizeRuntimeRooms(catalog.Snapshot);
            }
            return map;
        }

        public static async ValueTask RemoveAsync(MapContentIdentity identity,
            CancellationToken cancellationToken = default)
        {
            MapCatalog catalog = (MapCatalog)Catalog;
            await catalog.RemoveAsync(identity, cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _definitions = BuildDefinitions(catalog.Snapshot);
                SynchronizeRuntimeRooms(catalog.Snapshot);
            }
        }

        private static void EnsureCatalog()
        {
            string directory = Path.GetFullPath(MapDirectory);
            if (_catalog != null && _catalogDirectory == directory) return;
            _catalog?.Dispose();
            ImmutableArray<string> projectDirectories = _includeUserProjects
                ? [MapStoragePaths.Projects, directory]
                : [directory];
            string installedDirectory = _includeUserProjects
                ? MapStoragePaths.InstalledMaps
                : Path.Combine(directory, ".project-prime-build-scope", "installed");
            _catalog = new MapCatalog(new MapCatalogOptions
            {
                InstalledDirectory = installedDirectory,
                ProjectDirectories = projectDirectories,
                CacheDirectory = _includeUserProjects ? MapStoragePaths.MapCache : null
            });
            _catalogDirectory = directory;
        }

        private static IReadOnlyList<MapDefinition> BuildDefinitions(MapCatalogSnapshot snapshot)
            => snapshot.Maps
                .Where(map => map.BuildState is MapBuildState.NeedsBuild or MapBuildState.Ready)
                .OrderByDescending(map => map.Source is MapInstallSource.InstalledPackage
                    or MapInstallSource.BundledPackage or MapInstallSource.LegacyPackage)
                .ThenBy(map => map.SourcePath, StringComparer.Ordinal)
                .Select(map => map.Project.Map)
                .Where(definition => definition.Import == null || definition.Import.Resolve() != null)
                .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(definition => definition.SourcePath, StringComparer.Ordinal)
                .ToArray();

        public static ImmutableArray<MapMode> SupportedModes(string roomName)
        {
            lock (_lock)
            {
                EnsureCatalog();
                InstalledMap? map = _catalog!.Snapshot.FindRoom(roomName);
                return map?.SupportedModes ?? [MapMode.Battle, MapMode.Survival];
            }
        }

        public static InstalledMap? Find(string roomName)
        {
            lock (_lock)
            {
                EnsureCatalog();
                if (_catalog!.Snapshot.Revision == 0)
                {
                    _catalog.RefreshAsync().AsTask().GetAwaiter().GetResult();
                    _definitions = BuildDefinitions(_catalog.Snapshot);
                    SynchronizeRuntimeRooms(_catalog.Snapshot);
                }
                return _catalog.Snapshot.FindRoom(roomName);
            }
        }

        /// <summary>Publishes a per-map compiler result without creating catalog ownership implicitly.</summary>
        public static void PublishBuildResult(MapProject project, MapBuildResult result)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(result);
            MapCatalog? catalog;
            InstalledMap? map;
            lock (_lock)
            {
                catalog = _catalog;
                if (catalog == null) return;
                map = result.ContentIdentity == null ? null
                    : catalog.Snapshot.Find(result.ContentIdentity);
                map ??= project.SourcePath == null ? null
                    : catalog.Snapshot.Maps.FirstOrDefault(candidate => candidate.SourcePath.Equals(
                        Path.GetFullPath(project.SourcePath), StringComparison.Ordinal));
            }
            if (map == null) return;
            catalog.PublishBuildState(map.ContentIdentity,
                result.Success ? MapBuildState.Ready : result.FailureKind.ToBuildState(),
                result.Statistics, result.Diagnostics);
            lock (_lock)
                if (ReferenceEquals(_catalog, catalog))
                {
                    _definitions = BuildDefinitions(catalog.Snapshot);
                    SynchronizeRuntimeRooms(catalog.Snapshot);
                }
        }

        internal static RuntimeRoomRegistry RuntimeRooms
        {
            get
            {
                lock (_lock)
                {
                    EnsureCatalog();
                    if (_catalog!.Snapshot.Revision == 0)
                        _catalog.RefreshAsync().AsTask().GetAwaiter().GetResult();
                    _definitions = BuildDefinitions(_catalog.Snapshot);
                    SynchronizeRuntimeRooms(_catalog.Snapshot);
                    return RuntimeRoomRegistry.Shared;
                }
            }
        }

        public static RuntimeRoomRegistration ActivateRuntimeRoom(InstalledMap map)
        {
            lock (_lock)
            {
                return RuntimeRoomRegistry.Shared.RegisterCustom(map,
                    static (installed, id) => MakeMetadata(installed.Project.Map, id));
            }
        }

        private static void SynchronizeRuntimeRooms(MapCatalogSnapshot snapshot)
            => RuntimeRoomRegistry.Shared.Synchronize(snapshot,
                static (map, id) => MakeMetadata(map.Project.Map, id));

        /// <summary>Compatibility view over the process-stable runtime registry.</summary>
        public static IReadOnlyDictionary<int, string> AppendIds(Dictionary<int, string> ids)
        {
            foreach (RuntimeRoomRegistration room in RuntimeRooms.Snapshot.Rooms)
            {
                if (room.IsCustom) ids.Add(room.RuntimeId, room.RoomKey);
            }
            return ids;
        }

        /// <summary>Compatibility view over the same registrations as <see cref="AppendIds"/>.</summary>
        public static IReadOnlyList<RoomMetadata> AppendRooms(List<RoomMetadata> rooms)
        {
            foreach (RuntimeRoomRegistration room in RuntimeRooms.Snapshot.Rooms)
            {
                if (room.IsCustom) rooms.Add(room.Metadata);
            }
            return rooms;
        }

        private static RoomMetadata MakeMetadata(MapDefinition def, int id)
        {
            string prefix = def.Name.ToLowerInvariant();
            return new RoomMetadata(
                id: id,
                name: def.Name,
                inGameName: def.InGameName ?? def.Name,
                archive: prefix,
                modelPath: $"{prefix}_Model.bin",
                animationPath: $"{prefix}_Anim.bin",
                collisionPath: $"{prefix}_Collision.bin",
                texturePath: null, // the textures are inside the model file
                entityPath: $"{prefix}_Ent.bin",
                // the metadata prepends levels\nodeData\ itself
                nodePath: $"{prefix}_Node.bin",
                roomNodeName: null,
                battleTimeLimit: def.BattleTimeLimit,
                timeLimit: def.BattleTimeLimit,
                pointLimit: def.PointLimit,
                nodeLayer: 0,
                fogEnabled: def.FogEnabled,
                clearFog: false,
                fogColor: ToColor(def.FogColor),
                fogSlope: def.FogSlope,
                fogOffset: (ushort)def.FogOffset,
                light1Color: ToColor(def.Light1Color),
                light1Vector: ToVector(def.Light1Vector),
                light2Color: ToColor(def.Light2Color),
                light2Vector: ToVector(def.Light2Vector),
                farClip: Fixed.ToInt(def.FarClip),
                killHeight: Fixed.ToInt(def.KillHeight),
                size: RoomSize.Large,
                // no camera or player limits: a custom map decides its own
                // extent, and a limit box inherited from someone else's room
                // is how the camera ends up stuck behind a wall
                multiplayer: true);
        }

        private static ColorRgb ToColor(int[] values)
        {
            return new ColorRgb((byte)values[0], (byte)values[1], (byte)values[2]);
        }

        private static Vector3 ToVector(float[] values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        public static string ArchiveDirectory(MapDefinition def)
        {
            return Paths.Combine(Paths.FileSystem, @"_archives", def.Name.ToLowerInvariant());
        }

        public static string EntityDirectory()
        {
            return Paths.Combine(Paths.FileSystem, @"levels\entities");
        }

        public static string NodeDirectory()
        {
            return Paths.Combine(Paths.FileSystem, @"levels\nodeData");
        }

        /// <summary>
        /// Why this room cannot be loaded, or null when it can.
        ///
        /// Only a custom map can answer with a reason. A custom room is
        /// registered from its recipe and built from it separately (see
        /// the platform map preparation step), so the case this exists for is a
        /// map that is listed -- the launcher offers it, the picker shows a
        /// frame for it -- and whose build failed. That used to be a crash
        /// the moment somebody picked it, in a process with no console to say
        /// why, which is the worst way for a bad map file to be reported.
        /// </summary>
        public static string? WhyUnplayable(string roomName)
        {
            InstalledMap? map = Find(roomName);
            if (map == null || map.BuildState == MapBuildState.Ready) return null;
            string detail = map.Diagnostics.FirstOrDefault(value =>
                value.Severity == MapDiagnosticSeverity.Error)?.Message
                ?? "The selected map has not reached Ready state.";
            return $"{map.DisplayName} is {map.BuildState}: {detail}";
        }

        public static bool NeedsGenerating(MapDefinition def)
        {
            string prefix = def.Name.ToLowerInvariant();
            string[] outputs =
            {
                Path.Combine(ArchiveDirectory(def), $"{prefix}_Model.bin"),
                Path.Combine(ArchiveDirectory(def), $"{prefix}_Anim.bin"),
                Path.Combine(ArchiveDirectory(def), $"{prefix}_Collision.bin"),
                Path.Combine(EntityDirectory(), $"{prefix}_Ent.bin"),
                Path.Combine(NodeDirectory(), $"{prefix}_Node.bin")
            };
            if (outputs.Any(path => !File.Exists(path)))
            {
                // every file a room is made of, not just the first: a build
                // from before one of them existed leaves the others in place
                // and looks up to date
                return true;
            }
            // The file it was actually loaded from -- a recipe or a bundle --
            // rather than a search for one named after the room, which a map
            // whose file is not named after its room quietly failed.
            string? source = def.SourcePath;
            return source != null && File.Exists(source)
                && outputs.Any(path => File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(path));
        }
    }
}
