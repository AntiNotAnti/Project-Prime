#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;
internal sealed record ReplayExportAudioReference(string Path,long Length,long LastWriteTicks);
internal sealed record ReplayExportAudioInput(TheatreStudioAudioSettings Settings,ImmutableArray<ReplayEvent> Events,
    ImmutableArray<ReplayPlayerInfo> Players,ImmutableArray<ReplayExportAudioReference> Sources)
{
    internal static ReplayExportAudioInput Capture(TheatreStudioAudioSettings settings,IEnumerable<ReplayEvent> events,IEnumerable<ReplayPlayerInfo> players)
    {
        settings=ReplayStudioAudioSettingsStore.Freeze(settings);
        if(!settings.Enabled)return new(settings,ImmutableArray<ReplayEvent>.Empty,ImmutableArray<ReplayPlayerInfo>.Empty,ImmutableArray<ReplayExportAudioReference>.Empty);
        var frozenEvents=events.Take(100001).ToImmutableArray();
        if(frozenEvents.Length>100000)throw new InvalidDataException("Offline audio supports at most 100,000 recorded events in one export. Disable offline audio for this recording.");
        var paths=(settings.Cues??Array.Empty<TheatreAudioCue>()).Select(c=>c.WaveFile).Append(settings.MusicFile).Where(p=>p.Length!=0).Distinct(StringComparer.Ordinal).ToArray();
        if(paths.Length>64)throw new InvalidDataException("Offline audio supports at most 64 WAV sources.");
        long total=0;var sources=ImmutableArray.CreateBuilder<ReplayExportAudioReference>();
        foreach(string path in paths)
        {
            var info=new FileInfo(path);
            if(!info.Exists||info.LinkTarget!=null||info.Length>128*1024*1024)throw new IOException("Choose regular WAV sources of at most 128 MiB.");
            total=checked(total+info.Length);if(total>128*1024*1024)throw new InvalidDataException("Offline audio WAV sources exceed the 256 MiB decoded audio budget.");
            sources.Add(new(info.FullName,info.Length,info.LastWriteTimeUtc.Ticks));
        }
        return new(settings,frozenEvents,players.ToImmutableArray(),sources.ToImmutable());
    }
}

/// <summary>Only storage workers call Write. Accepted values/events are immutable; external WAVs are copied and content-verified before decoding.</summary>
internal static class ReplayExportAudio
{
    internal static long TotalVideoFrames(IReadOnlyList<ReplayVideoSegment> segments,int fps)
    {
        long count=0;
        foreach(var segment in segments)count=checked(count+new ReplayExportSampler(segment.StartFrame,segment.EndFrame,fps).Count);
        return count;
    }
    internal static long SegmentSamples(long priorVideoFrames,long segmentVideoFrames,int fps)
        =>StudioOfflineAudio.SampleCount(checked(priorVideoFrames+segmentVideoFrames),fps)
            -(priorVideoFrames==0?0:StudioOfflineAudio.SampleCount(priorVideoFrames,fps));
    private static bool Matches(ReplayExportAudioReference source)
    {
        var info=new FileInfo(source.Path);
        return info.Exists&&info.LinkTarget==null&&info.Length==source.Length&&info.LastWriteTimeUtc.Ticks==source.LastWriteTicks;
    }
    private static string Hash(Stream input,CancellationToken token)
    {
        input.Position=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[65536];int count;
        while((count=input.Read(buffer))>0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,count);}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static TheatreStudioAudioSettings SnapshotSources(ReplayExportAudioInput input,string directory,CancellationToken token)
    {
        var replacements=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int index=0;index<input.Sources.Length;index++)
        {
            token.ThrowIfCancellationRequested();var source=input.Sources[index];
            if(!Matches(source))throw new IOException("An accepted audio source changed. Review the WAV and queue a new export.");
            using var original=new FileStream(source.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
            string frozen=Path.Combine(directory,$"source-{index:D2}.wav");using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using(var output=new FileStream(frozen,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                byte[] buffer=new byte[65536];int count;
                while((count=original.Read(buffer))>0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,count);output.Write(buffer,0,count);}
                output.Flush(true);
            }
            string copied=Convert.ToHexString(hash.GetHashAndReset());
            if(!Matches(source)||Hash(original,token)!=copied)throw new IOException("An audio source changed while its immutable bytes were copied.");
            replacements.Add(source.Path,frozen);
        }
        var settings=input.Settings;
        return settings with{MusicFile=settings.MusicFile.Length==0?"":replacements[settings.MusicFile],
            Cues=Array.AsReadOnly((settings.Cues??Array.Empty<TheatreAudioCue>()).Select(c=>c with{WaveFile=replacements[c.WaveFile]}).ToArray())};
    }
    internal static string Write(ReplayVideoExportManifest job,CancellationToken token)
    {
        var input=job.Audio??throw new ArgumentException("No accepted audio snapshot.");
        if(!input.Settings.Enabled)throw new ArgumentException("Offline audio is disabled for this export.");
        var segments=job.Segments is{Count:>0}?job.Segments.ToArray():new[]{new ReplayVideoSegment(job.StartFrame,job.EndFrame,"Selection",ReplaySegmentCamera.Current)};
        if(segments.Length>1024||segments.Any(s=>s.StartFrame>=s.EndFrame))throw new InvalidDataException("Invalid audio export segment range.");
        long totalFrames=TotalVideoFrames(segments,job.Fps),samples=StudioOfflineAudio.SampleCount(totalFrames,job.Fps),bytes=checked(samples*4);
        if(bytes>uint.MaxValue-36)throw new InvalidDataException("Offline audio exceeds the WAV file limit.");
        string directory=Path.GetDirectoryName(Path.GetFullPath(job.FramePattern))!;
        string work=Path.Combine(directory,".audio-work-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        string output=Path.Combine(directory,"offline.wav"),staging=Path.Combine(work,"offline.staging");
        try
        {
            var settings=SnapshotSources(input,work,token);long prior=0;
            using(var stream=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(stream,Encoding.ASCII,leaveOpen:true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write((uint)(bytes+36));writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16u);writer.Write((ushort)1);writer.Write((ushort)2);writer.Write(48000);writer.Write(192000);writer.Write((ushort)4);writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write((uint)bytes);writer.Flush();
                byte[] buffer=new byte[65536];
                for(int index=0;index<segments.Length;index++)
                {
                    token.ThrowIfCancellationRequested();var segment=segments[index];long frames=new ReplayExportSampler(segment.StartFrame,segment.EndFrame,job.Fps).Count;
                    long count=SegmentSamples(prior,frames,job.Fps);string wave=Path.Combine(work,$"segment-{index:D4}.wav");
                    var options=new TheatreStudioToolOptions(TheatreStudioTool.AudioMix,"",settings.GameGain,settings.CombatGain,settings.ReplayGain,settings.MusicGain,
                        settings.GameEvents,settings.CombatFeedback,segment.StartFrame,segment.EndFrame,AudioCues:settings.Cues,MusicFile:settings.MusicFile);
                    double musicOffset=(prior==0?0:StudioOfflineAudio.SampleCount(prior,job.Fps))/48000d;
                    ReplayStudioAudioMix.Write(wave,options,input.Events,input.Players,token,frames,job.Fps,musicOffset);
                    using(var pcm=new FileStream(wave,FileMode.Open,FileAccess.Read,FileShare.Read))
                    {
                        // Write() above produces canonical 44-byte PCM16 stereo headers.
                        if(pcm.Length<44+count*4)throw new InvalidDataException("Audio segment ended before its exact video duration.");
                        pcm.Position=44;long remaining=count*4;
                        while(remaining>0){token.ThrowIfCancellationRequested();int read=pcm.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(read==0)throw new EndOfStreamException();stream.Write(buffer,0,read);remaining-=read;}
                    }
                    File.Delete(wave);prior=checked(prior+frames);
                }
                writer.Flush();stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();File.Move(staging,output,overwrite:true);return output;
        }
        finally
        {
            try{Directory.Delete(work,recursive:true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    internal static IReadOnlyList<string> EncoderArguments(ReplayVideoExportManifest job,string audio)
    {
        var values=new List<string>{"-progress","pipe:1","-nostats","-y","-framerate",job.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",job.FramePattern,
            "-i",audio,"-map","0:v:0","-map","1:a:0","-vf",$"scale={job.Width}:{job.Height}:flags=lanczos,fps={job.Fps}","-c:v",job.EncoderCodec};
        if(job.EncoderCodec is "libx264" or "libx265")values.AddRange(new[]{"-preset","slow","-crf","18"});else values.AddRange(new[]{"-b:v","20M"});
        values.AddRange(new[]{"-pix_fmt","yuv420p","-movflags","+faststart","-c:a","aac","-shortest",job.SuggestedOutput});return values;
    }
}
#endif
