using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public enum MapStudioMaterialChannel { Texture, Albedo, Normal, SpecularRoughness, Emissive }

/// <summary>Existing authoring writers over declared materials/assets; no asset import, deletion or file publication.</summary>
public static class MapStudioResourceCommands
{
    public static string Fingerprint<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static IEnumerable<string?> MaterialPaths(MapMaterial material)=>new[]{material.Texture,material.Albedo,material.Normal,material.SpecularRoughness,material.Emissive}
        .Concat(material.Animation?.FlipbookFrames??new List<string>());
    public static int AssetUses(MapDefinition definition,string path)
        =>definition.Materials.Sum(m=>MaterialPaths(m).Count(p=>Same(p,path)))
            +(definition.Import?.ModernTextures.Values.Count(p=>Same(p,path))??0)
            +(Same(definition.Audio?.Music,path)?1:0)+definition.Assets.Count(a=>a.Kind=="preview"&&Same(a.Path,path));
    private static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    public static MapAsset Asset(MapDefinition definition,string path)
    {
        var matches=definition.Assets.Where(a=>Same(a.Path,path)).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Select one current declared asset.");
        MapPackageReader.CanonicalName(matches[0].Path);return matches[0];
    }
    public static int AddMaterial(MapDocument document,int? duplicate=null)
    {
        var definition=document.Project.Definition;
        MapMaterial material=duplicate is {} index
            ?JsonSerializer.Deserialize<MapMaterial>(JsonSerializer.Serialize(definition.Materials[index]))!:new();
        material.Id=Guid.NewGuid();string basis=duplicate.HasValue?material.Name+"_copy":"Material_"+definition.Materials.Count;
        int max=MapUvAnimation.IsAnimated(material)?31:256;basis=basis[..Math.Min(basis.Length,max)];
        string name=basis;int suffix=2;
        while(definition.Materials.Any(m=>m.Name==name)){string tail="_"+suffix++;name=basis[..Math.Min(basis.Length,max-tail.Length)]+tail;}
        material.Name=name;int added=definition.Materials.Count;
        document.Edit(duplicate.HasValue?"Duplicate material":"Add material",d=>d.Materials.Add(material),MapChangeDomain.Material);return added;
    }
    public static bool AssignSelection(MapDocument document,int material)
    {
        if(material<0||material>=document.Project.Definition.Materials.Count)throw new ArgumentOutOfRangeException(nameof(material));
        var ids=MapObjects.All(document.Project.Definition).Where(o=>document.Selection.Contains(o.Id)
            &&o.Value is MapBrush or MapGeometry{Locked:false}).Select(o=>o.Id).ToArray();
        if(ids.Length==0)return false;
        foreach (var mesh in document.Project.Definition.Geometry.OfType<MapMesh>().Where(mesh => ids.Contains(mesh.Id)))
            MapMeshEditing.RequireEditableSourceChannels(mesh);
        document.EditObjects("Assign material",ids,d=>{
            foreach(var geometry in d.Geometry){geometry.Material=material;if(geometry is MapMesh mesh)for(int i=0;i<mesh.FaceMaterials.Count;i++)mesh.FaceMaterials[i]=material;}
            foreach(var brush in d.Brushes)brush.Material=material;
        });return true;
    }
    public static Guid[] SelectMaterialUsages(MapDocument document,int material)=>Select(document,MapMaterialEditing.Usages(document.Project.Definition,material));
    public static Guid[] SelectAssetUsages(MapDocument document,string path)
    {
        _=Asset(document.Project.Definition,path);
        return Select(document,document.Project.Definition.Materials.Select((m,i)=>(Material:m,Index:i)).Where(x=>MaterialPaths(x.Material).Any(p=>Same(p,path)))
            .SelectMany(x=>MapMaterialEditing.Usages(document.Project.Definition,x.Index)));
    }
    private static Guid[] Select(MapDocument document,IEnumerable<Guid> objects)
    {
        var ids=objects.Distinct().ToArray();document.Selection.Clear();foreach(Guid id in ids)document.Selection.Add(id);
        document.ActiveObjectId=ids.FirstOrDefault();document.SelectionChanged();return ids;
    }
    public static void RenameAsset(MapDocument document,string path,string name)
    {
        var asset=Asset(document.Project.Definition,path);
        if(name.Length>256||name.Any(char.IsControl))throw new FormatException("Asset name must fit 256 display characters.");
        string value=name.Trim();if(value==asset.Name)return;
        document.Edit("Rename asset",d=>Asset(d,path).Name=value,MapChangeDomain.Metadata);
    }
    public static void BindChannel(MapDocument document,int index,MapStudioMaterialChannel channel,string? assetPath)
    {
        if(!Enum.IsDefined(channel))throw new ArgumentOutOfRangeException(nameof(channel));
        if(index<0||index>=document.Project.Definition.Materials.Count)throw new ArgumentOutOfRangeException(nameof(index));
        string? path=null;
        if(assetPath!=null)
        {
            var asset=Asset(document.Project.Definition,assetPath);if(asset.Kind!="texture")throw new FormatException("Select a declared texture asset.");
            string extension=Path.GetExtension(asset.Path).ToLowerInvariant();
            if(channel==MapStudioMaterialChannel.Texture?extension!=".tex":extension is not(".png" or ".jpg" or ".jpeg" or ".tga" or ".ktx2"))
                throw new FormatException(channel==MapStudioMaterialChannel.Texture?"Native fallback requires a .tex asset.":"HD channels require PNG, JPEG, TGA or KTX2 assets.");
            path=asset.Path;
        }
        document.EditMaterial(index,m=>{switch(channel){case MapStudioMaterialChannel.Texture:m.Texture=path;break;case MapStudioMaterialChannel.Albedo:m.Albedo=path;break;case MapStudioMaterialChannel.Normal:m.Normal=path;break;case MapStudioMaterialChannel.SpecularRoughness:m.SpecularRoughness=path;break;case MapStudioMaterialChannel.Emissive:m.Emissive=path;break;}});
    }
    public static bool CanDeleteMaterial(MapDefinition definition,int index)
    {
        try{MapMaterialEditing.DeleteUnused(MapProjectSerializer.Clone(definition),index);return true;}
        catch(InvalidOperationException){return false;}
    }
    public static void DeleteMaterial(MapDocument document,int index)
        =>document.Edit("Delete unused material",d=>MapMaterialEditing.DeleteUnused(d,index),MapChangeDomain.Geometry|MapChangeDomain.Material|MapChangeDomain.Import);
    public static void RemoveUnusedAsset(MapDocument document,string path)
    {
        _=Asset(document.Project.Definition,path);if(AssetUses(document.Project.Definition,path)>0)throw new InvalidOperationException("This asset is still referenced.");
        document.Edit("Remove unused asset reference",d=>d.Assets.RemoveAll(a=>Same(a.Path,path)),MapChangeDomain.Metadata);
    }
    public static void ReplaceMaterialUsages(MapDocument document,int source,int target)
    {if(source==target)return;document.Edit("Replace material usages",d=>MapMaterialEditing.Replace(d,source,target),MapChangeDomain.Geometry|MapChangeDomain.Material|MapChangeDomain.Import);}
}
