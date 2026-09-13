using Mono.Cecil;
using Mono.Cecil.Cil;

const string InputOption = "--input-dir";
const string OutputOption = "--output-dir";
string[] expectedModules =
[
    "ProjectPrime.dll",
    "ProjectPrime.Client.Core.dll",
    "ProjectPrime.Client.Presentation.dll",
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
        (int marshalDescriptors, int genericConstraints, int interfaceBodies) =
            RepairMetadata(inputPath, outputPath);
        Console.WriteLine($"{moduleName}: preserved {marshalDescriptors} marshal descriptor(s), "
            + $"repaired {genericConstraints} generic constraint(s), "
            + $"restored {interfaceBodies} default interface body/bodies");
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

static (int MarshalDescriptors, int GenericConstraints, int InterfaceBodies) RepairMetadata(
    string inputPath, string outputPath)
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

    int expectedMarshalDescriptors = CountMarshalMetadata(input.MainModule);
    int restoredMarshalDescriptors = RestoreMarshalMetadata(input.MainModule, output.MainModule);
    int repairedGenericConstraints = RepairGenericConstraints(input.MainModule, output.MainModule);
    int restoredInterfaceBodies = RestoreDefaultInterfaceBodies(
        input.MainModule, output.MainModule);
    IReadOnlyDictionary<string, string> expectedConstraintProfiles =
        UnmanagedConstraintProfiles(output.MainModule);
    IReadOnlyDictionary<string, string> expectedInterfaceBodyProfiles =
        DefaultInterfaceBodyProfiles(output.MainModule);
    bool changed = restoredMarshalDescriptors != 0 || repairedGenericConstraints != 0
        || restoredInterfaceBodies != 0;

    if (!changed)
    {
        ValidateMetadata(output.MainModule, expectedMarshalDescriptors,
            expectedConstraintProfiles, expectedInterfaceBodyProfiles, outputPath);
        return (0, 0, 0);
    }

    string temporary = outputPath + ".metadata-repair." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        output.Write(temporary);
        using AssemblyDefinition verified = AssemblyDefinition.ReadAssembly(temporary, reader);
        ValidateMetadata(verified.MainModule, expectedMarshalDescriptors,
            expectedConstraintProfiles, expectedInterfaceBodyProfiles, outputPath);
        File.Move(temporary, outputPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }
    }
    return (restoredMarshalDescriptors, repairedGenericConstraints, restoredInterfaceBodies);
}

static int RestoreDefaultInterfaceBodies(ModuleDefinition input, ModuleDefinition output)
{
    int restored = 0;
    foreach (TypeDefinition type in input.Types.SelectMany(AllTypes)
        .Where(candidate => candidate.IsInterface))
    {
        TypeDefinition targetType = Lookup<TypeDefinition>(output, type.MetadataToken, "interface");
        if (!targetType.IsInterface)
        {
            throw new InvalidDataException(
                $"Obfuscar changed interface token {type.MetadataToken} into {targetType.FullName}");
        }
        foreach (MethodDefinition method in type.Methods.Where(candidate => candidate.HasBody))
        {
            MethodDefinition target = Lookup<MethodDefinition>(
                output, method.MetadataToken, "default interface method");
            int expectedInstructions = method.Body.Instructions.Count;
            int actualInstructions = target.HasBody ? target.Body.Instructions.Count : 0;
            if (expectedInstructions == 0)
            {
                if (actualInstructions != 0)
                {
                    throw new InvalidDataException(
                        $"Obfuscar added a body to default interface method token {method.MetadataToken}");
                }
                continue;
            }
            if (actualInstructions == expectedInstructions)
            {
                continue;
            }
            if (actualInstructions != 0)
            {
                throw new InvalidDataException(
                    $"Obfuscar changed the body length for default interface method token {method.MetadataToken}: "
                    + $"expected {expectedInstructions}, found {actualInstructions}");
            }
            target.Body = CloneMethodBody(method, target, input, output);
            restored++;
        }
    }
    return restored;
}

static MethodBody CloneMethodBody(MethodDefinition source, MethodDefinition target,
    ModuleDefinition input, ModuleDefinition output)
{
    var body = new MethodBody(target)
    {
        InitLocals = source.Body.InitLocals,
        MaxStackSize = source.Body.MaxStackSize
    };
    foreach (VariableDefinition variable in source.Body.Variables)
    {
        body.Variables.Add(new VariableDefinition(
            MapTypeReference(variable.VariableType, source, target, input, output)));
    }

    var instructions = new Dictionary<Instruction, Instruction>();
    foreach (Instruction instruction in source.Body.Instructions)
    {
        Instruction clone = Instruction.Create(OpCodes.Nop);
        clone.OpCode = instruction.OpCode;
        instructions.Add(instruction, clone);
        body.Instructions.Add(clone);
    }
    foreach ((Instruction sourceInstruction, Instruction targetInstruction) in instructions)
    {
        targetInstruction.Operand = MapInstructionOperand(sourceInstruction.Operand,
            source, target, body, instructions, input, output);
    }
    foreach (ExceptionHandler handler in source.Body.ExceptionHandlers)
    {
        body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
        {
            CatchType = handler.CatchType is null ? null
                : MapTypeReference(handler.CatchType, source, target, input, output),
            TryStart = MapInstruction(handler.TryStart, instructions),
            TryEnd = MapInstruction(handler.TryEnd, instructions),
            HandlerStart = MapInstruction(handler.HandlerStart, instructions),
            HandlerEnd = MapInstruction(handler.HandlerEnd, instructions),
            FilterStart = MapInstruction(handler.FilterStart, instructions)
        });
    }
    return body;
}

static object? MapInstructionOperand(object? operand, MethodDefinition source,
    MethodDefinition target, MethodBody body,
    IReadOnlyDictionary<Instruction, Instruction> instructions,
    ModuleDefinition input, ModuleDefinition output) => operand switch
    {
        null => null,
        Instruction instruction => MapInstruction(instruction, instructions),
        Instruction[] targets => targets.Select(instruction =>
            MapInstruction(instruction, instructions)!).ToArray(),
        VariableDefinition variable => body.Variables[variable.Index],
        ParameterDefinition parameter => target.Parameters[parameter.Index],
        MethodReference method => MapMethodReference(method, source, target, input, output),
        FieldReference field => MapFieldReference(field, source, target, input, output),
        TypeReference type => MapTypeReference(type, source, target, input, output),
        CallSite callSite => MapCallSite(callSite, source, target, input, output),
        string or sbyte or byte or int or long or float or double => operand,
        _ => throw new InvalidDataException(
            $"unsupported IL operand {operand.GetType().FullName} while restoring {source.FullName}")
    };

static Instruction? MapInstruction(Instruction? instruction,
    IReadOnlyDictionary<Instruction, Instruction> instructions)
    => instruction is null ? null : instructions.TryGetValue(instruction, out Instruction? mapped)
        ? mapped : throw new InvalidDataException("default interface body refers to an unknown instruction");

static MethodReference MapMethodReference(MethodReference method, MethodDefinition source,
    MethodDefinition target, ModuleDefinition input, ModuleDefinition output)
{
    if (method is GenericInstanceMethod generic)
    {
        var mapped = new GenericInstanceMethod(MapMethodReference(
            generic.ElementMethod, source, target, input, output));
        foreach (TypeReference argument in generic.GenericArguments)
        {
            mapped.GenericArguments.Add(MapTypeReference(argument,
                source, target, input, output));
        }
        return mapped;
    }
    if (method is MethodDefinition definition && ReferenceEquals(definition.Module, input))
    {
        return Lookup<MethodDefinition>(output, definition.MetadataToken, "method body operand");
    }
    return output.ImportReference(method, target);
}

static FieldReference MapFieldReference(FieldReference field, MethodDefinition source,
    MethodDefinition target, ModuleDefinition input, ModuleDefinition output)
{
    if (field is FieldDefinition definition && ReferenceEquals(definition.Module, input))
    {
        return Lookup<FieldDefinition>(output, definition.MetadataToken, "field body operand");
    }
    return output.ImportReference(field, target);
}

static TypeReference MapTypeReference(TypeReference type, MethodDefinition source,
    MethodDefinition target, ModuleDefinition input, ModuleDefinition output)
{
    if (type is TypeDefinition definition && ReferenceEquals(definition.Module, input))
    {
        return Lookup<TypeDefinition>(output, definition.MetadataToken, "type body operand");
    }
    if (type is GenericParameter parameter)
    {
        if (parameter.Owner is MethodDefinition)
        {
            return target.GenericParameters[parameter.Position];
        }
        TypeDefinition targetOwner = Lookup<TypeDefinition>(output,
            ((TypeDefinition)parameter.Owner).MetadataToken, "generic type owner");
        return targetOwner.GenericParameters[parameter.Position];
    }
    return type switch
    {
        ArrayType array => new ArrayType(MapTypeReference(array.ElementType,
            source, target, input, output), array.Rank),
        ByReferenceType byReference => new ByReferenceType(MapTypeReference(
            byReference.ElementType, source, target, input, output)),
        PointerType pointer => new PointerType(MapTypeReference(
            pointer.ElementType, source, target, input, output)),
        PinnedType pinned => new PinnedType(MapTypeReference(
            pinned.ElementType, source, target, input, output)),
        SentinelType sentinel => new SentinelType(MapTypeReference(
            sentinel.ElementType, source, target, input, output)),
        RequiredModifierType required => new RequiredModifierType(
            MapTypeReference(required.ModifierType, source, target, input, output),
            MapTypeReference(required.ElementType, source, target, input, output)),
        OptionalModifierType optional => new OptionalModifierType(
            MapTypeReference(optional.ModifierType, source, target, input, output),
            MapTypeReference(optional.ElementType, source, target, input, output)),
        GenericInstanceType generic => MapGenericInstanceType(
            generic, source, target, input, output),
        _ => output.ImportReference(type, target)
    };
}

static GenericInstanceType MapGenericInstanceType(GenericInstanceType generic,
    MethodDefinition source, MethodDefinition target,
    ModuleDefinition input, ModuleDefinition output)
{
    var mapped = new GenericInstanceType(MapTypeReference(
        generic.ElementType, source, target, input, output));
    foreach (TypeReference argument in generic.GenericArguments)
    {
        mapped.GenericArguments.Add(MapTypeReference(argument,
            source, target, input, output));
    }
    return mapped;
}

static CallSite MapCallSite(CallSite callSite, MethodDefinition source,
    MethodDefinition target, ModuleDefinition input, ModuleDefinition output)
{
    var mapped = new CallSite(MapTypeReference(
        callSite.ReturnType, source, target, input, output))
    {
        CallingConvention = callSite.CallingConvention,
        ExplicitThis = callSite.ExplicitThis,
        HasThis = callSite.HasThis
    };
    foreach (ParameterDefinition parameter in callSite.Parameters)
    {
        mapped.Parameters.Add(new ParameterDefinition(MapTypeReference(
            parameter.ParameterType, source, target, input, output)));
    }
    return mapped;
}

static int RestoreMarshalMetadata(ModuleDefinition input, ModuleDefinition output)
{
    int expected = CountMarshalMetadata(input);
    int actual = CountMarshalMetadata(output);
    if (expected == 0)
    {
        if (actual != 0)
        {
            throw new InvalidDataException($"{output.Name} unexpectedly gained marshal metadata");
        }
        return 0;
    }

    int restored = 0;
    foreach (TypeDefinition type in input.Types.SelectMany(AllTypes))
    {
        foreach (FieldDefinition field in type.Fields.Where(candidate => candidate.HasMarshalInfo))
        {
            FieldDefinition target = Lookup<FieldDefinition>(output, field.MetadataToken, "field");
            RequireMatchingType(field.FieldType, target.FieldType, field.MetadataToken, "field");
            target.MarshalInfo = field.MarshalInfo;
            restored++;
        }
        foreach (MethodDefinition method in type.Methods)
        {
            MethodDefinition? targetMethod = null;
            if (method.MethodReturnType.HasMarshalInfo)
            {
                targetMethod = Lookup<MethodDefinition>(output, method.MetadataToken, "method");
                RequireMatchingType(method.ReturnType, targetMethod.ReturnType, method.MetadataToken, "return value");
                targetMethod.MethodReturnType.MarshalInfo = method.MethodReturnType.MarshalInfo;
                restored++;
            }
            for (int index = 0; index < method.Parameters.Count; index++)
            {
                ParameterDefinition parameter = method.Parameters[index];
                if (parameter.HasMarshalInfo)
                {
                    targetMethod ??= Lookup<MethodDefinition>(output, method.MetadataToken, "method");
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
        throw new InvalidDataException($"{input.Name} marshal descriptor count changed while repairing: expected {expected}, restored {restored}");
    }
    return restored;
}

static int RepairGenericConstraints(ModuleDefinition input, ModuleDefinition output)
{
    int repaired = 0;
    foreach (TypeDefinition type in input.Types.SelectMany(AllTypes))
    {
        if (HasUnmanagedValueTypeConstraint(type.GenericParameters))
        {
            TypeDefinition targetType = Lookup<TypeDefinition>(output, type.MetadataToken, "type");
            repaired += RepairGenericParameters(type.GenericParameters, targetType.GenericParameters,
                output, $"type token {type.MetadataToken}");
        }
        foreach (MethodDefinition method in type.Methods
            .Where(candidate => HasUnmanagedValueTypeConstraint(candidate.GenericParameters)))
        {
            MethodDefinition targetMethod = Lookup<MethodDefinition>(output, method.MetadataToken, "method");
            repaired += RepairGenericParameters(method.GenericParameters, targetMethod.GenericParameters,
                output, $"method token {method.MetadataToken}");
        }
    }
    return repaired;
}

static int RepairGenericParameters(IList<GenericParameter> expectedParameters,
    IList<GenericParameter> actualParameters, ModuleDefinition output, string owner)
{
    if (expectedParameters.Count != actualParameters.Count)
    {
        throw new InvalidDataException(
            $"Obfuscar changed the generic parameter count for {owner}: expected {expectedParameters.Count}, found {actualParameters.Count}");
    }

    int repaired = 0;
    for (int parameterIndex = 0; parameterIndex < expectedParameters.Count; parameterIndex++)
    {
        GenericParameter expected = expectedParameters[parameterIndex];
        GenericParameter actual = actualParameters[parameterIndex];
        if (expected.Position != actual.Position || expected.Attributes != actual.Attributes)
        {
            throw new InvalidDataException(
                $"Obfuscar changed generic parameter {parameterIndex} for {owner}");
        }
        if (expected.Constraints.Count != actual.Constraints.Count)
        {
            throw new InvalidDataException(
                $"Obfuscar changed the generic constraint count for parameter {parameterIndex} of {owner}: "
                + $"expected {expected.Constraints.Count}, found {actual.Constraints.Count}");
        }

        for (int constraintIndex = 0; constraintIndex < expected.Constraints.Count; constraintIndex++)
        {
            TypeReference expectedType = expected.Constraints[constraintIndex].ConstraintType;
            GenericParameterConstraint actualConstraint = actual.Constraints[constraintIndex];
            TypeReference actualType = actualConstraint.ConstraintType;
            if (!TryGetUnmanagedValueTypeConstraint(expectedType,
                out TypeReference expectedValueType, out TypeReference expectedModifier))
            {
                RequireMatchingExternalConstraint(expectedType, actualType,
                    owner, parameterIndex, constraintIndex);
                continue;
            }

            AssemblyNameReference expectedValueTypeScope = RequireFrameworkScope(
                expectedValueType, owner, parameterIndex, constraintIndex, "input System.ValueType");
            AssemblyNameReference expectedModifierScope = RequireFrameworkScope(
                expectedModifier, owner, parameterIndex, constraintIndex, "input UnmanagedType modifier");
            if (actualType.FullName == "System.ValueType" && IsSelfScoped(actualType, output))
            {
                actualConstraint.ConstraintType = output.ImportReference(expectedType);
                repaired++;
                continue;
            }

            if (!TryGetUnmanagedValueTypeConstraint(actualType,
                out TypeReference actualValueType, out TypeReference actualModifier))
            {
                throw new InvalidDataException(
                    $"Obfuscar changed the unmanaged constraint at {owner}, generic parameter {parameterIndex}, constraint {constraintIndex}: "
                    + $"expected {expectedType.FullName}, found {actualType.FullName}");
            }
            AssemblyNameReference actualValueTypeScope = RequireFrameworkScope(
                actualValueType, owner, parameterIndex, constraintIndex, "output System.ValueType");
            AssemblyNameReference actualModifierScope = RequireFrameworkScope(
                actualModifier, owner, parameterIndex, constraintIndex, "output UnmanagedType modifier");
            if (!StringComparer.Ordinal.Equals(expectedValueTypeScope.FullName, actualValueTypeScope.FullName)
                || !StringComparer.Ordinal.Equals(expectedModifierScope.FullName, actualModifierScope.FullName))
            {
                throw new InvalidDataException(
                    $"Obfuscar changed the unmanaged-constraint framework scope at {owner}, generic parameter {parameterIndex}, constraint {constraintIndex}");
            }
        }
    }
    return repaired;
}

static bool TryGetUnmanagedValueTypeConstraint(TypeReference type,
    out TypeReference valueType, out TypeReference modifier)
{
    if (type is RequiredModifierType required
        && required.ElementType.FullName == "System.ValueType"
        && required.ModifierType.FullName == "System.Runtime.InteropServices.UnmanagedType")
    {
        valueType = required.ElementType;
        modifier = required.ModifierType;
        return true;
    }
    valueType = null!;
    modifier = null!;
    return false;
}

static bool HasUnmanagedValueTypeConstraint(IList<GenericParameter> parameters)
    => parameters.Any(parameter => parameter.Constraints.Any(constraint =>
        TryGetUnmanagedValueTypeConstraint(constraint.ConstraintType, out _, out _)));

static AssemblyNameReference RequireFrameworkScope(TypeReference type, string owner,
    int parameterIndex, int constraintIndex, string description)
{
    if (type.Scope is AssemblyNameReference assembly)
    {
        return assembly;
    }
    throw new InvalidDataException(
        $"{description} has a non-framework scope at {owner}, generic parameter {parameterIndex}, constraint {constraintIndex}");
}

static bool IsSelfScoped(TypeReference type, ModuleDefinition module)
    => type.Scope is ModuleDefinition scope && ReferenceEquals(scope, module);

static void RequireMatchingExternalConstraint(TypeReference expected, TypeReference actual,
    string owner, int parameterIndex, int constraintIndex)
{
    if (expected.Scope is not AssemblyNameReference expectedScope)
    {
        return;
    }
    if (actual.Scope is not AssemblyNameReference actualScope
        || !StringComparer.Ordinal.Equals(expected.FullName, actual.FullName)
        || !StringComparer.Ordinal.Equals(expectedScope.FullName, actualScope.FullName))
    {
        throw new InvalidDataException(
            $"Obfuscar changed the external constraint at {owner}, generic parameter {parameterIndex}, constraint {constraintIndex}: "
            + $"expected {ConstraintIdentity(expected)}, found {ConstraintIdentity(actual)}");
    }
}

static void ValidateMetadata(ModuleDefinition output, int expectedMarshalDescriptors,
    IReadOnlyDictionary<string, string> expectedConstraintProfiles,
    IReadOnlyDictionary<string, string> expectedInterfaceBodyProfiles, string outputPath)
{
    int actualMarshalDescriptors = CountMarshalMetadata(output);
    if (actualMarshalDescriptors != expectedMarshalDescriptors)
    {
        throw new InvalidDataException(
            $"{Path.GetFileName(outputPath)} marshal descriptor verification failed: expected {expectedMarshalDescriptors}, found {actualMarshalDescriptors}");
    }

    // Cecil can reorder metadata tokens while writing. The obfuscated owner names
    // and generic-parameter positions captured immediately before the write remain
    // stable, so use those identities to ensure each profile survived on its owner.
    IReadOnlyDictionary<string, string> actualConstraintProfiles =
        UnmanagedConstraintProfiles(output);
    if (expectedConstraintProfiles.Count != actualConstraintProfiles.Count
        || expectedConstraintProfiles.Any(expected =>
            !actualConstraintProfiles.TryGetValue(expected.Key, out string? actual)
            || !StringComparer.Ordinal.Equals(expected.Value, actual)))
    {
        throw new InvalidDataException(
            $"{Path.GetFileName(outputPath)} unmanaged generic-constraint profiles changed after metadata repair");
    }

    IReadOnlyDictionary<string, string> actualInterfaceBodyProfiles =
        DefaultInterfaceBodyProfiles(output);
    if (expectedInterfaceBodyProfiles.Count != actualInterfaceBodyProfiles.Count
        || expectedInterfaceBodyProfiles.Any(expected =>
            !actualInterfaceBodyProfiles.TryGetValue(expected.Key, out string? actual)
            || !StringComparer.Ordinal.Equals(expected.Value, actual)))
    {
        throw new InvalidDataException(
            $"{Path.GetFileName(outputPath)} default interface bodies changed after metadata repair");
    }

    foreach (TypeDefinition type in output.Types.SelectMany(AllTypes))
    {
        foreach (GenericParameter parameter in type.GenericParameters
            .Concat(type.Methods.SelectMany(method => method.GenericParameters)))
        {
            if (parameter.Constraints.Any(constraint =>
                constraint.ConstraintType.FullName == "System.ValueType"
                && IsSelfScoped(constraint.ConstraintType, output)))
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(outputPath)} contains a stripped, self-scoped unmanaged constraint");
            }
        }
    }
}

static IReadOnlyDictionary<string, string> DefaultInterfaceBodyProfiles(ModuleDefinition module)
{
    var profiles = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (TypeDefinition type in module.Types.SelectMany(AllTypes)
        .Where(candidate => candidate.IsInterface))
    {
        foreach (MethodDefinition method in type.Methods.Where(candidate => candidate.HasBody))
        {
            string key = $"{type.FullName}|{method.Name}|generic:{method.GenericParameters.Count}"
                + $"|return:{method.ReturnType.FullName}|parameters:"
                + string.Join(",", method.Parameters.Select(parameter =>
                    parameter.ParameterType.FullName));
            string value = $"instructions:{method.Body.Instructions.Count}|opcodes:"
                + string.Join(",", method.Body.Instructions.Select(instruction =>
                    instruction.OpCode.Code.ToString()));
            if (!profiles.TryAdd(key, value))
            {
                throw new InvalidDataException(
                    $"duplicate default interface method identity: {key}");
            }
        }
    }
    return profiles;
}

static IReadOnlyDictionary<string, string> UnmanagedConstraintProfiles(ModuleDefinition module)
{
    var profiles = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (TypeDefinition type in module.Types.SelectMany(AllTypes))
    {
        AddProfiles(type.GenericParameters, profiles, $"type:{type.FullName}");
        foreach (MethodDefinition method in type.Methods)
        {
            AddProfiles(method.GenericParameters, profiles, $"method:{method.FullName}");
        }
    }
    return profiles;
}

static void AddProfiles(IList<GenericParameter> parameters, Dictionary<string, string> profiles,
    string owner)
{
    foreach (GenericParameter parameter in parameters)
    {
        if (!parameter.Constraints.Any(constraint =>
            TryGetUnmanagedValueTypeConstraint(constraint.ConstraintType, out _, out _)))
        {
            continue;
        }
        string key = $"{owner}|parameter:{parameter.Position}";
        string value = $"{(int)parameter.Attributes}:"
            + string.Join(";", parameter.Constraints.Select(constraint =>
                ConstraintIdentity(constraint.ConstraintType)));
        if (!profiles.TryAdd(key, value))
        {
            throw new InvalidDataException($"duplicate generic-constraint owner identity: {key}");
        }
    }
}

static string ConstraintIdentity(TypeReference type)
{
    if (type is RequiredModifierType required)
    {
        return $"{type.FullName}|value-scope:{ScopeIdentity(required.ElementType.Scope)}"
            + $"|modifier-scope:{ScopeIdentity(required.ModifierType.Scope)}";
    }
    return $"{type.FullName}|scope:{ScopeIdentity(type.Scope)}";
}

static string ScopeIdentity(IMetadataScope? scope) => scope switch
{
    AssemblyNameReference assembly => $"assembly:{assembly.FullName}",
    ModuleDefinition module => $"module:{module.Name}",
    ModuleReference module => $"module-reference:{module.Name}",
    null => "none",
    _ => $"{scope.MetadataScopeType}:{scope.Name}"
};

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
