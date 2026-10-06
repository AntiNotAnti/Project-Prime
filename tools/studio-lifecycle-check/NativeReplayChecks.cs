using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Settings;
using SkiaSharp;

internal static partial class Program
{
    private static async Task CheckNativeReplayLifecycleAsync(string directory,string assets,string fixture,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        string profile=Path.Combine(directory,"native-replay-profile");Directory.CreateDirectory(profile);
        new StudioSettingsStore(new(AppContext.BaseDirectory,profile)).SaveSettings(new(){GamePathsFile=Path.GetFullPath(assets)});
        string source=Path.Combine(directory,"native-replay.ppdemo");File.Copy(fixture,source);
        byte[] original=File.ReadAllBytes(source);Process? studio=null;var exports=new List<Guid>();
        try
        {
            studio=await StartNativeProbeAsync(profile);
            using(Process forwarded=StartStudioExecutable(profile,["--replay",source]))
            {await forwarded.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(forwarded.ExitCode==0,"actual second Studio executable forwards replay to native owner");}
            await WaitForNativeReplayAsync(studio,1);
            string image=Path.Combine(output,"native-replay-single.png");
            await studio.StandardInput.WriteLineAsync("capture-replay "+image);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
            CheckNativePng(image,640,360,"actual native passive replay GPU capture renders nonflat canonical scene");
            await studio.StandardInput.WriteLineAsync("replay-four");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-FOUR");
            NativeSnapshot four=await WaitForNativeReplayAsync(studio,4);
            Check(four.NativeSurfaces==4&&four.Replay is {Ready:true,State:"Paused"},"actual four replay native surfaces share one paused passive transport");
            image=Path.Combine(output,"native-replay-four-primary.png");
            await studio.StandardInput.WriteLineAsync("capture-replay "+image);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
            CheckNativePng(image,640,360,"actual four-view replay primary presents canonical GPU pixels");
            await studio.StandardInput.WriteLineAsync("replay-four");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-FOUR");await WaitForNativeReplayAsync(studio,1);

            Guid png=await NativeQueueExportAsync(studio,new(Path.Combine(output,"png-export"),0,6,320,180,60,Encoder:null,
                Camera:StudioReplayCameraMode.Player,Audio:new(Enabled:false)));
            exports.Add(png);
            StudioReplayExportStatus pngStatus=await WaitNativeExportAsync(profile,png);
            Check(pngStatus.State=="Complete"&&pngStatus.Frames==7,"actual native detached PNG export completes canonical inclusive frame count");
            CheckNativePng(Path.Combine(pngStatus.Directory,"frame_00000000.png"),320,180,"native export worker writes actual nonflat replay PNG frame");

            Guid cancelled=await NativeQueueExportAsync(studio,new(Path.Combine(output,"cancelled-export"),0,600,640,360,60,Encoder:null,Audio:new(Enabled:false)));
            exports.Add(cancelled);
            await studio.StandardInput.WriteLineAsync("cancel-export "+cancelled.ToString("N"));await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"EXPORT-CANCELLED");
            Check((await WaitNativeExportAsync(profile,cancelled)).State=="Cancelled","explicit persisted cancellation cleanly stops separate native replay export worker");

            string music=Path.Combine(directory,"offline-music.wav");WriteTestWave(music);
            string ffmpeg=Environment.GetEnvironmentVariable("PROJECT_PRIME_TEST_FFMPEG")??"/tmp/project-prime-export-ffmpeg/ffmpeg";
            string ffprobe=Environment.GetEnvironmentVariable("PROJECT_PRIME_TEST_FFPROBE")??"/tmp/project-prime-export-ffmpeg/ffprobe";
            Check(File.Exists(ffmpeg)&&File.Exists(ffprobe),"native mux acceptance has explicit real encoder and media probe executables");
            Guid mux=await NativeQueueExportAsync(studio,new(Path.Combine(output,"mux-export"),0,60,320,180,24,Encoder:ffmpeg,
                Camera:StudioReplayCameraMode.Player,Audio:new(MusicFile:music,GameEvents:false,CombatFeedback:false)));
            exports.Add(mux);
            await studio.StandardInput.WriteLineAsync("close-document");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"DOCUMENT-CLOSED");
            Check((await NativeStatusAsync(studio)).Documents.Length==0,"native replay document closes while its persisted export worker owns separate decoder/device");
            StudioReplayExportStatus muxStatus=await WaitNativeExportAsync(profile,mux);
            Check(muxStatus.State=="Complete"&&muxStatus.Frames==25&&File.Exists(Path.Combine(muxStatus.Directory,"offline.wav")),
                "detached native export survives document closure and completes independent offline audio/mux");
            var probeStart=new ProcessStartInfo(ffprobe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string argument in new[]{"-v","error","-show_streams","-show_format","-of","json",Path.Combine(muxStatus.Directory,"replay.mp4")})probeStart.ArgumentList.Add(argument);
            using(var probe=Process.Start(probeStart)!)
            {
                try
                {
                string json=await probe.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Check(probe.ExitCode==0,"real FFprobe accepts native mux output");using var details=JsonDocument.Parse(json);
                var streams=details.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                Check(streams.Any(stream=>stream.GetProperty("codec_type").GetString()=="video"&&stream.GetProperty("width").GetInt32()==320&&stream.GetProperty("height").GetInt32()==180)
                    &&streams.Any(stream=>stream.GetProperty("codec_type").GetString()=="audio")
                    &&double.Parse(details.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture) is >=.95 and <=1.2,
                    "native mux contains requested video geometry plus audible offline PCM at canonical one-second duration");
                }
                finally{if(!probe.HasExited){probe.Kill(entireProcessTree:true);await probe.WaitForExitAsync();}}
            }
            using(Process reopened=StartStudioExecutable(profile,["--replay",source]))
            {await reopened.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(reopened.ExitCode==0,"actual native Replay reopens for whole-application export ownership check");}
            await WaitForNativeReplayAsync(studio,1);
            Guid afterClose=await NativeQueueExportAsync(studio,new(Path.Combine(output,"after-window-close"),0,120,320,180,60,Encoder:null,
                Camera:StudioReplayCameraMode.Player,Audio:new(Enabled:false)));
            exports.Add(afterClose);
            await WaitNativeExportRunningAsync(profile,afterClose);
            await CloseNativeProbeAsync(studio);
            Check(studio.HasExited,"normal native Studio window closes while independent export worker is rendering");
            bool updateBlocked=false;
            try{using var update=MphRead.Mods.Update.InstallationLifetime.AcquireUpdate(AppContext.BaseDirectory);}
            catch(IOException){updateBlocked=true;}
            Check(updateBlocked,"detached actual native worker retains installation ownership after editing application exits");
            StudioReplayExportStatus completedAfterClose=await WaitNativeExportAsync(profile,afterClose);
            Check(completedAfterClose.State=="Complete"&&completedAfterClose.Frames==121,
                "persisted native export reaches terminal success after entire Studio application exits");
            CheckNativePng(Path.Combine(completedAfterClose.Directory,"frame_00000120.png"),320,180,
                "worker preserves final native GPU frame after editing application shutdown");
            await WaitNativeWorkerLeaseReleaseAsync();
            Check(original.SequenceEqual(File.ReadAllBytes(source)),"actual native replay capture/export/close preserves original recording bytes");
        }
        finally
        {
            // Persisted explicit cancellation cleans up this test's detached children on a failed assertion.
            foreach(Guid id in exports)
            {
                string root=NativeExportRoot(profile,id);if(!Directory.Exists(root))continue;
                var state=ReadNativeExport(profile,id);
                if(state?.State is "Complete" or "Cancelled" or "Failed")continue;
                File.WriteAllText(Path.Combine(root,"cancel"),"cancel");
                try{await WaitNativeExportAsync(profile,id);}catch(Exception ex)when(ex is IOException or TimeoutException or InvalidOperationException){}
            }
            if(studio is not null){if(!studio.HasExited){studio.Kill(entireProcessTree:true);await studio.WaitForExitAsync();}studio.Dispose();}
        }
    }

    private static async Task<NativeSnapshot> WaitForNativeReplayAsync(Process studio,int views)
    {
        var timer=Stopwatch.StartNew();NativeSnapshot? snapshot=null;
        while(timer.Elapsed<TimeSpan.FromSeconds(40))
        {
            snapshot=await NativeStatusAsync(studio);
            if(snapshot.Replay is {Ready:true} replay&&replay.Views==views)return snapshot;
            if(snapshot.Replay is {State:"Error"} error)throw new InvalidOperationException(error.Error);
            await Task.Delay(50);
        }
        throw new TimeoutException("Native Replay did not present: "+JsonSerializer.Serialize(snapshot));
    }
    private static async Task<Guid> NativeQueueExportAsync(Process studio,StudioReplayExportRequest request)
    {
        await studio.StandardInput.WriteLineAsync("export "+JsonSerializer.Serialize(request,new JsonSerializerOptions{IncludeFields=true}));await studio.StandardInput.FlushAsync();
        return Guid.Parse((await ReadNativeLineAsync(studio,"EXPORT "))[7..]);
    }
    private static async Task<StudioReplayExportStatus> WaitNativeExportAsync(string profile,Guid id)
    {
        var deadline=Stopwatch.StartNew();StudioReplayExportStatus? latest=null;
        while(deadline.Elapsed<TimeSpan.FromSeconds(90))
        {
            if(ReadNativeExport(profile,id) is { } state)
            {
                latest=state;
                if(latest is {State:"Failed"})throw new InvalidOperationException("Native export failed: "+latest.Error);
                if(latest is {State:"Complete" or "Cancelled"})return latest;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("Native export did not reach terminal state: "+JsonSerializer.Serialize(latest));
    }
    private static string NativeExportRoot(string profile,Guid id)=>Path.Combine(profile,"build-cache","replay","exports",id.ToString("N"));
    private static StudioReplayExportStatus? ReadNativeExport(string profile,Guid id)
    {
        string path=Path.Combine(NativeExportRoot(profile,id),"status.json");
        try{return File.Exists(path)?JsonSerializer.Deserialize<StudioReplayExportStatus>(File.ReadAllText(path)):null;}
        catch(Exception ex)when(ex is IOException or JsonException){return null;}
    }
    private static async Task WaitNativeExportRunningAsync(string profile,Guid id)
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(40))
        {
            var status=ReadNativeExport(profile,id);
            if(status is {State:"Rendering",Frames:>0}&&status.Frames<status.TotalFrames)return;
            if(status?.State is "Complete" or "Cancelled" or "Failed")throw new InvalidOperationException("Export finished before application-close acceptance: "+JsonSerializer.Serialize(status));
            await Task.Delay(25);
        }
        throw new TimeoutException("Native export worker did not begin rendering before application-close acceptance.");
    }
    private static async Task WaitNativeWorkerLeaseReleaseAsync()
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(10))
        {
            try{using var update=MphRead.Mods.Update.InstallationLifetime.AcquireUpdate(AppContext.BaseDirectory);
                Check(true,"actual native worker releases installation lease after terminal output and native shutdown");return;}
            catch(IOException){await Task.Delay(50);}
        }
        throw new TimeoutException("Native worker retained installation lease after terminal output.");
    }
    private static void CheckNativePng(string path,int width,int height,string message)
    {
        using var image=SKBitmap.Decode(path)??throw new InvalidDataException("Native GPU image is unreadable.");
        Check(image.Width==width&&image.Height==height&&image.Pixels.Distinct().Count()>30,message);
    }
    private static void WriteTestWave(string path)
    {
        const int rate=48000,channels=2,frames=rate;using var writer=new BinaryWriter(File.Create(path),Encoding.ASCII);
        writer.Write("RIFF"u8);writer.Write(36+frames*channels*2);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)channels);
        writer.Write(rate);writer.Write(rate*channels*2);writer.Write((short)(channels*2));writer.Write((short)16);writer.Write("data"u8);writer.Write(frames*channels*2);
        for(int frame=0;frame<frames;frame++){short sample=(short)(Math.Sin(frame*2*Math.PI*440/rate)*8192);writer.Write(sample);writer.Write(sample);}
    }
}
