#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

/// <summary>Process-level export ownership. Accepted jobs retain files and detached
/// values, never a page, playback transport, scene or editor callback.</summary>
internal static class ReplayDetachedExportQueue
{
    private sealed class Admission:IDisposable
    {
        internal readonly FileStream Replay;
        internal readonly FileStream? OwnerPresentation;
        internal readonly string? OwnerPresentationHash;
        internal readonly Dictionary<string,FileStream> Waves=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> WaveHashes=new(StringComparer.Ordinal);
        internal readonly GameFiles.ConfiguredFilesUse Files;
        internal readonly ReplayExportAudioInput? Audio;
        internal readonly string[] Paths;
        internal readonly string Mph,Fh,MapDirectory,UserMapDirectory;
        private int _streamsReleased;
        internal Admission(ReplayVideoExportManifest manifest,ReplayDetachedExportTicket? frozen)
        {
            Files=GameFiles.TryUseConfiguredFiles()??throw new IOException("Game files are being changed. Retry the export after setup finishes.");
            FileStream? replay=null;
            try
            {
                string source=frozen?.Manifest.Replay??manifest.Replay;
                if(new FileInfo(source).LinkTarget!=null)throw new IOException("Choose an ordinary replay file.");
                replay=new(source,FileMode.Open,FileAccess.Read,FileShare.Read);
                string ownerPath = source + ReplayOwnerTrack.Extension;
                bool expectedOwner = frozen?.InputHashes.ContainsKey(ownerPath) ?? File.Exists(ownerPath);
                if (expectedOwner)
                {
                    var ownerInfo = new FileInfo(ownerPath);
                    bool usable = ownerInfo.Exists && ownerInfo.LinkTarget == null && ownerInfo.Length <= 18 * 1024 * 1024;
                    if (!usable && frozen != null)
                        throw new IOException("The accepted owner presentation track is unavailable.");
                    if (usable)
                    {
                        OwnerPresentation = new(ownerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        // Admission only retains a handle. Hash/copy/validation
                        // run on the existing preparation worker, never the UI.
                        OwnerPresentationHash = frozen == null ? null : frozen.InputHashes[ownerPath];
                    }
                }
                Audio=frozen?.Manifest.Audio??manifest.Audio;
                foreach(var wave in Audio?.Sources??ImmutableArray<ReplayExportAudioReference>.Empty)
                {
                    var info=new FileInfo(wave.Path);
                    if(!info.Exists||info.LinkTarget!=null||info.Length!=wave.Length||info.LastWriteTimeUtc.Ticks!=wave.LastWriteTicks)
                        throw new IOException("An audio source changed. Review it before exporting.");
                    Waves.Add(wave.Path,new(wave.Path,FileMode.Open,FileAccess.Read,FileShare.Read));
                    if(frozen!=null)WaveHashes.Add(wave.Path,frozen.InputHashes.TryGetValue(wave.Path,out var hash)
                        ?hash:throw new IOException("The retained audio input has no accepted content identity."));
                }
                if(frozen==null)Paths=new[]{"0.35.1"}.Concat(global::MphRead.Paths.AllPaths.Select(row=>row.Key+"="+row.Value)).ToArray();
                else
                {
                    string path=Path.Combine(frozen.WorkDirectory,"paths.txt");var info=new FileInfo(path);
                    if(!info.Exists||info.LinkTarget!=null||info.Length>1024*1024
                        ||!frozen.InputHashes.TryGetValue(path,out var hash)||ReplayDetachedExportFiles.Hash(path)!=hash)
                        throw new IOException("The accepted runtime paths changed before retry.");
                    Paths=File.ReadAllLines(path);
                    if(ReplayDetachedExportFiles.Hash(path)!=hash)throw new IOException("The accepted runtime paths changed during retry.");
                }
                Mph=frozen?.MphKey??global::MphRead.Paths.MphKey;Fh=frozen?.FhKey??global::MphRead.Paths.FhKey;
                MapDirectory=frozen?.MapDirectory??Path.GetFullPath(CustomRooms.MapDirectory);
                UserMapDirectory=frozen?.UserMapDirectory??Path.GetFullPath(CustomRooms.UserMapDirectory);
                Replay=replay;replay=null;
            }
            catch{replay?.Dispose();OwnerPresentation?.Dispose();foreach(var wave in Waves.Values)wave.Dispose();Files.Dispose();throw;}
        }
        internal void ReleaseStreams()
        {
            if(Interlocked.Exchange(ref _streamsReleased,1)!=0)return;
            Replay.Dispose();OwnerPresentation?.Dispose();foreach(var wave in Waves.Values)wave.Dispose();
        }
        public void Dispose(){ReleaseStreams();Files.Dispose();}
    }
    private sealed class Job
    {
        internal readonly ReplayExportReceipt Receipt;
        internal readonly ReplayExportPresentation Presentation;
        internal readonly string ExpectedHash,Room,WorkDirectory,LogicalPath;
        internal readonly int Subject;
        internal readonly Admission Inputs;
        internal readonly CancellationTokenSource Cancellation=new();
        internal ReplayDetachedExportTicket? Ticket;
        internal volatile bool CancelRequested;
        internal Job(ReplayVideoExportManifest manifest,ReplayExportPresentation presentation,string hash,string room,int subject,string logicalPath,ReplayDetachedExportTicket? frozen=null)
        {
            Inputs=new(manifest,frozen);
            try
            {
                Receipt=new(Guid.NewGuid(),manifest);Presentation=presentation;ExpectedHash=hash;Room=room;Subject=subject;LogicalPath=logicalPath;
                WorkDirectory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest.FramePattern))!,".studio-worker-"+Receipt.Id.ToString("N"));
                Directory.CreateDirectory(WorkDirectory);
            }
            catch{Inputs.Dispose();throw;}
        }
    }
    private static readonly object Gate=new();
    private static readonly Queue<Job> Pending=new();
    private static readonly List<string> Failures=new();
    private static Job? _active,_retry;
    private static bool _pumping;
    private static long _revision;
    private static string _state="Idle",_status="Export queue idle.",_output="";
    private static float _progress;

    internal static bool CheckCapacity(){lock(Gate)return Pending.Count<ReplayExportQueue.MaximumPending;}
    internal static void Enqueue(ReplayVideoExportManifest manifest,ReplayExportPresentation presentation,string playbackHash,string room,int subject,string logicalPath)
    {
        lock(Gate)
        {
            if(!CheckCapacity())throw new InvalidOperationException("The export queue is full. Wait or clear pending exports.");
            var job=new Job(manifest,presentation,playbackHash,room,subject,logicalPath);
            Pending.Enqueue(job);_revision++;_status=$"Queued {Pending.Count} independent export(s).";
            StartPump();
        }
    }
    private static void StartPump()
    {if(!_pumping){_pumping=true;_=Task.Run(Drain);}}
    private static async Task Drain()
    {
        try
        {
            while(true)
            {
                Job job;
                lock(Gate)
                {
                    if(Pending.Count==0)return;
                    _active=job=Pending.Dequeue();_revision++;_state="Queued";_progress=0;_status="Preparing private export inputs...";
                }
                try
                {
                    job.Ticket=Prepare(job);job.Inputs.ReleaseStreams();job.Cancellation.Token.ThrowIfCancellationRequested();
                    var result=await RunWorker(job).ConfigureAwait(false);
                    lock(Gate)
                    {
                        _state=result.State.ToString();_status=result.Message;_progress=result.Progress;
                        if(result.State==ReplayExportState.Completed){_output=result.Output??"";_retry=null;}
                        else{_retry=job;if(result.State==ReplayExportState.Failed)NoteFailure(result.Error??result.Message);}
                    }
                }
                catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
                {
                    lock(Gate)
                    {
                        _retry=job;_state=job.CancelRequested||error is OperationCanceledException?"Cancelled":"Failed";
                        _status=_state=="Cancelled"?"Independent export cancelled.":"Independent export failed: "+error.Message;
                        if(_state=="Failed")NoteFailure(error.Message);
                    }
                }
                finally
                {
                    lock(Gate){_active=null;_revision++;}
                    job.Inputs.Dispose();job.Cancellation.Dispose();
                }
            }
        }
        finally{lock(Gate){_pumping=false;if(Pending.Count>0)StartPump();}}
    }
    private static void NoteFailure(string message)
    {Failures.Add(message.Length>500?message[..500]:message);if(Failures.Count>5)Failures.RemoveAt(0);}
    private static string CopyInput(FileStream input,string path,CancellationToken token,string? expected=null)
    {
        input.Position=0;
        using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            byte[] buffer=new byte[65536];int read;
            while((read=input.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,read);}
            output.Flush(true);
        }
        string hash=ReplayDetachedExportFiles.Hash(path);
        if(ReplayDetachedExportFiles.Hash(input,token)!=hash||expected!=null&&!StringComparer.OrdinalIgnoreCase.Equals(expected,hash))
            throw new IOException("An accepted export input changed while its private copy was being prepared.");
        return hash;
    }
    private static ReplayDetachedExportTicket Prepare(Job job)
    {
        var token=job.Cancellation.Token;var inputs=job.Inputs;var hashes=new Dictionary<string,string>(StringComparer.Ordinal);
        string replay=Path.Combine(job.WorkDirectory,"replay.ppdemo");hashes.Add(replay,CopyInput(inputs.Replay,replay,token,job.ExpectedHash));
        if (inputs.OwnerPresentation != null)
        {
            string owner = replay + ReplayOwnerTrack.Extension;
            hashes.Add(owner, CopyInput(inputs.OwnerPresentation, owner, token, inputs.OwnerPresentationHash));
        }
        var replacements=new Dictionary<string,string>(StringComparer.Ordinal);int index=0;
        foreach(var wave in inputs.Waves)
        {
            token.ThrowIfCancellationRequested();string path=Path.Combine(job.WorkDirectory,$"audio-{index++:D2}.wav");
            hashes.Add(path,CopyInput(wave.Value,path,token,inputs.WaveHashes.GetValueOrDefault(wave.Key)));replacements.Add(wave.Key,path);
        }
        ReplayExportAudioInput? audio=inputs.Audio;
        if(audio?.Settings.Enabled==true)
        {
            var settings=audio.Settings with
            {
                MusicFile=audio.Settings.MusicFile.Length==0?"":replacements[audio.Settings.MusicFile],
                Cues=Array.AsReadOnly((audio.Settings.Cues??Array.Empty<TheatreAudioCue>()).Select(c=>c with{WaveFile=replacements[c.WaveFile]}).ToArray())
            };
            audio=audio with{Settings=settings,Sources=replacements.Values.Select(path=>{var info=new FileInfo(path);return new ReplayExportAudioReference(path,info.Length,info.LastWriteTimeUtc.Ticks);}).ToImmutableArray()};
        }
        else if(audio!=null)audio=audio with{Settings=audio.Settings with{MusicFile="",Cues=Array.Empty<TheatreAudioCue>()},Sources=ImmutableArray<ReplayExportAudioReference>.Empty};
        string paths=Path.Combine(job.WorkDirectory,"paths.txt");File.WriteAllLines(paths,inputs.Paths);
        hashes.Add(paths,ReplayDetachedExportFiles.Hash(paths));
        ulong geometry=ReplayMapIdentity.Compute(job.Room);token.ThrowIfCancellationRequested();
        if(geometry==0)throw new IOException("The exact map geometry is unavailable for export.");
        var ticket=new ReplayDetachedExportTicket(1,job.Receipt.Id,job.WorkDirectory,job.Receipt.Manifest with{Replay=replay,Audio=audio},
            job.Presentation,ReplayDetachedExportFiles.Hash(ReplayDetachedExportFiles.RuntimeImage),inputs.Mph,inputs.Fh,
            inputs.MapDirectory,inputs.UserMapDirectory,geometry,job.Subject,hashes);
        ReplayDetachedExportFiles.Write(Path.Combine(job.WorkDirectory,"ticket.json"),ticket);return ticket;
    }
    private static async Task<ReplayDetachedExportStatus> RunWorker(Job job)
    {
        var ticket=job.Ticket!;string host=Environment.ProcessPath??throw new IOException("This installation cannot launch its export worker.");
        var start=new ProcessStartInfo(host){UseShellExecute=false,WorkingDirectory=job.WorkDirectory,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        if(String.Equals(Path.GetFileNameWithoutExtension(host),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(ReplayDetachedExportFiles.RuntimeImage);
        start.ArgumentList.Add("-replayexportworker");start.ArgumentList.Add(Path.Combine(job.WorkDirectory,"ticket.json"));
        start.Environment["PROJECT_PRIME_USER_DATA"]=job.WorkDirectory;
        using var process=Process.Start(start)??throw new IOException("The export worker did not start.");
        var tail=new OutputTail();Task drainOut=DrainOutput(process.StandardOutput,tail),drainError=DrainOutput(process.StandardError,tail);
        var elapsed=Stopwatch.StartNew();long cancelAt=-1;
        try
        {
            while(!process.HasExited)
            {
                if(job.CancelRequested&&cancelAt<0){File.WriteAllText(ticket.CancelPath,ticket.Id.ToString("N"));cancelAt=elapsed.ElapsedMilliseconds;}
                if(cancelAt>=0&&elapsed.ElapsedMilliseconds-cancelAt>5000||elapsed.Elapsed.TotalHours>6)
                {process.Kill(entireProcessTree:true);await process.WaitForExitAsync().ConfigureAwait(false);break;}
                if(ReadStatus(ticket) is{} status)lock(Gate){_state=status.State.ToString();_status=status.Message;_progress=status.Progress;}
                await Task.Delay(100).ConfigureAwait(false);
            }
            await process.WaitForExitAsync().ConfigureAwait(false);await Task.WhenAll(drainOut,drainError).ConfigureAwait(false);
            var terminal=ReadStatus(ticket);
            if(job.CancelRequested)return new(ticket.Id,process.Id,ReplayExportState.Cancelled,"Independent export cancelled.",_progress,terminal?.Frames??0,null,null,DateTime.UtcNow);
            if(process.ExitCode!=0||terminal==null||terminal.ProcessId!=process.Id||terminal.State!=ReplayExportState.Completed)
                throw new IOException(terminal?.Error??terminal?.Message??$"The export worker exited with code {process.ExitCode}. {tail.Text} See {ticket.LogPath}");
            return terminal;
        }
        finally
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync().ConfigureAwait(false);}
        }
    }
    private sealed class OutputTail
    {
        private string _text="";
        internal void Append(char[] buffer,int count){lock(this){_text+=new string(buffer,0,count);if(_text.Length>8192)_text=_text[^8192..];}}
        internal string Text{get{lock(this)return _text;}}
    }
    private static async Task DrainOutput(StreamReader reader,OutputTail tail)
    {char[] buffer=new char[4096];int count;while((count=await reader.ReadAsync(buffer).ConfigureAwait(false))>0)tail.Append(buffer,count);}
    private static ReplayDetachedExportStatus? ReadStatus(ReplayDetachedExportTicket ticket)
    {
        try
        {
            var info=new FileInfo(ticket.StatusPath);if(!info.Exists||info.Length>65536)return null;
            var value=JsonSerializer.Deserialize<ReplayDetachedExportStatus>(File.ReadAllBytes(ticket.StatusPath),ReplayDetachedExportFiles.Json);
            return value?.Id==ticket.Id?value:null;
        }
        catch(Exception error)when(error is IOException or JsonException){return null;}
    }
    internal static TheatreExportQueueState Snapshot()
    {
        lock(Gate)return new(_revision,Pending.Select(JobView).ToImmutableArray(),_active==null?null:JobView(_active),
            _retry==null?null:JobView(_retry),_state,_status,_progress,Failures.ToImmutableArray(),_output);
    }
    private static TheatreExportJob JobView(Job job)
    {
        var m=job.Receipt.Manifest;
        string camera=m.CameraTrack?"SAVED TRACK":m.Director?"DIRECTOR":string.Join(", ",m.Segments?.Select(s=>s.Camera.ToString()).Distinct()??new[]{"CURRENT"});
        return new(job.Receipt.Id,job.Receipt,Path.GetFileName(job.LogicalPath),m.SuggestedOutput,
            $"{m.StartFrame}–{m.EndFrame} // {m.Width}×{m.Height} // {m.Fps} FPS // {m.PresetName} // {camera}"
            +$" // HUD {(m.IncludeGameHud?"ON":"OFF")} // OVERLAY {(m.IncludeReplayOverlay?"ON":"OFF")} // INDEPENDENT WORKER");
    }
    internal static bool CancelExact(TheatreExportQueueState reviewed)
    {
        lock(Gate)
        {
            if(_active==null||reviewed.Revision!=_revision||!ReferenceEquals(reviewed.Active?.Token,_active.Receipt)||_active.CancelRequested)return false;
            _active.CancelRequested=true;_active.Cancellation.Cancel();_revision++;_status="Cancellation requested for the independent export.";return true;
        }
    }
    internal static bool RetryExact(TheatreExportQueueState reviewed)
    {
        lock(Gate)
        {
            if(_retry==null||reviewed.Revision!=_revision||!ReferenceEquals(reviewed.Retry?.Token,_retry.Receipt)||!CheckCapacity())return false;
            var previous=_retry;var manifest=previous.Receipt.Manifest;
            string directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest.FramePattern))!,"retry-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            string frames=Path.Combine(directory,"frame_%08d.png"),output=Path.Combine(directory,Path.GetFileName(manifest.SuggestedOutput));
            manifest=manifest with{FramePattern=frames,SuggestedOutput=output,
                FfmpegArguments=manifest.FfmpegArguments.Replace(manifest.FramePattern,frames,StringComparison.Ordinal).Replace(manifest.SuggestedOutput,output,StringComparison.Ordinal)};
            ReplayVideoExport.WriteReviewedManifest(manifest);
            var job=new Job(manifest,previous.Presentation,previous.ExpectedHash,previous.Room,previous.Subject,previous.LogicalPath,previous.Ticket);
            Pending.Enqueue(job);_revision++;_status="Queued a new independent export attempt.";StartPump();return true;
        }
    }
    internal static bool ClearExact(TheatreExportQueueState reviewed)
    {
        lock(Gate)
        {
            if(reviewed.Revision!=_revision||!Pending.Select(j=>(object)j.Receipt).SequenceEqual(reviewed.Pending.Select(j=>j.Token)))return false;
            while(Pending.TryDequeue(out var job)){job.Inputs.Dispose();job.Cancellation.Dispose();}
            _revision++;_status="Pending exports cleared. The active worker continues.";return true;
        }
    }
}
#endif
