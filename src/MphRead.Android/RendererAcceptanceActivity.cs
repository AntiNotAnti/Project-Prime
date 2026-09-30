#if DEBUG
using System;
using System.IO;
using System.Runtime.InteropServices;
using Android.App;
using Android.OS;
using Android.Views;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Droid;

// Asset-free instrumentation entry point. Surface callbacks serialize creation,
// rendering and destruction on the activity thread, including recreation.
[Activity(Name = "com.projectprime.game.RendererAcceptanceActivity", Exported = true)]
public sealed class RendererAcceptanceActivity : Activity, ISurfaceHolderCallback
{
    private nint _window;
    private int _frames;
    [DllImport("android")] private static extern nint ANativeWindow_fromSurface(nint env, nint surface);
    [DllImport("android")] private static extern void ANativeWindow_release(nint window);
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        var view = new SurfaceView(this);
        view.Holder!.AddCallback(this);
        SetContentView(view);
    }
    public void SurfaceCreated(ISurfaceHolder holder) { }
    public void SurfaceChanged(ISurfaceHolder holder, Android.Graphics.Format format, int width, int height)
    {
        string report = Path.Combine(FilesDir!.AbsolutePath, "renderer-runtime-check.txt");
        try
        {
            ReleaseSurface();
            GraphicsBackendPolicy.Configure("vulkan");
            _window = ANativeWindow_fromSurface(Android.Runtime.JNIEnv.Handle, holder.Surface!.Handle);
            if (_window == 0) throw new InvalidOperationException("ANativeWindow unavailable");
            ModernGraphicsCompat.AttachAndroidWindow(_window, width, height);
            GraphicsApi.Viewport(0, 0, width, height);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.Color4(1f, 0f, 0f, 1f);
            GraphicsApi.Begin(PrimitiveType.Triangles);
            GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.Vertex3(3, -1, 0);
            GraphicsApi.Vertex3(-1, 3, 0);
            GraphicsApi.End();
            byte[] pixel = new byte[4];
            GraphicsApi.ReadPixels(width / 2, height / 2, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            ModernGraphicsCompat.Present();
            if (pixel[0] < 220 || pixel[1] > 24 || pixel[2] > 24)
                throw new InvalidOperationException($"Unexpected triangle pixel {string.Join(',', pixel)}");
            File.WriteAllText(report, $"PASS Vulkan device/surface/acquire/draw/readback/present frames={++_frames}\n");
        }
        catch (Exception ex) { File.WriteAllText(report, "FAIL " + ex + "\n"); }
    }
    public void SurfaceDestroyed(ISurfaceHolder holder) => ReleaseSurface();
    private void ReleaseSurface()
    {
        if (_window == 0) return;
        ModernGraphicsCompat.DetachAndroidWindow();
        ANativeWindow_release(_window);
        _window = 0;
    }
    protected override void OnDestroy()
    {
        ReleaseSurface();
        ModernGraphicsCompat.Shutdown();
        base.OnDestroy();
    }
}
#endif
