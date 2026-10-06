using MphRead.Mods.MapEditor;
using MphRead.Mods.StudioRendering;
using ProjectPrime.Studio.Map;

namespace ProjectPrime.Studio.Rendering;

/// <summary>Application device ownership, separate from every document and native surface lifetime.</summary>
public static class StudioGraphicsHost
{
    private sealed class DocumentWorld(EditorRenderWorld world)
    { internal EditorRenderWorld World = world; internal int References; internal StudioRenderMetrics? Metrics; }
    private static StudioRenderDevice? _device;
    private static readonly Dictionary<MapDocument,DocumentWorld> Worlds = new();
    public static bool SafeMode { get; private set; }
    public static bool HasDevice => _device != null;
    public static string? Backend => _device?.Backend;
    public static string? Adapter => _device?.Adapter;
    public static int? DeviceGeneration => _device?.Generation;
    public static int ResidentWorldCount => _device?.ResidentWorldCount ?? 0;
    public static int NativeSurfaceCount => _device?.NativeSurfaceCount ?? 0;
    public static int ViewportSurfaceCount => _device?.ViewportSurfaceCount ?? 0;
    public static StudioRenderMetrics? LastMetrics { get; private set; }
    public static double? LastReplayCpuMilliseconds { get; private set; }
    public static event Action<StudioRenderMetrics>? DiagnosticsChanged;
    public static event Action<double>? ReplayCpuFrameMeasured;
    public static StudioRenderMetrics? GetMapMetrics(MapDocument document)
        => _device?.NativeResourcesAvailable==true && Worlds.TryGetValue(document,out var entry)
            && entry.Metrics?.DeviceGeneration==_device.Generation ? entry.Metrics : null;
    internal static void Report(MapDocument document,StudioRenderMetrics metrics)
    {
        if(Worlds.TryGetValue(document,out var entry))entry.Metrics=metrics;
        LastMetrics=metrics;DiagnosticsChanged?.Invoke(metrics);
    }
    internal static void ReportReplayFrame(double milliseconds) {LastReplayCpuMilliseconds=milliseconds;ReplayCpuFrameMeasured?.Invoke(milliseconds);}
    public static StudioRenderDevice Device => SafeMode ? throw new InvalidOperationException("Graphics are disabled in Studio safe mode.") : _device ??= new StudioRenderDevice();
    public static void Initialize(bool safeMode)
    {
        SafeMode=safeMode;
        StudioViewportConfiguration.MapPresentationFactory=safeMode ? null : document => new MapViewportHost(document);
    }
    internal static EditorRenderWorld AcquireWorld(MapDocument document)
    {
        if(!Worlds.TryGetValue(document,out var entry)) Worlds[document]=entry=new(Device.CreateWorld());
        entry.References++;return entry.World;
    }
    internal static void ReleaseWorld(MapDocument document)
    {
        if(!Worlds.TryGetValue(document,out var entry))return;
        if(--entry.References != 0)return;
        Worlds.Remove(document);entry.World.Dispose();
    }
    public static void Shutdown()
    {
        StudioViewportConfiguration.MapPresentationFactory=null;
        foreach(var world in Worlds.Values)world.World.Dispose();Worlds.Clear();
        _device?.Dispose();_device=null;
        LastMetrics=null;LastReplayCpuMilliseconds=null;
    }
}
