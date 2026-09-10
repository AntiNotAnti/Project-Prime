using System.Text;

namespace MphRead;

internal static class EnhancedMaterialsProgram
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Usage();
                return args.Length == 0 ? 2 : 0;
            }
            return args[0].ToLowerInvariant() switch
            {
                "inspect" => Inspect(args),
                "starter" => Starter(args),
                _ => throw new ArgumentException($"Unknown enhanced-material command: {args[0]}")
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Inspect(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
            throw new ArgumentException("inspect requires an enhancement pack directory.");
        Options options = Options.Parse(args, 2, "inventory", "output");
        EnhancedMaterialInspectionReport report = EnhancedMaterialAuthoring.Inspect(
            Path.GetFullPath(args[1]), options.Path("inventory"));
        Write(options.Path("output"), EnhancedMaterialAuthoring.Serialize(report));
        return report.HasIssues ? 3 : 0;
    }

    private static int Starter(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
            throw new ArgumentException("starter requires a JSON key list or inventory file.");
        Options options = Options.Parse(args, 2, "output");
        string output = options.Path("output")
            ?? throw new ArgumentException("starter requires --output FILE.");
        IReadOnlyList<string> keys = EnhancedMaterialAuthoring.ReadStarterKeys(Path.GetFullPath(args[1]));
        Write(output, EnhancedMaterialAuthoring.GenerateStarterManifest(keys));
        return 0;
    }

    private static void Write(string? output, string content)
    {
        if (output is null)
        {
            Console.Write(content);
            return;
        }
        string? directory = Path.GetDirectoryName(output);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(output, content, new UTF8Encoding(false));
        Console.WriteLine($"Enhanced material output written to {output}");
    }

    private static void Usage()
    {
        Console.WriteLine("inspect PACK_DIRECTORY [--inventory FILE] [--output FILE]");
        Console.WriteLine("starter KEYS_OR_INVENTORY_JSON --output MATERIALS_JSON");
    }

    private sealed class Options
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        private Options(IReadOnlyDictionary<string, string> values) => _values = values;

        public string? Path(string name) => _values.TryGetValue(name, out string? value)
            ? System.IO.Path.GetFullPath(value) : null;

        public static Options Parse(string[] args, int start, params string[] allowed)
        {
            var accepted = new HashSet<string>(allowed, StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = start; i < args.Length; i += 2)
            {
                string option = args[i];
                if (!option.StartsWith("--", StringComparison.Ordinal) || option.Length <= 2
                    || i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    throw new ArgumentException($"Invalid enhanced-material option near: {option}");
                }
                string name = option[2..];
                if (!accepted.Contains(name) || !values.TryAdd(name, args[i + 1]))
                    throw new ArgumentException($"Unknown or duplicate enhanced-material option: {option}");
            }
            return new Options(values);
        }
    }
}
