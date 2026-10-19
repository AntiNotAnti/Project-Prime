using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal static class CustomReplayAudioAcceptance
{
    internal static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"prime-custom-audio-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string wave=Path.Combine(directory,"cue.wav"),output=Path.Combine(directory,"mixed.wav");
            var pcm=new StudioPcmAudio(48000,1,Enumerable.Repeat(.25f,480).ToArray());
            StudioOfflineAudio.WriteWave(wave,0,1,60,new[]{new StudioAudioClip(0,StudioAudioBus.Replay,pcm)});
            var bound=new TheatreAudioCue("WeaponFired",wave,2,7,2);
            var events=new[]{new ReplayEvent(10,ReplayEventType.WeaponFired,Value:7),
                new ReplayEvent(20,ReplayEventType.WeaponFired,Value:8),new ReplayEvent(30,ReplayEventType.Kill,Value:7)};
            var clips=new List<StudioAudioClip>();
            ReplayStudioAudioMix.AppendCustomCues(clips,new[]{bound,bound with{Value=null,Bus=3,Gain=.25f}},events,0,60,CancellationToken.None);
            Require(clips.Count==3&&clips.Count(c=>c.Frame==10)==2&&clips.Count(c=>c.Frame==20)==1,
                "event type and optional recorded value match exact event times");
            Require(clips[0].Bus==StudioAudioBus.Replay&&clips[0].Gain==2
                &&clips[1].Bus==StudioAudioBus.Music&&clips[1].Gain==.25f,"per-binding bus and gain retained");
            var mutable=new List<TheatreAudioCue>{bound};var frozen=ReplayStudioAudioMix.FreezeCues(mutable);
            mutable[0]=bound with{Gain=3};Require(frozen[0].Gain==2,"binding snapshot detached from authoring list");
            var options=new TheatreStudioToolOptions(TheatreStudioTool.AudioMix,"",0,0,.5f,0,false,false,0,60,AudioCues:frozen);
            ReplayStudioAudioMix.Write(output,options,events,Array.Empty<ReplayPlayerInfo>(),CancellationToken.None);
            var rendered=StudioPcmAudio.ReadWave(output);
            Require(rendered.Frames==48000&&rendered.Channels==2,"custom cue mixes bounded 48k stereo");
            Require(Math.Abs(rendered.Samples[8000*2]-.25f)<.001f&&rendered.Samples[16000*2]==0,
                "custom gain multiplied by replay bus and unmatched event remains silent");
            var updated=frozen.Select(c=>c with{Value=8,Bus=1,Gain=.75f}).ToArray();
            var updatedClips=new List<StudioAudioClip>();
            ReplayStudioAudioMix.AppendCustomCues(updatedClips,updated,events,0,60,CancellationToken.None);
            Require(updatedClips.Count==1&&updatedClips[0].Frame==20&&updatedClips[0].Bus==StudioAudioBus.Combat,
                "updated binding targets replacement event value and bus");
            var removed=new List<StudioAudioClip>();
            ReplayStudioAudioMix.AppendCustomCues(removed,Array.Empty<TheatreAudioCue>(),events,0,60,CancellationToken.None);
            Require(removed.Count==0,"removing binding removes authored event clips");
            Throws(()=>ReplayStudioAudioMix.FreezeCues(Enumerable.Repeat(bound,65).ToArray()),"64 cue bound");
            Throws(()=>ReplayStudioAudioMix.ValidateCue(bound with{Bus=9}),"invalid bus");
            Throws(()=>ReplayStudioAudioMix.ValidateCue(bound with{Gain=float.NaN}),"nonfinite gain");
            Throws(()=>ReplayStudioAudioMix.ValidateCue(bound with{EventType="NotRecordedType"}),"unknown event");
            Console.WriteLine("RML03 PASS custom recorded event audio: exact values, timing, bus/gain, immutable edits, removal and bounded real PCM mix");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static void Require(bool pass,string detail)
    {if(!pass)throw new InvalidOperationException("Custom replay audio: "+detail);}
    private static void Throws(Action action,string detail)
    {try{action();}catch(ArgumentException){return;}throw new InvalidOperationException("Expected rejection: "+detail);}
}
