using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Sound;

/// <summary>One construction owner, with publication fenced to the newest request.</summary>
internal sealed class LatestResourceQueue<T> where T : class
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private CancellationTokenSource? _cancel;
    private long _generation;
    private bool _closed, _loading;

    internal bool Loading { get { lock (_gate) return _loading; } }
    internal bool Cancelled { get { lock (_gate) return _cancel?.IsCancellationRequested ?? false; } }
    internal Task Pending { get { lock (_gate) return _tail; } }

    internal void Request(Func<CancellationToken, T> build, Action<T> publish,
        Action<T> release, Action<Exception> report)
    {
        lock (_gate)
        {
            if (_closed) return;
            _cancel?.Cancel();
            var cancel = _cancel = new CancellationTokenSource();
            long generation = ++_generation;
            Task preceding = _tail;
            _loading = true;
            _tail = Task.Run(async () =>
            {
                T? owned = null;
                try
                {
                    // The tail includes every earlier constructor, including one
                    // that ignores cancellation until its third-party call ends.
                    await preceding.ConfigureAwait(false);
                    cancel.Token.ThrowIfCancellationRequested();
                    owned = build(cancel.Token);
                    lock (_gate)
                    {
                        if (_closed || generation != _generation || cancel.IsCancellationRequested) return;
                        publish(owned);
                        owned = null; // publication transfers ownership
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                catch (Exception ex) { try { report(ex); } catch { } }
                finally
                {
                    try { if (owned != null) release(owned); }
                    catch (Exception ex) { try { report(ex); } catch { } }
                    lock (_gate)
                    {
                        if (generation == _generation) _loading = false;
                        if (ReferenceEquals(_cancel, cancel)) _cancel = null;
                    }
                    cancel.Dispose();
                }
            });
        }
    }

    internal void Cancel()
    {
        lock (_gate)
        {
            // Invalidate publication immediately. An older worker cannot clear a
            // newer request's loading state, even when its constructor returns late.
            ++_generation;
            _cancel?.Cancel();
            _loading = false;
        }
    }

    internal Task Close()
    {
        lock (_gate)
        {
            _closed = true;
            ++_generation;
            _cancel?.Cancel();
            _loading = false;
            return _tail;
        }
    }
}
