using System;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Render.Materials;

/// <summary>Common resolution for all graphics APIs and offline inspection. No graphics context required.</summary>
public sealed class MaterialResolver
{
    private readonly string _root;
    private readonly MaterialPack? _pack;
    public MaterialResolver(string root, bool useManifest = true)
    {
        _root = Path.GetFullPath(root);
        if (useManifest && File.Exists(Path.Combine(_root, "materials.json"))) _pack = MaterialPack.Load(_root);
    }
    public ResolvedMaterial? ResolveExplicit(MaterialAssetKey key)
        => _pack != null && _pack.TryResolve(key, out var material) ? material : null;

    public ResolvedMaterial Resolve(string model, int texture, int palette, int recolor, MaterialAssetKey? scopedKey = null, MaterialAssetKey? fallbackKey = null)
    {
        var modelKey = MaterialAssetKey.Model(model, texture, palette, recolor);
        var key = scopedKey ?? modelKey;
        if (_pack != null && _pack.TryResolve(key, out var explicitMaterial)) return explicitMaterial;
        if (_pack != null && fallbackKey is { } fallback && _pack.TryResolve(fallback, out explicitMaterial))
            return explicitMaterial with { Key = key };
        // Preexisting model manifests remain valid after adding richer room/effect identities.
        if (_pack != null && key != modelKey && _pack.TryResolve(modelKey, out explicitMaterial))
            return explicitMaterial with { Key = key };
        // Particle textures are shared with ordinary model users. Always consider the
        // shared effect/model alias, avoiding load-order-dependent binding identities.
        var effectKey = MaterialAssetKey.Scoped(MaterialAssetKey.EffectScope(model), texture, palette, recolor);
        if (_pack != null && key == modelKey && _pack.TryResolve(effectKey, out explicitMaterial))
            return explicitMaterial;
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
