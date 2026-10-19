#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

internal readonly record struct TimelineMetrics(int FramebufferWidth,int FramebufferHeight,int ClientWidth,int ClientHeight,float Density)
{
    internal bool Valid=>FramebufferWidth is >0 and <=100000&&FramebufferHeight is >0 and <=100000
        &&ClientWidth is >0 and <=100000&&ClientHeight is >0 and <=100000&&float.IsFinite(Density)&&Density>0;
    internal (double X,double Y) Pixels(double x,double y)=>
        (x*FramebufferWidth/ClientWidth,y*FramebufferHeight/ClientHeight);
}
internal readonly record struct TimelineBox(double X,double Y,double Width,double Height)
{
    internal bool Valid=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Width)&&double.IsFinite(Height)&&Width>0&&Height>0;
    internal bool Bounded=>Valid&&Math.Abs(X)<=100000&&Math.Abs(Y)<=100000&&Width<=100000&&Height<=100000;
    internal bool Contains(double x,double y)=>Valid&&x>=X&&y>=Y&&x<X+Width&&y<Y+Height;
    internal bool Inside(TimelineBox outer)=>Valid&&outer.Valid&&X>=outer.X&&Y>=outer.Y&&X+Width<=outer.X+outer.Width&&Y+Height<=outer.Y+outer.Height;
}
internal readonly record struct TimelineWindow(uint First,uint Last)
{
    internal uint Span=>Last-First;
    internal double X(uint frame,TimelineBox box)=>box.X+(Math.Clamp(frame,First,Last)-First)/(double)Math.Max(1u,Span)*box.Width;
    internal uint Frame(double x,TimelineBox box)=>First+(uint)Math.Round(Math.Clamp((x-box.X)/box.Width,0,1)*Span,MidpointRounding.AwayFromZero);
}
internal sealed record TimelineKeyBox(uint Frame,TimelineBox Box);
internal sealed record TimelineLaneButton(string Id,TimelineBox Box,TimelineLaneGlyph Glyph);
internal sealed record TimelineActivation(uint? Key,TimelineLaneGlyph? Lane);
internal sealed record TimelineRenderTicket(long Paint,long ViewRevision,RmlUiDocumentToken Document,TimelineMetrics Metrics,
    TimelineWindow Window,TimelineBox CameraLane,ImmutableArray<TimelineKeyBox> Keys,TheatreCameraToolsContext Context)
{
    internal ImmutableArray<TimelineLaneButton> Lanes { get; init; }=ImmutableArray<TimelineLaneButton>.Empty;
    internal TimelineBox Clip { get; init; }
    internal TimelineBox? SeekControl { get; init; }
}
internal sealed record TimelineMoveProposal(TheatreCameraToolsContext Context,ImmutableArray<uint> Selection,uint Destination,
    ImmutableArray<uint> PreviewFrames);

/// <summary>Presenter-only window and pointer preview state; it never seeks, owns a replay, edits keys or writes metadata.</summary>
internal sealed class TheatreTimelineViewState
{
    private sealed record Drag(TimelineRenderTicket Ticket,ImmutableArray<uint> Selection,uint PressFrame,double PressX,
        uint Destination,ImmutableArray<uint> Preview,bool Moved,bool Valid);
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private TheatreCameraToolsContext? _review;
    private TimelineRenderTicket? _prepared,_presented;
    private Drag? _drag;
    private uint _center;
    private int _zoom;
    private long _viewRevision,_paint;
    internal string Notice { get; private set; }="";
    internal bool Dragging=>_drag!=null;
    internal ImmutableArray<uint> Preview=>_drag?.Preview??ImmutableArray<uint>.Empty;
    internal bool PreviewValid=>_drag?.Valid??false;
    internal int Zoom=>1<<_zoom;
    internal TimelineWindow Window
    {
        get
        {
            uint duration=_review?.Camera.Duration??0;
            uint visible=(uint)Math.Min(duration,Math.Max(60UL,((ulong)duration+(uint)Zoom-1)/(uint)Zoom));
            if(visible>=duration)return new(0,duration);
            uint first=(uint)Math.Clamp((long)_center-visible/2,0,(long)duration-visible);
            return new(first,first+visible);
        }
    }
    internal void Review(TheatreCameraToolsSnapshot snapshot)
    {
        Verify();if(!snapshot.Current||snapshot.Pending!=null)throw new InvalidOperationException("Pause and explicitly review camera tools before opening the timeline.");
        bool ownerChanged=_review==null||!ReferenceEquals(_review.Owner,snapshot.Context.Owner);
        _review=snapshot.Context;if(ownerChanged){_center=snapshot.Context.Camera.Frame;_zoom=0;}
        Retire("Timeline review refreshed. Dragging only previews a later reviewed group move.");
    }
    internal void ZoomBy(int direction)
    {
        Verify();if(direction is not(-1 or 1))throw new ArgumentException("Choose zoom in or out.");
        _zoom=Math.Clamp(_zoom+direction,0,4);Retire("Timeline zoom changed; wait for the new native picture.");
    }
    internal void Pan(int direction)
    {
        Verify();if(direction is not(-1 or 1))throw new ArgumentException("Choose earlier or later frames.");
        _center=(uint)Math.Clamp((long)_center+direction*Math.Max(1u,Window.Span/2),0,_review?.Camera.Duration??0);
        Retire("Timeline window changed; wait for the new native picture.");
    }
    internal void Center(){Verify();_center=_review?.Camera.Frame??0;Retire("Timeline centered on the reviewed playhead.");}
    internal void Reset(){Verify();_zoom=0;_center=_review?.Camera.Frame??0;Retire("Timeline shows the full reviewed recording.");}
    internal void Cancel(string notice="Drag preview cancelled. Saved camera keys are unchanged.")
    {Verify();_drag=null;Notice=notice;}
    internal void Invalidate(string notice){Verify();Retire(notice);}
    private void Retire(string notice){_viewRevision++;_prepared=null;_presented=null;_drag=null;Notice=notice;}
    internal TimelineRenderTicket? Prepare(TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,TimelineMetrics metrics,
        TimelineBox cameraLane,ImmutableArray<TimelineKeyBox> actualDomKeys,bool focused,
        ImmutableArray<TimelineLaneButton> actualDomLanes=default,TimelineBox? actualDomClip=null,TimelineBox? actualDomSeek=null,bool cameraVisible=true)
    {
        Verify();_prepared=null;
        var screen=new TimelineBox(0,0,metrics.FramebufferWidth,metrics.FramebufferHeight);var clip=actualDomClip??screen;
        var review=_review;
        if(review==null||!Ready(snapshot)||document==default||!metrics.Valid||!focused||!cameraLane.Bounded||!clip.Inside(screen)
            ||actualDomKeys.IsDefault||actualDomKeys.Length>64){Retire("Timeline layout or review is unavailable. Refresh after the native picture settles.");return null;}
        var expected=cameraVisible?_review!.Camera.Keys.Where(key=>key.Frame>=Window.First&&key.Frame<=Window.Last).Select(key=>key.Frame).ToArray():Array.Empty<uint>();
        if(!actualDomKeys.Select(key=>key.Frame).SequenceEqual(expected)||actualDomKeys.Any(key=>!key.Box.Inside(cameraLane)))
        {Retire("Native camera marker layout does not match the reviewed frame window.");return null;}
        if(actualDomLanes.IsDefault)actualDomLanes=ImmutableArray<TimelineLaneButton>.Empty;
        if(actualDomLanes.Length>768||actualDomLanes.Any(row=>!row.Box.Inside(clip))
            ||actualDomLanes.Select(row=>row.Id).Distinct().Count()!=actualDomLanes.Length)
        {Retire("Native timeline lane bounds are invalid.");return null;}
        var ticket=new TimelineRenderTicket(++_paint,_viewRevision,document,metrics,Window,cameraLane,actualDomKeys,review)
            {Lanes=actualDomLanes,Clip=clip,SeekControl=actualDomSeek is{} seek&&seek.Inside(clip)?seek:null};
        if(_drag is{} drag&&!Compatible(drag.Ticket,ticket))Cancel("Timeline geometry changed during the drag. Saved keys are unchanged.");
        _prepared=ticket;return ticket;
    }
    /// <summary>Root publishes only the prepared DOM draw that completed the same successful native frame presentation.</summary>
    internal bool Presented(TimelineRenderTicket? expected,RmlUiDocumentToken document,TimelineMetrics metrics,bool nativeFramePresented)
    {
        Verify();if(expected==null||!ReferenceEquals(expected,_prepared))return false;
        var prepared=_prepared;_prepared=null;
        if(!nativeFramePresented||prepared.Document!=document||prepared.Metrics!=metrics||prepared.ViewRevision!=_viewRevision)
        {Retire("The native timeline picture was not presented. Pointer editing is inactive.");return false;}
        _presented=prepared;return true;
    }
    internal uint? HitKey(double windowX,double windowY,TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,
        TimelineMetrics metrics,bool foregroundAndFocused)
    {
        Verify();if(!PointerReady(snapshot,document,metrics,foregroundAndFocused)||!double.IsFinite(windowX)||!double.IsFinite(windowY))return null;
        var (x,y)=metrics.Pixels(windowX,windowY);
        return _presented!.Keys.Where(key=>key.Box.Contains(x,y)).OrderBy(key=>Math.Abs(key.Box.X+key.Box.Width/2-x))
            .ThenBy(key=>key.Frame).Select(key=>(uint?)key.Frame).FirstOrDefault();
    }
    internal TimelineActivation? Activate(string focusedId,TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,
        TimelineMetrics metrics,bool foregroundAndFocused)
    {
        Verify();if(!CommonReady(snapshot,document,metrics,foregroundAndFocused))return null;
        const string prefix="replay_timeline_key_";
        if(focusedId.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(focusedId[prefix.Length..],out int index)
            &&index>=0&&index<_presented!.Keys.Length&&focusedId==prefix+index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return _presented.Keys[index].Box.Inside(_presented.Clip)?new(_presented.Keys[index].Frame,null):null;
        var lane=_presented!.Lanes.FirstOrDefault(row=>row.Id==focusedId);
        return lane==null?null:new(null,lane.Glyph);
    }
    internal uint? NativeKeyAt(string focusedId,double windowX,double windowY,TheatreCameraToolsSnapshot snapshot,
        RmlUiDocumentToken document,TimelineMetrics metrics,bool foregroundAndFocused)
    {
        Verify();if(!PointerReady(snapshot,document,metrics,foregroundAndFocused))return null;
        var target=Activate(focusedId,snapshot,document,metrics,foregroundAndFocused);
        if(target?.Key is not uint key||!double.IsFinite(windowX)||!double.IsFinite(windowY))return null;
        var (x,y)=metrics.Pixels(windowX,windowY);
        return _presented!.Keys.Any(row=>row.Frame==key&&row.Box.Contains(x,y))?key:null;
    }
    internal bool PresentedCurrent(TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,TimelineMetrics metrics,bool foregroundAndFocused)
    {Verify();return CommonReady(snapshot,document,metrics,foregroundAndFocused);}
    internal bool SeekPresentedCurrent(TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,TimelineMetrics metrics,
        bool foregroundAndFocused,TimelineBox actualControl,TimelineBox actualClip)
    {Verify();return CommonReady(snapshot,document,metrics,foregroundAndFocused)&&_presented!.SeekControl is{} seek
        &&seek==actualControl&&_presented.Clip==actualClip&&seek.Inside(actualClip);}
    internal bool ActivationBoundsCurrent(string id,TimelineBox actualControl,TimelineBox actualClip)
    {
        Verify();if(_presented==null||_presented.Clip!=actualClip||!actualControl.Inside(actualClip))return false;
        const string prefix="replay_timeline_key_";
        if(id.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(id[prefix.Length..],out int index)
            &&index>=0&&index<_presented.Keys.Length&&id==prefix+index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return _presented.Keys[index].Box==actualControl;
        return _presented.Lanes.Any(row=>row.Id==id&&row.Box==actualControl);
    }
    internal bool PointerBoundsCurrent(TimelineBox actualCamera,TimelineBox actualClip)
    {Verify();return _presented!=null&&_presented.CameraLane==actualCamera&&_presented.Clip==actualClip&&actualCamera.Inside(actualClip);}
    internal void InspectBin(TimelineLaneGlyph glyph)
    {Verify();_center=glyph.First+(glyph.Last-glyph.First)/2;_zoom=Math.Min(4,_zoom+1);Retire($"Zoomed to {glyph.Count} reviewed entries. Aggregates are not single replay events.");}
    /// <summary>The presenter applies ordinary camera-tools focus/selection first; a drag freezes that exact resulting selection.</summary>
    internal bool Begin(uint picked,double windowX,double windowY,TheatreCameraToolsSnapshot snapshot,
        RmlUiDocumentToken document,TimelineMetrics metrics,bool foregroundAndFocused)
    {
        Verify();Cancel();
        if(!PointerReady(snapshot,document,metrics,foregroundAndFocused)||!double.IsFinite(windowX)||!double.IsFinite(windowY)
            ||!snapshot.Selection.Contains(picked)||snapshot.Selection.IsEmpty)return false;
        var (x,y)=metrics.Pixels(windowX,windowY);
        if(!_presented!.Keys.Any(key=>key.Frame==picked&&key.Box.Contains(x,y)))return false;
        _drag=new(_presented!,snapshot.Selection,_presented!.Window.Frame(x,_presented.CameraLane),x,
            snapshot.Selection.Min(),snapshot.Selection,false,true);
        Notice="Camera drag preview only. Release to review the exact complete group move.";return true;
    }
    internal bool Move(double windowX,double windowY,TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,
        TimelineMetrics metrics,bool foregroundAndFocused)
    {
        Verify();if(_drag is not{} drag)return false;
        if(!PointerReady(snapshot,document,metrics,foregroundAndFocused)||!Compatible(drag.Ticket,_presented!)
            ||!snapshot.Selection.SequenceEqual(drag.Selection)||!double.IsFinite(windowX)||!double.IsFinite(windowY))
        {Cancel("The drag's picture, focus, selection or replay owner changed. Saved keys are unchanged.");return false;}
        var (x,y)=metrics.Pixels(windowX,windowY);
        if(!drag.Ticket.CameraLane.Contains(x,y)){Cancel("Pointer left the camera lane. Saved keys are unchanged.");return false;}
        long delta=(long)drag.Ticket.Window.Frame(x,drag.Ticket.CameraLane)-drag.PressFrame;
        bool moved=drag.Moved||Math.Abs(x-drag.PressX)>=4;
        long first=(long)drag.Selection.Min()+delta,last=(long)drag.Selection.Max()+delta;
        bool valid=first>=0&&last<=_review!.Camera.Duration;
        var frames=valid?drag.Selection.Select(frame=>(uint)((long)frame+delta)).ToImmutableArray():drag.Selection;
        if(valid&&frames.Any(frame=>_review!.Camera.Keys.Any(key=>key.Frame==frame&&!drag.Selection.Contains(key.Frame))))valid=false;
        _drag=drag with{Destination=valid?(uint)first:drag.Destination,Preview=frames,Moved=moved,Valid=valid};
        Notice=valid?"Preview only. Release to open the existing reviewed group-move confirmation.":"This group collides with another key or exceeds the replay. No edit can be staged.";
        return true;
    }
    internal TimelineMoveProposal? Release(double windowX,double windowY,TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,
        TimelineMetrics metrics,bool foregroundAndFocused)
    {
        Verify();if(!Move(windowX,windowY,snapshot,document,metrics,foregroundAndFocused)||_drag is not{} drag)return null;
        _drag=null;
        if(!drag.Moved||!drag.Valid||drag.Destination==drag.Selection.Min()){Notice="Camera selection changed. Saved keys are unchanged.";return null;}
        // The presenter must explicitly ValidateReviewedTools immediately before Destination + Stage(Move).
        return new(drag.Ticket.Context,drag.Selection,drag.Destination,drag.Preview);
    }
    private bool Ready(TheatreCameraToolsSnapshot snapshot)=>_review!=null&&snapshot.Current&&snapshot.Pending==null
        &&ReferenceEquals(snapshot.Context,_review)&&snapshot.Context.Camera.Keys.SequenceEqual(_review.Camera.Keys);
    private bool PointerReady(TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,TimelineMetrics metrics,bool foregroundAndFocused)
    {
        if(!CommonReady(snapshot,document,metrics,foregroundAndFocused)||!_presented!.CameraLane.Inside(_presented.Clip))
        {Cancel("The complete camera lane must be visible in its successfully presented scroll region before pointer editing.");return false;}
        return true;
    }
    private bool CommonReady(TheatreCameraToolsSnapshot snapshot,RmlUiDocumentToken document,TimelineMetrics metrics,bool foregroundAndFocused)
    {
        if(!Ready(snapshot)||!foregroundAndFocused||_presented==null||_presented.Document!=document||_presented.Metrics!=metrics
            ||_presented.ViewRevision!=_viewRevision){Cancel("Timeline pointer authority retired. Refresh or wait for its native picture.");return false;}
        return true;
    }
    private static bool Compatible(TimelineRenderTicket a,TimelineRenderTicket b)=>a.ViewRevision==b.ViewRevision&&a.Document==b.Document
        &&a.Metrics==b.Metrics&&a.Window==b.Window&&a.CameraLane==b.CameraLane&&a.Clip==b.Clip&&ReferenceEquals(a.Context,b.Context)&&a.Keys.SequenceEqual(b.Keys);
    private void Verify(){if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("Timeline view state belongs to the presenter owner thread.");}
}
#endif
