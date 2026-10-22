#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    // External UI presentation storage: no Scene instance fields, replay hashes, protocol or archive fields.
    private sealed record PendingReplayTimeline(RenderWindow Window,TheatrePlaybackPagePresenter Page,
        TimelineRenderTicket Ticket,TimelineMetrics Metrics);
    private static PendingReplayTimeline? _pendingReplayTimeline;
    private sealed record PendingCameraCurves(RenderWindow Window,TheatrePlaybackPagePresenter Page,CameraCurveTicket Ticket,TimelineMetrics Metrics);
    private static PendingCameraCurves? _pendingCameraCurves;
    internal static void BeginNativePresentation(RenderWindow window)
    { _pendingReplayTimeline=null;_pendingCameraCurves=null; }
    private static TimelineMetrics ReplayTimelineMetrics(RenderWindow window)=>new(window.FramebufferSize.X,window.FramebufferSize.Y,
        window.ClientSize.X,window.ClientSize.Y,Density(window));
    private static void QueueReplayTimeline(RenderWindow window,TheatrePlaybackPagePresenter? page,TimelineRenderTicket? ticket,TimelineMetrics metrics)
    {
        var curves=page?.PrepareCameraCurves(metrics,window.IsFocused);
        _pendingCameraCurves=page!=null&&curves!=null&&ReferenceEquals(page,_playback)&&GameplayLeaseCurrent?new(window,page,curves,metrics):null;
        _pendingReplayTimeline=page!=null&&ticket!=null&&ReferenceEquals(page,_playback)&&GameplayLeaseCurrent
            ?new(window,page,ticket,metrics):null;
    }
    /// <summary>Renderer invokes this after SwapBuffers AND after the same scene's successful camera picture publication.</summary>
    internal static void PublishNativeTimelinePresentedFrame(RenderWindow window)
    {
        var curves=_pendingCameraCurves;_pendingCameraCurves=null;
        if(curves!=null)
        {
            bool current=ReferenceEquals(curves.Window,window)&&ReferenceEquals(_playback,curves.Page)&&GameplayLeaseCurrent&&window.IsFocused&&ReplayTimelineMetrics(window)==curves.Metrics;
            curves.Page.CameraCurvesPresented(curves.Ticket,curves.Metrics,current);
        }
        var pending=_pendingReplayTimeline;_pendingReplayTimeline=null;
        if(pending==null||!ReferenceEquals(pending.Window,window)||!ReferenceEquals(_playback,pending.Page)
            ||!GameplayLeaseCurrent||!window.IsFocused||ReplayTimelineMetrics(window)!=pending.Metrics)
        {if(pending!=null)pending.Page.ReleaseTimelineInput();return;}
        pending.Page.TimelinePresented(pending.Ticket,pending.Metrics,true);
    }
}
#endif
