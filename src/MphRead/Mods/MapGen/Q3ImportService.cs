using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>
/// GUI/CLI-neutral Quake 3 import workflow. Analysis is read-only; import is
/// transactional and never leaves a half-created map folder behind.
/// </summary>
public static class Q3ImportService
{
    public sealed record Options(
        string Source,
        string? MapName,
        string RoomName,
        string DestinationDirectory,
        float? UnitsPerUnit = null,
        bool KeepClip = true,
        bool KeepItems = true,
        bool KeepSky = true,
        bool KeepSpawns = true,
        int PatchLevel = 3,
        int TextureSize = MapTextureBake.DefaultSize,
        IReadOnlyList<string>? Dependencies = null,
        bool AutoHealCollision = true,
        float CollisionHealTolerance = 0.0625f,
        bool AllowFallbackTextures = true);

    public sealed record Analysis(
        string Source,
        string MapName,
        IReadOnlyList<string> Maps,
        float AutoScale,
        float Width,
        float Height,
        float Depth,
        int Surfaces,
        int Patches,
        int Brushes,
        int PlayerClips,
        int Spawns,
        int Pickups,
        MapTextureBake.Coverage Textures)
    {
        public IReadOnlyList<MapDiagnostic> GameplayWarnings { get; init; } = Array.Empty<MapDiagnostic>();
    }

    private sealed record PreparedAnalysis(Analysis Analysis, Q3Bsp? Bsp);

    public sealed record ReimportDiff(
        Analysis? Current, Analysis Next,
        int AuthoredGeometry, int AuthoredEntities, int AuthoredMaterials)
    {
        public string Summary()
        {
            string Delta(int? a,int b)=>a.HasValue?$"{a.Value:N0} → {b:N0}":$"? → {b:N0}";
            string currentTextures=Current==null?"?":$"{Current.Textures.Resolved}/{Current.Textures.Total}";
            return $"Q3 REIMPORT PREVIEW\n"
                +$"Map: {Next.MapName}\n"
                +$"Surfaces: {Delta(Current?.Surfaces,Next.Surfaces)}\n"
                +$"Patches: {Delta(Current?.Patches,Next.Patches)} · Brushes: {Delta(Current?.Brushes,Next.Brushes)}\n"
                +$"Spawns: {Delta(Current?.Spawns,Next.Spawns)} · Pickups: {Delta(Current?.Pickups,Next.Pickups)}\n"
                +$"Dimensions: {(Current==null?"?":$"{Current.Width:0.#} × {Current.Height:0.#} × {Current.Depth:0.#}")} → {Next.Width:0.#} × {Next.Height:0.#} × {Next.Depth:0.#}\n"
                +$"Textures resolved: {currentTextures} → {Next.Textures.Resolved}/{Next.Textures.Total} ({Next.Textures.Missing.Count} fallback)\n\n"
                +$"Preserved Project Prime authoring: {AuthoredGeometry} geometry · {AuthoredEntities} gameplay objects · {AuthoredMaterials} materials";
        }
    }

    public enum Severity { Info, Warning, Error }
    public sealed record Diagnostic(Severity Severity, string Message);
    public sealed record Result(bool Succeeded, string? ProjectPath, Analysis? Analysis,
        IReadOnlyList<Diagnostic> Diagnostics);

    public static Analysis Analyze(string source, string? mapName = null,
        IReadOnlyList<string>? dependencies = null, float? scale = null,
        CancellationToken cancellation = default)
        => AnalyzeCore(source, mapName, dependencies, scale, cancellation).Analysis;

    private static PreparedAnalysis AnalyzeCore(string source, string? mapName = null,
        IReadOnlyList<string>? dependencies = null, float? scale = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (scale.HasValue && (!float.IsFinite(scale.Value) || scale.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(scale), "Import scale must be finite and positive.");
        if (!File.Exists(source)) throw new FileNotFoundException("Quake 3 source was not found.", source);
        var maps = Q3Bsp.ListMaps(source);
        if (maps.Count == 0) throw new ProgramException($"{Path.GetFileName(source)} contains no BSP levels.");
        string selected = mapName ?? maps[0];
        if (!maps.Any(m => m.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            throw new ProgramException($"{Path.GetFileName(source)} has no map {selected}.");
        var bsp = Q3Bsp.Load(source, selected, cancellation);
        float auto = Q3Convert.AutoScale(Q3Convert.WidestExtent(bsp));
        float unit = scale ?? auto;
        Q3Convert.Bounds(bsp, out float[] min, out float[] max, sky: false);
        if (min[0] > max[0]) throw new ProgramException("This BSP has no drawn architecture to import.");
        if (min.Concat(max).Any(value => !float.IsFinite(value / unit) || Math.Abs(value / unit) >= 524288))
            throw new ArgumentOutOfRangeException(nameof(scale), "Scaled map coordinates exceed Prime's runtime range. Increase Quake units per Prime unit.");
        var archives = MapTextureBake.DiscoverArchives(source, dependencies);
        var coverage = MapTextureBake.Analyze(bsp, archives);
        int clips = bsp.Brushes.Count(b =>
            (bsp.Textures[b.Texture].Contents & Q3Bsp.ContentsSolid) == 0
            && (bsp.Textures[b.Texture].Contents & Q3Bsp.ContentsPlayerClip) != 0);
        int spawns = Q3Gameplay.Spawns(bsp, unit).Count;
        int patches = bsp.Faces.Count(f => f.Type == 2);
        int pickups = Q3Import.Pickups(bsp, unit).Count();
        var analysis = new Analysis(source, selected, maps, auto,
            (max[0] - min[0]) / unit,
            (max[2] - min[2]) / unit,
            (max[1] - min[1]) / unit,
            bsp.Faces.Count, patches, bsp.Brushes.Count, clips, spawns, pickups, coverage)
        { GameplayWarnings = Q3Gameplay.Inspect(bsp, unit) };
        return new PreparedAnalysis(analysis, bsp);
    }

    public static ReimportDiff PreviewReimport(MapDefinition existing,string source,string? mapName=null,
        IReadOnlyList<string>? dependencies=null,CancellationToken cancellation=default)
    {
        if(existing.Import==null)throw new InvalidOperationException("This project is not a Q3 import.");
        Analysis? current=null;
        string? currentSource=existing.Import.Resolve();
        if(currentSource!=null)
        {
            try{current=Analyze(currentSource,existing.Import.MapName,null,existing.Import.UnitsPerUnit,cancellation);}
            catch(Exception ex) when(ex is IOException or InvalidDataException or ProgramException or ArgumentException){ }
        }
        Analysis next=Analyze(source,mapName??existing.Import.MapName,dependencies,existing.Import.UnitsPerUnit,cancellation);
        int authoredGeometry=existing.Geometry.Count+existing.Brushes.Count;
        int authoredEntities=existing.Spawns.Count+existing.Items.Count+existing.JumpPads.Count+existing.NavigationLinks.Count;
        return new(current,next,authoredGeometry,authoredEntities,existing.Materials.Count);
    }

    public static Result Import(Options options, CancellationToken cancellation = default,
        Action<string>? progress = null)
    {
        var diagnostics = new List<Diagnostic>();
        string destination = Path.GetFullPath(options.DestinationDirectory);
        string parent = Path.GetDirectoryName(destination)
            ?? throw new IOException("Import destination has no parent directory.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, "." + Path.GetFileName(destination)
            + ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            MapValidator.RequireRuntimeName(options.RoomName);
            if (Directory.Exists(destination))
                throw new IOException("A map folder already has this name. Choose a new name or use Reimport.");
            if (options.PatchLevel is < 1 or > 8)
                throw new ArgumentOutOfRangeException(nameof(options.PatchLevel), "Patch detail must be 1-8.");

            progress?.Invoke("Analyzing Quake 3 source…");
            IReadOnlyList<string> archives = MapTextureBake.DiscoverArchives(options.Source, options.Dependencies);
            string selectedMap = options.MapName ?? Q3Bsp.ListMaps(options.Source).FirstOrDefault()
                ?? throw new InvalidDataException("Quake source contains no BSP levels.");
            string cacheKey = Pk3DiscoveryService.Fingerprint(options, selectedMap, archives, cancellation);
            PreparedAnalysis prepared = AnalyzeCore(options.Source, options.MapName, options.Dependencies,
                options.UnitsPerUnit, cancellation);
            Analysis analysis = prepared.Analysis;
            if (!options.AllowFallbackTextures && analysis.Textures.Missing.Count != 0)
                throw new InvalidDataException("Missing source textures. Add a local dependency archive or allow fallback textures.");
            foreach (var warning in analysis.GameplayWarnings)
                diagnostics.Add(new(Severity.Warning, warning.Message));
            foreach (string missing in analysis.Textures.Missing)
                diagnostics.Add(new(Severity.Warning, $"Missing texture {missing}; a visible fallback will be used."));

            Directory.CreateDirectory(staging);
            var lines = new List<string>();
            void RequireUnchangedSource()
            {
                IReadOnlyList<string> currentArchives = MapTextureBake.DiscoverArchives(options.Source, options.Dependencies);
                if (!currentArchives.SequenceEqual(archives, StringComparer.Ordinal)
                    || Pk3DiscoveryService.Fingerprint(options, analysis.MapName, archives, cancellation) != cacheKey)
                    throw new IOException("Quake source or texture dependencies changed during import. Retry with unchanged files.");
            }
            bool cacheHit = false;
            try { cacheHit = Pk3DiscoveryService.Restore(cacheKey, staging, cancellation); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                diagnostics.Add(new(Severity.Warning, "Conversion cache unavailable: " + ex.Message));
            }
            diagnostics.Add(new(Severity.Info, cacheHit ? "Q3 conversion cache hit." : "Q3 conversion cache miss."));
            if (!cacheHit)
            {
                progress?.Invoke("Copying source and preparing textures…");
                int status = Q3Convert.Run(options.Source, prepared.Bsp
                    ?? throw new InvalidOperationException("Q3 analysis did not retain its parsed level."), analysis.MapName,
                    options.RoomName, staging,
                    dropClip: !options.KeepClip, dropItems: !options.KeepItems,
                    forcedScale: options.UnitsPerUnit, textureSize: options.TextureSize,
                    cancellation: cancellation, textureArchives: archives, log: lines.Add,
                    textureProgress: (done,total,name) =>
                        progress?.Invoke($"Baking textures {done:N0}/{total:N0} · {name}"));
                foreach (string line in lines) diagnostics.Add(new(Severity.Info, line));
                if (status != 0) throw new IOException("Quake 3 conversion failed.");
                RequireUnchangedSource();
                try { Pk3DiscoveryService.Store(cacheKey, staging, cancellation); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { diagnostics.Add(new(Severity.Warning, "Conversion cache unavailable: " + ex.Message)); }
            }
            else prepared = prepared with { Bsp = null };

            progress?.Invoke("Finalizing imported project…");
            string projectPath = Directory.EnumerateFiles(staging, "*.json").Single();
            var definition = MapDefinition.Load(projectPath);
            definition.FormatVersion = 2;
            definition.MapId = Guid.NewGuid();
            if (definition.Import != null)
            {
                definition.Import.KeepSky = options.KeepSky;
                definition.Import.KeepClip = options.KeepClip;
                definition.Import.PatchLevel = options.PatchLevel;
                definition.Import.AutoHealCollision = options.AutoHealCollision;
                definition.Import.CollisionHealTolerance = options.CollisionHealTolerance;
                if (!options.KeepSpawns)
                {
                    definition.Import.KeepSpawns = false;
                    definition.Spawns.Clear();
                }
            }
            Q3Traversal.Initialize(definition);
            definition.Save(projectPath);
            Q3ImportManifest.From(analysis,options.UnitsPerUnit??analysis.AutoScale).Save(staging);
            cancellation.ThrowIfCancellationRequested();

            RequireUnchangedSource();
            progress?.Invoke("Publishing imported map…");
            Directory.Move(staging, destination);
            string finalProject = Path.Combine(destination, Path.GetFileName(projectPath));
            return new(true, finalProject, analysis, diagnostics.AsReadOnly());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ProgramException
            or ArgumentException or UnauthorizedAccessException)
        {
            diagnostics.Add(new(Severity.Error, ex.Message));
            return new(false, null, null, diagnostics.AsReadOnly());
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Reimport architecture and art while preserving Project Prime-authored
    /// gameplay/editor data. Source-specific scale/preview are refreshed;
    /// authored entities, metadata, assets, audio, capabilities and geometry
    /// remain with the project.
    /// </summary>
    public static Result Reimport(MapDefinition existing, Options options,
        string projectPath, CancellationToken cancellation = default)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            ?? throw new IOException("Project path has no directory.");
        IReadOnlyList<string>? dependencies=options.Dependencies;
        if(dependencies==null||dependencies.Count==0)
            dependencies=Q3ImportManifest.Load(root)?.DependencyArchives();
        string tempDestination = Path.Combine(Path.GetTempPath(), "ProjectPrime-reimport-"
            + Guid.NewGuid().ToString("N"));
        var request = options with
        {
            DestinationDirectory = tempDestination,
            RoomName = existing.Name,
            Dependencies = dependencies
        };
        Result imported = Import(request, cancellation);
        if (!imported.Succeeded || imported.ProjectPath == null) return imported;
        bool preserveRecovery = false;
        try
        {
            var fresh = MapDefinition.Load(imported.ProjectPath);
            var merged = MapProjectSerializer.Clone(existing);
            var previousImport = existing.Import;
            merged.Import = fresh.Import;
            if (merged.Import != null && previousImport != null)
            {
                merged.Import.KeepSky = previousImport.KeepSky;
                merged.Import.KeepClip = previousImport.KeepClip;
                merged.Import.KeepSpawns = previousImport.KeepSpawns;
                merged.Import.KeepItems = previousImport.KeepItems;
                merged.Import.PatchLevel = previousImport.PatchLevel;
                merged.Import.CollisionPatchLevel = previousImport.CollisionPatchLevel;
                merged.Import.AutoHealCollision = previousImport.AutoHealCollision;
                merged.Import.CollisionHealTolerance = previousImport.CollisionHealTolerance;
                merged.Import.CollisionHealExclusions = previousImport.CollisionHealExclusions
                    .Select(region => new MapCollisionHealRegion
                    {
                        Center=(float[])region.Center.Clone(),Radius=region.Radius,Note=region.Note
                    }).ToList();
                merged.Import.MaterialReplacements = previousImport.MaterialReplacements
                    .Select(value=>new MapSourceMaterialReplacement
                    {
                        Source=value.Source,Target=value.Target,TargetSource=value.TargetSource
                    }).ToList();
                merged.Import.TexScale = previousImport.TexScale;
                merged.Import.DefaultMaterial = previousImport.DefaultMaterial;
                merged.Import.ShaderMaterials = new Dictionary<string, int>(previousImport.ShaderMaterials,
                    StringComparer.OrdinalIgnoreCase);
            }
            merged.ScaleFactor = fresh.ScaleFactor;
            merged.KillHeight = fresh.KillHeight;
            merged.FarClip = fresh.FarClip;
            if (merged.Preview == null) merged.Preview = fresh.Preview;

            string freshRoot = Path.GetDirectoryName(imported.ProjectPath)!;
            var updates = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (fresh.Import != null)
            {
                string? level = fresh.Import.Resolve();
                string? textures = fresh.Import.ResolveTextures();
                if (level != null)
                {
                    string target = Path.Combine(root, Path.GetFileName(level));
                    updates[target] = File.ReadAllBytes(level);
                    merged.Import!.Source = Path.GetFileName(target);
                }
                if (textures != null)
                {
                    string target = Path.Combine(root, Path.GetFileName(textures));
                    updates[target] = File.ReadAllBytes(textures);
                    merged.Import!.Textures = Path.GetFileName(target);
                }
                var oldModern = previousImport?.ModernTextures.Values.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var newModern = fresh.Import.ModernTextures.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string relative in newModern)
                {
                    byte[] bytes = MapAssets.Read(fresh, relative);
                    string target = Path.GetFullPath(Path.Combine(root, relative));
                    string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Reimported HD texture escapes the map project.");
                    updates[target] = bytes;
                    if (!merged.Assets.Any(a => a.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)))
                        merged.Assets.Add(new MapAsset { Path = relative, Kind = "texture", Name = "Q3 HD source" });
                }
                foreach (string stale in oldModern.Except(newModern, StringComparer.OrdinalIgnoreCase))
                    merged.Assets.RemoveAll(a => a.Path.Equals(stale, StringComparison.OrdinalIgnoreCase)
                        && !merged.Materials.Any(m => new[] { m.Texture, m.Albedo, m.Normal, m.SpecularRoughness, m.Emissive }
                            .Any(p => String.Equals(p, stale, StringComparison.OrdinalIgnoreCase))));
                merged.Import!.BaseDirectory = root;
                merged.Import.BundlePath = null;
            }
            merged.BaseDirectory = root;
            merged.SourcePath = Path.GetFullPath(projectPath);
            merged.BundlePath = null;
            // Source traversal is already validated in the isolated fresh import.
            // Prepare every byte before changing any existing project file.
            merged.Q3Triggers = fresh.Q3Triggers;
            merged.FormatVersion = fresh.FormatVersion;
            string preparedProject = Path.Combine(freshRoot, ".reimport-project.json");
            merged.Save(preparedProject);
            string manifest=Path.Combine(freshRoot,Q3ImportManifest.FileName);
            if(File.Exists(manifest))
                updates[Path.Combine(root,Q3ImportManifest.FileName)] = File.ReadAllBytes(manifest);
            updates[Path.GetFullPath(projectPath)] = File.ReadAllBytes(preparedProject);
            PublishReimport(updates, Path.Combine(freshRoot, ".rollback"), cancellation);
            return imported with { ProjectPath = Path.GetFullPath(projectPath) };
        }
        catch (AggregateException) { preserveRecovery = true; throw; }
        finally
        {
            try { if (!preserveRecovery && Directory.Exists(tempDestination)) Directory.Delete(tempDestination, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void PublishReimport(IReadOnlyDictionary<string, byte[]> updates, string backupDirectory,
        CancellationToken cancellation)
    {
        Directory.CreateDirectory(backupDirectory);
        var backups = new List<(string Target, string? Backup)>();
        try
        {
            foreach ((string target, byte[] bytes) in updates)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Directory.Exists(target)) throw new IOException("Reimport target is a directory: " + target);
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = Path.Combine(backupDirectory, backups.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    File.Copy(target, backup, false);
                }
                backups.Add((target, backup));
                AtomicFile.Write(target, bytes);
            }
            cancellation.ThrowIfCancellationRequested();
        }
        catch (Exception publishError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var (target, backup) in backups.AsEnumerable().Reverse())
            {
                try
                {
                    if (backup == null) { if (File.Exists(target)) File.Delete(target); }
                    else AtomicFile.Write(target, File.ReadAllBytes(backup));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { rollbackErrors.Add(ex); }
            }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("Reimport failed and some original files could not be restored. Recovery copies: " + backupDirectory,
                    new[] { publishError }.Concat(rollbackErrors));
            throw;
        }
    }

}
