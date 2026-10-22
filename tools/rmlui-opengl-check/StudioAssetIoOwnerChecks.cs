using System.Reflection;
using System.Security.Cryptography;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Private D fixture: existing fresh-userdata headless owner setup, real worker/IO/history, no OS dialog or GL.</summary>
internal static class StudioAssetIoOwnerChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(CustomRooms.UserMapDirectory,"native-asset-owner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string projectRoot=Path.Combine(root,"project");Directory.CreateDirectory(projectRoot);string source=Path.Combine(root,"source.png"),replacementSource=Path.Combine(root,"replacement.png");
        byte[] blue=Image(SkiaSharp.SKColors.CornflowerBlue),green=Image(SkiaSharp.SKColors.SeaGreen);File.WriteAllBytes(source,blue);File.WriteAllBytes(replacementSource,green);
        using var owner=new MapStudioScreen(preview:true);check(owner.TryAcquireNativeEditor(out var acquired,out _)&&acquired!=null,"actual retained owner grants asset IO lease");
        using var session=acquired!;var io=(INativeStudioEditorAssetIo)(INativeStudioEditorSession)session;var resources=(INativeStudioEditorResources)(INativeStudioEditorSession)session;
        var document=Field<MapDocument>(owner,"_document");var viewport=Field<MapViewport>(owner,"_viewport");var history=document.History;
        string save=Path.Combine(projectRoot,"map.json");check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Save,new(Path:save)).Success,"asset IO starts from real saved editable project");
        var writer=Field<MapAutosaveService>(owner,"_autosave");var watcher=Field<MapSourceWatchService>(owner,"_sourceWatch");
        string before=document.Project.Definition.Serialize();var beforeState=document.CurrentStateId;int commands=history.CommandCount;
        check(io.ReviewAssetIo(Empty(resources),new(NativeStudioAssetIoKind.ImportTexture,source,"","OWNER_IMPORTED")).Success,"actual IO owner starts bounded texture review");
        var reviewed=Pump(io);check(reviewed.Stage==NativeStudioAssetIoStage.Ready&&reviewed.Width==4&&reviewed.Height==4&&reviewed.PortableBytes==blue.Length&&reviewed.NativeBytes>0,
            "actual worker probes and bakes texture without creating document edits");
        check(document.Project.Definition.Serialize()==before&&document.CurrentStateId==beforeState&&history.CommandCount==commands,"review keeps exact authored state/history");
        check(io.ApplyAssetIo(reviewed.Ticket.Operation,false).Success,"actual reviewed import begins existing owner worker");var applied=Pump(io);
        check(applied.Stage==NativeStudioAssetIoStage.Complete&&history.CommandCount==commands+1,"import commits one real history command after worker drains");
        Guid importedMaterialId=document.Project.Definition.Materials.Last().Id;
        var native=document.Project.Definition.Assets.Single(a=>a.Path.EndsWith(".tex",StringComparison.Ordinal));var hd=document.Project.Definition.Assets.Single(a=>a.Path.EndsWith(".png",StringComparison.Ordinal));
        check(MapAssets.Read(document.Project.Definition,hd.Path).SequenceEqual(blue)&&MapAssets.Read(document.Project.Definition,native.Path).SequenceEqual(MapTextureBake.BakeImage(blue)),"actual owner publishes exact portable/native bytes readable by MapAssets");
        check(Generated(document).ContainsKey(native.Path)&&Generated(document).ContainsKey(hd.Path),"every real generated publication is registered on captured document");
        string after=document.Project.Definition.Serialize();check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Undo,default).Success&&document.Project.Definition.Serialize()==before,
            "actual asset import one-step undo restores exact authored bytes");
        check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Redo,default).Success&&document.Project.Definition.Serialize()==after,"actual asset import redo restores exact declarations/material");
        document.Edit("Fixture HD reference coverage",d=>{var material=d.Materials.Last();material.Normal=hd.Path;material.SpecularRoughness=hd.Path;material.Emissive=hd.Path;});
        before=document.Project.Definition.Serialize();commands=history.CommandCount;var hdTicket=Asset(resources,hd.Path);
        check(io.ReviewAssetIo(hdTicket,new(NativeStudioAssetIoKind.ReplaceTexture,replacementSource,"","")).Success,"actual selected HD asset begins replacement review");reviewed=Pump(io);
        check(io.ApplyAssetIo(reviewed.Ticket.Operation,false).Success,"reviewed HD replacement begins bounded publication");applied=Pump(io);
        var current=document.Project.Definition.Materials.Last();string newHd=current.Albedo!;
        check(applied.Stage==NativeStudioAssetIoStage.Complete&&newHd!=hd.Path&&current.Normal==newHd&&current.SpecularRoughness==newHd&&current.Emissive==newHd
            &&MapAssets.Read(document.Project.Definition,newHd).SequenceEqual(green)&&File.Exists(Path.Combine(projectRoot,hd.Path)),"real HD replacement remaps all channels and retains prior bytes");
        check(history.CommandCount==commands+1&&session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Undo,default).Success&&document.Project.Definition.Serialize()==before,"actual replacement one-step undo restores exact source");
        check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Redo,default).Success,"actual replacement redo is available");
        var selected=Asset(resources,newHd);check(io.ReviewAssetIo(selected,new(NativeStudioAssetIoKind.ReplaceTexture,replacementSource,"","")).Success,"review source for stale-byte rejection");reviewed=Pump(io);
        File.WriteAllBytes(replacementSource,blue);before=document.Project.Definition.Serialize();int generatedBefore=Generated(document).Count;
        var staleAttempt=io.ApplyAssetIo(reviewed.Ticket.Operation,false);
        applied=staleAttempt.Success?Pump(io):io.CaptureAssetIo()!;
        check((!staleAttempt.Success||applied.Stage==NativeStudioAssetIoStage.Failed)&&document.Project.Definition.Serialize()==before&&Generated(document).Count==generatedBefore,"changed source hash/observed notification rejects replacement without authored or file effects");
        File.WriteAllBytes(replacementSource,green);
        string export=Path.Combine(root,"export.png");selected=Asset(resources,newHd);check(io.ReviewAssetIo(selected,new(NativeStudioAssetIoKind.ExportOriginal,"",export,"")).Success,"actual original-asset export starts reviewed destination read");reviewed=Pump(io);
        check(reviewed.Stage==NativeStudioAssetIoStage.Ready&&io.ApplyAssetIo(reviewed.Ticket.Operation,false).Success,"missing export target uses explicit new-destination action");applied=Pump(io);
        check(applied.Stage==NativeStudioAssetIoStage.Complete&&File.ReadAllBytes(export).SequenceEqual(green)&&document.Project.Definition.Serialize()==before,"actual export writes exact asset bytes without authoring mutation");
        selected=Asset(resources,newHd);check(io.ReviewAssetIo(selected,new(NativeStudioAssetIoKind.ExportOriginal,"",export,"")).Success,"existing target is reviewed separately");reviewed=Pump(io);
        check(reviewed.Stage==NativeStudioAssetIoStage.NeedsOverwrite&&!io.ApplyAssetIo(reviewed.Ticket.Operation,false).Success,"existing target requires explicit reviewed overwrite");
        using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            SetCheckpoint(session,(stage,token)=>{if(stage=="before-export-publication"){reached.Set();release.Wait(token);}});
            check(io.ApplyAssetIo(reviewed.Ticket.Operation,true).Success,"actual overwrite begins with reviewed target ticket");Wait(reached);
            byte[] raced={91,92,93,94};File.WriteAllBytes(export,raced);release.Set();applied=Pump(io);SetCheckpoint(session,null);
            check(applied.Stage==NativeStudioAssetIoStage.Failed&&applied.RecoveryPath!=null&&File.ReadAllBytes(applied.RecoveryPath).SequenceEqual(raced)
                &&!File.Exists(export)&&document.Project.Definition.Serialize()==before,"actual worker overwrite race preserves displaced external bytes, refuses target publication, and rejects verified-success claim");
        }
        string redirectedExport=Path.Combine(root,"redirected-export.png"),sourceAsset=Path.Combine(projectRoot,newHd),retainedAsset=sourceAsset+".retained",equalExternal=Path.Combine(root,"equal-external.png");
        File.WriteAllBytes(equalExternal,green);selected=Asset(resources,newHd);
        check(io.ReviewAssetIo(selected,new(NativeStudioAssetIoKind.ExportOriginal,"",redirectedExport,"")).Success,"actual loose export reviews a direct contained source before redirection");reviewed=Pump(io);
        File.Move(sourceAsset,retainedAsset);File.CreateSymbolicLink(sourceAsset,equalExternal);
        staleAttempt=io.ApplyAssetIo(reviewed.Ticket.Operation,false);applied=staleAttempt.Success?Pump(io):io.CaptureAssetIo()!;
        check((!staleAttempt.Success||applied.Stage==NativeStudioAssetIoStage.Failed)&&!File.Exists(redirectedExport)&&File.ReadAllBytes(equalExternal).SequenceEqual(green),
            "real reviewed loose export rejects equally hashed linked source redirection before publication and preserves external bytes");
        File.Delete(sourceAsset);File.Move(retainedAsset,sourceAsset);
        string rawAlias=Path.Combine(root,"read-root-alias"),otherRoot=Path.Combine(root,"other-project-root"),redirectDestination=Path.Combine(root,"root-redirect-export.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(otherRoot,newHd))!);File.WriteAllBytes(Path.Combine(otherRoot,newHd),green);
        Directory.CreateSymbolicLink(rawAlias,projectRoot);document.Project.Definition.BaseDirectory=rawAlias;
        using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            SetCheckpoint(session,(stage,token)=>{if(stage=="read-start"){reached.Set();release.Wait(token);}});
            check(io.ReviewAssetIo(Asset(resources,newHd),new(NativeStudioAssetIoKind.ExportOriginal,"",redirectDestination,"")).Success,"actual export freezes canonical project root while retaining original root alias witness");Wait(reached);
            Directory.Delete(rawAlias);Directory.CreateSymbolicLink(rawAlias,otherRoot);release.Set();applied=Pump(io);SetCheckpoint(session,null);
            check(applied.Stage==NativeStudioAssetIoStage.Failed&&!File.Exists(redirectDestination)&&File.ReadAllBytes(Path.Combine(otherRoot,newHd)).SequenceEqual(green),
                "actual worker root alias redirection to equally hashed different project cannot become Ready or publish");
        }
        document.Project.Definition.BaseDirectory=projectRoot;Directory.Delete(rawAlias);
        string alias=Path.Combine(root,"project-alias");Directory.CreateSymbolicLink(alias,projectRoot);selected=Asset(resources,newHd);
        check(io.ReviewAssetIo(selected,new(NativeStudioAssetIoKind.ExportOriginal,"",Path.Combine(alias,"aliased.png"),"")).Success,"export review resolves destination parent aliases explicitly");applied=Pump(io);
        check(applied.Stage==NativeStudioAssetIoStage.Failed&&!File.Exists(Path.Combine(projectRoot,"aliased.png")),"canonical destination alias cannot export over current project assets");
        string linkedSource=Path.Combine(root,"linked-source.png");File.CreateSymbolicLink(linkedSource,source);
        check(io.ReviewAssetIo(Empty(resources),new(NativeStudioAssetIoKind.ImportTexture,linkedSource,"","LINKED_REJECTED")).Success,"linked source enters bounded review validation");applied=Pump(io);
        check(applied.Stage==NativeStudioAssetIoStage.Failed&&document.Project.Definition.Serialize()==before,"linked source leaf refuses import without changing actual document");
        using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            SetCheckpoint(session,(stage,token)=>{if(stage=="generated-published"){reached.Set();release.Wait(token);}});
            check(io.ReviewAssetIo(Empty(resources),new(NativeStudioAssetIoKind.ImportTexture,source,"","CANCELED_PUBLICATION")).Success,"prepare cancellation-after-publication case");reviewed=Pump(io);
            generatedBefore=Generated(document).Count;check(io.ApplyAssetIo(reviewed.Ticket.Operation,false).Success,"start actual generated publication before cancellation");Wait(reached);
            io.CancelAssetIo(reviewed.Ticket.Operation);applied=Pump(io);SetCheckpoint(session,null);
            check(applied.Stage==NativeStudioAssetIoStage.Failed&&document.Project.Definition.Serialize()==before&&Generated(document).Count==generatedBefore+1
                &&Generated(document).Keys.All(path=>File.Exists(Path.Combine(projectRoot,path))),"canceled actual publication registers its file effect before discarding stale result");
        }
        check(ReferenceEquals(document,Field<MapDocument>(owner,"_document"))&&ReferenceEquals(history,document.History)&&ReferenceEquals(writer,Field<MapAutosaveService>(owner,"_autosave"))
            &&ReferenceEquals(watcher,Field<MapSourceWatchService>(owner,"_sourceWatch"))&&ReferenceEquals(viewport,Field<MapViewport>(owner,"_viewport"))&&Field<object?>(viewport,"_renderer")==null,
            "all IO retains exact document/history/autosave/watcher/viewport and creates no renderer");
        check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Save,new(Path:save)).Success&&!document.IsDirty,
            "actual asset IO project Save delegates the sole existing writer");
        var reopened=MapProjectSerializer.Load(save);
        var reopenedMaterial=reopened.Definition.Materials.Single(m=>m.Id==importedMaterialId);
        bool exactHdBytes=MapAssets.Read(reopened.Definition,newHd).SequenceEqual(green),exactNativeBytes=MapAssets.Read(reopened.Definition,native.Path).SequenceEqual(MapTextureBake.BakeImage(blue));
        Console.WriteLine("STUDIO ASSET SAVE REOPEN DIAGNOSTIC "+System.Text.Json.JsonSerializer.Serialize(new{importedMaterialId,expectedHd=newHd,expectedNative=native.Path,
            reopenedMaterial.Id,reopenedMaterial.Albedo,reopenedMaterial.Texture,exactHdBytes,exactNativeBytes,
            savedMaterials=reopened.Definition.Materials.Select(m=>new{m.Id,m.Name,m.Albedo,m.Texture}).ToArray()}));
        check(reopenedMaterial.Albedo==newHd&&exactHdBytes&&exactNativeBytes,
            "actual saved project reopens exact replacement and baked fallback through existing serializer/asset reader");
        before=document.Project.Definition.Serialize();
        var picker=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);Set(session,"AssetPickerOverride",(Func<Task<string?>>)(()=>picker.Task));
        check(io.ChooseAssetSource(Empty(resources)).Success,"actual retained picker ticket uses injected OS boundary only");var pick=io.CaptureAssetIo()!;
        io.CancelAssetIo(pick.Ticket.Operation);picker.SetResult(source);Thread.Sleep(5);
        check(io.CaptureAssetIo()!.Stage==NativeStudioAssetIoStage.Failed&&document.Project.Definition.Serialize()==before,"late canceled OS picker completion cannot change source or document");
        using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            SetCheckpoint(session,(stage,token)=>{if(stage=="read-start"){reached.Set();release.Wait(token);}});
            check(io.ReviewAssetIo(Empty(resources),new(NativeStudioAssetIoKind.ImportTexture,source,"","BACK_CANCEL")).Success,"actual worker begins cooperative Back cancellation case");Wait(reached);
            check(!session.TryClose().Success&&session.Capture().Alive,"Back cancels busy worker and retains live editor until drain");Pump(io);SetCheckpoint(session,null);
            check(session.TryClose().Success&&!session.Capture().Alive,"Back releases same lease after bounded worker retires and recovery completes");
        }
        check(owner.TryAcquireNativeEditor(out var forced,out _)&&forced!=null,"same retained owner can reacquire after canceled IO close");using var forcedSession=forced!;
        var forcedIo=(INativeStudioEditorAssetIo)(INativeStudioEditorSession)forcedSession;var forcedResources=(INativeStudioEditorResources)(INativeStudioEditorSession)forcedSession;
        using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            SetCheckpoint(forcedSession,(stage,token)=>{if(stage=="generated-published"){reached.Set();release.Wait(token);}});
            check(forcedIo.ReviewAssetIo(Empty(forcedResources),new(NativeStudioAssetIoKind.ImportTexture,source,"","FORCED_CANCEL")).Success,"forced retirement case reviews through real reacquired owner");reviewed=Pump(forcedIo);
            generatedBefore=Generated(document).Count;check(forcedIo.ApplyAssetIo(reviewed.Ticket.Operation,false).Success,"forced retirement case begins actual publication");Wait(reached);
            var timer=System.Diagnostics.Stopwatch.StartNew();forcedSession.Dispose();timer.Stop();
            check(timer.Elapsed<TimeSpan.FromSeconds(5)&&Generated(document).Count==generatedBefore+1&&Field<CancellationTokenSource?>(owner,"_work")==null
                &&document.Project.Definition.Serialize()==before&&!forcedSession.Capture().Alive,"forced Dispose drains cooperative worker without UI continuation deadlock and registers published effect");
        }
        Console.WriteLine("PRIVATE D ACTUAL OWNER ROOT "+root);
    }
    private static NativeStudioAssetIoSnapshot Pump(INativeStudioEditorAssetIo io)
    {for(int i=0;i<10000;i++){var state=io.CaptureAssetIo()??throw new InvalidOperationException("IO snapshot missing");if(!state.Busy)return state;Thread.Sleep(1);}throw new InvalidOperationException("Actual asset worker did not drain");}
    private static void Wait(ManualResetEventSlim signal){if(!signal.Wait(TimeSpan.FromSeconds(10)))throw new InvalidOperationException("Actual asset worker checkpoint not reached");}
    private static NativeStudioResourceTicket Empty(INativeStudioEditorResources resources){var s=resources.CaptureResources();return new(s.Owner,-1,Guid.Empty,"","","",s.SourceNotices);}
    private static NativeStudioResourceTicket Asset(INativeStudioEditorResources resources,string path){var s=resources.CaptureResources();var a=s.Assets.Single(a=>a.Path==path);return new(s.Owner,-1,Guid.Empty,"",a.Path,a.Fingerprint,s.SourceNotices);}
    private static Dictionary<string,string> Generated(MapDocument doc)=>Field<Dictionary<string,string>>(doc,"_generatedAssets");
    private static byte[] Image(SkiaSharp.SKColor color){using var bitmap=new SkiaSharp.SKBitmap(4,4);bitmap.Erase(color);using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);return encoded.ToArray();}
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Set(object owner,string name,object? value)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,value);
    private static void SetCheckpoint(object owner,Action<string,CancellationToken>? callback)=>Set(owner,"AssetIoWorkerCheckpoint",callback);
}
