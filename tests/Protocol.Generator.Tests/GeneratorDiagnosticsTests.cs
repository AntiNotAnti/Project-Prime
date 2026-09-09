using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using FruityPrime.Protocol.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FruityPrime.Protocol.Generator.Tests;

public sealed class GeneratorDiagnosticsTests
{
    [Fact]
    public void UnsupportedTypeIsRejectedAtCompileTime()
    {
        ImmutableArray<Diagnostic> diagnostics = Run("""
            using System;
            using MphRead.Mods.Network;
            [NetPacket(NetMessageType.World)]
            public readonly partial record struct BadPacket(DateTime Value);
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NETGEN002");
    }

    [Fact]
    public void UnboundedArrayIsRejectedAtCompileTime()
    {
        ImmutableArray<Diagnostic> diagnostics = Run("""
            using MphRead.Mods.Network;
            [NetPacket(NetMessageType.World)]
            public readonly partial record struct BadPacket(byte[] Values);
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NETGEN002");
    }

    [Fact]
    public void ContradictoryConstraintsAreRejectedAtCompileTime()
    {
        ImmutableArray<Diagnostic> diagnostics = Run("""
            using MphRead.Mods.Network;
            [NetPacket(NetMessageType.World)]
            public readonly partial record struct BadPacket(
                [property: NetRange(0, 10), property: NetUnsignedRange(0, 10)] byte Value);
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NETGEN004");
    }

    [Fact]
    public void ProtocolEightSchemaIsRejectedWithoutTouchingLegacyCodecs()
    {
        ImmutableArray<Diagnostic> diagnostics = Run("""
            using MphRead.Mods.Network;
            [NetPacket(NetMessageType.World, protocol: 8)]
            public readonly partial record struct BadPacket(byte Value);
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NETGEN005");
    }

    private static ImmutableArray<Diagnostic> Run(string packetSource)
    {
        const string attributes = """
            using System;
            namespace MphRead.Mods.Network
            {
                public enum NetMessageType : byte { World = 12 }
                [AttributeUsage(AttributeTargets.Struct)]
                public sealed class NetPacketAttribute(NetMessageType type, byte protocol = 9) : Attribute { }
                [AttributeUsage(AttributeTargets.Property)]
                public sealed class NetRangeAttribute(long minimum, long maximum) : Attribute { }
                [AttributeUsage(AttributeTargets.Property)]
                public sealed class NetUnsignedRangeAttribute(ulong minimum, ulong maximum) : Attribute { }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProtocolGeneratorFixture",
            new[] { CSharpSyntaxTree.ParseText(attributes), CSharpSyntaxTree.ParseText(packetSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new NetPacketGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);
        return diagnostics;
    }
}
