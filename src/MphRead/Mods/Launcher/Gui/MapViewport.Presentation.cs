#if MPHREAD_SHELL
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MphRead.Mods.StudioRendering;
using System;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapViewport
{
    private IStudioMapPresentation? _studioPresentation;
    private WriteableBitmap? _studioBitmap;
    private bool _studioPresentationFailed;
    private bool _capturingStudioOverlay;

    private void InitializeStudioPresentation()
    {
        if(_studioPresentationFailed || _studioPresentation!=null)return;
        try
        {
            _studioPresentation=StudioViewportConfiguration.MapPresentationFactory?.Invoke(Document);
            if(_studioPresentation is IStudioNativeMapPresentation native)
            {
                LogicalChildren.Add(native.NativeControl);VisualChildren.Add(native.NativeControl);InvalidateMeasure();
            }
        }
        catch(Exception ex)
        {
            _studioPresentationFailed=true;ReleaseStudioPresentation();
            Console.Error.WriteLine("[studio] Map host unavailable: "+ex.Message);
        }
    }
    public StudioViewportImage? CaptureStudioViewport()
    {
        Dispatcher.UIThread.VerifyAccess();
        return _studioPresentation?.Active==true && Layout.IsValid ? _studioPresentation.Render(BuildRenderFrame(Layout)) : null;
    }
    private void DegradeStudioPresentation(Exception ex)
    {
        _studioPresentationFailed=true;
        // Rendering cannot mutate the visual tree. Tear down native ownership
        // after this render transaction, then request the CPU fallback picture.
        Dispatcher.UIThread.Post(()=>{ReleaseStudioPresentation();InvalidateVisual();});
        Console.Error.WriteLine("[studio] Map presentation degraded to CPU: "+ex.Message);
    }

    private bool DrawNativeStudioPresentation(DrawingContext context)
    {
        if (_capturingStudioOverlay || _studioPresentationFailed) return false;
        if (_studioPresentation is not IStudioNativeMapPresentation native || !Layout.IsValid) return false;
        if (!native.Active) return false;
        try
        {
            // The canonical overlays render only when the authoring control is
            // invalidated. Upload their CPU bitmap; normal GPU frames never read
            // the world back to the CPU or route it through the game UI surface.
            using var bitmap = new RenderTargetBitmap(new PixelSize(Layout.PixelWidth, Layout.PixelHeight),
                new Avalonia.Vector(96 * Layout.RenderScale, 96 * Layout.RenderScale));
            _capturingStudioOverlay = true;
            try { using var drawing = bitmap.CreateDrawingContext(true); Render(drawing); }
            finally { _capturingStudioOverlay = false; }
            byte[] rgba = new byte[checked(Layout.PixelWidth * Layout.PixelHeight * 4)];
            using (var converted = new WriteableBitmap(new PixelSize(Layout.PixelWidth,Layout.PixelHeight),new Avalonia.Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Premul))
            using (var pixels = converted.Lock())
            { bitmap.CopyPixels(pixels); for (int row=0;row<Layout.PixelHeight;row++) Marshal.Copy(pixels.Address+row*pixels.RowBytes,rgba,row*Layout.PixelWidth*4,Layout.PixelWidth*4); }
            native.Present(BuildRenderFrame(Layout), new(Layout.PixelWidth, Layout.PixelHeight, rgba));
            return true;
        }
        catch (Exception ex)
        {
            DegradeStudioPresentation(ex);return false;
        }
    }

    private bool DrawStudioPresentation(DrawingContext context)
    {
        if (_studioPresentationFailed) return false;
        if (_studioPresentation is IStudioNativeMapPresentation) return false;
        if (_studioPresentation == null || !Layout.IsValid) return false;
        try
        {
            var image = _studioPresentation.Render(BuildRenderFrame(Layout));
            if (_studioBitmap == null || _studioBitmap.PixelSize.Width != image.Width || _studioBitmap.PixelSize.Height != image.Height)
            {
                _studioBitmap?.Dispose();
                _studioBitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
            }
            using (var target = _studioBitmap.Lock())
                for (int row = 0; row < image.Height; row++)
                    Marshal.Copy(image.Rgba, row * image.Width * 4, target.Address + row * target.RowBytes, image.Width * 4);
            context.DrawImage(_studioBitmap, new Rect(0, 0, image.Width, image.Height), new Rect(Bounds.Size));
            return true;
        }
        catch (Exception ex)
        {
            DegradeStudioPresentation(ex);
            return false;
        }
    }

    private void ReleaseStudioPresentation()
    {
        if (_studioPresentation is IStudioNativeMapPresentation native)
        { VisualChildren.Remove(native.NativeControl); LogicalChildren.Remove(native.NativeControl); }
        _studioPresentation?.Dispose(); _studioPresentation = null;
        _studioBitmap?.Dispose(); _studioBitmap = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_studioPresentation is IStudioNativeMapPresentation native) native.NativeControl.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_studioPresentation is IStudioNativeMapPresentation native) native.NativeControl.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
#endif
