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
    }

    private sealed class Options
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        private Options(IReadOnlyDictionary<string, string> values) => _values = values;
        public bool Has(string name) => _values.ContainsKey(name);
        public string? Path(string name) => _values.TryGetValue(name, out string? value)
            ? LaunchPath(value) : null;

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
