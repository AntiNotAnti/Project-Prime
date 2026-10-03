using System;
using System.IO;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Render.Materials;

/// <summary>
/// Resolves HD channels that travel inside a custom-map project/package.
/// Local user material packs still take precedence in TextureReplacementPack.
/// </summary>
internal static class MapMaterialAssetRegistry
{
    public static ResolvedMaterial? Resolve(MaterialAssetKey key, bool includeCompanions = true)
    {
        if (!key.Value.StartsWith("map/", StringComparison.Ordinal)) return null;
        try
        {
            foreach (MapDefinition definition in CustomRooms.Definitions)
                if (Resolve(definition, key, includeCompanions) is { } resolved) return resolved;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            DebugLog.Line("render", "packaged map material ignored " + key.Value + ": " + ex.Message);
        }
        return null;
    }

    internal static ResolvedMaterial? Resolve(MapDefinition definition, MaterialAssetKey key,
        bool includeCompanions = true)
    {
        if (definition.MapId == Guid.Empty || !key.Value.StartsWith($"map/{definition.MapId:N}/", StringComparison.Ordinal))
            return null;

        MaterialImage? Image(string? relative)
        {
            if (String.IsNullOrWhiteSpace(relative)) return null;
            byte[] probe = MapAssets.Read(definition, relative);
            (int width, int height) = ModernTextureAsset.ProbeDimensions(probe);
            string identity = $"ppmap/{definition.MapId:N}/{relative}";
            // Keep the streaming queue bounded: retain dimensions/identity, not
            // every encoded image in the package. The worker reopens only this
            // already-validated entry when it is ready to decode it.
            return new MaterialImage(identity, width, height,
                () => new MemoryStream(MapAssets.Read(definition, relative), writable: false),
                canDecodeOffThread: true);
        }

        foreach (MapMaterial material in definition.Materials)
        {
            if (material.Id == Guid.Empty) continue;
            string prefix = $"map/{definition.MapId:N}/material/{material.Id:N}/recolor/";
            if (!key.Value.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string recolorText = key.Value[prefix.Length..];
            if (!Int32.TryParse(recolorText, out int recolor) || recolor < 0) return null;
            var resolved = new ResolvedMaterial(key, Image(material.Albedo),
                includeCompanions ? Image(material.Normal) : null,
                includeCompanions ? Image(material.SpecularRoughness) : null,
                includeCompanions ? Image(material.Emissive) : null);
            return resolved.Albedo != null || resolved.Normal != null
                || resolved.SpecularRoughness != null || resolved.Emissive != null ? resolved : null;
        }

        if (definition.Import is { ModernTextures.Count: > 0 } import)
        {
            string prefix = $"map/{definition.MapId:N}/texture/";
            if (!key.Value.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string[] parts = key.Value[prefix.Length..].Split('/');
            if (parts.Length != 5 || parts[1] != "palette" || parts[3] != "recolor"
                || !Int32.TryParse(parts[0], out int texture) || texture < 0
                || !Int32.TryParse(parts[2], out int palette) || palette < 0
                || !Int32.TryParse(parts[4], out int recolor) || recolor < 0)
                return null;
            MapTexturePack? pack = import.LoadTexturePack();
            if (pack == null || texture >= pack.Entries.Count) return null;
            int sourceIndex = pack.Entries[texture].SourceIndex;
            if (!import.ModernTextures.TryGetValue(sourceIndex, out string? relative)) return null;
            MaterialImage? albedo = Image(relative);
            return albedo == null ? null : new ResolvedMaterial(key, albedo, null, null, null);
        }
        return null;
    }
}
