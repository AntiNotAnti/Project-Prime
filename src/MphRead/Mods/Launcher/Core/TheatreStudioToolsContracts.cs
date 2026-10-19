#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;
public enum TheatreStudioTool { AudioMix, CompareRecording, PortableBundle, DiagnosticBundle, SaveBuildEvidence, CompareBuildEvidence, MuxVideo, CompareWorld, ImportPortable }
public sealed record TheatreStudioToolOptions(TheatreStudioTool Tool,string InputPath,float GameGain,float CombatGain,float ReplayGain,float MusicGain,bool GameEvents,bool CombatFeedback,uint Start,uint End,string ReplayOverlay="",IReadOnlyList<TheatreAudioCue>? AudioCues=null,string MusicFile="");
public sealed record TheatreStudioToolResult(string Summary,string OutputPath);
public sealed record TheatreAudioCue(string EventType,string WaveFile,int Bus=2,int? Value=null,float Gain=1);
public sealed record TheatreAudioEventValues(string EventType,IReadOnlyList<int> Values);
public sealed record TheatreAudioCueState(IReadOnlyList<TheatreAudioCue> Cues,IReadOnlyList<TheatreAudioEventValues> Events);
public interface ITheatreStudioToolsBackend
{
    Task<TheatreStudioToolResult> RunStudioTool(TheatreEditorContext context,TheatreStudioToolOptions options,CancellationToken cancellation);
    void ValidateStudioTool(TheatreEditorContext context);
    void PreviewStudioAudio(TheatreEditorContext context,string wavePath);
    void StopStudioAudioPreview();
    TheatreAudioCueState ReadStudioAudioCues(TheatreEditorContext context)
        => new(Array.Empty<TheatreAudioCue>(),Array.Empty<TheatreAudioEventValues>());
    Task<TheatreAudioCue> ValidateStudioAudioCue(TheatreEditorContext context,TheatreAudioCue cue,CancellationToken cancellation)
        => throw new NotSupportedException("Custom audio cues are unavailable.");
    void SaveStudioAudioCues(TheatreEditorContext context,IReadOnlyList<TheatreAudioCue> cues)
        => throw new NotSupportedException("Custom audio cues are unavailable.");
}
#endif
