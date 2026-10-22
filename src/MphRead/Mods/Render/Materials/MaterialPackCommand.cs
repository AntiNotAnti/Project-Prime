using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Render.Materials;

public static class MaterialPackCommand
{
    public static int Run(string[] args)
    {
        try
        {
            args = (string[])args.Clone();
            for (int i = 1; i < args.Length; i++) args[i] = Path.GetFullPath(Path.Combine(ConsoleSetup.LaunchDirectory, args[i]));
            if (args.Length == 2 && args[0] is "inspect" or "validate")
            {
                var pack = MaterialPack.Load(args[1]);
                MaterialObservation[] observations = File.Exists(MaterialInventory.DefaultPath)
                    ? MaterialInventory.Read(MaterialInventory.DefaultPath) : Array.Empty<MaterialObservation>();
                foreach (var entry in pack.Manifest.Materials)
                {
                    pack.TryResolve(new(entry.Key), out var material);
                    var observed = observations.FirstOrDefault(o => o.Key == entry.Key);
                    Console.WriteLine(entry.Key + (observed == null ? " · not in observed inventory (usage unknown)"
                        : $" · original {observed.OriginalWidth}x{observed.OriginalHeight} · models {string.Join(", ", observed.Models.Length == 0 ? new[] { observed.Model } : observed.Models)} · maps {(observed.Maps.Length == 0 ? "not observed" : string.Join(", ", observed.Maps))}"));
                    foreach (var map in new[] { ("albedo", material.Albedo), ("normal", material.Normal),
                        ("specularRoughness", material.SpecularRoughness), ("emissive", material.Emissive) })
                        Console.WriteLine($"  {map.Item1}: " + (map.Item2 is { } image ? $"{image.Width}x{image.Height} {image.Path}" : "absent"));
                }
                foreach (var issue in pack.Issues) Console.WriteLine($"{(issue.Error ? "ERROR" : "WARNING")} {issue.Key} {issue.Channel}: {issue.Message}");
                return pack.Issues.Any(i => i.Error) ? 1 : 0;
            }
            if (args.Length == 2 && args[0] == "inventory")
            {
                if (!File.Exists(MaterialInventory.DefaultPath)) throw new InvalidDataException("No observed inventory yet. Load a scene and close it normally first.");
                MaterialInventory.Write(args[1], MaterialInventory.Read(MaterialInventory.DefaultPath));
                return 0;
            }
            if (args.Length == 3 && args[0] == "starter")
            {
                var entries = MaterialInventory.Read(args[1]);
                string output = args[2].EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? args[2] : Path.Combine(args[2], "materials.json");
                if (File.Exists(output)) throw new IOException("Starter destination already exists.");
                MaterialInventory.Write(output, new MaterialPackManifest
                {
                    Materials = entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new MaterialPackEntry
                    { Key = new MaterialAssetKey(e.Key).Value, Albedo = "", Normal = "", SpecularRoughness = "", Emissive = "" }).ToList()
                });
                return 0;
            }
            Console.Error.WriteLine("Usage: -materials inspect|validate <pack> | inventory <output.json> | starter <inventory.json> <pack-or-manifest>");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        { Console.Error.WriteLine("Materials: " + ex.Message); return 1; }
    }
}
