using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Render.Materials;

public sealed record MaterialObservation(string Key, int OriginalWidth, int OriginalHeight, string Model)
{
    public string[] Models { get; init; } = Array.Empty<string>();
    public string[] Maps { get; init; } = Array.Empty<string>();
}
/// <summary>Bounded observed usage; no cartridge texture bytes are persisted.</summary>
public static class MaterialInventory
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, MaterialObservation> Observed = new(StringComparer.Ordinal);
    public const int MaximumUsageLabels = 8;
    private static bool LabelValid(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 256 && !value.Any(char.IsControl);
    private static bool DimensionsValid(int width, int height) => width > 0 && height > 0
        && width <= MaterialPack.MaximumDimension && height <= MaterialPack.MaximumDimension
        && (long)width * height <= MaterialPack.MaximumPixels;
    private static string[] AddLabel(string[] labels, string? value) => value != null && LabelValid(value)
        ? labels.Append(value).Distinct(StringComparer.Ordinal).Take(MaximumUsageLabels).ToArray() : labels;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectPrime", "material-inventory.json");
    public static void Observe(MaterialAssetKey key, int width, int height, string model, string? map = null)
    {
        if (!DimensionsValid(width, height) || !LabelValid(model)) return;
        lock (Gate)
        {
            Observed.TryGetValue(key.Value, out var previous);
            if (Observed.Count < MaterialPack.MaximumMaterials || previous != null)
                Observed[key.Value] = new(key.Value, width, height, model)
                {
                    Models = AddLabel(previous?.Models ?? Array.Empty<string>(), model),
                    Maps = AddLabel(previous?.Maps ?? Array.Empty<string>(), map)
                };
        }
    }
    public static void SaveObserved(string? path = null)
    {
        MaterialObservation[] values;
        lock (Gate) values = Observed.Values.OrderBy(v => v.Key, StringComparer.Ordinal).ToArray();
        // Keep persisted snapshots readable under the same limit as imported inventories.
        // An inventory is observed evidence, never an exhaustive asset catalog.
        if (values.Length == 0) return;
        var bounded = new List<MaterialObservation>();
        int bytes = 2;
        foreach (var value in values)
        {
            // Reserve collection indentation and separators beyond the standalone record.
            int size = JsonSerializer.SerializeToUtf8Bytes(value, MaterialPack.Json).Length + 256;
            if (bytes + size > MaterialPack.MaximumManifestBytes) break;
            bounded.Add(value); bytes += size;
        }
        Write(path ?? DefaultPath, bounded);
    }
    public static MaterialObservation[] Read(string path)
    {
        if (new FileInfo(path).Length > MaterialPack.MaximumManifestBytes) throw new InvalidDataException("Inventory exceeds limit.");
        var entries = JsonSerializer.Deserialize<MaterialObservation[]>(File.ReadAllBytes(path), MaterialPack.Json)
            ?? throw new InvalidDataException("Invalid inventory.");
        if (entries.Length > MaterialPack.MaximumMaterials) throw new InvalidDataException("Too many inventory entries.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (entry == null || entry.Key == null || !keys.Add(new MaterialAssetKey(entry.Key).Value)
                || !DimensionsValid(entry.OriginalWidth, entry.OriginalHeight) || !LabelValid(entry.Model)
                || entry.Models == null || entry.Maps == null
                || entry.Models.Length > MaximumUsageLabels || entry.Maps.Length > MaximumUsageLabels
                || entry.Models.Any(label => label == null || !LabelValid(label))
                || entry.Maps.Any(label => label == null || !LabelValid(label)))
                throw new InvalidDataException("Invalid or duplicate observation.");
        return entries.Select(entry => entry with { Key = new MaterialAssetKey(entry.Key).Value }).ToArray();
    }
    public static void Write<T>(string path, T value)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, MaterialPack.Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
