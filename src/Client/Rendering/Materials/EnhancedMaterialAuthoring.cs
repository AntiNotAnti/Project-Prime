using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead;

public sealed record EnhancedMaterialInventory
{
    public const int CurrentFormat = 1;
    public int Format { get; init; } = CurrentFormat;
    public EnhancedMaterialInventoryEntry[] Textures { get; init; }
        = Array.Empty<EnhancedMaterialInventoryEntry>();
}

public sealed record EnhancedMaterialInventoryEntry
{
    public string Key { get; init; } = String.Empty;
    public EnhancedMaterialOriginalAsset? OriginalAlbedo { get; init; }
    public string[] Models { get; init; } = Array.Empty<string>();
    public string[] Rooms { get; init; } = Array.Empty<string>();
}

public sealed record EnhancedMaterialOriginalAsset
{
    public string? Path { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed record EnhancedMaterialAssetInspection(
    bool Present,
    string? Path,
    int? Width,
    int? Height);

public sealed record EnhancedMaterialInspection(
    string Key,
    EnhancedMaterialAssetInspection OriginalAlbedo,
    EnhancedMaterialAssetInspection ReplacementAlbedo,
    EnhancedMaterialAssetInspection Normal,
    EnhancedMaterialAssetInspection Emissive,
    float? SpecularStrength,
    float? Smoothness,
    float? ReflectionStrength,
    float[]? EmissionTint,
    float? EmissionStrength,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Rooms);

public sealed record EnhancedMaterialInspectionIssue(
    string Kind,
    string Message,
    string? Key,
    string? Path);

public sealed record EnhancedMaterialInspectionReport(
    int Format,
    IReadOnlyList<EnhancedMaterialInspection> Materials,
    IReadOnlyList<EnhancedMaterialInspectionIssue> Issues)
{
    public const int CurrentFormat = 1;
    public bool HasIssues => Issues.Count != 0;
}

/// <summary>
/// Deterministic, bounded helpers for enhancement-pack authoring. This is a
/// development-time view over <see cref="EnhancementPackLoader"/> and never
/// changes the resolver or assets used by gameplay.
/// </summary>
public static class EnhancedMaterialAuthoring
{
    public const int MaximumInventoryBytes = 1024 * 1024;
    public const int MaximumInventoryEntries = EnhancementPackLimits.MaximumMaterials;
    public const int MaximumUsageEntriesPerTexture = 256;
    public const int MaximumUsageNameLength = 128;

    private static readonly JsonSerializerOptions _readOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly JsonSerializerOptions _writeOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static EnhancedMaterialInspectionReport Inspect(string packRoot, string? inventoryPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packRoot);
        EnhancementPackLoadResult loaded = EnhancementPackLoader.Load(packRoot);
        IReadOnlyList<TextureAssetKey> manifestKeys = ReadManifestKeys(packRoot);
        IReadOnlyDictionary<TextureAssetKey, InventoryValue> inventory = inventoryPath is null
            ? new Dictionary<TextureAssetKey, InventoryValue>()
            : ReadInventory(inventoryPath);

        TextureAssetKey[] keys = manifestKeys.Concat(inventory.Keys)
            .Distinct()
            .Order()
            .ToArray();
        var materials = new List<EnhancedMaterialInspection>(keys.Length);
        foreach (TextureAssetKey key in keys)
        {
            inventory.TryGetValue(key, out InventoryValue? usage);
            _ = loaded.Resolver.TryGetReplacement(key, out EnhancedMaterialReplacement? replacement);
            materials.Add(new EnhancedMaterialInspection(
                key.Value,
                OriginalAsset(usage?.OriginalAlbedo),
                ReplacementAsset(replacement?.Albedo),
                ReplacementAsset(replacement?.Normal),
                ReplacementAsset(replacement?.Emissive),
                replacement?.SpecularStrength,
                replacement?.Smoothness,
                replacement?.ReflectionStrength,
                replacement?.EmissionTint is { } tint ? [tint.X, tint.Y, tint.Z] : null,
                replacement?.EmissionStrength,
                usage?.Models ?? Array.Empty<string>(),
                usage?.Rooms ?? Array.Empty<string>()));
        }

        EnhancedMaterialInspectionIssue[] issues = loaded.Issues
            .Select(issue => new EnhancedMaterialInspectionIssue(
                issue.Kind.ToString(), issue.Message, issue.Key?.Value, issue.RelativePath))
            .OrderBy(issue => issue.Key, StringComparer.Ordinal)
            .ThenBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Kind, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray();
        return new EnhancedMaterialInspectionReport(
            EnhancedMaterialInspectionReport.CurrentFormat, materials, issues);
    }

    public static string Serialize(EnhancedMaterialInspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, _writeOptions) + "\n";
    }

    public static string GenerateStarterManifest(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        TextureAssetKey[] canonical = ParseKeys(keys, "starter key list");
        var manifest = new EnhancementManifest
        {
            Materials = canonical.Select(key => new EnhancedMaterialDeclaration { Key = key.Value }).ToArray()
        };
        return JsonSerializer.Serialize(manifest, _writeOptions) + "\n";
    }

    public static IReadOnlyList<string> ReadStarterKeys(string path)
    {
        try
        {
            using JsonDocument document = ReadJson(path, MaximumInventoryBytes, "Starter key input");
            JsonElement root = document.RootElement;
            IEnumerable<JsonElement> values;
            if (root.ValueKind == JsonValueKind.Array)
            {
                values = root.EnumerateArray();
            }
            else if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("textures", out JsonElement textures)
                && textures.ValueKind == JsonValueKind.Array)
            {
                values = textures.EnumerateArray().Select(entry => entry.GetProperty("key"));
            }
            else
            {
                throw new InvalidDataException(
                    "Starter key input must be a JSON string array or an inventory object with a textures array.");
            }
            string[] keys;
            try
            {
                keys = values.Select(value => value.GetString()
                    ?? throw new InvalidDataException("Starter keys must be strings.")).ToArray();
            }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
            {
                throw new InvalidDataException("Every starter inventory entry must contain a string key.", error);
            }
            if (keys.Length > MaximumInventoryEntries)
                throw new InvalidDataException($"Starter key input exceeds {MaximumInventoryEntries} entries.");
            return keys;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Starter key input is malformed JSON.", error);
        }
    }

    private static IReadOnlyList<TextureAssetKey> ReadManifestKeys(string packRoot)
    {
        string manifestPath = Path.Combine(Path.GetFullPath(packRoot), EnhancementPackLoader.ManifestFileName);
        try
        {
            using JsonDocument document = ReadJson(manifestPath,
                EnhancementPackLimits.MaximumManifestBytes, "Enhancement manifest");
            JsonElement root = document.RootElement;
            IEnumerable<JsonElement> entries = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray(),
                JsonValueKind.Object when root.TryGetProperty("materials", out JsonElement materials)
                    && materials.ValueKind == JsonValueKind.Array => materials.EnumerateArray(),
                JsonValueKind.Object when root.TryGetProperty("key", out _) => [root],
                _ => Array.Empty<JsonElement>()
            };
            return entries
                .Take(EnhancementPackLimits.MaximumMaterials)
                .Select(entry => entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("key", out JsonElement key) ? key.GetString() : null)
                .Where(value => TextureAssetKey.TryParse(value, out _))
                .Select(value => TextureAssetKey.Parse(value!))
                .Distinct()
                .Order()
                .ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        {
            // EnhancementPackLoader owns pack validation and reports its
            // fail-soft issue. Key discovery adds no second failure channel.
            return Array.Empty<TextureAssetKey>();
        }
    }

    private static IReadOnlyDictionary<TextureAssetKey, InventoryValue> ReadInventory(string path)
    {
        try
        {
            using JsonDocument document = ReadJson(path, MaximumInventoryBytes, "Material inventory");
            RejectDuplicateProperties(document.RootElement);
            EnhancedMaterialInventory inventory = document.RootElement.Deserialize<EnhancedMaterialInventory>(_readOptions)
                ?? throw new InvalidDataException("Material inventory is empty.");
            if (inventory.Format != EnhancedMaterialInventory.CurrentFormat)
                throw new InvalidDataException($"Material inventory format {inventory.Format} is not supported.");
            EnhancedMaterialInventoryEntry[] entries = inventory.Textures
                ?? throw new InvalidDataException("Material inventory textures must be an array.");
            if (entries.Length > MaximumInventoryEntries)
                throw new InvalidDataException($"Material inventory exceeds {MaximumInventoryEntries} entries.");

            var result = new Dictionary<TextureAssetKey, InventoryValue>();
            foreach (EnhancedMaterialInventoryEntry? entry in entries)
            {
                if (entry is null)
                    throw new InvalidDataException("Material inventory texture entries must be objects.");
                if (!TextureAssetKey.TryParse(entry.Key, out TextureAssetKey key))
                    throw new InvalidDataException($"Material inventory contains an invalid texture key: {entry.Key}");
                if (!result.TryAdd(key, new InventoryValue(
                    ValidateOriginal(entry.OriginalAlbedo, key),
                    ValidateUsage(entry.Models, "models", key),
                    ValidateUsage(entry.Rooms, "rooms", key))))
                {
                    throw new InvalidDataException($"Material inventory contains duplicate key: {key.Value}");
                }
            }
            return result;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Material inventory is malformed JSON.", error);
        }
    }

    private static EnhancedMaterialOriginalAsset? ValidateOriginal(EnhancedMaterialOriginalAsset? asset,
        TextureAssetKey key)
    {
        if (asset is null) return null;
        if (asset.Width <= 0 || asset.Height <= 0
            || asset.Width > EnhancementPackLimits.MaximumTextureDimension
            || asset.Height > EnhancementPackLimits.MaximumTextureDimension)
        {
            throw new InvalidDataException(
                $"Original albedo dimensions for {key.Value} must be between 1 and {EnhancementPackLimits.MaximumTextureDimension}.");
        }
        if (asset.Path is { } path && (path.Length == 0 || path.Length > 512
            || !String.Equals(path, path.Trim(), StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Original albedo path for {key.Value} is invalid.");
        }
        return asset;
    }

    private static string[] ValidateUsage(string[]? values, string name, TextureAssetKey key)
    {
        values ??= Array.Empty<string>();
        if (values.Length > MaximumUsageEntriesPerTexture)
            throw new InvalidDataException(
                $"Material inventory {name} for {key.Value} exceeds {MaximumUsageEntriesPerTexture} entries.");
        foreach (string? value in values)
        {
            if (String.IsNullOrEmpty(value) || value.Length > MaximumUsageNameLength
                || !String.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Material inventory {name} for {key.Value} contains an invalid name.");
            }
        }
        return values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static TextureAssetKey[] ParseKeys(IEnumerable<string> values, string source)
    {
        var result = new SortedSet<TextureAssetKey>();
        int count = 0;
        foreach (string value in values)
        {
            count++;
            if (count > MaximumInventoryEntries)
                throw new InvalidDataException($"The {source} exceeds {MaximumInventoryEntries} entries.");
            if (!TextureAssetKey.TryParse(value, out TextureAssetKey key))
                throw new InvalidDataException($"The {source} contains an invalid texture key: {value}");
            result.Add(key);
        }
        return result.ToArray();
    }

    private static JsonDocument ReadJson(string path, int maximumBytes, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
        long length = stream.Length;
        if (length <= 0 || length > maximumBytes)
            throw new InvalidDataException($"{label} must be a non-empty file of at most {maximumBytes} bytes: {path}");
        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)length);
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException($"{label} changed while it was being read: {path}");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Material inventory has duplicate property: {property.Name}");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateProperties(child);
        }
    }

    private static EnhancedMaterialAssetInspection OriginalAsset(EnhancedMaterialOriginalAsset? asset)
        => asset is null
            ? new EnhancedMaterialAssetInspection(false, null, null, null)
            : new EnhancedMaterialAssetInspection(true, asset.Path, asset.Width, asset.Height);

    private static EnhancedMaterialAssetInspection ReplacementAsset(EnhancedTextureAsset? asset)
        => asset is null
            ? new EnhancedMaterialAssetInspection(false, null, null, null)
            : new EnhancedMaterialAssetInspection(true, asset.RelativePath, asset.Width, asset.Height);

    private sealed record InventoryValue(
        EnhancedMaterialOriginalAsset? OriginalAlbedo,
        string[] Models,
        string[] Rooms);
}
