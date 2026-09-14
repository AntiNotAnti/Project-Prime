using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

public sealed class MapCompiler
{
    public const int CompilerSchemaVersion = MapCompilerSchema.Current;
    private const int TotalStages = 12;
    private static readonly string[] RuntimeFiles =
        ["Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin"];
    // Publication is the only serialized portion of compilation. Stripes keep
    // concurrently compiled maps independent without retaining one lock per
    // content hash for the lifetime of the process.
    private static readonly object[] PublishLocks = Enumerable.Range(0, 64)
        .Select(_ => new object()).ToArray();
    private readonly MapValidator _validator;

    public MapCompiler(MapValidator? validator = null) => _validator = validator ?? new MapValidator();

    public MapBuildResult Compile(MapProject project, MapBuildOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = new MapDiagnosticBag();
        var timings = new List<MapStageTiming>();
        string fingerprint = "";
        string? temporary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(options, "Validating", 0);
            ImmutableArray<MapDiagnostic> sourceDiagnostics = Timed("Validate Source", timings,
                () => _validator.ValidateProject(project));
            diagnostics.AddRange(sourceDiagnostics);
            if (diagnostics.HasErrors)
                return Failure(fingerprint, diagnostics, timings,
                    CompilationFailureKinds.FromDiagnostics(sourceDiagnostics));

            Report(options, "Resolving dependencies", 1);
            MapDependencyAnalysis dependencies = Timed("Resolve Dependencies", timings,
                () => MapDependencyAnalyzer.Analyze(project));
            diagnostics.AddRange(dependencies.Diagnostics);
            if (diagnostics.HasErrors)
                return Failure(fingerprint, diagnostics, timings,
                    CompilationFailureKind.MissingDependency);
            Report(options, "Fingerprinting", 2);
            fingerprint = Timed("Fingerprint", timings,
                () => MapBuildFingerprint.Compute(project, options.BaseContentIdentity));
            string cacheRoot = Path.GetFullPath(options.CacheDirectory);
            string destination = Path.Combine(cacheRoot, fingerprint);
            if (!options.Force && TryReadValidCache(destination, fingerprint, out MapBuildMetadata? cached))
            {
                diagnostics.AddRange(cached.Diagnostics);
                Report(options, "Complete", TotalStages);
                return new MapBuildResult(true, true, fingerprint, destination, cached.SourceIdentity,
                    diagnostics.ToImmutable(), cached.Statistics, [.. timings]);
            }

            // Imported legacy maps use a detached MapDefinition while they
            // derive runtime spawns, jump pads, items, and a preview. Capture
            // the logical source identity before that importer-owned mutation;
            // otherwise the first build publishes an identity for generated
            // state rather than for the source the author/package declared.
            // Packaged maps carry an authoritative manifest identity and must
            // retain it exactly.
            MapContentIdentity sourceIdentity = project.DeclaredContentIdentity
                ?? new MapContentIdentity(project.Identity, MapProjectContentHasher.Compute(project));

            cancellationToken.ThrowIfCancellationRequested();
            Report(options, "Importing geometry", 3);
            MapBuildScene scene = Timed("Import", timings, () =>
                (project.Authoring == null ? (IMapImporter)new LegacyMapImporter() : new NativeMapProjectImporter())
                    .Import(project, options.Verbose));
            scene.DependencyAnalysis = dependencies;
            cancellationToken.ThrowIfCancellationRequested();
            Report(options, "Normalizing", 4);
            Timed("Normalize", timings, () => ValidateSceneShape(scene));
            Report(options, "Optimizing geometry", 5);
            Timed("Optimize Geometry", timings, () => true); // Current importers already emit normalized shared faces.

            cancellationToken.ThrowIfCancellationRequested();
            int packedStage = 6;
            MapPackedContent content = MapPacker.Compile(scene, timing =>
            {
                timings.Add(timing);
                packedStage++;
            }, cancellationToken, stage => Report(options, stage, packedStage));
            Report(options, "Validating runtime content", 10);
            ImmutableArray<MapDiagnostic> budgetDiagnostics = Timed("Validate Runtime Content", timings,
                () => _validator.ValidateStatistics(content.Statistics));
            diagnostics.AddRange(budgetDiagnostics);
            if (diagnostics.HasErrors)
                return Failure(fingerprint, diagnostics, timings,
                    CompilationFailureKind.FormatLimit);

            Directory.CreateDirectory(cacheRoot);
            temporary = Path.Combine(cacheRoot, ".build-" + fingerprint + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            Report(options, "Publishing cache", 11);
            Timed("Publish Cache", timings, () =>
            {
                Write(Path.Combine(temporary, RuntimeFiles[0]), content.Model);
                Write(Path.Combine(temporary, RuntimeFiles[1]), content.Animation);
                Write(Path.Combine(temporary, RuntimeFiles[2]), content.Collision);
                Write(Path.Combine(temporary, RuntimeFiles[3]), content.Entities);
                Write(Path.Combine(temporary, RuntimeFiles[4]), content.Nodes);
                return true;
            });

            var metadata = new MapBuildMetadata
            {
                CompilerSchemaVersion = CompilerSchemaVersion,
                CompilerVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "development",
                BuildFingerprint = fingerprint,
                SourceIdentity = sourceIdentity,
                GeneratedFiles = RuntimeFiles.Select(name =>
                {
                    string path = Path.Combine(temporary, name);
                    byte[] bytes = File.ReadAllBytes(path);
                    return new MapGeneratedFile(name, bytes.LongLength, MapJson.Sha256(bytes));
                }).ToList(),
                Statistics = content.Statistics,
                Diagnostics = diagnostics.ToImmutable().ToList(),
                Timings = timings.ToList()
            };
            byte[] buildJson = JsonSerializer.SerializeToUtf8Bytes(metadata, MapJsonContext.Default.MapBuildMetadata);
            Write(Path.Combine(temporary, "build.json"), buildJson);
            cancellationToken.ThrowIfCancellationRequested();

            lock (PublishLock(fingerprint))
            {
                if (Directory.Exists(destination))
                {
                    if (TryReadValidCache(destination, fingerprint, out MapBuildMetadata? concurrentlyBuilt))
                    {
                        Directory.Delete(temporary, recursive: true);
                        temporary = null;
                        return new MapBuildResult(true, true, fingerprint, destination,
                            concurrentlyBuilt.SourceIdentity, diagnostics.ToImmutable(), concurrentlyBuilt.Statistics, [.. timings]);
                    }
                    string invalid = destination + ".invalid-" + Guid.NewGuid().ToString("N");
                    Directory.Move(destination, invalid);
                    try { Directory.Move(temporary, destination); temporary = null; }
                    finally { if (Directory.Exists(invalid)) Directory.Delete(invalid, recursive: true); }
                }
                else
                {
                    try { Directory.Move(temporary, destination); temporary = null; }
                    catch (IOException) when (TryReadValidCache(destination, fingerprint, out _))
                    {
                        // Another process won the same content-addressed
                        // publish race. Its fully validated cache is the same
                        // result; our private temporary tree can be discarded.
                        Directory.Delete(temporary!, recursive: true);
                        temporary = null;
                        return new MapBuildResult(true, true, fingerprint, destination, sourceIdentity,
                            diagnostics.ToImmutable(), content.Statistics, [.. timings]);
                    }
                }
            }
            Report(options, "Complete", TotalStages);
            return new MapBuildResult(true, false, fingerprint, destination, sourceIdentity,
                diagnostics.ToImmutable(), content.Statistics, [.. timings]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MapCompilationException exception)
        {
            diagnostics.AddRange(exception.Diagnostics);
            if (!exception.Diagnostics.Any())
                diagnostics.Add(new("MAP-CMP-001", MapDiagnosticSeverity.Error, exception.Message,
                    SourcePath: project.SourcePath));
            CompilationFailureKind kind = exception is MapDependencyException
                ? CompilationFailureKind.MissingDependency
                : CompilationFailureKinds.FromDiagnostics(exception.Diagnostics);
            return Failure(fingerprint, diagnostics, timings, kind);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("MAP-CMP-002", MapDiagnosticSeverity.Error,
                exception.Message, SourcePath: project.SourcePath,
                SuggestedAction: "Retry the build after checking file access and available storage."));
            return Failure(fingerprint, diagnostics, timings, CompilationFailureKind.IOFailure);
        }
        catch (Exception exception)
        {
            if (options.Verbose) Console.Error.WriteLine(exception);
            diagnostics.Add(new("MAP-CMP-999", MapDiagnosticSeverity.Error,
                exception.Message, SourcePath: project.SourcePath,
                SuggestedAction: "See the full compiler log for the retained exception details."));
            return Failure(fingerprint, diagnostics, timings, CompilationFailureKind.InternalError);
        }
        finally
        {
            if (temporary != null && Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    public static bool TryReadValidCache(string directory, string fingerprint,
        out MapBuildMetadata metadata)
    {
        metadata = null!;
        try
        {
            string buildPath = Path.Combine(directory, "build.json");
            if (!File.Exists(buildPath)) return false;
            metadata = JsonSerializer.Deserialize(File.ReadAllBytes(buildPath), MapJsonContext.Default.MapBuildMetadata)!;
            if (metadata == null || metadata.CompilerSchemaVersion != CompilerSchemaVersion
                || metadata.BuildFingerprint != fingerprint || metadata.GeneratedFiles.Count != RuntimeFiles.Length)
                return false;
            foreach (MapGeneratedFile file in metadata.GeneratedFiles)
            {
                if (!RuntimeFiles.Contains(file.Path, StringComparer.Ordinal)) return false;
                string path = Path.Combine(directory, file.Path);
                if (!File.Exists(path)) return false;
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.LongLength != file.Size || MapJson.Sha256(bytes) != file.Sha256) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            metadata = null!;
            return false;
        }
    }

    private static bool ValidateSceneShape(BuiltMap scene)
    {
        if (scene.Faces.Count == 0) throw new MapCompilationException("Map produces no render geometry.",
            [new("MAP-GEO-006", MapDiagnosticSeverity.Error, "Map produces no render geometry.")]);
        if (scene.Solid.Count == 0) throw new MapCompilationException("Map produces no collision geometry.",
            [new("MAP-COL-001", MapDiagnosticSeverity.Error, "Map produces no collision geometry.")]);
        if (scene.Entities.Count == 0) throw new MapCompilationException("Map produces no entities.",
            [new("MAP-ENT-001", MapDiagnosticSeverity.Error, "Map produces no entities.")]);
        return true;
    }

    private static T Timed<T>(string stage, List<MapStageTiming> timings, Func<T> action)
    {
        long start = Stopwatch.GetTimestamp();
        try { return action(); }
        finally { timings.Add(new(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds)); }
    }

    private static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static object PublishLock(string fingerprint)
        => PublishLocks[Convert.ToInt32(fingerprint[..2], 16) % PublishLocks.Length];

    private static MapBuildResult Failure(string fingerprint, MapDiagnosticBag diagnostics,
        List<MapStageTiming> timings, CompilationFailureKind kind)
        => new(false, false, fingerprint, null, null, diagnostics.ToImmutable(), null,
            [.. timings], kind);

    private static void Report(MapBuildOptions options, string stage, int completedStages)
        => options.Progress?.Report(new(stage, completedStages, TotalStages));
}
