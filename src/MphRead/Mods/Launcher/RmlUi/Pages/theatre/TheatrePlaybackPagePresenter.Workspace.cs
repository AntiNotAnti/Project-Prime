#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private bool WorkspaceMode=>_editorBackend!=null&&_document!=default;
    private bool _workspaceClosing,_workspaceSettings;
    private readonly HashSet<string> _workspaceHiddenTracks=new(StringComparer.Ordinal);
    private string _workspaceTab="camera",_workspaceProblem="";
    public bool TryTakeStudioSettingsRequest(){bool pending=_workspaceSettings;_workspaceSettings=false;return pending;}
    private void EnsureWorkspaceOwners()
    {
        if(!WorkspaceMode||_workspaceClosing||_pages.Top!=_document)return;
        try
        {
            if(_editor==null){_editor=new(_editorBackend!);_editorModal=_document;}
            if(_cameraEditor==null&&_cameraBackend!=null){_cameraEditor=new(_cameraBackend);_cameraModal=_document;}
            if(_cameraTools==null&&_cameraBackend is ITheatreCameraToolsBackend tools)
            {_cameraTools=new(tools);_cameraToolsModal=_document;_cameraCurveReview=null;}
            if(_bookmarks==null&&_bookmarkBackend!=null){_bookmarks=new(_bookmarkBackend);_bookmarkModal=_document;}
            if(_ranges==null&&_rangeBackend!=null){_ranges=new(_rangeBackend);_rangeModal=_document;}
            if(_exports==null&&_exportBackend!=null){_exports=new(_exportBackend);_exportModal=_document;}
            if(_timeline==null&&_cameraTools!=null){_timeline=new();_timelineModal=_document;RefreshTimeline();}
            else if(_timeline!=null&&_timelineLanes==null)RefreshTimeline();
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_workspaceProblem=error.Message;}
    }
    private void RefreshWorkspaceReview(bool allowSeekAdoption=false)
    {
        _workspaceProblem="";
        Viewport.Release();ReleaseTimelineInput();ReleaseCameraCurveInput();
        EnsureWorkspaceOwners();
        if(allowSeekAdoption)_editor?.RefreshWorkspace();else _editor?.Refresh();
        _cameraEditor?.Refresh();_cameraTools?.Refresh();_bookmarks?.Refresh();_ranges?.Refresh();_exports?.Refresh();
        if(_timeline!=null)RefreshTimeline();
        if(_editor!=null)
        {
            if(_spatialTask==null)_spatialContext=_editor.Snapshot().Context;
            if(_studioToolsTask==null&&_studioCueTask==null)_studioToolsContext=_editor.Snapshot().Context;
        }
    }
    private void SelectWorkspaceTab(string tab)
    {
        if(!WorkspaceMode)return;_workspaceProblem="";Viewport.Release();ReleaseTimelineInput();ReleaseCameraCurveInput();_workspaceTab=tab;
        try
        {
            EnsureWorkspaceOwners();
            if(tab=="analysis"&&_editor!=null&&_editorBackend is ITheatreSpatialBackend spatial)
            {_spatialContext=_editor.Snapshot().Context;spatial.ValidateSpatial(_spatialContext);_spatialModal=_document;}
            if(tab=="audio"&&_editor!=null&&_editorBackend is ITheatreStudioToolsBackend tools)
            {
                if(_studioToolsModal==default)
                {
                    var context=_editor.Snapshot().Context;tools.ValidateStudioTool(context);
                    _studioToolsModal=_document;_studioToolsContext=context;
                    try{OpenAudioCues(tools,context);}
                    catch
                    {
                        // A stale review must leave initialization retryable when the
                        // user refreshes and returns to Audio. Existing drafts stay intact.
                        _studioToolsModal=default;_studioToolsContext=null;throw;
                    }
                }
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException){_workspaceProblem=error.Message;}
        PresentWorkspace();
    }
    private bool HandleWorkspaceIntent(in RmlUiIntent intent)
    {
        if(!WorkspaceMode||!IsOpen||intent.Kind!=RmlUiIntentKind.ReplayAction||intent.Document!=_document||_pages.Top!=_document)return false;
        bool typed=TryWorkspaceCommand(intent.Argument,out string id);
        var action=(TheatrePlaybackAction)intent.Argument;
        string? tab=action switch
        {
            TheatrePlaybackAction.CameraEditor or TheatrePlaybackAction.CameraToolsOpen=>"camera",
            TheatrePlaybackAction.Bookmarks or TheatrePlaybackAction.Ranges or TheatrePlaybackAction.EditorOpen=>"markers",
            TheatrePlaybackAction.SpatialOpen=>"analysis",TheatrePlaybackAction.StudioToolsOpen=>"audio",TheatrePlaybackAction.ExportOpen=>"export",
            TheatrePlaybackAction.TimelineOpen=>"timeline",_=>null
        };
        bool refreshAction=action is TheatrePlaybackAction.EditorRefresh or TheatrePlaybackAction.TimelineRefresh;
        bool workspaceAction=typed||action==TheatrePlaybackAction.Studio||refreshAction;
        if(tab==null&&!workspaceAction)return false;
        if(typed&&!_host.TryGetElementBounds(_document,id,out _,out _,out _,out _))return false;
        if(!_pages.Accept(intent))return false;
        try
        {
            if(refreshAction)RefreshWorkspaceReview(allowSeekAdoption:true);
            else if(tab!=null){if(tab=="timeline")_host.FocusDocument(_document,"replay_timeline_surface");else SelectWorkspaceTab(tab);}
            else if(id=="studio_settings"){Viewport.Release();ReleaseTimelineInput();_workspaceSettings=true;}
            else if(id=="studio_refresh")RefreshWorkspaceReview(allowSeekAdoption:true);
            else if(id=="studio_save_view")RequestStudioSaveView();
            else if(id=="studio_seek")
            {
                if(!uint.TryParse(_host.ReadField(_document,"studio_frame"),NumberStyles.None,CultureInfo.InvariantCulture,out uint frame)||frame>_controller.Snapshot().Duration)throw new ArgumentException("Enter a frame inside this recording.");
                long delta=(long)frame-_controller.Snapshot().Frame;if(delta<int.MinValue||delta>int.MaxValue)throw new ArgumentException("Frame jump exceeds this transport's bound.");_controller.Jump((int)delta);
            }
            else if(id.StartsWith("studio_rate_",StringComparison.Ordinal))
            {
                float desired=float.Parse(id[12..],CultureInfo.InvariantCulture);
                for(int i=0;i<12&&_controller.Snapshot().Rate!=desired;i++)_controller.Dispatch(TheatrePlaybackAction.NextRate);
            }
            else if(id.StartsWith("studio_camera_mode_",StringComparison.Ordinal))
            {
                string desired=id[19..];for(int i=0;i<4&&!String.Equals(_controller.Snapshot().Camera,desired,StringComparison.OrdinalIgnoreCase);i++)_controller.Dispatch(TheatrePlaybackAction.NextCamera);
            }
            else if(id.StartsWith("studio_operation_",StringComparison.Ordinal))
            {
                if(_studioToolsTask!=null||_studioCueTask!=null)throw new InvalidOperationException("Cancel or finish the current operation before changing tools.");
                if(!Enum.TryParse<TheatreStudioTool>(id[17..],out var operation)||!Enum.IsDefined(operation))throw new ArgumentException("Unknown Studio operation.");_studioTool=operation;
            }
            else if(id.StartsWith("studio_editor_tab_",StringComparison.Ordinal)&&_editor!=null)
            {
                if(!Enum.TryParse<TheatreEditorTab>(id[18..],out var tabValue)||!Enum.IsDefined(tabValue))throw new ArgumentException("Unknown editor view.");
                for(int i=0;i<5&&_editor.Snapshot().Tab!=tabValue;i++)_editor.NextTab();
            }
            else if(id.StartsWith("studio_export_fps_",StringComparison.Ordinal)&&_exports!=null)
            {int fps=int.Parse(id[18..],CultureInfo.InvariantCulture);for(int i=0;i<7&&_exports.Snapshot().Options.Fps!=fps;i++)_exports.Change("fps");}
            else if(id.StartsWith("studio_export_source_",StringComparison.Ordinal)&&_exports!=null)
            {if(!Enum.TryParse<TheatreExportSource>(id[21..],out var source)||!Enum.IsDefined(source))throw new ArgumentException("Unknown export source.");for(int i=0;i<3&&_exports.Snapshot().Options.Source!=source;i++)_exports.Change("source");}
            else if(id.StartsWith("studio_export_camera_",StringComparison.Ordinal)&&_exports!=null)
            {if(!Enum.TryParse<TheatreExportCamera>(id[21..],out var camera)||!Enum.IsDefined(camera))throw new ArgumentException("Unknown export camera.");for(int i=0;i<5&&_exports.Snapshot().Options.Camera!=camera;i++)_exports.Change("camera");}
            else if(id.StartsWith("studio_export_resolution_",StringComparison.Ordinal)&&_exports!=null)
            {int resolution=int.Parse(id[25..],CultureInfo.InvariantCulture);for(int i=0;i<4&&_exports.Snapshot().Options.Resolution!=resolution;i++)_exports.Change("resolution");}
            else if(id.StartsWith("studio_track_",StringComparison.Ordinal))
            {string track=id[13..];if(!_workspaceHiddenTracks.Add(track))_workspaceHiddenTracks.Remove(track);ReleaseTimelineInput();}
            else if(id=="studio_camera_graph")_host.FocusDocument(_document,"replay_camera_curve_lane_3");
            else if(id.StartsWith("studio_tab_",StringComparison.Ordinal))SelectWorkspaceTab(id[11..]);
            else if(id=="replay_studio")SelectWorkspaceTab("markers");
            else if(!HandleWorkspaceMarkerControl(id)&&!HandleWorkspaceWaveChooser(id)&&!HandleWorkspaceCameraControl(id)&&!HandleWorkspaceCombatControl(id)&&!HandleWorkspaceComparisonControl(id))HandleWorkspaceProjectControl(id);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException){_workspaceProblem=error.Message;}
        Present();return true;
    }
    private bool WorkspaceTrackVisible(string name)=>!WorkspaceMode||!_workspaceHiddenTracks.Contains(name);
    private uint WorkspaceTimelineFrame(uint reviewed)=>WorkspaceMode?_controller.Snapshot().Frame:reviewed;
    private void PresentWorkspace()
    {
        if(!WorkspaceMode)return;
        foreach(string tab in new[]{"camera","markers","combat","analysis","audio","export"})
        {_host.SetBool(_document,"visible:studio_inspector_"+tab,_workspaceTab==tab);_host.SetBool(_document,"class:studio_tab_"+tab+":selected",_workspaceTab==tab);}
        _host.SetText(_document,"studio_notice",_workspaceProblem.Length>0?_workspaceProblem:"Pause to save edits. Refresh updates the reviewed frame without changing recording bytes.");
        foreach(string lane in new[]{"camera","events","bookmarks","ranges","reel","kills","damage","shots","spawns","objectives","audio","cuts","exportrange"})
        {_host.SetBool(_document,"visible:studio_track_wrap_"+lane,!_workspaceHiddenTracks.Contains(lane));_host.SetBool(_document,"class:studio_track_"+lane+":selected",!_workspaceHiddenTracks.Contains(lane));}
        var state=_controller.Snapshot();
        if(state.PlayAction&&_cameraTools!=null&&_cameraTools.Snapshot().Context.Camera.Frame!=state.Frame&&_cameraTools.Snapshot().Pending==null
            &&_editor?.Snapshot().Pending==null&&_cameraToolsConfirmation==default&&_editorConfirmation==default&&_timeline?.Dragging!=true)
        {try{RefreshWorkspaceReview();}catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException){_workspaceProblem=error.Message;}}
        if(_host.FocusedElement()!="studio_frame")_host.SetField(_document,"studio_frame",state.Frame.ToString(CultureInfo.InvariantCulture));
        PresentWorkspaceCameraControls(_document);PresentWorkspaceCombatControls(_document);PresentWorkspaceComparisonControls();TickWorkspaceProjectFiles();
        PresentWorkspaceMarkerControls(_document);TickWorkspaceWaveFields();
    }
}
#endif
