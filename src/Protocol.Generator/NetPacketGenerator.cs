using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FruityPrime.Protocol.Generator;

/// <summary>Generates bounded, allocation-free-on-write protocol 9 codecs.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class NetPacketGenerator : IIncrementalGenerator
{
    private const string PacketAttribute = "MphRead.Mods.Network.NetPacketAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<PacketModel> packets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PacketAttribute,
                static (_, _) => true,
                static (candidate, _) => PacketModel.Create(candidate))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(packets, static (productionContext, model) =>
        {
            model.Emit(productionContext);
        });
    }

    private sealed class PacketModel
    {
        private static readonly SymbolDisplayFormat FullyQualified =
            SymbolDisplayFormat.FullyQualifiedFormat;
        private static readonly DiagnosticDescriptor NotPartial = new(
            "NETGEN001", "Packet must be a partial record struct",
            "The generated protocol packet '{0}' must be declared as a partial record struct.",
            "ProtocolGenerator", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Unsupported = new(
            "NETGEN002", "Unsupported packet schema",
            "Protocol packet member '{0}' uses an unsupported or unbounded schema type: {1}.",
            "ProtocolGenerator", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor InvalidSchema = new(
            "NETGEN003", "Invalid packet schema",
            "Protocol packet '{0}' has an invalid schema: {1}.",
            "ProtocolGenerator", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor InvalidMember = new(
            "NETGEN004", "Invalid packet member constraint",
            "Protocol packet member '{0}' has an invalid constraint: {1}.",
            "ProtocolGenerator", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor ProtocolVersion = new(
            "NETGEN005", "Unsupported generated protocol",
            "Generated packet '{0}' declares protocol {1}; the MVP only generates Protocol 9 additions.",
            "ProtocolGenerator", DiagnosticSeverity.Error, true);

        private readonly INamedTypeSymbol _symbol;
        private readonly ImmutableArray<FieldModel> _fields;
        private readonly ImmutableArray<DiagnosticInfo> _diagnostics;
        private readonly int _messageType;
        private readonly int _protocol;

        private PacketModel(INamedTypeSymbol symbol, int messageType, int protocol,
            ImmutableArray<FieldModel> fields, ImmutableArray<DiagnosticInfo> diagnostics)
        {
            _symbol = symbol;
            _messageType = messageType;
            _protocol = protocol;
            _fields = fields;
            _diagnostics = diagnostics;
        }

        public static PacketModel Create(GeneratorAttributeSyntaxContext context)
        {
            var symbol = (INamedTypeSymbol)context.TargetSymbol;

            var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            AttributeData attribute = context.Attributes[0];
            int messageType = ReadInt(attribute.ConstructorArguments, 0, -1);
            int protocol = ReadInt(attribute.ConstructorArguments, 1, 9);
            if (messageType is < 0 or > byte.MaxValue)
            {
                diagnostics.Add(new(InvalidSchema, symbol.Locations.FirstOrDefault(),
                    symbol.Name, "message type must be between 0 and 255"));
            }
            if (protocol != 9)
            {
                diagnostics.Add(new(ProtocolVersion, symbol.Locations.FirstOrDefault(), symbol.Name, protocol));
            }

            bool partial = symbol.DeclaringSyntaxReferences.Any(reference =>
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax declaration) return false;
                return declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
            });
            if (symbol.TypeKind != TypeKind.Struct || !symbol.IsRecord || !partial || symbol.ContainingType is not null)
            {
                diagnostics.Add(new(NotPartial, symbol.Locations.FirstOrDefault(), symbol.Name));
            }

            if (symbol.GetMembers().Any(member => member.Name is "MinimumSize" or "MaximumSize" or "Size"
                    or "EncodedSize" or "Validate" or "Write" or "TryRead"))
            {
                diagnostics.Add(new(InvalidSchema, symbol.Locations.FirstOrDefault(), symbol.Name,
                    "generated member name is already declared"));
            }

            ImmutableArray<IPropertySymbol> properties = symbol.GetMembers().OfType<IPropertySymbol>()
                .Where(property => !property.IsStatic && property.GetMethod is not null)
                .OrderBy(property => property.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
                .ToImmutableArray();
            if (properties.Length == 0)
            {
                diagnostics.Add(new(InvalidSchema, symbol.Locations.FirstOrDefault(), symbol.Name,
                    "at least one readable instance property is required"));
            }

            var fields = ImmutableArray.CreateBuilder<FieldModel>();
            foreach (IPropertySymbol property in properties)
            {
                fields.Add(BuildField(property, property.Type, property.GetAttributes(), diagnostics, property.Name));
            }

            if (diagnostics.Count == 0 && fields.Any(field => field is null))
            {
                diagnostics.Add(new(InvalidSchema, symbol.Locations.FirstOrDefault(), symbol.Name,
                    "one or more members could not be represented"));
            }

            return new PacketModel(symbol, messageType, protocol, fields.ToImmutable(), diagnostics.ToImmutable());
        }

        public void Emit(SourceProductionContext context)
        {
            foreach (DiagnosticInfo diagnostic in _diagnostics)
            {
                context.ReportDiagnostic(diagnostic.Create());
            }
            if (_diagnostics.Length != 0 || _fields.Any(field => field is null)) return;

            string source = GenerateSource();
            context.AddSource(_symbol.Name + ".NetPacket.g.cs", source);
        }

        private string GenerateSource()
        {
            string ns = _symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : "namespace " + _symbol.ContainingNamespace.ToDisplayString() + "\n{\n";
            string closeNamespace = _symbol.ContainingNamespace.IsGlobalNamespace ? "" : "}\n";
            var builder = new StringBuilder(8192);
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Buffers.Binary;");
            builder.AppendLine("using System.Text;");
            builder.AppendLine(ns);
            builder.Append("partial record struct ").Append(_symbol.Name).AppendLine();
            builder.AppendLine("{");
            builder.Append("    public const byte Protocol = ").Append(_protocol).AppendLine(";");
            builder.Append("    public const global::MphRead.Mods.Network.NetMessageType MessageType = (global::MphRead.Mods.Network.NetMessageType)")
                .Append(_messageType).AppendLine(";");
            int size = _fields.Sum(field => field.Size);
            builder.Append("    public const int MinimumSize = ").Append(size).AppendLine(";");
            builder.Append("    public const int MaximumSize = ").Append(size).AppendLine(";");
            builder.AppendLine("    public const int Size = MaximumSize;");
            builder.AppendLine("    public readonly int EncodedSize => Size;");

            if (_fields.Any(field => field.Kind == FieldKind.String || field.ContainsString))
            {
                builder.AppendLine("    private static readonly UTF8Encoding Utf8 = new(false, true);");
            }

            EmitValidate(builder);
            EmitWrite(builder);
            EmitTryRead(builder);
            EmitEnumHelpers(builder);
            EmitStringHelper(builder);
            builder.AppendLine("}");
            builder.Append(closeNamespace);
            return builder.ToString();
        }

        private void EmitValidate(StringBuilder builder)
        {
            builder.AppendLine("    public readonly bool Validate()");
            builder.AppendLine("    {");
            foreach (FieldModel field in _fields)
            {
                EmitValidation(builder, field, field.AccessExpression, "        ");
            }
            builder.AppendLine("        return true;");
            builder.AppendLine("    }");
        }

        private void EmitValidation(StringBuilder builder, FieldModel field, string expression, string indent)
        {
            if (field.Kind == FieldKind.Array)
            {
                builder.Append(indent).Append("if (").Append(expression).Append(" is null || ")
                    .Append(expression).Append(".Length != ").Append(field.ArrayCount).AppendLine(") return false;");
                for (int i = 0; i < field.ArrayCount; i++)
                {
                    EmitValidation(builder, field.Element!, expression + "[" + i + "]", indent);
                }
                return;
            }
            if (field.Kind == FieldKind.String)
            {
                builder.Append(indent).Append("if (").Append(expression).Append(" is null || !IsValidUtf8(")
                    .Append(expression).Append(", ").Append(field.StringBytes).AppendLine(")) return false;");
            }
            if (field.Kind == FieldKind.Enum)
            {
                builder.Append(indent).Append("if (!IsValid_").Append(field.HelperName).Append('(')
                    .Append(expression).AppendLine(")) return false;");
            }
            if (field.Kind == FieldKind.Vector3 && field.Finite)
            {
                builder.Append(indent).Append("if (!float.IsFinite(").Append(expression).Append(".X) || !float.IsFinite(")
                    .Append(expression).Append(".Y) || !float.IsFinite(").Append(expression).AppendLine(".Z)) return false;");
            }
            if (field.Range is { } range)
            {
                builder.Append(indent).Append("if (").Append(expression).Append(' ')
                    .Append(range.MinimumExpression).Append(" || ").Append(expression).Append(' ')
                    .Append(range.MaximumExpression).AppendLine(") return false;");
            }
        }

        private void EmitWrite(StringBuilder builder)
        {
            builder.AppendLine("    public readonly void Write(Span<byte> destination)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (destination.Length < Size) throw new ArgumentException(\"Destination is smaller than the packet.\", nameof(destination));");
            builder.AppendLine("        if (!Validate()) throw new ArgumentException(\"Packet validation failed.\", nameof(destination));");
            int offset = 0;
            foreach (FieldModel field in _fields)
            {
                EmitWriteField(builder, field, field.AccessExpression, offset, "        ");
                offset += field.Size;
            }
            builder.AppendLine("    }");
        }

        private void EmitWriteField(StringBuilder builder, FieldModel field, string expression, int offset, string indent)
        {
            if (field.Kind == FieldKind.Array)
            {
                int elementOffset = offset;
                for (int i = 0; i < field.ArrayCount; i++)
                {
                    EmitWriteField(builder, field.Element!, expression + "[" + i + "]", elementOffset, indent);
                    elementOffset += field.Element!.Size;
                }
                return;
            }
            if (field.Kind == FieldKind.String)
            {
                builder.Append(indent).Append("destination.Slice(").Append(offset).Append(", ").Append(field.StringBytes).AppendLine(").Clear();");
                builder.Append(indent).Append("Utf8.GetBytes(").Append(expression).Append(".AsSpan(), destination.Slice(")
                    .Append(offset).Append(", ").Append(field.StringBytes - 1).AppendLine(")); // validation reserves the terminator");
                return;
            }
            string target = "destination.Slice(" + offset + ", " + field.Size + ")";
            switch (field.Kind)
            {
                case FieldKind.Byte:
                case FieldKind.SByte:
                case FieldKind.Bool:
                    builder.Append(indent).Append(target).Append("[0] = ").Append(field.Kind == FieldKind.Bool ? "(byte)(" + expression + " ? 1 : 0)" : "unchecked((byte)" + expression + ")").AppendLine(";");
                    break;
                case FieldKind.UShort:
                    builder.Append(indent).Append("BinaryPrimitives.WriteUInt16LittleEndian(").Append(target).Append(", unchecked((ushort)").Append(expression).AppendLine("));");
                    break;
                case FieldKind.Short:
                    builder.Append(indent).Append("BinaryPrimitives.WriteInt16LittleEndian(").Append(target).Append(", ").Append(expression).AppendLine(");");
                    break;
                case FieldKind.UInt:
                    builder.Append(indent).Append("BinaryPrimitives.WriteUInt32LittleEndian(").Append(target).Append(", unchecked((uint)").Append(expression).AppendLine("));");
                    break;
                case FieldKind.Int:
                    builder.Append(indent).Append("BinaryPrimitives.WriteInt32LittleEndian(").Append(target).Append(", ").Append(expression).AppendLine(");");
                    break;
                case FieldKind.ULong:
                    builder.Append(indent).Append("BinaryPrimitives.WriteUInt64LittleEndian(").Append(target).Append(", ").Append(expression).AppendLine(");");
                    break;
                case FieldKind.Long:
                    builder.Append(indent).Append("BinaryPrimitives.WriteInt64LittleEndian(").Append(target).Append(", ").Append(expression).AppendLine(");");
                    break;
                case FieldKind.Enum:
                    EmitWriteUnderlying(builder, field, expression, target, indent);
                    break;
                case FieldKind.Guid:
                    builder.Append(indent).Append(expression).Append(".TryWriteBytes(").Append(target).AppendLine(");");
                    break;
                case FieldKind.Vector3:
                    builder.Append(indent).Append("BinaryPrimitives.WriteSingleLittleEndian(").Append(target).Append(", ").Append(expression).AppendLine(".X);");
                    builder.Append(indent).Append("BinaryPrimitives.WriteSingleLittleEndian(").Append(target).Append("[4..], ").Append(expression).AppendLine(".Y);");
                    builder.Append(indent).Append("BinaryPrimitives.WriteSingleLittleEndian(").Append(target).Append("[8..], ").Append(expression).AppendLine(".Z);");
                    break;
            }
        }

        private static void EmitWriteUnderlying(StringBuilder builder, FieldModel field, string expression, string target, string indent)
        {
            string cast = "(" + field.EnumUnderlyingDisplay + ")" + expression;
            switch (field.EnumUnderlyingKind)
            {
                case FieldKind.Byte:
                case FieldKind.SByte:
                    builder.Append(indent).Append(target).Append("[0] = unchecked((byte)").Append(cast).AppendLine(");"); break;
                case FieldKind.UShort:
                    builder.Append(indent).Append("BinaryPrimitives.WriteUInt16LittleEndian(").Append(target).Append(", unchecked((ushort)").Append(cast).AppendLine("));"); break;
                case FieldKind.Short:
                    builder.Append(indent).Append("BinaryPrimitives.WriteInt16LittleEndian(").Append(target).Append(", unchecked((short)").Append(cast).AppendLine("));"); break;
                case FieldKind.UInt:
                    builder.Append(indent).Append("BinaryPrimitives.WriteUInt32LittleEndian(").Append(target).Append(", unchecked((uint)").Append(cast).AppendLine("));"); break;
                case FieldKind.Int:
                    builder.Append(indent).Append("BinaryPrimitives.WriteInt32LittleEndian(").Append(target).Append(", unchecked((int)").Append(cast).AppendLine("));"); break;
                case FieldKind.ULong:
                    builder.Append(indent).Append("BinaryPrimitives.WriteUInt64LittleEndian(").Append(target).Append(", unchecked((ulong)").Append(cast).AppendLine(");"); break;
                case FieldKind.Long:
                    builder.Append(indent).Append("BinaryPrimitives.WriteInt64LittleEndian(").Append(target).Append(", unchecked((long)").Append(cast).AppendLine(");"); break;
            }
        }

        private void EmitTryRead(StringBuilder builder)
        {
            builder.AppendLine("    public static bool TryRead(ReadOnlySpan<byte> source, out " + _symbol.Name + " value)");
            builder.AppendLine("    {");
            builder.AppendLine("        value = default;");
            builder.AppendLine("        if (source.Length != Size) return false;");
            int offset = 0;
            foreach (FieldModel field in _fields)
            {
                EmitReadField(builder, field, field.ParameterName, offset, "        ", declare: true);
                offset += field.Size;
            }
            builder.Append("        var parsed = new ").Append(_symbol.Name).Append('(')
                .Append(string.Join(", ", _fields.Select(field => field.ParameterName))).AppendLine(");");
            builder.AppendLine("        if (!parsed.Validate()) return false;");
            builder.AppendLine("        value = parsed;");
            builder.AppendLine("        return true;");
            builder.AppendLine("    }");
        }

        private void EmitReadField(StringBuilder builder, FieldModel field, string variable, int offset, string indent, bool declare)
        {
            if (field.Kind == FieldKind.Array)
            {
                builder.Append(indent).Append("var ").Append(variable).Append(" = new ").Append(field.Element!.TypeDisplay)
                    .Append('[').Append(field.ArrayCount).AppendLine("];" );
                int elementOffset = offset;
                for (int i = 0; i < field.ArrayCount; i++)
                {
                    EmitReadField(builder, field.Element!, variable + "[" + i + "]", elementOffset, indent, declare: false);
                    elementOffset += field.Element!.Size;
                }
                return;
            }
            if (field.Kind == FieldKind.String)
            {
                builder.Append(indent).Append("if (!TryReadUtf8(source.Slice(").Append(offset).Append(", ").Append(field.StringBytes)
                    .Append("), ").Append(field.StringBytes).Append(", out string ").Append(variable).AppendLine(")) return false;");
                return;
            }
            string sourceSlice = "source.Slice(" + offset + ", " + field.Size + ")";
            switch (field.Kind)
            {
                case FieldKind.Byte: EmitReadAssignment(builder, indent, "byte", variable, sourceSlice + "[0]", declare); break;
                case FieldKind.SByte: EmitReadAssignment(builder, indent, "sbyte", variable, "unchecked((sbyte)" + sourceSlice + "[0])", declare); break;
                case FieldKind.Bool:
                    builder.Append(indent).Append("if (").Append(sourceSlice).AppendLine("[0] > 1) return false;");
                    EmitReadAssignment(builder, indent, "bool", variable, sourceSlice + "[0] != 0", declare); break;
                case FieldKind.UShort: EmitReadAssignment(builder, indent, "ushort", variable, "BinaryPrimitives.ReadUInt16LittleEndian(" + sourceSlice + ")", declare); break;
                case FieldKind.Short: EmitReadAssignment(builder, indent, "short", variable, "BinaryPrimitives.ReadInt16LittleEndian(" + sourceSlice + ")", declare); break;
                case FieldKind.UInt: EmitReadAssignment(builder, indent, "uint", variable, "BinaryPrimitives.ReadUInt32LittleEndian(" + sourceSlice + ")", declare); break;
                case FieldKind.Int: EmitReadAssignment(builder, indent, "int", variable, "BinaryPrimitives.ReadInt32LittleEndian(" + sourceSlice + ")", declare); break;
                case FieldKind.ULong: EmitReadAssignment(builder, indent, "ulong", variable, "BinaryPrimitives.ReadUInt64LittleEndian(" + sourceSlice + ")", declare); break;
                case FieldKind.Long: EmitReadAssignment(builder, indent, "long", variable, "BinaryPrimitives.ReadInt64LittleEndian(" + sourceSlice + ")", declare); break;
                case FieldKind.Enum:
                    builder.Append(indent);
                    if (declare) builder.Append(field.TypeDisplay).Append(' ');
                    builder.Append(variable).Append(" = (" ).Append(field.TypeDisplay).Append(')');
                    EmitReadUnderlying(builder, field, sourceSlice);
                    builder.AppendLine(";");
                    break;
                case FieldKind.Guid:
                    builder.Append(indent);
                    if (declare) builder.Append("var ");
                    builder.Append(variable).Append(" = new Guid(").Append(sourceSlice).AppendLine(");");
                    break;
                case FieldKind.Vector3:
                    builder.Append(indent);
                    if (declare) builder.Append("var ");
                    builder.Append(variable).Append(" = new ").Append(field.TypeDisplay).Append("(BinaryPrimitives.ReadSingleLittleEndian(").Append(sourceSlice)
                        .Append("), BinaryPrimitives.ReadSingleLittleEndian(").Append(sourceSlice).Append("[4..]), BinaryPrimitives.ReadSingleLittleEndian(").Append(sourceSlice).AppendLine("[8..]));");
                    break;
            }
        }

        private static void EmitReadAssignment(StringBuilder builder, string indent, string type,
            string variable, string expression, bool declare)
        {
            builder.Append(indent);
            if (declare) builder.Append(type).Append(' ');
            builder.Append(variable).Append(" = ").Append(expression).AppendLine(";");
        }

        private static void EmitReadUnderlying(StringBuilder builder, FieldModel field, string sourceSlice)
        {
            switch (field.EnumUnderlyingKind)
            {
                case FieldKind.Byte: builder.Append(sourceSlice).Append("[0]"); break;
                case FieldKind.SByte: builder.Append("unchecked((sbyte)").Append(sourceSlice).Append("[0])"); break;
                case FieldKind.UShort: builder.Append("BinaryPrimitives.ReadUInt16LittleEndian(").Append(sourceSlice).Append(')'); break;
                case FieldKind.Short: builder.Append("BinaryPrimitives.ReadInt16LittleEndian(").Append(sourceSlice).Append(')'); break;
                case FieldKind.UInt: builder.Append("BinaryPrimitives.ReadUInt32LittleEndian(").Append(sourceSlice).Append(')'); break;
                case FieldKind.Int: builder.Append("BinaryPrimitives.ReadInt32LittleEndian(").Append(sourceSlice).Append(')'); break;
                case FieldKind.ULong: builder.Append("BinaryPrimitives.ReadUInt64LittleEndian(").Append(sourceSlice).Append(')'); break;
                case FieldKind.Long: builder.Append("BinaryPrimitives.ReadInt64LittleEndian(").Append(sourceSlice).Append(')'); break;
            }
        }

        private void EmitEnumHelpers(StringBuilder builder)
        {
            foreach (FieldModel field in _fields.SelectMany(Flatten).Where(field => field.Kind == FieldKind.Enum)
                .GroupBy(field => field.HelperName).Select(group => group.First()))
            {
                builder.Append("    private static bool IsValid_").Append(field.HelperName).Append('(').Append(field.TypeDisplay).AppendLine(" value)");
                builder.AppendLine("    {");
                if (field.EnumMembers.Length == 0)
                {
                    builder.AppendLine("        return false;");
                }
                else
                {
                    builder.Append("        return ").Append(string.Join(" || ", field.EnumMembers.Select(member => "value == " + member))).AppendLine(";");
                }
                builder.AppendLine("    }");
            }
        }

        private static IEnumerable<FieldModel> Flatten(FieldModel field)
        {
            yield return field;
            if (field.Kind == FieldKind.Array)
            {
                foreach (FieldModel child in Flatten(field.Element!)) yield return child;
            }
        }

        private void EmitStringHelper(StringBuilder builder)
        {
            if (!_fields.Any(field => field.Kind == FieldKind.String || field.ContainsString)) return;
            builder.AppendLine("    private static bool IsValidUtf8(string value, int maximumBytes)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value.IndexOf('\\0') >= 0) return false;");
            builder.AppendLine("        try { return Utf8.GetByteCount(value) < maximumBytes; }");
            builder.AppendLine("        catch (EncoderFallbackException) { return false; }");
            builder.AppendLine("    }");
            builder.AppendLine("    private static bool TryReadUtf8(ReadOnlySpan<byte> source, int maximumBytes, out string value)");
            builder.AppendLine("    {");
            builder.AppendLine("        value = string.Empty;");
            builder.AppendLine("        int terminator = source.IndexOf((byte)0);");
            builder.AppendLine("        if (terminator < 0) return false;");
            builder.AppendLine("        for (int i = terminator + 1; i < maximumBytes; i++) if (source[i] != 0) return false;");
            builder.AppendLine("        try { value = Utf8.GetString(source[..terminator]); return true; }");
            builder.AppendLine("        catch (DecoderFallbackException) { return false; }");
            builder.AppendLine("    }");
        }

        private static FieldModel BuildField(IPropertySymbol property, ITypeSymbol type,
            ImmutableArray<AttributeData> attributes, ImmutableArray<DiagnosticInfo>.Builder diagnostics, string name)
        {
            AttributeData? arrayAttribute = Find(attributes, "MphRead.Mods.Network.NetArrayAttribute");
            AttributeData? stringAttribute = Find(attributes, "MphRead.Mods.Network.NetUtf8StringAttribute");
            AttributeData? finiteAttribute = Find(attributes, "MphRead.Mods.Network.NetFiniteAttribute");
            AttributeData? signedRange = Find(attributes, "MphRead.Mods.Network.NetRangeAttribute");
            AttributeData? unsignedRange = Find(attributes, "MphRead.Mods.Network.NetUnsignedRangeAttribute");
            if (signedRange is not null && unsignedRange is not null)
            {
                diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                    "signed and unsigned ranges cannot both be present"));
            }
            RangeModel? range = ReadRange(type, signedRange, unsignedRange, property, diagnostics, name);
            if (finiteAttribute is not null && !IsVector(type))
            {
                diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                    "NetFinite is only valid on Vector3"));
            }

            if (arrayAttribute is not null)
            {
                int count = ReadInt(arrayAttribute.ConstructorArguments, 0, 0);
                if (type is not IArrayTypeSymbol array || array.Rank != 1 || count is < 1 or > 256)
                {
                    diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                        "NetArray requires a one-dimensional array count between 1 and 256"));
                    return FieldModel.Invalid(name);
                }
                if (stringAttribute is not null || finiteAttribute is not null)
                {
                    diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                        "array members cannot carry string or finite constraints"));
                }
                FieldModel? element = BuildScalarField(property, array.ElementType, property.Locations.FirstOrDefault(), diagnostics, name, range: null, finite: false);
                if (element is null || element.Kind is FieldKind.Invalid or FieldKind.Array or FieldKind.String)
                {
                    diagnostics.Add(new(Unsupported, property.Locations.FirstOrDefault(), name,
                        "fixed arrays only support fixed-width scalar, enum, Guid, or Vector3 elements"));
                    return FieldModel.Invalid(name);
                }
                return FieldModel.Array(name, array.ElementType.ToDisplayString(FullyQualified), count, element);
            }
            if (stringAttribute is not null)
            {
                int maximumBytes = ReadInt(stringAttribute.ConstructorArguments, 0, 0);
                if (type.SpecialType != SpecialType.System_String || maximumBytes is < 2 or > 4096)
                {
                    diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                        "NetUtf8String requires a string maximum between 2 and 4096 bytes"));
                    return FieldModel.Invalid(name);
                }
                if (finiteAttribute is not null || range is not null)
                {
                    diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                        "strings cannot carry numeric or finite constraints"));
                }
                return FieldModel.String(name, maximumBytes);
            }
            if (finiteAttribute is not null && !IsVector(type)) return FieldModel.Invalid(name);
            FieldModel? scalar = BuildScalarField(property, type, property.Locations.FirstOrDefault(), diagnostics, name, range, finiteAttribute is not null);
            return scalar ?? FieldModel.Invalid(name);
        }

        private static FieldModel? BuildScalarField(IPropertySymbol property, ITypeSymbol type, Location? location,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics, string name, RangeModel? range, bool finite)
        {
            FieldKind kind = type.SpecialType switch
            {
                SpecialType.System_Byte => FieldKind.Byte,
                SpecialType.System_SByte => FieldKind.SByte,
                SpecialType.System_UInt16 => FieldKind.UShort,
                SpecialType.System_Int16 => FieldKind.Short,
                SpecialType.System_UInt32 => FieldKind.UInt,
                SpecialType.System_Int32 => FieldKind.Int,
                SpecialType.System_UInt64 => FieldKind.ULong,
                SpecialType.System_Int64 => FieldKind.Long,
                SpecialType.System_Boolean => FieldKind.Bool,
                _ => FieldKind.Invalid
            };
            if (kind != FieldKind.Invalid)
            {
                return FieldModel.Scalar(name, type.ToDisplayString(FullyQualified), kind, range, finite: false);
            }
            if (type.ToDisplayString(FullyQualified) == "global::System.Guid")
            {
                if (range is not null || finite)
                {
                    diagnostics.Add(new(InvalidMember, location, name, "Guid cannot carry numeric or finite constraints"));
                }
                return FieldModel.Scalar(name, type.ToDisplayString(FullyQualified), FieldKind.Guid, null, false);
            }
            if (IsVector(type))
            {
                if (range is not null) diagnostics.Add(new(InvalidMember, location, name, "Vector3 cannot carry numeric ranges"));
                return FieldModel.Scalar(name, type.ToDisplayString(FullyQualified), FieldKind.Vector3, null, finite);
            }
            if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
            {
                if (range is not null || finite)
                {
                    diagnostics.Add(new(InvalidMember, location, name, "enum cannot carry numeric or finite constraints"));
                }
                ITypeSymbol underlying = enumType.EnumUnderlyingType!;
                FieldModel? underlyingField = BuildScalarField(property, underlying, location, diagnostics, name, null, false);
                if (underlyingField is null || underlyingField.Kind is FieldKind.Invalid or FieldKind.Bool)
                {
                    diagnostics.Add(new(Unsupported, location, name, "enum underlying type must be an integral scalar"));
                    return FieldModel.Invalid(name);
                }
                string typeDisplay = type.ToDisplayString(FullyQualified);
                string helper = MakeIdentifier(name + "_" + enumType.Name);
                ImmutableArray<string> members = enumType.GetMembers().OfType<IFieldSymbol>()
                    .Where(field => field.HasConstantValue)
                    .Select(field => typeDisplay + "." + field.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray();
                return FieldModel.Enum(name, typeDisplay, underlyingField.Kind, helper, members);
            }
            diagnostics.Add(new(Unsupported, location, name, type.ToDisplayString(FullyQualified)));
            return FieldModel.Invalid(name);
        }

        private static RangeModel? ReadRange(ITypeSymbol type, AttributeData? signed, AttributeData? unsigned,
            IPropertySymbol property, ImmutableArray<DiagnosticInfo>.Builder diagnostics, string name)
        {
            if (signed is not null)
            {
                long minimum = ReadLong(signed.ConstructorArguments, 0, long.MinValue);
                long maximum = ReadLong(signed.ConstructorArguments, 1, long.MaxValue);
                if (minimum > maximum || !IsSigned(type))
                {
                    diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                        "signed range requires a signed integral member and minimum <= maximum"));
                    return null;
                }
                return RangeModel.Signed(minimum, maximum);
            }
            if (unsigned is not null)
            {
                ulong minimum = ReadULong(unsigned.ConstructorArguments, 0, 0);
                ulong maximum = ReadULong(unsigned.ConstructorArguments, 1, ulong.MaxValue);
                if (minimum > maximum || !IsUnsigned(type))
                {
                    diagnostics.Add(new(InvalidMember, property.Locations.FirstOrDefault(), name,
                        "unsigned range requires an unsigned integral member and minimum <= maximum"));
                    return null;
                }
                return RangeModel.Unsigned(minimum, maximum);
            }
            return null;
        }

        private static bool IsSigned(ITypeSymbol type) => type.SpecialType is SpecialType.System_SByte or SpecialType.System_Int16
            or SpecialType.System_Int32 or SpecialType.System_Int64;
        private static bool IsUnsigned(ITypeSymbol type) => type.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16
            or SpecialType.System_UInt32 or SpecialType.System_UInt64;
        private static bool IsVector(ITypeSymbol type) => type.ToDisplayString(FullyQualified) == "global::OpenTK.Mathematics.Vector3";
        private static AttributeData? Find(ImmutableArray<AttributeData> attributes, string metadataName)
            => attributes.FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);
        private static int ReadInt(ImmutableArray<TypedConstant> arguments, int index, int fallback)
            => index < arguments.Length && arguments[index].Value is not null ? Convert.ToInt32(arguments[index].Value) : fallback;
        private static long ReadLong(ImmutableArray<TypedConstant> arguments, int index, long fallback)
            => index < arguments.Length && arguments[index].Value is not null ? Convert.ToInt64(arguments[index].Value) : fallback;
        private static ulong ReadULong(ImmutableArray<TypedConstant> arguments, int index, ulong fallback)
            => index < arguments.Length && arguments[index].Value is not null ? Convert.ToUInt64(arguments[index].Value) : fallback;
        private static string MakeIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            }
            return builder.ToString();
        }

        private sealed class DiagnosticInfo
        {
            private readonly DiagnosticDescriptor _descriptor;
            private readonly Location? _location;
            private readonly object[] _arguments;
            public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, params object[] arguments)
            {
                _descriptor = descriptor; _location = location; _arguments = arguments;
            }
            public Diagnostic Create() => Diagnostic.Create(_descriptor, _location, _arguments);
        }

        private sealed class RangeModel
        {
            public string MinimumExpression { get; }
            public string MaximumExpression { get; }
            private RangeModel(string minimumExpression, string maximumExpression)
            { MinimumExpression = minimumExpression; MaximumExpression = maximumExpression; }
            public static RangeModel Signed(long minimum, long maximum)
                => new("< " + Literal(minimum), "> " + Literal(maximum));
            public static RangeModel Unsigned(ulong minimum, ulong maximum)
                => new("< " + Literal(minimum), "> " + Literal(maximum));
            private static string Literal(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L";
            private static string Literal(ulong value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL";
        }

        private enum FieldKind { Invalid, Byte, SByte, UShort, Short, UInt, Int, ULong, Long, Bool, Enum, Guid, Vector3, String, Array }

        private sealed class FieldModel
        {
            public string Name { get; }
            public string TypeDisplay { get; }
            public FieldKind Kind { get; }
            public int Size { get; }
            public RangeModel? Range { get; }
            public bool Finite { get; }
            public string ParameterName => "value_" + MakeIdentifier(Name);
            public string AccessExpression => "@" + Name;
            public int ArrayCount { get; }
            public FieldModel? Element { get; }
            public int StringBytes { get; }
            public string HelperName { get; }
            public string EnumUnderlyingDisplay { get; }
            public FieldKind EnumUnderlyingKind { get; }
            public ImmutableArray<string> EnumMembers { get; }
            public bool ContainsString => Kind == FieldKind.String || Kind == FieldKind.Array && Element!.ContainsString;

            private FieldModel(string name, string typeDisplay, FieldKind kind, int size, RangeModel? range, bool finite,
                int arrayCount = 0, FieldModel? element = null, int stringBytes = 0, string helperName = "",
                string enumUnderlyingDisplay = "", FieldKind enumUnderlyingKind = FieldKind.Invalid,
                ImmutableArray<string> enumMembers = default)
            {
                Name = name; TypeDisplay = typeDisplay; Kind = kind; Size = size; Range = range; Finite = finite;
                ArrayCount = arrayCount; Element = element; StringBytes = stringBytes; HelperName = helperName;
                EnumUnderlyingDisplay = enumUnderlyingDisplay; EnumUnderlyingKind = enumUnderlyingKind;
                EnumMembers = enumMembers.IsDefault ? ImmutableArray<string>.Empty : enumMembers;
            }

            public static FieldModel Invalid(string name) => new(name, "", FieldKind.Invalid, 0, null, false);
            public static FieldModel Scalar(string name, string typeDisplay, FieldKind kind, RangeModel? range, bool finite)
                => new(name, typeDisplay, kind, ScalarSize(kind), range, finite);
            public static FieldModel Enum(string name, string typeDisplay, FieldKind underlyingKind, string helper,
                ImmutableArray<string> members)
                => new(name, typeDisplay, FieldKind.Enum, ScalarSize(underlyingKind), null, false,
                    helperName: helper, enumUnderlyingDisplay: UnderlyingDisplay(underlyingKind),
                    enumUnderlyingKind: underlyingKind, enumMembers: members);
            public static FieldModel String(string name, int bytes) => new(name, "string", FieldKind.String, bytes, null, false, stringBytes: bytes);
            public static FieldModel Array(string name, string typeDisplay, int count, FieldModel element)
                => new(name, typeDisplay + "[]", FieldKind.Array, checked(count * element.Size), null, false,
                    arrayCount: count, element: element);
            private static int ScalarSize(FieldKind kind) => kind switch
            {
                FieldKind.Byte or FieldKind.SByte or FieldKind.Bool => 1,
                FieldKind.UShort or FieldKind.Short => 2,
                FieldKind.UInt or FieldKind.Int or FieldKind.Vector3 => kind == FieldKind.Vector3 ? 12 : 4,
                FieldKind.ULong or FieldKind.Long => 8,
                FieldKind.Guid => 16,
                FieldKind.Enum => 1,
                _ => 0
            };
            private static string UnderlyingDisplay(FieldKind kind) => kind switch
            {
                FieldKind.Byte => "byte", FieldKind.SByte => "sbyte", FieldKind.UShort => "ushort", FieldKind.Short => "short",
                FieldKind.UInt => "uint", FieldKind.Int => "int", FieldKind.ULong => "ulong", FieldKind.Long => "long", _ => "byte"
            };
        }
    }
}
