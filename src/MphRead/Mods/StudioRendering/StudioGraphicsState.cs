#if MPHREAD_AVALONIA && !ANDROID
using System;
using System.Threading;

namespace MphRead.Mods.StudioRendering;

/// <summary>Observed native creator surface lifetimes; GL objects remain owned by Avalonia's current-context callbacks.</summary>
public static class StudioGraphicsState
{
    private static int _surfaces;
    public static bool SafeMode { get; private set; }
    public static bool HasDevice => Volatile.Read(ref _surfaces) > 0;
    public static int NativeSurfaceCount => Volatile.Read(ref _surfaces);
    public static int ViewportSurfaceCount => NativeSurfaceCount;
    public static int ResidentWorldCount => NativeSurfaceCount;
    public static string? Backend { get; private set; }
    public static string? Adapter { get; private set; }
    public static void Configure(bool safeMode)
    {
        if (HasDevice) throw new InvalidOperationException("Close native viewports before changing the graphics policy.");
        SafeMode=safeMode;Backend=null;Adapter=null;
    }
    public static IDisposable RegisterSurface(string backend,string? adapter)
    {
        if(SafeMode)throw new InvalidOperationException("Native viewports are disabled in Safe Mode.");
        Backend=backend;Adapter=adapter;Interlocked.Increment(ref _surfaces);
        return new Registration();
    }
    public static void Shutdown()
    {
        // Surfaces release GL resources in OnOpenGlDeinit, which is the only
        // safe place to delete them. Never delete GL names from the UI thread.
        if(!HasDevice){Backend=null;Adapter=null;}
    }
    private sealed class Registration : IDisposable
    {
        private int _disposed;
        public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)==0)Interlocked.Decrement(ref _surfaces);}
    }
}
#endif
