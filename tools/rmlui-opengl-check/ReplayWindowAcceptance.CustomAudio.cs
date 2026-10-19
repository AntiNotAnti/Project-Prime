using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal sealed partial class ReplayWindowAcceptance
{
    private ReplayEvent _rml97CueEvent;
    private string _rml97CueRevision="";
    private TheatreAudioCueState Rml97Cues=>Read<TheatreAudioCueState>(Page,"_studioCueState")!;
    private void PlanRml97CustomAudio()
    {
        Do("RML97 select an actual recorded event inside reviewed audio range",()=>
        {
            Require(!File.Exists(_rml97Path+".studio-audio.json"),"isolated recording begins without audio cue metadata");
            var selected=DemoPlayback.Events.Where(e=>e.Frame<30).OrderBy(e=>e.Frame).ToArray();
            Require(selected.Length>0,"qualification source contains a real recorded event in first 30 frames");
            _rml97CueEvent=selected[0];
        });
        Wait("RML97 native event button selects recorded event type",()=>
        {
            int selected=(int)Page!.GetType().GetField("_studioCueEvent",Private)!.GetValue(Page)!;
            var cues=Rml97Cues;
            Require(cues!=null&&cues.Events.Count>0&&selected>=0&&selected<cues.Events.Count,
                "actual Audio tab must initialize recorded cue events before selection; count="+(cues?.Events.Count??0)+" selected="+selected
                +" toolsCurrent="+TryTools()?.Snapshot().Current+" workspaceProblem="+Read<string>(Page,"_workspaceProblem"));
            if(cues!.Events[selected].EventType==_rml97CueEvent.Type.ToString())return true;
            Activate("replay_studio_cue_event");return false;
        },5000);
        Do("RML97 author event WAV value gain and bus through native fields",()=>
        {
            Host!.SetField(Host.CurrentInputDocument,"replay_studio_cue_wave",_rml97Wave);
            Host.SetField(Host.CurrentInputDocument,"replay_studio_cue_value",_rml97CueEvent.Value.ToString(CultureInfo.InvariantCulture));
            Host.SetField(Host.CurrentInputDocument,"replay_studio_cue_gain","0.25");
            Activate("replay_studio_cue_bus"); // Replay -> Music, whose reviewed bus gain is 0.5.
            Activate("replay_studio_cue_add");
        });
        Wait("RML97 actual backend saved source-bound custom cue and native count",()=>
        {
            if(!Rml97CueReady(1))return false;
            VerifyRml97Cue(.25f);_rml97CueRevision=Hash(_rml97Path+".studio-audio.json");return true;
        },15000);
        Do("RML97 update selected custom cue using actual native action",()=>
        {
            Host!.SetField(Host.CurrentInputDocument,"replay_studio_cue_gain","0.5");
            Activate("replay_studio_cue_update");
        });
        Wait("RML97 updated custom cue atomically replaced saved binding",()=>
        {
            if(!Rml97CueReady(1)||Rml97Cues.Cues[0].Gain!=.5f)return false;
            VerifyRml97Cue(.5f);Require(Hash(_rml97Path+".studio-audio.json")!=_rml97CueRevision,"update published changed sidecar");
            _rml97CueRevision=Hash(_rml97Path+".studio-audio.json");return true;
        },15000);
        Do("RML97 leave persistent Audio inspector with its saved cue",()=>Activate("studio_tab_camera"));
        Do("RML97 return through actual Audio workspace tab",()=>Activate("studio_tab_audio"));
        Wait("RML97 persistent native inspector retains exact persisted event cue",()=>
        {
            if(!Rml97CueReady(1))return false;
            VerifyRml97Cue(.5f);Require(Hash(_rml97Path+".studio-audio.json")==_rml97CueRevision,"reopening does not rewrite cues");
            Host!.SetField(Host.CurrentInputDocument,"replay_studio_overlay",_rml97Wave);
            _rml97StudioChecks.Add(new{Feature="actual-native-custom-audio-add-update-tab-return",Event=_rml97CueEvent.Type.ToString(),
                _rml97CueEvent.Value,Bus="Music",Gain=.5,SourceRevision=Editor!.Snapshot().Context.SourceRevision,SidecarSha256=_rml97CueRevision});
            return true;
        });
    }
    private bool Rml97CueReady(int count)
    {
        string error=Read<string>(Page,"_studioToolsError")??"";
        Require(error.Length==0,"actual custom cue authoring error: "+error);
        return Read<Task<TheatreAudioCue>>(Page,"_studioCueTask")==null&&Rml97Cues.Cues.Count==count
            &&NativeSemanticReadback.Read(Host!,Host!.CurrentInputDocument).ContainsText(count+"/64 SAVED EVENT CUES");
    }
    private void VerifyRml97Cue(float gain)
    {
        var cue=Rml97Cues.Cues.Single();
        Require(cue.EventType==_rml97CueEvent.Type.ToString()&&cue.Value==_rml97CueEvent.Value
            &&cue.Bus==3&&cue.Gain==gain&&cue.WaveFile==_rml97Wave,"native authoring retains recorded event/value, WAV, Music bus and gain");
        using var saved=JsonDocument.Parse(File.ReadAllText(_rml97Path+".studio-audio.json"));var root=saved.RootElement;
        Require(root.GetProperty("SourceRevision").GetString()==Editor!.Snapshot().Context.SourceRevision
            &&root.GetProperty("Cues").GetArrayLength()==1
            &&root.GetProperty("Cues")[0].GetProperty("Gain").GetSingle()==gain,"persisted cue is bound to exact reviewed source revision");
        Rml97SameWorld();
    }
    private void VerifyRml97CustomAudioWave(StudioPcmAudio pcm)
    {
        int sample=checked((int)_rml97CueEvent.Frame*800);
        // The authored overlay and cue WAV contain the same constant quarter-scale
        // PCM. Music bus .5 * cue gain .5 adds one quarter of that PCM per match.
        float authored=StudioPcmAudio.ReadWave(_rml97Wave).Samples[0];
        int matches=DemoPlayback.Events.Count(e=>e.Type==_rml97CueEvent.Type&&e.Value==_rml97CueEvent.Value&&e.Frame<=_rml97CueEvent.Frame);
        float expected=Math.Clamp(authored*(1+.25f*matches),-1,1);
        Require(Math.Abs(pcm.Samples[sample*2]-expected)<.001f,"actual mixed WAV includes custom event cue at exact frame and Music bus/gain");
        _rml97StudioChecks.Add(new{Feature="actual-custom-event-mix-waveform",Frame=_rml97CueEvent.Frame,Matches=matches,Expected=expected,Actual=pcm.Samples[sample*2],Scope="generated private PCM; actual backend output; no audibility claim"});
    }
    private void PlanRml97RemoveCustomAudio()
    {
        Do("RML97 select restored custom cue through actual Next button",()=>Activate("replay_studio_cue_next"));
        Do("RML97 remove selected custom cue through actual native action",()=>Activate("replay_studio_cue_remove"));
        Wait("RML97 actual backend persisted cue removal and native zero count",()=>
        {
            if(!Rml97CueReady(0))return false;
            using var saved=JsonDocument.Parse(File.ReadAllText(_rml97Path+".studio-audio.json"));
            Require(saved.RootElement.GetProperty("Cues").GetArrayLength()==0&&Hash(_rml97Path)==_sourceHash,"removal saved empty cues while recording bytes stayed unchanged");
            _rml97StudioChecks.Add(new{Feature="actual-native-custom-audio-remove",Count=0,RecordingSha256=_sourceHash});return true;
        });
    }
}
