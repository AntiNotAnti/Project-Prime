using System;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal sealed partial class ReplayWindowAcceptance
{
    private void PlanDetachedQueueControls(Func<bool> parentStable)
    {
        ITheatreExportBackend? backend=null;ReplayExportReceipt? canceled=null,retry=null,audible=null;
        ReplayDetachedExportTicket? canceledTicket=null;object?[] pendingTokens=Array.Empty<object?>();
        Do("review short silent export controls on the independent reopened parent",()=>
        {ReplayController.SetMarkIn(0);ReplayController.SetMarkOut(120);Activate("studio_refresh");});
        Wait("short export controls have an actual current editor review",()=>StudioWorkspaceForeground()&&Editor?.Snapshot().Live.Current==true);
        Do("enqueue one short worker and two pending actual exports",()=>
        {
            Require(parentStable(),"queue controls begin with exact reopened paused world");
            backend=Read<ITheatreExportBackend>(Page,"_exportBackend")!;
            var context=backend.Capture();var options=new TheatreExportOptions(0,0,60,false,false,"libx264",TheatreExportSource.Selection,TheatreExportCamera.FirstPerson,"");
            for(int i=0;i<3;i++)
            {
                backend.Enqueue(context,options);
                var queue=backend.Queue();foreach(var job in queue.Pending)_detachedOwnedJobs.Add(job.Id);
                if(queue.Active!=null)_detachedOwnedJobs.Add(queue.Active.Id);
            }
        });
        Wait("short actual worker renders with two pending accepted receipts",()=>
        {
            var queue=backend!.Queue();Require(queue.State!="Failed","short queue worker starts: "+queue.Status);
            if(queue.State!="Rendering"||queue.Active==null)return false;
            Require(queue.Pending.Length==2&&parentStable(),"two pending jobs stay distinct while active child preserves parent");
            canceled=(ReplayExportReceipt)queue.Active.Token;pendingTokens=queue.Pending.Select(j=>j.Token).ToArray();return true;
        },60000);
        Do("exact Clear Pending leaves current independent worker running",()=>
        {
            var reviewed=backend!.Queue();Require(backend.ClearExact(reviewed),"actual backend accepts exact pending clear");
            var after=backend.Queue();Require(after.Pending.IsEmpty&&ReferenceEquals(after.Active?.Token,canceled)
                &&pendingTokens.All(token=>!after.Pending.Any(j=>ReferenceEquals(j.Token,token)))&&parentStable(),
                "clear retires only the two pending receipts and retains active child and parent");
            canceledTicket=ActiveDetachedTicket();Require(canceledTicket!=null,"rendering active job has a prepared immutable ticket");
            Require(backend.CancelExact(after),"actual backend accepts exact active cancellation");
        });
        Wait("exact canceled render child exits and exposes retry receipt",()=>
        {
            var queue=backend!.Queue();Require(parentStable(),"cancel never mutates parent canonical world");
            return queue.Active==null&&queue.Pending.IsEmpty&&queue.State=="Cancelled"&&ReferenceEquals(queue.Retry?.Token,canceled);
        },15000);
        Do("actual retry uses retained private accepted input rather than new foreground review",()=>
        {
            var queue=backend!.Queue();Require(backend.RetryExact(queue),"exact canceled receipt authorizes a new attempt");
            var after=backend.Queue();var job=after.Pending.FirstOrDefault()??after.Active;
            Require(job!=null&&job.Id!=canceled!.Id,"retry creates a distinct attempt receipt");
            retry=(ReplayExportReceipt)job!.Token;_detachedOwnedJobs.Add(job.Id);
            Require(retry.Manifest.FramePattern!=canceled!.Manifest.FramePattern,"retry output directory is distinct from canceled attempt");
        });
        Wait("private retry is prepared with source bytes matching original accepted ticket",()=>
        {
            var queue=backend!.Queue();Require(queue.State!="Failed","retry worker starts: "+queue.Status);
            if(queue.Active?.Id!=retry!.Id)return false;var ticket=ActiveDetachedTicket();if(ticket==null)return false;
            Require(ticket.Manifest.Replay!=canceledTicket!.Manifest.Replay
                &&ticket.InputHashes[ticket.Manifest.Replay]==canceledTicket.InputHashes[canceledTicket.Manifest.Replay],
                "retry consumes a new private copy of the exact previously accepted replay bytes");return true;
        },60000);
        Wait("actual retried short export completes with no active or pending child",()=>
        {
            var queue=backend!.Queue();Require(queue.State!="Failed"&&parentStable(),"retry preserves parent and completes: "+queue.Status);
            if(queue.Active!=null||!queue.Pending.IsEmpty||queue.State!="Completed")return false;
            Require(Directory.GetFiles(Path.GetDirectoryName(retry!.Manifest.FramePattern)!,"frame_*.png").Length
                ==new ReplayExportSampler(0,120,60).Count,"retry renders exact short inclusive PNG count");
            _rml97StudioChecks.Add(new{Feature="actual-detached-clear-cancel-private-retry",Canceled=canceled!.Id,Retry=retry.Id,
                PendingCleared=2,ParentUnchanged=true,ChildRetired=true});return true;
        },60000);
        Do("open actual Audio inspector before reviewing fresh audible settings",()=>Activate("studio_tab_audio"));
        Do("review tiny actual audible export with saved music bus",()=>
        {
            var editor=Read<ITheatreEditorBackend>(Page,"_editorBackend")!;var audio=(ITheatreStudioAudioSettingsBackend)editor;
            var context=editor.Capture();var reviewed=audio.ReadAudioSettings(context);
            audio.SaveAudioSettings(context,reviewed with{Enabled=true,GameEvents=false,CombatFeedback=false,MusicGain=.5f,MusicFile=_rml97Wave});
            ReplayController.SetMarkIn(0);ReplayController.SetMarkOut(2);Activate("studio_refresh");
        });
        Wait("tiny audible export has current native review",()=>StudioWorkspaceForeground()&&Editor?.Snapshot().Live.Current==true);
        Do("enqueue actual three-frame offline audio and MP4 mux worker",()=>
        {
            var context=backend!.Capture();string executable=File.Exists("/opt/homebrew/bin/ffmpeg")?"/opt/homebrew/bin/ffmpeg":"ffmpeg";
            backend.Enqueue(context,new(0,0,60,false,false,"libx264",TheatreExportSource.Selection,TheatreExportCamera.FirstPerson,executable));
            var queue=backend.Queue();var job=queue.Pending.FirstOrDefault()??queue.Active;Require(job!=null,"tiny audible export admitted");
            audible=(ReplayExportReceipt)job!.Token;_detachedOwnedJobs.Add(job.Id);
        });
        Wait("tiny actual child completes offline PCM and audio/video mux",()=>
        {
            var queue=backend!.Queue();Require(queue.State!="Failed"&&parentStable(),"actual audible child preserves parent and succeeds: "+queue.Status);
            if(queue.Active!=null||!queue.Pending.IsEmpty||queue.State!="Completed")return false;
            string directory=Path.GetDirectoryName(audible!.Manifest.FramePattern)!;var pcm=StudioPcmAudio.ReadWave(Path.Combine(directory,"offline.wav"));
            long expected=StudioOfflineAudio.SampleCount(new ReplayExportSampler(0,2,60).Count,60);
            Require(pcm.SampleRate==48000&&pcm.Channels==2&&pcm.Frames==expected&&pcm.Samples.Any(sample=>Math.Abs(sample)>.001f),
                "child offline WAV has exact inclusive video duration and actual nonzero authored audio");
            byte[] movie=File.ReadAllBytes(audible.Manifest.SuggestedOutput);
            Require(Mp4Handler(movie,"vide")&&Mp4Handler(movie,"soun"),"actual encoded MP4 contains both video and audio tracks");
            Require(Hash(_rml97Path)==_detachedSourceHash,"queue controls and mux preserve original recording bytes");
            _rml97StudioChecks.Add(new{Feature="actual-detached-offline-audio-mux",Job=audible.Id,PcmFrames=pcm.Frames,VideoFrames=3,
                VideoTrack=true,AudioTrack=true,ParentUnchanged=true,ChildRetired=true});return true;
        },60000);
    }
    private static ReplayDetachedExportTicket? ActiveDetachedTicket()
    {
        var job=typeof(ReplayDetachedExportQueue).GetField("_active",StaticPrivate)!.GetValue(null);
        return job==null?null:job.GetType().GetField("Ticket",Private)!.GetValue(job) as ReplayDetachedExportTicket;
    }
    private static bool Mp4Handler(byte[] bytes,string handler)
    {
        bool Walk(int start,int end)
        {
            for(int at=start;at+8<=end;)
            {
                uint size=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at,4));
                if(size<8||size>int.MaxValue||at+(long)size>end)return false;
                string type=Encoding.ASCII.GetString(bytes,at+4,4);int next=at+(int)size;
                if(type=="hdlr"&&size>=24&&Encoding.ASCII.GetString(bytes,at+16,4)==handler)return true;
                if(type is "moov" or "trak" or "mdia")if(Walk(at+8,next))return true;
                at=next;
            }
            return false;
        }
        return Walk(0,bytes.Length);
    }
}
