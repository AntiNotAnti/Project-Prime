using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal sealed partial class ReplayWindowAcceptance
{
    private ReplayExportReceipt? _detachedReceipt;
    private readonly System.Collections.Generic.HashSet<Guid> _detachedOwnedJobs=new();
    private object? _detachedOriginalOwner;
    private string _detachedSourceHash="";
    private bool _detachedClosedWhileRendering;
    private void CleanupDetachedQualification()
    {
        if(_detachedOwnedJobs.Count==0)return;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while(elapsed.Elapsed.TotalSeconds<10)
            {
                var queue=ReplayDetachedExportQueue.Snapshot();
                bool ownedActive=queue.Active!=null&&_detachedOwnedJobs.Contains(queue.Active.Id);
                bool ownedPending=queue.Pending.Any(job=>_detachedOwnedJobs.Contains(job.Id));
                if(!ownedActive&&!ownedPending)return;
                if(ownedActive)ReplayDetachedExportQueue.CancelExact(queue);
                queue=ReplayDetachedExportQueue.Snapshot();
                if(queue.Pending.Length>0&&queue.Pending.All(job=>_detachedOwnedJobs.Contains(job.Id)))
                    ReplayDetachedExportQueue.ClearExact(queue);
                System.Threading.Thread.Sleep(50);
            }
            Fail(new InvalidOperationException("Isolated detached export cleanup did not retire owned child/pending jobs within ten seconds."));
        }
        catch(Exception error){Fail(new InvalidOperationException("Isolated detached export cleanup failed.",error));}
    }
    private void PlanDetachedExportAdmission()
    {
        Do("detached export accepts actual saved source and silent PNG settings",()=>
        {
            Rml97SameWorld();_detachedOriginalOwner=DemoPlayback.PlaybackOwner;_detachedSourceHash=Hash(_rml97Path);
            var editorBackend=Read<ITheatreEditorBackend>(Page,"_editorBackend")!;
            var audio=(ITheatreStudioAudioSettingsBackend)editorBackend;
            var review=editorBackend.Capture();audio.SaveAudioSettings(review,audio.ReadAudioSettings(review) with{Enabled=false});
            ReplayController.SetMarkIn(0);ReplayController.SetMarkOut(240);Activate("studio_refresh");
        });
        Wait("detached export parent has current reviewed source and picture",()=>
            StudioWorkspaceForeground()&&!ReplayController.IsSeeking&&ReplayController.IsPaused&&Editor?.Snapshot().Live.Current==true);
        Do("enqueue actual backend selection into independent render worker",()=>
        {
            var backend=Read<ITheatreExportBackend>(Page,"_exportBackend")!;
            var context=backend.Capture();backend.Enqueue(context,new(0,0,144,false,false,"libx264",TheatreExportSource.Selection,TheatreExportCamera.FirstPerson,EncoderExecutable:""));
            var queue=ReplayDetachedExportQueue.Snapshot();
            _detachedReceipt=(queue.Active??queue.Pending.Single()).Token as ReplayExportReceipt;
            Require(_detachedReceipt!=null,"actual detached export admission returns exact accepted receipt");
            _detachedOwnedJobs.Add(_detachedReceipt!.Id);
        });
        Wait("independent export is actually rendering before closing foreground recording",()=>
        {
            var queue=ReplayDetachedExportQueue.Snapshot();
            Require(queue.State!="Failed"&&queue.State!="Cancelled","actual detached worker starts successfully: "+queue.Status);
            if(queue.Active?.Id!=_detachedReceipt!.Id||queue.State!="Rendering")return false;
            Require(!ReplayVideoExporter.Rendering&&ReferenceEquals(DemoPlayback.PlaybackOwner,_detachedOriginalOwner)
                &&ReplayController.CurrentFrame==0&&ReplayController.IsPaused&&Rml97WorldHash()==_rml97Baseline,
                "private child rendering does not acquire or advance parent replay/camera owner");
            return true;
        },60000);
    }
    private void RequireDetachedExportActiveAtClose()
    {
        if(!_studioRole||_detachedReceipt==null)return;
        var queue=ReplayDetachedExportQueue.Snapshot();
        Require(queue.Active?.Id==_detachedReceipt.Id&&queue.State=="Rendering",
            "survival qualification closes the recording while its exact independent child is still rendering");
        _detachedClosedWhileRendering=true;
    }
    private void PlanDetachedExportCompletion()
    {
        object? reopenedOwner=null;string reopenedWorld="";
        Do("reopen actual Studio recording while detached export continues",()=>
        {
            Require(_detachedClosedWhileRendering&&!DemoPlayback.IsActive&&Page==null,"detached job survived actual Library teardown admission");
            Activate("theatre_studio");
        });
        Wait("new Studio player can prepare while original export worker remains independent",()=>
        {
            if(!StudioWorkspaceForeground())return false;
            var queue=ReplayDetachedExportQueue.Snapshot();
            Require(queue.Active?.Id==_detachedReceipt!.Id&&queue.State=="Rendering","same detached worker is still rendering after a new replay opens");
            reopenedOwner=DemoPlayback.PlaybackOwner;
            Require(!ReferenceEquals(reopenedOwner,_detachedOriginalOwner)&&!ReplayVideoExporter.Rendering,
                "reopened Studio owns a fresh foreground player without inheriting child rendering");
            return true;
        },45000);
        Do("pause fresh foreground through its presented native transport",()=>
        {if(!ReplayController.IsPaused)Activate("replay_pause");});
        Wait("fresh foreground pause has a settled successful world picture",()=>
            StudioWorkspaceForeground()&&!ReplayController.IsSeeking&&ReplayController.IsPaused,15000);
        Do("explicitly review fresh paused foreground before native Go",()=>Activate("studio_refresh"));
        Wait("fresh paused native editor review belongs to current transport",()=>
        {
            var snapshot=Editor?.Snapshot();
            return StudioWorkspaceForeground()&&snapshot?.Live.Current==true&&snapshot.Context.SettledPaused
                &&ReferenceEquals(snapshot.Context.Transport,DemoPlayback.Session.Transport)
                &&snapshot.Context.Frame==ReplayController.CurrentFrame;
        },15000);
        WaitFrames(2,"fresh paused native transport field is presented before Go",()=>
            Require(ReferenceEquals(reopenedOwner,DemoPlayback.PlaybackOwner)&&ReplayController.IsPaused&&!ReplayController.IsSeeking,
                "fresh paused player remains exact while native fields settle"));
        Do("seek independent fresh foreground with actual native frame field and Go",()=>
        {
            Require(StudioWorkspaceForeground()&&Editor?.Snapshot().Live.Current==true,"native Go owns current fresh paused page");
            Focus("studio_frame");Host!.SetField(Page!.Document,"studio_frame","30");Activate("studio_seek");
        });
        Wait("new actual foreground native seek settles independently of child rendering",()=>
            StudioWorkspaceForeground()&&!ReplayController.IsSeeking&&ReplayController.IsPaused&&ReplayController.CurrentFrame==30,45000);
        Do("retain new paused foreground canonical state during export completion",()=>reopenedWorld=Rml97WorldHash());
        Wait("detached PNG export completes after original Library close and new player preparation",()=>
        {
            var queue=ReplayDetachedExportQueue.Snapshot();
            Require(queue.State!="Failed"&&queue.State!="Cancelled","actual detached export completes successfully: "+queue.Status);
            Require(ReferenceEquals(reopenedOwner,DemoPlayback.PlaybackOwner)&&ReplayController.CurrentFrame==30&&ReplayController.IsPaused
                &&!ReplayController.IsSeeking&&Rml97WorldHash()==reopenedWorld&&!ReplayVideoExporter.Rendering,
                "child completion preserves exact reopened paused canonical world");
            if(queue.Active!=null||queue.Pending.Length!=0||queue.State!="Completed")return false;
            var manifest=_detachedReceipt!.Manifest;string directory=Path.GetDirectoryName(manifest.FramePattern)!;
            var frames=Directory.GetFiles(directory,"frame_*.png").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            long expected=new ReplayExportSampler(manifest.StartFrame,manifest.EndFrame,manifest.Fps).Count;
            Require(frames.Length==expected,"completed detached PNG count matches exact inclusive sampler");
            foreach(string frame in frames)
            {
                byte[] header=new byte[24];using var input=File.OpenRead(frame);input.ReadExactly(header);
                Require(header[0]==137&&header[1]==80&&header[2]==78&&header[3]==71
                    &&BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16,4))==manifest.Width
                    &&BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20,4))==manifest.Height,"detached frames have actual requested PNG dimensions");
            }
            int distinct=frames.Select(Hash).Distinct(StringComparer.Ordinal).Count();
            Require(distinct>1&&Hash(_rml97Path)==_detachedSourceHash,"actual detached frame pictures vary while original recording bytes remain exact");
            _rml97StudioChecks.Add(new{Feature="actual-detached-export-survives-library-and-reopen",Job=_detachedReceipt.Id,
                ClosedWhileRendering=_detachedClosedWhileRendering,Frames=frames.Length,manifest.Width,manifest.Height,DistinctPngHashes=distinct,
                ChildRetired=queue.Active==null,ParentUnchanged=true,Output=directory});return true;
        },120000);
        PlanDetachedQueueControls(() => ReferenceEquals(reopenedOwner,DemoPlayback.PlaybackOwner)
            &&ReplayController.CurrentFrame==30&&ReplayController.IsPaused&&!ReplayController.IsSeeking
            &&Rml97WorldHash()==reopenedWorld&&!ReplayVideoExporter.Rendering);
        Do("close independent qualification foreground through actual Library",()=>Activate("replay_back"));
        Wait("detached qualification returns to native Library without an owned render child",()=>
            !DemoPlayback.IsActive&&!_window!.HasScene&&Page==null&&ReplayDetachedExportQueue.Snapshot().Active==null,15000);
    }
}
