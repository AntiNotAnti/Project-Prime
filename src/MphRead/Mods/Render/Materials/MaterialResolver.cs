using System;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Render.Materials;

/// <summary>Common resolution for all graphics APIs and offline inspection. No graphics context required.</summary>
public sealed class MaterialResolver
{
    private readonly string _root;
    private readonly MaterialPack? _pack;
    public MaterialResolver(string root)
    {
        _root = Path.GetFullPath(root);
        if (File.Exists(Path.Combine(_root, "materials.json"))) _pack = MaterialPack.Load(_root);
    }
    public ResolvedMaterial Resolve(string model, int texture, int palette, int recolor)
    {
        var key = MaterialAssetKey.Model(model, texture, palette, recolor);
        if (_pack != null && _pack.TryResolve(key, out var explicitMaterial)) return explicitMaterial;
        string safe = string.IsNullOrWhiteSpace(model) ? "unnamed" : new string(model.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        foreach (string candidate in new[] { $"{safe}/{texture}_{palette}_{recolor}.png", $"{safe}/{texture}_{palette}.png",
            $"{safe}/{texture}.png", $"{safe}_{texture}_{palette}_{recolor}.png", $"{safe}_{texture}.png" })
        {
            MaterialImage? Read(string relative)
            {
                try
                {
                    string path = MaterialPack.ContainedPath(_root, relative);
                    return File.Exists(path) ? MaterialPack.ValidateImage(path) : null;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
            }
            var albedo = Read(candidate);
            if (albedo == null) continue;
            string stem = candidate[..^4];
            return new(key, albedo, Read(stem + "_n.png"), Read(stem + "_s.png"), Read(stem + "_e.png"));
        }
        return new(key, null, null, null, null);
    }
}
