using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.MapGen;

// Real supplied-package reader/compiler/packer checks; no importer, GL or ROM.
string Value(string flag)
{
    int index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1]
        : throw new ArgumentException("Required: --maps DIRECTORY --output NEW_EMPTY_DIRECTORY");
}
string maps = Path.GetFullPath(Value("--maps")), output = Path.GetFullPath(Value("--output"));
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Use a new empty output directory; evidence is never overwritten.");
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(output, "user-data"));
string priorDirectory = Directory.GetCurrentDirectory();
Directory.SetCurrentDirectory(output); // Paths cannot load an operator's paths.txt.
var checks = new List<object>();
var packages = new List<object>();
void Check(bool passed, string name)
{
    checks.Add(new { passed, name });
    Console.WriteLine($"SUPPLIEDMAP {(passed ? "PASS" : "FAIL")} {name}");
    if (!passed) throw new InvalidDataException(name);
}
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
void Report(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "report.json"),
    JsonSerializer.Serialize(new { passed, error, checks = checks.Count, assertions = checks, packages,
        assembly = typeof(MapCompiler).Assembly.Location, clientSha256 = Hash(typeof(MapCompiler).Assembly.Location),
        scope = "Strict supplied-package reading, mode validation, compilation and native output generation with an empty game-data root; no GL, player simulation or native startup claim." },
        new JsonSerializerOptions { WriteIndented = true }));
try
{
    string emptyGameData = Path.Combine(output, "empty-game-data");
    Directory.CreateDirectory(emptyGameData);
    Paths.SetPath(Paths.MphKey, emptyGameData);
    foreach (var fixture in new[] {
        (File: "capture-the-zone.ppmap", Room: "CAPTURE THE ZONE V1", Mode: GameMode.CaptureTheZone),
        (File: "payload-district.ppmap", Room: "PAYLOAD DISTRICT V1", Mode: GameMode.PushThePayload) })
    {
        string path = Path.Combine(maps, fixture.File), before = Hash(path);
        using var reader = new MapPackageReader(path); // Verifies the full archive content digest.
        Check(reader.Manifest is { Format: 2 } manifest && manifest.Name == fixture.Room,
            fixture.File + " strict format-two manifest and content hash");
        using var zip = ZipFile.OpenRead(path);
        Check(zip.Entries.All(entry => new[] { ".json", ".jpg", ".png", ".tex", ".obj" }
            .Contains(Path.GetExtension(entry.FullName))), fixture.File + " contains only expected portable asset types");
        foreach (var texture in zip.Entries.Where(entry => entry.FullName.EndsWith(".tex")))
        {
            using var stream = texture.Open();
            byte[] magic = new byte[4]; stream.ReadExactly(magic);
            Check(magic.AsSpan().SequenceEqual("FPTX"u8), fixture.File + " custom fallback " + texture.Name);
        }
        var definition = MapDefinition.Load(path);
        Check(definition.Name == fixture.Room && !definition.RequiresPrivateSource
            && definition.Import == null && definition.NativeRoom == null && definition.ModelSources.Count == 0,
            fixture.File + " needs no cartridge/source-model import");
        Check(MapDependencyAnalyzer.PackageAssets(definition).All(asset => zip.GetEntry(asset) != null),
            fixture.File + " declares complete packaged material dependencies");
        Check(MapModeValidator.WhyUnsupported(definition, fixture.Mode, 2) == null
            && MapModeValidator.WhyUnsupported(definition, fixture.Mode, 8) == null,
            fixture.File + " retained gameplay schema supports two and eight players");
        Check(MapModeValidator.WhyUnsupported(definition, fixture.Mode, 1) != null
            && MapModeValidator.WhyUnsupported(definition, fixture.Mode, 9) != null,
            fixture.File + " rejects unsupported roster sizes");
        var compilation = MapCompiler.Compile(definition);
        Check(compilation.Map != null && compilation.Validation.IsValid,
            fixture.File + " compiles: " + string.Join("; ", compilation.Validation.Diagnostics
                .Where(d => d.Severity == MapDiagnosticSeverity.Error).Select(d => d.Message)));
        string generated = Path.Combine(output, Path.GetFileNameWithoutExtension(fixture.File));
        string archive = Path.Combine(generated, "archive"), entities = Path.Combine(generated, "entities"),
            nodes = Path.Combine(generated, "nodes");
        Directory.CreateDirectory(nodes);
        MapPacker.Generate(compilation.Map!, archive, entities, nodes, verbose: false);
        var outputs = MapOutputSet.Create(definition, archive, entities, nodes);
        Check(outputs.Complete && outputs.Files.All(file => new FileInfo(file).Length > 0)
            && MapBuildManifest.IsCurrent(definition, outputs), fixture.File + " generates complete verified native runtime outputs");
        Check(!Directory.EnumerateFileSystemEntries(emptyGameData).Any() && Hash(path) == before,
            fixture.File + " preserves input bytes and uses no game data");
        packages.Add(new { fixture.File, fixture.Room, mode = fixture.Mode.ToString(), sha256 = before,
            manifest = reader.Manifest, polygons = compilation.Map!.Faces.Count, collisionFaces = compilation.Map.Solid.Count,
            spawns = definition.Spawns.Count, outputHashes = outputs.Files.ToDictionary(file => Path.GetFileName(file)!, Hash) });
    }
    Report(true, null);
    Console.WriteLine($"SUPPLIEDMAP PASS {checks.Count} checks; no game assets or GL.");
    return 0;
}
catch (Exception error)
{
    Report(false, error.ToString()); Console.Error.WriteLine(error); return 1;
}
finally { Directory.SetCurrentDirectory(priorDirectory); }
