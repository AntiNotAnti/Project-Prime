using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

public static class MapProjectFolder
{
    public static string Serialize(MapDefinition source)
    {
        var copy=MapProjectSerializer.Clone(source);
        // Material slots are positional: reorder references and reimport baselines together.
        var order=copy.Materials.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Id).ThenBy(x=>x.i).ToArray();
        var remap=order.Select((x,i)=>(old:x.i,index:i)).ToDictionary(x=>x.old,x=>x.index);
        int Material(int index)=>remap.GetValueOrDefault(index,index);
        copy.Materials=order.Select(x=>x.m).ToList();
        foreach(var brush in copy.Brushes)brush.Material=Material(brush.Material);
        foreach(var geometry in copy.Geometry){geometry.Material=Material(geometry.Material);if(geometry is MapMesh mesh)mesh.FaceMaterials=mesh.FaceMaterials.Select(Material).ToList();}
        if(copy.Import is {} import){import.DefaultMaterial=Material(import.DefaultMaterial);import.ShaderMaterials=import.ShaderMaterials.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>Material(p.Value));foreach(var replacement in import.MaterialReplacements)if(!replacement.TargetSource)replacement.Target=Material(replacement.Target);}
        if(copy.NativeRoom is {} native)foreach(var replacement in native.MaterialReplacements)if(!replacement.TargetSource)replacement.Target=Material(replacement.Target);
        foreach(var model in copy.ModelSources)foreach(var obj in model.Objects){obj.BaseMaterial=Material(obj.BaseMaterial);obj.FaceMaterials=obj.FaceMaterials.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>Material(p.Value));}
        copy.Geometry=copy.Geometry.OrderBy(g=>g.Id).ToList();copy.Brushes=copy.Brushes.OrderBy(g=>g.Id).ToList();copy.Spawns=copy.Spawns.OrderBy(g=>g.Id).ToList();copy.Items=copy.Items.OrderBy(g=>g.Id).ToList();copy.JumpPads=copy.JumpPads.OrderBy(g=>g.Id).ToList();copy.NavigationLinks=copy.NavigationLinks.OrderBy(g=>g.Id).ToList();
        copy.Assets=copy.Assets.OrderBy(a=>a.Path,StringComparer.Ordinal).ToList();copy.ModelSources=copy.ModelSources.OrderBy(s=>s.Id).ToList();
        foreach(var model in copy.ModelSources)model.Dependencies=model.Dependencies.OrderBy(d=>d.Path,StringComparer.Ordinal).ToList();
        string Portable(string path)
        {
            if(copy.BaseDirectory==null||!Path.IsPathFullyQualified(path))return path;
            string relative=Path.GetRelativePath(copy.BaseDirectory,path).Replace('\\','/');
            return relative.StartsWith("../",StringComparison.Ordinal)?path:relative;
        }
        foreach(var model in copy.ModelSources){model.Source=Portable(model.Source);model.Dependencies=model.Dependencies.Select(d=>d with{Path=Portable(d.Path)}).ToList();}
        foreach(var asset in copy.Assets)if(asset.SourcePath!=null)asset.SourcePath=Portable(asset.SourcePath);
        return JsonSerializer.Serialize(copy,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull});
    }
    public static string Export(MapDefinition source,string destination)
    {
        destination=Path.GetFullPath(destination);if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("Choose a new project folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string stage=destination+".export-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
        try
        {
            var copy=MapProjectSerializer.Clone(source);var paths=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string folder in new[]{"models","textures","collision","audio"})Directory.CreateDirectory(Path.Combine(stage,folder));
            foreach(string asset in MapDependencyAnalyzer.PackageAssets(source))
            {
                byte[] bytes=MapAssets.Read(source,asset);string? kind=source.Assets.FirstOrDefault(a=>a.Path==asset)?.Kind;
                string folder=kind=="audio"?"audio":"textures";
                string target=folder+"/"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()+Path.GetExtension(asset).ToLowerInvariant();
                AtomicFile.Write(Path.Combine(stage,target),bytes);paths[asset]=target;
            }
            foreach(var material in copy.Materials)if(material.Texture is {} texture)material.Texture=paths[texture];
            foreach(var asset in copy.Assets)asset.Path=paths[asset.Path];if(copy.Audio?.Music is {} music)copy.Audio.Music=paths[music];
            var external=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var model in copy.ModelSources)
            {
                string root=Path.GetDirectoryName(Path.GetFullPath(model.Source))!;string folder="models/"+model.Id.ToString("N");
                foreach(string file in model.Dependencies.Select(d=>d.Path).Append(model.Source).Distinct(StringComparer.Ordinal))
                {
                    string full=Path.GetFullPath(file);string relative=Path.GetRelativePath(root,full).Replace('\\','/');
                    if(relative.StartsWith("../",StringComparison.Ordinal)||Path.IsPathRooted(relative))throw new IOException("Model dependency is outside its source folder: "+Path.GetFileName(full));
                    string target=folder+"/"+relative;AtomicFile.Write(Path.Combine(stage,target),File.ReadAllBytes(full));external[full]=target;
                }
                model.Source=external[Path.GetFullPath(model.Source)];model.Dependencies=model.Dependencies.Select(d=>d with{Path=external[Path.GetFullPath(d.Path)]}).ToList();
            }
            foreach(var asset in copy.Assets)
            {
                if(string.IsNullOrWhiteSpace(asset.SourcePath))continue;string full=Path.GetFullPath(asset.SourcePath);
                if(!external.TryGetValue(full,out string? relative))
                {byte[] bytes=File.ReadAllBytes(full);relative="textures/sources/"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()+Path.GetExtension(full);AtomicFile.Write(Path.Combine(stage,relative),bytes);external[full]=relative;}
                asset.SourcePath=relative;
            }
            if(copy.Collision is {} collision){string target="collision/collision.obj";AtomicFile.Write(Path.Combine(stage,target),source.Collision!.ReadBytes()??throw new IOException("Collision source is missing."));collision.Source=target;collision.BundlePath=null;collision.BaseDirectory=stage;}
            if(copy.Import is {} import)
            {
                string sourceName=source.Import!.Source;byte[] bytes=source.BundlePath==null?File.ReadAllBytes(source.Import.Resolve()!):MapBundle.ReadEntry(source.BundlePath,sourceName)??throw new IOException("Imported level is missing.");
                string target="models/level"+Path.GetExtension(sourceName);AtomicFile.Write(Path.Combine(stage,target),bytes);import.Source=target;
                if(!string.IsNullOrEmpty(import.Textures)){byte[] texture=source.BundlePath==null?File.ReadAllBytes(source.Import.ResolveTextures()!):MapBundle.ReadEntry(source.BundlePath,import.Textures)??throw new IOException("Imported textures are missing.");AtomicFile.Write(Path.Combine(stage,"textures/import.tex"),texture);import.Textures="textures/import.tex";}
                import.BundlePath=null;import.BaseDirectory=stage;
            }
            copy.BundlePath=null;copy.BaseDirectory=stage;copy.SourcePath=Path.Combine(stage,"map.json");copy.Save(copy.SourcePath);
            Directory.Move(stage,destination);return Path.Combine(destination,"map.json");
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
}
