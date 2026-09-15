namespace MphRead;

public static class FidelityCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Usage();
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "manifest" => Manifest(args),
                "extract" => Extract(args),
                "run" => Trial(args),
                "compare" => Compare(args),
                "report" => Report(args),
                "oracle" => Oracle(args),
                _ => throw new ArgumentException($"Unknown fidelity command: {args[0]}")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Manifest(string[] args)
    {
        Options options = Options.Parse(args, 1, "reference", "output");
        FidelityReferenceManifest manifest = FidelityManifestBuilder.Build(RequiredPath(options, "reference"));
        Write(options, FidelityManifestBuilder.Serialize(manifest));
        if (options.Has("output")) Console.WriteLine($"Aggregate SHA-256: {manifest.AggregateSha256}");
        return 0;
    }

    private static int Extract(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
            throw new ArgumentException("fidelity extract requires a domain.");
        Options options = Options.Parse(args, 2, "reference", "output");
        FidelityExtractReport report = FidelityReferenceExtractor.Extract(
            RequiredPath(options, "reference"), args[1]);
        Write(options, FidelityJson.Serialize(report));
        return 0;
    }

    private static int Trial(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
            throw new ArgumentException("fidelity run requires a case identifier.");
        Options options = Options.Parse(args, 2, "reference", "fixtures", "output");
        FidelityReferenceManifest manifest = FidelityManifestBuilder.Build(RequiredPath(options, "reference"));
        string fixtures = options.Path("fixtures") ?? LaunchPath("tests/Tests/Fidelity/Fixtures");
        FidelityCaseDocument value = FidelityCaseParser.Load(fixtures, args[1] + ".json",
            manifest.ContentVersion, manifest.AggregateSha256);
        Write(options, FidelityJson.Serialize(FidelityTrialWorksheetBuilder.Build(value)));
        return 0;
    }

    private static int Compare(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
            throw new ArgumentException("fidelity compare requires a case identifier.");
        Options options = Options.Parse(args, 2, "expected", "actual", "output");
        string expected = RequiredPath(options, "expected");
        string actual = RequiredPath(options, "actual");
        FidelityJsonDifference difference = FidelityJsonComparer.Compare(args[1],
            ReadBounded(expected, FidelityJsonComparer.MaximumDocumentBytes),
            ReadBounded(actual, FidelityJsonComparer.MaximumDocumentBytes));
        Write(options, FidelityJson.Serialize(difference));
        return difference.Equal ? 0 : 3;
    }

    private static int Report(string[] args)
    {
        Options options = Options.Parse(args, 1, "reference", "fixtures", "output");
        FidelityReferenceManifest manifest = FidelityManifestBuilder.Build(RequiredPath(options, "reference"));
        string fixtures = options.Path("fixtures") ?? LaunchPath("tests/Tests/Fidelity/Fixtures");
        FidelityProgramReport report = FidelityProgramReportBuilder.Build(fixtures,
            manifest.ContentVersion, manifest.AggregateSha256);
        Write(options, FidelityJson.Serialize(report));
        return 0;
    }

    private static int Oracle(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
            throw new ArgumentException("fidelity oracle requires record, compare, verify, or list.");
        return args[1].ToLowerInvariant() switch
        {
            "record" => OracleRecord(args),
            "compare" => OracleCompare(args),
            "verify" => OracleVerify(args),
            "list" => OracleList(args),
            _ => throw new ArgumentException($"Unknown fidelity oracle command: {args[1]}")
        };
    }

    private static int OracleRecord(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith('-'))
            throw new ArgumentException("fidelity oracle record requires a scenario identifier.");
        Options options = Options.Parse(args, 3, "reference", "scenarios", "adapter",
            "artifact-root", "timeout-ms", "run-id", "rom", "output");
        string scenarios = options.Path("scenarios") ?? LaunchPath("tools/fidelity/scenarios");
        OracleScenario scenario = LoadOracleScenario(scenarios, args[2]);
        string reference = RequiredPath(options, "reference");
        string? adapter = options.Path("adapter")
            ?? Environment.GetEnvironmentVariable("PRIME_ORACLE_ADAPTER");
        if (String.IsNullOrWhiteSpace(adapter))
            throw new ArgumentException("AMHE1 oracle recording requires --adapter PATH or PRIME_ORACLE_ADAPTER; no live run is claimed without an external adapter.");
        string artifactRoot = options.Path("artifact-root")
            ?? LaunchPath("artifacts/fidelity/oracle");
        int timeout = options.Int32("timeout-ms", OracleHost.DefaultTimeoutMilliseconds,
            1, OracleHost.MaximumTimeoutMilliseconds);
        OracleRecordResult result = OracleHost.Record(scenario, reference, adapter,
            artifactRoot, timeout, options.Value("run-id"), options.Path("rom"));
        Write(options, OracleJson.Serialize(new
        {
            schemaVersion = OracleJson.SchemaVersion,
            scenarioId = scenario.Id,
            runDirectory = result.RunDirectory,
            adapterExitCode = result.AdapterExitCode,
            timedOut = result.TimedOut,
            romIdentity = result.Artifact.Manifest.RomIdentity,
            result = result.Artifact.Manifest.Result
        }));
        return 0;
    }

    private static int OracleCompare(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith('-'))
            throw new ArgumentException("fidelity oracle compare requires a scenario identifier.");
        Options options = Options.Parse(args, 3, "scenarios", "expected", "actual",
            "normalizer", "output");
        string scenarios = options.Path("scenarios") ?? LaunchPath("tools/fidelity/scenarios");
        OracleScenario scenario = LoadOracleScenario(scenarios, args[2]);
        OracleArtifact expected = OracleJson.LoadArtifact(RequiredPath(options, "expected"));
        OracleArtifact actual = OracleJson.LoadArtifact(RequiredPath(options, "actual"));
        OracleNormalizer? normalizer = options.Path("normalizer") is string normalizerPath
            ? OracleJson.ParseNormalizer(File.ReadAllBytes(normalizerPath), scenario.Source.ReferenceRevision)
            : OracleJson.DefaultNormalizer(scenario.Source.ReferenceRevision);
        if (!StringComparer.Ordinal.Equals(expected.Manifest.ScenarioId, scenario.Id)
            || !StringComparer.Ordinal.Equals(actual.Manifest.ScenarioId, scenario.Id))
            throw new InvalidDataException("Oracle artifact scenario does not match the requested scenario.");
        OracleComparison comparison = OracleComparer.Compare(expected, actual, normalizer);
        Write(options, OracleJson.SerializeComparison(comparison));
        return comparison.Compatible && comparison.Equal ? 0 : 3;
    }

    private static int OracleVerify(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith('-'))
            throw new ArgumentException("fidelity oracle verify requires an artifact path.");
        Options options = Options.Parse(args, 3, "reference", "rom", "output");
        OracleArtifact artifact = OracleJson.LoadArtifact(LaunchPath(args[2]));
        string reference = RequiredPath(options, "reference");
        // Parsing an artifact proves only its schema and source labels. The
        // extracted-reference claim is made only after hashing the supplied
        // directory against the frozen AMHE1 identity.
        OracleHost.VerifyReference(reference, artifact.Manifest.Source);
        OracleRomIdentity? rom = options.Path("rom") is string romPath
            ? OracleHost.VerifyRom(romPath)
            : null;
        if (rom is not null && !StringComparer.Ordinal.Equals(
            artifact.Manifest.RomIdentity, rom.Identity))
            throw new InvalidDataException("Artifact ROM identity does not match the supplied private ROM.");
        Write(options, OracleJson.Serialize(new
        {
            schemaVersion = OracleJson.SchemaVersion,
            valid = true,
            scenarioId = artifact.Manifest.ScenarioId,
            referenceRevision = artifact.Manifest.Source.ReferenceRevision,
            extractedReferenceVerified = true,
            fullRomVerified = rom is not null,
            romIdentity = artifact.Manifest.RomIdentity,
            verificationNote = rom is null
                ? "Only the extracted AMHE1 reference identity is verified; supply --rom to verify the complete private AMHE1 image."
                : "The extracted AMHE1 reference and complete private AMHE1 image identity are verified; the ROM path is never written to the artifact.",
            checkpoints = artifact.Checkpoints.Count,
            events = artifact.Events.Count,
            result = artifact.Manifest.Result
        }));
        return 0;
    }

    private static int OracleList(string[] args)
    {
        Options options = Options.Parse(args, 2, "scenarios", "output");
        string root = options.Path("scenarios") ?? LaunchPath("tools/fidelity/scenarios");
        DirectoryInfo info = new(root);
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Oracle scenario directory must be a regular directory.");
        string[] files = Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > OracleJson.MaximumCheckpoints)
            throw new InvalidDataException("Oracle scenario directory contains no bounded scenario files.");
        var summaries = new List<OracleScenarioSummary>(files.Length);
        foreach (string file in files)
        {
            OracleScenario scenario = OracleJson.ParseScenario(File.ReadAllBytes(file));
            summaries.Add(new OracleScenarioSummary(scenario.Id, scenario.Hunter, scenario.Map,
                scenario.Actions.Count, scenario.Checkpoints.Count, scenario.Source.ReferenceRevision,
                scenario.Source.ProjectRevision));
        }
        Write(options, OracleJson.Serialize(summaries));
        return 0;
    }

    private static OracleScenario LoadOracleScenario(string root, string identifier)
    {
        string relative = identifier.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? identifier : identifier + ".json";
        return OracleJson.LoadScenario(root, relative);
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length is < 2 || info.Length > maximum)
            throw new InvalidDataException($"Normalized report must be a regular file of at most {maximum} bytes: {path}");
        return File.ReadAllBytes(path);
    }

    private static string RequiredPath(Options options, string name)
        => options.Path(name) ?? throw new ArgumentException($"fidelity command requires --{name} PATH");

    private static void Write(Options options, string content)
    {
        string? output = options.Path("output");
        if (output == null)
        {
            Console.Write(content);
            return;
        }
        string? parent = Path.GetDirectoryName(output);
        if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(output, content, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"Fidelity output written to {output}");
    }

    private static string LaunchPath(string path) => Path.GetFullPath(Path.IsPathRooted(path)
        ? path : Path.Combine(ConsoleSetup.LaunchDirectory, path));

    private static void Usage()
    {
        Console.WriteLine("fidelity manifest --reference DIRECTORY [--output FILE]");
        Console.WriteLine("fidelity extract <binary-layout|multiplayer-archives|audio-sequences> --reference DIRECTORY [--output FILE]");
        Console.WriteLine("fidelity run <case-id> --reference DIRECTORY [--fixtures DIRECTORY] [--output FILE]");
        Console.WriteLine("fidelity compare <case-id> --expected FILE --actual FILE [--output FILE]");
        Console.WriteLine("fidelity report --reference DIRECTORY [--fixtures DIRECTORY] [--output FILE]");
        Console.WriteLine("fidelity oracle record|compare|verify|list ...");
    }

    private sealed class Options
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        private Options(IReadOnlyDictionary<string, string> values) => _values = values;
        public bool Has(string name) => _values.ContainsKey(name);
        public string? Value(string name) => _values.TryGetValue(name, out string? value) ? value : null;
        public string? Path(string name) => _values.TryGetValue(name, out string? value)
            ? LaunchPath(value) : null;

        public int Int32(string name, int fallback, int minimum, int maximum)
        {
            if (!_values.TryGetValue(name, out string? value)) return fallback;
            if (!System.Int32.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int parsed)
                || parsed < minimum || parsed > maximum)
                throw new ArgumentException($"Invalid --{name} value.");
            return parsed;
        }

        public static Options Parse(string[] args, int start, params string[] allowed)
        {
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = start; i < args.Length; i += 2)
            {
                string option = args[i];
                if (!option.StartsWith("--", StringComparison.Ordinal) || option.Length <= 2
                    || i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    throw new ArgumentException($"Invalid fidelity option near: {option}");
                string name = option[2..];
                if (!allowedSet.Contains(name) || !values.TryAdd(name, args[i + 1]))
                    throw new ArgumentException($"Unknown or duplicate fidelity option: {option}");
            }
            return new Options(values);
        }
    }
}
