#if MPHREAD_STUDIO
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.StudioRendering;

/// <summary>The canonical editor pass draws directly into Avalonia's current
/// FBO. Immutable frames are captured on the UI thread; no preview readback.</summary>
internal sealed class StudioMapViewport : OpenGlControlBase
{
    private readonly MapViewport _view;
    private readonly Action<string> _report;
    private readonly DispatcherTimer _timer;
    private Scene? _scene;
    private MapRenderFrame? _frame;
    private IDisposable? _registration;
    private MacLegacyGlSurface? _macSurface;
    private bool _failed;
    internal static Control Wrap(MapViewport view,Action<string> report)
    {
        if(StudioGraphicsState.SafeMode)return view;
        // A viewport can be moved between the single/four-view layouts.
        if(view.Parent is Panel previous)previous.Children.Remove(view);
        var grid=new Grid();
        grid.Children.Add(new StudioMapViewport(view,report){IsHitTestVisible=false});
        grid.Children.Add(view);
        return grid;
    }
    private StudioMapViewport(MapViewport view,Action<string> report)
    {
        _view=view;_report=report;
        _timer=new DispatcherTimer(TimeSpan.FromSeconds(1d/30),DispatcherPriority.Render,(_,_)=>CaptureFrame());
        AttachedToVisualTree+=(_,_)=>{CaptureFrame();_timer.Start();};
        DetachedFromVisualTree+=(_,_)=>_timer.Stop();
    }
    private void CaptureFrame()
    {
        if(!IsEffectivelyVisible||_failed)return;
        double scale=TopLevel.GetTopLevel(this)?.RenderScaling??1;
        var layout=new MapViewportLayout(Bounds.Width,Bounds.Height,scale);
        if(!layout.IsValid)return;
        System.Threading.Volatile.Write(ref _frame,_view.BuildRenderFrame(layout));
        RequestNextFrameRendering();
    }
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            string? adapter=null;
            if(OperatingSystem.IsMacOS())
            {
                _macSurface=new MacLegacyGlSurface();
                _macSurface.RunInLegacy(()=>
                {
                    _scene=Scene.CreateEditorRenderer(new Vector2i(1,1));
                    adapter=GL.GetString(StringName.Renderer);
                });
            }
            else
            {
                StudioGlBindings.Initialize(gl);
                _scene=Scene.CreateEditorRenderer(new Vector2i(1,1));
                adapter=GL.GetString(StringName.Renderer);
            }
            string backend=_macSurface!=null?"OpenGL 2.1 / IOSurface":"OpenGL";
            _registration=StudioGraphicsState.RegisterSurface(backend,adapter);
            Console.Error.WriteLine($"[studio-gl] Native map surface created: {backend}; {adapter}");
        }
        catch(Exception ex){Fail(ex);}
    }
    protected override void OnOpenGlRender(GlInterface gl,int framebuffer)
    {
        var frame=System.Threading.Volatile.Read(ref _frame);
        if(_scene==null||_failed||frame==null)return;
        if(_macSurface!=null)
        {
            try
            {
                _macSurface.Render(frame.Layout.PixelWidth,frame.Layout.PixelHeight,framebuffer,
                    ()=>_scene.DrawEditorFrame(frame,new(frame.Layout.PixelWidth,frame.Layout.PixelHeight)));
                ActivateGpuOverlay();
            }
            catch(Exception ex){Fail(ex);}
            return;
        }
        int previousDraw=GL.GetInteger(GetPName.DrawFramebufferBinding),previousRead=GL.GetInteger(GetPName.ReadFramebufferBinding);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer,framebuffer);
            if(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)!=FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("The native viewport framebuffer is incomplete.");
            _scene.DrawEditorFrame(frame,new(frame.Layout.PixelWidth,frame.Layout.PixelHeight));
            ActivateGpuOverlay();
        }
        catch(Exception ex){Fail(ex);}
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,previousDraw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,previousRead);
        }
    }
    private void ActivateGpuOverlay()=>Dispatcher.UIThread.Post(()=>{if(!_view.NativeGpuActive){_view.NativeGpuActive=true;_view.InvalidateVisual();}});
    private void Fail(Exception ex)
    {
        _failed=true;
        Dispatcher.UIThread.Post(()=>
        {
            _timer.Stop();_view.NativeGpuActive=false;_view.InvalidateVisual();
            _report("OpenGL preview unavailable; using CPU preview. "+ex.Message);
        });
        ReleaseScene();
    }
    private void ReleaseScene()
    {
        try
        {
            if(_macSurface!=null)_macSurface.RunInLegacy(()=>_scene?.UnloadGl());
            else _scene?.UnloadGl();
        }
        finally
        {
            _scene=null;
            try{_macSurface?.Dispose();}
            finally{_macSurface=null;_registration?.Dispose();_registration=null;}
        }
    }
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        Dispatcher.UIThread.Post(()=>{_view.NativeGpuActive=false;_view.InvalidateVisual();});
        try{ReleaseScene();}finally{_failed=false;}
    }
}
#endif
