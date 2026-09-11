using MphRead.Mods.MapGen;
using ProjectPrime.Editor.App;
using ProjectPrime.Editor.Documents;

namespace ProjectPrime.Editor;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            MapImageDecoding.Decoder = global::MphRead.Imaging.StbImageDecoder.Decode;
            string? content = Option(args, "--content-dir")
                ?? Environment.GetEnvironmentVariable("PRIME_CONTENT_DIRECTORY");
            string contentVersion = Option(args, "--content-version") ?? "AMHE1";
            string? cache = Option(args, "--cache");
            using var builds = new EditorBuildService(content, contentVersion, cache);
            return args.FirstOrDefault() switch
            {
                "new" => New(args),
                "import-q3" => ImportQ3(args),
                "validate" => Validate(args),
                "build" => Build(args, builds),
                "export" => Export(args, builds),
                _ => RunEditor(args, builds)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int New(string[] args)
    {
        string path = RequiredPath(args, 1);
        string id = Option(args, "--id") ?? "community.new-map";
        string name = Option(args, "--name") ?? "New Map";
        MapDocument document = MapDocument.New(id, name);
        document.Save(path);
        Console.WriteLine(Path.GetFullPath(path));
        return 0;
    }

    private static int Validate(string[] args)
    {
        MapProject project = MapProjectIO.Load(RequiredPath(args, 1));
        IReadOnlyList<MapDiagnostic> diagnostics = new MapValidator().ValidateProject(project);
        Print(diagnostics);
        return diagnostics.Any(value => value.Severity == MapDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static int ImportQ3(string[] args)
    {
        string source = RequiredPath(args, 1);
        string output = Option(args, "--out") is { } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(Path.GetDirectoryName(source)!,
                Path.GetFileNameWithoutExtension(source) + ".project.json");
        string name = Option(args, "--name") ?? Path.GetFileNameWithoutExtension(source);
        string stableId = Option(args, "--id") ?? MapIdentity.FromLegacyName(name);
        MapProject project = Q3MapProjectFactory.Create(source, output, stableId, name,
            Option(args, "--level"), Option(args, "--textures"),
            Number(args, "--units", 28),
            keepClip: !args.Contains("--drop-clip", StringComparer.Ordinal),
            keepSky: args.Contains("--keep-sky", StringComparer.Ordinal),
            keepSpawns: !args.Contains("--drop-spawns", StringComparer.Ordinal),
            patchLevel: checked((int)Number(args, "--patch", 3)),
            textureScale: Number(args, "--texture-scale", 24),
            sourceReferenceMode: args.Contains("--external-source", StringComparer.Ordinal)
                ? Q3SourceReferenceMode.ReferenceExternally
                : Q3SourceReferenceMode.CopyIntoProject);
        MapProjectIO.Save(project, output);
        IReadOnlyList<MapDiagnostic> diagnostics = new MapValidator().ValidateProject(project);
        Print(diagnostics);
        Console.WriteLine(Path.GetFullPath(output));
        return diagnostics.Any(value => value.Severity == MapDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static int Build(string[] args, EditorBuildService builds)
    {
        MapProject project = MapProjectIO.Load(RequiredPath(args, 1));
        MapBuildResult result = builds.BuildAsync(project, args.Contains("--force"),
            CancellationToken.None).GetAwaiter().GetResult();
        Print(result.Diagnostics);
        if (result.Success)
            Console.WriteLine($"{result.BuildFingerprint} {result.CachePath}");
        return result.Success ? 0 : 1;
    }

    private static int Export(string[] args, EditorBuildService builds)
    {
        string path = RequiredPath(args, 1);
        MapProject project = MapProjectIO.Load(path);
        string output = Option(args, "--out") ?? Path.ChangeExtension(path, MapBundle.Extension);
        MapBundleWriteResult result = builds.ExportAsync(project, path, output,
            CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"{result.Path}\ncontent {result.ContentIdentity.ContentHash}\nartifact {result.ArtifactHash}");
        return 0;
    }

    private static int RunEditor(string[] args, EditorBuildService builds)
    {
        string? path = args.FirstOrDefault(value => !value.StartsWith("-", StringComparison.Ordinal)
            && value is not ("new" or "validate" or "build" or "export"));
        MapDocument document = path == null
            ? MapDocument.New("community.new-map", "New Map") : MapDocument.Open(path);
        return new EditorApplication(document, builds).Run();
    }

    private static void Print(IEnumerable<MapDiagnostic> diagnostics)
    {
        foreach (MapDiagnostic value in diagnostics)
            Console.WriteLine($"{value.Severity,-7} {value.Code} {value.Message}");
    }

    private static string RequiredPath(string[] args, int index)
        => args.Length > index ? Path.GetFullPath(args[index])
            : throw new ArgumentException("A map project path is required.");

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static float Number(string[] args, string name, float fallback)
        => Option(args, name) is { } value
            ? float.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
}
