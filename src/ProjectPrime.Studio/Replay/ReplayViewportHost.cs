using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Rendering;
using System.Diagnostics;

namespace ProjectPrime.Studio.Replay;

/// <summary>Private replay initialization, simulation, presentation and teardown run on the graphics owner.</summary>
public sealed class ReplayViewportHost : Grid, IDisposable
{
    private readonly IStudioReplayGraphicsSession _session;
    private readonly StudioNativeViewport _native = new();
    private readonly TextBlock _error = new() { IsVisible=false,Margin=new Thickness(20),TextWrapping=TextWrapping.Wrap,
        Foreground=Brushes.Orange,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center };
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _last;
    private bool _initialized,_disposed,_automaticRenderingEnabled=true;
    public bool AutomaticRenderingEnabled
    {
        get=>_automaticRenderingEnabled;
        set
        {
            Dispatcher.UIThread.VerifyAccess();_automaticRenderingEnabled=value;
            if(!_initialized)return;
            if(value){_last=_clock.Elapsed;_timer.Start();}else _timer.Stop();
        }
    }
    public Func<int,int,StudioReplayView> ViewFactory { get; set; } = (width,height)=>new(width,height);
    public string? PresentationError { get; private set; }
    public ReplayViewportHost(IStudioReplayGraphicsSession session)
    {
        _session=session;Children.Add(_error);MinHeight=200;
        if(!StudioGraphicsHost.SafeMode)Children.Insert(0,_native);
        else { _error.Text="Safe mode: replay graphics are disabled. Metadata and authoring edits remain available.";_error.IsVisible=true; }
        _native.GraphicsReady+=Start;_native.GraphicsDestroying+=Stop;
        _native.GraphicsFailed+=Fail;
        _timer=new DispatcherTimer(TimeSpan.FromSeconds(1d/60),DispatcherPriority.Render,(_,_)=>RenderFrame());
        AttachedToVisualTree+=(_,_)=>{if(_native.Surface!=null)Start();};DetachedFromVisualTree+=(_,_)=>Stop();
    }
    private (int Width,int Height) PixelBounds()
    {
        double scale=TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return (Math.Max(1,(int)Math.Round(Bounds.Width*scale)),Math.Max(1,(int)Math.Round(Bounds.Height*scale)));
    }
    private void Start()
    {
        if(StudioGraphicsHost.SafeMode || _disposed || _initialized || _native.Surface==null)return;
        try
        {
            var size=PixelBounds();StudioGraphicsHost.Device.BeginReplayFrame(_native.Surface,size.Width,size.Height);
            _session.OnGraphicsInitialize(size.Width,size.Height);_initialized=true;_last=_clock.Elapsed;
            if(_automaticRenderingEnabled)_timer.Start();
            _error.IsVisible=false;PresentationError=null;
        }
        catch(Exception ex){Fail(ex);}
    }
    private void RenderFrame()
    {
        var now=_clock.Elapsed;var elapsed=now-_last;_last=now;
        RenderExternal(elapsed);
    }
    /// <summary>A workspace can present several views sequentially from one presentation clock.</summary>
    public void RenderExternal(TimeSpan elapsed)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(!_initialized || _disposed || _native.Surface==null || !IsEffectivelyVisible || PresentationError!=null)return;
        try
        {
            var size=PixelBounds();
            var frameClock=Stopwatch.StartNew();
            StudioGraphicsHost.Device.BeginReplayFrame(_native.Surface,size.Width,size.Height);
            _session.OnGraphicsFrame(elapsed,ViewFactory(size.Width,size.Height));StudioGraphicsHost.Device.PresentReplayFrame();
            StudioGraphicsHost.ReportReplayFrame(frameClock.Elapsed.TotalMilliseconds);
        }
        catch(Exception ex){Fail(ex);}
    }
    private void Fail(Exception ex)
    {
        _timer.Stop();PresentationError=ex.Message;
        _error.Text="Replay presentation failed: "+ex.Message+"\nThe replay document and edits are retained. Retry the viewport to recreate presentation.";
        _error.IsVisible=true;
        // A native child occupies its own airspace. Remove it before displaying
        // the Avalonia error panel so a failed surface cannot hide recovery UI.
        _native.IsVisible=false;
    }
    public void Retry()
    { if(StudioGraphicsHost.SafeMode)return;Stop();_native.IsVisible=true;Start(); }
    /// <summary>Explicit diagnostic/export capture. Ordinary presentation never reads the GPU target.</summary>
    public StudioReplayCapture? Capture(int width,int height)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(!_initialized || _disposed || _native.Surface==null || StudioGraphicsHost.SafeMode)return null;
        width=Math.Max(1,width);height=Math.Max(1,height);
        StudioGraphicsHost.Device.BeginReplayFrame(_native.Surface,width,height);
        try{return _session.Capture(ViewFactory(width,height));}
        finally{StudioGraphicsHost.Device.PresentReplayFrame();}
    }
    /// <summary>Release a private graphics session while this native owner is current.</summary>
    public void ReleaseGraphicsSession(IStudioReplayGraphicsSession session)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(ReferenceEquals(session,_session)){Stop();return;}
        bool eligible=!_disposed && _native.Surface!=null && StudioGraphicsHost.HasDevice && StudioGraphicsHost.Device.NativeResourcesAvailable;
        if(eligible)
        {
            try {var size=PixelBounds();StudioGraphicsHost.Device.BeginReplayFrame(_native.Surface!,size.Width,size.Height);}
            catch(Exception ex){eligible=false;Console.Error.WriteLine("[studio] Comparison context unavailable during cleanup: "+ex.Message);}
        }
        session.OnGraphicsDeinitialize(eligible);
    }
    private void Stop()
    {
        _timer.Stop();if(!_initialized)return;
        try
        {
            bool nativeEligible=StudioGraphicsHost.HasDevice && StudioGraphicsHost.Device.NativeResourcesAvailable;
            if(_native.Surface!=null && nativeEligible)
            {
                try {var size=PixelBounds();StudioGraphicsHost.Device.BeginReplayFrame(_native.Surface,size.Width,size.Height);}
                catch(Exception ex){nativeEligible=false;Console.Error.WriteLine("[studio] Replay context unavailable during cleanup: "+ex.Message);}
            }
            _session.OnGraphicsDeinitialize(nativeEligible);
        }
        finally{_initialized=false;}
    }
    public void Dispose()
    {if(_disposed)return;Stop();_disposed=true;_native.Dispose();}
}
