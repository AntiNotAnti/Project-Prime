#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Replay;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor : ITheatreStudioAudioSettingsBackend
    {
        private ReplayStudioAudioSettingsReview? _reviewedAudioSettings;
        public TheatreStudioAudioSettings ReadAudioSettings(TheatreEditorContext context)
        {
            Validate(context,false);
            _reviewedAudioSettings=ReplayStudioAudioSettingsStore.Read(context.LogicalPath,context.SourceRevision);
            return _reviewedAudioSettings.Settings;
        }
        public TheatreAudioCueState ReadStudioAudioCues(TheatreEditorContext context)
        {
            var settings=ReadAudioSettings(context);
            var cues=settings.Cues!;
            var events=((Files)context.Persisted).Events.GroupBy(e=>e.Type.ToString(),StringComparer.Ordinal)
                .OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new TheatreAudioEventValues(g.Key,
                    Array.AsReadOnly(g.Select(e=>e.Value).Distinct().Order().ToArray()))).ToArray();
            return new(cues,Array.AsReadOnly(events));
        }
        public Task<TheatreAudioCue> ValidateStudioAudioCue(TheatreEditorContext context,TheatreAudioCue cue,CancellationToken cancellation)
        {
            Validate(context,false);cue=ReplayStudioAudioMix.ValidateCue(cue);
            if(!((Files)context.Persisted).Events.Any(e=>e.Type.ToString()==cue.EventType))
                throw new ArgumentException("Choose an event type recorded in this replay.");
            return ReplayStorageJobs.Run(()=>
            {
                cancellation.ThrowIfCancellationRequested();RequireRegular(cue.WaveFile);
                var pcm=StudioPcmAudio.ReadWave(cue.WaveFile);
                if(pcm.Frames==0)throw new InvalidDataException("The cue WAV contains no samples.");
                cancellation.ThrowIfCancellationRequested();return cue;
            },cancellation);
        }
        public void SaveStudioAudioCues(TheatreEditorContext context,IReadOnlyList<TheatreAudioCue> cues)
        {
            if(_reviewedAudioSettings is not{} reviewed)
                throw new InvalidOperationException("Reopen audio tools before editing settings.");
            SaveAudioSettings(context,reviewed.Settings with{Cues=cues});
        }
        public void SaveAudioSettings(TheatreEditorContext context,TheatreStudioAudioSettings settings)
        {
            Validate(context,false);
            var frozen=ReplayStudioAudioSettingsStore.Freeze(settings);
            if(_reviewedAudioSettings is not{} reviewed||reviewed.Reviewed.Path!=context.LogicalPath+ReplayStudioAudioSettingsStore.Extension)
                throw new InvalidOperationException("Reopen audio tools to review saved settings before editing.");
            if(frozen.Cues!.Any(c=>!((Files)context.Persisted).Events.Any(e=>e.Type.ToString()==c.EventType)))
                throw new ArgumentException("Choose an event type recorded in this replay.");
            _reviewedAudioSettings=ReplayStudioAudioSettingsStore.Save(reviewed,context.SourceRevision,frozen,()=>Validate(context,false));
        }
    }
}
#endif
