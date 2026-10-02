using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MphRead.Mods.Render;

namespace MphRead.Mods.MapGen;

/// <summary>Copies author-selected HD material images into an editable map project.</summary>
internal static class MapMaterialAssetAuthoring
{
    public static string? Assign(MapDefinition definition, Guid materialId, string channel, string? source)
    {
        if (definition.BundlePath != null) throw new IOException("Save this package as an editable project before changing packaged material art.");
        MapMaterial material = definition.Materials.FirstOrDefault(m => m.Id == materialId)
            ?? throw new InvalidDataException("Material no longer exists.");
        string? previous = Get(material, channel);
        if (source == null)
        {
            Set(material, channel, null);
            RemoveUnused(definition, previous);
            return null;
        }

        string? root = definition.BaseDirectory;
        if (String.IsNullOrWhiteSpace(root) && !String.IsNullOrWhiteSpace(definition.SourcePath))
            root = Path.GetDirectoryName(Path.GetFullPath(definition.SourcePath));
        if (String.IsNullOrWhiteSpace(root)) throw new IOException("Save this map project before assigning packaged HD material art.");
        root = Path.GetFullPath(root);
        byte[] bytes = File.ReadAllBytes(source);
        if (bytes.LongLength > MapPackageReader.MaxEntryBytes) throw new InvalidDataException("HD material image exceeds the map asset limit.");
        _ = ModernTextureAsset.ProbeDimensions(bytes);
        string extension = ModernTextureAsset.PortableEncodedExtension(bytes)
            ?? throw new InvalidDataException("Packaged HD material images must be PNG or JPEG.");
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string relative = $"textures/material-{materialId:N}-{channel.ToLowerInvariant()}-{hash}{extension}";
        MapPackageReader.CanonicalName(relative);
        string destination = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("HD material asset escapes the map project.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        AtomicFile.Write(destination, bytes);

        Set(material, channel, relative);
        if (!definition.Assets.Any(a => a.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)))
            definition.Assets.Add(new MapAsset { Path = relative, Kind = "texture", Name = material.Name });
        if (!String.Equals(previous, relative, StringComparison.OrdinalIgnoreCase)) RemoveUnused(definition, previous);
        return relative;
    }

    private static string? Get(MapMaterial material, string channel) => channel switch
    {
        "albedo" => material.Albedo,
        "normal" => material.Normal,
        "specularRoughness" => material.SpecularRoughness,
        "emissive" => material.Emissive,
        _ => throw new ArgumentException("Unknown HD material channel.", nameof(channel))
    };

    private static void Set(MapMaterial material, string channel, string? value)
    {
        switch (channel)
        {
            case "albedo": material.Albedo = value; break;
            case "normal": material.Normal = value; break;
            case "specularRoughness": material.SpecularRoughness = value; break;
            case "emissive": material.Emissive = value; break;
            default: throw new ArgumentException("Unknown HD material channel.", nameof(channel));
        }
    }

    private static void RemoveUnused(MapDefinition definition, string? path)
    {
        if (String.IsNullOrEmpty(path)) return;
        bool referenced = definition.Materials.Any(m => new[] { m.Texture, m.Albedo, m.Normal, m.SpecularRoughness, m.Emissive }
                .Any(p => String.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            || String.Equals(definition.Audio?.Music, path, StringComparison.OrdinalIgnoreCase)
            || definition.Assets.Any(a => a.Kind == "preview" && String.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
        if (!referenced) definition.Assets.RemoveAll(a => String.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}
