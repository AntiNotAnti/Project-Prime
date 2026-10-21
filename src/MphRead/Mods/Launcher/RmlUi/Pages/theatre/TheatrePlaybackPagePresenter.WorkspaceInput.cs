#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.Core;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private sealed record WorkspaceScrub(TheatreCameraToolsContext Camera,TheatreEditorContext Editor,TimelineMetrics Metrics,TimelineWindow Window,TimelineBox Bounds,uint Start,uint End,bool Range);
    private WorkspaceScrub? _workspaceScrub;
    partial void RetireWorkspaceInput()=>_workspaceScrub=null;
    internal bool TimelineInputAt(double x,double y,TimelineMetrics metrics)
    {var p=metrics.Pixels(x,y);return TimelineOpen&&(_workspaceScrub!=null||_timeline?.Dragging==true||TimelineBounds("replay_timeline_panel",out var bounds)&&bounds.Contains(p.X,p.Y));}
    internal bool CameraCurveInputAt(double x,double y,TimelineMetrics metrics)
    {
        if(!CameraCurvesOpen)return false;if(_curveDrag!=null||_curveBox!=null)return true;
        var p=metrics.Pixels(x,y);if(!CurveClip(metrics,out var clip)||!clip.Contains(p.X,p.Y))return false;
        for(int i=0;i<5;i++)if(CurveBounds("replay_camera_curve_lane_"+i,out var lane)&&lane.Contains(p.X,p.Y))return true;
        return false;
    }
    internal bool StudioTimelinePointerButton(int button,double x,double y,bool down,TimelineMetrics metrics,bool focused,bool shift)
    {
        if(!WorkspaceMode||button!=0||_timeline==null||_cameraTools==null||_timelineLanes==null)return false;
        if(down)
        {
            _workspaceScrub=null;
            if(!focused||_pages.Top!=_document||!_timeline.PresentedCurrent(_cameraTools.Snapshot(),_document,metrics,true)
                ||!TimelineBounds("replay_timeline_surface",out var bounds))return false;
            var p=metrics.Pixels(x,y);if(!bounds.Contains(p.X,p.Y))return false;
            string id=_host.FocusedElement();
            if(id.StartsWith("replay_timeline_key_",StringComparison.Ordinal)||id.StartsWith("replay_timeline_events_",StringComparison.Ordinal)
                ||id.StartsWith("replay_timeline_bookmarks_",StringComparison.Ordinal)||id.StartsWith("replay_timeline_ranges_",StringComparison.Ordinal)
                ||id.StartsWith("replay_timeline_reel_",StringComparison.Ordinal)||id.StartsWith("replay_timeline_kills_",StringComparison.Ordinal)
                ||id.StartsWith("replay_timeline_damage_",StringComparison.Ordinal)||id.StartsWith("replay_timeline_shots_",StringComparison.Ordinal)
                ||id.StartsWith("replay_timeline_spawns_",StringComparison.Ordinal)||id.StartsWith("replay_timeline_objectives_",StringComparison.Ordinal)
                ||id.StartsWith("replay_timeline_audio_",StringComparison.Ordinal)||id.StartsWith("replay_timeline_cuts_",StringComparison.Ordinal))return false;
            if(!_host.FocusDocument(_document,"replay_timeline_surface")||!_cameraTools.ValidateReview())return false;
            uint frame=_timeline.Window.Frame(p.X,bounds);
            _workspaceScrub=new(_cameraTools.Snapshot().Context,_timelineLanes.Context,metrics,_timeline.Window,bounds,frame,frame,shift);return true;
        }
        if(_workspaceScrub is not{} drag)return false;_workspaceScrub=null;
        try
        {
            if(!focused||_pages.Top!=_document||_host.FocusedElement()!="replay_timeline_surface"||drag.Metrics!=metrics
                ||!TimelineBounds("replay_timeline_surface",out var bounds)||bounds!=drag.Bounds||!ReferenceEquals(drag.Camera,_cameraTools.Snapshot().Context)
                ||!_cameraTools.ValidateReview())return true;
            var p=metrics.Pixels(x,y);uint frame=drag.Window.Frame(p.X,bounds);
            if(drag.Range)
            {
                if(_editorBackend is not ITheatreWorkspaceTimelineBackend backend)throw new InvalidOperationException("Range selection is unavailable for this replay owner.");
                backend.SetReviewedTimelineRange(drag.Editor,Math.Min(drag.Start,frame),Math.Max(drag.Start,frame));RefreshWorkspaceReview();
            }
            else _controller.Jump(checked((int)((long)frame-_controller.Snapshot().Frame)));
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException){_workspaceProblem=error.Message;}
        return true;
    }
    internal bool StudioTimelinePointerMove(double x,double y,TimelineMetrics metrics,bool focused)
    {
        if(_workspaceScrub is not{} drag)return false;
        if(!focused||metrics!=drag.Metrics||_pages.Top!=_document||_host.FocusedElement()!="replay_timeline_surface"
            ||!TimelineBounds("replay_timeline_surface",out var bounds)||bounds!=drag.Bounds){_workspaceScrub=null;return true;}
        var p=metrics.Pixels(x,y);uint frame=drag.Window.Frame(p.X,bounds);_workspaceScrub=drag with{End=frame};
        _host.SetText(_document,"studio_notice",drag.Range?$"SELECT RANGE {Math.Min(frame,drag.Start)}–{Math.Max(frame,drag.Start)}":$"SEEK FRAME {frame} ON RELEASE");return true;
    }
    internal bool StudioTimelineWheel(double x,double y,double delta,bool shift,TimelineMetrics metrics,bool focused)
    {
        if(!WorkspaceMode||!TimelineInputAt(x,y,metrics)||!focused||_pages.Top!=_document||_timeline==null||_cameraTools==null||delta==0)return false;
        ReleaseTimelineInput();if(shift)_timeline.Pan(delta>0?-1:1);else _timeline.ZoomBy(delta>0?1:-1);PresentTimeline();return true;
    }
    internal bool StudioTimelineShortcut(string command)
    {
        // Replay transport shortcuts belong to the foreground Studio workspace,
        // not only its viewport and timeline. A focused button (including
        // LIBRARY) must not consume Space as a click, but numeric/text fields
        // and modal dialogs retain their normal keyboard interaction.
        if(!WorkspaceMode||!IsOpen||_pages.Top!=_document||_host.CurrentInputDocument!=_document
            ||_host.TextInputActive)return false;
        var state=_controller.Snapshot();
        switch(command)
        {
            case "play":_controller.Dispatch(TheatrePlaybackAction.TogglePause);break;
            // Keyboard navigation should preserve the playback state; mouse
            // scrubs and explicit numeric seeks still pause by default.
            case "previous-frame":_controller.Jump(-1,resume:null);break;case "next-frame":_controller.Jump(1,resume:null);break;
            case "previous-second":_controller.Jump(-60,resume:null);break;case "next-second":_controller.Jump(60,resume:null);break;
            case "home":_controller.Dispatch(TheatrePlaybackAction.Restart);break;
            case "end":_controller.Jump(checked((int)((long)state.Duration-state.Frame)));break;
            default:return false;
        }
        return true;
    }
}
#endif
