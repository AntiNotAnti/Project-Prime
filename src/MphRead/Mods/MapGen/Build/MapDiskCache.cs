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
    /// <summary>Independent shared ownership of immutable bytes. The preparation
    /// lease stays exclusive; callers acquire this pin before releasing it.</summary>
    internal static IDisposable Pin(string root, string key, CancellationToken cancellation = default)
    {
        if (!MapCommunityClient.ValidHash(key)) throw new ArgumentException("Invalid immutable cache key.", nameof(key));
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        using var owners = Open(Path.Combine(root, ".owners.lock"), cancellation);
        cancellation.ThrowIfCancellationRequested();
        // FileStream uses a shared kernel lease on Unix and shared open ownership
        // on Windows. Prune must open this same inode exclusively before deletion.
        var stream = new FileStream(Path.Combine(root, key + ".pins.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try { cancellation.ThrowIfCancellationRequested(); return new CachePin(root, key, stream); }
        catch { stream.Dispose(); throw; }
    }
    /// <summary>Retain an already owned shared kernel lease without reopening a
    /// file or waiting on the cross-process owners gate.</summary>
    internal static IDisposable Retain(IDisposable pin) => pin switch
    {
        CachePin owner => owner.Retain(),
        RetainedPin lease => lease.Retain(),
        _ => throw new ArgumentException("The resource is not a shared cache pin.", nameof(pin))
    };
    private sealed class CachePin(string root, string key, FileStream stream) : IDisposable
    {
        private readonly object _gate = new();
        private FileStream? _stream = stream;
        private int _references = 1, _ownerDisposed;
        internal IDisposable Retain()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_stream == null, this);
                _references++; return new RetainedPin(this);
            }
        }
        public void Dispose()
        { if (Interlocked.Exchange(ref _ownerDisposed, 1) == 0) Release(); }
        internal void Release()
        {
            FileStream? owned;
            lock (_gate)
            { if (--_references != 0) return; owned = _stream; _stream = null; }
            if (owned == null) return;
            try
            {
                string directory = Path.Combine(root, key);
                if (Directory.Exists(directory)) Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { owned.Dispose(); }
        }
    }
    private sealed class RetainedPin(CachePin owner) : IDisposable
    {
        private readonly object _gate = new();
        private CachePin? _owner = owner;
        internal IDisposable Retain()
        {
            lock (_gate)
            { ObjectDisposedException.ThrowIf(_owner == null, this); return _owner.Retain(); }
        }
        public void Dispose()
        { CachePin? owned; lock (_gate) { owned = _owner; _owner = null; } owned?.Release(); }
    }
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
    internal static void RemoveIdleKey(string root, string key)
    {
        try
        {
            using var owners = Open(Path.Combine(root, ".owners.lock"), CancellationToken.None);
            string path = Path.Combine(root, key + ".lock");
            if (!File.Exists(path) || Directory.Exists(Path.Combine(root, key))) return;
            using var lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            string pins = Path.Combine(root, key + ".pins.lock");
            using var pinLease = Exclusive(pins);
            lease.Dispose(); pinLease.Dispose();
            File.Delete(path); File.Delete(pins);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    internal static void Prune(string root,long budget,TimeSpan retention,string? protectedKey=null,
        CancellationToken cancellation=default,Func<string,bool>? protectedDirectory=null)
    {
        if (budget < 0 || retention < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        try
        {
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            using var owners=Open(Path.Combine(root,".owners.lock"),cancellation);
            var entries=Directory.EnumerateDirectories(root).Select(path=>new DirectoryInfo(path))
                .Where(info=>MapCommunityClient.ValidHash(info.Name) && (info.Attributes & FileAttributes.ReparsePoint) == 0).Select(info=>new
                {
                    Directory=info,Last=LastUse(info.FullName),Bytes=Size(info.FullName,cancellation)
                }).OrderBy(entry=>entry.Last).ToArray();
            long total=entries.Sum(entry=>entry.Bytes);
            foreach(var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if(entry.Directory.Name==protectedKey || protectedDirectory?.Invoke(entry.Directory.FullName)==true)continue;
                if(total<=budget&&entry.Last>=DateTime.UtcNow-retention)continue;
                string lockPath=Path.Combine(root,entry.Directory.Name+".lock");
                string pinPath=Path.Combine(root,entry.Directory.Name+".pins.lock");
                FileStream lease; FileStream? pins=null;
                try{lease=Exclusive(lockPath);try{pins=Exclusive(pinPath);}catch{lease.Dispose();throw;}}
                catch(IOException){continue;}
                using(lease) using(pins)
                {
                    // Recheck after acquiring both leases: a writer or the last
                    // reader may have refreshed this entry since enumeration.
                    if(!Directory.Exists(entry.Directory.FullName)
                        || (File.GetAttributes(entry.Directory.FullName)&FileAttributes.ReparsePoint)!=0
                        || protectedDirectory?.Invoke(entry.Directory.FullName)==true)continue;
                    if(total<=budget&&LastUse(entry.Directory.FullName)>=DateTime.UtcNow-retention)continue;
                    cancellation.ThrowIfCancellationRequested();
                    entry.Directory.Delete(true);total-=entry.Bytes;
                }
                File.Delete(lockPath); File.Delete(pinPath);
            }
            // A tagged staging directory exists only while its key is held.
            // An idle key proves its compiler died before promotion/cleanup.
            foreach(string stage in Directory.EnumerateDirectories(root,".build-*"))
            {
                cancellation.ThrowIfCancellationRequested();
                string name=Path.GetFileName(stage);
                if(name.Length!=104||name[71]!='-')continue; // Preserve untagged legacy directories.
                string key=name.Substring(7,64);
                if(!MapCommunityClient.ValidHash(key)||key==protectedKey
                    || protectedDirectory?.Invoke(Path.Combine(root,key))==true
                    || (File.GetAttributes(stage)&FileAttributes.ReparsePoint)!=0)continue;
                string lockPath=Path.Combine(root,key+".lock"),pinPath=Path.Combine(root,key+".pins.lock");FileStream lease;FileStream? pins=null;
                try{lease=Exclusive(lockPath);try{pins=Exclusive(pinPath);}catch{lease.Dispose();throw;}}catch(IOException){continue;}
                using(lease)using(pins)Directory.Delete(stage,true);
                if(!Directory.Exists(Path.Combine(root,key))){File.Delete(lockPath);File.Delete(pinPath);}
            }
            // Failed builds leave no immutable directory. Reclaim their idle key
            // files too, with the same inode ownership rule.
            var orphanKeys=Directory.EnumerateFiles(root,"*.lock").Select(path=>
            {
                string name=Path.GetFileName(path);
                return name.EndsWith(".pins.lock",StringComparison.Ordinal) ? name[..^10] : name[..^5];
            }).Where(MapCommunityClient.ValidHash).Distinct(StringComparer.Ordinal).ToArray();
            foreach(string key in orphanKeys)
            {
                cancellation.ThrowIfCancellationRequested();
                string path=Path.Combine(root,key+".lock");
                if(!MapCommunityClient.ValidHash(key)||key==protectedKey||Directory.Exists(Path.Combine(root,key))
                    || protectedDirectory?.Invoke(Path.Combine(root,key))==true)continue;
                string pinPath=Path.Combine(root,key+".pins.lock");FileStream lease;FileStream? pins=null;
                try{lease=Exclusive(path);try{pins=Exclusive(pinPath);}catch{lease.Dispose();throw;}}catch(IOException){continue;}
                lease.Dispose();pins.Dispose();File.Delete(path);File.Delete(pinPath);
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException)
        {Console.Error.WriteLine("[map-cache] Pruning deferred: "+ex.Message);}
    }
    private static FileStream Exclusive(string path) => new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    private static DateTime LastUse(string directory) => new(Math.Max(Directory.GetLastWriteTimeUtc(directory).Ticks,
        File.GetLastWriteTimeUtc(Path.Combine(directory,"cache.json")).Ticks),DateTimeKind.Utc);
    private static long Size(string directory,CancellationToken cancellation)
    {
        long bytes=0;
        foreach(var file in new DirectoryInfo(directory).EnumerateFiles("*",new EnumerationOptions
            { RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint }))
        {cancellation.ThrowIfCancellationRequested();bytes=checked(bytes+file.Length);}
        return bytes;
    }
}
