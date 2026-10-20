using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Import;

// The document remains opaque until a worker opens it. No URI, provider name,
// persisted grant or protected payload is kept by the shared importer.
internal sealed record OwnedSourceDocument(Func<CancellationToken, Task<Stream>> Open);

internal sealed class SourceDocumentPickerSession : IDisposable
{
    internal const int FirstCode = 0x5310, LastCode = 0x5fff;
    private static int _highWater = FirstCode - 1;
    private readonly object _gate = new();
    private Pending? _pending;
    private bool _disposed;
    private sealed class Pending(int code)
    {
        internal readonly int Code = code;
        internal readonly TaskCompletionSource<OwnedSourceDocument?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Cancellation;
    }
    internal static int HighWater => Volatile.Read(ref _highWater);
    internal static void RestoreHighWater(int value)
    {
        value = Math.Clamp(value, FirstCode - 1, LastCode);
        int seen;
        do { seen = HighWater; if (seen >= value) return; }
        while (Interlocked.CompareExchange(ref _highWater, value, seen) != seen);
    }
    internal bool HasPending { get { lock (_gate) return _pending != null; } }
    internal (int Code, Task<OwnedSourceDocument?> Task) Begin(CancellationToken token)
    {
        Pending pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (_pending != null) throw new InvalidOperationException("Close the previous document chooser before opening another.");
            int code = Interlocked.Increment(ref _highWater);
            if (code > LastCode) throw new InvalidOperationException("Restart the application before opening another source document.");
            _pending = pending = new(code);
        }
        // Cancellation leaves a launched chooser as a tombstone. Its eventual
        // result is drained before another request can use this activity.
        var registration = token.Register(() => pending.Completion.TrySetCanceled(token));
        bool retired;
        lock (_gate)
        {
            retired = !ReferenceEquals(_pending, pending);
            if (!retired) pending.Cancellation = registration;
        }
        if (retired) registration.Dispose();
        return (pending.Code, pending.Completion.Task);
    }
    internal bool TryLaunch(int code, Action launch)
    {
        Pending? retired = null;
        try
        {
            lock (_gate)
            {
                if (_disposed || _pending is not { } p || p.Code != code) return false;
                if (p.Completion.Task.IsCompleted) { retired = p; _pending = null; return false; }
                // Serialize launch with retirement. The callback only starts
                // the system chooser; it must not open or copy the source.
                launch();
                return true;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_pending is { } p && p.Code == code) { retired = p; _pending = null; p.Completion.TrySetException(ex); }
            }
            return false;
        }
        finally { retired?.Cancellation.Dispose(); }
    }
    internal bool TryComplete(int code, Func<OwnedSourceDocument?> document)
    {
        Pending? pending;
        lock (_gate)
        {
            if (_pending is not { } p || p.Code != code) return false;
            pending = p; _pending = null;
            if (!p.Completion.Task.IsCompleted)
            {
                try { p.Completion.TrySetResult(document()); }
                catch (Exception ex) { p.Completion.TrySetException(ex); }
            }
        }
        pending.Cancellation.Dispose();
        return true;
    }
    public void Dispose()
    {
        Pending? pending;
        lock (_gate) { if (_disposed) return; _disposed = true; pending = _pending; _pending = null; pending?.Completion.TrySetCanceled(); }
        pending?.Cancellation.Dispose();
    }
}

internal static class SourceDocumentPlatform
{
    private static readonly object Gate = new();
    private static Registration? _current;
    internal sealed class Registration : IDisposable
    {
        internal readonly Func<CancellationToken, Task<OwnedSourceDocument?>> Pick;
        internal readonly string ScratchRoot;
        private readonly CancellationTokenSource _lifetime = new();
        internal readonly CancellationToken Lifetime;
        private int _disposed;
        internal Registration(Func<CancellationToken, Task<OwnedSourceDocument?>> pick, string root)
        { Pick = pick; ScratchRoot = Path.GetFullPath(root); Lifetime = _lifetime.Token; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (Gate) { if (ReferenceEquals(_current, this)) _current = null; }
            _lifetime.Cancel();
            // Captured tokens stay cancelled while an uncooperative provider
            // retires; no source stream is owned by the registration.
            _lifetime.Dispose();
        }
    }
    internal static Registration Install(Func<CancellationToken, Task<OwnedSourceDocument?>> pick, string scratchRoot)
    {
        var registration = new Registration(pick, scratchRoot);
        Registration? old;
        lock (Gate) { old = _current; _current = registration; }
        old?.Dispose();
        return registration;
    }
    internal static Registration Capture()
    { lock (Gate) return _current ?? throw new InvalidOperationException("The source document picker is unavailable."); }
}
