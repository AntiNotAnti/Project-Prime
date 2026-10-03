using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MphRead.Mods.Render.Materials;

/// <summary>Edits local presentation packs; map packages and their identity stay unchanged.</summary>
public static class MaterialPackAuthoring
{
    public static void Assign(string root, MaterialAssetKey key, string channel, string? source)
    {
        if (channel is not ("albedo" or "normal" or "specularRoughness" or "emissive"))
            throw new ArgumentException("Unknown material channel.");
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        string manifestPath = MaterialPack.ContainedPath(root, "materials.json");
        var manifest = File.Exists(manifestPath) ? MaterialPack.Load(root).Manifest : new MaterialPackManifest();
        string? relative = null;
        if (source != null)
        {
            MaterialPack.ValidateImage(source);
            byte[] bytes = File.ReadAllBytes(source);
            string extension = Path.GetExtension(source).Equals(".ktx2", StringComparison.OrdinalIgnoreCase)
                ? ".ktx2" : ".png";
            relative = "textures/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension;
            string path = MaterialPack.ContainedPath(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                MaterialPack.ValidatePackBudget(root, bytes.Length);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                file.Write(bytes);
            }
        }
        var entry = manifest.Materials.FirstOrDefault(e => e.Key == key.Value);
        if (entry == null)
        {
            if (manifest.Materials.Count >= MaterialPack.MaximumMaterials) throw new InvalidDataException("Too many material entries.");
            entry = new() { Key = key.Value }; manifest.Materials.Add(entry);
        }
        switch (channel)
        {
            case "albedo": entry.Albedo = relative; break;
            case "normal": entry.Normal = relative; break;
            case "specularRoughness": entry.SpecularRoughness = relative; break;
            case "emissive": entry.Emissive = relative; break;
        }
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, MaterialPack.Json).Length > MaterialPack.MaximumManifestBytes)
            throw new InvalidDataException("Manifest exceeds limit.");
        MaterialInventory.Write(manifestPath, manifest);
    }
}
