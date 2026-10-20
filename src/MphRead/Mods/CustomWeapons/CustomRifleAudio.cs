using System;
using System.Buffers.Binary;
using System.Threading;
using MphRead.Sound;
using OpenTK.Mathematics;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Structs;

namespace MphRead.Mods.CustomWeapons;

// One callback-owned cursor per player. Owner commands coalesce to the newest
// cue at the next audio block. The callback never waits, allocates or decodes.
internal sealed class CustomRiflePcmVoice
{
    private readonly float[][] _waves;
    private readonly double _ratio;
    private double _position;
    private long _command;
    private int _cue=-1;
    private float _playingGain;
    internal CustomRiflePcmVoice(float[][] waves,int outputRate)
    {
        ArgumentNullException.ThrowIfNull(waves);
        if(waves.Length!=4||outputRate is <8000 or >192000)throw new ArgumentException("M7 audio format.");
        foreach(var row in waves)
        {
            if(row==null||row.Length is <1 or >11025)throw new ArgumentException("M7 PCM budget.");
            foreach(float sample in row)if(!float.IsFinite(sample)||MathF.Abs(sample)>1)throw new ArgumentException("M7 PCM sample.");
        }
        _waves=waves;_ratio=22050d/outputRate;
    }
    internal void Play(CustomRifleCue cue,float gain)
    {
        if((uint)cue>=4||!float.IsFinite(gain))return;
        long command=((long)(uint)BitConverter.SingleToInt32Bits(Math.Clamp(gain,0,1))<<8)|(long)((uint)cue+1);
        Interlocked.Exchange(ref _command,command);
    }
    internal void Stop()=>Interlocked.Exchange(ref _command,-1);
    internal void Render(Span<float> output,int channels)
    {
        output.Clear();if(channels<1)return;
        long command=Interlocked.Exchange(ref _command,0);
        if(command<0)_cue=-1;
        else if(command>0){_cue=(int)(command&255)-1;_position=0;_playingGain=BitConverter.Int32BitsToSingle((int)(command>>8));}
        if(_cue<0)return;
        var samples=_waves[_cue];
        for(int frame=0;frame<output.Length/channels;frame++)
        {
            int index=(int)_position;if(index>=samples.Length){_cue=-1;return;}
            int next=Math.Min(index+1,samples.Length-1);float fraction=(float)(_position-index);
            float value=(samples[index]+(samples[next]-samples[index])*fraction)*_playingGain;
            for(int channel=0;channel<channels;channel++)output[frame*channels+channel]=value;
            _position+=_ratio;
        }
    }
}

internal sealed class CustomRifleAudio : IDisposable
{
    private sealed class Voice : SoundComponent
    {
        internal readonly MusicCallbackLifetime Lifetime=new();
        internal readonly CustomRiflePcmVoice Pcm;
        internal Voice(AudioEngine engine,AudioFormat format,float[][] waves):base(engine,format)
        {Pcm=new(waves,format.SampleRate);Enabled=true;}
        protected override void GenerateAudio(Span<float> output,int channels)
        {
            if(!Lifetime.TryEnter()){output.Clear();return;}
            try{Pcm.Render(output,channels);}finally{Lifetime.Exit();}
        }
    }
    private readonly AudioPlaybackDevice _device;
    private readonly Voice?[] _voices=new Voice[8];
    private readonly CustomRifleCue?[] _lastCues=new CustomRifleCue?[8];
    private bool _disposed;
    internal bool MatchesDevice=>!_disposed&&ReferenceEquals(_device,MusicPlayer.PlaybackDevice);
    internal int VoiceCount { get; private set; }
    private CustomRifleAudio(AudioPlaybackDevice device)=>_device=device;
    internal static CustomRifleAudio? Prewarm()
    {
        if(Mods.Headless.Active||Mods.ThumbnailMode.Active||!MusicPlayer.Available
            ||MusicPlayer.Engine is not {} engine||MusicPlayer.PlaybackDevice is not {} device)return null;
        var audio=new CustomRifleAudio(device);
        try
        {
            var waves=new float[4][];
            for(int cue=0;cue<4;cue++)
            {
                byte[] wave=CustomRifleCues.BuildWave((CustomRifleCue)cue);waves[cue]=new float[(wave.Length-44)/2];
                for(int i=0;i<waves[cue].Length;i++)waves[cue][i]=BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44+i*2))/32768f;
            }
            for(int slot=0;slot<8;slot++)
            {
                var voice=new Voice(engine,MusicPlayer.Format,waves);audio._voices[slot]=voice;
                device.MasterMixer.AddComponent(voice);audio.VoiceCount++;
            }
            device.Start();return audio;
        }
        catch{audio.Dispose();throw;}
    }
    internal bool Play(int slot,CustomRifleCue cue,Vector3 source,Vector3 listener)
    {
        if(!MatchesDevice||(uint)slot>=8)return false;
        float distance=(source-listener).Length;if(!float.IsFinite(distance)||distance>70)return true;
        float gain=Math.Clamp(Sfx.Volume*AudioMixer.GetVolume(AudioBus.SoundEffects)*.36f/(1+.12f*distance+.01f*distance*distance),0,.36f);
        _lastCues[slot]=cue;_voices[slot]!.Pcm.Play(cue,gain);return true;
    }
    internal void Stop(int slot){if((uint)slot<8){_lastCues[slot]=null;_voices[slot]?.Pcm.Stop();}}
    internal void StopShot(int slot)
    {if((uint)slot<8&&_lastCues[slot] is CustomRifleCue.Shot or CustomRifleCue.Bolt)Stop(slot);}
    internal void StopAll(){for(int i=0;i<8;i++)Stop(i);}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        foreach(var voice in _voices)if(voice!=null){voice.Lifetime.BeginRetirement();voice.Enabled=false;}
        foreach(var voice in _voices)if(voice!=null)
        {
            try{_device.MasterMixer.RemoveComponent(voice);}
            catch(ObjectDisposedException){ /* Captured output already retired. */ }
            while(!voice.Lifetime.IsIdle)Thread.Yield();
            voice.Dispose();
        }
        Array.Clear(_voices);VoiceCount=0;
    }
}
