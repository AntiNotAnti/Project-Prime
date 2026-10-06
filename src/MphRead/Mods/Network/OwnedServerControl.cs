using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

/// <summary>Private parent/child process control. It never opens a remote admin endpoint.</summary>
internal static class OwnedServerControl
{
    private const string TokenVariable = "PROJECT_PRIME_OWNED_SERVER_CONTROL";
    private const string StopPrefix = "PROJECT_PRIME_STOP ";
    private const string LoadPrefix = "PROJECT_PRIME_LOADING ";
    private const string ReadyPrefix = "PROJECT_PRIME_LOADED ";
    private static readonly ConditionalWeakTable<Process, Child> Children = new();
    private static long _loadGeneration;
    private sealed class Child
    {
        internal readonly string Token;
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly BoundedLoadingLease Lease = new();
        internal Child(string token) => Token = token;
    }

    internal static string Configure(ProcessStartInfo start)
    {
        string token = Guid.NewGuid().ToString("N");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.Environment[TokenVariable] = token;
        return token;
    }

    internal static void Attach(Process process, string token)
    {
        var child = new Child(token);
        Children.Add(process, child);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not string line) return;
            if (!Observe(child, line)) Mods.DebugLog.Line("hosted", line);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Mods.DebugLog.Line("hosted", e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private static bool Observe(Child child, string line)
    {
        string begin = LoadPrefix + child.Token + " ";
        string end = ReadyPrefix + child.Token + " ";
        if (line.StartsWith(begin, StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(begin.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long generation))
        { child.Lease.Begin(generation, child.Clock.Elapsed.TotalSeconds); return true; }
        if (line.StartsWith(end, StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(end.Length), NumberStyles.None, CultureInfo.InvariantCulture, out generation))
        { child.Lease.Complete(generation, child.Clock.Elapsed.TotalSeconds); return true; }
        return false;
    }

    internal static bool Loading(Process process) => Children.TryGetValue(process, out Child? child)
        && child.Lease.Active(child.Clock.Elapsed.TotalSeconds);
    internal static bool LoadingProbeGrace(Process process) => Children.TryGetValue(process, out Child? child)
        && child.Lease.ProtectsResponsiveness(child.Clock.Elapsed.TotalSeconds);

    internal static IDisposable Listen(Action stop)
    {
        string? token = Environment.GetEnvironmentVariable(TokenVariable);
        return Guid.TryParseExact(token, "N", out _) && Console.IsInputRedirected
            ? new Listener(token!, stop) : Empty.Instance;
    }

    private sealed class Listener : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new();
        internal Listener(string token, Action stop)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cancel.IsCancellationRequested)
                    {
                        string? line = await Console.In.ReadLineAsync(_cancel.Token).ConfigureAwait(false);
                        // EOF is not a stop request. In particular, an independently
                        // living server must not die when its launching client exits.
                        if (line == null) break;
                        if (String.Equals(line, StopPrefix + token, StringComparison.Ordinal))
                        { stop(); break; }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            });
        }
        public void Dispose() { _cancel.Cancel(); _cancel.Dispose(); }
    }

    internal static IDisposable LoadingScope()
    {
        string? token = Environment.GetEnvironmentVariable(TokenVariable);
        if (!Guid.TryParseExact(token, "N", out _) || !Console.IsOutputRedirected) return Empty.Instance;
        long generation = Interlocked.Increment(ref _loadGeneration);
        Console.WriteLine(LoadPrefix + token + " " + generation.ToString(CultureInfo.InvariantCulture));
        Console.Out.Flush();
        return new LoadScope(token!, generation);
    }

    private sealed class LoadScope : IDisposable
    {
        private readonly string _token;
        private readonly long _generation;
        private int _completed;
        internal LoadScope(string token, long generation) { _token = token; _generation = generation; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            Console.WriteLine(ReadyPrefix + _token + " " + _generation.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
        }
    }

    /// <summary>Ask an owned child to finalize first, then enforce a finite exit deadline.</summary>
    internal static bool Stop(Process process, int gracefulMilliseconds = 3000)
    {
        try
        {
            if (!process.HasExited)
            {
                if (Children.TryGetValue(process, out Child? child))
                {
                    // Do not let a full/broken pipe turn the graceful deadline
                    // into an unbounded parent wait. The writer owns its errors.
                    Task write = Task.Run(() =>
                    {
                        try
                        {
                            process.StandardInput.WriteLine(StopPrefix + child.Token);
                            process.StandardInput.Flush();
                        }
                        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
                    });
                    write.Wait(200);
                }
                else if (!OperatingSystem.IsWindows())
                    SendSignal(process.Id, 15); // SIGTERM; ShutdownSignals owns finalization.
                else
                    process.CloseMainWindow();
                if (!process.WaitForExit(gracefulMilliseconds))
                {
                    Mods.DebugLog.Line("hosted", $"child {process.Id} exceeded graceful stop deadline; forcing exit");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return false; }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int processId, int signal);
    private sealed class Empty : IDisposable
    {
        internal static readonly Empty Instance = new();
        public void Dispose() { }
    }
}

/// <summary>One finite grace per load generation; duplicate begin records cannot renew it.</summary>
internal sealed class BoundedLoadingLease
{
    internal const double MaximumSeconds = 180;
    private readonly object _gate = new();
    private long _generation;
    private double _deadline;
    private double _probeGraceUntil;
    internal void Begin(long generation, double now)
    {
        lock (_gate)
        {
            if (generation <= _generation || !double.IsFinite(now)) return;
            // A newer marker during an unfinished load also cannot extend its
            // original deadline. Only completion opens another bounded lease.
            bool active = _deadline > 0;
            _generation = generation;
            if (!active) _deadline = now + MaximumSeconds;
        }
    }
    internal void Complete(long generation, double now)
    {
        lock (_gate)
        {
            if (generation != _generation || _deadline <= 0 || !double.IsFinite(now)) return;
            _deadline = 0;
            // Give the asynchronous status probe one ordinary watchdog window
            // to observe the listener again after synchronous loading returns.
            _probeGraceUntil = now + 20;
        }
    }
    internal bool Active(double now)
    { lock (_gate) return _deadline > 0 && double.IsFinite(now) && now < _deadline; }
    internal bool ProtectsResponsiveness(double now)
    { lock (_gate) return double.IsFinite(now) && ((_deadline > 0 && now < _deadline) || now < _probeGraceUntil); }
}
