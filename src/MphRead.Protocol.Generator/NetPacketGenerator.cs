using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MphRead.Protocol.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class NetPacketGenerator : IIncrementalGenerator
{
    private const string Prefix = "MphRead.Mods.Network.Generated.";
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var packets = context.SyntaxProvider.ForAttributeWithMetadataName(Prefix + "NetPacketAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol).Collect();
        context.RegisterSourceOutput(packets.Combine(context.CompilationProvider),
            static (ctx, input) => Generate(ctx, input.Left, input.Right));
    }

    private static readonly string[] Titles = { "Unsupported field or declaration", "Unbounded string",
        "Invalid numeric range", "Invalid enum", "Variable field without supported bound",
        "Packet exceeds maximum size", "Duplicate packet declaration", "Invalid protocol version" };
    private static void Error(SourceProductionContext ctx, int number, ISymbol symbol, string detail)
        => ctx.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("NETGEN" + number.ToString("000"),
            Titles[number - 1], "{0}", "NetworkSchema", DiagnosticSeverity.Error, true),
            symbol.Locations.FirstOrDefault(), detail));

    private sealed class Field
    {
        public IParameterSymbol Symbol = null!;
        public string Name => "this.@" + Symbol.Name;
        public int Index;
        public string Type => Symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        public SpecialType Primitive;
        public int Size, Bound;
        public bool String, Enum;
        public long? Minimum, Maximum;
        public string[] EnumValues = Array.Empty<string>();
    }

    private static void Generate(SourceProductionContext ctx, ImmutableArray<INamedTypeSymbol> symbols, Compilation compilation)
    {
        // The transport owns the limit. Synthetic consumers may supply the same
        // constant; no packet attribute can override it to exceed the live MTU.
        var config = compilation.GetTypeByMetadataName("MphRead.Mods.Network.NetConfig");
        int maxPacket = config?.GetMembers("MaxPacketSize").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue as int? ?? 1472;
        var unique = symbols.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        var ids = new Dictionary<int, List<INamedTypeSymbol>>();
        foreach (var symbol in unique)
        {
            var attr = symbol.GetAttributes().First(a => a.AttributeClass?.ToDisplayString() == Prefix + "NetPacketAttribute");
            int id;
            try {
                object? raw = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value : null;
                if (!(raw is byte || raw is sbyte || raw is short || raw is ushort || raw is int || raw is uint || raw is long || raw is ulong))
                    throw new InvalidOperationException();
                id = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch { Error(ctx, 1, symbol, "Packet ID must be an integer in [0, 255]."); continue; }
            if (id < 0 || id > 255) { Error(ctx, 1, symbol, "Packet ID must fit one byte."); continue; }
            if (!ids.TryGetValue(id, out var group)) ids.Add(id, group = new List<INamedTypeSymbol>());
            group.Add(symbol);
        }
        foreach (var group in ids)
        {
            if (group.Value.Count > 1)
            {
                foreach (var symbol in group.Value) Error(ctx, 7, symbol, "Packet ID " + group.Key + " is declared more than once.");
                continue;
            }
            EmitPacket(ctx, group.Value[0], group.Key, maxPacket);
        }
    }

    private static void EmitPacket(SourceProductionContext ctx, INamedTypeSymbol symbol, int id, int maxPacket)
    {
        var syntax = symbol.DeclaringSyntaxReferences.First().GetSyntax() as RecordDeclarationSyntax;
        if (syntax == null || !syntax.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
            || syntax.ParameterList == null || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
            || !syntax.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || symbol.ContainingType != null || symbol.IsGenericType)
        {
            Error(ctx, 1, symbol, "Packets must be top-level, non-generic readonly partial positional record structs."); return;
        }
        // Refuse hand-authored state: otherwise generated serialization could omit it.
        if (symbol.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsImplicitlyDeclared && !f.IsStatic)
            || symbol.GetMembers().OfType<IPropertySymbol>().Any(p => p.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is PropertyDeclarationSyntax)))
        { Error(ctx, 1, symbol, "Packet state must use positional parameters only."); return; }
        var attr = symbol.GetAttributes().First(a => a.AttributeClass?.ToDisplayString() == Prefix + "NetPacketAttribute");
        int version = attr.ConstructorArguments.Length > 1 && attr.ConstructorArguments[1].Value is int v ? v : 0;
        if (version < 1 || version > ushort.MaxValue)
        { Error(ctx, 8, symbol, "Protocol version metadata must be in [1, 65535]."); return; }
        var ctor = symbol.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == syntax.ParameterList.Parameters.Count
            && c.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is RecordDeclarationSyntax));
        if (ctor == null) { Error(ctx, 1, symbol, "Cannot resolve positional packet constructor."); return; }
        string[] reserved = { "Validate", "Write", "TryRead", "EncodedSize", "MinimumSize", "MaximumSize", "Size", "SchemaProtocolVersion", "__netUtf8", "ValidateSchema" };
        if (symbol.GetMembers().Any(m => reserved.Contains(m.Name)
            && !(m.Name == "ValidateSchema" && m is IMethodSymbol method && !method.IsStatic
                && method.ReturnsVoid && method.Parameters.Length == 1
                && method.Parameters[0].RefKind == RefKind.Ref
                && method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean
                && method.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is MethodDeclarationSyntax declaration
                    && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))))
        { Error(ctx, 1, symbol, "Packet declares a reserved generated member name."); return; }
        var fields = new List<Field>();
        bool valid = true;
        foreach (var parameter in ctor.Parameters)
        {
            Field? field = Parse(ctx, parameter);
            if (field == null) valid = false; else { field.Index = fields.Count; fields.Add(field); }
        }
        if (!valid) return;
        long maximum = 1L + fields.Sum(f => (long)f.Size + f.Bound);
        int minimum = 1 + fields.Sum(f => f.Size);
        if (maximum > maxPacket)
        { Error(ctx, 6, symbol, "Maximum encoded size " + maximum + " exceeds NetConfig.MaxPacketSize " + maxPacket + "."); return; }
        string name = "@" + symbol.Name;
        var b = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!symbol.ContainingNamespace.IsGlobalNamespace) b.Append("namespace ").Append(symbol.ContainingNamespace.ToDisplayString()).AppendLine(";");
        b.Append("readonly partial record struct ").Append(name).AppendLine("\n{");
        b.Append("public const int MinimumSize = ").Append(minimum).AppendLine(";");
        b.Append("public const int MaximumSize = ").Append(maximum).AppendLine(";");
        b.Append("public const int SchemaProtocolVersion = ").Append(version).AppendLine(";");
        if (!fields.Any(f => f.String)) b.AppendLine("public const int Size = MaximumSize;");
        b.AppendLine("private static readonly global::System.Text.UTF8Encoding __netUtf8 = new(false, true);");
        b.AppendLine("public bool Validate() { try {");
        foreach (var f in fields)
        {
            if (f.String) b.Append("if (").Append(f.Name).Append(" is null || ").Append(f.Name).Append(".Length > ").Append(f.Bound).Append(" || __netUtf8.GetByteCount(").Append(f.Name).Append(") > ").Append(f.Bound).AppendLine(") return false;");
            if (f.Minimum.HasValue) b.Append("if ((decimal)").Append(f.Name).Append(" < ").Append(Literal(f.Minimum.Value)).Append("m || (decimal)").Append(f.Name).Append(" > ").Append(Literal(f.Maximum!.Value)).AppendLine("m) return false;");
            if (f.Enum) b.Append("if (!(").Append(string.Join(" || ", f.EnumValues.Select(value => f.Name + " == (" + f.Type + ")(" + value + ")"))).AppendLine(")) return false;");
        }
        b.AppendLine("bool valid = true; ValidateSchema(ref valid); return valid; } catch (global::System.Text.EncoderFallbackException) { return false; } }");
        b.AppendLine("partial void ValidateSchema(ref bool valid);");
        b.Append("public int EncodedSize { get { if (!Validate()) throw new global::System.InvalidOperationException(\"Invalid packet values.\"); return MinimumSize");
        foreach (var f in fields.Where(f => f.String)) b.Append(" + __netUtf8.GetByteCount(").Append(f.Name).Append(')');
        b.AppendLine("; } }");
        b.AppendLine("public void Write(global::System.Span<byte> destination) { int size = EncodedSize; if (destination.Length < size) throw new global::System.ArgumentException(\"Destination is too short.\", nameof(destination));");
        b.Append("destination[0] = ").Append(id).AppendLine("; int offset = 1;");
        foreach (var f in fields) Write(b, f);
        b.AppendLine("}");
        b.Append("public static bool TryRead(global::System.ReadOnlySpan<byte> source, out ").Append(name).AppendLine(" packet) { packet = default;");
        b.Append("if (source.Length < MinimumSize || source.Length > MaximumSize || source[0] != ").Append(id).AppendLine(") return false; int offset = 1; try {");
        foreach (var f in fields) Read(b, f);
        b.Append("if (offset != source.Length) return false; var candidate = new ").Append(name).Append('(').Append(string.Join(", ", fields.Select(f => "__netField" + f.Index))).AppendLine(");");
        b.AppendLine("if (!candidate.Validate()) return false; packet = candidate; return true; } catch (global::System.Text.DecoderFallbackException) { return false; } }\n}");
        ctx.AddSource(symbol.ToDisplayString().Replace('.', '_') + ".NetPacket.g.cs", SourceText.From(b.ToString(), Encoding.UTF8));
    }

    private static string Literal(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static AttributeData? Attribute(IParameterSymbol p, string name) => p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == Prefix + name);
    private static Field? Parse(SourceProductionContext ctx, IParameterSymbol p)
    {
        var f = new Field { Symbol = p, Primitive = p.Type.SpecialType };
        if (p.RefKind != RefKind.None) { Error(ctx, 1, p, "By-reference fields are unsupported."); return null; }
        var range = Attribute(p, "NetRangeAttribute");
        var text = Attribute(p, "NetStringAttribute");
        var array = Attribute(p, "NetFixedArrayAttribute");
        if (p.Type is IArrayTypeSymbol || array != null)
        { Error(ctx, 5, p, "Arrays and NetFixedArray are reserved and not supported in this MVP."); return null; }
        if (p.Type.SpecialType == SpecialType.System_String)
        {
            if (text == null || text.ConstructorArguments.Length != 1 || text.ConstructorArguments[0].Value is not int bound || bound < 0 || bound > ushort.MaxValue)
            { Error(ctx, 2, p, "Strings require NetString(maximumBytes) in [0, 65535]."); return null; }
            if (range != null) { Error(ctx, 3, p, "Numeric bounds cannot be applied to strings."); return null; }
            f.String = true; f.Size = 2; f.Bound = bound; return f;
        }
        if (text != null) { Error(ctx, 2, p, "NetString applies only to strings."); return null; }
        if (p.Type.TypeKind == TypeKind.Enum)
        {
            var en = (INamedTypeSymbol)p.Type;
            f.Enum = true; f.Primitive = en.EnumUnderlyingType!.SpecialType;
            var values = en.GetMembers().OfType<IFieldSymbol>().Where(x => x.HasConstantValue).ToArray();
            if (values.Length == 0 || en.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"))
            { Error(ctx, 4, p, "Enums must declare values; Flags combinations require an explicit future contract."); return null; }
            f.EnumValues = values.Select(x => Convert.ToString(x.ConstantValue, CultureInfo.InvariantCulture)
                + (f.Primitive == SpecialType.System_UInt64 ? "UL" : "L")).Distinct().ToArray();
        }
        switch (f.Primitive)
        {
            case SpecialType.System_Byte: case SpecialType.System_SByte: case SpecialType.System_Boolean: f.Size = 1; break;
            case SpecialType.System_Int16: case SpecialType.System_UInt16: f.Size = 2; break;
            case SpecialType.System_Int32: case SpecialType.System_UInt32: f.Size = 4; break;
            case SpecialType.System_Int64: case SpecialType.System_UInt64: f.Size = 8; break;
            default: Error(ctx, 1, p, "Unsupported field type: " + p.Type); return null;
        }
        if (range != null)
        {
            if (range.ConstructorArguments.Length != 2 || range.ConstructorArguments[0].Value is not long min
                || range.ConstructorArguments[1].Value is not long max)
            { Error(ctx, 3, p, "NetRange requires two valid integer bounds."); return null; }
            (decimal lo, decimal hi) limits = f.Primitive switch
            {
                SpecialType.System_Byte => (byte.MinValue, byte.MaxValue),
                SpecialType.System_SByte => (sbyte.MinValue, sbyte.MaxValue),
                SpecialType.System_Int16 => (short.MinValue, short.MaxValue),
                SpecialType.System_UInt16 => (ushort.MinValue, ushort.MaxValue),
                SpecialType.System_Int32 => (int.MinValue, int.MaxValue),
                SpecialType.System_UInt32 => (uint.MinValue, uint.MaxValue),
                SpecialType.System_Int64 => (long.MinValue, long.MaxValue),
                SpecialType.System_UInt64 => (ulong.MinValue, ulong.MaxValue),
                _ => (0, -1)
            };
            if (min > max || min < limits.lo || max > limits.hi)
            { Error(ctx, 3, p, "Range must be ordered and fit the numeric field type."); return null; }
            f.Minimum = min; f.Maximum = max;
        }
        return f;
    }

    private static string BinaryMethod(SpecialType type) => type switch
    {
        SpecialType.System_Int16 => "Int16", SpecialType.System_UInt16 => "UInt16",
        SpecialType.System_Int32 => "Int32", SpecialType.System_UInt32 => "UInt32",
        SpecialType.System_Int64 => "Int64", SpecialType.System_UInt64 => "UInt64", _ => throw new InvalidOperationException()
    };
    private static string PrimitiveName(SpecialType type) => type switch
    {
        SpecialType.System_Byte => "byte", SpecialType.System_SByte => "sbyte",
        SpecialType.System_Int16 => "short", SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int", SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long", SpecialType.System_UInt64 => "ulong", _ => "bool"
    };
    private static void Write(StringBuilder b, Field f)
    {
        string value = f.Enum ? "(" + PrimitiveName(f.Primitive) + ")" + f.Name : f.Name;
        if (f.String)
        {
            b.Append("int __len_").Append(f.Symbol.Name).Append(" = __netUtf8.GetByteCount(").Append(f.Name).AppendLine(");");
            b.Append("global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), (ushort)__len_").Append(f.Symbol.Name).AppendLine("); offset += 2;");
            b.Append("offset += __netUtf8.GetBytes(global::System.MemoryExtensions.AsSpan(").Append(f.Name).AppendLine("), destination.Slice(offset));");
        }
        else if (f.Primitive == SpecialType.System_Boolean) b.Append("destination[offset++] = ").Append(value).AppendLine(" ? (byte)1 : (byte)0;");
        else if (f.Size == 1) b.Append("destination[offset++] = unchecked((byte)").Append(value).AppendLine(");");
        else b.Append("global::System.Buffers.Binary.BinaryPrimitives.Write").Append(BinaryMethod(f.Primitive)).Append("LittleEndian(destination.Slice(offset, ").Append(f.Size).Append("), ").Append(value).Append("); offset += ").Append(f.Size).AppendLine(";");
    }
    private static void Read(StringBuilder b, Field f)
    {
        string name = "__netField" + f.Index;
        b.Append("if (source.Length - offset < ").Append(f.Size).AppendLine(") return false;");
        if (f.String)
        {
            b.Append("int ").Append(name).AppendLine("Length = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2)); offset += 2;");
            b.Append("if (").Append(name).Append("Length > ").Append(f.Bound).Append(" || source.Length - offset < ").Append(name).AppendLine("Length) return false;");
            b.Append("string ").Append(name).Append(" = __netUtf8.GetString(source.Slice(offset, ").Append(name).Append("Length)); offset += ").Append(name).AppendLine("Length;");
        }
        else
        {
            if (f.Primitive == SpecialType.System_Boolean) b.AppendLine("if (source[offset] > 1) return false;");
            string read = f.Primitive == SpecialType.System_Boolean ? "source[offset++] == 1"
                : f.Size == 1 ? "unchecked((" + PrimitiveName(f.Primitive) + ")source[offset++])"
                : "global::System.Buffers.Binary.BinaryPrimitives.Read" + BinaryMethod(f.Primitive) + "LittleEndian(source.Slice(offset, " + f.Size + "))";
            b.Append(f.Type).Append(' ').Append(name).Append(" = ").Append(f.Enum ? "(" + f.Type + ")" : "").Append(read).AppendLine(";");
            if (f.Size > 1) b.Append("offset += ").Append(f.Size).AppendLine(";");
        }
    }
}
