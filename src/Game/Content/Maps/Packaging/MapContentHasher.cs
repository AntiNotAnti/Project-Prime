using System;
using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public static class MapContentHasher
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("ProjectPrime.MapContent.v2\0");

    public static string Compute(MapManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", manifest.Format);
            writer.WriteString("stableId", manifest.StableId);
            writer.WriteString("version", manifest.Version.ToString());
            writer.WriteString("name", manifest.Name);
            writer.WriteString("author", manifest.Author);
            writer.WriteString("description", manifest.Description);
            writer.WriteBoolean("redistribution", manifest.Redistribution);
            writer.WriteString("recipe", manifest.Recipe);
            if (manifest.Preview != null) writer.WriteString("preview", manifest.Preview);
            writer.WritePropertyName("supportedModes");
            writer.WriteStartArray();
            foreach (MapMode mode in manifest.SupportedModes.Distinct().OrderBy(value => value))
                writer.WriteStringValue(mode.ToString());
            writer.WriteEndArray();
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (MapManifestFile file in manifest.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteNumber("size", file.Size);
                writer.WriteString("sha256", file.Sha256.ToLowerInvariant());
                writer.WriteString("role", file.Role.ToString());
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
}
