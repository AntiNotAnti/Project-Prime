#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;
internal static class ReplayStudioAudioMix
{
    internal static TheatreAudioCue ValidateCue(TheatreAudioCue cue)
    {
        if(!Enum.TryParse<ReplayEventType>(cue.EventType,out var type)||!Enum.IsDefined(type)
            ||type.ToString()!=cue.EventType)throw new ArgumentException("Choose a recorded event type.");
        if(cue.Bus<0||cue.Bus>3||!float.IsFinite(cue.Gain)||cue.Gain<0||cue.Gain>4)
            throw new ArgumentException("Cue bus must be Game, Combat, Replay or Music and gain must be 0–4.");
        if(string.IsNullOrWhiteSpace(cue.WaveFile)||cue.WaveFile.Length>32768
            ||!Path.IsPathFullyQualified(cue.WaveFile)||!cue.WaveFile.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter an absolute local PCM WAV path.");
        return cue with{WaveFile=Path.GetFullPath(cue.WaveFile)};
    }
    internal static TheatreAudioCue[] FreezeCues(IReadOnlyList<TheatreAudioCue>? cues)
    {
        if(cues is{Count:>64})throw new ArgumentException("At most 64 event cue bindings are supported.");
        return (cues??Array.Empty<TheatreAudioCue>()).Select(ValidateCue).ToArray();
    }
    internal static void AppendCustomCues(List<StudioAudioClip> clips,IReadOnlyList<TheatreAudioCue> cues,
        IReadOnlyList<ReplayEvent> events,uint start,uint end,CancellationToken cancellation)
    {
        var decoded=new Dictionary<string,StudioPcmAudio>(StringComparer.Ordinal);
        foreach(var cue in FreezeCues(cues))
        {
            cancellation.ThrowIfCancellationRequested();
            if(new FileInfo(cue.WaveFile).LinkTarget!=null)throw new IOException("Use a regular WAV file for an event cue.");
            if(!decoded.TryGetValue(cue.WaveFile,out var wave))
            {
                wave=StudioPcmAudio.ReadWave(cue.WaveFile);
                if(wave.Frames==0)throw new InvalidDataException("The cue WAV contains no samples.");
                decoded.Add(cue.WaveFile,wave);
            }
            foreach(var marker in events)
            {
                cancellation.ThrowIfCancellationRequested();
                if(marker.Type.ToString()!=cue.EventType||cue.Value is{} value&&marker.Value!=value
                    ||marker.Frame>end||marker.Frame+wave.Frames*60d/wave.SampleRate<=start)continue;
                clips.Add(new(marker.Frame,(StudioAudioBus)cue.Bus,wave,cue.Gain));
                if(clips.Count>100_000)throw new InvalidDataException("Audio selection exceeds 100,000 recorded cues.");
            }
        }
    }
    internal static void Write(string output,TheatreStudioToolOptions options,IReadOnlyList<ReplayEvent> events,IReadOnlyList<ReplayPlayerInfo> players,CancellationToken cancellation,long? videoFrames=null,int videoFps=60,double musicOffsetSeconds=0,string? replaySource=null)
    {
        var clips=new List<StudioAudioClip>();
        bool Overlaps(StudioAudioClip clip)=>clip.Frame<=options.End&&(clip.Loop||clip.Frame+clip.Audio.Frames*60d/clip.Audio.SampleRate/clip.Pitch>options.Start);
        bool customWeaponAudio=options.GameEvents&&replaySource!=null&&ReplayCustomOfflineAudio.Append(replaySource,clips,options.Start,options.End,cancellation);
        using var game=options.GameEvents?new StudioOfflineGameSounds(players,cancellation):null;
        StudioPcmAudio? headshot=null;
        if(options.CombatFeedback)
        {
            var assembly=typeof(ReplayStudioAudioMix).Assembly;
            string? name=assembly.GetManifestResourceNames().FirstOrDefault(n=>n.EndsWith("headshot-prime.wav",StringComparison.OrdinalIgnoreCase));
            if(name==null)throw new InvalidDataException("The bundled combat feedback audio is unavailable.");
            using var resource=assembly.GetManifestResourceStream(name)!;headshot=StudioPcmAudio.ReadWave(resource);
        }
        foreach(var marker in events)
        {
            cancellation.ThrowIfCancellationRequested();if(marker.Frame>options.End)continue;
            int first=clips.Count;
            if(!(customWeaponAudio&&marker.Type==ReplayEventType.WeaponFired&&marker.Value==10))game?.Append(marker,clips);
            for(int i=clips.Count-1;i>=first;i--)if(!Overlaps(clips[i]))clips.RemoveAt(i);
            if(marker.Type==ReplayEventType.Headshot&&headshot!=null)
            {var clip=new StudioAudioClip(marker.Frame,StudioAudioBus.Combat,headshot);if(Overlaps(clip))clips.Add(clip);}
            if(clips.Count>100_000)throw new InvalidDataException("Audio selection exceeds 100,000 recorded cues.");
        }
        AppendCustomCues(clips,options.AudioCues??Array.Empty<TheatreAudioCue>(),events,options.Start,options.End,cancellation);
        string musicFile=string.IsNullOrWhiteSpace(options.MusicFile)?options.InputPath:options.MusicFile;
        if(!string.IsNullOrWhiteSpace(musicFile))
        {
            string music=Path.GetFullPath(musicFile);
            if(new FileInfo(music).LinkTarget!=null)throw new IOException("Use a regular WAV file for the music source.");
            clips.Add(new(options.Start,StudioAudioBus.Music,StudioPcmAudio.ReadWave(music),Loop:true,SourceOffsetSeconds:musicOffsetSeconds));
        }
        if(!string.IsNullOrWhiteSpace(options.ReplayOverlay))
        {
            string overlay=Path.GetFullPath(options.ReplayOverlay);
            if(new FileInfo(overlay).LinkTarget!=null)throw new IOException("Use a regular WAV file for the replay overlay.");
            clips.Add(new(options.Start,StudioAudioBus.Replay,StudioPcmAudio.ReadWave(overlay)));
        }
        StudioOfflineAudio.WriteWave(output,options.Start,videoFrames??options.End-options.Start,videoFps,clips,
            new(options.GameGain,options.CombatGain,options.ReplayGain,options.MusicGain),cancellation);
    }
}
#endif
