#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreAudioCueState _studioCueState=new(Array.Empty<TheatreAudioCue>(),Array.Empty<TheatreAudioEventValues>());
    private int _studioCueSelected=-1,_studioCueEvent,_studioCueBus=2;
    private int? _studioCueReplace;
    private Task<TheatreAudioCue>? _studioCueTask;
    private void OpenAudioCues(ITheatreStudioToolsBackend backend,TheatreEditorContext context)
    {
        var cues=backend.ReadStudioAudioCues(context);
        var settings=backend is ITheatreStudioAudioSettingsBackend saved
            ?saved.ReadAudioSettings(context):new TheatreStudioAudioSettings(Cues:cues.Cues);
        // Read both source-bound snapshots before committing the inspector state.
        _studioCueState=cues;_studioCueSelected=-1;_studioCueEvent=0;_studioCueBus=2;
        OpenStudioAudioSettings(settings);
        _host.SetField(_studioToolsModal,"replay_studio_cue_gain","1");
        _host.SetField(_studioToolsModal,"replay_studio_cue_value","");
        _host.SetField(_studioToolsModal,"replay_studio_cue_wave","");
    }
    private void HandleAudioCueAction(TheatrePlaybackAction action,ITheatreStudioToolsBackend backend,TheatreEditorContext context)
    {
        if(action==TheatrePlaybackAction.StudioCueEvent)
        {_studioCueEvent=(_studioCueEvent+1)%Math.Max(1,_studioCueState.Events.Count);return;}
        if(action==TheatrePlaybackAction.StudioCueBus){_studioCueBus=(_studioCueBus+1)%4;return;}
        if(action is TheatrePlaybackAction.StudioCuePrevious or TheatrePlaybackAction.StudioCueNext)
        {
            if(_studioCueState.Cues.Count==0)return;
            _studioCueSelected=(_studioCueSelected+(action==TheatrePlaybackAction.StudioCueNext?1:-1)+_studioCueState.Cues.Count)%_studioCueState.Cues.Count;
            var cue=_studioCueState.Cues[_studioCueSelected];_studioCueBus=cue.Bus;
            _studioCueEvent=Math.Max(0,_studioCueState.Events.ToList().FindIndex(e=>e.EventType==cue.EventType));
            _host.SetField(_studioToolsModal,"replay_studio_cue_wave",cue.WaveFile);
            _host.SetField(_studioToolsModal,"replay_studio_cue_gain",cue.Gain.ToString(CultureInfo.InvariantCulture));
            _host.SetField(_studioToolsModal,"replay_studio_cue_value",cue.Value?.ToString(CultureInfo.InvariantCulture)??"");return;
        }
        bool update=action==TheatrePlaybackAction.StudioCueUpdate;
        if((update||action==TheatrePlaybackAction.StudioCueRemove)&&_studioCueSelected<0)
            throw new InvalidOperationException("Select a saved cue with Previous/Next first.");
        if(action==TheatrePlaybackAction.StudioCueRemove)
        {
            var list=_studioCueState.Cues.ToList();list.RemoveAt(_studioCueSelected);
            PersistStudioAudioCues(backend,context,list);_studioCueState=_studioCueState with{Cues=Array.AsReadOnly(list.ToArray())};
            _studioCueSelected=-1;_studioToolsStatus="Removed and saved the selected event cue.";return;
        }
        if(_studioCueState.Events.Count==0)throw new InvalidOperationException("This recording has no event types to bind.");
        if(!update&&_studioCueState.Cues.Count>=64)throw new InvalidOperationException("At most 64 event cues are supported.");
        string valueText=_host.ReadField(_studioToolsModal,"replay_studio_cue_value").Trim();int? value=null;
        if(valueText.Length!=0)
        {
            if(!int.TryParse(valueText,NumberStyles.Integer,CultureInfo.InvariantCulture,out int parsed))
                throw new ArgumentException("Event value must be an integer, or blank for every value.");
            value=parsed;
        }
        if(!float.TryParse(_host.ReadField(_studioToolsModal,"replay_studio_cue_gain"),NumberStyles.Float,CultureInfo.InvariantCulture,out float gain)
            ||!float.IsFinite(gain)||gain<0||gain>4)throw new ArgumentException("Cue gain must be between 0 and 4.");
        var proposed=new TheatreAudioCue(_studioCueState.Events[_studioCueEvent].EventType,
            _host.ReadField(_studioToolsModal,"replay_studio_cue_wave").Trim(),_studioCueBus,value,gain);
        _studioCueReplace=update?_studioCueSelected:null;
        _studioToolsCancellation=new();_studioCueTask=backend.ValidateStudioAudioCue(context,proposed,_studioToolsCancellation.Token);
        _studioToolsStatus="Validating the event cue WAV before saving...";
    }
    private void PresentAudioCues(ITheatreStudioToolsBackend backend,TheatreEditorContext context)
    {
        if(_studioCueTask is{IsCompleted:true} complete)
        {
            _studioCueTask=null;
            try
            {
                var cue=complete.GetAwaiter().GetResult();backend.ValidateStudioTool(context);
                var cues=_studioCueState.Cues.ToList();
                if(_studioCueReplace is{} index)cues[index]=cue;else cues.Add(cue);
                PersistStudioAudioCues(backend,context,cues);
                _studioCueState=_studioCueState with{Cues=Array.AsReadOnly(cues.ToArray())};
                _studioCueSelected=_studioCueReplace??cues.Count-1;_studioToolsStatus="Saved the custom event cue for this recording.";
            }
            catch(Exception error)when(ToolError(error)){_studioToolsError=error.Message;}
            _studioToolsCancellation?.Dispose();_studioToolsCancellation=null;
        }
        var chosen=_studioCueState.Events.ElementAtOrDefault(_studioCueEvent);
        _host.SetText(_studioToolsModal,"replay_studio_cue_event","EVENT: "+(chosen?.EventType??"NO RECORDED EVENTS"));
        _host.SetText(_studioToolsModal,"replay_studio_cue_values",chosen==null?"No recorded values.":"RECORDED VALUES: "+string.Join(", ",chosen.Values.Take(32)));
        _host.SetText(_studioToolsModal,"replay_studio_cue_bus","CUE BUS: "+new[]{"GAME","COMBAT","REPLAY","MUSIC"}[_studioCueBus]);
        _host.SetText(_studioToolsModal,"replay_studio_cue_list",$"{_studioCueState.Cues.Count}/64 SAVED EVENT CUES\n"+
            string.Join("\n",_studioCueState.Cues.Select((c,i)=>$"{(i==_studioCueSelected?"> ":"  ")}{i+1}: {c.EventType} [{c.Value?.ToString(CultureInfo.InvariantCulture)??"any"}] // {new[]{"GAME","COMBAT","REPLAY","MUSIC"}[c.Bus]} × {c.Gain:0.###} // {System.IO.Path.GetFileName(c.WaveFile)}")));
        bool blocked=_studioToolsTask!=null||_studioCueTask!=null||_editor==null||!_editor.Snapshot().Live.Current;
        foreach(string id in new[]{"event","bus","previous","next","add","update","remove","wave","gain","value"})
            _host.SetBool(_studioToolsModal,"disabled:replay_studio_cue_"+id,blocked);
    }
}
#endif
