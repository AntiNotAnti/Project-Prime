using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.CustomWeapons.Packages;

public static class WeaponPackageLimits
{
    public const int MaximumArchiveBytes = 8 * 1024 * 1024;
    public const int MaximumExpandedBytes = 16 * 1024 * 1024;
    public const int MaximumEntries = 32, MaximumPathBytes = 128;
    public const int MaximumJsonBytes = 65536, MaximumTextBytes = 65536;
    public const int MaximumWaveFrames = 11025;
}

internal static class WeaponPackageJson
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static InvalidDataException Bad(string message)
    {
        // Diagnostics may reach a terminal or creator UI. Untrusted JSON keys
        // must not inject control sequences, line breaks or bidi formatting.
        var safe = new char[Math.Min(message.Length, 512)];
        for (int i = 0; i < safe.Length; i++) safe[i] = message[i] is >= ' ' and <= '~' ? message[i] : '?';
        return new("Weapon package: " + new string(safe));
    }

    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is <= 0 or > WeaponPackageLimits.MaximumJsonBytes) throw Bad("JSON byte budget.");
        try
        {
            _ = Utf8.GetString(bytes.Span);
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            try { int remaining = 4096; Visit(document.RootElement, ref remaining); return document; }
            catch { document.Dispose(); throw; }
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        { throw Bad("Invalid UTF-8/JSON: " + e.Message); }
    }

    private static void Visit(JsonElement value, ref int remaining)
    {
        if (--remaining < 0) throw Bad("JSON complexity budget.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Unicode(property.Name);
                if (!names.Add(property.Name)) throw Bad("Duplicate JSON field.");
                Visit(property.Value, ref remaining);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) Visit(child, ref remaining);
        else if (value.ValueKind == JsonValueKind.String) Unicode(value.GetString()!);
        else if (value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out double number) || !double.IsFinite(number)))
            throw Bad("Nonfinite number.");
    }

    private static void Unicode(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            { if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw Bad("Unpaired surrogate."); }
            else if (char.IsLowSurrogate(text[i])) throw Bad("Unpaired surrogate.");
        }
    }

    internal static void Keys(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Bad("Expected object.");
        int count = 0;
        foreach (var property in value.EnumerateObject())
        { if (!expected.Contains(property.Name, StringComparer.Ordinal)) throw Bad("Unsupported field: " + property.Name); count++; }
        if (count != expected.Length) throw Bad("Missing field."); // Parse already rejects duplicates recursively.
    }

    internal static string Text(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String) throw Bad("Expected text.");
        string text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)) throw Bad("Text budget/control character.");
        return text;
    }
    internal static void Is(JsonElement value, string expected)
    { if (Text(value, 128) != expected) throw Bad("Unsupported capability; expected " + expected + "."); }
    internal static void One(JsonElement value)
    { if (!value.TryGetInt32(out int schema) || schema != 1) throw Bad("Unsupported schema."); }

    internal static string Path(string path)
    {
        if (path.Length is < 1 or > WeaponPackageLimits.MaximumPathBytes) throw Bad("Path length.");
        var parts = path.Split('/');
        if (parts.Length > 8) throw Bad("Path depth.");
        foreach (string part in parts)
        {
            if (part.Length is < 1 or > 64 || part[0] == '.' || part[^1] == '.'
                || part.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_')))
                throw Bad("Unsafe package path.");
            // Permit conventional LICENSE/README filenames, but prohibit every
            // case alias with a separate ordinal-ignore-case entry set.
            string stem = part.Split('.')[0].ToLowerInvariant();
            if (stem is "con" or "prn" or "aux" or "nul"
                || stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal))
                    && stem[3] is >= '0' and <= '9') throw Bad("Device path.");
        }
        return path;
    }
    internal static string Path(JsonElement value, string suffix)
    {
        string path = Path(Text(value, WeaponPackageLimits.MaximumPathBytes));
        if (!path.EndsWith(suffix, StringComparison.Ordinal)) throw Bad("Unsupported resource type.");
        return path;
    }
    internal static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        return stream.ToArray();
    }
}

/// <summary>Strict authoring metadata. This does not grant or activate a weapon.</summary>
public sealed class WeaponProject
{
    public Guid ContentId { get; }
    public string ProductId { get; }
    public string Name { get; }
    public string Author { get; }
    public string LicenseId { get; }
    public string LicensePath { get; }
    public string ReadmePath { get; }
    public string MechanicsPath { get; }
    public string ModelPath { get; }
    public IReadOnlyList<string> AudioPaths { get; }
    internal readonly SortedDictionary<string, string> Required = new(StringComparer.Ordinal);
    internal const string LockPath = "dependencies.lock.json";
    internal static readonly byte[] EmptyLock = "{\"schema\":1,\"dependencies\":[]}"u8.ToArray();
    private static readonly string[] Cues = { "shot", "bolt", "reload", "empty" };

    private WeaponProject(JsonElement root)
    {
        WeaponPackageJson.Keys(root, "schema", "contentId", "productId", "name", "author", "license", "readme", "mechanics", "presentation", "dependencies");
        WeaponPackageJson.One(root.GetProperty("schema"));
        string id = WeaponPackageJson.Text(root.GetProperty("contentId"), 36);
        if (!Guid.TryParseExact(id, "D", out var content) || content == Guid.Empty || content.ToString("D") != id)
            throw WeaponPackageJson.Bad("Expected canonical nonempty content UUID.");
        ContentId = content;
        ProductId = WeaponPackageJson.Text(root.GetProperty("productId"), 64);
        if (ProductId[0] is not (>= 'a' and <= 'z') || ProductId.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            throw WeaponPackageJson.Bad("Invalid product identifier.");
        Name = WeaponPackageJson.Text(root.GetProperty("name"), 80);
        Author = WeaponPackageJson.Text(root.GetProperty("author"), 120);
        var license = root.GetProperty("license"); WeaponPackageJson.Keys(license, "spdx", "path");
        LicenseId = WeaponPackageJson.Text(license.GetProperty("spdx"), 64);
        if (LicenseId is not ("CC0-1.0" or "CC-BY-4.0" or "MIT" or "Apache-2.0" or "LicenseRef-Creator"))
            throw WeaponPackageJson.Bad("Unsupported license declaration.");
        LicensePath = Add(license.GetProperty("path"), ".txt", "text/plain");
        ReadmePath = Add(root.GetProperty("readme"), ".txt", "text/plain");
        MechanicsPath = Add(root.GetProperty("mechanics"), ".json", "application/vnd.projectprime.weapon-mechanics+json");
        WeaponPackageJson.Is(root.GetProperty("dependencies"), LockPath); AddPath(LockPath, "application/vnd.projectprime.weapon-lock+json");
        var presentation = root.GetProperty("presentation");
        WeaponPackageJson.Keys(presentation, "schema", "mount", "model", "fallback", "audio");
        WeaponPackageJson.One(presentation.GetProperty("schema"));
        WeaponPackageJson.Is(presentation.GetProperty("mount"), "native-arm-module-v1");
        WeaponPackageJson.Is(presentation.GetProperty("fallback"), "builtin-original-module-v1");
        var model = presentation.GetProperty("model"); WeaponPackageJson.Keys(model, "format", "path");
        WeaponPackageJson.Is(model.GetProperty("format"), "prime-rigid-boxes-v1");
        ModelPath = Add(model.GetProperty("path"), ".json", "application/vnd.projectprime.rigid-boxes+json");
        var audio = presentation.GetProperty("audio"); WeaponPackageJson.Keys(audio, "format", "shot", "bolt", "reload", "empty");
        WeaponPackageJson.Is(audio.GetProperty("format"), "pcm16-mono-22050-v1");
        AudioPaths = Array.AsReadOnly(Cues.Select(c => Add(audio.GetProperty(c), ".wav", "audio/wav")).ToArray());
    }
    private string Add(JsonElement value, string suffix, string media)
    { string path = WeaponPackageJson.Path(value, suffix); AddPath(path, media); return path; }
    private void AddPath(string path, string media)
    {
        if (path.Equals("weapon.json", StringComparison.OrdinalIgnoreCase) || path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            || Required.Keys.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase))) throw WeaponPackageJson.Bad("Aliased resource reference.");
        Required.Add(path, media);
    }
    public static WeaponProject Parse(ReadOnlyMemory<byte> bytes)
    {
        try { using var document = WeaponPackageJson.Parse(bytes); return new(document.RootElement); }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw WeaponPackageJson.Bad("Invalid project: " + e.Message); }
    }
    public byte[] ToCanonicalBytes() => WeaponPackageJson.Write(w =>
    {
        w.WriteStartObject(); w.WriteNumber("schema", 1); w.WriteString("contentId", ContentId.ToString("D"));
        w.WriteString("productId", ProductId); w.WriteString("name", Name); w.WriteString("author", Author);
        w.WriteStartObject("license"); w.WriteString("spdx", LicenseId); w.WriteString("path", LicensePath); w.WriteEndObject();
        w.WriteString("readme", ReadmePath); w.WriteString("mechanics", MechanicsPath);
        w.WriteStartObject("presentation"); w.WriteNumber("schema", 1); w.WriteString("mount", "native-arm-module-v1");
        w.WriteStartObject("model"); w.WriteString("format", "prime-rigid-boxes-v1"); w.WriteString("path", ModelPath); w.WriteEndObject();
        w.WriteString("fallback", "builtin-original-module-v1"); w.WriteStartObject("audio"); w.WriteString("format", "pcm16-mono-22050-v1");
        for (int i = 0; i < Cues.Length; i++) w.WriteString(Cues[i], AudioPaths[i]);
        w.WriteEndObject(); w.WriteEndObject(); w.WriteString("dependencies", LockPath); w.WriteEndObject();
    });
}
