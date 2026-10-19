using System.Collections.ObjectModel;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static class StudioMaterialOwnerChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var document=new MapDocument(MapTemplates.Create("RESOURCE_FIXTURE","basic-ffa"));
        var history=document.History;int originalMaterials=document.Project.Definition.Materials.Count;
        int added=MapStudioResourceCommands.AddMaterial(document);
        check(added==originalMaterials&&document.Project.Definition.Materials[added].Id!=Guid.Empty,"real material add uses retained document/history and assigns identity");
        Edit(()=>MapMaterialPropertySchema.Apply(document,added,new Dictionary<string,string>{{"name","NATIVE_MATERIAL"},{"texScale","24"},{"alpha","15"},{"twoSided","true"}}),"material core typed patch");
        var saved=document.CurrentStateId;
        MapMaterialPropertySchema.Apply(document,added,new Dictionary<string,string>{{"texScale","24.0"},{"twoSided","TRUE"}});
        check(saved==document.CurrentStateId,"semantic no-op material patch creates no history state");
        foreach(var invalid in new[]{("texScale","NaN"),("texScale","0"),("texScale","1,5"),("texScale","1e50"),("sourceMaterial","-1"),("alpha","32"),("alpha","-1"),("twoSided","sometimes"),("name",new string('x',257))})
        {var patch=new Dictionary<string,string>{{"name","MUST_NOT_APPLY"}};patch[invalid.Item1]=invalid.Item2;Reject(patch,"invalid "+invalid.Item1+" material patch");}
        var fields=MapMaterialPropertySchema.Read(document.Project.Definition,added);
        check(fields.Count>16&&fields is not MapObjectPropertyField[]&&fields.Any(f=>f.Id=="animation.loop"),"real immutable material schema supplies paged surface/animation fields");
        Edit(()=>MapMaterialPropertySchema.Apply(document,added,new Dictionary<string,string>{{"animation.enabled","true"},{"animation.scroll.x","0.3"},{"animation.rotation","3.6"},{"animation.loop","3000"}}),"native animation patch");
        Reject(new Dictionary<string,string>{{"name","MUST_NOT_APPLY"},{"animation.scroll.x","0.333"}},"nonseamless animation endpoint");
        Reject(new Dictionary<string,string>{{"animation.scale.x","0.02"},{"animation.pulse.x","0.02"}},"UV pulse crossing supported scale");
        Reject(new Dictionary<string,string>{{"animation.emissive","0.9"},{"animation.emissivePulse","0.2"}},"emissive pulse leaving finite range");
        Reject(new Dictionary<string,string>{{"animation.loop","3000"},{"animation.phase","3000"}},"phase outside native animation loop");
        int duplicate=MapStudioResourceCommands.AddMaterial(document,added);
        check(document.Project.Definition.Materials[duplicate].Id!=document.Project.Definition.Materials[added].Id
            &&document.Project.Definition.Materials[duplicate].Name!=document.Project.Definition.Materials[added].Name,"material duplication keeps new identity and unique native animation name");
        Reject(new Dictionary<string,string>{{"name",document.Project.Definition.Materials[duplicate].Name}},"duplicate native animated name reported on a different material row");
        Edit(()=>MapMaterialPropertySchema.Apply(document,added,new Dictionary<string,string>{{"alpha",""}}),"blank alpha restores native inherited surface setting");
        var geometry=document.Project.Definition.Geometry.First();document.Selection.Add(geometry.Id);document.ActiveObjectId=geometry.Id;document.SelectionChanged();
        Edit(()=>MapStudioResourceCommands.AssignSelection(document,added),"assign material to real selected geometry");
        check(MapStudioResourceCommands.SelectMaterialUsages(document,added).Contains(geometry.Id)&&document.ActiveObjectId!=Guid.Empty,"material usage selection resolves retained object IDs");
        check(!MapStudioResourceCommands.CanDeleteMaterial(document.Project.Definition,added),"referenced material cannot be confirmed for removal");
        Edit(()=>MapStudioResourceCommands.ReplaceMaterialUsages(document,added,duplicate),"replace material usages through shared remap");
        check(MapStudioResourceCommands.CanDeleteMaterial(document.Project.Definition,added),"replaced unused material becomes eligible for removal");
        Edit(()=>MapStudioResourceCommands.DeleteMaterial(document,added),"delete unused material with index remap");
        var native=new MapAsset{Path="textures/native.tex",Kind="texture",Name="Native"};
        var hd=new MapAsset{Path="textures/hd.png",Kind="texture",Name="HD"};
        var audio=new MapAsset{Path="audio/music.ogg",Kind="audio"};
        document.Edit("Synthetic declared resources",d=>d.Assets.AddRange(new[]{native,hd,audio}));
        int target=document.Project.Definition.Materials.Count-1;
        foreach(var channel in Enum.GetValues<MapStudioMaterialChannel>())
        {
            string path=channel==MapStudioMaterialChannel.Texture?native.Path:hd.Path;
            Edit(()=>MapStudioResourceCommands.BindChannel(document,target,channel,path),"declared asset binding "+channel);
        }
        check(MapStudioResourceCommands.AssetUses(document.Project.Definition,hd.Path)==4,"all declared HD channel references count as usages");
        string beforeInvalid=document.Project.Definition.Serialize();bool wrongKind=false;
        try{MapStudioResourceCommands.BindChannel(document,target,MapStudioMaterialChannel.Albedo,audio.Path);}catch(FormatException){wrongKind=true;}
        check(wrongKind&&document.Project.Definition.Serialize()==beforeInvalid,"audio asset cannot become a texture channel and leaves exact document unchanged");
        bool nativeFormat=false;try{MapStudioResourceCommands.BindChannel(document,target,MapStudioMaterialChannel.Texture,hd.Path);}catch(FormatException){nativeFormat=true;}
        check(nativeFormat&&document.Project.Definition.Serialize()==beforeInvalid,"HD image cannot replace universal native .tex fallback");
        Edit(()=>MapStudioResourceCommands.RenameAsset(document,hd.Path,"Renamed HD"),"asset logical-name edit");
        bool used=false;try{MapStudioResourceCommands.RemoveUnusedAsset(document,hd.Path);}catch(InvalidOperationException){used=true;}
        check(used,"referenced asset cannot lose its declared reference");
        Edit(()=>MapStudioResourceCommands.RemoveUnusedAsset(document,audio.Path),"remove unused declared reference without deleting files");
        check(ReferenceEquals(history,document.History),"all real resource commands retain original document history authority");


        void Edit(Action action,string label)
        {
            string before=document.Project.Definition.Serialize();var state=document.CurrentStateId;action();string after=document.Project.Definition.Serialize();var changed=document.CurrentStateId;
            check(before!=after&&state!=changed,label+" creates an existing history transaction");history.Undo();check(document.Project.Definition.Serialize()==before&&document.CurrentStateId==state,label+" undoes exact authored bytes");
            history.Redo();check(document.Project.Definition.Serialize()==after&&document.CurrentStateId==changed,label+" redoes exact authored bytes");
        }
        void Reject(Dictionary<string,string> patch,string label)
        {string before=document.Project.Definition.Serialize();var state=document.CurrentStateId;bool failed=false;try{MapMaterialPropertySchema.Apply(document,added,patch);}catch(FormatException){failed=true;}check(failed&&document.CurrentStateId==state&&document.Project.Definition.Serialize()==before,label+" fails atomically");}
    }
}
