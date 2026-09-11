using Mono.Cecil;

const string InputOption = "--input-dir";
const string OutputOption = "--output-dir";
string[] expectedModules =
[
    "ProjectPrime.dll",
    "ProjectPrime.Game.dll",
    "Server.Shared.dll",
    "ProjectPrime.Replay.dll",
    "Audio.Ncsf.dll"
];

try
{
    Dictionary<string, string> options = ParseOptions(args);
    string inputDirectory = AbsoluteDirectory(options, InputOption);
    string outputDirectory = AbsoluteDirectory(options, OutputOption);

    foreach (string moduleName in expectedModules)
    {
        string inputPath = Path.Combine(inputDirectory, moduleName);
        string outputPath = Path.Combine(outputDirectory, moduleName);
        RequireFile(inputPath, "protection input");
        RequireFile(outputPath, "Obfuscar output");
        int restored = RestoreMarshalMetadata(inputPath, outputPath);
        Console.WriteLine($"{moduleName}: preserved {restored} marshal descriptor(s)");
    }
    return 0;
}
catch (Exception exception) when (exception is ArgumentException
    or BadImageFormatException
    or IOException
    or InvalidDataException)
{
    Console.Error.WriteLine($"client protection metadata repair failed: {exception.Message}");
    return 1;
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || arguments[index] is not (InputOption or OutputOption))
        {
            throw new ArgumentException($"usage: {InputOption} ABSOLUTE_PATH {OutputOption} ABSOLUTE_PATH");
        }
        if (!result.TryAdd(arguments[index], arguments[index + 1]))
        {
            throw new ArgumentException($"duplicate option: {arguments[index]}");
        }
    }
    if (result.Count != 2 || !result.ContainsKey(InputOption) || !result.ContainsKey(OutputOption))
    {
        throw new ArgumentException($"usage: {InputOption} ABSOLUTE_PATH {OutputOption} ABSOLUTE_PATH");
    }
    return result;
}

static string AbsoluteDirectory(IReadOnlyDictionary<string, string> options, string name)
{
    string path = options[name];
    if (!Path.IsPathFullyQualified(path))
    {
        throw new ArgumentException($"{name} must be an absolute path: {path}");
    }
    path = Path.GetFullPath(path);
    if (!Directory.Exists(path))
    {
        throw new ArgumentException($"{name} does not exist: {path}");
    }
    return path;
}

static void RequireFile(string path, string label)
{
    if (!File.Exists(path))
    {
        throw new InvalidDataException($"missing {label}: {path}");
    }
}

static int RestoreMarshalMetadata(string inputPath, string outputPath)
{
    var resolver = new DefaultAssemblyResolver();
    // Resolve renamed first-party references from the Obfuscar output graph.
    resolver.AddSearchDirectory(Path.GetDirectoryName(outputPath)!);
    resolver.AddSearchDirectory(Path.GetDirectoryName(inputPath)!);
    var reader = new ReaderParameters
    {
        AssemblyResolver = resolver,
        InMemory = true,
        ReadSymbols = false
    };
    using AssemblyDefinition input = AssemblyDefinition.ReadAssembly(inputPath, reader);
    using AssemblyDefinition output = AssemblyDefinition.ReadAssembly(outputPath, reader);

    int expected = CountMarshalMetadata(input.MainModule);
    if (expected == 0)
    {
        if (CountMarshalMetadata(output.MainModule) != 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(outputPath)} unexpectedly gained marshal metadata");
        }
        return 0;
    }

    int restored = 0;
    foreach (TypeDefinition type in input.MainModule.Types.SelectMany(AllTypes))
    {
        foreach (FieldDefinition field in type.Fields.Where(candidate => candidate.HasMarshalInfo))
        {
            FieldDefinition target = Lookup<FieldDefinition>(output.MainModule, field.MetadataToken, "field");
            RequireMatchingType(field.FieldType, target.FieldType, field.MetadataToken, "field");
            target.MarshalInfo = field.MarshalInfo;
            restored++;
        }
        foreach (MethodDefinition method in type.Methods)
        {
            MethodDefinition? targetMethod = null;
            if (method.MethodReturnType.HasMarshalInfo)
            {
                targetMethod = Lookup<MethodDefinition>(output.MainModule, method.MetadataToken, "method");
                RequireMatchingType(method.ReturnType, targetMethod.ReturnType, method.MetadataToken, "return value");
                targetMethod.MethodReturnType.MarshalInfo = method.MethodReturnType.MarshalInfo;
                restored++;
            }
            for (int index = 0; index < method.Parameters.Count; index++)
            {
                ParameterDefinition parameter = method.Parameters[index];
                if (parameter.HasMarshalInfo)
                {
                    targetMethod ??= Lookup<MethodDefinition>(output.MainModule, method.MetadataToken, "method");
                    if (targetMethod.Parameters.Count != method.Parameters.Count)
                    {
                        throw new InvalidDataException($"Obfuscar changed the parameter count for method token {method.MetadataToken}");
                    }
                    RequireMatchingType(parameter.ParameterType, targetMethod.Parameters[index].ParameterType,
                        method.MetadataToken, $"parameter {index}");
                    targetMethod.Parameters[index].MarshalInfo = parameter.MarshalInfo;
                    restored++;
                }
            }
        }
    }
    if (restored != expected)
    {
        throw new InvalidDataException($"{Path.GetFileName(inputPath)} marshal descriptor count changed while repairing: expected {expected}, restored {restored}");
    }

    string temporary = outputPath + ".marshal-repair." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        output.Write(temporary);
        using AssemblyDefinition verified = AssemblyDefinition.ReadAssembly(temporary, reader);
        int actual = CountMarshalMetadata(verified.MainModule);
        if (actual != expected)
        {
            throw new InvalidDataException($"{Path.GetFileName(outputPath)} marshal descriptor verification failed: expected {expected}, found {actual}");
        }
        File.Move(temporary, outputPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }
    }
    return restored;
}

static T Lookup<T>(ModuleDefinition module, MetadataToken token, string kind) where T : class, IMetadataTokenProvider
{
    if (module.LookupToken(token) is not T result)
    {
        throw new InvalidDataException($"Obfuscar did not preserve {kind} metadata token {token}");
    }
    return result;
}

static void RequireMatchingType(TypeReference expected, TypeReference actual, MetadataToken owner, string kind)
{
    if (expected.FullName != actual.FullName)
    {
        throw new InvalidDataException(
            $"Obfuscar changed the {kind} signature at {owner}: expected {expected.FullName}, found {actual.FullName}");
    }
}

static int CountMarshalMetadata(ModuleDefinition module)
{
    int count = 0;
    foreach (TypeDefinition type in module.Types.SelectMany(AllTypes))
    {
        count += type.Fields.Count(field => field.HasMarshalInfo);
        foreach (MethodDefinition method in type.Methods)
        {
            if (method.MethodReturnType.HasMarshalInfo)
            {
                count++;
            }
            count += method.Parameters.Count(parameter => parameter.HasMarshalInfo);
        }
    }
    return count;
}

static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
{
    yield return type;
    foreach (TypeDefinition nested in type.NestedTypes)
    {
        foreach (TypeDefinition descendant in AllTypes(nested))
        {
            yield return descendant;
        }
    }
}
