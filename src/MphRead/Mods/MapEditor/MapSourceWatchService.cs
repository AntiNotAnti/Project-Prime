using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>Notifications only. The UI owns explicit review/reload actions.</summary>
public sealed class MapSourceWatchService : IDisposable
{
    private readonly object _gate=new();
    private readonly Dictionary<string,string> _hashes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,DateTime> _pending=new(StringComparer.Ordinal);
    private readonly List<FileSystemWatcher> _watchers=new();
    private readonly Timer _timer;
    private bool _disposed;
    public event Action<string>? Changed;
    public MapSourceWatchService()=>_timer=new Timer(_=>Poll(),null,250,250);
    public void SetSources(IEnumerable<MapSourceDependency> sources)
    {
        lock(_gate)
        {
            if(_disposed)return;
            var desired=sources.GroupBy(s=>Path.GetFullPath(s.Path),StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First().Hash,StringComparer.Ordinal);
            bool changed=!desired.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(_hashes.Keys);
            foreach(string removed in _hashes.Keys.Except(desired.Keys).ToArray()){_hashes.Remove(removed);_pending.Remove(removed);}
            foreach(var source in desired)if(!_hashes.ContainsKey(source.Key)){_hashes[source.Key]=source.Value;_pending[source.Key]=DateTime.UtcNow;}
            if(!changed)return;
            foreach(var watcher in _watchers)watcher.Dispose();_watchers.Clear();
            foreach(string directory in desired.Keys.Select(p=>Path.GetDirectoryName(p)!).Distinct(StringComparer.Ordinal).Where(Directory.Exists))
            {
                var watcher=new FileSystemWatcher(directory){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.CreationTime};
                watcher.Changed+=(_,e)=>Queue(e.FullPath);watcher.Created+=(_,e)=>Queue(e.FullPath);watcher.Deleted+=(_,e)=>Queue(e.FullPath);
                watcher.Renamed+=(_,e)=>{Queue(e.OldFullPath);Queue(e.FullPath);};
                watcher.Error+=(_,_)=>{lock(_gate)foreach(string path in _hashes.Keys)_pending[path]=DateTime.UtcNow;};
                watcher.EnableRaisingEvents=true;_watchers.Add(watcher);
            }
        }
    }
    public void Queue(string path){lock(_gate)if(!_disposed&&_hashes.ContainsKey(path))_pending[path]=DateTime.UtcNow;}
    public void Poll()
    {
        string[] ready;lock(_gate){if(_disposed)return;ready=_pending.Where(p=>DateTime.UtcNow-p.Value>=TimeSpan.FromMilliseconds(500)).Select(p=>p.Key).ToArray();foreach(string path in ready)_pending.Remove(path);}
        foreach(string path in ready)
        {
            string hash;
            try{hash=File.Exists(path)?MapHash256.HashFile(path).ToString():"missing";}
            catch(IOException){Queue(path);continue;}catch(UnauthorizedAccessException){Queue(path);continue;}
            bool changed;lock(_gate){if(_disposed)return;changed=_hashes.TryGetValue(path,out var old)&&old!=hash;if(changed)_hashes[path]=hash;}
            if(changed)Changed?.Invoke(path);
        }
    }
    public void Dispose(){lock(_gate){if(_disposed)return;_disposed=true;_timer.Dispose();foreach(var watcher in _watchers)watcher.Dispose();_watchers.Clear();_pending.Clear();}}
}
