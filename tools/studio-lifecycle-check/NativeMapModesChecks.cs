using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using SkiaSharp;

internal static partial class Program
{
    private static async Task CheckNativeMapModesAsync(string directory,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var project=MapTemplates.Create("NATIVE_MODE_ACCEPTANCE",true);
        project.Definition.Light1Color=[31,4,3];project.Definition.Light2Color=[2,3,20];
        project.Definition.FogEnabled=true;project.Definition.FogColor=[3,26,8];project.Definition.FogSlope=3;project.Definition.FogOffset=0;
        string source=Path.Combine(directory,"native-modes.json");MapProjectSerializer.Save(project,source);
        byte[] immutable=File.ReadAllBytes(source);string profile=Path.Combine(directory,"mode-profile");
        using Process process=await StartNativeProbeAsync(profile);
        try
        {
            using(Process forwarded=StartStudioExecutable(profile,["--map",source]))
            {await forwarded.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(forwarded.ExitCode==0,"actual native Map modes open through production forwarding");}
            await WaitForNativeDocumentAsync(process,source);NativeSnapshot measured=await WaitForNativeMapMetricsAsync(process,1);
            var uploads=measured.MapMetrics!.MeshUploads;var geometry=measured.MapMetrics.GeometryUploadBytes;
            var evidence=new List<object>();var hashes=new Dictionary<string,string>();
            foreach(string mode in new[]{"Rendered","Lighting","Shadows","Fog","Terrain","Overdraw","Texel density","Material ID"})
            {
                await process.StandardInput.WriteLineAsync("map-mode "+mode);await process.StandardInput.FlushAsync();await ReadNativeLineAsync(process,"MAP-MODE");
                measured=await RedrawNativeMapAsync(process,measured.MetricsRevision);
                Check(measured.MapMetrics is {ReadbackBytes:0}&&measured.MapMetrics.MeshUploads==uploads&&measured.MapMetrics.GeometryUploadBytes==geometry,
                    "actual native "+mode+" mode retains canonical mesh uploads and no normal-frame GPU readback");
                string path=Path.Combine(output,"native-map-"+mode.ToLowerInvariant().Replace(' ','-')+".png");
                await process.StandardInput.WriteLineAsync("capture-map "+path);await process.StandardInput.FlushAsync();await ReadNativeLineAsync(process,"MAP-CAPTURED ");
                using var bitmap=SKBitmap.Decode(path)??throw new InvalidDataException("Native mode PNG is unreadable.");
                Check(bitmap.Width==measured.MapBounds.Single().PixelWidth&&bitmap.Height==measured.MapBounds.Single().PixelHeight
                    &&bitmap.Pixels.Distinct().Count()>1,"explicit native "+mode+" diagnostic capture contains actual allocated GPU pixels");
                hashes[mode]=Convert.ToHexString(SHA256.HashData(bitmap.Bytes));
                evidence.Add(new{Mode=mode,Path=path,RgbaHash=hashes[mode],DistinctColors=bitmap.Pixels.Distinct().Count(),Metrics=measured.MapMetrics,Viewport=measured.MapBounds.Single()});
            }
            File.WriteAllText(Path.Combine(output,"native-map-modes.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
            Check(hashes["Lighting"]!=hashes["Rendered"]&&hashes["Fog"]!=hashes["Rendered"]&&hashes.Values.Distinct().Count()>=5,
                "actual native BGRA host presents authored lighting/fog and distinct diagnostic mode pixels");
            Check(immutable.SequenceEqual(File.ReadAllBytes(source)),"native diagnostic presentation preserves canonical authored source bytes");
            await process.StandardInput.WriteLineAsync("focus-map");await process.StandardInput.FlushAsync();await ReadNativeLineAsync(process,"MAP-FOCUSED");
            measured=await RedrawNativeMapAsync(process,measured.MetricsRevision);
            await CheckNativeHudAsync(process,Path.Combine(output,"native-map-performance-hud.png"),measured);
            await CloseNativeProbeAsync(process);
        }
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }

    private static async Task CheckNativeHudAsync(Process process,string capture,NativeSnapshot before)
    {
        await process.StandardInput.WriteLineAsync("capture-hud "+capture);await process.StandardInput.FlushAsync();
        string result=await ReadNativeLineAsync(process,"HUD-CAPTURED ");using var metadata=JsonDocument.Parse(result[13..]);var hud=metadata.RootElement;
        Check(hud.GetProperty("Visible").GetBoolean()&&hud.GetProperty("OwnsWindow").GetBoolean()&&hud.GetProperty("NativeHandle").GetInt64()!=0
            &&hud.GetProperty("Sampling").GetBoolean()&&hud.GetProperty("Snapshot").ValueKind==JsonValueKind.Object,
            "actual graphics HUD uses a visible owned native window with active measured sources");
        var snapshot=hud.GetProperty("Snapshot");
        Check(snapshot.GetProperty("ProcessWorkingSetBytes").GetInt64()>0&&snapshot.GetProperty("Sources").EnumerateArray()
            .Any(source=>source.GetProperty("Graphics") is {ValueKind:JsonValueKind.Object} graphics&&graphics.GetProperty("DeviceCreated").GetBoolean())
            &&snapshot.GetProperty("Sources").EnumerateArray().Any(source=>source.GetProperty("Render") is {ValueKind:JsonValueKind.Object} render
                &&render.GetProperty("CpuMilliseconds").GetDouble()>0&&render.GetProperty("GpuMilliseconds").ValueKind==JsonValueKind.Null),
            "native HUD reports actual device, positive process memory and measured submission CPU while unavailable GPU time remains null");
        CheckNativePng(capture,hud.GetProperty("Width").GetInt32(),hud.GetProperty("Height").GetInt32(),"native HUD window renders readable metrics independently of native viewport airspace");
        var after=await NativeStatusAsync(process);
        Check(before.MapBounds.SequenceEqual(after.MapBounds),"native HUD owned window preserves full-width viewport bounds with inspector hidden");
        File.WriteAllText(Path.ChangeExtension(capture,".json"),metadata.RootElement.GetRawText());
        await process.StandardInput.WriteLineAsync("hide-hud");await process.StandardInput.FlushAsync();using var hidden=JsonDocument.Parse((await ReadNativeLineAsync(process,"HUD-HIDDEN "))[11..]);
        Check(!hidden.RootElement.GetProperty("Sampling").GetBoolean()&&hidden.RootElement.GetProperty("Released").GetBoolean()
            &&hidden.RootElement.GetProperty("WindowReleased").GetBoolean(),"closing native HUD releases sampling, providers and owned window");
    }
}
