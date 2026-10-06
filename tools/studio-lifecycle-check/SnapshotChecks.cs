using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.MapGen;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;

internal static partial class Program
{
    private static void CheckReplaySnapshotPins(string root)
    {
        string source=Path.Combine(root,"pin-source.bin"),cache=Path.Combine(root,"pin-fixture-cache");
        byte[] original=Enumerable.Range(0,4096).Select(value=>(byte)value).ToArray();File.WriteAllBytes(source,original);
        using var stop=new CancellationTokenSource();Exception? pruneError=null;
        var pruner=new Thread(()=>
        {
            try{while(!stop.IsCancellationRequested){MapDiskCache.Prune(cache,0,TimeSpan.Zero);Thread.Sleep(1);}}
            catch(Exception error){pruneError=error;}
        });pruner.Start();
        try
        {
            for(int iteration=0;iteration<40;iteration++)
            {
                using var captured=StudioReplaySnapshotCache.CapturePinned(source,cache,CancellationToken.None);
                Check(File.Exists(captured.Path)&&File.ReadAllBytes(captured.Path).SequenceEqual(original),
                    "immutable source writer-to-reader handoff survives concurrent zero-budget eviction "+iteration);
                using var owner=captured.TakePin();using var job=MapDiskCache.Retain(owner);owner.Dispose();
                MapDiskCache.Prune(cache,0,TimeSpan.Zero);
                Check(File.Exists(captured.Path),"retained job pin protects exact bytes after owner disposal "+iteration);
            }
        }
        finally{stop.Cancel();pruner.Join();}
        Check(pruneError is null,"aggressive source-pruning thread completes without ownership errors");
        MapDiskCache.Prune(cache,0,TimeSpan.Zero);
        Check(!Directory.EnumerateDirectories(cache).Any(path=>Path.GetFileName(path).Length==64),
            "all immutable source entries become prunable after final job pin release");
    }

    private static void CheckFrozenReplaySource(StudioReplayPlayer player,string path,string clip,byte[] original,
        Dictionary<string,byte[]> existingClip,StudioReplayWorldSnapshot world,string root)
    {
        string expectedHash=Convert.ToHexString(SHA256.HashData(original));
        using(var descriptor=JsonDocument.Parse(File.ReadAllText(clip)))
            Check(descriptor.RootElement.GetProperty("SourceContentHash").GetString()!.Equals(expectedHash,StringComparison.OrdinalIgnoreCase),
                "saved clip binds the exact durable source recording hash");
        string nested=ReplayVirtualClips.Save(clip,5,30,"Acceptance nested bound clip");
        try
        {
            Check(ReplayVirtualClips.TryLoad(nested,out var child)&&child is {StartFrame:35,EndFrame:60}
                &&Path.GetFullPath(child.SourceReplay)==Path.GetFullPath(path)&&child.SourceContentHash!.Equals(expectedHash,StringComparison.OrdinalIgnoreCase),
                "metadata-only nested clip flattens range and preserves exact parent source identity");
            byte[] alternate=Directory.GetFiles(root,"*.ppdemo").Where(candidate=>candidate!=path)
                .Select(File.ReadAllBytes).First(bytes=>bytes.Length>200_000&&!bytes.SequenceEqual(original));
            File.WriteAllBytes(path,alternate);
            bool refused=false,boundRefused=false,nestedRefused=false;
            try{player.SaveClipProjectAsync(40,70,clip).GetAwaiter().GetResult();}catch(InvalidDataException){refused=true;}
            try{StudioReplayPlayer.ValidateSourceAsync(clip).GetAwaiter().GetResult();}catch(InvalidDataException){boundRefused=true;}
            try{StudioReplayPlayer.ValidateSourceAsync(nested).GetAwaiter().GetResult();}catch(InvalidDataException){nestedRefused=true;}
            Check(refused&&boundRefused&&nestedRefused&&SameReplayWorld(world,player.Snapshot())
                &&existingClip.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                "valid replacement recording cannot rebind Save As or bound/nested clips; exact owner world and previous target bytes survive");
        }
        finally{File.WriteAllBytes(path,original);File.Delete(nested);}
        StudioReplayPlayer.ValidateSourceAsync(clip).GetAwaiter().GetResult();
        Check(SameReplayWorld(world,player.Snapshot())&&original.SequenceEqual(File.ReadAllBytes(path)),
            "restoring frozen source bytes makes bound clip valid and preserves all gameplay, presentation and graph hashes");
    }

    private static void CheckDeferredReplayJobPins(string source,byte[] original,string root,bool cancelled)
    {
        string cache=Path.Combine(root,"deferred-pin-"+Path.GetFileNameWithoutExtension(source)+(cancelled?"-cancel":"-finish"));
        using var owner=new StudioReplayPlayer(source,cache);owner.OnGraphicsInitialize(256,192);WaitReplayReady(owner);
        string snapshot=(string)owner.GetType().GetField("_playbackPath",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
        ThreadPool.GetMinThreads(out int minimum,out int minimumIo);ThreadPool.GetMaxThreads(out int maximum,out int maximumIo);
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var cancellation=new CancellationTokenSource();
        Task? blocker=null;Task<StudioReplayAnalysis>? job=null;
        try
        {
            Check(ThreadPool.SetMinThreads(1,minimumIo)&&ThreadPool.SetMaxThreads(1,maximumIo),"tool can pause detached job admission with one owned worker slot");
            blocker=Task.Run(()=>{entered.Set();release.Wait();});
            Check(entered.Wait(TimeSpan.FromSeconds(3)),"owned scheduling barrier holds before detached analytics reads source");
            if(cancelled)cancellation.Cancel();
            job=owner.AnalyzeAsync(cancellation.Token);owner.Dispose();
            MapDiskCache.Prune(Path.Combine(cache,"sources"),0,TimeSpan.Zero);
            Check(!job.IsCompleted&&File.Exists(snapshot)&&File.ReadAllBytes(snapshot).SequenceEqual(original),
                "admitted detached analytics retains immutable source through owner close and aggressive eviction before its first worker read");
            release.Set();bool rejected=false;
            try{var analysis=job.GetAwaiter().GetResult();Check(analysis.SourceHash.Equals(Convert.ToHexString(SHA256.HashData(original)),StringComparison.OrdinalIgnoreCase),
                "deferred analytics completes with its frozen recording identity after owner closure");}
            catch(OperationCanceledException){rejected=true;}
            Check(rejected==cancelled,"pre-canceled detached analytics runs its admitted cleanup without leaking source ownership");
            MapDiskCache.Prune(Path.Combine(cache,"sources"),0,TimeSpan.Zero);
            Check(!File.Exists(snapshot),"completed or canceled detached job releases source for zero-budget eviction");
        }
        finally
        {
            release.Set();blocker?.GetAwaiter().GetResult();
            ThreadPool.SetMaxThreads(maximum,maximumIo);ThreadPool.SetMinThreads(minimum,minimumIo);
        }
    }
}
