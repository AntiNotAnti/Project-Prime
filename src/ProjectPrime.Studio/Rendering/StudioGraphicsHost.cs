using MphRead.Mods.StudioRendering;
namespace ProjectPrime.Studio.Rendering;

/// <summary>Desktop policy/diagnostics over the shared native OpenGL viewport host.</summary>
public static class StudioGraphicsHost
{
    public static bool SafeMode=>StudioGraphicsState.SafeMode;
    public static bool HasDevice=>StudioGraphicsState.HasDevice;
    public static string? Backend=>StudioGraphicsState.Backend;
    public static string? Adapter=>StudioGraphicsState.Adapter;
    public static int ResidentWorldCount=>StudioGraphicsState.ResidentWorldCount;
    public static int NativeSurfaceCount=>StudioGraphicsState.NativeSurfaceCount;
    public static int ViewportSurfaceCount=>StudioGraphicsState.ViewportSurfaceCount;
    public static void Initialize(bool safeMode)=>StudioGraphicsState.Configure(safeMode);
    public static void Shutdown()=>StudioGraphicsState.Shutdown();
}
