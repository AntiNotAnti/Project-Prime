using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.StudioRendering;
using System.Runtime.InteropServices;

namespace ProjectPrime.Studio.Rendering;

/// <summary>Avalonia owns the child window; the shared graphics device owns only its WebGPU surface.</summary>
public class StudioNativeViewport : NativeControlHost, IDisposable
{
    private nint _display;
    private nint _oldWindowProcedure;
    private WindowProcedure? _windowProcedure;
    private nint _originalClass;
    private bool _disposed;
    public StudioNativeSurface? Surface { get; private set; }
    public string? GraphicsError { get; private set; }
    public event Action? GraphicsReady;
    public event Action? GraphicsDestroying;
    public event Action<Exception>? GraphicsFailed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var control=base.CreateNativeControlCore(parent);
        try
        {
            if(OperatingSystem.IsLinux() && control.HandleDescriptor=="XID")
            { _display=XOpenDisplay(0); if(_display==0)throw new InvalidOperationException("Studio viewport could not open the X11 display."); }
            MakeInputTransparent(control);
            Surface=StudioGraphicsHost.Device.CreateNativeSurface(control.Handle,control.HandleDescriptor ?? "",_display);
            Dispatcher.UIThread.Post(() => { GraphicsReady?.Invoke(); (this.GetVisualParent() as Control)?.InvalidateVisual(); });
        }
        catch(Exception ex)
        {
            GraphicsError=ex.Message; Console.Error.WriteLine("[studio] Native viewport unavailable: "+ex.Message);
            Dispatcher.UIThread.Post(()=>{ IsVisible=false;GraphicsFailed?.Invoke(ex);(this.GetVisualParent() as Control)?.InvalidateVisual(); });
        }
        return control;
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        try
        {
            GraphicsDestroying?.Invoke();Surface?.Dispose();Surface=null;
            if(_oldWindowProcedure!=0)SetWindowLongPtr(control.Handle,-4,_oldWindowProcedure);
            _oldWindowProcedure=0;_windowProcedure=null;
            if(_originalClass!=0)object_setClass(control.Handle,_originalClass);
            _originalClass=0;
            if(_display!=0)XCloseDisplay(_display);_display=0;
        }
        finally { base.DestroyNativeControlCore(control); }
    }
    private void MakeInputTransparent(IPlatformHandle control)
    {
        // Native children are visual-only; canonical Avalonia authoring controls
        // receive the pointer/keyboard stream, including drag capture and menus.
        if(OperatingSystem.IsWindows())
        {
            _windowProcedure=(window,message,w,l) => message==0x84 ? -1 : CallWindowProc(_oldWindowProcedure,window,message,w,l);
            _oldWindowProcedure=SetWindowLongPtr(control.Handle,-4,Marshal.GetFunctionPointerForDelegate(_windowProcedure));
        }
        else if(OperatingSystem.IsLinux() && _display!=0)
        { XShapeCombineRectangles(_display,control.Handle,2,0,0,0,0,0,0); XFlush(_display); }
        else if(OperatingSystem.IsMacOS())
        {
            _originalClass=object_getClass(control.Handle);
            nint transparentClass=objc_getClass("ProjectPrimeStudioTransparentViewport");
            if(transparentClass==0)
            {
                transparentClass=objc_allocateClassPair(_originalClass,"ProjectPrimeStudioTransparentViewport",0);
                if(transparentClass==0)throw new InvalidOperationException("Could not create transparent native viewport class.");
                class_addMethod(transparentClass,sel_registerName("hitTest:"),Marshal.GetFunctionPointerForDelegate(HitTest),"@32@0:8{CGPoint=dd}16");
                objc_registerClassPair(transparentClass);
            }
            object_setClass(control.Handle,transparentClass);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public double X,Y; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint HitTestProcedure(nint view,nint selector,NativePoint point);
    private static readonly HitTestProcedure HitTest=(_,_,_)=>0;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint window,uint message,nint wParam,nint lParam);
    [DllImport("user32",EntryPoint="SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [DllImport("user32",EntryPoint="CallWindowProcW")] private static extern nint CallWindowProc(nint procedure,nint window,uint message,nint w,nint l);
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(nint display,nint window,int kind,int x,int y,nint rectangles,int count,int operation,int ordering);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint object_getClass(nint obj);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint object_setClass(nint obj,nint cls);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint objc_allocateClassPair(nint superclass,string name,nuint extraBytes);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern void objc_registerClassPair(nint cls);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(nint cls,nint selector,nint imp,string signature);
    public virtual void Dispose()
    { if(_disposed)return;_disposed=true;Surface?.Dispose();Surface=null; }
}
