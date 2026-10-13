using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Sound;

internal readonly record struct MusicLoadRequest(SeqId Sequence, ushort Tracks, float Volume, long Version);

internal interface IMusicLoadGraph
{
    void Attach();
    void BeginRetirement();
    void Retire();
}

/// <summary>One worker, one pending request, and one retiring graph. Decoder
/// construction stays private until the latest request is ready.</summary>
internal sealed class MusicLoadOwner
{
    private readonly object _gate = new();
    private readonly Func<MusicLoadRequest, Func<bool>, IMusicLoadGraph> _create;
    private readonly Action<Exception> _report;
    private MusicLoadRequest? _pending;
    private IMusicLoadGraph? _current, _retiring;
    private long _version;
    private bool _loading, _stopLoading, _shutdown, _workerRunning;
    private Task _worker = Task.CompletedTask;

    internal MusicLoadOwner(Func<MusicLoadRequest, Func<bool>, IMusicLoadGraph> create, Action<Exception> report)
    { _create = create; _report = report; }
    internal bool Loading { get { lock (_gate) return _loading; } }
    internal bool StopLoading { get { lock (_gate) return _stopLoading; } }
    internal IMusicLoadGraph? Current => Volatile.Read(ref _current);
    internal Task Completion { get { lock (_gate) return _worker; } }

    internal void Load(SeqId sequence, ushort tracks, float volume)
    {
        lock (_gate)
        {
            if (_shutdown) return;
            RetireCurrent();
            _stopLoading = false;
            _version++;
            _loading = sequence != SeqId.None;
            _pending = _loading ? new MusicLoadRequest(sequence, tracks, volume, _version) : null;
            StartWorker();
        }
    }

    internal bool WaitForLoad(TimeSpan timeout, int sleepMs)
    {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            TimeSpan remaining;
            lock (_gate)
            {
                if (!_loading) return true;
                remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    // Deadline and cancellation are one decision: publication
                    // cannot sneak between a timed-out wait and its fence.
                    CancelPending();
                    return false;
                }
            }
            Thread.Sleep(Math.Max(1, Math.Min(Math.Max(1, sleepMs), (int)Math.Ceiling(remaining.TotalMilliseconds))));
        }
    }

    internal void CancelLoading(bool cancel)
    {
        lock (_gate)
        {
            _stopLoading = cancel;
            if (!cancel) return;
            CancelPending();
        }
    }

    private void CancelPending()
    {
        _stopLoading = true;
        _version++;
        _pending = null;
        _loading = false;
    }

    internal void Remove(bool shutdown = false)
    {
        lock (_gate)
        {
            _shutdown |= shutdown;
            _version++;
            _stopLoading = true;
            _pending = null;
            _loading = false;
            RetireCurrent();
            StartWorker();
        }
    }

    private void RetireCurrent()
    {
        IMusicLoadGraph? graph = _current;
        if (graph == null) return;
        graph.BeginRetirement(); // Atomics only: scene exit never waits for a callback.
        Volatile.Write(ref _current, null);
        _retiring = graph;
    }

    private bool IsCurrent(long version)
    { lock (_gate) return !_shutdown && version == _version; }

    private void StartWorker()
    {
        if (_workerRunning || (_pending == null && _retiring == null)) return;
        _workerRunning = true;
        _worker = Task.Run(Work);
    }

    private void Work()
    {
        while (true)
        {
            MusicLoadRequest? request;
            IMusicLoadGraph? retiring;
            lock (_gate)
            {
                retiring = _retiring;
                _retiring = null;
                request = _pending;
                _pending = null;
                if (retiring == null && request == null)
                { _workerRunning = false; return; }
            }
            if (retiring != null) Retire(retiring);
            if (request is not { } value || !IsCurrent(value.Version)) continue;
            IMusicLoadGraph? graph = null;
            try
            {
                graph = _create(value, () => IsCurrent(value.Version));
                if (IsCurrent(value.Version)) graph.Attach();
                lock (_gate)
                {
                    if (!_shutdown && value.Version == _version)
                    {
                        Volatile.Write(ref _current, graph);
                        graph = null;
                        _loading = false;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _report(ex); }
            finally
            {
                if (graph != null) Retire(graph);
                lock (_gate)
                {
                    if (value.Version == _version) _loading = false;
                }
            }
        }
    }

    private void Retire(IMusicLoadGraph graph)
    {
        try { graph.BeginRetirement(); graph.Retire(); }
        catch (Exception ex) { _report(ex); }
    }
}

/// <summary>Callbacks never wait. Retirement runs on the owner worker after all
/// readers admitted before the retirement flag have finished.</summary>
internal sealed class MusicCallbackLifetime
{
    private int _retired, _readers;
    internal bool TryEnter()
    {
        Interlocked.Increment(ref _readers);
        if (Volatile.Read(ref _retired) == 0) return true;
        Interlocked.Decrement(ref _readers);
        return false;
    }
    internal void Exit() => Interlocked.Decrement(ref _readers);
    internal void BeginRetirement() => Interlocked.Exchange(ref _retired, 1);
    internal bool IsIdle => Volatile.Read(ref _readers) == 0;
}
