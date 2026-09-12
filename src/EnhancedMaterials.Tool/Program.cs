using System.Text;
using MphRead.Imaging;

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
                "cook" => Cook(args),
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

    private static int Cook(string[] args)
    {
        if (args.Length != 3 || args[1].StartsWith('-')
            || args[2].StartsWith('-'))
            throw new ArgumentException("cook requires an input PNG and output PPTE path.");
        string input = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]);
        FileInfo source = new(input);
        if (!source.Exists)
            throw new FileNotFoundException("Input PNG was not found.", input);
        if (source.Length is <= 0 or > EnhancementPackLimits.MaximumTextureFileBytes)
            throw new InvalidDataException("Input PNG exceeds the enhancement texture file limit.");
        byte[] encoded = File.ReadAllBytes(input);
        if (!EnhancementPackLoader.TryReadPngDimensions(encoded,
                out int width, out int height)
            || width > EnhancementPackLimits.MaximumTextureDimension
            || height > EnhancementPackLimits.MaximumTextureDimension
            || checked((long)width * height * 4)
                > EnhancementPackLimits.MaximumDecodedTextureBytes)
        {
            throw new InvalidDataException(
                "Input PNG dimensions exceed the enhancement texture limit.");
        }
        RgbaImage image = StbImageDecoder.DecodeRgba(encoded);
        byte[] cooked = EnhancedTextureCookedFormat.Encode(image.Width,
            image.Height, image.Pixels.Span);
        string? directory = Path.GetDirectoryName(output);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllBytes(output, cooked);
        Console.WriteLine($"Cooked {image.Width}x{image.Height} RGBA8 texture to {output}");
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
        Console.WriteLine("cook INPUT.png OUTPUT.ppte");
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
