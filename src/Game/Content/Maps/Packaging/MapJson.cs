using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public static class MapJson
{
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        using JsonDocument document = ParseStrict(utf8Json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static JsonDocument ParseStrict(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(utf8Json.ToArray(),
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            RejectDuplicateMembers(document.RootElement, "$");
            return document;
        }
        catch (JsonException exception)
        {
            throw new MapPackageException("MAP-PKG-012", "Bundle JSON is malformed or contains duplicate members.", exception);
        }
    }

    public static string Sha256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RejectDuplicateMembers(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"Duplicate JSON member '{property.Name}' at {path}.");
                }
                RejectDuplicateMembers(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement child in element.EnumerateArray())
                RejectDuplicateMembers(child, $"{path}[{index++}]");
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement child in element.EnumerateArray()) WriteCanonical(writer, child);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long integer)) writer.WriteNumberValue(integer);
                else if (element.TryGetDecimal(out decimal decimalValue)) writer.WriteNumberValue(decimalValue);
                else writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported JSON token.");
        }
    }
}

public static class MapBundlePath
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".com", ".bat", ".cmd", ".ps1", ".sh",
        ".js", ".mjs", ".cjs", ".vbs", ".py", ".jar", ".app", ".scr"
    };

    public static string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\\') || path.StartsWith('/') || path.EndsWith('/')
            || path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
            throw new MapPackageException("MAP-PKG-002", $"Bundle path '{path}' is not a canonical relative path.");
        string[] parts = path.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new MapPackageException("MAP-PKG-002", $"Bundle path '{path}' contains an unsafe segment.");
        foreach (char character in path)
        {
            if (char.IsControl(character))
                throw new MapPackageException("MAP-PKG-002", "Bundle paths cannot contain control characters.");
        }
        if (ExecutableExtensions.Contains(Path.GetExtension(path)))
            throw new MapPackageException("MAP-PKG-010", $"Executable map content is forbidden: '{path}'.");
        return string.Join('/', parts);
    }
}
