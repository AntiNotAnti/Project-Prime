using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Settings;
using SkiaSharp;

internal static partial class Program
{
    private static async Task CheckLoadedAssetWorkflowAsync(StudioWindow window,MapStudioDocument document,StudioPaths paths,string output)
    {
        string sources=Path.Combine(paths.UserDataDirectory,"fixture-sources");Directory.CreateDirectory(sources);
        string texture=Path.Combine(sources,"AcceptanceTexture.png"),replacement=Path.Combine(sources,"ReplacementTexture.png"),music=Path.Combine(sources,"AcceptanceMusic.wav"),dropped=Path.Combine(sources,"DroppedTexture.png");
        WriteFixtureTexture(texture,false);WriteFixtureTexture(replacement,true);WriteFixtureMusic(music);WriteFixtureTexture(dropped,true);
        string model=Path.Combine(sources,"AcceptanceModel.obj"),mtl=Path.Combine(sources,"AcceptanceModel.mtl");
        File.WriteAllText(mtl,"newmtl AcceptanceModelMaterial\nKd 1 1 1\nmap_Kd AcceptanceTexture.png\n");
        string originalObj="mtllib AcceptanceModel.mtl\no AcceptanceQuad\nv -1 0 -1\nv 1 0 -1\nv 1 0 1\nv -1 0 1\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nusemtl AcceptanceModelMaterial\nf 1/1 2/2 3/3 4/4\n";
        File.WriteAllText(model,originalObj);
        var immutable=Directory.GetFiles(sources).ToDictionary(path=>path,File.ReadAllBytes);
        var map=document.Host.Document!;
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ImportAssetAsync",texture,CancellationToken.None),"actual texture import, fallback and HD material");
        Check(map.Project.Definition.Assets.Count(asset=>asset.SourcePath==texture)==2
            &&map.Project.Definition.Materials.Any(material=>material.Name=="AcceptanceTexture"&&material.Texture is not null&&material.Albedo is not null),
            "authoritative import produces actual portable source and baked fallback texture bytes");
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ImportDroppedFilesAsync",new[]{dropped},CancellationToken.None),"actual external file-drop import pipeline");
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ImportAssetAsync",music,CancellationToken.None),"actual audio import and music assignment");
        Check(map.Project.Definition.Audio?.Music is { } audio&&MapAssets.Read(map.Project.Definition,audio).SequenceEqual(immutable[music]),
            "actual music import retains exact source WAV bytes");
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ImportModelAsync",model,new ModelImportSettings(Collision:ModelCollisionMode.Visual),null,CancellationToken.None),"actual model import with material dependencies");
        var importedSource=map.Project.Definition.ModelSources.Single(source=>source.Source==model);Guid sourceId=importedSource.Id;
        var importedIds=importedSource.Objects.Select(item=>item.Id).Order().ToArray();string sourceHash=importedSource.SourceHash;
        File.WriteAllText(model,originalObj.Replace("v 1 0 1","v 1 1 1",StringComparison.Ordinal));immutable[model]=File.ReadAllBytes(model);
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ImportModelAsync",model,new ModelImportSettings(Collision:ModelCollisionMode.Visual),sourceId,CancellationToken.None),"actual source model reimport");
        importedSource=map.Project.Definition.ModelSources.Single(source=>source.Id==sourceId);
        Check(importedSource.Objects.Select(item=>item.Id).Order().SequenceEqual(importedIds)&&importedSource.SourceHash!=sourceHash,
            "model reimport preserves canonical object identities and updates captured source identity");
        int unchanged=map.History.CommandCount;await MapFacadeAsync(document,"ImportModelAsync",model,new ModelImportSettings(Collision:ModelCollisionMode.Visual),sourceId,CancellationToken.None);
        Check(map.History.CommandCount==unchanged,"unchanged actual model reimport creates no redundant authored history");
        var material=map.Project.Definition.Materials.Single(material=>material.Name=="AcceptanceTexture");string modern=material.Albedo!,fallback=material.Texture!;
        Guid materialId=material.Id;
        map.Edit("Author modern channels and flipbook",definition=>
        {
            var target=definition.Materials.Single(value=>value.Id==materialId);target.Normal=modern;target.SpecularRoughness=modern;target.Emissive=modern;
            target.Animation=new(){FlipbookFrames=[fallback]};
        },MapChangeDomain.Material);
        byte[] oldModern=MapAssets.Read(map.Project.Definition,modern),oldFallback=MapAssets.Read(map.Project.Definition,fallback);
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ReplaceAssetAsync",modern,replacement,CancellationToken.None),"actual modern texture replacement");
        material=map.Project.Definition.Materials.Single(value=>value.Id==materialId);string replacedModern=material.Albedo!;
        Check(replacedModern!=modern&&material.Normal==replacedModern&&material.SpecularRoughness==replacedModern&&material.Emissive==replacedModern
            &&MapAssets.Read(map.Project.Definition,replacedModern).SequenceEqual(immutable[replacement])
            &&File.ReadAllBytes(Path.Combine(map.Project.Definition.BaseDirectory!,modern)).SequenceEqual(oldModern),
            "replacement updates all canonical modern material references and retains old leased asset bytes for Undo");
        await OneAssetUndoAsync(map,()=>MapFacadeAsync(document,"ReplaceAssetAsync",fallback,replacement,CancellationToken.None),"actual fallback and flipbook replacement");
        material=map.Project.Definition.Materials.Single(value=>value.Id==materialId);
        Check(material.Texture!=fallback&&material.Animation!.FlipbookFrames.SequenceEqual([material.Texture!])
            &&File.ReadAllBytes(Path.Combine(map.Project.Definition.BaseDirectory!,fallback)).SequenceEqual(oldFallback),
            "actual baked replacement updates native texture and every flipbook reference without overwriting historical bytes");
        var viewport=document.Host.GetVisualDescendants().OfType<Control>().Single(control=>control.GetType().Name=="MapViewport"&&control.IsEffectivelyVisible);
        PumpLayout(window);Point drop=new(viewport.Bounds.Width/2,viewport.Bounds.Height/2);
        await OneAssetUndoAsync(map,()=>ApplyAssetDropFacadeAsync(document,"Material",materialId.ToString(),drop),"actual typed material surface drop");
        Check(map.Project.Definition.Geometry.Any(geometry=>geometry.Material==map.Project.Definition.Materials.FindIndex(value=>value.Id==materialId)
            ||geometry is MapMesh mesh&&mesh.FaceMaterials.Contains(map.Project.Definition.Materials.FindIndex(value=>value.Id==materialId))),
            "typed browser drop paints a canonical authored surface");
        map.Edit("Clear music for drop fixture",definition=>definition.Audio!.Music=null,MapChangeDomain.Metadata);
        string musicAsset=map.Project.Definition.Assets.Single(asset=>asset.SourcePath==music).Path;
        await OneAssetUndoAsync(map,()=>ApplyAssetDropFacadeAsync(document,"Audio",musicAsset,drop),"actual typed audio assignment");
        await OneAssetUndoAsync(map,()=>ApplyAssetDropFacadeAsync(document,"Model",model,drop),"actual typed model placement");
        string prefab=Path.Combine(paths.UserDataDirectory,"map-projects",".prefabs","AcceptancePrefab.json");
        // Use the authored source quad: copying the template's entire floor at
        // the drop point would intentionally bury the template's player spawns.
        MapPrefabService.Save(map.Project.Definition,new HashSet<Guid>{importedIds[0]},prefab);
        await OneAssetUndoAsync(map,()=>ApplyAssetDropFacadeAsync(document,"Prefab",prefab,drop),"actual typed prefab placement");
        Check(map.Project.Definition.PrefabInstances.Any(instance=>instance.SourcePath==prefab),"typed prefab drop retains a canonical linked instance");
        await CheckAssetDragContextsAsync(window,document,paths,materialId,prefab,drop);
        document.Host.OpenAssetBrowser();Window browser=document.AssetBrowserWindow!;
        try
        {
            SetAssetSearch(browser,"AcceptanceTexture HD");await MapFacadeAsync(document,"WaitForAssetThumbnailsAsync");
            TextBox tags=browser.GetVisualDescendants().OfType<TextBox>().Single(box=>Placeholder(box)=="Tags separated by commas");tags.Text="acceptance, vivid";
            Control apply=browser.GetVisualDescendants().OfType<Control>().Single(control=>control.GetType().Name=="PrimeButton"
                &&control.GetType().GetProperty("Label")?.GetValue(control)?.ToString()=="APPLY TAGS");Click(browser,apply);
            Check(map.Project.Definition.Assets.Single(asset=>asset.Path==replacedModern).Tags.SequenceEqual(["acceptance","vivid"]),
                "actual Asset Browser tags action edits canonical asset metadata");
            SetAssetSearch(browser,"vivid");await CaptureLoadedAssetVariantsAsync(document,browser,output,"tagged-texture");
            Check(browser.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.Text?.StartsWith("1 matching assets",StringComparison.Ordinal)==true),
                "tagged browser search filters actual loaded texture references");
            var unused=browser.GetVisualDescendants().OfType<CheckBox>().Single(box=>box.Content?.ToString()=="Only unused assets");unused.IsChecked=true;
            Check(browser.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.Text?.StartsWith("0 matching assets",StringComparison.Ordinal)==true),
                "used modern texture is excluded by actual unused filter");unused.IsChecked=false;
            foreach((string query,string route) in new[]{("AcceptanceMusic","audio"),("model AcceptanceModel","model-source"),("prefab AcceptancePrefab","prefab")})
            {SetAssetSearch(browser,query);await CaptureLoadedAssetVariantsAsync(document,browser,output,route);}
        }
        finally{browser.Close();}
        Check(immutable.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),"real import, replacement, reimport and browser operations preserve external source bytes");
        Check(map.Project.Definition.Assets.All(asset=>File.Exists(Path.Combine(map.Project.Definition.BaseDirectory!,asset.Path))),
            "all declared imported canonical asset files exist after Undo/Redo and replacement");
        await CheckAssetDestinationOwnershipAsync(window,document,sources,texture);
        await CheckMissingAssetLifecycleAsync(window,paths,texture,output);
    }

    private static async Task CheckMissingAssetLifecycleAsync(StudioWindow window,StudioPaths paths,string texture,string output)
    {
        var broken=new MapStudioDocument(paths,window,window.Jobs);broken.NewProject("MISSING_ASSET_FIXTURE",example:true);window.Documents.Add(broken);
        try
        {
            await MapFacadeAsync(broken,"ImportAssetAsync",texture,CancellationToken.None);await MapFacadeAsync(broken,"WaitForAssetThumbnailsAsync");
            var definition=broken.Host.Document!.Project.Definition;string file=Path.Combine(definition.BaseDirectory!,definition.Materials.Single(material=>material.Name=="AcceptanceTexture").Albedo!);
            File.Delete(file);broken.Host.OpenAssetBrowser();var browser=broken.AssetBrowserWindow!;SetAssetSearch(browser,"AcceptanceTexture HD");
            bool failed=false;
            try{await MapFacadeAsync(broken,"WaitForAssetThumbnailsAsync");}catch(Exception ex)when(ex is IOException or InvalidDataException){failed=true;}
            PumpLayout(browser);
            Check(failed&&browser.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.Text?.StartsWith("Thumbnail unavailable",StringComparison.Ordinal)==true
                &&block.Text.Contains(Path.GetFileName(file),StringComparison.Ordinal)),
                "missing declared texture faults explicit readiness and shows an actionable thumbnail file error");
            using var image=browser.CaptureRenderedFrame()??throw new InvalidOperationException("Missing asset error view did not render.");
            image.Save(Path.Combine(output,"map-studio-missing-asset.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Check(await window.Documents.RequestCloseAsync(broken,_=>Task.FromResult(ProjectPrime.Studio.Shell.StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null))
                &&broken.AssetBrowserWindow is null&&!window.Documents.Documents.Contains(broken),
                "missing thumbnail failure still permits actual discard close and releases document, owned browser and tracked work");
        }
        finally{await broken.DisposeAsync();}
    }

    private static object CreateAssetDragFacade(MapStudioDocument document,string kind,string key)
    {
        var api=document.Host.GetType().GetMethod("CreateAssetDragData")??throw new MissingMethodException("Production browser drag ownership factory missing.");
        return api.Invoke(document.Host,[Enum.Parse(api.GetParameters()[0].ParameterType,kind),key])!;
    }
    private static async Task CheckAssetDragContextsAsync(StudioWindow window,MapStudioDocument document,StudioPaths paths,Guid material,string prefab,Point drop)
    {
        var map=document.Host.Document!;object payload=CreateAssetDragFacade(document,"Material",material.ToString());
        map.Edit("Change map during browser drag",definition=>definition.Name+=" revision",MapChangeDomain.Metadata);
        await RejectAssetMutationAsync(map,()=>MapFacadeAsync(document,"ApplyAssetDropAsync",payload,drop),"browser payload rejects a stale canonical history state");map.History.Undo();
        object prefabPayload=CreateAssetDragFacade(document,"Prefab",prefab);DateTime timestamp=File.GetLastWriteTimeUtc(prefab);
        try{File.SetLastWriteTimeUtc(prefab,timestamp.AddSeconds(2));await RejectAssetMutationAsync(map,()=>MapFacadeAsync(document,"ApplyAssetDropAsync",prefabPayload,drop),"browser payload rejects changed prefab source identity");}
        finally{File.SetLastWriteTimeUtc(prefab,timestamp);}
        Type data=payload.GetType();object raw=Activator.CreateInstance(data,map.Project.Definition.MapId,
            Enum.Parse(data.GetProperty("Kind")!.PropertyType,"Material"),material.ToString())!;
        await RejectAssetMutationAsync(map,()=>MapFacadeAsync(document,"ApplyAssetDropAsync",raw,drop),"raw browser payload without an editor ownership context is rejected");
        await using var other=new MapStudioDocument(paths,window,window.Jobs);other.NewProject("OTHER ASSET OWNER",example:true);
        var second=other.Host.Document!;second.Edit("Prepare same identity fixture",definition=>{definition.MapId=map.Project.Definition.MapId;definition.Materials[0].Id=material;});
        await RejectAssetMutationAsync(second,()=>MapFacadeAsync(other,"ApplyAssetDropAsync",CreateAssetDragFacade(document,"Material",material.ToString()),drop),
            "different editor with the same MapId rejects another owner's browser payload");
        object beforeSave=CreateAssetDragFacade(other,"Material",material.ToString());
        await other.SaveAsync(Path.Combine(paths.UserDataDirectory,"other-asset-owner.json"),CancellationToken.None);
        await RejectAssetMutationAsync(second,()=>MapFacadeAsync(other,"ApplyAssetDropAsync",beforeSave,drop),"Save As invalidates browser payload location context even when authored state stays saved");
    }
    private static async Task RejectAssetMutationAsync(MapDocument map,Func<Task> action,string message)
    {
        string definition=map.Project.Definition.Serialize();var state=map.CurrentStateId;int count=map.History.CommandCount;bool rejected=false;
        try{await action();}catch(Exception ex)when(ex is InvalidOperationException or InvalidDataException or IOException or OperationCanceledException){rejected=true;}
        Check(rejected&&map.Project.Definition.Serialize()==definition&&map.CurrentStateId==state&&map.History.CommandCount==count,message+" without any canonical mutation");
    }
    private static async Task CheckAssetDestinationOwnershipAsync(StudioWindow window,MapStudioDocument document,string sources,string texture)
    {
        string root=document.Host.Document!.Project.Definition.BaseDirectory!;
        string outside=Path.Combine(sources,"outside-owner"),sentinel=Path.Combine(outside,"sentinel.bin");Directory.CreateDirectory(outside);File.WriteAllBytes(sentinel,[31,27,19]);
        string textures=Path.Combine(root,"textures"),preserved=textures+".acceptance-owned";
        await MapFacadeAsync(document,"WaitForAssetThumbnailsAsync");
        try
        {
            Directory.Move(textures,preserved);
            try{Directory.CreateSymbolicLink(textures,outside);}catch(Exception ex)when(ex is UnauthorizedAccessException or IOException)
            {Console.WriteLine("Asset symlink fixture unavailable on this platform: "+ex.Message);return;}
            await RejectAssetMutationAsync(document.Host.Document!,()=>MapFacadeAsync(document,"ImportAssetAsync",texture,CancellationToken.None),
                "actual import rejects a parent symlink resolving beyond the owned project");
            Check(Directory.GetFiles(outside).SequenceEqual([sentinel])&&File.ReadAllBytes(sentinel).SequenceEqual(new byte[]{31,27,19}),
                "rejected import preserves every outside-owner byte and file");
        }
        finally
        {
            if(new DirectoryInfo(textures).LinkTarget is not null)Directory.Delete(textures);
            if(Directory.Exists(preserved))Directory.Move(preserved,textures);
        }
        string savedRoot=root+".acceptance-owned";bool swapped=false;int attempted=0;Exception? swapError=null;
        void SwapRoot()
        {
            if(!window.Jobs.Jobs.Any(job=>job.Title=="Importing asset"&&job.State==ProjectPrime.Studio.Jobs.StudioJobState.Running)
                ||Interlocked.CompareExchange(ref attempted,1,0)!=0)return;
            try{Directory.Move(root,savedRoot);Directory.CreateSymbolicLink(root,outside);swapped=true;}catch(Exception ex){swapError=ex;}
        }
        window.Jobs.Changed+=SwapRoot;
        try
        {
            await RejectAssetMutationAsync(document.Host.Document!,()=>MapFacadeAsync(document,"ImportAssetAsync",texture,CancellationToken.None),
                "actual import detects project root replacement between capture and publication");
            Check(swapped&&swapError is null&&Directory.GetFiles(outside).SequenceEqual([sentinel])&&File.ReadAllBytes(sentinel).SequenceEqual(new byte[]{31,27,19}),
                "mid-operation root substitution never writes or cleans another owner's files");
        }
        finally
        {
            window.Jobs.Changed-=SwapRoot;
            if(new DirectoryInfo(root).LinkTarget is not null)Directory.Delete(root);
            if(Directory.Exists(savedRoot))Directory.Move(savedRoot,root);
        }
    }

    private static async Task OneAssetUndoAsync(MapDocument map,Func<Task> action,string label)
    {
        string before=map.Project.Definition.Serialize();int count=map.History.CommandCount;var state=map.CurrentStateId;
        int changes=0;void Changed(MapDocumentChange _)=>changes++;map.History.Changed+=Changed;
        try{await action();}finally{map.History.Changed-=Changed;}string after=map.Project.Definition.Serialize();
        Check(changes==1&&map.CurrentStateId!=state&&after!=before,label+" is one canonical history edit; historyEvents="+changes+",beforeCount="+count
            +",afterCount="+map.History.CommandCount+",beforeState="+state+",afterState="+map.CurrentStateId+",definitionChanged="+(after!=before));
        map.History.Undo();Check(map.Project.Definition.Serialize()==before&&map.CurrentStateId==state,label+" one Undo restores every authored field");
        map.History.Redo();Check(map.Project.Definition.Serialize()==after,label+" Redo restores exact canonical edit");
    }
    private static async Task MapFacadeAsync(MapStudioDocument document,string method,params object?[] arguments)
    {
        var api=document.Host.GetType().GetMethod(method)??throw new MissingMethodException("Production Map facade is missing "+method);
        try{await ((Task?)api.Invoke(document.Host,arguments)??throw new InvalidOperationException("Map facade did not return its owned work."));}
        catch(TargetInvocationException ex)when(ex.InnerException is not null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }
    private static Task ApplyAssetDropFacadeAsync(MapStudioDocument document,string kind,string key,Point point)
    {
        var api=document.Host.GetType().GetMethod("ApplyAssetDropAsync")??throw new MissingMethodException("Production typed asset drop facade missing.");
        Type data=api.GetParameters()[0].ParameterType;Type enumeration=data.GetProperty("Kind")!.PropertyType;
        object value=document.Host.GetType().GetMethod("CreateAssetDragData") is { } factory
            ?factory.Invoke(document.Host,[Enum.Parse(enumeration,kind),key])!
            :Activator.CreateInstance(data,document.Host.Document!.Project.Definition.MapId,Enum.Parse(enumeration,kind),key)!;
        return MapFacadeAsync(document,"ApplyAssetDropAsync",value,point);
    }
    private static string? Placeholder(TextBox box)=>box.GetType().GetProperty("PlaceholderText")?.GetValue(box)?.ToString()
        ??box.GetType().GetProperty("Watermark")?.GetValue(box)?.ToString();
    private static void SetAssetSearch(Window browser,string value)
    {browser.GetVisualDescendants().OfType<TextBox>().Single(box=>Placeholder(box)=="Search names, paths and tags").Text=value;PumpLayout(browser);}
    private static async Task CaptureLoadedAssetVariantsAsync(MapStudioDocument document,Window browser,string output,string route)
    {
        foreach((int width,int height,double scale) in new[]{(760,640,1d),(1200,900,1d),(760,640,2d)})
        {
            browser.Width=width;browser.Height=height;browser.SetRenderScaling(scale);PumpLayout(browser);
            await MapFacadeAsync(document,"WaitForAssetThumbnailsAsync");PumpLayout(browser);
            if(!browser.GetVisualDescendants().OfType<Image>().Any(image=>image.Source is not null&&image.IsEffectivelyVisible))
            {
                using var failed=browser.CaptureRenderedFrame();failed?.Save(Path.Combine(output,"failed-loaded-"+route+".png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                File.WriteAllText(Path.Combine(output,"failed-loaded-"+route+".txt"),string.Join(Environment.NewLine,browser.GetVisualDescendants().OfType<TextBlock>().Select(block=>block.Text))
                    +Environment.NewLine+"Images: "+string.Join(";",browser.GetVisualDescendants().OfType<Image>().Select(image=>new{image.IsEffectivelyVisible,Loaded=image.Source is not null,image.Bounds})));
            }
            Check(browser.GetVisualDescendants().OfType<Image>().Any(image=>image.Source is not null&&image.IsEffectivelyVisible),
                "loaded "+route+" browser has completed real thumbnail pixels");
            Check(!browser.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.Text?.StartsWith("Thumbnail unavailable",StringComparison.Ordinal)==true),
                "loaded "+route+" browser thumbnail preparation succeeds");
            using var bitmap=browser.CaptureRenderedFrame()??throw new InvalidOperationException("Loaded Asset Browser did not render.");
            CheckImageContent(bitmap,"loaded-assets-"+route);string file="map-studio-loaded-assets-"+route+"-"+width+"x"+height+(scale==1?"":"-2x")+".png";
            bitmap.Save(Path.Combine(output,file),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Captures.Add(new{route="loaded-assets-"+route,logicalWidth=width,logicalHeight=height,scale,file,pixelWidth=bitmap.PixelSize.Width,pixelHeight=bitmap.PixelSize.Height});
        }
    }
    private static void WriteFixtureTexture(string path,bool alternate)
    {
        using var bitmap=new SKBitmap(32,32);for(int y=0;y<32;y++)for(int x=0;x<32;x++)bitmap.SetPixel(x,y,
            alternate?new SKColor((byte)(x*7),(byte)(y*7),220):new SKColor((byte)(((x/4+y/4)%2)*200+30),100,(byte)(x*7)));
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);png.SaveTo(file);
    }
    private static void WriteFixtureMusic(string path)
    {
        const int frames=4800;using var writer=new BinaryWriter(File.Create(path));writer.Write("RIFF"u8);writer.Write(36+frames*2);writer.Write("WAVEfmt "u8);
        writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(48000);writer.Write(96000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(frames*2);
        for(int frame=0;frame<frames;frame++)writer.Write((short)(Math.Sin(frame*2*Math.PI*440/48000)*8192));
    }
}
