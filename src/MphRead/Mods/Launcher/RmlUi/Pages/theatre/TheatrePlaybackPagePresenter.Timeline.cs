#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreTimelineViewState? _timeline;
    private TheatreTimelineLanes? _timelineLanes;
    private RmlUiDocumentToken _timelineModal;
    private TimelineMetrics _timelineMetrics;
    private string _timelineProblem="",_timelinePublicationNotice="";
    private (uint Key,double X,double Y)? _timelinePress;
    private ImmutableArray<TheatreCameraKey> _timelineVisible=ImmutableArray<TheatreCameraKey>.Empty;
    private ImmutableArray<TimelineLaneGlyph>[] _timelineRows=new ImmutableArray<TimelineLaneGlyph>[Enum.GetValues<TimelineLane>().Length];
    internal bool TimelineOpen=>_timelineModal!=default&&_host.IsAlive(_timelineModal);
    private bool HandleTimelineIntent(in RmlUiIntent intent)
    {
        if(!IsOpen||intent.Kind!=RmlUiIntentKind.ReplayAction||_cameraTools==null||_editorBackend==null
            ||!Enum.IsDefined((TheatrePlaybackAction)intent.Argument))return false;
        var action=(TheatrePlaybackAction)intent.Argument;
        bool opening=action==TheatrePlaybackAction.TimelineOpen&&_timelineModal==default&&intent.Document==_cameraToolsModal;
        bool editing=_timelineModal!=default&&intent.Document==_timelineModal&&_pages.Top==_timelineModal;
        if((!opening&&!editing)||!_pages.Accept(intent))return false;
        try
        {
            _timelineProblem="";
            if(opening)
            {
                _timeline=new();RefreshTimeline();
                _timelineModal=_pages.OpenModal(new("replay-timeline","pages/theatre/timeline.rml","replay_timeline_refresh"));
                _host.SetField(_timelineModal,"replay_timeline_frame",_cameraTools.Snapshot().Context.Camera.Frame.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                _timelinePress=null;_timeline?.Cancel();
                switch(action)
                {
                    case TheatrePlaybackAction.TimelineRefresh:RefreshTimeline();break;
                    case TheatrePlaybackAction.TimelineZoomIn:_timeline?.ZoomBy(1);break;
                    case TheatrePlaybackAction.TimelineZoomOut:_timeline?.ZoomBy(-1);break;
                    case TheatrePlaybackAction.TimelinePanLeft:_timeline?.Pan(-1);break;
                    case TheatrePlaybackAction.TimelinePanRight:_timeline?.Pan(1);break;
                    case TheatrePlaybackAction.TimelineCenter:_timeline?.Center();break;
                    case TheatrePlaybackAction.TimelineReset:_timeline?.Reset();break;
                    case TheatrePlaybackAction.TimelineCancelDrag:ReleaseTimelineInput();break;
                    case TheatrePlaybackAction.TimelineClose:CloseTimeline();break;
                    case TheatrePlaybackAction.TimelineActivate:ActivateTimeline();break;
                    case TheatrePlaybackAction.CameraToolsToggleSelection:_cameraTools.ToggleSelection();break;
                    case TheatrePlaybackAction.CameraToolsRangeSelection:_cameraTools.SelectRange();break;
                    case TheatrePlaybackAction.CameraToolsSelectAll:_cameraTools.SelectAll();break;
                    case TheatrePlaybackAction.CameraToolsClearSelection:_cameraTools.ClearSelection();break;
                    case TheatrePlaybackAction.CameraToolsSnapEvent:StageTimelineCamera(TheatreCameraToolsChange.SnapEvent);break;
                    case TheatrePlaybackAction.CameraToolsSnapKill:StageTimelineCamera(TheatreCameraToolsChange.SnapKill);break;
                    case TheatrePlaybackAction.CameraToolsSnapBookmark:StageTimelineCamera(TheatreCameraToolsChange.SnapBookmark);break;
                    default:throw new InvalidOperationException("This camera command is not available from the timeline.");
                }
            }
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_timelineProblem=error.Message;ReleaseTimelineInput();}
        PresentTimeline();return true;
    }
    private void RefreshTimeline()
    {
        if(_cameraTools==null||_timeline==null||_editorBackend==null||!_cameraTools.Refresh())
            throw new InvalidOperationException(_cameraTools?.Snapshot().Error??"Camera review is unavailable.");
        var snapshot=_cameraTools.Snapshot();
        var lanes=new TheatreTimelineLanes(snapshot.Context,_editorBackend.Capture());
        if(!_cameraTools.ValidateReview())throw new TheatreCameraStaleException(_cameraTools.Snapshot().Error);
        _timelineLanes=lanes;_timeline.Review(snapshot);_timelinePress=null;_timelinePublicationNotice="";
    }
    private void ActivateTimeline()
    {
        if(_timeline==null||_cameraTools==null||_timelineLanes==null)return;
        string id=_host.FocusedElement();var snapshot=_cameraTools.Snapshot();
        if(!TimelineClip(_timelineMetrics,out var clip)||!TimelineBounds(id,out var actualControl))
            throw new TheatreCameraStaleException("Wait for this exact visible native control to be presented before activating it.");
        if(id=="replay_timeline_seek")
        {
            if(_pages.Top!=_timelineModal||_editorBackend is not ITheatreTimelineSeek seek
                ||!_timeline.SeekPresentedCurrent(snapshot,_timelineModal,_timelineMetrics,_host.CurrentInputDocument==_timelineModal,actualControl,clip))
                throw new TheatreCameraStaleException("Exact timeline seek is unavailable for this owner.");
            if(!uint.TryParse(_host.ReadField(_timelineModal,"replay_timeline_frame"),NumberStyles.None,CultureInfo.InvariantCulture,out uint frame)
                ||frame>snapshot.Context.Camera.Duration)throw new ArgumentException("Enter a whole frame within this reviewed replay.");
            if(!_cameraTools.ValidateReview())throw new TheatreCameraStaleException(_cameraTools.Snapshot().Error);
            seek.SeekTimelineFrame(_timelineLanes.Context,frame);_timeline.Invalidate("Seeking the exact reviewed frame. Refresh after the new picture settles.");return;
        }
        if(!_timeline.ActivationBoundsCurrent(id,actualControl,clip))
            throw new TheatreCameraStaleException("This native marker moved or scrolled after its last successful picture. Wait for the new picture.");
        var target=_timeline.Activate(id,snapshot,_timelineModal,_timelineMetrics,_pages.Top==_timelineModal&&_host.CurrentInputDocument==_timelineModal);
        if(target==null)throw new InvalidOperationException("Wait for this exact native timeline marker to be presented before activating it.");
        if(!_cameraTools.ValidateReview())throw new TheatreCameraStaleException(_cameraTools.Snapshot().Error);
        if(target.Key is uint key)
        {_cameraTools.FocusSavedFrame(key);return;}
        if(target.Lane is not{} glyph)return;
        if(glyph.Count>1){_timeline.InspectBin(glyph);return;}
        if(glyph.Exact==null)throw new InvalidOperationException("This timeline bin has no single reviewed source entry.");
        _editorBackend!.Seek(_timelineLanes.Context,glyph.Exact,selectRange:glyph.Exact.EndFrame>glyph.Exact.Frame);
        _timeline.Invalidate("Seeking the exact reviewed entry. Refresh after seeking settles.");
    }
    private void StageTimelineCamera(TheatreCameraToolsChange change)
    {
        if(_cameraTools?.ValidateReview()!=true)return;
        if(_cameraTools.Stage(change))
        {
            _timeline?.Invalidate("Review the existing exact full-track confirmation.");
            _cameraToolsConfirmation=_pages.OpenModal(new("replay-camera-tools-confirm","pages/theatre/camera-tools-confirm.rml","replay_camera_tools_confirm_cancel"));
            PresentCameraTools();
        }
    }
    private void PresentTimeline()
    {
        if(_timelineModal!=default&&!_host.IsAlive(_timelineModal)){_timelineModal=default;_timeline=null;_timelineLanes=null;_timelinePress=null;}
        if(!TimelineOpen||_timeline==null||_cameraTools==null)return;
        var snapshot=_cameraTools.Snapshot();
        if(_pages.Top!=_timelineModal||!snapshot.Current||snapshot.Pending!=null){_timelinePress=null;_timeline.Invalidate("Timeline review is inactive under another modal or changed replay picture.");}
        var window=_timeline.Window;var camera=snapshot.Context.Camera;uint liveFrame=WorkspaceTimelineFrame(camera.Frame);
        _host.SetText(_timelineModal,"replay_timeline_window",$"FRAMES {window.First} — {window.Last} // {_timeline.Zoom}× ZOOM // PLAYHEAD {liveFrame}");
        _host.SetText(_timelineModal,"replay_timeline_focus",snapshot.Focus==null?"NO CAMERA KEYS":$"FOCUSED KEY {snapshot.Focus.Frame}. Toggle or range selects saved keys; dragging moves the selected group.");
        _host.SetText(_timelineModal,"replay_timeline_selection","SELECTED: "+(snapshot.Selection.IsEmpty?"NONE":string.Join(", ",snapshot.Selection)));
        _host.SetText(_timelineModal,"replay_timeline_status",_timelinePublicationNotice.Length>0?_timelinePublicationNotice:_timeline.Notice);
        _host.SetText(_timelineModal,"replay_timeline_error",_timelineProblem.Length>0?_timelineProblem:snapshot.Error);
        _host.SetBool(_timelineModal,"disabled:replay_timeline_seek",!snapshot.Current||_editorBackend is not ITheatreTimelineSeek);
        foreach(string name in new[]{"toggle","range","all","clear","snap_event","snap_kill","snap_bookmark"})
            _host.SetBool(_timelineModal,"disabled:replay_timeline_"+name,!snapshot.Current||snapshot.Context.Camera.Keys.IsEmpty);
        _timelineVisible=camera.Keys.Where(key=>key.Frame>=window.First&&key.Frame<=window.Last).ToImmutableArray();
        if(TimelineBounds("replay_timeline_camera",out var lane))
        {
            for(int i=0;i<64;i++)
            {
                string id="replay_timeline_key_"+i;bool visible=i<_timelineVisible.Length;_host.SetBool(_timelineModal,id,visible);
                if(visible)
                {
                    var key=_timelineVisible[i];double x=window.X(key.Frame,new(0,0,lane.Width,lane.Height));
                    _host.SetText(_timelineModal,"rect:"+id,Rect(Math.Clamp(x-5,0,Math.Max(0,lane.Width-10)),2,10,Math.Max(1,lane.Height-4)));
                    _host.SetText(_timelineModal,id,"Camera key frame "+key.Frame);
                    _host.SetText(_timelineModal,"color:"+id,snapshot.Focus?.Frame==key.Frame?"#e9db94":snapshot.Selection.Contains(key.Frame)?"#6fb7c8":"#25435b");
                    _host.SetBool(_timelineModal,"disabled:"+id,!snapshot.Current);
                }
                string ghost="replay_timeline_ghost_"+i;bool draw=i<_timeline.Preview.Length&&_timeline.Preview[i]>=window.First&&_timeline.Preview[i]<=window.Last;
                _host.SetBool(_timelineModal,ghost,draw);
                if(draw)
                {
                    double x=window.X(_timeline.Preview[i],new(0,0,lane.Width,lane.Height));
                    _host.SetText(_timelineModal,"rect:"+ghost,Rect(Math.Clamp(x-5,0,Math.Max(0,lane.Width-10)),0,10,lane.Height));
                    _host.SetText(_timelineModal,"color:"+ghost,_timeline.PreviewValid?"#ffffff":"#e34f42");
                }
            }
            _host.SetBool(_timelineModal,"replay_timeline_playhead",camera.Frame>=window.First&&camera.Frame<=window.Last);
            _host.SetText(_timelineModal,"rect:replay_timeline_playhead",Rect(window.X(liveFrame,new(0,0,lane.Width,lane.Height)),0,1,lane.Height));
        }
        for(int kind=0;kind<_timelineRows.Length;kind++)
        {
            string name=((TimelineLane)kind).ToString().ToLowerInvariant();
            var rows=_timelineLanes?.Project(window,(TimelineLane)kind)??ImmutableArray<TimelineLaneGlyph>.Empty;_timelineRows[kind]=rows;
            _host.SetText(_timelineModal,"replay_timeline_"+name+"_count",WorkspaceMode?$"{name.ToUpperInvariant()} {rows.Sum(row=>row.Count)}":$"{name.ToUpperInvariant()} // {rows.Sum(row=>row.Count)} REVIEWED ENTRIES");
            if(!TimelineBounds("replay_timeline_"+name,out var area))continue;
            for(int i=0;i<64;i++)
            {
                string id=$"replay_timeline_{name}_{i}";bool visible=i<rows.Length;_host.SetBool(_timelineModal,id,visible);if(!visible)continue;
                var row=rows[i];double left=window.X(row.First,new(0,0,area.Width,area.Height));double right=window.X(row.Last,new(0,0,area.Width,area.Height));
                _host.SetText(_timelineModal,"rect:"+id,Rect(Math.Clamp(left-4,0,Math.Max(0,area.Width-8)),2,Math.Min(area.Width,Math.Max(8,right-left)),Math.Max(1,area.Height-4)));
                _host.SetText(_timelineModal,id,row.Label+" // FRAME "+row.First+(row.Last==row.First?"":" — "+row.Last));
                _host.SetBool(_timelineModal,"disabled:"+id,!snapshot.Current);
            }
        }
    }
    internal TimelineRenderTicket? PrepareTimeline(TimelineMetrics metrics,bool windowFocused)
    {
        _timelineMetrics=metrics;
        if(_timeline?.Dragging==true&&_host.FocusedElement()!="replay_timeline_surface")ReleaseTimelineInput();
        if(!TimelineOpen||_timeline==null||_cameraTools==null)return null;
        bool cameraVisible=WorkspaceTrackVisible("camera");TimelineBox area;
        if(!(cameraVisible?TimelineBounds("replay_timeline_camera",out area):TimelineBounds("replay_timeline_surface",out area))||!TimelineClip(metrics,out var clip))
        {ReleaseTimelineInput();return null;}
        var keys=ImmutableArray.CreateBuilder<TimelineKeyBox>();
        for(int i=0;cameraVisible&&i<_timelineVisible.Length;i++)
            if(TimelineBounds("replay_timeline_key_"+i,out var key))keys.Add(new(_timelineVisible[i].Frame,key));
        var lanes=ImmutableArray.CreateBuilder<TimelineLaneButton>();
        for(int kind=0;kind<_timelineRows.Length;kind++)
            for(int i=0;!_timelineRows[kind].IsDefault&&i<_timelineRows[kind].Length;i++)
            {
                string id=$"replay_timeline_{((TimelineLane)kind).ToString().ToLowerInvariant()}_{i}";
                if(TimelineBounds(id,out var box)&&box.Inside(clip))lanes.Add(new(id,box,_timelineRows[kind][i]));
            }
        return _timeline.Prepare(_cameraTools.Snapshot(),_timelineModal,metrics,area,keys.ToImmutable(),
            windowFocused&&_pages.Top==_timelineModal&&_host.CurrentInputDocument==_timelineModal,lanes.ToImmutable(),clip,
            TimelineBounds("replay_timeline_seek",out var seek)?seek:null,cameraVisible);
    }
    private bool TimelineClip(TimelineMetrics metrics,out TimelineBox clip)
    {
        clip=default;
        if(!metrics.Valid||!TimelineBounds("replay_timeline_panel",out var panel)||!panel.Bounded)return false;
        // Existing bounds ABI gives transformed border boxes, not native clipping regions.
        // This authored scroll container has a 1dp border (timeline.rcss); its inner
        // border box intersected with the framebuffer conservatively bounds its children.
        double border=metrics.Density,left=Math.Max(0,panel.X+border),top=Math.Max(0,panel.Y+border);
        double right=Math.Min(metrics.FramebufferWidth,panel.X+panel.Width-border);
        double bottom=Math.Min(metrics.FramebufferHeight,panel.Y+panel.Height-border);
        if(WorkspaceMode&&TimelineBounds("replay_timeline_surface",out var surface))
        {left=Math.Max(left,surface.X+border);top=Math.Max(top,surface.Y+border);right=Math.Min(right,surface.X+surface.Width-border);bottom=Math.Min(bottom,surface.Y+surface.Height-border);}
        clip=new(left,top,right-left,bottom-top);return clip.Valid;
    }
    internal bool TimelinePresented(TimelineRenderTicket ticket,TimelineMetrics metrics,bool successful)
    {
        if(_cameraTools?.Snapshot() is not{Current:true}||_pages.Top!=_timelineModal||_host.CurrentInputDocument!=_timelineModal)
        {ReleaseTimelineInput();return false;}
        return _timeline?.Presented(ticket,_timelineModal,metrics,successful)==true;
    }
    partial void RetireWorkspaceInput();
    internal void ReleaseTimelineInput(){RetireWorkspaceInput();ReleaseCameraCurveInput();_timelinePress=null;_timeline?.Invalidate("Timeline input focus or frame ownership retired. Saved keys are unchanged.");}
    internal bool TimelinePointerButton(int button,double x,double y,bool down,TimelineMetrics metrics,bool windowFocused)
    {
        if(!TimelineOpen||_timeline==null||_cameraTools==null||button!=0)return false;
        bool focused=windowFocused&&_pages.Top==_timelineModal&&_host.CurrentInputDocument==_timelineModal;
        if(down)
        {
            // Ordinary native buttons still own their one click/Enter action.
            // Unrelated pointer movement or Go clicks must not retire a valid
            // semantic-only ticket merely because the camera lane is scrolled away.
            if(!TimelinePointerGeometryCurrent(metrics))
            {_timelinePress=null;if(_timeline.Dragging)ReleaseTimelineInput();return false;}
            var picked=_timeline.NativeKeyAt(_host.FocusedElement(),x,y,_cameraTools.Snapshot(),_timelineModal,metrics,focused);
            _timelinePress=picked is uint frame?(frame,x,y):null;return false; // Native click/Enter remains the only marker activation.
        }
        _timelinePress=null;if(!_timeline.Dragging)return false;
        if(!TimelinePointerGeometryCurrent(metrics)){ReleaseTimelineInput();return false;}
        var proposal=_timeline.Release(x,y,_cameraTools.Snapshot(),_timelineModal,metrics,focused&&_host.FocusedElement()=="replay_timeline_surface");
        if(proposal!=null)
        {
            if(!_cameraTools.ValidateReview()){_timelineProblem=_cameraTools.Snapshot().Error;return true;}
            var state=_cameraTools.Snapshot();
            if(!ReferenceEquals(proposal.Context,state.Context)||!proposal.Selection.SequenceEqual(state.Selection))return true;
            if(_cameraTools.Destination(proposal.Destination.ToString(CultureInfo.InvariantCulture)))StageTimelineCamera(TheatreCameraToolsChange.Move);
        }
        PresentTimeline();return true;
    }
    internal void TimelinePointerMove(double x,double y,TimelineMetrics metrics,bool windowFocused)
    {
        if(!TimelineOpen||_timeline==null||_cameraTools==null)return;
        if(_timelinePress==null&&!_timeline.Dragging)return;
        if(!TimelinePointerGeometryCurrent(metrics)){ReleaseTimelineInput();return;}
        bool focused=windowFocused&&_pages.Top==_timelineModal&&_host.CurrentInputDocument==_timelineModal;
        if(_timelinePress is{} press&&!_timeline.Dragging)
        {
            if(Math.Abs((x-press.X)*metrics.FramebufferWidth/metrics.ClientWidth)<4)return;
            if(!_cameraTools.ValidateReview()||!_cameraTools.FocusSavedFrame(press.Key)){ReleaseTimelineInput();return;}
            if(!_cameraTools.Snapshot().Selection.Contains(press.Key)){_cameraTools.ClearSelection();_cameraTools.ToggleSelection();}
            if(!_host.FocusDocument(_timelineModal,"replay_timeline_surface")
                ||!TimelinePointerGeometryCurrent(metrics)
                ||!_timeline.Begin(press.Key,press.X,press.Y,_cameraTools.Snapshot(),_timelineModal,metrics,focused)){ReleaseTimelineInput();return;}
        }
        if(_timeline.Dragging)_timeline.Move(x,y,_cameraTools.Snapshot(),_timelineModal,metrics,focused&&_host.FocusedElement()=="replay_timeline_surface");
        PresentTimeline();
    }
    private bool TimelinePointerGeometryCurrent(TimelineMetrics metrics)=>_timeline!=null
        &&TimelineBounds("replay_timeline_camera",out var camera)&&TimelineClip(metrics,out var clip)
        &&_timeline.PointerBoundsCurrent(camera,clip);
    private bool TimelineBounds(string id,out TimelineBox box)
    {box=default;if(!_host.TryGetElementBounds(_timelineModal,id,out float x,out float y,out float w,out float h))return false;box=new(x,y,w,h);return true;}
    private static string Rect(double x,double y,double w,double h)=>string.Create(CultureInfo.InvariantCulture,$"{x:0.###},{y:0.###},{w:0.###},{h:0.###}");
    private bool BackTimeline()
    {if(WorkspaceMode&&!_workspaceClosing&&_cameraToolsConfirmation==default)return false;if(!TimelineOpen)return false;if(_cameraToolsConfirmation!=default)return BackCameraTools();CloseTimeline();return true;}
    private bool CloseTimeline()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        ReleaseTimelineInput();
        if(_cameraToolsConfirmation!=default&&_host.IsAlive(_cameraToolsConfirmation))
        {if(!_pages.CloseModal())return false;_cameraToolsConfirmation=default;_cameraTools?.Cancel();}
        if(_timelineModal!=default&&_timelineModal!=_document&&_host.IsAlive(_timelineModal)&&!_pages.CloseModal())return false;
        _timelineModal=default;_timeline=null;_timelineLanes=null;_timelineProblem="";_timelinePublicationNotice="";return true;
    }
}
#endif
