#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Replay;
namespace MphRead.Mods.Launcher.RmlUi.Render;
/// <summary>Detached project staging. The caller retains foreground publication authority.</summary>
internal static class RmlUiReplayProjectSave
{
    internal static Task<ITheatreProjectSave> Prepare(string target,string parent,string originalSource,uint sourceStart,uint sourceEnd,
        uint start,uint end,ReplayAnnotationDocument annotations,ReplayReelDocument reel,ReplayCameraTrack cropped,
        string cameraRevision,List<FileStream> leases,CancellationToken token,ReplayProjectPreferencesSnapshot? preferences=null)
    {
            return Task.Run<ITheatreProjectSave>(()=>
            {
                string? staging=null;
                try
                {
                    token.ThrowIfCancellationRequested();
                    using var input=new FileStream(originalSource,FileMode.Open,FileAccess.Read,FileShare.Read);
                    byte[] sourceHash=SHA256.HashData(input);
                    staging=Path.Combine(parent,".prime-project-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
                    string staged=Path.Combine(staging,"selection.ppclip");
                    var descriptor=new {Version=1,SourceReplay=originalSource,StartFrame=sourceStart,EndFrame=sourceEnd,CreatedUtc=DateTime.UtcNow,Name=Path.GetFileNameWithoutExtension(target),SourceContentHash=Convert.ToHexString(sourceHash)};
                    WriteNew(staged,descriptor);
                    var rebased=new ReplayAnnotationDocument{Tags=annotations.Tags.ToList(),Collections=annotations.Collections.ToList(),
                        Bookmarks=annotations.Bookmarks.Where(x=>x.Frame>=start&&x.Frame<=end).Select(x=>x with{Frame=x.Frame-start}).ToList(),
                        Highlights=annotations.Highlights.Where(x=>x.StartFrame<end&&x.EndFrame>start).Select(x=>x with{StartFrame=Math.Max(start,x.StartFrame)-start,EndFrame=Math.Min(end,x.EndFrame)-start}).ToList()};
                    WriteNew(staged+ReplayAnnotations.Extension,rebased);
                    var rebasedReel=new ReplayReelDocument{SourceReplay=target,Segments=reel.Segments.Where(x=>x.StartFrame<end&&x.EndFrame>start)
                        .Select(x=>x with{StartFrame=Math.Max(start,x.StartFrame)-start,EndFrame=Math.Min(end,x.EndFrame)-start}).ToList()};
                    WriteNew(staged+ReplayReels.Extension,rebasedReel);
                    if(cropped.Keys.Count>0&&!cropped.SaveBound(staged,new ReplayCameraTrackIdentity(target,sourceHash,sourceStart,sourceEnd)))throw new IOException(cropped.LastError);
                    RmlUiReplayProjectPreferences.Stage(staged,preferences,sourceHash,sourceStart,sourceEnd,token);
                    token.ThrowIfCancellationRequested();
                    return new PreparedProjectSave(target,staging,staged,cameraRevision,leases,preferences);
                }
                catch
                {
                    foreach(var lease in leases)lease.Dispose();
                    if(staging!=null&&Directory.Exists(staging))Directory.Delete(staging,true);
                    throw;
                }
            });

    }
        private static void WriteNew(string path,object value)
        {using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);JsonSerializer.Serialize(stream,value,new JsonSerializerOptions{WriteIndented=true});stream.Flush(true);}
        internal static string[] ProjectMembers(string path)=>new[]{path,path+".camera",path+ReplayAnnotations.Extension,path+ReplayReels.Extension,path+ReplayStudioAudioSettingsStore.Extension,path+ReplayStudioPresentationPreferencesStore.Extension};
        internal static void RequireProjectDirectory(string path)
        {
            var directory=new DirectoryInfo(path);if(!directory.Exists)throw new DirectoryNotFoundException("Choose an existing project folder.");
            for(var parent=directory;parent!=null;parent=parent.Parent)if(parent.LinkTarget!=null)throw new IOException("Choose a project folder without symbolic-link ancestors.");
        }
        internal sealed class PreparedProjectSave : ITheatreProjectSave
        {
            public string Destination{get;}
            internal string CameraRevision{get;}
            private readonly string _directory,_staged;
            private readonly List<FileStream> _leases;
            private bool _disposed,_committed;
            private readonly ReplayProjectPreferencesSnapshot? _preferences;
            internal PreparedProjectSave(string destination,string directory,string staged,string camera,List<FileStream> leases,ReplayProjectPreferencesSnapshot? preferences)
                =>(Destination,_directory,_staged,CameraRevision,_leases,_preferences)=(destination,directory,staged,camera,leases,preferences);
            internal void Commit()
            {
                ObjectDisposedException.ThrowIf(_disposed,this);if(_committed)throw new InvalidOperationException("Project already published.");
                RmlUiReplayProjectPreferences.Validate(_preferences);
                RequireProjectDirectory(Path.GetDirectoryName(Destination)!);
                if(ProjectMembers(Destination).Any(File.Exists))throw new IOException("The chosen destination now exists. Choose a new project filename.");
                var published=new List<string>();
                try
                {
                    // Sidecars publish first; the descriptor is the final commit marker.
                    foreach(string suffix in new[]{".camera",ReplayAnnotations.Extension,ReplayReels.Extension,ReplayStudioAudioSettingsStore.Extension,ReplayStudioPresentationPreferencesStore.Extension,""})
                        if(File.Exists(_staged+suffix)){File.Move(_staged+suffix,Destination+suffix,false);published.Add(Destination+suffix);}
                    _committed=true;
                }
                catch{foreach(string path in published)File.Delete(path);throw;}
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;foreach(var lease in _leases)lease.Dispose();
                if(Directory.Exists(_directory))Directory.Delete(_directory,true);
            }
        }

}
#endif
