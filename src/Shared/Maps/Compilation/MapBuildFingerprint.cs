using System;
using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public static class MapBuildFingerprint
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("ProjectPrime.MapBuild.v1\0");

    public static string Compute(MapProject project, string baseContentIdentity)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentIdentity);
        byte[] projectJson = JsonSerializer.SerializeToUtf8Bytes(project, MapJsonContext.Default.MapProject);
        byte[] canonicalProject = MapJson.Canonicalize(projectJson);
        IReadOnlyList<(string Name, string Hash)> dependencies = MapProjectContentHasher.Dependencies(project);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("compilerSchemaVersion", MapCompiler.CompilerSchemaVersion);
            writer.WriteString("projectSha256", MapJson.Sha256(canonicalProject));
            writer.WriteString("baseContentIdentity", MapDependencyAnalyzer.Analyze(project).RequiresBaseContent
                ? baseContentIdentity : "not-required");
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach ((string name, string hash) in dependencies.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", name);
                writer.WriteString("sha256", hash);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        byte[] payload = GC.AllocateUninitializedArray<byte>(Domain.Length + buffer.WrittenCount);
        Domain.CopyTo(payload, 0);
        buffer.WrittenSpan.CopyTo(payload.AsSpan(Domain.Length));
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    public static IReadOnlyList<(string Name, string Hash)> Dependencies(MapProject project)
        => MapProjectContentHasher.Dependencies(project);

}
