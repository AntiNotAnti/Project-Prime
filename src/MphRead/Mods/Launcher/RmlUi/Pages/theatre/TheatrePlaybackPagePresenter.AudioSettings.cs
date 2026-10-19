#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreStudioAudioSettings _studioAudioSettings=new();
    private bool _studioAudioEnabled=true;
    private void OpenStudioAudioSettings(TheatreStudioAudioSettings settings)
    {
        _studioAudioSettings=settings;
        _studioAudioEnabled=_studioAudioSettings.Enabled;
        _studioGameEvents=_studioAudioSettings.GameEvents;_studioCombatFeedback=_studioAudioSettings.CombatFeedback;
        foreach(var field in new[]{("game",_studioAudioSettings.GameGain),("combat",_studioAudioSettings.CombatGain),
            ("replay",_studioAudioSettings.ReplayGain),("music",_studioAudioSettings.MusicGain)})
            _host.SetField(_studioToolsModal,"replay_studio_"+field.Item1,field.Item2.ToString(CultureInfo.InvariantCulture));
        _host.SetField(_studioToolsModal,"replay_studio_music_file",_studioAudioSettings.MusicFile);
    }
    private TheatreStudioAudioSettings StudioAudioSettingsFromControls()
        =>new(_studioAudioEnabled,_studioGameEvents,_studioCombatFeedback,Gain("game"),Gain("combat"),Gain("replay"),Gain("music"),
            _host.ReadField(_studioToolsModal,"replay_studio_music_file").Trim(),_studioCueState.Cues);
    private TheatreStudioAudioSettings SaveStudioAudioSettings(ITheatreStudioToolsBackend backend,TheatreEditorContext context)
    {
        if(backend is not ITheatreStudioAudioSettingsBackend settings)
            throw new InvalidOperationException("Saved audio preferences are unavailable in this editor.");
        var proposed=StudioAudioSettingsFromControls();
        settings.SaveAudioSettings(context,proposed);_studioAudioSettings=proposed;
        _studioToolsStatus="Saved audio enable, event toggles, bus gains, music and custom cues for this recording.";
        return proposed;
    }
    private void PersistStudioAudioCues(ITheatreStudioToolsBackend backend,TheatreEditorContext context,IReadOnlyList<TheatreAudioCue> cues)
    {
        if(backend is ITheatreStudioAudioSettingsBackend settings)
        {
            var proposed=StudioAudioSettingsFromControls() with{Cues=cues};
            settings.SaveAudioSettings(context,proposed);_studioAudioSettings=proposed;
        }
        else backend.SaveStudioAudioCues(context,cues);
    }
    private void PresentStudioAudioSettings(bool blocked)
    {
        _host.SetText(_studioToolsModal,"replay_studio_audio_enabled","OFFLINE AUDIO: "+(_studioAudioEnabled?"ON":"OFF"));
        foreach(string id in new[]{"audio_enabled","audio_save","music_file"})
            _host.SetBool(_studioToolsModal,"disabled:replay_studio_"+id,blocked);
    }
}
#endif
