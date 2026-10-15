#if MPHREAD_STUDIO
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.StudioReplay;

/// <summary>Bounds a native worker even when graphics initialization or its UI thread never responds.</summary>
internal static class StudioReplayWorkerLifetime
{
    private static readonly object ChildGate=new();
    private static readonly HashSet<Process> Children=new();
    private static bool _abandoned;
    // Starting and registering share the gate with orphan shutdown: no child can
    // slip between the watchdog's final enumeration and process exit.
    internal static IDisposable StartChild(Process process)
    {
        lock(ChildGate)
        {
            if(_abandoned)throw new OperationCanceledException("Replay export parent exited.");
            if(!process.Start())throw new IOException("Replay media process did not start.");
            Children.Add(process);return new ChildLease(process);
        }
    }
    private sealed class ChildLease(Process process):IDisposable
    {public void Dispose(){lock(ChildGate)Children.Remove(process);}}
    internal static void AbandonChildren()
    {
        Process[] children;
        lock(ChildGate){_abandoned=true;children=new Process[Children.Count];Children.CopyTo(children);}
        foreach(var child in children)
        {
            try{if(!child.HasExited)child.Kill(entireProcessTree:true);child.WaitForExit(3000);}
            catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        }
    }
    internal static async Task WaitAsync(Process process, string directory, CancellationToken cancellation,
        TimeSpan? startupTimeout = null, TimeSpan? heartbeatTimeout = null, TimeSpan? cancelGrace = null)
    {
        var startup = startupTimeout ?? TimeSpan.FromSeconds(90);
        var heartbeat = heartbeatTimeout ?? TimeSpan.FromSeconds(90);
        var grace = cancelGrace ?? TimeSpan.FromSeconds(3);
        var elapsed = Stopwatch.StartNew();
        var sinceHeartbeat = Stopwatch.StartNew();
        DateTime lastHeartbeat = DateTime.MinValue;
        bool ready = false;
        string? failure = null;
        Task exit = process.WaitForExitAsync();
        while (!exit.IsCompleted)
        {
            if (cancellation.IsCancellationRequested) break;
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(directory, "heartbeat"));
            if (stamp.Year > 2000 && stamp != lastHeartbeat)
            { ready = true; lastHeartbeat = stamp; sinceHeartbeat.Restart(); }
            if (!ready && elapsed.Elapsed >= startup) { failure = "Replay export graphics did not become ready before the startup timeout."; break; }
            if (ready && sinceHeartbeat.Elapsed >= heartbeat) { failure = "Replay export stopped responding (heartbeat timeout)."; break; }
            await Task.WhenAny(exit, Task.Delay(50)).ConfigureAwait(false);
        }
        if (!exit.IsCompleted)
        {
            try { File.WriteAllText(Path.Combine(directory, "cancel"), "cancel"); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (await Task.WhenAny(exit, Task.Delay(grace)).ConfigureAwait(false) != exit)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
                // Kill is non-cooperative; always reap before scratch cleanup or releasing the queue.
                await exit.ConfigureAwait(false);
            }
        }
        await exit.ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (failure != null) throw new TimeoutException(failure);
    }
    internal static bool ParentAlive(int processId, long startTimeUtcTicks)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            return !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == startTimeUtcTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}

#endif
