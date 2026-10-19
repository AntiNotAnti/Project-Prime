#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Replay;

internal sealed record ReplayStudioAudioBinding(string SourceSha256,uint StartFrame,uint EndFrame);
internal sealed record ReplayStudioAudioSettingsReview(ReplayReviewedSidecar Reviewed,TheatreStudioAudioSettings Settings,
    ReplayStudioAudioBinding? Binding=null);
/// <summary>Bounded source-bound preferences; WAV bytes are decoded only by worker jobs when needed.</summary>
internal static class ReplayStudioAudioSettingsStore
{
    internal const string Extension=".studio-audio.json";
    private sealed record Document(int Version,string SourceRevision,TheatreAudioCue[] Cues,TheatreStudioAudioSettings? Settings=null,ReplayStudioAudioBinding? Binding=null);
    internal static TheatreStudioAudioSettings Freeze(TheatreStudioAudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach(float gain in new[]{settings.GameGain,settings.CombatGain,settings.ReplayGain,settings.MusicGain})
            if(!float.IsFinite(gain)||gain<0||gain>4)throw new ArgumentException("Audio bus gains must be between 0 and 4.");
        string music=settings.MusicFile?.Trim()??"";
        if(music.Length!=0)
        {
            if(music.Length>32768||!Path.IsPathFullyQualified(music)||!music.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a local music PCM WAV with an absolute path, or leave it blank.");
            music=Path.GetFullPath(music);
        }
        var cues=ReplayStudioAudioMix.FreezeCues(settings.Cues);
        if(cues.Select(c=>c.WaveFile).Append(music).Where(p=>p.Length!=0).Distinct(StringComparer.Ordinal).Count()>64)
            throw new ArgumentException("Audio supports at most 64 distinct WAV sources including music.");
        return settings with{MusicFile=music,Cues=Array.AsReadOnly(cues)};
    }
    internal static ReplayStudioAudioBinding CreateBinding(byte[] sourceHash,uint start,uint end)
    {
        ArgumentNullException.ThrowIfNull(sourceHash);
        if(sourceHash.Length!=32||end==0&&start!=0||end!=0&&start>=end)
            throw new ArgumentException("Review a valid recording hash and clip range.");
        return new(Convert.ToHexString(sourceHash),start,end);
    }
    // Source and logical range define the project. Playback-cache names, file
    // timestamps and materialized bytes belong to the current owner witness.
    internal static ReplayStudioAudioBinding? BindingFromRevision(string revision)
    {
        if(string.IsNullOrEmpty(revision))return null;
        const string source=":SOURCE-SHA:",playback=":PLAYBACK-SHA:",range=":RANGE:";
        int offset=revision.LastIndexOf(source,StringComparison.Ordinal);
        if(offset<0)return null;
        offset+=source.Length;
        int next=offset+64;
        if(next+playback.Length+64+range.Length>revision.Length)return null;
        string hash=revision.Substring(offset,64);
        if(!hash.All(char.IsAsciiHexDigit)||!revision.AsSpan(next).StartsWith(playback,StringComparison.Ordinal))return null;
        next+=playback.Length;
        if(!revision.AsSpan(next,64).ToString().All(char.IsAsciiHexDigit))return null;
        next+=64;
        if(!revision.AsSpan(next).StartsWith(range,StringComparison.Ordinal))return null;
        string[] frames=revision[(next+range.Length)..].Split(':');
        if(frames.Length!=2||!uint.TryParse(frames[0],NumberStyles.None,CultureInfo.InvariantCulture,out uint start)
            ||!uint.TryParse(frames[1],NumberStyles.None,CultureInfo.InvariantCulture,out uint end)
            ||end==0&&start!=0||end!=0&&start>=end)return null;
        return new(hash.ToUpperInvariant(),start,end);
    }
    private static ReplayStudioAudioBinding CheckBinding(ReplayStudioAudioBinding binding)
    {
        if(binding.SourceSha256==null||binding.SourceSha256.Length!=64||!binding.SourceSha256.All(char.IsAsciiHexDigit)
            ||binding.EndFrame==0&&binding.StartFrame!=0||binding.EndFrame!=0&&binding.StartFrame>=binding.EndFrame)
            throw new InvalidDataException("The saved audio recording identity is invalid.");
        return binding with{SourceSha256=binding.SourceSha256.ToUpperInvariant()};
    }
    internal static ReplayStudioAudioSettingsReview Read(string logicalPath,string sourceRevision)
    {
        if(string.IsNullOrWhiteSpace(sourceRevision))throw new ArgumentException("Review the recording source before reading audio settings.");
        return ReadCore(logicalPath,sourceRevision,BindingFromRevision(sourceRevision));
    }
    internal static ReplayStudioAudioSettingsReview ReadForCopy(string logicalPath,ReplayStudioAudioBinding currentBinding)
        =>ReadCore(logicalPath,"",CheckBinding(currentBinding));
    private static ReplayStudioAudioSettingsReview ReadCore(string logicalPath,string sourceRevision,ReplayStudioAudioBinding? currentBinding)
    {
        var reviewed=ReplayReviewedMetadata.Read(logicalPath+Extension);
        var document=ReplayReviewedMetadata.Parse(reviewed,()=>new Document(3,sourceRevision,Array.Empty<TheatreAudioCue>(),new(),currentBinding));
        if(document.Version is not(1 or 2 or 3))throw new InvalidDataException("The saved audio settings version is unsupported.");
        var savedBinding=document.Version==3&&document.Binding!=null?CheckBinding(document.Binding):BindingFromRevision(document.SourceRevision);
        bool matches=currentBinding!=null&&savedBinding!=null?currentBinding==savedBinding
            :sourceRevision.Length!=0&&document.SourceRevision==sourceRevision;
        if(reviewed.Bytes!=null&&!matches)throw new InvalidDataException("Audio settings belong to a different recording or clip range.");
        TheatreStudioAudioSettings settings;
        if(document.Version==1)settings=new(Cues:document.Cues);
        else
        {
            settings=document.Settings??throw new InvalidDataException("Saved audio settings are missing.");
            if(!(settings.Cues??Array.Empty<TheatreAudioCue>()).SequenceEqual(document.Cues??Array.Empty<TheatreAudioCue>()))
                throw new InvalidDataException("Saved audio cue snapshots disagree. Repair metadata before editing.");
        }
        return new(reviewed,Freeze(settings),currentBinding);
    }
    internal static ReplayStudioAudioSettingsReview Save(ReplayStudioAudioSettingsReview review,string sourceRevision,
        TheatreStudioAudioSettings settings,Action verifyOwner)
    {
        var frozen=Freeze(settings);var binding=BindingFromRevision(sourceRevision);
        if(review.Binding!=null&&binding!=review.Binding)
            throw new InvalidOperationException("The reviewed audio recording identity changed. Reopen the settings.");
        var result=ReplayReviewedMetadata.Commit(review.Reviewed,
            new Document(3,sourceRevision,frozen.Cues!.ToArray(),frozen,binding),verifyOwner);
        if(!result.Verified)throw new IOException(result.Notice);
        return new(result.Written,frozen,binding);
    }
    /// <summary>Worker-only staging; final publication still belongs to the reviewed project owner.</summary>
    internal static bool StageRebound(string stagedLogical,ReplayStudioAudioSettingsReview review,
        ReplayStudioAudioBinding targetBinding,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();ReplayReviewedMetadata.Validate(review.Reviewed);
        if(review.Reviewed.Bytes==null)return false;
        var frozen=Freeze(review.Settings);targetBinding=CheckBinding(targetBinding);
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Document(3,"",frozen.Cues!.ToArray(),frozen,targetBinding),
            new JsonSerializerOptions{WriteIndented=true});
        if(bytes.Length>ReplayReviewedMetadata.MaximumBytes)throw new InvalidDataException("Saved audio settings exceed their size limit.");
        string target=stagedLogical+Extension;bool created=false;
        try
        {
            using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {created=true;output.Write(bytes);output.Flush(true);}
            cancellation.ThrowIfCancellationRequested();ReplayReviewedMetadata.Validate(review.Reviewed);
            return true;
        }
        catch{if(created)File.Delete(target);throw;}
    }
}
#endif
