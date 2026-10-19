#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using MphRead.Sound;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Structs;

namespace MphRead.Mods.Replay;
/// <summary>A removable component on MusicPlayer's existing mixer. Never opens or owns a playback device.</summary>
internal sealed class ReplayStudioAudioPreview : IDisposable
{
    private sealed class Component : SoundComponent
    {
        private readonly StudioPcmAudio _audio;
        private readonly double _ratio;
        private double _position;
        internal readonly MusicCallbackLifetime Lifetime=new();
        internal Component(AudioEngine engine,AudioFormat format,StudioPcmAudio audio):base(engine,format)
        {_audio=audio;_ratio=audio.SampleRate/(double)format.SampleRate;Enabled=true;}
        protected override void GenerateAudio(Span<float> output,int channels)
        {
            output.Clear();if(channels<1||!Lifetime.TryEnter())return;
            try
            {
                for(int frame=0;frame<output.Length/channels;frame++)
                {
                    int index=(int)_position;if(index>=_audio.Frames)return;
                    int next=Math.Min(index+1,_audio.Frames-1);float fraction=(float)(_position-index);
                    for(int channel=0;channel<channels;channel++)
                    {int source=Math.Min(channel,_audio.Channels-1);float a=_audio.Samples[index*_audio.Channels+source],b=_audio.Samples[next*_audio.Channels+source];output[frame*channels+channel]=Math.Clamp(a+(b-a)*fraction,-1,1);}
                    _position+=_ratio;
                }
            }
            finally{Lifetime.Exit();}
        }
    }
    private readonly AudioPlaybackDevice _device;
    private readonly Component _component;
    private bool _disposed;
    internal ReplayStudioAudioPreview(StudioPcmAudio audio)
    {
        var engine=MusicPlayer.Engine??throw new InvalidOperationException("The shared audio device is unavailable.");
        _device=MusicPlayer.PlaybackDevice??throw new InvalidOperationException("The shared audio output is unavailable.");
        _component=new Component(engine,MusicPlayer.Format,audio);
        try{_device.MasterMixer.AddComponent(_component);_device.Start();}
        catch{_component.Lifetime.BeginRetirement();_device.MasterMixer.RemoveComponent(_component);_component.Dispose();throw;}
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_component.Lifetime.BeginRetirement();_component.Enabled=false;
        _device.MasterMixer.RemoveComponent(_component);
        // A mixer snapshot can retain the component after removal. Fence its
        // callback before releasing resources, matching the existing music owner.
        while(!_component.Lifetime.IsIdle)Thread.Yield();
        _component.Dispose();
    }
}
#endif
