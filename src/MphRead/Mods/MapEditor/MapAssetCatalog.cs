using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed record MapAssetUsage(string Kind, string Name, Guid? ObjectId = null);

/// <summary>One authoritative reference walk for browser usage and transactional asset replacement.</summary>
public static class MapAssetCatalog
{
    private static bool Same(string? left,string right) => string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    public static IReadOnlyList<MapAssetUsage> Usages(MapDefinition definition,string path)
    {
        var usages=new List<MapAssetUsage>();
        foreach (var material in definition.Materials)
        {
            string[] channels={ "fallback", "albedo", "normal", "roughness/specular", "emissive" };
            string?[] paths={material.Texture,material.Albedo,material.Normal,material.SpecularRoughness,material.Emissive};
            for (int i=0;i<paths.Length;i++) if (Same(paths[i],path)) usages.Add(new("Material",material.Name+" · "+channels[i],material.Id));
            foreach (string frame in material.Animation?.FlipbookFrames ?? Enumerable.Empty<string>())
                if (Same(frame,path)) usages.Add(new("Flipbook",material.Name,material.Id));
        }
        if (definition.Import is { } import)
            foreach (var texture in import.ModernTextures.Where(texture=>Same(texture.Value,path))) usages.Add(new("Source surface",texture.Key.ToString()));
        if (Same(definition.Audio?.Music,path)) usages.Add(new("Audio","Map music"));
        if (definition.Assets.Any(asset=>asset.Kind=="preview" && Same(asset.Path,path))) usages.Add(new("Preview","Map preview"));
        return usages;
    }
    public static void ReplaceReferences(MapDefinition definition,string previous,string replacement)
    {
        MapPackageReader.CanonicalName(previous); MapPackageReader.CanonicalName(replacement);
        foreach (var material in definition.Materials)
        {
            if (Same(material.Texture,previous)) material.Texture=replacement;
            if (Same(material.Albedo,previous)) material.Albedo=replacement;
            if (Same(material.Normal,previous)) material.Normal=replacement;
            if (Same(material.SpecularRoughness,previous)) material.SpecularRoughness=replacement;
            if (Same(material.Emissive,previous)) material.Emissive=replacement;
            if (material.Animation?.FlipbookFrames is { } frames)
                for(int i=0;i<frames.Count;i++) if(Same(frames[i],previous)) frames[i]=replacement;
        }
        if (definition.Import is { } import)
            foreach (int shader in import.ModernTextures.Keys.ToArray()) if(Same(import.ModernTextures[shader],previous)) import.ModernTextures[shader]=replacement;
        if(Same(definition.Audio?.Music,previous)) definition.Audio!.Music=replacement;
    }
    public static bool Matches(MapAsset asset,string query)
    {
        string[] terms=query.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        string text=string.Join(" ",asset.Kind,asset.Name,asset.Path,string.Join(" ",asset.Tags ?? new()));
        return terms.All(term=>text.Contains(term,StringComparison.OrdinalIgnoreCase));
    }
    public static List<string> ParseTags(string value)
    {
        var tags=value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(tags.Count>32 || tags.Any(tag=>tag.Length>64)) throw new ArgumentException("Choose up to 32 tags of at most 64 characters each.");
        return tags;
    }
}
