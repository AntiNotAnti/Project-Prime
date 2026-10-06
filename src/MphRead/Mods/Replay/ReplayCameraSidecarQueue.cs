using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

/// <summary>One detached writer retains the latest accepted immutable authoring
/// snapshot. Canceling a wait never discards an accepted edit.</summary>
internal sealed class ReplayCameraSidecarQueue(Action<byte[], Func<Action, bool>> persist)
{
    private readonly object _gate = new();
    private readonly object _publicationGate = new();
    private long _discardEpoch;
    private byte[]? _pending, _accepted;
    private long _revision, _persisted;
    private Task _writer = Task.CompletedTask;
    private bool _running;
    private string? _error;
    internal bool Dirty { get { lock (_gate) return _persisted < _revision; } }
    internal string? Error { get { lock (_gate) return _error; } }
    internal bool Writing { get { lock (_gate) return _running; } }

    internal void Enqueue(byte[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Length > ReplayCameraTrack.MaxStateBytes) throw new InvalidDataException("Camera state exceeds its bounded size.");
        lock (_gate)
        {
            _accepted = (byte[])snapshot.Clone(); _pending = _accepted; _revision++;
            if (!_running) { _running = true; _writer = Task.Run(Drain); }
        }
    }
    internal void Retry()
    {
        lock (_gate)
        {
            if (_accepted == null || _persisted >= _revision) return;
            _pending = _accepted;
            if (!_running) { _running = true; _writer = Task.Run(Drain); }
        }
    }
    private void Drain()
    {
        while (true)
        {
            byte[]? snapshot; long revision, epoch;
            lock (_gate)
            { snapshot = _pending; _pending = null; revision = _revision; epoch = _discardEpoch; if (snapshot == null) _running = false; }
            if (snapshot == null) return;
            string? error = null;
            try
            {
                persist(snapshot, commit =>
                {
                    // File publication may wait on storage; Enqueue never waits on
                    // this gate. Explicit Discard competes here on a worker.
                    lock (_publicationGate)
                    {
                        lock (_gate) if (epoch != _discardEpoch) return false;
                        commit(); return true;
                    }
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { error = ex.Message; }
            lock (_gate)
            {
                if (epoch == _discardEpoch)
                { _error = error; if (error == null) _persisted = revision; }
                if (_pending == null) { _running = false; return; }
            }
        }
    }
    internal async Task DiscardAsync(CancellationToken cancellation = default)
    {
        Task? writer = null;
        await Task.Run(() =>
        {
            lock (_publicationGate)
            {
                cancellation.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    _discardEpoch++; _pending = _accepted = null;
                    _persisted = _revision; _error = null;
                    writer = _writer;
                }
            }
        }, cancellation).ConfigureAwait(false);
        // Discard was explicitly accepted. Finish observing that generation so
        // no canceled close leaves an old publication racing the retained file.
        await writer!.ConfigureAwait(false);
    }
    internal async Task FlushAsync(CancellationToken cancellation = default)
    {
        long required; lock (_gate) required = _revision;
        while (true)
        {
            Task writer; lock (_gate) writer = _writer;
            await writer.WaitAsync(cancellation).ConfigureAwait(false);
            lock (_gate)
            {
                if (_persisted >= required) return;
                if (!_running) throw new IOException(_error ?? "Camera edits have not been persisted.");
            }
        }
    }
}
