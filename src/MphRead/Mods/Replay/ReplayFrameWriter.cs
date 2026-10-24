using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

internal readonly record struct ReplayExportPixels(byte[] Pixels, int Width, int Height, string Path);
internal readonly record struct ReplayFrameWriteResult(int Written, bool Cancelled, string? Error,
    double EncodeMilliseconds);

/// <summary>
/// The render owner reserves capacity before readback and transfers detached RGB
/// pixels once. Capacity includes the encoder's current frame and reservations,
/// so even a slow disk cannot grow the pixel budget. No worker may access a Scene
/// or GL resource. One worker preserves sequence order and owns all PNG writes.
/// </summary>
internal sealed class ReplayFrameWriter : IDisposable
{
    internal const int DefaultCapacity = 3;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Channel<ReplayExportPixels> _frames;
    private readonly Action<ReplayExportPixels> _write;
    private int _stopped, _pending, _written, _maximumPending;
    internal Task<ReplayFrameWriteResult> Completion { get; }
    internal int Pending => Volatile.Read(ref _pending);
    internal int Written => Volatile.Read(ref _written);
    internal int MaximumPending => Volatile.Read(ref _maximumPending);

    internal ReplayFrameWriter(Action<ReplayExportPixels> write, int capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > DefaultCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _slots = new(capacity, capacity);
        _frames = Channel.CreateBounded<ReplayExportPixels>(new BoundedChannelOptions(capacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        Completion = Task.Run(Run);
    }

    internal Reservation? TryReserve()
    {
        if (Volatile.Read(ref _stopped) != 0 || !_slots.Wait(0)) return null;
        if (Volatile.Read(ref _stopped) != 0) { _slots.Release(); return null; }
        int pending = Interlocked.Increment(ref _pending);
        // Reservations are made by the single render owner.
        if (pending > _maximumPending) Volatile.Write(ref _maximumPending, pending);
        return new Reservation(this);
    }

    internal sealed class Reservation : IDisposable
    {
        private ReplayFrameWriter? _owner;
        internal Reservation(ReplayFrameWriter owner) => _owner = owner;
        internal void Submit(ReplayExportPixels frame)
        {
            var owner = _owner ?? throw new InvalidOperationException("Frame reservation was already consumed.");
            if (frame.Width <= 0 || frame.Height <= 0
                || frame.Pixels == null || frame.Pixels.Length != checked(frame.Width * frame.Height * 3))
                throw new ArgumentException("Frame pixels do not match the RGB dimensions.");
            if (!owner._frames.Writer.TryWrite(frame))
                throw new InvalidOperationException("The PNG writer stopped before accepting the frame.");
            _owner = null; // The worker now owns both the pixels and the reserved slot.
        }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    internal void Complete()
    {
        Interlocked.Exchange(ref _stopped, 1);
        _frames.Writer.TryComplete();
    }

    internal void Cancel()
    {
        Complete();
        // Tokens and semaphore live until all already-acquired reservations retire.
        // Cancellation cannot interrupt native PNG encoding; ownership is retained
        // until that last write returns, and no new job can overwrite its files.
        _cancellation.Cancel();
    }

    public void Dispose()
    {
        if (!Completion.IsCompleted || Pending != 0)
            throw new InvalidOperationException("The frame writer still owns pixels or reservations.");
        _cancellation.Dispose();
        _slots.Dispose();
    }

    private void Release()
    {
        Interlocked.Decrement(ref _pending);
        _slots.Release();
    }

    private async Task<ReplayFrameWriteResult> Run()
    {
        string? error = null;
        double milliseconds = 0;
        try
        {
            while (await _frames.Reader.WaitToReadAsync(_cancellation.Token).ConfigureAwait(false))
            {
                while (_frames.Reader.TryRead(out var frame))
                {
                    try
                    {
                        _cancellation.Token.ThrowIfCancellationRequested();
                        long start = Stopwatch.GetTimestamp();
                        _write(frame);
                        milliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        Interlocked.Increment(ref _written);
                    }
                    finally
                    {
                        // Clear the worker's reference before reopening capacity;
                        // an async enumerator's Current could retain a fourth RGB
                        // buffer while waiting for the producer's next frame.
                        frame = default;
                        Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { error = exception.Message; }
        finally
        {
            Complete();
            // A cancelled or failed encoder discards queued detached buffers.
            while (_frames.Reader.TryRead(out _)) Release();
        }
        return new(Written, _cancellation.IsCancellationRequested, error, milliseconds);
    }
}
