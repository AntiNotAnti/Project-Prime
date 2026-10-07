using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Settings;
using SkiaSharp;

internal static partial class Program
{
    private static async Task CheckNativeReplayLifecycleAsync(string directory,string assets,string fixture,string output,bool disableGameHud=false,bool workersOnly=false,bool staleView=false)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        string nativeLibrary = OperatingSystem.IsMacOS() ? "libwgpu_native.dylib"
            : OperatingSystem.IsWindows() ? "wgpu_native.dll" : "libwgpu_native.so";
        object ArtifactIdentity(string path) => new { Path = path, Bytes = new FileInfo(path).Length,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() };
        File.WriteAllText(Path.Combine(output,"native-replay-input-identity.json"),JsonSerializer.Serialize(new
        {
            Scope="Actual native Replay/worker acceptance; world comparisons use the same explicitly presented phase within this run.",
            CapturedUtc=DateTime.UtcNow,
            Source=ArtifactIdentity(Path.GetFullPath(fixture)),
            Engine=ArtifactIdentity(typeof(StudioReplayPlayer).Assembly.Location),
            Studio=ArtifactIdentity(typeof(ProjectPrime.Studio.StudioWindow).Assembly.Location),
            Harness=ArtifactIdentity(typeof(Program).Assembly.Location),
            StagedNativeRuntime=ArtifactIdentity(Path.Combine(AppContext.BaseDirectory,nativeLibrary)),
            AssetsPathsFile=Path.GetFullPath(assets), disableGameHud, workersOnly, staleView
        },new JsonSerializerOptions{WriteIndented=true}));
        string profile=Path.Combine(directory,"native-replay-profile");Directory.CreateDirectory(profile);
        new StudioSettingsStore(new(AppContext.BaseDirectory,profile)).SaveSettings(new(){GamePathsFile=Path.GetFullPath(assets)});
        string source=Path.Combine(directory,"native-replay.ppdemo");File.Copy(fixture,source);
        byte[] original=File.ReadAllBytes(source);Process? studio=null;var exports=new List<Guid>();Exception? timingFailure=null;
        try
        {
            studio=await StartNativeProbeAsync(profile);
            using(Process forwarded=StartStudioExecutable(profile,["--replay",source]))
            {await forwarded.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(forwarded.ExitCode==0,"actual second Studio executable forwards replay to native owner");}
            await WaitForNativeReplayAsync(studio,1);
            if(disableGameHud)
            {
                await studio.StandardInput.WriteLineAsync("replay-hud-off");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-HUD-OFF");
                await WaitForNativeReplayAsync(studio,1);
                Console.WriteLine("Native Replay diagnostic explicitly disables Game HUD; this does not certify the default Game HUD path.");
            }
            string image=Path.Combine(output,"native-replay-single.png");
            await studio.StandardInput.WriteLineAsync("capture-replay "+image);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
            CheckNativePng(image,640,360,"actual native passive replay GPU capture renders nonflat canonical scene");
            if(!disableGameHud)await CheckNativeReplayHudAsync(studio,output,image);
            if(workersOnly)Console.WriteLine("Native Replay worker diagnostic omits the separate four-view and clip viewport gates.");
            if(!workersOnly)
            {
            await studio.StandardInput.WriteLineAsync("replay-four");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-FOUR");
            NativeSnapshot four=await WaitForNativeReplayAsync(studio,4);
            Check(four.NativeSurfaces==4&&four.Replay is {Ready:true,State:"Paused"},"actual four replay native surfaces share one paused passive transport");
            image=Path.Combine(output,"native-replay-four-primary.png");
            await studio.StandardInput.WriteLineAsync("capture-replay "+image);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
            CheckNativePng(image,640,360,"actual four-view replay primary presents canonical GPU pixels");
            try{await CheckNativeReplayClockAsync(studio,output);}
            catch(InvalidOperationException ex)when(ex.Message.StartsWith("FAILED: actual four-view loop",StringComparison.Ordinal))
            {timingFailure=ex;Console.WriteLine(ex.Message+"; continuing independent retry, clip and export gates. Overall native Replay acceptance still fails.");}
            await studio.StandardInput.WriteLineAsync("replay-four");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-FOUR");await WaitForNativeReplayAsync(studio,1);

            await studio.StandardInput.WriteLineAsync("replay-transport");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-TRANSPORT");
            await WaitNativeReplayFrameAsync(studio,17);
            var beforeRetry=await NativeReplayWorldAsync(studio);
            await studio.StandardInput.WriteLineAsync("replay-retry");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-RETRY");
            var retry=await WaitNativeReplayFrameAsync(studio,17);var afterRetry=await NativeReplayWorldAsync(studio);
            Check(retry.Replay is {State:"Paused",Rate:2,ClipIn:5,ClipOut:45}
                &&beforeRetry.GameplayHash==afterRetry.GameplayHash&&beforeRetry.PresentationHash==afterRetry.PresentationHash
                &&beforeRetry.FullGraphHash==afterRetry.FullGraphHash,
                "actual native viewport retry preserves nonzero world, semantic graph and paused transport preferences");
            await CheckNativeReplaySeekJobsAsync(studio,output);
            if(staleView)
            {
                await CheckNativeReplayStaleViewAsync(studio,output);
                await studio.StandardInput.WriteLineAsync("close-document");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"DOCUMENT-CLOSED");
                using(Process reopened=StartStudioExecutable(profile,["--replay",source]))
                {await reopened.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(reopened.ExitCode==0,"recording reopens after stale fourth-view native resource cleanup");}
                await WaitForNativeReplayAsync(studio,1);
            }

            await studio.StandardInput.WriteLineAsync("prepare-replay-clip");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-CLIP-PREPARED");
            var authoredSidecars=Directory.GetFiles(directory,Path.GetFileName(source)+".*").ToDictionary(path=>path,File.ReadAllBytes);
            string clip=Path.Combine(directory,"native-saved.ppclip");
            await studio.StandardInput.WriteLineAsync("save-replay-as "+clip);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-SAVED");
            NativeSnapshot saved=await WaitForNativeReplayAsync(studio,1);
            Check(saved.Documents is [{Kind:"ReplayClip"}]&&saved.Replay is {Duration:30,Camera:"Free",Fov:65,CameraKeys:>0}
                &&File.Exists(clip)&&authoredSidecars.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)))&&original.SequenceEqual(File.ReadAllBytes(source)),
                "actual native Save As rebinds clip viewport with range, camera preferences and authored keys while preserving original bytes");
            image=Path.Combine(output,"native-replay-saved-clip.png");
            await studio.StandardInput.WriteLineAsync("capture-replay "+image);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
            CheckNativePng(image,640,360,"native replacement clip viewport continues rendering after Save As");
            string cameraReference=Path.Combine(output,"native-replay-clip-camera-reference.png");
            byte[] frozenCamera=Convert.FromBase64String((await NativeCommandAsync(studio,"replay-camera-reference "+cameraReference,"REPLAY-CAMERA-REFERENCE "))[24..]);
            Guid clipExport=await NativeQueueExportAsync(studio,new(Path.Combine(output,"clip-camera-export"),0,6,320,180,60,Encoder:null,
                Camera:StudioReplayCameraMode.Authored,Audio:new(Enabled:false),View:new(320,180,StudioReplayCameraMode.Authored,Fov:65)));
            exports.Add(clipExport);
            await NativeCommandAsync(studio,"replay-mutate-camera","REPLAY-CAMERA-MUTATED");
            var cameraTicket=JsonSerializer.Deserialize<StudioReplayExportTicket>(File.ReadAllText(Path.Combine(NativeExportRoot(profile,clipExport),"ticket.json")),new JsonSerializerOptions{IncludeFields=true})!;
            Check(cameraTicket.CameraState is not null&&cameraTicket.CameraState.SequenceEqual(frozenCamera),
                "actual queued native worker freezes exact v5 cropped camera state before later editor key changes");
            var clipExportStatus=await WaitNativeExportAsync(profile,clipExport);
            Check(clipExportStatus.State=="Complete"&&clipExportStatus.Frames==7,"actual native worker renders cropped authored-camera export with immutable queued state");
            using(var referencePixels=SKBitmap.Decode(cameraReference))using(var workerPixels=SKBitmap.Decode(Path.Combine(clipExportStatus.Directory,"frame_00000000.png")))
                Check(referencePixels.Bytes.SequenceEqual(workerPixels.Bytes),"native worker first frame is pixel-exact with owned cropped camera at same frame, view and frozen authoring state");
            await studio.StandardInput.WriteLineAsync("close-document");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"DOCUMENT-CLOSED");
            using(Process reopened=StartStudioExecutable(profile,["--replay",source]))
            {await reopened.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(reopened.ExitCode==0,"original recording reopens after native Save As clip lifecycle");}
            await WaitForNativeReplayAsync(studio,1);
            }

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
            CheckNativePcm(Path.Combine(muxStatus.Directory,"offline.wav"));
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
            string hiddenSource=source+".renamed";File.Move(source,hiddenSource);
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
            foreach(Guid id in exports.Where(id=>id!=cancelled))
            {
                string durable=NativeExportRoot(profile,id),log=Path.Combine(durable,"worker.log");
                Check(!Directory.Exists(Path.Combine(durable,"cache"))&&File.Exists(Path.Combine(durable,"status.json"))
                    &&File.Exists(log)&&new FileInfo(log).Length is >0 and <=65_539,
                    "terminal native worker releases private decoder/asset scratch after shutdown while durable status and bounded independent log survive "+id);
            }
            Check(original.SequenceEqual(File.ReadAllBytes(hiddenSource)),"queued native worker retains immutable playback after original path is renamed and Studio exits");
            File.Move(hiddenSource,source);
            if(timingFailure is not null)throw timingFailure;
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

    private static async Task CheckNativeReplayStaleViewAsync(Process studio,string output)
    {
        await studio.StandardInput.WriteLineAsync("replay-four");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-FOUR");
        var live=await WaitForNativeReplayAsync(studio,4);
        var world=await CaptureNativeReplayWorldAsync(studio,Path.Combine(output,"native-replay-stale-fourth-before.png"));
        await studio.StandardInput.WriteLineAsync("replay-fail-last-and-loss");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-LOST ");
        var timer=Stopwatch.StartNew();NativeSnapshot restored;
        do{restored=await NativeStatusAsync(studio);if(restored.Generation>live.Generation&&restored.Replay is {Ready:true,Frame:17,State:"Paused"})break;await Task.Delay(25);}
        while(timer.Elapsed<TimeSpan.FromSeconds(40));
        Check(restored.Generation>live.Generation&&restored.Replay is {Ready:true,Frame:17,Rate:2,ClipIn:5,ClipOut:45},
            "three healthy native Replay views recover one shared paused transport while the fourth remains stale and failed");
        string capture=Path.Combine(output,"native-replay-stale-fourth-recovered.png");
        var after=await CaptureNativeReplayWorldAsync(studio,capture);
        CheckNativePng(capture,640,360,"healthy native primary presents recovered scene while fourth-view error retains its lease");
        File.WriteAllText(Path.Combine(output,"native-replay-stale-fourth-state.json"),JsonSerializer.Serialize(new{Before=world,After=after},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Check(world.Frame==after.Frame&&world.GameplayHash==after.GameplayHash&&world.PresentationHash==after.PresentationHash&&world.FullGraphHash==after.FullGraphHash,
            "shared-generation recovery preserves canonical gameplay, presentation and semantic full graph; before="+JsonSerializer.Serialize(world)+",after="+JsonSerializer.Serialize(after));
        await studio.StandardInput.WriteLineAsync("replay-resources");await studio.StandardInput.FlushAsync();
        using var before=JsonDocument.Parse((await ReadNativeLineAsync(studio,"REPLAY-RESOURCES "))[17..]);
        Check(before.RootElement.GetProperty("Counts").GetProperty("Renderbuffers").GetInt32()>0,
            "actual recovered Replay scene owns live native renderbuffers before teardown");
        await studio.StandardInput.WriteLineAsync("replay-dispose-views");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-VIEWS-DISPOSED");
        await studio.StandardInput.WriteLineAsync("replay-resources");await studio.StandardInput.FlushAsync();
        using var released=JsonDocument.Parse((await ReadNativeLineAsync(studio,"REPLAY-RESOURCES "))[17..]);
        var counts=released.RootElement.GetProperty("Counts");
        Check(released.RootElement.GetProperty("Surfaces").GetInt32()==0&&counts.GetProperty("Renderbuffers").GetInt32()==0
            &&counts.GetProperty("Textures").GetInt32()<before.RootElement.GetProperty("Counts").GetProperty("Textures").GetInt32(),
            "disposing healthy views first and stale fourth last releases actual scene renderbuffers/textures and all native surfaces");
        File.WriteAllText(Path.Combine(output,"native-replay-stale-fourth-resources.json"),JsonSerializer.Serialize(new{Before=before.RootElement,After=released.RootElement},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task<StudioReplayWorldSnapshot> CaptureNativeReplayWorldAsync(Process studio,string path)
    {
        await studio.StandardInput.WriteLineAsync("capture-replay-state "+path);await studio.StandardInput.FlushAsync();
        return JsonSerializer.Deserialize<StudioReplayWorldSnapshot>((await ReadNativeLineAsync(studio,"CAPTURE-STATE "))[14..],new JsonSerializerOptions{IncludeFields=true})
            ??throw new InvalidDataException("Native owner did not return atomic post-capture world state.");
    }

    private static async Task CheckNativeReplayClockAsync(Process studio,string output)
    {
        var clocks=new List<object>();
        foreach(float rate in new[]{1f,2f})
        {
            await studio.StandardInput.WriteLineAsync("replay-play "+rate.ToString(System.Globalization.CultureInfo.InvariantCulture));await studio.StandardInput.FlushAsync();
            await ReadNativeLineAsync(studio,"REPLAY-PLAYING");await Task.Delay(500);
            var beginSnapshot=await NativeStatusAsync(studio);var begin=beginSnapshot.Replay!;var timer=Stopwatch.StartNew();await Task.Delay(1000);
            await studio.StandardInput.WriteLineAsync("replay-pause");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-PAUSED");
            double parentSeconds=timer.Elapsed.TotalSeconds;var endSnapshot=await NativeStatusAsync(studio);var transport=endSnapshot.Replay!;
            double seconds=(double)(endSnapshot.Timestamp-beginSnapshot.Timestamp)/Stopwatch.Frequency;
            double expected=seconds*60*rate;
            long advanced=transport.Frame-begin.Frame;
            // Status can arrive between a workspace callback and the completion of its
            // four GPU submissions. Compare simulation against the callback clock and
            // retained fractional tick, then bound the wallclock difference by that
            // measured phase instead of a fixed six-frame scheduling allowance.
            double acceptedSeconds=transport.Clock.LastViewSeconds-begin.Clock.LastViewSeconds;
            double acceptedExpected=(acceptedSeconds+begin.Clock.AccumulatorSeconds-transport.Clock.AccumulatorSeconds)*60*rate;
            double phaseSeconds=Math.Abs(begin.Clock.WorkspaceSeconds-begin.Clock.LastViewSeconds)
                +Math.Abs(transport.Clock.WorkspaceSeconds-transport.Clock.LastViewSeconds);
            double wallAllowance=phaseSeconds*60*rate+2;
            clocks.Add(new{Rate=rate,ElapsedSeconds=seconds,ParentElapsedSeconds=parentSeconds,ExpectedAdvancedFrames=expected,
                AcceptedCallbackSeconds=acceptedSeconds,ExpectedAcceptedFrames=acceptedExpected,MeasuredPhaseAllowanceFrames=wallAllowance,
                BeginFrame=begin.Frame,AdvancedFrames=advanced,Begin=begin,Transport=transport});
            File.WriteAllText(Path.Combine(output,"native-replay-four-clock.json"),JsonSerializer.Serialize(clocks,new JsonSerializerOptions{WriteIndented=true}));
            Check(transport is {Ready:true,State:"Paused",Views:4}&&transport.Rate==rate&&advanced>=expected*.25
                &&Math.Abs(advanced-acceptedExpected)<=2&&Math.Abs(advanced-expected)<=wallAllowance,
                "actual four-view loop retains one bounded wallclock transport at "+rate+"x without multiplying simulation steps per viewport; elapsed="+seconds
                +",expectedAdvance="+expected+",acceptedExpected="+acceptedExpected+",measuredPhaseAllowance="+wallAllowance
                +",beginFrame="+begin.Frame+",advanced="+advanced+",transport="+JsonSerializer.Serialize(transport));
            Check(transport.Clock is {SharedTimerEnabled:true}&&transport.Clock.TimerEnabled.All(enabled=>!enabled)&&transport.Clock.AutomaticRendering.All(enabled=>!enabled),
                "actual four-view presentation owns one workspace timer and disables every viewport's separate timer");
        }
        uint sharedFrame=(await NativeStatusAsync(studio)).Replay!.Frame;var hashes=new HashSet<string>();
        for(int index=0;index<4;index++)
        {
            string path=Path.Combine(output,"native-replay-four-view-"+index+".png");
            await studio.StandardInput.WriteLineAsync("capture-replay-view "+index+" "+path);await studio.StandardInput.FlushAsync();
            using var capture=JsonDocument.Parse((await ReadNativeLineAsync(studio,"VIEW-CAPTURED "))[14..]);
            Check(capture.RootElement.GetProperty("Frame").GetUInt32()==sharedFrame,"actual Replay viewport "+index+" captures the shared paused transport frame");
            CheckNativePng(path,640,360,"actual Replay viewport "+index+" has native camera pixels");
            using var bitmap=SKBitmap.Decode(path);hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bitmap.Bytes)));
        }
        Check(hashes.Count>=3,"actual four-view GPU captures differ by their independent camera descriptors at one recorded frame");
        File.WriteAllText(Path.Combine(output,"native-replay-four-clock.json"),JsonSerializer.Serialize(clocks,new JsonSerializerOptions{WriteIndented=true}));
    }

    private static async Task CheckNativeReplayHudAsync(Process studio,string output,string enabledImage)
    {
        var before=await NativeReplayWorldAsync(studio);
        await studio.StandardInput.WriteLineAsync("replay-hud false");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-HUD");
        string disabled=Path.Combine(output,"native-replay-hud-off.png");
        await studio.StandardInput.WriteLineAsync("capture-replay "+disabled);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
        var after=await NativeReplayWorldAsync(studio);
        Check(before.Frame==after.Frame&&before.GameplayHash==after.GameplayHash&&before.PresentationHash==after.PresentationHash,
            "actual native HUD visibility leaves recorded gameplay and canonical presentation state unchanged");
        using(var hud=SKBitmap.Decode(enabledImage))using(var clean=SKBitmap.Decode(disabled))
        {
            int health=0,reticle=0;
            for(int y=0;y<hud.Height;y++)for(int x=0;x<hud.Width;x++)
            {
                if(hud.GetPixel(x,y)==clean.GetPixel(x,y))continue;
                if(y<hud.Height/2)health++;
                if(Math.Abs(x-hud.Width/2)<80&&Math.Abs(y-hud.Height/2)<70)reticle++;
            }
            Check(health>100&&reticle>20,"actual default HUD adds positive health/reticle-region GPU pixels at the same recorded frame and POV");
        }
        await studio.StandardInput.WriteLineAsync("replay-hud true");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-HUD");
        int[] active=before.Players.Where(player=>player.Active).Select(player=>player.Slot).ToArray();
        Check(active.Length>=2,"native HUD acceptance fixture contains at least two active player POVs");
        foreach(int slot in new[]{active[1],active[0]})
        {
            await studio.StandardInput.WriteLineAsync("replay-slot "+slot);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"REPLAY-SLOT");
            string capture=Path.Combine(output,"native-replay-hud-player-"+slot+".png");
            await studio.StandardInput.WriteLineAsync("capture-replay "+capture);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CAPTURED");
            CheckNativePng(capture,640,360,"native Game HUD initializes independently for player POV "+slot);
            var world=await NativeReplayWorldAsync(studio);
            Check(world.Frame==before.Frame&&world.GameplayHash==before.GameplayHash&&world.PresentationHash==before.PresentationHash,
                "native player HUD/camera switch preserves canonical recorded world for POV "+slot);
        }
        File.WriteAllText(Path.Combine(output,"native-replay-hud-state.json"),JsonSerializer.Serialize(new{Before=before,After=after},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
    private static async Task<StudioReplayWorldSnapshot> NativeReplayWorldAsync(Process studio)
    {
        await studio.StandardInput.WriteLineAsync("replay-state");await studio.StandardInput.FlushAsync();
        using var json=JsonDocument.Parse((await ReadNativeLineAsync(studio,"REPLAY-STATE "))[13..]);
        return json.RootElement.GetProperty("World").Deserialize<StudioReplayWorldSnapshot>(new JsonSerializerOptions{IncludeFields=true})
            ??throw new InvalidDataException("Native Replay owner did not return its canonical world snapshot.");
    }
    private static async Task<NativeSnapshot> WaitNativeReplayFrameAsync(Process studio,uint frame)
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(40))
        {
            NativeSnapshot snapshot=await WaitForNativeReplayAsync(studio,1);
            if(snapshot.Replay is {State:"Paused"} replay&&replay.Frame==frame)return snapshot;
            await Task.Delay(25);
        }
        throw new TimeoutException("Native Replay did not preserve requested paused frame "+frame);
    }
    private static void CheckNativePcm(string path)
    {
        using var input=new BinaryReader(File.OpenRead(path),Encoding.ASCII);
        Check(Encoding.ASCII.GetString(input.ReadBytes(4))=="RIFF","native audio output uses canonical WAV container");
        input.ReadInt32();Check(Encoding.ASCII.GetString(input.ReadBytes(4))=="WAVE","native audio output is WAV PCM");
        byte[]? data=null;short format=0,bits=0,channels=0;int rate=0;
        while(input.BaseStream.Position+8<=input.BaseStream.Length)
        {
            string type=Encoding.ASCII.GetString(input.ReadBytes(4));int size=input.ReadInt32();long end=input.BaseStream.Position+size;
            if(type=="fmt "){format=input.ReadInt16();channels=input.ReadInt16();rate=input.ReadInt32();input.ReadInt32();input.ReadInt16();bits=input.ReadInt16();}
            if(type=="data")data=input.ReadBytes(size);
            input.BaseStream.Position=end+(size&1);
        }
        Check(format==1&&channels==2&&rate==48000&&bits==16&&data is {Length:>0},"actual native export contains requested offline stereo 48kHz 16-bit PCM");
        long energy=0;int nonzero=0;for(int offset=0;offset<data!.Length;offset+=2)
        {short sample=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset,2));energy+=(long)sample*sample;if(sample!=0)nonzero++;}
        Check(nonzero>data.Length/4&&Math.Sqrt((double)energy/(data.Length/2))>100,
            "native encoder input has measured audible synthetic music energy rather than an empty audio stream");
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
                if(latest is {State:"Failed"})
                {
                    string log=Path.Combine(NativeExportRoot(profile,id),"worker.log");
                    if(!File.Exists(log))log=Path.Combine(NativeExportRoot(profile,id),"cache","worker.log");
                    string tail=File.Exists(log)?File.ReadAllText(log):"Worker log unavailable.";
                    if(tail.Length>16000)tail=tail[^16000..];
                    Directory.CreateDirectory(latest.Directory);File.WriteAllText(Path.Combine(latest.Directory,"failure-diagnostics.txt"),JsonSerializer.Serialize(latest)+Environment.NewLine+tail);
                    throw new InvalidOperationException("Native export failed: "+latest.Error+Environment.NewLine+tail);
                }
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
