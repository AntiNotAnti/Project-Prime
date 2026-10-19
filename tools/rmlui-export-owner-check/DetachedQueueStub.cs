using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Replay;

// Deterministic admission/receipt seam only. No copies, worker, native window or encoder runs here.
internal static class ReplayDetachedExportQueue
{
    internal sealed record Admission(ReplayExportReceipt Receipt,ReplayExportPresentation Presentation,
        string PlaybackHash,string Room,int Subject,string LogicalPath);
    internal static Admission? Active,Retry;
    internal static bool Capacity=true;
    private static long _revision;
    internal static bool CheckCapacity()=>Capacity;
    internal static void Enqueue(ReplayVideoExportManifest manifest,ReplayExportPresentation presentation,string hash,string room,int subject,string logicalPath)
    {
        if(!Capacity)throw new InvalidOperationException("SYNTHETIC DETACHED CAPACITY");
        if(Active!=null)throw new InvalidOperationException("SYNTHETIC DETACHED ACTIVE");
        Active=new(new(Guid.NewGuid(),manifest),presentation,hash,room,subject,logicalPath);_revision++;
    }
    internal static TheatreExportQueueState Snapshot()=>new(_revision,ImmutableArray<TheatreExportJob>.Empty,
        View(Active),View(Retry),Active==null?"Idle":"Queued","SYNTHETIC ADMISSION ONLY",0,ImmutableArray<string>.Empty,"");
    private static TheatreExportJob? View(Admission? input)=>input==null?null:new(input.Receipt.Id,input.Receipt,
        Path.GetFileName(input.LogicalPath),input.Receipt.Manifest.SuggestedOutput,"SYNTHETIC IMMUTABLE DETACHED INPUTS");
    internal static bool CancelExact(TheatreExportQueueState reviewed)
    {
        if(Active==null||reviewed.Revision!=_revision||!ReferenceEquals(reviewed.Active?.Token,Active.Receipt))return false;
        Retry=Active;Active=null;_revision++;return true;
    }
    internal static bool RetryExact(TheatreExportQueueState reviewed)
    {
        if(Active!=null||Retry==null||reviewed.Revision!=_revision||!ReferenceEquals(reviewed.Retry?.Token,Retry.Receipt))return false;
        Active=Retry with{Receipt=new(Guid.NewGuid(),Retry.Receipt.Manifest)};Retry=null;_revision++;return true;
    }
    internal static bool ClearExact(TheatreExportQueueState reviewed)
        =>reviewed.Revision==_revision&&reviewed.Pending.IsEmpty;
    internal static void Reset(){Active=Retry=null;Capacity=true;_revision++;}
}

// The legacy exporter remains real; only its absent offline audio engine is stubbed.
internal static partial class ReplayVideoExporter
{
    private static bool HasAudioWork=>false;
    private static void BeginAudioWork(ReplayVideoExportManifest job)
    {if(job.Audio?.Settings.Enabled==true)throw new InvalidOperationException("This CPU fixture does not render or mix audio.");}
    private static void CancelAudioWork(bool failure=false) { }
    private static bool PollAudioWork()=>false;
    private static void FinishWithOfflineAudio(ReplayVideoExportManifest job)
    {
        _state=ReplayExportState.Encoding;_encodingJob=job;
        _encoder=new ReplayEncoderJob(job.EncoderExecutable,"-progress pipe:1 -nostats "+job.FfmpegArguments,_directory??"");
    }
}
