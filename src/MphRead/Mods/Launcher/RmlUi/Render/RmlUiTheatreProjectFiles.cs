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
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor : ITheatreProjectFilesBackend
    {
        public Task<ITheatreProjectSave> PrepareProjectSaveAs(TheatreEditorContext context,string destination,CancellationToken token)
        {
            Validate(context,true);token.ThrowIfCancellationRequested();
            destination=Path.GetFullPath(destination);
            if(!destination.EndsWith(".ppclip",StringComparison.OrdinalIgnoreCase)||ReplayPathComparer.Same(destination,context.LogicalPath))
                throw new ArgumentException("Save As requires a new .ppclip project path.");
            string parent=Path.GetDirectoryName(destination)!;
            RmlUiReplayProjectSave.RequireProjectDirectory(parent);
            if(RmlUiReplayProjectSave.ProjectMembers(destination).Any(File.Exists))throw new IOException("Choose a new project filename. An existing project or sidecar will not be overwritten.");
            uint start=context.MarkIn??0,end=context.MarkOut??context.Duration;
            if(start>=end||end>context.Duration)throw new ArgumentException("Review a nonempty clip range before Save As.");
            if(context.Persisted is not Files files)throw new InvalidOperationException("Refresh this project before saving.");
            var annotations=ReplayAnnotations.ReadReviewed(files.Annotations,context.Duration);
            var reel=ReplayReels.ReadReviewed(files.Reel,context.LogicalPath,context.Duration);
            string cameraRevision=ReplayReviewedMetadata.Read(context.LogicalPath+".camera",8192).Revision;
            var camera=new ReplayCameraTrack();if(!camera.Load(context.LogicalPath))throw new InvalidDataException(camera.LastError);
            var cropped=new ReplayCameraTrack();
            if(camera.Sample(start,out var first))cropped.Put(first with{Frame=0});
            foreach(var key in camera.Keys.Where(k=>k.Frame>=start&&k.Frame<=end))cropped.Put(key with{Frame=key.Frame-start});
            if(camera.Sample(end,out var last))cropped.Put(last with{Frame=end-start});
            string source=context.LogicalPath;uint offset=0;
            if(source.EndsWith(".ppclip",StringComparison.OrdinalIgnoreCase))
            {
                if(!ReplayVirtualClips.TryLoad(source,out var descriptor)||descriptor==null)throw new InvalidDataException("The original clip descriptor changed.");
                source=Path.GetFullPath(descriptor.SourceReplay);offset=descriptor.StartFrame;
            }
            string originalSource=source;
            var sourceIdentity=ReplayCameraTrackIdentity.Create(context.LogicalPath);
            var preferences=RmlUiReplayProjectPreferences.Capture(context.LogicalPath,sourceIdentity.SourceContentHash,
                sourceIdentity.StartFrame,sourceIdentity.EndFrame,token);
            var leases=new List<FileStream>();
            try
            {
                foreach(string path in new[]{source,context.LogicalPath,context.PlaybackPath,context.LogicalPath+".camera",files.Annotations.Path,files.Reel.Path,preferences.Audio.Reviewed.Path,preferences.Presentation.Reviewed.Path}.Distinct(ReplayPathComparer.Comparer))
                {
                    if(!File.Exists(path))continue;
                    if(new FileInfo(path).LinkTarget!=null)throw new IOException("A project source or sidecar is a symbolic link.");
                    leases.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
                }
                Validate(context,true);
            }
            catch{foreach(var lease in leases)lease.Dispose();throw;}
            string target=destination;uint sourceStart=checked(offset+start),sourceEnd=checked(offset+end);
            return RmlUiReplayProjectSave.Prepare(target,parent,originalSource,sourceStart,sourceEnd,start,end,
                annotations,reel,cropped,cameraRevision,leases,token,preferences);
        }

        public string PublishProjectSaveAs(TheatreEditorContext context,ITheatreProjectSave prepared)
        {
            if(prepared is not RmlUiReplayProjectSave.PreparedProjectSave owned)throw new InvalidOperationException("The project save belongs to another owner.");
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context,true);
                if(ReplayReviewedMetadata.Read(context.LogicalPath+".camera",8192).Revision!=owned.CameraRevision)
                    throw new TheatreEditorStaleException("Camera edits changed while Save As was preparing. Review and save again.");
                owned.Commit();return owned.Destination;
            }
        }
    }
}
#endif
