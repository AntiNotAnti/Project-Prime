using System.Security.Cryptography;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Launcher;

internal static class StudioAssetFileChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-studio-d-files-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        byte[] original={1,2,3,4},written={5,6,7,8},raced={9,10,11,12};string source=Path.Combine(root,"source.bin");File.WriteAllBytes(source,original);
        var read=MapStudioReviewedAssetFiles.ReadSource(source);check(read.Bytes.SequenceEqual(original)&&read.Version.Sha256==Convert.ToHexString(SHA256.HashData(original)),"review reads exact bounded source bytes and SHA");
        File.WriteAllBytes(source,raced);check(!MapStudioReviewedAssetFiles.Matches(read.Version),"source content change invalidates review independent of path");
        string target=Path.Combine(root,"missing.bin");var missing=MapStudioReviewedAssetFiles.ReviewTarget(target);check(!missing.Exists,"missing export target review is explicit");
        var result=MapStudioReviewedAssetFiles.Publish(missing,written,()=>{});check(result.Verified&&File.ReadAllBytes(target).SequenceEqual(written)&&result.RecoveryPath==null,"missing target publishes exact bytes without replacement");
        var existing=MapStudioReviewedAssetFiles.ReviewTarget(target);result=MapStudioReviewedAssetFiles.Publish(existing,original,()=>{});
        check(result.Verified&&File.ReadAllBytes(target).SequenceEqual(original)&&result.RecoveryPath!=null&&File.ReadAllBytes(result.RecoveryPath).SequenceEqual(written),"explicit unchanged overwrite verifies new bytes and retains exact previous recovery receipt");
        string missingRace=Path.Combine(root,"missing-race.bin");missing=MapStudioReviewedAssetFiles.ReviewTarget(missingRace);bool refused=false;
        try{MapStudioReviewedAssetFiles.Publish(missing,written,()=>{},()=>File.WriteAllBytes(missingRace,raced));}catch(IOException){refused=true;}
        check(refused&&File.ReadAllBytes(missingRace).SequenceEqual(raced),"missing-target publication never overwrites a file created after review");
        existing=MapStudioReviewedAssetFiles.ReviewTarget(target);result=MapStudioReviewedAssetFiles.Publish(existing,written,()=>{},()=>File.WriteAllBytes(target,raced));
        check(!result.Verified&&result.RecoveryPath!=null&&File.ReadAllBytes(result.RecoveryPath).SequenceEqual(raced)&&!File.Exists(target),
            "overwrite race preserves ACTUAL displaced external bytes and refuses publication before replacing reviewed source");
        check(result.Notice.Contains("changed",StringComparison.OrdinalIgnoreCase)&&File.Exists(result.RecoveryPath),"raced overwrite recovery is disclosed and never silently deleted");
        File.WriteAllBytes(target,original);
        existing=MapStudioReviewedAssetFiles.ReviewTarget(target);File.WriteAllBytes(target,original);refused=false;
        try{MapStudioReviewedAssetFiles.Publish(existing,written,()=>{});}catch(InvalidOperationException){refused=true;}
        check(refused&&File.ReadAllBytes(target).SequenceEqual(original),"changed reviewed target refuses before publication");
        existing=MapStudioReviewedAssetFiles.ReviewTarget(target);int verifies=0;refused=false;
        try{MapStudioReviewedAssetFiles.Publish(existing,written,()=>{if(++verifies==2)throw new OperationCanceledException();});}catch(OperationCanceledException){refused=true;}
        check(refused&&verifies==2&&File.ReadAllBytes(target).SequenceEqual(original),"owner retirement after staging but before publication leaves target untouched");
        existing=MapStudioReviewedAssetFiles.ReviewTarget(target);verifies=0;refused=false;
        try{MapStudioReviewedAssetFiles.Publish(existing,written,()=>{if(++verifies==3)throw new OperationCanceledException();});}catch(OperationCanceledException){refused=true;}
        check(refused&&verifies==3&&File.ReadAllBytes(target).SequenceEqual(original),"owner retirement immediately before displacement leaves target untouched");
        string blocked=Path.Combine(root,"recoveries-full.bin");File.WriteAllBytes(blocked,original);var blockedReview=MapStudioReviewedAssetFiles.ReviewTarget(blocked);
        for(int i=0;i<4;i++)File.WriteAllBytes(blocked+".prime-asset-recovery-"+i,raced);refused=false;
        try{MapStudioReviewedAssetFiles.Publish(blockedReview,written,()=>{});}catch(IOException){refused=true;}
        check(refused&&File.ReadAllBytes(blocked).SequenceEqual(original)&&Enumerable.Range(0,4).All(i=>File.ReadAllBytes(blocked+".prime-asset-recovery-"+i).SequenceEqual(raced)),"full recovery slots retain every prior file and refuse new overwrite");
        string link=Path.Combine(root,"linked.bin");File.CreateSymbolicLink(link,target);refused=false;
        try{MapStudioReviewedAssetFiles.ReviewTarget(link);}catch(IOException){refused=true;}
        check(refused&&File.ReadAllBytes(target).SequenceEqual(original),"linked export leaf is rejected without modifying its destination");
        string recoveryRace=Path.Combine(root,"recovery-race.bin");File.WriteAllBytes(recoveryRace,original);
        var recoveryReview=MapStudioReviewedAssetFiles.ReviewTarget(recoveryRace);string racedRecovery=recoveryRace+".prime-asset-recovery-0";
        result=MapStudioReviewedAssetFiles.Publish(recoveryReview,written,()=>{},()=>File.WriteAllBytes(racedRecovery,raced));
        check(!result.Verified&&File.ReadAllBytes(racedRecovery).SequenceEqual(raced)&&File.ReadAllBytes(recoveryRace).SequenceEqual(original),"recovery leaf appearing after selection is never overwritten or deleted and target stays untouched");
        string gap=Path.Combine(root,"gap-race.bin");File.WriteAllBytes(gap,original);var gapReview=MapStudioReviewedAssetFiles.ReviewTarget(gap);verifies=0;
        result=MapStudioReviewedAssetFiles.Publish(gapReview,written,()=>{if(++verifies==4)File.WriteAllBytes(gap,raced);});
        check(!result.Verified&&result.RecoveryPath!=null&&File.ReadAllBytes(result.RecoveryPath).SequenceEqual(original)&&File.ReadAllBytes(gap).SequenceEqual(raced),"external target appearing between exclusive moves and displaced original are both preserved");
        string cancelGap=Path.Combine(root,"cancel-gap.bin");File.WriteAllBytes(cancelGap,original);var cancelReview=MapStudioReviewedAssetFiles.ReviewTarget(cancelGap);verifies=0;
        result=MapStudioReviewedAssetFiles.Publish(cancelReview,written,()=>{if(++verifies==4)throw new OperationCanceledException();});
        check(!result.Verified&&result.RecoveryPath!=null&&File.ReadAllBytes(result.RecoveryPath).SequenceEqual(original)&&!File.Exists(cancelGap)&&result.Notice.Contains("absent",StringComparison.OrdinalIgnoreCase),"cancellation after displacement retains truthful recovery receipt and discloses potentially absent target");
        check(!Directory.EnumerateFiles(root,".prime-asset-*.tmp").Any(),"all publication success/rejection/retirement paths retire owned staging files");
        // Existing MapAssets reader and real document writer use only private generated bytes.
        string projectRoot=Path.Combine(root,"project");Directory.CreateDirectory(Path.Combine(projectRoot,"textures"));
        byte[] png;using(var bitmap=new SkiaSharp.SKBitmap(4,4)){bitmap.Erase(SkiaSharp.SKColors.CadetBlue);using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);png=encoded.ToArray();}
        byte[] baked=MapTextureBake.BakeImage(png);string pngPath=Path.Combine(projectRoot,"textures/a.png");File.WriteAllBytes(pngPath,png);File.WriteAllBytes(Path.Combine(projectRoot,"textures/a.tex"),baked);
        var prepared=MapStudioTextureIoPreparation.PrepareTexture(pngPath,CancellationToken.None);
        check(prepared.Width==4&&prepared.Height==4&&prepared.PortableExtension==".png"&&prepared.PortableBytes.Span.SequenceEqual(png)&&prepared.NativeBytes.Span.SequenceEqual(baked),
            "actual existing image probe/native bake prepares exact portable and baked texture pair");
        bool canceled=false;try{MapStudioTextureIoPreparation.PrepareTexture(pngPath,new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}
        check(canceled,"canceled texture preparation cannot read/bake/publish a result");
        var definition=new MapDefinition{BaseDirectory=projectRoot};var document=new MapDocument(new MapProject(definition));
        MapStudioAssetReferenceCommands.ImportTexture(document,new(){Path="textures/a.tex",Kind="texture"},new(){Path="textures/a.png",Kind="texture"},"Private actual import",projectRoot);
        check(MapAssets.Read(document.Project.Definition,"textures/a.png").SequenceEqual(png)&&MapAssets.Read(document.Project.Definition,"textures/a.tex").SequenceEqual(baked),"real MapAssets reads generated portable/native import pair exactly");
        var export=MapStudioTextureIoPreparation.PrepareExport(document.Project.Definition,"textures/a.png",CancellationToken.None);
        check(export.Bytes.Span.SequenceEqual(png)&&export.Sha256==Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),"actual MapAssets streaming/hash export review captures exact original asset bytes");
        string outside=Path.Combine(root,"external-assets");Directory.CreateDirectory(outside);string outsidePng=Path.Combine(outside,"a.png");File.WriteAllBytes(outsidePng,png);
        string looseLeaf=Path.Combine(projectRoot,"textures/linked.png");File.CreateSymbolicLink(looseLeaf,outsidePng);
        var linkedDefinition=document.Project.ToDefinition();linkedDefinition.Assets.Add(new(){Path="textures/linked.png",Kind="texture"});
        refused=false;try{MapStudioTextureIoPreparation.PrepareExport(linkedDefinition,"textures/linked.png",CancellationToken.None);}catch(IOException){refused=true;}
        check(refused,"actual loose asset export rejects a symlink leaf even when external bytes have the identical SHA");
        string textures=Path.Combine(projectRoot,"textures"),retainedTextures=Path.Combine(projectRoot,"textures-retained");Directory.Move(textures,retainedTextures);Directory.CreateSymbolicLink(textures,outside);
        check(export.LooseSource!=null&&!MapStudioReviewedAssetFiles.Matches(export.LooseSource),"reviewed loose source identity rejects ancestor redirection to an equally hashed different asset");
        refused=false;try{MapStudioTextureIoPreparation.PrepareExport(document.Project.Definition,"textures/a.png",CancellationToken.None);}catch(IOException){refused=true;}
        check(refused,"actual loose asset export rejects linked ancestor outside canonical project root before package reader access");
        Directory.Delete(textures);Directory.Move(retainedTextures,textures);
        string save=Path.Combine(projectRoot,"map.json");document.Save(save);check(File.Exists(save)&&!document.IsDirty&&MapProjectSerializer.Load(save).Definition.Materials.Last().Texture=="textures/a.tex","real retained document writer saves imported declarations and material");
        document.History.Undo();check(document.Project.Definition.Assets.Count==0&&File.Exists(Path.Combine(projectRoot,"textures/a.png"))&&File.Exists(Path.Combine(projectRoot,"textures/a.tex")),"undo removes imported references but retains actual generated files for redo/history");
        string oversized=Path.Combine(root,"oversized.bin");using(var sparse=new FileStream(oversized,FileMode.CreateNew,FileAccess.Write,FileShare.None))sparse.SetLength(MapPackageReader.MaxEntryBytes+1L);
        refused=false;try{MapStudioReviewedAssetFiles.ReadSource(oversized);}catch(InvalidDataException){refused=true;}check(refused,"oversized source is refused before allocating asset bytes");
        refused=false;try{MapStudioReviewedAssetFiles.ReviewTarget(oversized);}catch(InvalidDataException){refused=true;}check(refused,"oversized existing target refuses overwrite review");
        Console.WriteLine("PRIVATE D FILE FIXTURE ROOT "+root);
    }
}
