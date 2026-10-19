#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
namespace MphRead.Mods.Launcher.RmlUi.Render;
/// <summary>Import an explicit external project into the retained library before native adoption.
/// Never adopts arbitrary external descriptor paths or materialized cache identities.</summary>
internal static class RmlUiReplayProjectLibrary
{
    internal static Task<string> ImportAsync(string selected,CancellationToken token)=>ReplayStorageJobs.Run(()=>Import(selected,token),token);
    private static string Import(string selected,CancellationToken token)
    {
        selected=Path.GetFullPath(selected);token.ThrowIfCancellationRequested();
        if(selected.EndsWith(DemoFile.Extension,StringComparison.OrdinalIgnoreCase))return RmlUiLiveMenuServices.ImportStudioRecording(selected,token);
        if(!selected.EndsWith(".ppclip",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a .ppclip project.");
        RequireRegular(selected,64*1024);
        using var descriptorLease=new FileStream(selected,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var raw=JsonDocument.Parse(descriptorLease);descriptorLease.Position=0;
        string? boundHash=raw.RootElement.TryGetProperty("SourceContentHash",out var hashValue)&&hashValue.ValueKind==JsonValueKind.String?hashValue.GetString():null;
        var descriptor=JsonSerializer.Deserialize<ReplayVirtualClipDocument>(descriptorLease)??throw new InvalidDataException("The clip project is invalid.");
        if(descriptor.Version!=1||descriptor.StartFrame>=descriptor.EndFrame||String.IsNullOrWhiteSpace(descriptor.SourceReplay))throw new InvalidDataException("The clip project range or source is invalid.");
        string source=Path.GetFullPath(descriptor.SourceReplay);RequireRegular(source,long.MaxValue);
        if(!source.EndsWith(DemoFile.Extension,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A clip project must reference a durable .ppdemo recording.");
        using var sourceLease=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        byte[] sourceHash=SHA256.HashData(sourceLease);sourceLease.Position=0;
        if(boundHash!=null&&!String.Equals(boundHash,Convert.ToHexString(sourceHash),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The clip project's bound recording bytes changed.");
        token.ThrowIfCancellationRequested();
        ReplayOpenResult validation=ReplayArchive.Validate(source);
        token.ThrowIfCancellationRequested();
        if(validation!=ReplayOpenResult.Success)throw new InvalidDataException("The project source recording failed integrity validation.");
        using(var reader=DemoReader.Open(source,out var result))
            if(reader==null||descriptor.EndFrame>reader.DurationFrames)throw new InvalidDataException("The clip project range exceeds its recording: "+result);
        uint duration=descriptor.EndFrame-descriptor.StartFrame;
        var annotationFile=ReplayReviewedMetadata.Read(selected+ReplayAnnotations.Extension);
        var reelFile=ReplayReviewedMetadata.Read(selected+ReplayReels.Extension);
        var cameraFile=ReplayReviewedMetadata.Read(selected+".camera",8192);
        var annotations=ReplayAnnotations.ReadReviewed(annotationFile,duration);
        var reel=ReplayReels.ReadReviewed(reelFile,selected,duration);
        var camera=new ReplayCameraTrack();if(!camera.Load(selected))throw new InvalidDataException(camera.LastError);
        var preferences=RmlUiReplayProjectPreferences.Capture(selected,sourceHash,descriptor.StartFrame,descriptor.EndFrame,token);
        if(ReplayPathComparer.Same(Path.GetDirectoryName(selected)!,DemoLibrary.Directory)
            &&ReplayPathComparer.Same(Path.GetDirectoryName(source)!,DemoLibrary.Directory))return selected;
        Directory.CreateDirectory(DemoLibrary.Directory);
        if(new DirectoryInfo(DemoLibrary.Directory).LinkTarget!=null)throw new IOException("The replay library is a symbolic link.");
        string id=Guid.NewGuid().ToString("N");
        string target=Path.Combine(DemoLibrary.Directory,"project_"+id+".ppclip"),recording=Path.Combine(DemoLibrary.Directory,"project_source_"+id+DemoFile.Extension);
        string stage=Path.Combine(DemoLibrary.Directory,".project-import-"+id);Directory.CreateDirectory(stage);
        string staged=Path.Combine(stage,"project.ppclip"),stagedRecording=Path.Combine(stage,"source.ppdemo");
        var published=new List<string>();
        try
        {
            RmlUiLiveMenuServices.CopyReplay(source,stagedRecording,token,validate:true);
            using(var copied=File.OpenRead(stagedRecording))if(!SHA256.HashData(copied).AsSpan().SequenceEqual(sourceHash))throw new IOException("The project source changed during import.");
            Write(staged,descriptor with{SourceReplay=recording});
            Write(staged+ReplayAnnotations.Extension,annotations);reel.SourceReplay=target;Write(staged+ReplayReels.Extension,reel);
            if(camera.Keys.Count>0&&!camera.SaveBound(staged,new ReplayCameraTrackIdentity(target,sourceHash,descriptor.StartFrame,descriptor.EndFrame)))throw new IOException(camera.LastError);
            RmlUiReplayProjectPreferences.Stage(staged,preferences,sourceHash,descriptor.StartFrame,descriptor.EndFrame,token);
            token.ThrowIfCancellationRequested();
            ReplayReviewedMetadata.Validate(annotationFile);ReplayReviewedMetadata.Validate(reelFile);
            if(ReplayReviewedMetadata.Read(selected+".camera",8192).Revision!=cameraFile.Revision)throw new IOException("Project camera changed during import.");
            sourceLease.Position=0;if(!SHA256.HashData(sourceLease).AsSpan().SequenceEqual(sourceHash))throw new IOException("Project source changed during import.");
            RmlUiReplayProjectPreferences.Validate(preferences);
            File.Move(stagedRecording,recording,false);published.Add(recording);
            foreach(string suffix in new[]{".camera",ReplayAnnotations.Extension,ReplayReels.Extension,ReplayStudioAudioSettingsStore.Extension,ReplayStudioPresentationPreferencesStore.Extension,""})
                if(File.Exists(staged+suffix)){File.Move(staged+suffix,target+suffix,false);published.Add(target+suffix);}
            return target;
        }
        catch{foreach(string path in published)File.Delete(path);throw;}
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private static void Write(string path,object value)
    {using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);JsonSerializer.Serialize(output,value,new JsonSerializerOptions{WriteIndented=true});output.Flush(true);}
    private static void RequireRegular(string path,long limit)
    {
        var file=new FileInfo(path);if(!file.Exists||file.LinkTarget!=null||file.Length>limit)throw new IOException("Choose a regular project/source file within its size limit.");
        for(var parent=file.Directory;parent!=null;parent=parent.Parent)if(parent.LinkTarget!=null)throw new IOException("Project sources cannot use symbolic-link ancestors.");
    }
}
#endif
