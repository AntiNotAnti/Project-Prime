#if MPHREAD_STUDIO
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Sound;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;

namespace MphRead.Mods.StudioReplay;

/// <summary>Offline audio uses the canonical linear replica and selected cue assets.
/// OpenAL advances only when explicitly asked for samples; host timing is irrelevant.</summary>
internal sealed class StudioReplayAudioCapture : IDisposable
{
    private readonly PassiveReplayPlayer _player;
    private readonly StudioLoopbackAudioBackend _backend=new();
    private readonly StudioPcmWaveWriter _wave;
    private readonly ReplayCombatPresenter _feedback=new();
    private readonly ReplayDirectorState _director=new();
    private readonly bool _directing;
    private readonly ReplayCameraTrack _camera;
    private readonly ReplayCameraMode _mode;
    private readonly int _slot;
    private readonly uint _start,_end;
    private readonly long _totalSamples;
    private readonly short[] _samples=new short[StudioLoopbackAudioBackend.SamplesPerStep*2];
    private readonly StudioReplayCueMixer _cues;
    private ulong _audio;
    private bool _disposed;
    internal bool Complete=>_wave.SampleFrames>=_totalSamples;
    internal long SampleFrames=>_wave.SampleFrames;
    internal StudioReplayAudioCapture(string replay,string output,uint start,uint end,int fps,long videoFrames,
        ReplayCameraTrack camera,ReplayCameraMode mode,int slot,string encoder,bool directing=false)
    {
        _directing=directing;_start=start;_end=end;_camera=camera;_mode=mode;_slot=slot;
        _totalSamples=checked((videoFrames*StudioLoopbackAudioBackend.SampleRate+fps-1)/fps);
        _player=new(replay,new Vector2i(64,64),ReplayPlayerOptions.Linear);
        _wave=new(output,_totalSamples);
        _cues=new(encoder);
        try
        {
            // Cue routing is installed before Load, so warming never creates a
            // SoundFlow speaker device inside an offline worker.
            Mods.Sound.CombatFeedbackAudio.OfflineCueSink=_cues.Play;
            Sfx.Load(_player.Current.Scene,_backend,requireAudio:true);
            if(_player.Current.Session.Metadata is {} metadata&&ReplayMapIdentity.Validate(metadata)!=ReplayOpenResult.Success)
                throw new InvalidDataException("The original replay map hash is unavailable for audio capture.");
            _audio=ReplayAudioOwner.Acquire(_player.Current.Scene,null);
            _player.Current.Scene.StudioReplayPrepare(camera,0,1,mode,slot);
            _player.Current.Scene.StudioReplayApplyPresentationCamera();
        }
        catch{Dispose();throw;}
    }
    internal void Advance(CancellationToken cancellation)
    {
        // Work is bounded even for a long lead-in; cancellation never waits for
        // the complete recording to reconstruct before the next check.
        for(int i=0;i<60&&!Complete;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var current=_player.Current;var scene=current.Scene;
            uint frame=current.Session.CurrentFrame;
            bool warming=current.Session.IsWarming||frame<_start;

            if(!current.Session.HasSimulatedFrame||warming||frame<_end)
            {
                if(!current.Step())throw new InvalidDataException("Replay ended before audio selection completed.");
                frame=current.Session.CurrentFrame;
            }
            if(_directing)_director.Tick(scene,current.Session);
            scene.StudioReplayPrepare(_camera,frame,1,_directing?_director.CurrentMode:_mode,_directing&&_director.CurrentSlot>=0?_director.CurrentSlot:_slot);
            scene.StudioReplayApplyPresentationCamera();

            if(_audio!=0)
            {
                _feedback.Update(scene,current.Session);
                Sfx.Update(1f/60);
                bool audible=!current.Session.IsWarming&&frame>=_start;
                int count=audible?(int)Math.Min(StudioLoopbackAudioBackend.SamplesPerStep,_totalSamples-_wave.SampleFrames):StudioLoopbackAudioBackend.SamplesPerStep;
                _backend.Render(_samples,count);if(audible)_wave.Append(_samples,count);
            }
        }
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        try
        {
            ReplayAudioOwner.Release(_audio);_audio=0;
            _cues.Dispose();Mods.Sound.CombatFeedbackAudio.OfflineCueSink=null;
            Sfx.ShutDown();Sfx.ShutdownCompletion.GetAwaiter().GetResult();
        }
        finally {try{_player.Dispose();}finally{_wave.Dispose();}}
    }
}

/// <summary>Routes the game's selected WAV/MP3/FLAC feedback through the same
/// explicitly clocked OpenAL device rather than its real-time SoundFlow output.</summary>
internal sealed class StudioReplayCueMixer : IDisposable
{
    private readonly string _encoder;
    private readonly Dictionary<string,int> _buffers=new();
    private readonly List<int> _sources=new();
    private long _bufferBytes;
    internal StudioReplayCueMixer(string encoder)=>_encoder=encoder;
    internal void Play(byte[] encoded,float gain,bool replaceExisting)
    {
        string hash=Convert.ToHexString(SHA256.HashData(encoded));
        if(!_buffers.TryGetValue(hash,out int buffer))
        {
            byte[] pcm=Decode(encoded);if(_buffers.Count>=32||_bufferBytes+pcm.Length>64*1024*1024)throw new InvalidOperationException("Replay cue buffer budget exceeded.");buffer=AL.GenBuffer();
            try{AL.BufferData(buffer,ALFormat.Stereo16,pcm,StudioLoopbackAudioBackend.SampleRate);_buffers.Add(hash,buffer);_bufferBytes+=pcm.Length;}
            catch{AL.DeleteBuffer(buffer);throw;}
        }
        for(int i=_sources.Count-1;i>=0;i--)
        {
            int source=_sources[i];
            AL.GetSource(source,ALGetSourcei.SourceState,out int state);
            if(replaceExisting||(ALSourceState)state!=ALSourceState.Playing){AL.SourceStop(source);AL.DeleteSource(source);_sources.RemoveAt(i);}
        }
        if(_sources.Count>=32)throw new InvalidOperationException("Replay cue voice budget exceeded.");
        int next=AL.GenSource();_sources.Add(next);
        AL.Source(next,ALSourcei.Buffer,buffer);AL.Source(next,ALSourceb.SourceRelative,true);AL.Source(next,ALSourcef.Gain,gain);AL.SourcePlay(next);
        if(AL.GetError()!=ALError.NoError)throw new InvalidOperationException("Replay cue could not be submitted to OpenAL.");
    }
    private byte[] Decode(byte[] encoded)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(_encoder){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string arg in new[]{"-v","error","-i","pipe:0","-f","s16le","-ar","48000","-ac","2","pipe:1"})process.StartInfo.ArgumentList.Add(arg);
        using var childOwner=StudioReplayWorkerLifetime.StartChild(process);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var write=Task.Run(async()=>{await process.StandardInput.BaseStream.WriteAsync(encoded,timeout.Token).ConfigureAwait(false);process.StandardInput.Close();});
        var error=process.StandardError.ReadToEndAsync(timeout.Token);
        var read=Task.Run(async()=>
        {
            using var output=new MemoryStream();byte[] data=new byte[65536];int count;
            while((count=await process.StandardOutput.BaseStream.ReadAsync(data,timeout.Token).ConfigureAwait(false))!=0)
            {if(output.Length+count>32*1024*1024)throw new InvalidDataException("Decoded replay cue exceeds 32 MiB.");output.Write(data,0,count);}
            return output.ToArray();
        });
        try
        {
            Task.WhenAll(write,read).WaitAsync(timeout.Token).GetAwaiter().GetResult();process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            if(process.ExitCode!=0)throw new IOException("Replay cue decoding failed: "+error.GetAwaiter().GetResult());
            return read.GetAwaiter().GetResult();
        }
        catch
        {try{if(!process.HasExited)process.Kill(true);}finally{process.WaitForExit();}throw;}
    }
    public void Dispose()
    {foreach(int source in _sources){AL.SourceStop(source);AL.DeleteSource(source);}foreach(int buffer in _buffers.Values)AL.DeleteBuffer(buffer);_sources.Clear();_buffers.Clear();}
}

#endif
