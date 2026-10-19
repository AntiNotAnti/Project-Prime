#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private RmlUiDocumentToken _studioToolsModal;
    private TheatreEditorContext? _studioToolsContext;
    private Task<TheatreStudioToolResult>? _studioToolsTask;
    private CancellationTokenSource? _studioToolsCancellation;
    private TheatreStudioTool _studioTool;
    private bool _studioPreviewRequested;
    private bool _studioGameEvents=true,_studioCombatFeedback;
    private string _studioToolsStatus="",_studioToolsError="",_studioToolsOutput="";
    private bool HandleStudioToolsIntent(in RmlUiIntent intent)
    {
        if(!IsOpen||_editorBackend is not ITheatreStudioToolsBackend backend||_editor==null
            ||!Enum.IsDefined((TheatrePlaybackAction)intent.Argument))return false;
        var action=(TheatrePlaybackAction)intent.Argument;
        bool opening=action==TheatrePlaybackAction.StudioToolsOpen&&_studioToolsModal==default&&intent.Document==_editorModal;
        bool editing=_studioToolsModal!=default&&intent.Document==_studioToolsModal&&_pages.Top==_studioToolsModal
            &&(action>=TheatrePlaybackAction.StudioToolsRun&&action<=TheatrePlaybackAction.StudioToolsClose||action is TheatrePlaybackAction.StudioToolsPreview or TheatrePlaybackAction.StudioToolsStopPreview
                ||action>=TheatrePlaybackAction.StudioCueEvent&&action<=TheatrePlaybackAction.StudioCueRemove
                ||action is TheatrePlaybackAction.StudioAudioEnabled or TheatrePlaybackAction.StudioAudioSave);
        if((!opening&&!editing)||!_pages.Accept(intent))return false;
        try
        {
            _studioToolsError="";
            if(opening)
            {
                _studioToolsContext=_editor.Snapshot().Context;backend.ValidateStudioTool(_studioToolsContext);
                _studioTool=TheatreStudioTool.AudioMix;_studioToolsOutput="";_studioToolsStatus="Review the operation and inputs before running. Each operation writes into a new export folder.";
                _studioToolsModal=_pages.OpenModal(new("replay-studio-tools","pages/theatre/studio-tools.rml","replay_studio_operation"));
                foreach(string id in new[]{"game","combat","replay"})_host.SetField(_studioToolsModal,"replay_studio_"+id,"1");
                _host.SetField(_studioToolsModal,"replay_studio_music","0.5");
                OpenAudioCues(backend,_studioToolsContext);
            }
            else if(action==TheatrePlaybackAction.StudioToolsClose)CloseStudioTools();
            else if(action==TheatrePlaybackAction.StudioToolsStopPreview){backend.StopStudioAudioPreview();_studioToolsStatus="Mixer preview stopped.";}
            else if(action==TheatrePlaybackAction.StudioToolsCancel){CancelStudioTools();_studioToolsStatus="Cancellation requested. A completed export may remain in its owned output folder.";}
            else if(_studioToolsTask==null&&_studioCueTask==null&&_studioToolsContext is{} context)
            {
                backend.ValidateStudioTool(context);
                switch(action)
                {
                    case TheatrePlaybackAction.StudioAudioEnabled:_studioAudioEnabled=!_studioAudioEnabled;break;
                    case TheatrePlaybackAction.StudioAudioSave:SaveStudioAudioSettings(backend,context);break;
                    case TheatrePlaybackAction.StudioToolsOperation:_studioTool=(TheatreStudioTool)(((int)_studioTool+1)%Enum.GetValues<TheatreStudioTool>().Length);break;
                    case TheatrePlaybackAction.StudioToolsGameEvents:_studioGameEvents=!_studioGameEvents;break;
                    case TheatrePlaybackAction.StudioToolsCombatFeedback:_studioCombatFeedback=!_studioCombatFeedback;break;
                    case TheatrePlaybackAction.StudioCueEvent:case TheatrePlaybackAction.StudioCueBus:
                    case TheatrePlaybackAction.StudioCuePrevious:case TheatrePlaybackAction.StudioCueNext:
                    case TheatrePlaybackAction.StudioCueAdd:case TheatrePlaybackAction.StudioCueUpdate:case TheatrePlaybackAction.StudioCueRemove:
                        HandleAudioCueAction(action,backend,context);break;
                    case TheatrePlaybackAction.StudioToolsRun:case TheatrePlaybackAction.StudioToolsPreview:
                        _studioPreviewRequested=action==TheatrePlaybackAction.StudioToolsPreview;
                        bool audioOperation=_studioPreviewRequested||_studioTool is TheatreStudioTool.AudioMix or TheatreStudioTool.MuxVideo;
                        var audio=audioOperation?SaveStudioAudioSettings(backend,context):StudioAudioSettingsFromControls();
                        if(audioOperation&&!audio.Enabled)throw new InvalidOperationException("Enable offline audio before mixing or previewing audio.");
                        uint start=context.MarkIn??0,end=context.MarkOut??context.Duration;
                        var options=new TheatreStudioToolOptions(_studioPreviewRequested?TheatreStudioTool.AudioMix:_studioTool,_host.ReadField(_studioToolsModal,"replay_studio_input").Trim(),
                            Gain("game"),Gain("combat"),Gain("replay"),Gain("music"),_studioGameEvents,_studioCombatFeedback,start,end,_host.ReadField(_studioToolsModal,"replay_studio_overlay").Trim(),_studioCueState.Cues,MusicFile:audio.MusicFile);
                        _studioToolsCancellation=new();_studioToolsTask=backend.RunStudioTool(context,options,_studioToolsCancellation.Token);
                        _studioToolsStatus="Working on a detached export task...";_studioToolsOutput="";break;
                }
            }
        }
        catch(Exception error)when(ToolError(error)){_studioToolsError=error.Message;}
        PresentStudioTools();return true;
    }
    private float Gain(string id)
    {
        if(!float.TryParse(_host.ReadField(_studioToolsModal,"replay_studio_"+id),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)
            ||!float.IsFinite(value)||value<0||value>4)throw new ArgumentException("Enter audio gains between 0 and 4 using a decimal point.");
        return value;
    }
    private static bool ToolError(Exception error)=>error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException or OverflowException;
    private void PresentStudioTools()
    {
        if(_studioToolsModal==default)return;
        if(!_host.IsAlive(_studioToolsModal)){backendStopPreview();CancelStudioTools();_studioToolsModal=default;_studioToolsContext=null;return;}
        if(_studioToolsContext is not{} context||_editorBackend is not ITheatreStudioToolsBackend backend)return;
        bool current=_editor!=null&&_editor.Snapshot().Live.Current;
        if(!current){CancelStudioTools();_studioToolsError="The replay owner changed. Close this panel.";}
        if(_studioToolsTask is{IsCompleted:true} completed)
        {
            _studioToolsTask=null;
            try
            {
                var result=completed.GetAwaiter().GetResult();backend.ValidateStudioTool(context);
                _studioToolsStatus=result.Summary;_studioToolsOutput=result.OutputPath;
                if(_studioPreviewRequested){backend.PreviewStudioAudio(context,result.OutputPath);_studioToolsStatus="Playing the reviewed mixer preview on the shared audio output.";}
                _studioPreviewRequested=false;
            }
            catch(Exception error)when(ToolError(error)){_studioToolsError=error.Message;_studioToolsStatus="The operation did not publish a verified result. A completed file may remain in its owned export folder.";}
            _studioToolsCancellation?.Dispose();_studioToolsCancellation=null;
        }
        PresentAudioCues(backend,context);
        _host.SetText(_studioToolsModal,"replay_studio_source",Path.GetFileName(context.LogicalPath)+$" // FRAME {context.Frame} // RANGE {context.MarkIn??0}–{context.MarkOut??context.Duration}");
        _host.SetText(_studioToolsModal,"replay_studio_operation","OPERATION: "+(_studioTool switch
        {
            TheatreStudioTool.AudioMix=>"AUDIO MIX",TheatreStudioTool.CompareRecording=>"COMPARE RECORDING",
            TheatreStudioTool.PortableBundle=>"PORTABLE BUNDLE",TheatreStudioTool.DiagnosticBundle=>"DIAGNOSTIC BUNDLE",
            TheatreStudioTool.SaveBuildEvidence=>"SAVE BUILD EVIDENCE",TheatreStudioTool.CompareBuildEvidence=>"COMPARE BUILD EVIDENCE",TheatreStudioTool.CompareWorld=>"COMPARE SIMULATED WORLDS",TheatreStudioTool.ImportPortable=>"IMPORT PORTABLE BUNDLE",_=>"MUX VIDEO WITH AUDIO"
        }));
        _host.SetText(_studioToolsModal,"replay_studio_help",_studioTool switch
        {
            TheatreStudioTool.AudioMix=>"Optional input: music WAV. Event cues use canonical game assets plus your saved custom event WAV bindings. Audio writes a standalone 48 kHz stereo mix for the reviewed range; no device is opened.",
            TheatreStudioTool.CompareRecording=>"Required input: another .ppdemo recording. Compare recorded decoder state and player positions at the reviewed frame. This does not simulate a second full world.",
            TheatreStudioTool.PortableBundle=>"Packages this materialized replay plus reviewed annotation, reel and camera sidecars. Exact installed custom map package is included when available; otherwise its required identity is recorded.",
            TheatreStudioTool.DiagnosticBundle=>"Packages recorded event metadata and reviewed editor sidecars. Recording bytes and player names are excluded.",
            TheatreStudioTool.SaveBuildEvidence=>"Pause and refresh before running. Save gameplay, complete graph, simulation presentation, camera, projectile and animation hashes from this exact presented frame for comparison with another build.",
            TheatreStudioTool.ImportPortable=>"Required input: a Prime Studio .ppreplay bundle. Verify and extract only its fixed members into a new private export folder. Camera state and reels are rebound to the extracted replay. Exact custom maps are preserved without changing the currently installed map.",
            TheatreStudioTool.CompareWorld=>"Required input: another replay with the exact same map identity. Pause and refresh first. An isolated replica seeks the reviewed frame in bounded engine-thread slices; compare complete graph, gameplay and simulation-owned presentation state without changing the foreground clock.",
            TheatreStudioTool.MuxVideo=>"Required input: an exported video. Mix the reviewed range and mux it into a new MP4 with FFmpeg. Use Replay Overlay WAV for optional authored audio. The shorter stream determines output duration.",
            _=>"Required input: build-evidence.json saved by another build at the same reviewed frame and hash schema. Pause and refresh before comparing."
        });
        _host.SetText(_studioToolsModal,"replay_studio_game_events","GAME EVENT CUES: "+(_studioGameEvents?"ON":"OFF"));
        _host.SetText(_studioToolsModal,"replay_studio_combat_feedback","HEADSHOT FEEDBACK: "+(_studioCombatFeedback?"ON":"OFF"));
        _host.SetText(_studioToolsModal,"replay_studio_status",_studioToolsStatus);_host.SetText(_studioToolsModal,"replay_studio_error",_studioToolsError);
        _host.SetText(_studioToolsModal,"replay_studio_output",_studioToolsOutput);
        foreach(string id in new[]{"run","preview","operation","game_events","combat_feedback","input","overlay","game","combat","replay","music"})
            _host.SetBool(_studioToolsModal,"disabled:replay_studio_"+id,!current||_studioToolsTask!=null||_studioCueTask!=null);
        _host.SetBool(_studioToolsModal,"disabled:replay_studio_cancel",_studioToolsTask==null&&_studioCueTask==null);
        PresentStudioAudioSettings(!current||_studioToolsTask!=null||_studioCueTask!=null);
    }
    private void backendStopPreview()=>(_editorBackend as ITheatreStudioToolsBackend)?.StopStudioAudioPreview();
    private void CancelStudioTools()
    {
        _studioToolsCancellation?.Cancel();_studioToolsCancellation?.Dispose();_studioToolsCancellation=null;
        if(_studioToolsTask is{} retired)_=retired.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
        if(_studioCueTask is{} retiredCue)_=retiredCue.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
        _studioToolsTask=null;_studioCueTask=null;
    }
    private bool BackStudioTools(){if(WorkspaceMode&&!_workspaceClosing&&true)return false;if(_studioToolsModal==default)return false;CloseStudioTools();return true;}
    private bool CloseStudioTools()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if(_studioToolsModal!=default&&_studioToolsModal!=_document&&_host.IsAlive(_studioToolsModal)&&!_pages.CloseModal())return false;
        (_editorBackend as ITheatreStudioToolsBackend)?.StopStudioAudioPreview();
        CancelStudioTools();_studioToolsModal=default;_studioToolsContext=null;return true;
    }
}
#endif
