using System;
using System.IO;
#if !ANDROID && !MPHREAD_SERVER
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
#endif
namespace MphRead.Mods.Replay;
internal static partial class ReplayVideoExporter
{
#if !ANDROID && !MPHREAD_SERVER
    private sealed class OfflineAudioWork
    {
        internal readonly ReplayVideoExportManifest Job;
        internal readonly CancellationTokenSource Cancellation=new();
        internal readonly Task<string> Task;
        internal bool Cancelled,Failed;
        private int _disposed;
        internal void DisposeCancellation(){if(Interlocked.Exchange(ref _disposed,1)==0)Cancellation.Dispose();}
        internal OfflineAudioWork(ReplayVideoExportManifest job)
        {
            Job=job;var token=Cancellation.Token;
            Task=ReplayStorageJobs.Run(()=>ReplayExportAudio.Write(job,token),token);
        }
        internal void Cancel(bool failure)
        {
            Cancelled=true;Failed|=failure;
            try{Cancellation.Cancel();}catch(ObjectDisposedException){}
            // Observe completion even when application teardown stops frame polling.
            _=Task.ContinueWith(completed=>
            {
                _=completed.Exception;
                if(completed.IsCompletedSuccessfully)
                    try{File.Delete(completed.Result);}catch(IOException){}catch(UnauthorizedAccessException){}
                DisposeCancellation();
            },TaskScheduler.Default);
        }
    }
    private static OfflineAudioWork? _offlineAudio;
    private static bool HasAudioWork=>_offlineAudio!=null;
    private static void BeginAudioWork(ReplayVideoExportManifest job)
    {
        if(job.Audio?.Settings.Enabled==true)_offlineAudio=new(job);
    }
    private static void CancelAudioWork(bool failure=false)=>_offlineAudio?.Cancel(failure);
    private static void FinishWithOfflineAudio(ReplayVideoExportManifest job)
    {
        _encodingJob=job;_state=ReplayExportState.Encoding;
        if(_offlineAudio!=null){Status="Finishing offline replay audio...";PollAudioWork();}
        else StartAcceptedEncoding(job,null);
    }
    private static void StartAcceptedEncoding(ReplayVideoExportManifest job,string? audio)
    {
        if(String.IsNullOrWhiteSpace(job.EncoderExecutable))
        {
            _state=ReplayExportState.Completed;_encodingJob=null;LastOutput=_directory;LastError=null;
            Status=audio==null?"PNG sequence export complete.":"PNG sequence and offline audio export complete.";
            ReplayExportQueue.NoteFinished(_status);return;
        }
        _state=ReplayExportState.Encoding;
        _encoder=audio==null?new ReplayEncoderJob(job.EncoderExecutable,"-progress pipe:1 -nostats "+job.FfmpegArguments,_directory??"")
            :new ReplayEncoderJob(job.EncoderExecutable,ReplayExportAudio.EncoderArguments(job,audio),_directory??"");
        Status="Encoding video...";
    }
    // Returns true while mixing owns finalization or after its terminal result.
    // This path never performs disk decode/copy/mix work on the renderer thread.
    private static bool PollAudioWork()
    {
        if(_offlineAudio is not{} work)return false;
        if(!work.Task.IsCompleted)return _job==null;
        if(work.Failed)
        {_offlineAudio=null;work.DisposeCancellation();return false;}
        if(work.Cancelled||work.Task.IsCanceled)
        {
            _offlineAudio=null;work.DisposeCancellation();_encodingJob=null;
            _state=ReplayExportState.Cancelled;LastOutput=null;LastError=null;Status="Video export cancelled.";
            ReplayExportQueue.NoteCancelled(work.Job);ReplayExportQueue.NoteFinished(_status);return true;
        }
        if(work.Task.IsFaulted)
        {
            string error=work.Task.Exception?.GetBaseException().Message??"Offline audio failed.";
            if(_job!=null){Fail(error);return true;}
            _offlineAudio=null;work.DisposeCancellation();_encodingJob=null;
            _state=ReplayExportState.Failed;LastOutput=null;LastError=error;Status="Offline audio failed. PNG sequence retained. "+error;
            ReplayExportQueue.NoteFailure(work.Job,error);ReplayExportQueue.NoteFinished(_status);return true;
        }
        if(_job!=null)return false; // Complete audio waits for all video frames.
        string output=work.Task.GetAwaiter().GetResult();
        _offlineAudio=null;work.DisposeCancellation();
        if(!ReferenceEquals(_encodingJob,work.Job))return false;
        StartAcceptedEncoding(work.Job,output);return false;
    }
#else
    private static bool HasAudioWork=>false;
    private static void BeginAudioWork(ReplayVideoExportManifest job){}
    private static void CancelAudioWork(bool failure=false){}
    private static bool PollAudioWork()=>false;
#endif
}
