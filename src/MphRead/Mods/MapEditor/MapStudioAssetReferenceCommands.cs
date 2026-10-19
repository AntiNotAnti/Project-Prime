using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>One retained-document history transaction; file publication is a separately reviewed owner operation.</summary>
public static class MapStudioAssetReferenceCommands
{
    private static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    public static void ReplaceReference(MapDocument document,string previous,MapAsset replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);MapPackageReader.CanonicalName(previous);MapPackageReader.CanonicalName(replacement.Path);
        var original=MapStudioResourceCommands.Asset(document.Project.Definition,previous);
        if(original.Kind!=replacement.Kind)throw new InvalidOperationException("Replacement must preserve the selected asset kind.");
        if(Same(original.Path,replacement.Path)||document.Project.Definition.Assets.Any(a=>Same(a.Path,replacement.Path)))
            throw new InvalidOperationException("Replacement requires a new unique generated asset path.");
        string extension=Path.GetExtension(replacement.Path).ToLowerInvariant();
        if(original.Kind=="texture")
        {
            bool native=original.Path.EndsWith(".tex",StringComparison.OrdinalIgnoreCase);
            if(native?extension!=".tex":extension is not(".png" or ".jpg" or ".jpeg" or ".tga" or ".ktx2"))
                throw new InvalidOperationException("Preserve native baked or portable HD texture encoding when replacing a reference.");
        }
        else if(original.Kind=="audio"&&extension is not(".wav" or ".ogg" or ".mp3")||original.Kind=="preview"&&extension!=".png")
            throw new InvalidOperationException("Replacement encoding does not match its asset kind.");
        else if(original.Kind is not("texture" or "audio" or "preview"))throw new InvalidOperationException("Unsupported asset kind.");
        // Detached inputs cannot mutate a saved history snapshot after Apply.
        string path=replacement.Path,kind=replacement.Kind,name=replacement.Name??original.Name??Path.GetFileName(path);string? source=replacement.SourcePath;
        document.Edit("Replace declared asset",definition=>{
            var asset=MapStudioResourceCommands.Asset(definition,previous);
            asset.Path=path;asset.Kind=kind;asset.Name=name;asset.SourcePath=source;
            foreach(var material in definition.Materials)
            {
                if(Same(material.Texture,previous))material.Texture=path;
                if(Same(material.Albedo,previous))material.Albedo=path;
                if(Same(material.Normal,previous))material.Normal=path;
                if(Same(material.SpecularRoughness,previous))material.SpecularRoughness=path;
                if(Same(material.Emissive,previous))material.Emissive=path;
                if(material.Animation?.FlipbookFrames is {} frames)
                    for(int i=0;i<frames.Count;i++)if(Same(frames[i],previous))frames[i]=path;
            }
            if(definition.Import is {} import)
                foreach(int key in import.ModernTextures.Keys.ToArray())if(Same(import.ModernTextures[key],previous))import.ModernTextures[key]=path;
            if(definition.Audio is {} audio&&Same(audio.Music,previous))audio.Music=path;
        },MapChangeDomain.Material|MapChangeDomain.Metadata);
    }
    public static Guid ImportTexture(MapDocument document,MapAsset fallback,MapAsset hd,string materialName,string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(fallback);ArgumentNullException.ThrowIfNull(hd);
        MapPackageReader.CanonicalName(fallback.Path);MapPackageReader.CanonicalName(hd.Path);
        if(fallback.Kind!="texture"||hd.Kind!="texture"||!fallback.Path.EndsWith(".tex",StringComparison.OrdinalIgnoreCase)
            ||Path.GetExtension(hd.Path).ToLowerInvariant() is not(".png" or ".jpg" or ".jpeg" or ".tga" or ".ktx2")
            ||Same(fallback.Path,hd.Path)||document.Project.Definition.Assets.Any(a=>Same(a.Path,fallback.Path)||Same(a.Path,hd.Path)))
            throw new InvalidOperationException("Import requires new unique baked and portable texture references.");
        if(string.IsNullOrWhiteSpace(materialName)||materialName.Length>256)throw new InvalidOperationException("Enter a material name of1..256 characters.");
        string root=Path.GetFullPath(projectRoot),fallbackPath=fallback.Path,hdPath=hd.Path;
        string? nativeSource=fallback.SourcePath,hdSource=hd.SourcePath;Guid id=Guid.NewGuid();
        document.Edit("Import native and HD texture",definition=>{
            definition.BaseDirectory=root;
            definition.Assets.Add(new(){Path=fallbackPath,Kind="texture",Name=materialName+" (native)",SourcePath=nativeSource});
            definition.Assets.Add(new(){Path=hdPath,Kind="texture",Name=materialName+" (HD)",SourcePath=hdSource});
            definition.Materials.Add(new(){Id=id,Name=materialName,Texture=fallbackPath,Albedo=hdPath,TexScale=16});
        },MapChangeDomain.Material|MapChangeDomain.Metadata);
        return id;
    }
}
