using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static partial class Program
{
    private static async Task CheckNativeStartupAsync(string directory,string output)
    {
        var samples=new List<NativeStartup>();
        for(int index=0;index<5;index++)
        {
            using Process process=await StartNativeProbeAsync(Path.Combine(directory,"startup-"+index));
            try
            {
                NativeStartup sample=NativeStartups[process.Id];samples.Add(sample);
                Check(sample.UsableHomeMilliseconds>0&&sample.WorkingSetBytes>0&&sample.Width>=900&&sample.Height>=600
                    &&sample.PixelWidth==(int)Math.Round(sample.Width*sample.RenderScale)&&sample.PixelHeight==(int)Math.Round(sample.Height*sample.RenderScale),
                    "native Home startup records usable layout, process memory, logical/physical dimensions "+index);
                await CloseNativeProbeAsync(process);
            }
            finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
        }
        double[] ordered=samples.Select(sample=>sample.UsableHomeMilliseconds).Order().ToArray();
        string path=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(new{Scope="Five owned fresh native processes after earlier binaries warmed the OS cache; each stops at usable Home layout",Samples=samples,
            MedianMilliseconds=ordered[2],TailMilliseconds=ordered[^1],MedianWorkingSetBytes=samples.Select(sample=>sample.WorkingSetBytes).Order().ElementAt(2)},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Native startup evidence: "+path);
    }

    private static async Task CheckNativeDenseMapAsync(string directory,string output)
    {
        var project=MapTemplates.Create("STUDIO_DENSE_PERFORMANCE",false);project.Definition.Geometry.Clear();
        for(int index=0;index<1024;index++)project.Definition.Geometry.Add(new MapBox{Label="Dense box "+index,Material=0,
            Transform=new(){Position=[(index%32-15.5f)*1.5f,(index%5)*.1f,(index/32-15.5f)*1.5f],Scale=[1,1,1]}});
        string source=Path.Combine(directory,"dense-map.json");MapProjectSerializer.Save(project,source);
        byte[] original=File.ReadAllBytes(source);
        using Process process=await StartNativeProbeAsync(Path.Combine(directory,"dense-profile"));
        try
        {
            // Resize the actual viewport to 2560x1440 after hiding its side panels.
            await process.StandardInput.WriteLineAsync("resize 1280 720");await process.StandardInput.FlushAsync();
            string dimensions=await ReadNativeLineAsync(process,"RESIZED ");
            using(Process forwarded=StartStudioExecutable(Path.Combine(directory,"dense-profile"),["--map",source]))
            {await forwarded.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(forwarded.ExitCode==0,"dense canonical map opens through production forwarding");}
            await WaitForNativeDocumentAsync(process,source);
            await process.StandardInput.WriteLineAsync("focus-map");await process.StandardInput.FlushAsync();await ReadNativeLineAsync(process,"MAP-FOCUSED");
            await process.StandardInput.WriteLineAsync("frame-all");await process.StandardInput.FlushAsync();await ReadNativeLineAsync(process,"FRAMED");
            NativeSnapshot initial=await WaitForNativeMapMetricsAsync(process,1);
            using var requested=JsonDocument.Parse(dimensions[8..]);double clientWidth=requested.RootElement.GetProperty("Width").GetDouble(),clientHeight=requested.RootElement.GetProperty("Height").GetDouble();
            for(int attempt=0;attempt<4;attempt++)
            {
                NativeViewport bounds=initial.MapBounds.Single();if(bounds.PixelWidth==2560&&bounds.PixelHeight==1440)break;
                clientWidth+=2560/bounds.RenderScale-bounds.Width;clientHeight+=1440/bounds.RenderScale-bounds.Height;
                await process.StandardInput.WriteLineAsync($"resize {(int)Math.Round(clientWidth)} {(int)Math.Round(clientHeight)}");await process.StandardInput.FlushAsync();
                dimensions=await ReadNativeLineAsync(process,"RESIZED ");
                using var measured=JsonDocument.Parse(dimensions[8..]);
                clientWidth=measured.RootElement.GetProperty("Width").GetDouble();clientHeight=measured.RootElement.GetProperty("Height").GetDouble();
                initial=await RedrawNativeMapAsync(process,initial.MetricsRevision);
                Console.WriteLine("Dense resize attempt "+attempt+": "+dimensions+" viewport="+JsonSerializer.Serialize(initial.MapBounds));
            }
            NativeViewport measuredViewport=initial.MapBounds.Single();
            bool requestedViewportReached=measuredViewport is {PixelWidth:2560,PixelHeight:1440};
            if(!requestedViewportReached)Console.WriteLine("Native display/window manager limited requested 2560x1440 viewport to "+JsonSerializer.Serialize(measuredViewport));
            Check(measuredViewport.PixelWidth==2560&&measuredViewport.PixelHeight>=900&&measuredViewport.PixelHeight<=1440,
                "dense-map native viewport reports allocated physical dimensions under current display constraints");
            var samples=new List<NativeSnapshot>();
            for(int index=0;index<20;index++){initial=await RedrawNativeMapAsync(process,initial.MetricsRevision);samples.Add(initial);}
            Check(samples.All(sample=>sample.MapMetrics is {ReadbackBytes:0,ResidentMeshes:>0,DrawCalls:>0}
                &&sample.MapMetrics.MeshUploads==initial.MapMetrics!.MeshUploads&&sample.MapMetrics.GeometryUploadBytes==initial.MapMetrics.GeometryUploadBytes),
                "actual dense native map retains uploads through measured redraws and avoids full-frame GPU readback");
            Check(original.SequenceEqual(File.ReadAllBytes(source)),"dense native performance sampling preserves canonical authored source bytes");
            string path=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string capturePath=Path.ChangeExtension(path,".png");
            await process.StandardInput.WriteLineAsync("capture-map "+capturePath);await process.StandardInput.FlushAsync();
            string captured=await ReadNativeLineAsync(process,"MAP-CAPTURED ");
            using var captureDimensions=JsonDocument.Parse(captured[13..]);
            CheckNativePng(capturePath,captureDimensions.RootElement.GetProperty("Width").GetInt32(),captureDimensions.RootElement.GetProperty("Height").GetInt32(),
                "explicit diagnostic native dense-map capture renders canonical retained GPU pixels");
            File.WriteAllText(path,JsonSerializer.Serialize(new{Scope="Actual native retained Map presentation; GPU milliseconds unavailable when driver timestamps are unavailable",AuthoredObjects=1024,
                RequestedViewport=new{PixelWidth=2560,PixelHeight=1440},RequestedViewportReached=requestedViewportReached,
                WindowDimensions=JsonSerializer.Deserialize<JsonElement>(dimensions[8..]),ViewportDimensions=initial.MapBounds.Single(),DiagnosticCapture=capturePath,Samples=samples},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("Native dense-map evidence: "+path);
            await CloseNativeProbeAsync(process);
        }
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }
    private static async Task<NativeSnapshot> RedrawNativeMapAsync(Process process,long before)
    {
        await process.StandardInput.WriteLineAsync("redraw-map");await process.StandardInput.FlushAsync();await ReadNativeLineAsync(process,"MAP-REDRAW");
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(10))
        {
            var snapshot=await NativeStatusAsync(process);if(snapshot.MetricsRevision>before)return snapshot;await Task.Delay(10);
        }
        throw new TimeoutException("Benign Map invalidation did not publish a new measured native render submission.");
    }
}
