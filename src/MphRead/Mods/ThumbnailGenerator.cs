using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MphRead.Mods
{
    /// <summary>
    /// Builds map preview images by rendering each room, rather than
    /// extracting them.
    ///
    /// The original game has no map thumbnails to extract: its multiplayer
    /// select screen lists map *names*, so nothing image-shaped was ever
    /// shipped for them. (files/&lt;version&gt;/stage/*_Model.bin are the rooms'
    /// 3D models, not previews.) Rendering the real geometry gives a more
    /// useful picture anyway.
    ///
    /// Everything here is local: previews are generated from the user's own
    /// extracted files into a cache directory that is git-ignored. No game
    /// asset is ever committed or redistributed.
    /// </summary>
    public static class ThumbnailGenerator
    {
        // High enough that downscaling in a launcher still looks sharp, but
        // small enough to fit inside a 1080p desktop work area: GLFW clamps a
        // window to what the display can hold, and a clamped window renders
        // smaller than the requested target, which is what produced black
        // bands on two edges. Override with -size when the display allows it.
        public const int ThumbnailWidth = 1600;
        public const int ThumbnailHeight = 900;

        /// <summary>
        /// Cache directory, beside the game files so it travels with the
        /// install. That is beside the executable everywhere but Android, where
        /// the package's own directory is read-only -- see GameFiles.Root.
        /// </summary>
        public static string CacheDirectory =>
            Path.Combine(Launcher.GameFiles.Root, "thumbnails");

        public static string PathFor(string roomKey)
        {
            // Room keys contain spaces and occasionally punctuation; keep the
            // filename recoverable but filesystem-safe.
            string safe = string.Concat(roomKey.Select(c =>
                char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_'));
            return Path.Combine(CacheDirectory, $"{safe}.png");
        }

        public static bool Exists(string roomKey)
        {
            // Zero bytes is what a failed encode leaves behind -- the file was
            // created before the encoder threw -- and counting it as a preview
            // is how a room gets skipped for ever after one bad run.
            var file = new FileInfo(PathFor(roomKey));
            return file.Exists && file.Length > 0;
        }

        // Bump only when the generated environment composition changes.
        // Custom-map authored previews do not need this marker; it is used by
        // the RmlUi menu stage to know whether a locally rendered room image
        // predates the clean no-player/no-pickup presentation pass.
        public const int CinematicPresentationVersion = 3;

        public static string CinematicMarkerFor(string roomKey)
            => PathFor(roomKey) + ".cinematic";

        public static bool HasCinematicPresentation(string roomKey)
        {
            if (!Exists(roomKey))
                return false;
            try
            {
                return File.Exists(CinematicMarkerFor(roomKey))
                    && String.Equals(File.ReadAllText(CinematicMarkerFor(roomKey)).Trim(),
                        CinematicPresentationVersion.ToString(),
                        StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal static void MarkCinematicPresentation(string roomKey)
        {
            EnsureCacheDirectory();
            File.WriteAllText(CinematicMarkerFor(roomKey),
                CinematicPresentationVersion.ToString());
        }

        internal static void InvalidateCinematicPresentation(string roomKey)
        {
            try
            {
                string marker = CinematicMarkerFor(roomKey);
                if (File.Exists(marker)) File.Delete(marker);
            }
            catch
            {
                // The preview itself remains usable; this only means a later
                // session may ask the worker to refresh it again.
            }
        }

        /// <summary>
        /// Multiplayer rooms a launcher would offer.
        ///
        /// First Hunt rooms ("Level MP1", "E3 level", ...) are excluded
        /// unless that game's files were extracted too: they live in a
        /// separate archive set, and asking for one without it fails at load
        /// rather than producing a picture. Skipping them up front keeps a
        /// normal MPH-only install from reporting failures it cannot fix.
        /// </summary>
        public static IReadOnlyList<string> MultiplayerRooms()
        {
            bool firstHuntAvailable = FirstHuntAvailable();
            var rooms = new List<string>();
            foreach (KeyValuePair<string, RoomMetadata> entry in Metadata.RoomMetadata)
            {
                if (!entry.Value.Multiplayer)
                {
                    continue;
                }
                if (entry.Value.FirstHunt && !firstHuntAvailable)
                {
                    continue;
                }
                if (!HasPlayerSpawn(entry.Key, entry.Value))
                {
                    continue;
                }
                rooms.Add(entry.Key);
            }
            rooms.Sort(StringComparer.OrdinalIgnoreCase);
            return rooms;
        }

        private static readonly Dictionary<string, bool> _spawnCache =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a match in this room would have anywhere to put anybody.
        ///
        /// The six Biodefense Chambers carry the multiplayer flag and no
        /// entity file at all: no player spawns, so a match there strands
        /// everybody at the origin and a preview is a picture of somewhere
        /// nobody can go. They are dropped from the room list itself rather
        /// than from any one screen, so the launcher, the map grid, the host
        /// menu, -rooms and the previews all lose them together.
        ///
        /// Only the entity file is read -- no models, no collision, no GL.
        /// </summary>
        private static bool HasPlayerSpawn(string roomKey, RoomMetadata meta)
        {
            if (_spawnCache.TryGetValue(roomKey, out bool known))
            {
                return known;
            }
            bool found = false;
            try
            {
                if (meta.EntityPath != null)
                {
                    int layerId = Metadata.GetMultiplayerEntityLayer(GameMode.Battle,
                        Network.NetLaunch.RoomPlayerCount);
                    foreach (Entity entity in Read.GetEntities(meta.EntityPath, layerId, meta.FirstHunt))
                    {
                        if (entity.Type != EntityType.PlayerSpawn && entity.Type != EntityType.FhPlayerSpawn)
                        {
                            continue;
                        }
                        if (((Entity<PlayerSpawnEntityData>)entity).Data.Active != 0)
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // A reader that cannot answer must not delist a room that may
                // be perfectly playable: a bad match is worse than a missing
                // one only until the launcher has no maps left in it.
                Console.WriteLine($"[rooms] could not read the spawns in {roomKey}: {ex.Message}");
                found = true;
            }
            _spawnCache[roomKey] = found;
            return found;
        }

        /// <summary>True when First Hunt's extracted files are present.</summary>
        public static bool FirstHuntAvailable()
        {
            try
            {
                string path = Paths.FhFileSystem;
                return !string.IsNullOrEmpty(path) && Directory.Exists(path);
            }
            catch
            {
                // Paths throws when no FH version is configured at all.
                return false;
            }
        }

        public static IReadOnlyList<string> MissingThumbnails()
        {
            return MultiplayerRooms().Where(r => !Exists(r)).ToList();
        }

        /// <summary>
        /// Renders the previews a custom map is missing, at startup.
        ///
        /// The sweep that renders every room runs once, when the game files
        /// are first set up. A custom map arrives later than that by
        /// definition -- somebody drops a file into maps/ on an install that
        /// was set up months ago -- so it had no picture and no way to get one
        /// but the menu entry nobody knows to press. There are never many, and
        /// only the ones that are missing are rendered, so the usual cost of
        /// this is nothing at all.
        /// </summary>
        public static void EnsureCustomPreviews(Action<string>? report = null,
            CancellationToken cancel = default)
        {
            foreach (var definition in MapGen.CustomRooms.Definitions)
            {
                cancel.ThrowIfCancellationRequested();
                var preview = definition.Assets.FirstOrDefault(a => a.Kind == "preview");
                if (preview == null) continue;
                try
                {
                    string destination = PathFor(definition.Name);
                    if (definition.BundlePath != null)
                    {
                        string marker = destination + ".source";
                        string fingerprint = PreviewFingerprint(definition.BundlePath, preview.Path);
                        if (Exists(definition.Name) && File.Exists(marker)
                            && String.Equals(File.ReadAllText(marker), fingerprint,
                                StringComparison.Ordinal))
                            continue;

                        byte[] bytes = MapGen.MapPackageReader.ReadCatalogEntry(
                            definition.BundlePath, preview.Path, 32L * 1024 * 1024)
                            ?? throw new InvalidDataException("Packaged preview is missing.");
                        ValidatePreview(bytes);
                        MapGen.AtomicFile.Write(destination, bytes);
                        MapGen.AtomicFile.Write(marker, Encoding.UTF8.GetBytes(fingerprint));
                    }
                    else
                    {
                        byte[] bytes = MapGen.MapAssets.Read(definition, preview.Path);
                        ValidatePreview(bytes);
                        MapGen.AtomicFile.Write(destination, bytes);
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException
                    or UnauthorizedAccessException)
                {
                    Console.WriteLine("[map] Preview unavailable: " + ex.Message);
                }
            }
            if (!Launcher.GameFiles.Ready || !ThumbnailBatch.CanRun)
            {
                return;
            }
            List<string> missing;
            try
            {
                var custom = MapGen.CustomRooms.Definitions.Select(d => d.Name).ToHashSet();
                missing = MissingThumbnails().Where(r => custom.Contains(r)).ToList();
            }
            catch
            {
                return;
            }
            if (missing.Count == 0)
            {
                return;
            }
            cancel.ThrowIfCancellationRequested();
            report ??= line => Console.WriteLine($"  {line}");
            report($"Rendering {missing.Count} custom map preview(s)...");
            try
            {
                ThumbnailBatch.Run(missing, ThumbnailBatch.DefaultParallelism,
                    ThumbnailWidth, ThumbnailHeight, report, cancel: cancel);
            }
            catch (OperationCanceledException)
            {
                report("preview generation cancelled during shutdown");
                throw;
            }
            catch (Exception ex)
            {
                // a map with no picture is a cosmetic problem; it must not
                // stand between the player and the front screen
                report($"could not render: {ex.Message}");
            }
        }

        private static string PreviewFingerprint(string package, string entry)
        {
            var info = new FileInfo(package);
            return $"{Path.GetFullPath(package)}\n{info.Length}\n"
                + $"{info.LastWriteTimeUtc.Ticks}\n{entry}";
        }

        private static void ValidatePreview(byte[] bytes)
        {
            if (bytes.Length < 24
                || !bytes.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
                    bytes.AsSpan(16)) is < 1 or > 4096
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
                    bytes.AsSpan(20)) is < 1 or > 4096)
                throw new InvalidDataException(
                    "Preview must be a PNG no larger than 4096×4096.");
        }

        public static void EnsureCacheDirectory()
        {
            Directory.CreateDirectory(CacheDirectory);
        }
    }
}
