using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapGen;

internal static class MapPerformanceChecks
{
    internal static void Run(string root)
    {
        foreach(int size in new[]{16,64,256})
        {
            string path=Path.Combine(root,"memory-"+size+".ppmap");
            var map=new MapDefinition{FormatVersion=2,MapId=Guid.NewGuid(),Name="MEMORY_PROFILE"};byte[] project=Encoding.UTF8.GetBytes(map.Serialize());byte[] data=new byte[size*1024*1024];
            var manifest=new MapPackageManifest{MapId=map.MapId,Name=map.Name,ContentHash=MapPackageReader.ContentHash(new[]{"project.json","textures/payload.tex"},name=>name=="project.json"?project:data)};
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            {
                using(var stream=zip.CreateEntry("project.json",CompressionLevel.NoCompression).Open())stream.Write(project);
                using(var stream=zip.CreateEntry("textures/payload.tex",CompressionLevel.NoCompression).Open())stream.Write(data);
                using(var stream=zip.CreateEntry("manifest.json").Open())stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest,MapPackageReader.JsonOptions));
            }
            data=null!;GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long before=GC.GetAllocatedBytesForCurrentThread();int gc2=GC.CollectionCount(2);var clock=Stopwatch.StartNew();
            using(var reader=new MapPackageReader(path)){_=reader.ReadProject();}clock.Stop();
            Console.WriteLine($"PROFILE strict package {size} MiB: {clock.Elapsed.TotalMilliseconds:0.0} ms, {(GC.GetAllocatedBytesForCurrentThread()-before)/(1024.0*1024):0.00} MiB allocated, gen2 {GC.CollectionCount(2)-gc2}, RSS {Process.GetCurrentProcess().WorkingSet64/(1024.0*1024):0.0} MiB");
        }
        var maps=Enumerable.Range(0,1000).Select(i=>Guid.NewGuid()).ToArray();
        foreach(int count in new[]{1000,10000,100000})
        {
            string storage=Path.Combine(root,"catalog-"+count);Directory.CreateDirectory(storage);
            var favorites=Enumerable.Range(0,count).Select(i=>new MapFavorite("user-"+i,maps[i%maps.Length],DateTimeOffset.UtcNow)).ToArray();
            File.WriteAllBytes(Path.Combine(storage,"map_favorites.json"),JsonSerializer.SerializeToUtf8Bytes(favorites,MapPackageReader.JsonOptions));
            var catalog=new MapCreatorCatalog(storage,new string('s',32));
            var entries=maps.Select(id=>new CommunityMap(new string('0',64),id,new string('1',64),"PROFILE","Profile","Fixture","1",0)).ToArray();
            var times=new double[10];for(int trial=0;trial<10;trial++){var clock=Stopwatch.StartNew();foreach(var entry in entries)_=catalog.Decorate(entry,"viewer");times[trial]=clock.Elapsed.TotalMilliseconds;}
            Array.Sort(times);Console.WriteLine($"PROFILE catalog {count} favorites / 1000 maps: list p50 {times[5]:0.0} ms, p90 {times[9]:0.0} ms");
        }
        string cache=Path.Combine(root,"disk-cache");
        var scheduler=new MapBuildScheduler(cache,build:(definition,path)=>
        {foreach(string file in MapOutputSet.Create(definition,path,path,path).Files)File.WriteAllBytes(file,new byte[4096]);return new();},cacheBudgetBytes:256*1024);
        var diskClock=Stopwatch.StartNew();MapBuildSnapshot? latest=null;
        for(int edit=0;edit<1000;edit++){latest=MapBuildSnapshot.Capture(new MapDefinition{Name="DISK_PROFILE_"+edit});_=scheduler.BuildAsync(latest).GetAwaiter().GetResult();}
        double cold=diskClock.Elapsed.TotalMilliseconds;var warm=new double[100];
        var restart=new MapBuildScheduler(cache,cacheBudgetBytes:256*1024);
        for(int read=0;read<warm.Length;read++){diskClock.Restart();var hit=restart.BuildAsync(latest!).GetAwaiter().GetResult();if(!hit.CacheHit)throw new Exception("Disk benchmark lost its protected warm entry.");warm[read]=diskClock.Elapsed.TotalMilliseconds;}
        Array.Sort(warm);long bytes=new DirectoryInfo(cache).EnumerateFiles("*",SearchOption.AllDirectories).Sum(file=>file.Length);
        Console.WriteLine($"PROFILE disk cache1000 edits: {cold:0.0} ms; retained {bytes} bytes / {Directory.EnumerateDirectories(cache).Count()} keys; restart warm p95 {warm[95]:0.00} ms");
    }
}
