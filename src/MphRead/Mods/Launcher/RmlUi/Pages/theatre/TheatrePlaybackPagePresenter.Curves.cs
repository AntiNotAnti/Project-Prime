#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
internal sealed record CameraCurveControl(string Id,uint Frame,int Channel,int Handle,TimelineBox Box,TimelineBox Lane,float Minimum,float Maximum);
internal sealed record CameraCurveLane(int Channel,TimelineBox Box);
internal sealed record CameraCurveTicket(RmlUiDocumentToken Document,TheatreCameraToolsContext Context,TimelineMetrics Metrics,TimelineBox Clip,ImmutableArray<CameraCurveControl> Controls,ImmutableArray<CameraCurveLane> Lanes=default);
public sealed partial class TheatrePlaybackPagePresenter
{
    private readonly (float Min,float Max)[] _curveRanges=new (float,float)[5];
    private CameraCurveTicket? _preparedCurves,_presentedCurves;
    private sealed record CurveDrag(CameraCurveTicket Ticket,CameraCurveControl Control,double X,double Y,ImmutableArray<uint> Selection,bool Moving,float Delta);
    private CurveDrag? _curveDrag;
    private sealed record CurveBoxDrag(CameraCurveTicket Ticket,int Channel,double X,double Y,double EndX,double EndY,ImmutableArray<uint> Selection);
    private CurveBoxDrag? _curveBox;
    internal bool CameraCurvesOpen=>(!WorkspaceMode||_workspaceTab=="camera")&&_cameraToolsModal!=default&&_host.IsAlive(_cameraToolsModal)&&_pages.Top==_cameraToolsModal;
    internal bool GraphInputOpen=>TimelineOpen||CameraCurvesOpen;
    private static float CurveValue(TheatreCameraPose pose,int channel)=>channel switch{0=>pose.X,1=>pose.Y,2=>pose.Z,3=>pose.Fov,_=>pose.Roll};
    private static float HandleValue(TheatreCameraKey key,TheatreCameraToolsContext context,int channel,int handle)
    {
        var p=key.Pose;var h=p.Handles;
        if(channel>=3)return CurveValue(p,channel)+((channel==3?(handle<0?h?.FovIn:h?.FovOut):(handle<0?h?.RollIn:h?.RollOut))??0);
        var v=handle<0?h?.Incoming:h?.Outgoing;
        if(v==null)
        {
            var neighbour=handle<0?context.Camera.Keys.LastOrDefault(k=>k.Frame<key.Frame):context.Camera.Keys.FirstOrDefault(k=>k.Frame>key.Frame);
            return CurveValue(p,channel)+(neighbour==null?0:(CurveValue(neighbour.Pose,channel)-CurveValue(p,channel))/3);
        }
        return CurveValue(p,channel)+(channel==0?v.X:channel==1?v.Y:v.Z);
    }
    private bool CurveBounds(string id,out TimelineBox box)
    {box=default;if(!_host.TryGetElementBounds(_cameraToolsModal,id,out float x,out float y,out float width,out float height))return false;box=new(x,y,width,height);return box.Bounded;}
    private bool CurveClip(TimelineMetrics metrics,out TimelineBox clip)
    {
        clip=default;if(!metrics.Valid||!CurveBounds(WorkspaceMode?"studio_inspector_scroll":"replay_camera_tools_panel",out var panel))return false;
        double border=metrics.Density,left=Math.Max(0,panel.X+border),top=Math.Max(0,panel.Y+border);
        double right=Math.Min(metrics.FramebufferWidth,panel.X+panel.Width-border),bottom=Math.Min(metrics.FramebufferHeight,panel.Y+panel.Height-border);
        clip=new(left,top,right-left,bottom-top);return clip.Valid;
    }
    private void PresentCameraCurveKeys(TheatreCameraToolsSnapshot state)
    {
        var context=state.Context;
        for(int channel=0;channel<5;channel++)
        {
            string laneId="replay_camera_curve_lane_"+channel;if(!CurveBounds(laneId,out var lane))continue;
            string selectionId="replay_camera_curve_selection_"+channel;
            bool selectionVisible=false;
            if(_curveBox is{} selecting)
            {
                var rectangle=CurveSelectionRectangle(selecting);
                double left=Math.Max(rectangle.X,lane.X),top=Math.Max(rectangle.Y,lane.Y);
                double right=Math.Min(rectangle.X+rectangle.Width,lane.X+lane.Width),bottom=Math.Min(rectangle.Y+rectangle.Height,lane.Y+lane.Height);
                selectionVisible=right>left&&bottom>top;
                if(selectionVisible)_host.SetText(_cameraToolsModal,"rect:"+selectionId,Rect(left-lane.X,top-lane.Y,right-left,bottom-top));
            }
            _host.SetBool(_cameraToolsModal,"visible:"+selectionId,selectionVisible);
            var range=_curveRanges[channel];
            double Y(float value)=>Math.Clamp((1-(value-range.Min)/Math.Max(.00001f,range.Max-range.Min))*(lane.Height-8),0,Math.Max(0,lane.Height-8));
            double X(uint frame)=>Math.Clamp(frame/(double)Math.Max(1u,context.Camera.Duration)*(lane.Width-8),0,Math.Max(0,lane.Width-8));
            for(int i=0;i<64;i++)
            {
                string id=$"replay_camera_curve_key_{channel}_{i}";bool visible=i<context.Camera.Keys.Length;
                _host.SetBool(_cameraToolsModal,"visible:"+id,visible);if(!visible)continue;
                var key=context.Camera.Keys[i];_host.SetBool(_cameraToolsModal,"class:"+id+":selected",state.Selection.Contains(key.Frame));_host.SetText(_cameraToolsModal,"rect:"+id,Rect(X(key.Frame),Y(CurveValue(key.Pose,channel)),8,8));
                _host.SetText(_cameraToolsModal,id,$"Channel {channel} authored key frame {key.Frame}");_host.SetBool(_cameraToolsModal,"disabled:"+id,!state.Current||state.Pending!=null);
            }
            foreach(int handle in new[]{-1,1})
            {
                string id=$"replay_camera_curve_{(handle<0?"in":"out")}_{channel}";_host.SetBool(_cameraToolsModal,"visible:"+id,state.Focus!=null);
                if(state.Focus is not{} key)continue;
                double x=Math.Clamp(X(key.Frame)+handle*16,0,Math.Max(0,lane.Width-8));
                _host.SetText(_cameraToolsModal,"rect:"+id,Rect(x,Y(HandleValue(key,context,channel,handle)),8,8));
                _host.SetText(_cameraToolsModal,id,$"Channel {channel} {(handle<0?"incoming":"outgoing")} Bezier handle frame {key.Frame}");_host.SetBool(_cameraToolsModal,"disabled:"+id,!state.Current||state.Pending!=null);
            }
            string ghost="replay_camera_curve_ghost_"+channel;bool preview=_curveDrag is{Moving:true} drag&&drag.Control.Channel==channel;
            _host.SetBool(_cameraToolsModal,"visible:"+ghost,preview);
            if(preview&&_curveDrag is{} held)
            {
                var key=context.Camera.Keys.First(k=>k.Frame==held.Control.Frame);
                float value=(held.Control.Handle==0?CurveValue(key.Pose,channel):HandleValue(key,context,channel,held.Control.Handle))+held.Delta;
                double x=held.Control.Box.X-lane.X;_host.SetText(_cameraToolsModal,"rect:"+ghost,Rect(x,Y(value),8,8));
            }
        }
    }
    internal CameraCurveTicket? PrepareCameraCurves(TimelineMetrics metrics,bool focused)
    {
        _preparedCurves=null;
        if(!CameraCurvesOpen||!focused||_host.CurrentInputDocument!=_cameraToolsModal||_cameraTools?.Snapshot() is not{Current:true,Pending:null} state
            ||!CurveClip(metrics,out var clip))return null;
        var controls=ImmutableArray.CreateBuilder<CameraCurveControl>();
        var lanes=ImmutableArray.CreateBuilder<CameraCurveLane>();
        for(int channel=0;channel<5;channel++)
        {
            if(!CurveBounds("replay_camera_curve_lane_"+channel,out var lane)||!lane.Inside(clip))continue;
            lanes.Add(new(channel,lane));
            void Add(string id,uint frame,int handle)
            {if(CurveBounds(id,out var box)&&box.Inside(lane)&&box.Inside(clip))controls.Add(new(id,frame,channel,handle,box,lane,_curveRanges[channel].Min,_curveRanges[channel].Max));}
            for(int i=0;i<state.Context.Camera.Keys.Length;i++)Add($"replay_camera_curve_key_{channel}_{i}",state.Context.Camera.Keys[i].Frame,0);
            if(state.Focus is{} key){Add("replay_camera_curve_in_"+channel,key.Frame,-1);Add("replay_camera_curve_out_"+channel,key.Frame,1);}
        }
        return _preparedCurves=new(_cameraToolsModal,state.Context,metrics,clip,controls.ToImmutable(),lanes.ToImmutable());
    }
    internal bool CameraCurvesPresented(CameraCurveTicket ticket,TimelineMetrics metrics,bool successful)
    {
        if(!successful||!ReferenceEquals(ticket,_preparedCurves)||!CurveTicketCurrent(ticket,metrics,true)){ReleaseCameraCurveInput();return false;}
        _presentedCurves=ticket;return true;
    }
    private bool CurveTicketCurrent(CameraCurveTicket ticket,TimelineMetrics metrics,bool focused)
    {
        if(!focused||!CameraCurvesOpen||ticket.Document!=_cameraToolsModal||ticket.Metrics!=metrics||_host.CurrentInputDocument!=ticket.Document
            ||_cameraTools?.Snapshot() is not{Current:true,Pending:null} state||!ReferenceEquals(state.Context,ticket.Context)||!CurveClip(metrics,out var clip)||clip!=ticket.Clip)return false;
        return true;
    }
    private bool CurveControlCurrent(CameraCurveControl control)=>(control.Handle==0||_cameraTools?.Snapshot().Focus?.Frame==control.Frame)&&CurveBounds(control.Id,out var box)&&box==control.Box
        &&CurveBounds("replay_camera_curve_lane_"+control.Channel,out var lane)&&lane==control.Lane;
    private void ActivateCameraCurveKey()
    {
        var ticket=_presentedCurves;string id=_host.FocusedElement();
        var target=ticket?.Controls.FirstOrDefault(row=>row.Id==id);
        if(ticket==null||target==null||!CurveTicketCurrent(ticket,ticket.Metrics,true)||!CurveControlCurrent(target)||_cameraTools==null
            ||!_cameraTools.ValidateReview()||!_cameraTools.FocusSavedFrame(target.Frame))return;
        if(!_cameraTools.Snapshot().Selection.Contains(target.Frame)){_cameraTools.ClearSelection();_cameraTools.ToggleSelection();}
    }
    internal bool CameraCurvePointerButton(int button,double x,double y,bool down,TimelineMetrics metrics,bool focused,bool alt=false,bool shift=false)
    {
        if(!CameraCurvesOpen||button!=0||_cameraTools==null)return false;
        if(!down&&_curveBox is{} box)
        {
            _curveBox=null;
            if(CurveSelectionCurrent(box,metrics,focused))
            {
                var point=metrics.Pixels(x,y);
                if(box.Ticket.Clip.Contains(point.X,point.Y))
                {
                    box=box with{EndX=point.X,EndY=point.Y};
                    var rectangle=CurveSelectionRectangle(box);
                    var frames=box.Ticket.Controls.Where(c=>c.Handle==0&&rectangle.Contains(c.Box.X+c.Box.Width/2,c.Box.Y+c.Box.Height/2))
                        .Select(c=>c.Frame).Distinct().OrderBy(frame=>frame).ToImmutableArray();
                    _cameraTools.SelectFrames(frames);
                }
            }
            ReleaseCameraCurveInput();PresentCameraTools();return true;
        }
        if(down)
        {
            _curveBox=null;_curveDrag=null;var ticket=_presentedCurves;if(ticket==null||!CurveTicketCurrent(ticket,metrics,focused))return false;
            var point=metrics.Pixels(x,y);string id=_host.FocusedElement();
            if(shift&&!ticket.Lanes.IsDefault)
            {
                var lane=ticket.Lanes.FirstOrDefault(row=>row.Box.Contains(point.X,point.Y));
                if(lane!=null&&CurveBounds("replay_camera_curve_lane_"+lane.Channel,out var current)&&current==lane.Box
                    &&_cameraTools.ValidateReview()&&_host.FocusDocument(_cameraToolsModal,"replay_camera_curve_lane_"+lane.Channel))
                {
                    _curveBox=new(ticket,lane.Channel,point.X,point.Y,point.X,point.Y,_cameraTools.Snapshot().Selection);
                    PresentCameraTools();return true;
                }
                return false;
            }
            var control=ticket.Controls.FirstOrDefault(c=>c.Id==id&&c.Box.Contains(point.X,point.Y)&&CurveControlCurrent(c));
            if(control!=null)
            {
                // Alt-drag on the authored key edits its incoming tangent. The
                // same frozen key box remains the gesture's geometry witness.
                if(alt&&control.Handle==0)
                {
                    if(!_cameraTools.ValidateReview()||!_cameraTools.FocusSavedFrame(control.Frame))return false;
                    control=control with{Handle=-1};
                }
                _curveDrag=new(ticket,control,x,y,_cameraTools.Snapshot().Selection,false,0);
            }
            return false;
        }
        var drag=_curveDrag;_curveDrag=null;if(drag is not{Moving:true})return false;
        if(_host.FocusedElement()!="replay_camera_curve_lane_"+drag.Control.Channel||!CurveTicketCurrent(drag.Ticket,metrics,focused)||!CurveControlCurrent(drag.Control)||!_cameraTools.ValidateReview()
            ||!drag.Selection.SequenceEqual(_cameraTools.Snapshot().Selection)){ReleaseCameraCurveInput();return true;}
        if(_cameraTools.Curve(new(drag.Control.Channel,drag.Control.Handle,drag.Delta)))StageCameraTools(TheatreCameraToolsChange.Curve);
        PresentCameraTools();return true;
    }
    internal void CameraCurvePointerMove(double x,double y,TimelineMetrics metrics,bool focused)
    {
        if(_curveBox is{} box)
        {
            if(!CurveSelectionCurrent(box,metrics,focused)){ReleaseCameraCurveInput();PresentCameraTools();return;}
            var boxPoint=metrics.Pixels(x,y);
            if(!box.Ticket.Clip.Contains(boxPoint.X,boxPoint.Y)){ReleaseCameraCurveInput();PresentCameraTools();return;}
            _curveBox=box with{EndX=boxPoint.X,EndY=boxPoint.Y};PresentCameraTools();return;
        }
        if(_curveDrag is not{} drag||_cameraTools==null)return;
        if((drag.Moving&&_host.FocusedElement()!="replay_camera_curve_lane_"+drag.Control.Channel)||!CurveTicketCurrent(drag.Ticket,metrics,focused)||!CurveControlCurrent(drag.Control)){ReleaseCameraCurveInput();return;}
        if(!drag.Moving)
        {
            if(Math.Abs((y-drag.Y)*metrics.FramebufferHeight/metrics.ClientHeight)<4)return;
            if(!_cameraTools.ValidateReview()||!_cameraTools.FocusSavedFrame(drag.Control.Frame)){ReleaseCameraCurveInput();return;}
            if(!_cameraTools.Snapshot().Selection.Contains(drag.Control.Frame)){_cameraTools.ClearSelection();_cameraTools.ToggleSelection();}
            if(!_host.FocusDocument(_cameraToolsModal,"replay_camera_curve_lane_"+drag.Control.Channel)||!CurveControlCurrent(drag.Control)){ReleaseCameraCurveInput();return;}
            drag=drag with{Moving=true,Selection=_cameraTools.Snapshot().Selection};
        }
        var point=metrics.Pixels(x,y);if(!drag.Ticket.Clip.Contains(point.X,point.Y)){ReleaseCameraCurveInput();return;}
        float delta=(float)(-(y-drag.Y)*metrics.FramebufferHeight/metrics.ClientHeight/Math.Max(1,drag.Control.Lane.Height-8)*(drag.Control.Maximum-drag.Control.Minimum));
        _curveDrag=drag with{Delta=delta};PresentCameraTools();
    }
    private static TimelineBox CurveSelectionRectangle(CurveBoxDrag drag)
        =>new(Math.Min(drag.X,drag.EndX),Math.Min(drag.Y,drag.EndY),Math.Abs(drag.EndX-drag.X),Math.Abs(drag.EndY-drag.Y));
    private bool CurveSelectionCurrent(CurveBoxDrag drag,TimelineMetrics metrics,bool focused)
    {
        if(_cameraTools==null||!CurveTicketCurrent(drag.Ticket,metrics,focused)||drag.Ticket.Lanes.IsDefault
            ||_host.FocusedElement()!="replay_camera_curve_lane_"+drag.Channel
            ||!drag.Selection.SequenceEqual(_cameraTools.Snapshot().Selection))return false;
        foreach(var lane in drag.Ticket.Lanes)
            if(!CurveBounds("replay_camera_curve_lane_"+lane.Channel,out var current)||current!=lane.Box)return false;
        return drag.Ticket.Controls.Where(c=>c.Handle==0).All(CurveControlCurrent);
    }
    internal void ReleaseCameraCurveInput()
    {
        _curveBox=null;_curveDrag=null;_preparedCurves=null;_presentedCurves=null;
        if(_cameraToolsModal!=default&&_host.IsAlive(_cameraToolsModal))
            for(int channel=0;channel<5;channel++)_host.SetBool(_cameraToolsModal,"visible:replay_camera_curve_selection_"+channel,false);
    }
}
#endif
