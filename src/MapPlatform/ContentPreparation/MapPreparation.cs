using System;
using System.Collections.Immutable;
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

        /// <summary>
        /// The single content gate for entering a room. A base room, an exact
        /// custom-map mount, and every failure state are deliberately distinct.
        /// </summary>
        public static async Task<RoomContentPreparationResult> PrepareRoomAsync(
            RoomContentRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            InstalledMap? installed;
            if (request.RequiredMap is { } requirement)
            {
                installed = CustomRooms.Catalog.Snapshot.Find(
                    requirement.ContentIdentity);
                if (installed == null)
                {
                    await CustomRooms.RefreshAsync(cancellationToken)
                        .ConfigureAwait(false);
                    installed = CustomRooms.Catalog.Snapshot.Find(
                        requirement.ContentIdentity);
                }
                if (installed == null)
                    return Missing(request, "MAP-RUN-003",
                        "The exact map required by this match is not installed.",
                        "Download the required map and retry.");
                if (!installed.Project.Map.Name.Equals(request.RoomKey,
                    StringComparison.OrdinalIgnoreCase))
                    return Invalid(request, "MAP-RUN-008",
                        $"Installed map room key '{installed.Project.Map.Name}' does not match requested room '{request.RoomKey}'.",
                        "Remove the incoherent package or correct the server map configuration.");
                if (requirement.ArtifactHash is { } artifactHash
                    && (!String.Equals(installed.ArtifactHash, artifactHash,
                            StringComparison.OrdinalIgnoreCase)
                        || installed.PackageSize != requirement.PackageSize))
                    return Invalid(request, "MAP-RUN-007",
                        "The installed package does not match the required artifact identity.",
                        "Download the exact required map package again.");
            }
            else
            {
                installed = CustomRooms.Find(request.RoomKey);
                if (installed == null)
                {
                    RuntimeRoomRegistration? room =
                        Metadata.GetRuntimeRoomByName(request.RoomKey,
                            contentIdentity: null);
                    return room is { IsCustom: false }
                        ? new RoomContentPreparationResult.BaseGame(room)
                        : Missing(request, "MAP-RUN-003",
                            "The requested custom map is not installed.",
                            "Install the map package or choose an installed room.");
                }
            }

            if (installed.BuildState == MapBuildState.Unsupported)
                return new RoomContentPreparationResult.Unsupported(request.RoomKey,
                    installed.Diagnostics.IsDefaultOrEmpty
                        ? [Diagnostic("MAP-RUN-009", request.RoomKey,
                            "The map requires unsupported compiler or mode features.",
                            "Open the map in a compatible Project Prime editor.")]
                        : installed.Diagnostics);
            if (installed.BuildState is MapBuildState.Invalid
                or MapBuildState.MissingDependency)
                return new RoomContentPreparationResult.Invalid(request.RoomKey,
                    installed.Diagnostics.IsDefaultOrEmpty
                        ? [Diagnostic(installed.BuildState == MapBuildState.MissingDependency
                                ? "MAP-RUN-005" : "MAP-RUN-004",
                            request.RoomKey,
                            installed.BuildState == MapBuildState.MissingDependency
                                ? "Required map source or compiled content is missing."
                                : "The map is invalid and cannot be prepared.",
                            "Open the map details or editor diagnostics and repair the reported issue.")]
                        : installed.Diagnostics);

            RuntimeRoomRegistration registration;
            try
            {
                registration = CustomRooms.ActivateRuntimeRoom(installed);
                MatchContentSnapshot snapshot = await CompileAndMountAsync(
                    installed.Project, request.GameplayIdentity, cancellationToken)
                    .ConfigureAwait(false);
                if (request.RequiredMap?.MatchContentHash is { } expectedMatchHash
                    && !snapshot.MatchContentIdentity.Equals(expectedMatchHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ContentEnvironment.UnmountMap();
                    return Invalid(request, "MAP-RUN-007",
                        "Compiled map content does not match the authoritative match identity.",
                        "Verify the client build and download the exact required map again.");
                }
                if (registration.RuntimeId < RuntimeRoomRegistry.FirstCustomRoomId
                    || registration.Metadata.Id != registration.RuntimeId
                    || !registration.RoomKey.Equals(installed.Project.Map.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ContentEnvironment.UnmountMap();
                    return Invalid(request, "MAP-RUN-008",
                        "Runtime room metadata does not match its registration.",
                        "Refresh the map catalog and rebuild the map.");
                }
                return new RoomContentPreparationResult.Ready(registration,
                    snapshot);
            }
            catch (MapRuntimeException exception)
            {
                return Invalid(request, exception.Code,
                    exception.DiagnosticMessage,
                    "Refresh the map catalog and retry.");
            }
            catch (MapCompilationException exception)
            {
                ImmutableArray<MapDiagnostic> diagnostics =
                    exception.Diagnostics.Count == 0
                        ? [Diagnostic("MAP-RUN-004", request.RoomKey,
                            exception.Message,
                            "Open the map build diagnostics and repair the reported issue.")]
                        : [.. exception.Diagnostics];
                return new RoomContentPreparationResult.Invalid(request.RoomKey,
                    diagnostics);
            }
        }

        /// <summary>
        /// Converts a completed preparation result at a UI/runtime boundary.
        /// The preparation API itself remains structured; callers that are
        /// about to create a Scene may use this to fail before any room data
        /// is loaded while retaining every diagnostic on the exception.
        /// </summary>
        public static RuntimeRoomRegistration RequirePreparedRoom(
            RoomContentPreparationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            return result switch
            {
                RoomContentPreparationResult.BaseGame ready => ready.Room,
                RoomContentPreparationResult.Ready ready => ready.Room,
                RoomContentPreparationResult.Failure failure
                    => throw new MapCompilationException(
                        failure.Diagnostics.FirstOrDefault() is { } diagnostic
                            ? $"{diagnostic.Code}: {diagnostic.Message}"
                            : $"Room '{failure.RoomKey}' could not be prepared.",
                        failure.Diagnostics),
                _ => throw new InvalidOperationException(
                    "Unknown room content preparation result.")
            };
        }

        /// <summary>Compatibility wrapper; new Scene entry must use <see cref="PrepareRoomAsync"/>.</summary>
        [Obsolete("Use PrepareRoomAsync so built-in, missing, invalid, and ready states remain explicit.")]
        public static async Task<MatchContentSnapshot?> CompileAndMountRoomAsync(string roomName,
            string gameplayIdentity, CancellationToken cancellationToken)
        {
            RoomContentPreparationResult result = await PrepareRoomAsync(new(
                roomName, null, gameplayIdentity, RoomContentPurpose.Inspection),
                cancellationToken).ConfigureAwait(false);
            return result switch
            {
                RoomContentPreparationResult.BaseGame => null,
                RoomContentPreparationResult.Ready ready => ready.Snapshot,
                RoomContentPreparationResult.Failure failure
                    => throw new MapCompilationException(
                        failure.Diagnostics.FirstOrDefault() is { } diagnostic
                            ? $"{diagnostic.Code}: {diagnostic.Message}"
                            : $"Room '{failure.RoomKey}' could not be prepared.",
                        failure.Diagnostics),
                _ => throw new InvalidOperationException(
                    "Unknown room content preparation result.")
            };
        }

        private static RoomContentPreparationResult.Missing Missing(
            RoomContentRequest request, string code, string message,
            string suggestedAction)
            => new(request.RoomKey, request.RequiredMap,
                [Diagnostic(code, request.RoomKey, message, suggestedAction)]);

        private static RoomContentPreparationResult.Invalid Invalid(
            RoomContentRequest request, string code, string message,
            string suggestedAction)
            => new(request.RoomKey,
                [Diagnostic(code, request.RoomKey, message, suggestedAction)]);

        private static MapDiagnostic Diagnostic(string code, string roomKey,
            string message, string suggestedAction)
            => new(code, MapDiagnosticSeverity.Error, message,
                ObjectId: roomKey, SuggestedAction: suggestedAction);

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
