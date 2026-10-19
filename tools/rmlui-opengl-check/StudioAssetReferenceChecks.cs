using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static class StudioAssetReferenceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var definition=new MapDefinition();definition.Assets.Add(new(){Path="textures/old.png",Kind="texture",Name="Old",SourcePath="/private/original.png"});
        definition.Assets.Add(new(){Path="textures/keep.png",Kind="texture",Name="Keep"});
        definition.Materials.Add(new(){Name="HD refs",Albedo="textures/old.png",Normal="TEXTURES/OLD.PNG",SpecularRoughness="textures/old.png",Emissive="textures/old.png"});
        definition.Import=new(){Source="external.bsp",ModernTextures=new(){{0,"textures/old.png"},{1,"textures/keep.png"}}};
        var document=new MapDocument(new MapProject(definition));var history=document.History;string before=document.Project.Definition.Serialize();var state=document.CurrentStateId;
        var replacement=new MapAsset{Path="textures/new.png",Kind="texture",Name="Reviewed replacement",SourcePath="/private/new.png"};
        MapStudioAssetReferenceCommands.ReplaceReference(document,"textures/old.png",replacement);replacement.Path="MUTATED";replacement.Name="MUTATED";
        var changed=document.Project.Definition;string after=changed.Serialize();
        check(changed.Materials[0].Albedo=="textures/new.png"&&changed.Materials[0].Normal=="textures/new.png"&&changed.Materials[0].SpecularRoughness=="textures/new.png"&&changed.Materials[0].Emissive=="textures/new.png",
            "replacement remaps every HD material channel including case-equivalent references");
        check(changed.Import!.ModernTextures[0]=="textures/new.png"&&changed.Import.ModernTextures[1]=="textures/keep.png"&&changed.Import.Source=="external.bsp",
            "replacement remaps exact Q3 modern reference and preserves unrelated mapping/import provenance");
        check(changed.Assets[0] is{Path:"textures/new.png",Name:"Reviewed replacement",SourcePath:"/private/new.png"}&&ReferenceEquals(history,document.History)&&history.CommandCount==1,
            "replacement stores detached metadata through one existing history command");
        history.Undo();check(document.Project.Definition.Serialize()==before&&document.CurrentStateId==state,"HD replacement undo restores exact source and state");history.Redo();check(document.Project.Definition.Serialize()==after,"HD replacement redo restores exact source");
        var native=new MapDefinition();native.Assets.Add(new(){Path="textures/a.tex",Kind="texture"});native.Materials.Add(new(){Texture="textures/a.tex",Animation=new(){FlipbookFrames=new(){"textures/a.tex","textures/keep.tex"}}});
        var nativeDocument=new MapDocument(new MapProject(native));string nativeBefore=nativeDocument.Project.Definition.Serialize();
        MapStudioAssetReferenceCommands.ReplaceReference(nativeDocument,"textures/a.tex",new(){Path="textures/b.tex",Kind="texture"});
        check(nativeDocument.Project.Definition.Materials[0].Texture=="textures/b.tex"&&nativeDocument.Project.Definition.Materials[0].Animation!.FlipbookFrames.SequenceEqual(new[]{"textures/b.tex","textures/keep.tex"}),"replacement remaps native fallback and every matching flipbook frame");
        nativeDocument.History.Undo();check(nativeDocument.Project.Definition.Serialize()==nativeBefore,"native fallback/flipbook replacement exact undo");
        var audio=new MapDefinition{Audio=new(){Music="audio/a.ogg",Volume=.4f,Loop=false}};audio.Assets.Add(new(){Path="audio/a.ogg",Kind="audio"});var audioDoc=new MapDocument(new MapProject(audio));
        MapStudioAssetReferenceCommands.ReplaceReference(audioDoc,"audio/a.ogg",new(){Path="audio/b.wav",Kind="audio"});check(audioDoc.Project.Definition.Audio is{Music:"audio/b.wav",Volume:.4f,Loop:false},"shared reference helper remaps audio while retaining audio settings");
        foreach(var invalid in new MapAsset[]{new(){Path="textures/a.tex",Kind="texture"},new(){Path="../escape.tex",Kind="texture"},new(){Path="textures/x.png",Kind="texture"},new(){Path="audio/x.ogg",Kind="audio"}})
            Reject(nativeDocument,()=>MapStudioAssetReferenceCommands.ReplaceReference(nativeDocument,"textures/a.tex",invalid),check,"invalid/colliding/path/kind replacement is atomic");
        Reject(document,()=>MapStudioAssetReferenceCommands.ReplaceReference(document,"textures/missing.png",new(){Path="textures/new2.png",Kind="texture"}),check,"missing selected asset cannot silently become an import");
        string root=Path.Combine(Path.GetTempPath(),"private-asset-reference-test");string importBefore=document.Project.Definition.Serialize();int count=history.CommandCount;
        var fallback=new MapAsset{Path="textures/import.tex",Kind="texture",SourcePath="/private/source.png"};var hd=new MapAsset{Path="textures/import.png",Kind="texture",SourcePath="/private/source.png"};
        Guid id=MapStudioAssetReferenceCommands.ImportTexture(document,fallback,hd,"Imported material",root);fallback.Path="MUTATED";hd.Path="MUTATED";
        check(document.Project.Definition.Materials.Last() is{Texture:"textures/import.tex",Albedo:"textures/import.png",TexScale:16} m&&m.Id==id&&document.Project.Definition.Assets.Count==4&&history.CommandCount==count+1,
            "texture-pair reference import creates two detached declarations and one material in one history command");
        string imported=document.Project.Definition.Serialize();history.Undo();check(document.Project.Definition.Serialize()==importBefore,"imported pair/material undo restores exact source");history.Redo();check(document.Project.Definition.Serialize()==imported,"imported pair/material redo restores exact source");
        Reject(document,()=>MapStudioAssetReferenceCommands.ImportTexture(document,new(){Path="textures/import.tex",Kind="texture"},new(){Path="textures/another.png",Kind="texture"},"Collision",root),check,"texture import refuses duplicate declaration before any authoring edit");
    }
    private static void Reject(MapDocument document,Action action,Action<bool,string> check,string label)
    {string before=document.Project.Definition.Serialize();var state=document.CurrentStateId;int count=document.History.CommandCount;bool rejected=false;try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or System.IO.InvalidDataException){rejected=true;}check(rejected&&document.Project.Definition.Serialize()==before&&document.CurrentStateId==state&&document.History.CommandCount==count,label);}
}
