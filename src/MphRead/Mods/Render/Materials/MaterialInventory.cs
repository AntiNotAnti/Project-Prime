using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Render.Materials;

public sealed record MaterialObservation(string Key, int OriginalWidth, int OriginalHeight, string Model);
/// <summary>Bounded observed usage; no cartridge texture bytes are persisted.</summary>
public static class MaterialInventory
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, MaterialObservation> Observed = new(StringComparer.Ordinal);
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectPrime", "material-inventory.json");
    public static void Observe(MaterialAssetKey key, int width, int height, string model)
    {
        lock (Gate)
        {
            if (Observed.Count < MaterialPack.MaximumMaterials || Observed.ContainsKey(key.Value))
                Observed[key.Value] = new(key.Value, width, height, model);
        }
    }
    public static void SaveObserved()
    {
        MaterialObservation[] values;
        lock (Gate) values = Observed.Values.OrderBy(v => v.Key, StringComparer.Ordinal).ToArray();
        if (values.Length != 0) Write(DefaultPath, values);
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
                || entry.OriginalWidth <= 0 || entry.OriginalHeight <= 0 || string.IsNullOrEmpty(entry.Model))
                throw new InvalidDataException("Invalid or duplicate observation.");
        return entries;
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
