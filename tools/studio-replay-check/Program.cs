using System.Diagnostics;
using System.Security.Cryptography;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;
using OpenTK.Mathematics;

if(args is ["--abandon-child-owner",var childMarker])
{
    var launch=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
    if(Path.GetFileNameWithoutExtension(launch.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))launch.ArgumentList.Add(typeof(Program).Assembly.Location);
    launch.ArgumentList.Add("--idle-worker");
    using var child=new Process{StartInfo=launch};
    using var ownership=StudioReplayWorkerLifetime.StartChild(child);
    await File.WriteAllTextAsync(childMarker,child.Id.ToString());
    StudioReplayWorkerLifetime.AbandonChildren();
    return child.HasExited?0:1;
}
if(args is ["--idle-worker"]){await Task.Delay(Timeout.Infinite);return 0;}
if(args is [var nativeMode,var studioDll,var original,var outputRoot,var pathsFile] && nativeMode is "--native-export" or "--native-export-composite")
{
    MphRead.AvaloniaShared.StudioGameAssets.Configure(pathsFile);
    Directory.CreateDirectory(outputRoot);
    string originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
    using var parent=Process.GetCurrentProcess();
    foreach(var size in (nativeMode=="--native-export-composite"?new[]{(Width:1280,Height:720)}:new[]{(Width:1280,Height:720),(Width:1920,Height:1080)}))
    {
        string destination=Path.GetFullPath(Path.Combine(outputRoot,$"replay-{size.Height}-audio{(nativeMode=="--native-export-composite"?"-composite":"")}.mp4"));
        string directory=Path.Combine(Path.GetFullPath(outputRoot),".prime-replay-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string snapshot=Path.Combine(directory,"replay.ppdemo");
        await StudioReplaySnapshot.CopyVerifiedAsync(original,snapshot,originalHash,CancellationToken.None);
        var ticket=new{Replay=snapshot,Hash=originalHash,Destination=destination,Start=0u,End=119u,size.Width,size.Height,Fps=60,GameHud=true,Mode=0,Slot=0,Preset="ultrafast",Audio=true,Multiview=nativeMode=="--native-export-composite",ComparisonMode=1,ComparisonSlot=1,Overlay=true,Director=false,Segments=nativeMode=="--native-export-composite"?new[]{new ReplayVideoSegment(0,59,"First",ReplaySegmentCamera.Current),new ReplayVideoSegment(60,119,"Director",ReplaySegmentCamera.Director)}:(ReplayVideoSegment[]?)null,ProtectedInputs=new[]{Path.GetFullPath(original)},RuntimePaths=MphRead.Paths.AllPaths.ToDictionary(p=>p.Key,p=>p.Value),MphKey=MphRead.Paths.MphKey,FhKey=MphRead.Paths.FhKey,ParentProcessId=parent.Id,ParentStartTimeUtcTicks=parent.StartTime.ToUniversalTime().Ticks};
        string ticketPath=Path.Combine(directory,"ticket.json");await File.WriteAllTextAsync(ticketPath,System.Text.Json.JsonSerializer.Serialize(ticket));
        var launch=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){UseShellExecute=false};
        launch.ArgumentList.Add(Path.GetFullPath(studioDll));launch.ArgumentList.Add("--replay-export-worker");launch.ArgumentList.Add(ticketPath);
        using var child=Process.Start(launch)!;
        try
        {
            await StudioReplayWorkerLifetime.WaitAsync(child,directory,CancellationToken.None);
            Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(directory,"status.json")));
            if(child.ExitCode!=0||!File.Exists(destination))return 1;
        }
        finally
        {
            string after=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
            Console.WriteLine($"Original SHA256 before={originalHash} after={after}");
            if(after!=originalHash)throw new IOException("Original replay changed during export.");
            // Keep only small diagnostics; successful jobs should not retain hundreds of PNGs.
            foreach(string frame in Directory.EnumerateFiles(directory,"frame_*.png"))File.Delete(frame);
            foreach(string wave in Directory.EnumerateFiles(directory,"audio_*.wav"))File.Delete(wave);
            File.Delete(snapshot);
        }
    }
    return 0;
}
int passed=0,failed=0;
void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
async Task Check(string name,Func<Task> check)
{try{await check();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL "+name+": "+ex.Message);}}
Task Sync(Action action){action();return Task.CompletedTask;}
string root=Path.Combine(Path.GetTempPath(),"prime-studio-replay-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string replay=Path.Combine(root,"fixture.ppdemo");byte[] bytes=Enumerable.Range(0,128*1024).Select(i=>(byte)(i*17)).ToArray();File.WriteAllBytes(replay,bytes);
try
{
    await Check("document snapshot stays immutable when original recording changes",async()=>
    {
        using var first=await StudioReplaySnapshot.CreateAsync(replay,root,CancellationToken.None);
        using var second=await StudioReplaySnapshot.CreateAsync(replay,root,CancellationToken.None);
        Require(first.PlaybackPath!=second.PlaybackPath,"documents share a playback file");
        Require(first.SourceHash==Convert.ToHexString(SHA256.HashData(bytes)),"source hash is not exact");
        File.WriteAllBytes(replay,[5,4,3]);
        Require(File.ReadAllBytes(first.PlaybackPath).SequenceEqual(bytes)&&File.ReadAllBytes(second.PlaybackPath).SequenceEqual(bytes),"snapshot followed source mutation");
        File.WriteAllBytes(replay,bytes);
    });
    await Check("tampered export snapshot is rejected without a partial destination",async()=>
    {
        string destination=Path.Combine(root,"tampered-copy.ppdemo");
        try{await StudioReplaySnapshot.CopyVerifiedAsync(replay,destination,new string('0',64),CancellationToken.None);throw new Exception("hash mismatch accepted");}
        catch(InvalidDataException){}
        Require(!File.Exists(destination)&&!Directory.EnumerateFiles(root,"*.tmp").Any(),"partial copy escaped cleanup");
    });
    await Check("copy cancellation leaves original and destination unchanged",async()=>
    {
        string destination=Path.Combine(root,"cancelled.ppdemo");using var stop=new CancellationTokenSource();stop.Cancel();
        try{await StudioReplaySnapshot.CopyVerifiedAsync(replay,destination,null,stop.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
        Require(!File.Exists(destination)&&File.ReadAllBytes(replay).SequenceEqual(bytes),"cancellation changed a recording");
    });
    await Check("snapshot lifetime releases private files",async()=>
    {
        var snapshot=await StudioReplaySnapshot.CreateAsync(replay,root,CancellationToken.None);string path=snapshot.PlaybackPath;snapshot.Dispose();
        Require(!Directory.Exists(Path.GetDirectoryName(path)),"private snapshot directory leaked");
    });
    await Check("copy never overwrites an existing recording",async()=>
    {
        string destination=Path.Combine(root,"existing.ppdemo");File.WriteAllBytes(destination,[1,2,3]);
        try{await StudioReplaySnapshot.CopyVerifiedAsync(replay,destination,null,CancellationToken.None);throw new Exception("existing destination overwritten");}catch(IOException){}
        Require(File.ReadAllBytes(destination).SequenceEqual(new byte[]{1,2,3}),"existing bytes changed");
    });
    await Check("closing a document retains its snapshot until asynchronous copy completes",async()=>
    {
        var snapshot=await StudioReplaySnapshot.CreateAsync(replay,root,CancellationToken.None);
        var lease=snapshot.Acquire();string path=snapshot.PlaybackPath;string hash=snapshot.PlaybackHash;
        snapshot.Dispose();snapshot.Dispose();
        Require(File.Exists(path),"closing deleted leased snapshot");
        string copy=Path.Combine(root,"leased-copy.ppdemo");
        await StudioReplaySnapshot.CopyVerifiedAsync(path,copy,hash,CancellationToken.None);
        Require(File.ReadAllBytes(copy).SequenceEqual(bytes),"export copy failed after document close");
        lease.Dispose();lease.Dispose();
        Require(!File.Exists(path),"snapshot was not deleted after final export reader");
        try{snapshot.Acquire();throw new Exception("closed snapshot acquired a new reader");}catch(ObjectDisposedException){}
    });
    await Check("outputs reject source recording, logical clip, backing source and sidecar aliases",()=>Sync(()=>
    {
        void Rejected(Action action,[System.Runtime.CompilerServices.CallerArgumentExpression("action")]string? expression=null){try{action();throw new Exception("unsafe output accepted: "+expression);}catch(IOException){}}
        Rejected(()=>StudioReplayOutputGuard.ValidateClip(replay,[replay]));
        if(OperatingSystem.IsMacOS()||OperatingSystem.IsWindows())Rejected(()=>StudioReplayOutputGuard.ValidateClip(replay.ToUpperInvariant(),[replay]));
        string logical=Path.Combine(root,"logical.ppclip");File.WriteAllText(logical,System.Text.Json.JsonSerializer.Serialize(new ReplayVirtualClipDocument(1,replay,0,120,DateTime.UtcNow,"Test clip")));
        Rejected(()=>StudioReplayOutputGuard.ValidateVideo(replay,[logical,replay]));
        Rejected(()=>StudioReplayOutputGuard.ValidateClip(replay,[logical]));
        Rejected(()=>StudioReplayOutputGuard.ValidateClip(logical,[logical,replay]));
        string alias=Path.Combine(root,"alias.ppdemo");File.CreateSymbolicLink(alias,replay);
        Rejected(()=>StudioReplayOutputGuard.ValidateClip(alias,[replay]));
        string videoAlias=Path.Combine(root,"alias.mp4");File.CreateSymbolicLink(videoAlias,replay);
        Rejected(()=>StudioReplayOutputGuard.ValidateVideo(videoAlias,[replay]));
        string directoryAlias=Path.Combine(root,"linked");Directory.CreateSymbolicLink(directoryAlias,root);
        Rejected(()=>StudioReplayOutputGuard.ValidateClip(Path.Combine(directoryAlias,"fixture.ppdemo"),[replay]));
        StudioReplayOutputGuard.ValidateVideo(Path.Combine(root,"safe.mp4"),[logical,replay]);
        StudioReplayOutputGuard.ValidateClip(Path.Combine(root,"safe.ppdemo"),[logical,replay]);
        Require(File.ReadAllBytes(replay).SequenceEqual(bytes),"guard modified source bytes");
        Directory.Delete(directoryAlias);
    }));
    Process StartIdle()
    {
        var launch=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
        if(Path.GetFileNameWithoutExtension(launch.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))launch.ArgumentList.Add(typeof(Program).Assembly.Location);
        launch.ArgumentList.Add("--idle-worker");return Process.Start(launch)!;
    }
    await Check("native worker without graphics callbacks times out and is reaped",async()=>
    {
        string directory=Path.Combine(root,"never-ready");Directory.CreateDirectory(directory);using var child=StartIdle();
        var watch=Stopwatch.StartNew();
        try{await StudioReplayWorkerLifetime.WaitAsync(child,directory,CancellationToken.None,TimeSpan.FromMilliseconds(150),cancelGrace:TimeSpan.FromMilliseconds(100));throw new Exception("never-ready worker accepted");}catch(TimeoutException){}
        Require(child.HasExited&&watch.Elapsed<TimeSpan.FromSeconds(5)&&File.Exists(Path.Combine(directory,"cancel")),"never-ready child was not bounded and reaped");
    });
    await Check("worker cancellation is bounded without any rendering callback",async()=>
    {
        string directory=Path.Combine(root,"never-cancels");Directory.CreateDirectory(directory);using var child=StartIdle();using var stop=new CancellationTokenSource(100);
        try{await StudioReplayWorkerLifetime.WaitAsync(child,directory,stop.Token,cancelGrace:TimeSpan.FromMilliseconds(100));throw new Exception("cancel ignored");}catch(OperationCanceledException){}
        Require(child.HasExited&&File.Exists(Path.Combine(directory,"cancel")),"cancelled native worker survived");
    });
    await Check("worker stalled after ready heartbeat is killed and parent identity is exact",async()=>
    {
        string directory=Path.Combine(root,"stalled");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"heartbeat"),"ready");using var child=StartIdle();
        try{await StudioReplayWorkerLifetime.WaitAsync(child,directory,CancellationToken.None,heartbeatTimeout:TimeSpan.FromMilliseconds(150),cancelGrace:TimeSpan.FromMilliseconds(100));throw new Exception("stalled worker accepted");}catch(TimeoutException){}
        Require(child.HasExited,"stalled worker survived");using var current=Process.GetCurrentProcess();
        Require(StudioReplayWorkerLifetime.ParentAlive(current.Id,current.StartTime.ToUniversalTime().Ticks),"living parent was rejected");
        Require(!StudioReplayWorkerLifetime.ParentAlive(current.Id,current.StartTime.ToUniversalTime().Ticks+1),"reused PID was accepted");
        Require(!StudioReplayWorkerLifetime.ParentAlive(int.MaxValue,0),"missing parent was accepted");
    });
    await Check("parent-loss cleanup kills and reaps registered media children",async()=>
    {
        string marker=Path.Combine(root,"owned-child.pid");
        var launch=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
        if(Path.GetFileNameWithoutExtension(launch.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))launch.ArgumentList.Add(typeof(Program).Assembly.Location);
        launch.ArgumentList.Add("--abandon-child-owner");launch.ArgumentList.Add(marker);
        using var owner=Process.Start(launch)!;
        await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Require(owner.ExitCode==0&&File.Exists(marker),"parent-loss cleanup did not complete");
        int childId=int.Parse(File.ReadAllText(marker));
        try{using var child=Process.GetProcessById(childId);Require(child.HasExited,"registered media child survived parent loss");}catch(ArgumentException){}
    });
    await Check("camera sidecars and histories are private and recording bytes immutable",()=>Sync(()=>
    {
        var first=new ReplayCameraTrack();var second=new ReplayCameraTrack();
        var start=new ReplayCameraKeyframe(0,Vector3.Zero,Quaternion.Identity,1);var end=start with{Frame=120,Position=new(12,24,36)};
        Require(first.Put(start)&&first.Put(end)&&second.Put(start with{Position=Vector3.One}),"camera insertion failed");
        Require(first.Sample(60,out var middle)&&middle.Position==new Vector3(6,12,18)&&second.Sample(60,out var other)&&other.Position==Vector3.One,"camera documents interfere");
        Require(first.Save(replay),first.LastError??"save failed");var restored=new ReplayCameraTrack();Require(restored.Load(replay)&&restored.Keys.SequenceEqual(first.Keys),"canonical camera roundtrip failed");
        byte[] camera=File.ReadAllBytes(replay+".camera");camera[^1]^=1;File.WriteAllBytes(replay+".camera",camera);
        Require(!restored.Load(replay)&&restored.Keys.Count==0,"tampered camera sidecar accepted");
        Require(File.ReadAllBytes(replay).SequenceEqual(bytes),"camera save rewrote recording");
    }));
    await Check("bookmarks use document duration without foreground playback",()=>Sync(()=>
    {
        var bookmark=ReplayAnnotations.AddBookmark(replay,180,"Private bookmark",240);
        Require(bookmark.Frame==180&&ReplayAnnotations.Bookmarks(replay).Single().Frame==180,"bookmark was clamped by foreground Game clock");
        ReplayAnnotations.AddHighlight(replay,120,240,"Selection");ReplayReels.Add(replay,120,240,"First segment");
        Require(ReplayAnnotations.Highlights(replay).Single().StartFrame==120&&ReplayReels.Segments(replay).Single().EndFrame==240,"canonical authoring sidecar lost range");
        Require(!Directory.EnumerateFiles(root,"*.tmp").Any()&&File.ReadAllBytes(replay).SequenceEqual(bytes),"sidecar write leaked scratch or rewrote replay");
    }));
    await Check("offline PCM sample clock writes exact stereo RIFF lengths",()=>Sync(()=>
    {
        string wave=Path.Combine(root,"clock.wav");
        using(var writer=new StudioPcmWaveWriter(wave,96000))
        {
            short[] step=Enumerable.Range(0,1600).Select(i=>(short)(i%2==0?1234:-1234)).ToArray();
            for(int i=0;i<120;i++)writer.Append(step,800);
            Require(writer.SampleFrames==96000,"audio sample count differs from two seconds");
        }
        byte[] output=File.ReadAllBytes(wave);
        Require(output.Length==44+96000*4&&BitConverter.ToUInt32(output,4)==output.Length-8&&BitConverter.ToUInt32(output,40)==96000*4,"PCM header does not match data");
        Require(BitConverter.ToInt16(output,44)==1234&&BitConverter.ToInt16(output,46)==-1234,"stereo samples changed byte order or interleaving");
    }));
    await Check("fixed sample clock is invariant across preview schedules at every supported FPS",()=>Sync(()=>
    {
        foreach(int fps in ReplayExportRates.Supported)
        {
            var sampler=new ReplayExportSampler(11,371,fps);var linear=Enumerable.Range(0,(int)sampler.Count).Select(i=>sampler.At(i)).ToArray();
            var burst=new List<ReplayExportSample>();long next=0;
            foreach(int batch in Enumerable.Repeat(new[]{0,1,7,2,0,13},500).SelectMany(a=>a))
            {for(int i=0;i<batch&&next<sampler.Count;i++)burst.Add(sampler.At(next++));if(next==sampler.Count)break;}
            Require(burst.SequenceEqual(linear),"host scheduling changed samples at "+fps);
            for(int i=1;i<linear.Length;i++)Require(linear[i].SimulationFrame>=linear[i-1].SimulationFrame&&linear[i].Frame>linear[i-1].Frame,"sample clock went backwards");
            Require(linear[0].Frame==11&&linear[^1].Frame==371&&linear.All(s=>s.Alpha>0&&s.Alpha<=1),"range endpoints/interpolation wrong at "+fps);
        }
    }));
    await Check("invalid detached export rates fail before GPU/world allocation",()=>Sync(()=>
    {
        var manifest=new ReplayVideoExportManifest(3,replay,0,60,1280,720,60,true,false,true,"","","",ReplayOverlay:true);
        try{using var export=ReplayVideoExporter.CreateDetached(manifest with{ReplayOverlay=false,Fps=37},root,Path.Combine(root,"output.mp4"),new(),ReplayCameraMode.FirstPerson,0);throw new Exception("unsupported FPS accepted");}catch(ArgumentOutOfRangeException){}
    }));
}
finally{Directory.Delete(root,true);}
Console.WriteLine($"{passed} PASS / {failed} FAIL");return failed==0?0:1;
