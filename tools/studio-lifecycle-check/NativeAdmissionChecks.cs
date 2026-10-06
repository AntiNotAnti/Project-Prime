using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.StudioRendering;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Shell;
using SkiaSharp;

internal static partial class Program
{
    private sealed record NativeAdmissionCounts(long Created,long Released,long Live,int Registered,int Worlds,int Targets);
    private sealed record NativeAdmissionReport(NativeAdmissionCounts Before,NativeAdmissionCounts Rejected,NativeAdmissionCounts Closed,
        string? Error,bool CpuFallback,bool CanonicalRetained,bool Editable,bool UndoRestored,bool PreviewWarningVisible,
        int CpuGeometryPixels,int WarningPixels,int Width,int Height,bool CloseAccepted,string CapturePath);

    private static NativeAdmissionCounts ReadNativeAdmissionCounts()
    {
        var device=StudioGraphicsHost.Device;
        return new(device.NativeSurfaceHandlesCreated,device.NativeSurfaceHandlesReleased,device.LiveNativeSurfaceHandles,
            device.NativeSurfaceCount,device.ResidentWorldCount,device.ViewportSurfaceCount);
    }

    private static async Task<bool> TryHandleNativeAdmissionCommandAsync(StudioWindow window,string command)
    {
        const string prefix="native-admission-check ";
        if(!command.StartsWith(prefix,StringComparison.Ordinal))return false;
        if(!StudioGraphicsHost.HasDevice || window.Documents.Documents.Count!=0)
            throw new InvalidOperationException("Native admission proof requires an existing healthy device and all previous documents closed.");
        string capturePath=Path.GetFullPath(command[prefix.Length..]);
        NativeAdmissionCounts before=ReadNativeAdmissionCounts();
        if(before.Live!=0 || before.Registered!=0 || before.Worlds!=0 || before.Targets!=0)
            throw new InvalidOperationException("Previous native Map resources did not reach zero before admission failure injection.");
        var device=StudioGraphicsHost.Device;
        device.FailNextNativeSurfaceAdmissionForDiagnostics();
        window.NewMapProject("NATIVE_ADMISSION_CPU_FALLBACK",example:true);
        var document=(MapStudioDocument)window.Documents.ActiveDocument!;
        var canonical=document.Host.Document!;
        MapViewport? viewport=null;
        StudioNativeViewport? native=null;
        var deadline=Stopwatch.StartNew();
        while(deadline.Elapsed<TimeSpan.FromSeconds(5))
        {
            window.UpdateLayout();
            viewport=document.Host.GetVisualDescendants().OfType<MapViewport>().SingleOrDefault();
            if(viewport is not null)
            {
                var presentation=typeof(MapViewport).GetField("_studioPresentation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(viewport);
                native=(presentation as IStudioNativeMapPresentation)?.NativeControl as StudioNativeViewport;
                if(native?.GraphicsError is not null && !native.IsVisible)break;
            }
            await Task.Delay(20);
        }
        if(viewport is null || native?.GraphicsError is null || native.IsVisible)
            throw new InvalidOperationException("Actual native surface admission did not report and contain its injected capability failure.");
        string error=native.GraphicsError;
        bool canonicalRetained=ReferenceEquals(document.Host.Document,canonical)&&canonical.Project.Definition.Geometry.Count>0;
        // This actual authoring preview already provides a visible CPU-availability
        // explanation when a native GPU mode cannot be presented.
        document.Host.SetViewportMode("Lighting");viewport.FrameAll();window.UpdateLayout();
        bool cpuFallback=native.Surface is null && document.Host.CaptureViewport() is null
            && typeof(MapViewport).GetProperty("GpuActive",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(viewport) is false;
        double scale=window.RenderScaling;
        int width=Math.Max(1,(int)Math.Round(viewport.Bounds.Width*scale));
        int height=Math.Max(1,(int)Math.Round(viewport.Bounds.Height*scale));
        Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
        using(var bitmap=new RenderTargetBitmap(new PixelSize(width,height),new Vector(96*scale,96*scale)))
        { bitmap.Render(viewport);bitmap.Save(capturePath,new PngBitmapEncoderOptions()); }
        string evidenceDirectory=Path.Combine(OperatingSystem.IsWindows()?Path.GetTempPath():"/tmp","project-prime-native-admission-evidence");
        Directory.CreateDirectory(evidenceDirectory);
        string evidencePath=Path.Combine(evidenceDirectory,"native-admission-cpu-fallback.png");
        File.Copy(capturePath,evidencePath,overwrite:true);
        int geometryPixels=0,warningPixels=0;
        using(var image=SKBitmap.Decode(capturePath)??throw new InvalidOperationException("CPU fallback viewport capture did not decode."))
        {
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
            {
                var pixel=image.GetPixel(x,y);
                // Canonical CPU geometry uses these exact shade offsets. Its grid
                // and background differ, so they cannot satisfy this evidence.
                if(pixel.Red>=35 && pixel.Red<=200 && pixel.Green-pixel.Red==15 && pixel.Blue-pixel.Red==30)geometryPixels++;
                if(y<Math.Ceiling(45*scale) && pixel.Red>160 && pixel.Green>130 && pixel.Blue<110)warningPixels++;
            }
        }
        bool previewWarning=typeof(MapViewport).GetField("_unavailablePreviewText",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(viewport)
            is FormattedText {Width:>0,Height:>0} && warningPixels>10;
        string beforeEdit=canonical.Project.Definition.Serialize();
        var originalState=canonical.CurrentStateId;
        Guid objectId=canonical.Project.Definition.Geometry[0].Id;
        canonical.EditObjects("CPU fallback edit after failed admission",[objectId],definition=>definition.Geometry[0].Transform.Scale[0]+=.25f);
        bool editable=canonical.CurrentStateId!=originalState && canonical.Project.Definition.Serialize()!=beforeEdit && canonical.History.CanUndo;
        document.Host.Undo();
        bool undoRestored=canonical.CurrentStateId==originalState && canonical.Project.Definition.Serialize()==beforeEdit;
        NativeAdmissionCounts rejected=ReadNativeAdmissionCounts();
        bool closed=await window.Documents.RequestCloseAsync(document,_=>Task.FromResult(StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null));
        window.UpdateLayout();await Task.Delay(20);
        var report=new NativeAdmissionReport(before,rejected,ReadNativeAdmissionCounts(),error,cpuFallback,canonicalRetained,editable,undoRestored,
            previewWarning,geometryPixels,warningPixels,width,height,closed,evidencePath);
        Console.WriteLine("NATIVE-ADMISSION "+JsonSerializer.Serialize(report));Console.Out.Flush();return true;
    }

    private static async Task CheckNativeAdmissionFailureAsync(Process process,string capturePath)
    {
        await process.StandardInput.WriteLineAsync("native-admission-check "+capturePath);await process.StandardInput.FlushAsync();
        string line=await ReadNativeLineAsync(process,"NATIVE-ADMISSION ");
        var report=JsonSerializer.Deserialize<NativeAdmissionReport>(line[17..])??throw new InvalidOperationException("Native admission report missing.");
        Check(report.Before is {Live:0,Registered:0,Worlds:0,Targets:0},"native admission starts after the previous Map releases all resources");
        Check(report.Rejected.Created==report.Before.Created+1 && report.Rejected.Released==report.Before.Released+1,
            "real native capability rejection creates and releases exactly one unregistered surface handle");
        Check(report.Rejected.Live==report.Before.Live && report.Rejected.Registered==report.Before.Registered,
            "failed native constructor leaves no tracked or hidden native surface handle");
        Check(report.Error?.Contains("no supported presentation format",StringComparison.Ordinal)==true && report.CpuFallback && report.CanonicalRetained,
            "actual failed admission retains its diagnostic and canonical Map through CPU fallback");
        Check(report.Width>0 && report.Height>0 && report.CpuGeometryPixels>32 && File.Exists(capturePath),
            "native admission fallback visibly renders canonical CPU geometry rather than an empty viewport");
        Check(report.PreviewWarningVisible && report.WarningPixels>10,"CPU authoring viewport visibly explains the unavailable native preview");
        Check(report.Editable && report.UndoRestored,"canonical Map editing and Undo remain usable after native admission fails");
        Check(report.CloseAccepted && report.Closed is {Live:0,Registered:0,Worlds:0,Targets:0}
            && report.Closed.Created==report.Rejected.Created && report.Closed.Released==report.Rejected.Released,
            "normal fallback document close releases ownership without hiding or double-releasing a native handle");
        Console.WriteLine("Native admission failure proof: "+JsonSerializer.Serialize(report));
    }
}
