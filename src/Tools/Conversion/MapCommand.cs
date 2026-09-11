using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

public static class MapCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[0] is "help" or "--help" or "-h")
        {
            Help();
            return args.Length == 0 ? 0 : 2;
        }
        string command = args[0].ToLowerInvariant();
        string target = Resolve(args[1]);
        try
        {
            return command switch
            {
                "validate" => Validate(target),
                "build" => Build(target, args),
                "cook" => Cook(target, args),
                "verify" => Verify(target),
                "stats" => Stats(target),
                _ => Unknown(command)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Validate(string path)
    {
        if (MapBundle.Is(path))
        {
            MapBundleValidationResult result = new MapBundleValidator().Validate(path);
            Print(result.Diagnostics);
            if (!result.IsValid) return 1;
        }
        MapProject project = MapProjectIO.Load(path);
        var diagnostics = new MapValidator().ValidateProject(project);
        Print(diagnostics);
        return diagnostics.Any(diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static int Build(string path, string[] args)
    {
        MapProject project = MapProjectIO.Load(path);
        string? content = Option(args, "--content-dir");
        if (content != null) ContentEnvironment.Open(Resolve(content), Option(args, "--content-version") ?? "AMHE1");
        (string _, string baseHash) = ContentEnvironment.GetContentIdentity();
        string cache = Resolve(Option(args, "--cache") ?? MapStoragePaths.MapCache);
        MapBuildResult result = new MapCompiler().Compile(project, new MapBuildOptions
        {
            CacheDirectory = cache,
            BaseContentIdentity = baseHash,
            Force = args.Contains("--force", StringComparer.Ordinal),
            Verbose = true
        }, CancellationToken.None);
        Print(result.Diagnostics);
        foreach (MapStageTiming timing in result.Timings)
            Console.WriteLine($"{timing.Stage,-26} {timing.ElapsedMilliseconds,10:N2} ms");
        if (!result.Success) return 1;
        Console.WriteLine($"build fingerprint  {result.BuildFingerprint}");
        Console.WriteLine($"content hash       {result.ContentIdentity!.ContentHash}");
        Console.WriteLine($"cache              {result.CachePath}");
        PrintStatistics(result.Statistics!);
        return 0;
    }

    private static int Cook(string path, string[] args)
    {
        if (MapBundle.Is(path)) throw new MapPackageException("MAP-PKG-005", "Cook expects an editable project or legacy recipe, not a package.");
        MapProject project = MapProjectIO.Load(path);
        string output = Resolve(Option(args, "--out")
            ?? Path.ChangeExtension(path, MapBundle.Extension));
        MapBundleTools.Cook(project, path, output);
        return Verify(output);
    }

    private static int Verify(string path)
    {
        MapBundleValidationResult result = new MapBundleValidator().Validate(path);
        Print(result.Diagnostics);
        if (!result.IsValid) return 1;
        MapBundleReadResult bundle = result.Bundle!;
        Console.WriteLine($"stable ID          {bundle.Manifest.StableId}");
        Console.WriteLine($"version            {bundle.Manifest.Version}");
        Console.WriteLine($"content hash       {bundle.Manifest.ContentHash}");
        Console.WriteLine($"artifact hash      {bundle.ArtifactHash}");
        Console.WriteLine($"package bytes      {bundle.PackageSize:N0}");
        return 0;
    }

    private static int Stats(string path)
    {
        if (MapBundle.Is(path))
        {
            MapBundleReadResult bundle = new MapBundleReader().Read(path);
            Console.WriteLine($"{bundle.Manifest.Name}: {bundle.Manifest.Files.Count} source files, {bundle.PackageSize:N0} package bytes");
            foreach (MapManifestFile file in bundle.Manifest.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
                Console.WriteLine($"  {file.Size,10:N0}  {file.Role,-9} {file.Path}");
            return 0;
        }
        string buildJson = Directory.Exists(path) ? Path.Combine(path, "build.json") : path;
        MapBuildMetadata metadata = System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllBytes(buildJson), MapJsonContext.Default.MapBuildMetadata)
            ?? throw new MapCompilationException("Build metadata is null.");
        PrintStatistics(metadata.Statistics);
        return 0;
    }

    private static void PrintStatistics(MapBuildStatistics statistics)
    {
        Console.WriteLine($"render triangles    {statistics.RenderTriangles:N0}");
        Console.WriteLine($"render vertices     {statistics.RenderVertices:N0}");
        Console.WriteLine($"materials/textures  {statistics.Materials:N0} / {statistics.Textures:N0}");
        Console.WriteLine($"collision faces     {statistics.CollisionFaces:N0}");
        Console.WriteLine($"collision points    {statistics.CollisionPoints:N0}");
        Console.WriteLine($"collision planes    {statistics.CollisionPlanes:N0}");
        Console.WriteLine($"collision grid      {statistics.CollisionGridX} x {statistics.CollisionGridY} x {statistics.CollisionGridZ}");
        Console.WriteLine($"grid references     {statistics.CollisionGridReferences:N0} / {ushort.MaxValue:N0}");
        Console.WriteLine($"entities/spawns     {statistics.Entities:N0} / {statistics.Spawns:N0}");
    }

    private static void Print(System.Collections.Generic.IEnumerable<MapDiagnostic> diagnostics)
    {
        foreach (MapDiagnostic diagnostic in diagnostics)
            Console.WriteLine($"{diagnostic.Severity,-7} {diagnostic.Code} {diagnostic.Message}");
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Resolve(string path)
        => Path.IsPathRooted(path) ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(global::MphRead.ConsoleSetup.LaunchDirectory, path));

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown map command '{command}'.");
        Help();
        return 2;
    }

    private static void Help()
        => Console.WriteLine("ProjectPrimeTools map validate|build|cook|verify|stats <path> [--content-dir AMHE1] [--out map.fpmap]");
}
