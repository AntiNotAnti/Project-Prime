using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Cache eviction and opening a key lease share a stable cross-process gate.
/// This prevents deleting/recreating a lock pathname while a waiter still owns its old inode.</summary>
internal static class MapDiskCache
{
    internal static FileStream Acquire(string root,string key,CancellationToken cancellation=default)
    {
        Directory.CreateDirectory(root);
        var clock=Stopwatch.StartNew();
        while(true)
        {
            cancellation.ThrowIfCancellationRequested();
            using(var owners=Open(Path.Combine(root,".owners.lock"),cancellation))
            {
                try{return new FileStream(Path.Combine(root,key+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException)when(clock.Elapsed<TimeSpan.FromSeconds(30)){}
            }
            Thread.Sleep(25);
        }
    }
    private static FileStream Open(string path,CancellationToken cancellation)
    {
        var clock=Stopwatch.StartNew();
        while(true)
        {
            cancellation.ThrowIfCancellationRequested();
            try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException)when(clock.Elapsed<TimeSpan.FromSeconds(30)){Thread.Sleep(25);}
        }
    }
    internal static void Prune(string root,long budget,TimeSpan retention,string? protectedKey=null)
    {
        try
        {
            Directory.CreateDirectory(root);
            using var owners=Open(Path.Combine(root,".owners.lock"),CancellationToken.None);
            var entries=Directory.EnumerateDirectories(root).Select(path=>new DirectoryInfo(path))
                .Where(info=>MapCommunityClient.ValidHash(info.Name)).Select(info=>new
                {
                    Directory=info,Last=File.GetLastWriteTimeUtc(Path.Combine(info.FullName,"cache.json")),
                    Bytes=info.EnumerateFiles("*",SearchOption.AllDirectories).Sum(file=>file.Length)
                }).OrderBy(entry=>entry.Last).ToArray();
            long total=entries.Sum(entry=>entry.Bytes);
            foreach(var entry in entries)
            {
                if(entry.Directory.Name==protectedKey)continue;
                if(total<=budget&&entry.Last>=DateTime.UtcNow-retention)continue;
                string lockPath=Path.Combine(root,entry.Directory.Name+".lock");
                FileStream lease;
                try{lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException){continue;}
                using(lease){entry.Directory.Delete(true);total-=entry.Bytes;}
                File.Delete(lockPath);
            }
            // A tagged staging directory exists only while its key is held.
            // An idle key proves its compiler died before promotion/cleanup.
            foreach(string stage in Directory.EnumerateDirectories(root,".build-*"))
            {
                string name=Path.GetFileName(stage);
                if(name.Length!=104||name[71]!='-')continue; // Preserve untagged legacy directories.
                string key=name.Substring(7,64);
                if(!MapCommunityClient.ValidHash(key)||key==protectedKey)continue;
                string lockPath=Path.Combine(root,key+".lock");FileStream lease;
                try{lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){continue;}
                using(lease)Directory.Delete(stage,true);
                if(!Directory.Exists(Path.Combine(root,key)))File.Delete(lockPath);
            }
            // Failed builds leave no immutable directory. Reclaim their idle key
            // files too, with the same inode ownership rule.
            foreach(string path in Directory.EnumerateFiles(root,"*.lock"))
            {
                string key=Path.GetFileNameWithoutExtension(path);
                if(!MapCommunityClient.ValidHash(key)||key==protectedKey||Directory.Exists(Path.Combine(root,key)))continue;
                FileStream lease;try{lease=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}catch(IOException){continue;}
                lease.Dispose();File.Delete(path);
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException)
        {Console.Error.WriteLine("[map-cache] Pruning deferred: "+ex.Message);}
    }
}
