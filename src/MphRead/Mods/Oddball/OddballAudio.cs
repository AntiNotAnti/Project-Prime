using System;
using System.Collections.Generic;
using MphRead.Sound;
using OpenTK.Audio.OpenAL;

namespace MphRead.Mods.Oddball;

/// <summary>Announcer on the existing effects device, independent of the music output.</summary>
internal static class OddballAudio
{
    internal static readonly string[] CueNames = OddballAudioAssets.CueNames;
    private static readonly Dictionary<string, int> Buffers = new();
    private static readonly Queue<string> Pending = new();
    private static Scene? _owner;
    private static int _source, _revision = -1;
    private static ALContext _context;
    private static string? _last;
    private static ulong _lastTick;
    internal static int LoadedCount => Buffers.Count;
    internal static bool HasCue(Scene scene, string name) => scene == _owner && Buffers.ContainsKey(name);
    internal static int PlayedCount { get; private set; }
    internal static int QueuedCount => Pending.Count;
    internal static bool IsPlaying => _source != 0 && ALC.GetCurrentContext() == _context
        && (ALSourceState)AL.GetSource(_source, ALGetSourcei.SourceState) == ALSourceState.Playing;
    internal static string Status => _source == 0 ? "ANNOUNCER: NO EFFECTS OUTPUT" :
        Sfx.Volume * AudioMixer.GetVolume(AudioBus.Notifications) <= 0 ? "ANNOUNCER: NOTIFICATIONS MUTED" :
        LoadedCount == CueNames.Length ? "ANNOUNCER: READY" :
        $"ANNOUNCER: {LoadedCount}/{CueNames.Length} - {OddballAudioAssets.State.ToUpperInvariant()}";

    internal static void Warm(Scene scene)
    {
        if (scene == _owner) return;
        Dispose();
        if (scene.GameState.Mode != GameMode.Oddball || Headless.Active || !scene.Services.AllowsPresentationSideEffects) return;
        OddballAudioAssets.LoadLocal();OddballAudioAssets.Start();
        if (Sfx.Instance is not SfxInstance || ALC.GetCurrentContext() == ALContext.Null)
        { Console.WriteLine("[oddball-audio] no effects context; not opening a second audio device");return; }
        _owner=scene;_context=ALC.GetCurrentContext();
        _source=AL.GenSource();
        AL.Source(_source,ALSourceb.SourceRelative,true);
        AL.Source(_source,ALSource3f.Position,0,0,0);
        AL.Source(_source,ALSourcef.RolloffFactor,0);
        AL.Source(_source,ALSourceb.Looping,false);
        InstallPrepared();
        Console.WriteLine($"[oddball-audio] effects source {_source}; {Status}; music device is not required");
    }
    private static void InstallPrepared()
    {
        if(_source==0 || _revision==OddballAudioAssets.Revision)return;
        _revision=OddballAudioAssets.Revision;
        foreach(var pair in OddballAudioAssets.Prepared)
        {
            if(Buffers.ContainsKey(pair.Key))continue;
            int buffer=AL.GenBuffer();
            try
            {
                var pcm=pair.Value;
                AL.BufferData(buffer,ALFormat.Mono16,pcm.Samples,pcm.Rate);
                if(AL.GetError()!=ALError.NoError)throw new InvalidOperationException("Effects buffer upload failed");
                Buffers.Add(pair.Key,buffer);
            }
            catch(Exception e){AL.DeleteBuffer(buffer);Console.WriteLine($"[oddball-audio] {pair.Key}: {e.Message}");}
        }
    }
    internal static bool Queue(Scene scene,string name,bool priority=false)
    {
        if(scene!=_owner || !Buffers.ContainsKey(name))return false;
        if(!priority && name==_last && scene.FrameCount-_lastTick<60)return true;
        _last=name;_lastTick=scene.FrameCount;
        if(priority){Pending.Clear();AL.SourceStop(_source);}
        if(Pending.Count<4)Pending.Enqueue(name);
        return true;
    }
    internal static void Tick(Scene scene)
    {
        if(scene!=_owner || _source==0 || ALC.GetCurrentContext()!=_context)return;
        InstallPrepared();
        float gain=Math.Clamp(Sfx.Volume*AudioMixer.GetVolume(AudioBus.Notifications),0,1);
        AL.Source(_source,ALSourcef.Gain,gain);
        if(IsPlaying || Pending.Count==0)return;
        string name=Pending.Dequeue();
        AL.Source(_source,ALSourcei.Buffer,Buffers[name]);AL.SourcePlay(_source);
        if(IsPlaying) { PlayedCount++;Console.WriteLine($"[oddball-audio] PLAY {name} gain={gain:0.###} source={_source}"); }
        else Console.WriteLine($"[oddball-audio] failed to start {name}: {AL.GetError()}");
    }
    internal static void Dispose()
    {
        if(_source!=0 && ALC.GetCurrentContext()==_context)
        { AL.SourceStop(_source);AL.Source(_source,ALSourcei.Buffer,0);AL.DeleteSource(_source);foreach(int buffer in Buffers.Values)AL.DeleteBuffer(buffer); }
        Buffers.Clear();Pending.Clear();_source=0;_owner=null;_last=null;_lastTick=0;PlayedCount=0;_revision=-1;
    }
}
