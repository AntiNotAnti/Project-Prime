#if MPHREAD_STUDIO
using System;
using System.Runtime.InteropServices;
using OpenTK;

namespace MphRead.Mods.StudioRendering;

/// <summary>
/// GPU-only transport between the canonical desktop compatibility renderer and
/// Avalonia's macOS core context. IOSurface shares storage, not GL object names.
/// All canonical scenes use one serialized legacy context; all presentation
/// objects stay in the Avalonia context which created this surface.
/// </summary>
internal sealed class MacLegacyGlSurface : IDisposable
{
    private static readonly object DeviceGate = new();
    private static IntPtr _legacyContext;
    private static int _references;
    private static long _deviceGeneration;
    [ThreadStatic] private static long _bindingsGeneration;
    private readonly IntPtr _presentationContext;
    private IntPtr _surface;
    private uint _legacyTexture, _legacyFramebuffer, _depth;
    private uint _presentationTexture, _presentationFramebuffer;
    private int _width, _height;
    private bool _disposed;
    internal string Renderer { get; }

    internal MacLegacyGlSurface()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        _presentationContext = Native.CGLGetCurrentContext();
        if (_presentationContext == IntPtr.Zero)
            throw new InvalidOperationException("The native presentation context is not current.");
        lock (DeviceGate)
        {
            IntPtr format = IntPtr.Zero;
            try
            {
                if (_legacyContext == IntPtr.Zero)
                {
                    // Public CGL's legacy profile exposes Apple's OpenGL 2.1,
                    // GLSL 1.20 and the display lists used by the game renderer.
                    Check(Native.CGLChoosePixelFormat([99, 0x1000, 73, 0], out format, out _), "choose a compatibility pixel format");
                    Check(Native.CGLCreateContext(format, IntPtr.Zero, out _legacyContext), "create a compatibility context");
                    _deviceGeneration++;
                }
                string renderer = "OpenGL";
                WithLegacy(() => renderer = Marshal.PtrToStringAnsi(Native.glGetString(0x1F01)) ?? renderer);
                Renderer = renderer;
                // Publish ownership only after all fallible initialization,
                // including activation/restoration and canonical bindings.
                _references++;
            }
            catch
            {
                if (_references == 0 && _legacyContext != IntPtr.Zero)
                {
                    Native.CGLDestroyContext(_legacyContext);
                    _legacyContext = IntPtr.Zero;
                }
                throw;
            }
            finally { if (format != IntPtr.Zero) Native.CGLDestroyPixelFormat(format); }
        }
    }

    /// <summary>Run creation/destruction on the same device as all Studio scenes.</summary>
    internal void RunInLegacy(Action action)
    {
        lock (DeviceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RequirePresentationContext();
            WithLegacy(action);
        }
    }

    /// <summary>Draw canonical GL into shared GPU storage, then copy on the GPU.</summary>
    internal void Render(int width, int height, int targetFramebuffer, Action draw)
    {
        if (width < 1 || height < 1) return;
        lock (DeviceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RequirePresentationContext();
            if (width != _width || height != _height) Resize(width, height);
            WithLegacy(() =>
            {
                Native.glBindFramebuffer(Framebuffer, _legacyFramebuffer);
                draw();
                // Explicit synchronization is required across independent CGL
                // contexts. Correctness first: this can later use GPU fences.
                Native.glFinish();
            });
            Native.glGetIntegerv(DrawFramebufferBinding, out int previousDraw);
            Native.glGetIntegerv(ReadFramebufferBinding, out int previousRead);
            bool scissor = Native.glIsEnabled(ScissorTest) != 0;
            bool srgb = Native.glIsEnabled(FramebufferSrgb) != 0;
            try
            {
                Native.glDisable(ScissorTest);
                Native.glDisable(FramebufferSrgb);
                // Rebind after the producer flush as required by CGLIOSurface.
                Native.glBindFramebuffer(ReadFramebuffer, _presentationFramebuffer);
                Native.glBindFramebuffer(DrawFramebuffer, (uint)targetFramebuffer);
                Native.glBlitFramebuffer(0, 0, width, height, 0, 0, width, height, 0x4000, 0x2600);
                // Complete this read before another viewport/frame can write
                // the shared storage, including immediately after a resize.
                Native.glFinish();
            }
            finally
            {
                Native.glBindFramebuffer(DrawFramebuffer, (uint)previousDraw);
                Native.glBindFramebuffer(ReadFramebuffer, (uint)previousRead);
                if (scissor) Native.glEnable(ScissorTest);
                if (srgb) Native.glEnable(FramebufferSrgb);
            }
        }
    }

    private void Resize(int width, int height)
    {
        DeleteStorage();
        _surface = Native.CreateSurface(width, height);
        if (_surface == IntPtr.Zero) throw new InvalidOperationException("macOS could not allocate the viewport IOSurface.");
        try
        {
            WithLegacy(() =>
            {
                CreateAttachment(_legacyContext, width, height, out _legacyTexture, out _legacyFramebuffer);
                Native.glGenRenderbuffers(1, out _depth);
                Native.glBindRenderbuffer(0x8D41, _depth);
                Native.glRenderbufferStorage(0x8D41, 0x88F0, width, height); // depth24/stencil8
                Native.glFramebufferRenderbuffer(Framebuffer, 0x821A, 0x8D41, _depth);
                RequireComplete();
            });
            Native.glGetIntegerv(DrawFramebufferBinding, out int draw);
            Native.glGetIntegerv(ReadFramebufferBinding, out int read);
            try { CreateAttachment(_presentationContext, width, height, out _presentationTexture, out _presentationFramebuffer); }
            finally
            {
                Native.glBindFramebuffer(DrawFramebuffer, (uint)draw);
                Native.glBindFramebuffer(ReadFramebuffer, (uint)read);
            }
            _width = width; _height = height;
        }
        catch { DeleteStorage(); throw; }
    }

    private void CreateAttachment(IntPtr context, int width, int height, out uint texture, out uint framebuffer)
    {
        Native.glGetIntegerv(0x84F6, out int previousTexture); // current unit rectangle binding
        Native.glGenTextures(1, out texture);
        try
        {
            Native.glBindTexture(0x84F5, texture);
            Check(Native.CGLTexImageIOSurface2D(context, 0x84F5, 0x8058, width, height, 0x80E1, 0x8367, _surface, 0), "import viewport GPU storage");
        }
        finally { Native.glBindTexture(0x84F5, (uint)previousTexture); }
        Native.glGenFramebuffers(1, out framebuffer);
        Native.glBindFramebuffer(Framebuffer, framebuffer);
        Native.glFramebufferTexture2D(Framebuffer, 0x8CE0, 0x84F5, texture, 0);
        RequireComplete();
    }

    private static void RequireComplete()
    {
        uint status = Native.glCheckFramebufferStatus(Framebuffer);
        if (status != 0x8CD5) throw new InvalidOperationException($"The macOS viewport framebuffer is incomplete (0x{status:X}).");
    }

    private void DeleteStorage()
    {
        // The caller holds DeviceGate and has the presentation context current.
        if (_presentationFramebuffer != 0) Native.glDeleteFramebuffers(1, ref _presentationFramebuffer);
        if (_presentationTexture != 0) Native.glDeleteTextures(1, ref _presentationTexture);
        _presentationFramebuffer = _presentationTexture = 0;
        WithLegacy(() =>
        {
            if (_legacyFramebuffer != 0) Native.glDeleteFramebuffers(1, ref _legacyFramebuffer);
            if (_legacyTexture != 0) Native.glDeleteTextures(1, ref _legacyTexture);
            if (_depth != 0) Native.glDeleteRenderbuffers(1, ref _depth);
            _legacyFramebuffer = _legacyTexture = _depth = 0;
        });
        if (_surface != IntPtr.Zero) Native.CFRelease(_surface);
        _surface = IntPtr.Zero; _width = _height = 0;
    }

    public void Dispose()
    {
        lock (DeviceGate)
        {
            if (_disposed) return;
            RequirePresentationContext();
            try { DeleteStorage(); }
            finally
            {
                _disposed = true;
                if (--_references == 0)
                {
                    try { WithLegacy(Mods.Render.OpenGlGpuProfiler.ReleaseCurrentContext); }
                    finally
                    {
                        Native.CGLDestroyContext(_legacyContext);
                        _legacyContext = IntPtr.Zero;
                    }
                }
            }
        }
    }

    private void RequirePresentationContext()
    {
        if (Native.CGLGetCurrentContext() != _presentationContext)
            throw new InvalidOperationException("The viewport must be accessed from its native current-context callback.");
    }
    private static void WithLegacy(Action action)
    {
        IntPtr previous = Native.CGLGetCurrentContext();
        Check(Native.CGLSetCurrentContext(_legacyContext), "activate the compatibility renderer");
        try
        {
            // The canonical facade owns capability resolution and retained GL
            // caches. Initialize once per actual device on each calling thread;
            // never reset a still-live device for another surface or frame.
            if (_bindingsGeneration != _deviceGeneration)
            {
                Mods.Render.GraphicsApi.LoadBindings(new NativeBindings());
                _bindingsGeneration = _deviceGeneration;
            }
            action();
        }
        finally { Check(Native.CGLSetCurrentContext(previous), "restore the presentation context"); }
    }
    private static void Check(int error, string operation)
    {
        if (error != 0) throw new InvalidOperationException($"macOS could not {operation} (CGL {error}).");
    }
    private const uint Framebuffer = 0x8D40, DrawFramebuffer = 0x8CA9, ReadFramebuffer = 0x8CA8;
    private const uint DrawFramebufferBinding = 0x8CA6, ReadFramebufferBinding = 0x8CAA;
    private const uint ScissorTest = 0x0C11, FramebufferSrgb = 0x8DB9;

    private sealed class NativeBindings : IBindingsContext
    {
        public IntPtr GetProcAddress(string name) => NativeLibrary.TryGetExport(Native.OpenGlHandle, name, out var address) ? address : IntPtr.Zero;
    }
    private static class Native
    {
        private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string IoSurface = "/System/Library/Frameworks/IOSurface.framework/IOSurface";
        internal static readonly IntPtr OpenGlHandle = NativeLibrary.Load(OpenGl);
        private static readonly IntPtr CfHandle = NativeLibrary.Load(CoreFoundation), IoHandle = NativeLibrary.Load(IoSurface);
        internal static IntPtr CreateSurface(int width, int height)
        {
            IntPtr dictionary = CFDictionaryCreateMutable(IntPtr.Zero, 0,
                NativeLibrary.GetExport(CfHandle, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(CfHandle, "kCFTypeDictionaryValueCallBacks"));
            try
            {
                // IOSurface may require platform-specific row alignment.
                int row = checked((int)IOSurfaceAlignProperty(Global("kIOSurfaceBytesPerRow"), (nuint)checked(width * 4)));
                int size = checked((int)IOSurfaceAlignProperty(Global("kIOSurfaceAllocSize"), (nuint)checked(row * height)));
                Set("kIOSurfaceWidth", width); Set("kIOSurfaceHeight", height);
                Set("kIOSurfaceBytesPerElement", 4); Set("kIOSurfaceBytesPerRow", row);
                Set("kIOSurfaceAllocSize", size); Set("kIOSurfacePixelFormat", 0x42475241);
                return IOSurfaceCreate(dictionary);
            }
            finally { CFRelease(dictionary); }
            void Set(string key, int value)
            {
                IntPtr number = CFNumberCreate(IntPtr.Zero, 9, ref value);
                try { CFDictionarySetValue(dictionary, Global(key), number); }
                finally { CFRelease(number); }
            }
        }
        private static IntPtr Global(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(IoHandle, name));
        [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keys, IntPtr values);
        [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFNumberCreate(IntPtr allocator, int type, ref int value);
        [DllImport(CoreFoundation)] internal static extern void CFRelease(IntPtr value);
        [DllImport(IoSurface)] private static extern IntPtr IOSurfaceCreate(IntPtr dictionary);
        [DllImport(IoSurface)] private static extern nuint IOSurfaceAlignProperty(IntPtr key, nuint value);
        [DllImport(OpenGl)] internal static extern int CGLChoosePixelFormat(int[] attributes, out IntPtr format, out int count);
        [DllImport(OpenGl)] internal static extern int CGLCreateContext(IntPtr format, IntPtr share, out IntPtr context);
        [DllImport(OpenGl)] internal static extern int CGLDestroyPixelFormat(IntPtr format);
        [DllImport(OpenGl)] internal static extern int CGLDestroyContext(IntPtr context);
        [DllImport(OpenGl)] internal static extern IntPtr CGLGetCurrentContext();
        [DllImport(OpenGl)] internal static extern int CGLSetCurrentContext(IntPtr context);
        [DllImport(OpenGl)] internal static extern int CGLTexImageIOSurface2D(IntPtr context, uint target, uint format, int width, int height, uint pixelFormat, uint type, IntPtr surface, uint plane);
        [DllImport(OpenGl)] internal static extern IntPtr glGetString(uint name);
        [DllImport(OpenGl)] internal static extern void glGetIntegerv(uint name, out int value);
        [DllImport(OpenGl)] internal static extern byte glIsEnabled(uint capability);
        [DllImport(OpenGl)] internal static extern void glEnable(uint capability);
        [DllImport(OpenGl)] internal static extern void glDisable(uint capability);
        [DllImport(OpenGl)] internal static extern void glFinish();
        [DllImport(OpenGl)] internal static extern void glGenTextures(int count, out uint texture);
        [DllImport(OpenGl)] internal static extern void glBindTexture(uint target, uint texture);
        [DllImport(OpenGl)] internal static extern void glDeleteTextures(int count, ref uint texture);
        [DllImport(OpenGl)] internal static extern void glGenFramebuffers(int count, out uint framebuffer);
        [DllImport(OpenGl)] internal static extern void glBindFramebuffer(uint target, uint framebuffer);
        [DllImport(OpenGl)] internal static extern void glDeleteFramebuffers(int count, ref uint framebuffer);
        [DllImport(OpenGl)] internal static extern void glFramebufferTexture2D(uint target, uint attachment, uint textureTarget, uint texture, int level);
        [DllImport(OpenGl)] internal static extern uint glCheckFramebufferStatus(uint target);
        [DllImport(OpenGl)] internal static extern void glGenRenderbuffers(int count, out uint renderbuffer);
        [DllImport(OpenGl)] internal static extern void glBindRenderbuffer(uint target, uint renderbuffer);
        [DllImport(OpenGl)] internal static extern void glDeleteRenderbuffers(int count, ref uint renderbuffer);
        [DllImport(OpenGl)] internal static extern void glRenderbufferStorage(uint target, uint format, int width, int height);
        [DllImport(OpenGl)] internal static extern void glFramebufferRenderbuffer(uint target, uint attachment, uint renderbufferTarget, uint renderbuffer);
        [DllImport(OpenGl)] internal static extern void glBlitFramebuffer(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, uint mask, uint filter);
    }
}
#endif
