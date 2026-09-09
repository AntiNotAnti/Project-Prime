using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MphRead
{
    /// <summary>The source target represented by a captured frame.</summary>
    public enum CaptureTargetKind
    {
        SceneTarget,
        FinalPresentedFrame,
        ThumbnailTarget
    }

    /// <summary>Explicit byte layout of a capture before an encoder consumes it.</summary>
    public enum CapturePixelFormat
    {
        Rgb8,
        Rgba8,
        Bgra8
    }

    /// <summary>Whether row zero is the top or bottom row of the target.</summary>
    public enum CaptureRowOrientation
    {
        TopDown,
        BottomUp
    }

    /// <summary>How a capture request is delivered to its consumer.</summary>
    public enum CaptureDeliveryKind
    {
        Screenshot,
        Recording
    }

    /// <summary>
    /// Immutable request for one backend-neutral frame readback.
    ///
    /// A request is attached to a <see cref="RenderFrame"/> before it is
    /// sealed and a backend encodes it. The requested layout is the layout existing desktop
    /// encoders consume (RGB8, bottom-up); an SDL backend may read a native
    /// texture layout and normalize to these values before publishing its
    /// result.
    /// </summary>
    public sealed class RenderCaptureRequest
    {
        public RenderCaptureRequest(Guid requestId, long originatingFrame, CaptureTargetKind target,
            int width, int height, CapturePixelFormat pixelFormat,
            CaptureRowOrientation rowOrientation, CaptureDeliveryKind delivery,
            string? outputName = null)
        {
            if (requestId == Guid.Empty) throw new ArgumentException("A capture request ID is required.", nameof(requestId));
            if (originatingFrame < 0) throw new ArgumentOutOfRangeException(nameof(originatingFrame));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            RequestId = requestId;
            OriginatingFrame = originatingFrame;
            Target = target;
            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            RowOrientation = rowOrientation;
            Delivery = delivery;
            OutputName = outputName;
        }

        public Guid RequestId { get; }
        public long OriginatingFrame { get; }
        public CaptureTargetKind Target { get; }
        public int Width { get; }
        public int Height { get; }
        public CapturePixelFormat PixelFormat { get; }
        public CaptureRowOrientation RowOrientation { get; }
        public CaptureDeliveryKind Delivery { get; }
        public string? OutputName { get; }
    }

    /// <summary>
    /// An owned, immutable description of one readback. The constructor copies
    /// the source span, so a pool rented by a backend can be returned as soon
    /// as the result is queued and no producer-owned buffer can be mutated
    /// while an encoder is consuming it.
    /// </summary>
    public sealed class RenderCaptureResult
    {
        private readonly byte[] _bytes;

        public RenderCaptureResult(Guid requestId, long originatingFrame, CaptureTargetKind target,
            int width, int height, CapturePixelFormat pixelFormat,
            CaptureRowOrientation rowOrientation, ReadOnlySpan<byte> bytes, string? outputName = null,
            CaptureDeliveryKind delivery = CaptureDeliveryKind.Screenshot)
        {
            if (requestId == Guid.Empty) throw new ArgumentException("A capture request ID is required.", nameof(requestId));
            if (originatingFrame < 0) throw new ArgumentOutOfRangeException(nameof(originatingFrame));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            int expected = checked(width * height * BytesPerPixel(pixelFormat));
            if (bytes.Length != expected)
            {
                throw new ArgumentException($"{pixelFormat} capture requires {expected} bytes, got {bytes.Length}.", nameof(bytes));
            }

            RequestId = requestId;
            OriginatingFrame = originatingFrame;
            Target = target;
            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            RowOrientation = rowOrientation;
            Delivery = delivery;
            OutputName = outputName;
            _bytes = bytes.ToArray();
        }

        public Guid RequestId { get; }
        public long OriginatingFrame { get; }
        public CaptureTargetKind Target { get; }
        public int Width { get; }
        public int Height { get; }
        public CapturePixelFormat PixelFormat { get; }
        public CaptureRowOrientation RowOrientation { get; }
        public CaptureDeliveryKind Delivery { get; }
        public string? OutputName { get; }
        public ReadOnlyMemory<byte> Bytes => _bytes;

        public byte[] CopyBytes() => (byte[])_bytes.Clone();

        public static int BytesPerPixel(CapturePixelFormat format)
        {
            return format switch
            {
                CapturePixelFormat.Rgb8 => 3,
                CapturePixelFormat.Rgba8 or CapturePixelFormat.Bgra8 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown capture format.")
            };
        }
    }

    /// <summary>
    /// A capture request that could not be completed. Failures are delivered
    /// through the same backend-neutral boundary as successful results so a
    /// host cannot silently acknowledge a capture that was dropped by a
    /// bounded GPU readback queue.
    /// </summary>
    public sealed class RenderCaptureFailure
    {
        public RenderCaptureFailure(RenderCaptureRequest request, string error)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(error)) throw new ArgumentException("A capture failure needs an explanation.", nameof(error));
            Error = error;
        }

        public RenderCaptureRequest Request { get; }
        public Guid RequestId => Request.RequestId;
        public long OriginatingFrame => Request.OriginatingFrame;
        public CaptureTargetKind Target => Request.Target;
        public CaptureDeliveryKind Delivery => Request.Delivery;
        public string? OutputName => Request.OutputName;
        public string Error { get; }
    }

    /// <summary>
    /// Converts a mapped GPU transfer buffer into the exact owned byte layout
    /// requested by a capture. SDL GPU download rows are allowed to contain
    /// padding; the normalizer deliberately consumes a caller-supplied pitch
    /// instead of assuming tightly packed rows. It also makes the row
    /// orientation explicit, which keeps backend and encoder conventions
    /// independent.
    /// </summary>
    public static class RenderCapturePixels
    {
        public static byte[] Normalize(ReadOnlySpan<byte> source, int width, int height,
            CapturePixelFormat sourceFormat, CaptureRowOrientation sourceOrientation,
            CapturePixelFormat destinationFormat, CaptureRowOrientation destinationOrientation,
            int sourceRowPitch)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            int sourceBytesPerPixel = RenderCaptureResult.BytesPerPixel(sourceFormat);
            int destinationBytesPerPixel = RenderCaptureResult.BytesPerPixel(destinationFormat);
            int minimumPitch = checked(width * sourceBytesPerPixel);
            if (sourceRowPitch < minimumPitch)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRowPitch),
                    $"A {sourceFormat} row needs at least {minimumPitch} bytes.");
            }
            int requiredSourceBytes = checked(sourceRowPitch * height);
            if (source.Length < requiredSourceBytes)
            {
                throw new ArgumentException($"The mapped transfer contains {source.Length} bytes, but {requiredSourceBytes} are required.", nameof(source));
            }

            byte[] destination = new byte[checked(width * height * destinationBytesPerPixel)];
            for (int destinationY = 0; destinationY < height; destinationY++)
            {
                int sourceY = sourceOrientation == destinationOrientation
                    ? destinationY
                    : height - destinationY - 1;
                ReadOnlySpan<byte> sourceRow = source.Slice(checked(sourceY * sourceRowPitch), minimumPitch);
                Span<byte> destinationRow = destination.AsSpan(
                    checked(destinationY * width * destinationBytesPerPixel), width * destinationBytesPerPixel);
                for (int x = 0; x < width; x++)
                {
                    int sourceOffset = x * sourceBytesPerPixel;
                    byte red;
                    byte green;
                    byte blue;
                    byte alpha;
                    if (sourceFormat == CapturePixelFormat.Bgra8)
                    {
                        blue = sourceRow[sourceOffset];
                        green = sourceRow[sourceOffset + 1];
                        red = sourceRow[sourceOffset + 2];
                        alpha = sourceRow[sourceOffset + 3];
                    }
                    else
                    {
                        red = sourceRow[sourceOffset];
                        green = sourceRow[sourceOffset + 1];
                        blue = sourceRow[sourceOffset + 2];
                        alpha = sourceFormat == CapturePixelFormat.Rgba8
                            ? sourceRow[sourceOffset + 3] : (byte)255;
                    }

                    int destinationOffset = x * destinationBytesPerPixel;
                    if (destinationFormat == CapturePixelFormat.Bgra8)
                    {
                        destinationRow[destinationOffset] = blue;
                        destinationRow[destinationOffset + 1] = green;
                        destinationRow[destinationOffset + 2] = red;
                        destinationRow[destinationOffset + 3] = alpha;
                    }
                    else
                    {
                        destinationRow[destinationOffset] = red;
                        destinationRow[destinationOffset + 1] = green;
                        destinationRow[destinationOffset + 2] = blue;
                        if (destinationFormat == CapturePixelFormat.Rgba8)
                        {
                            destinationRow[destinationOffset + 3] = alpha;
                        }
                    }
                }
            }
            return destination;
        }
    }

    /// <summary>
    /// Bounded producer/consumer storage for recordings. Stop prevents new
    /// frames but never discards already queued frames; the consumer drains
    /// them. Starting again reopens the same queue, including while a previous
    /// drain is in progress, so restarting a recording cannot race an old
    /// encoder task or lose ownership of a pending result.
    /// </summary>
    public sealed class CaptureRecordingQueue
    {
        private readonly object _gate = new object();
        private readonly Queue<RenderCaptureResult> _pending;
        private readonly int _capacity;
        private bool _recording;
        private bool _draining;
        private long _dropped;

        public CaptureRecordingQueue(int capacity = 8)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _pending = new Queue<RenderCaptureResult>(capacity);
        }

        public int Capacity => _capacity;
        public bool IsRecording { get { lock (_gate) return _recording; } }
        public bool IsDraining { get { lock (_gate) return _draining; } }
        public int Count { get { lock (_gate) return _pending.Count; } }
        public long DroppedCount { get { lock (_gate) return _dropped; } }

        public void StartRecording()
        {
            lock (_gate)
            {
                _recording = true;
                _draining = _pending.Count > 0;
            }
        }

        public void StopRecording()
        {
            lock (_gate)
            {
                _recording = false;
                _draining = _pending.Count > 0;
            }
        }

        public bool TryEnqueue(RenderCaptureResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            lock (_gate)
            {
                if (!_recording)
                {
                    return false;
                }
                if (_pending.Count >= _capacity)
                {
                    _dropped++;
                    return false;
                }
                _pending.Enqueue(result);
                _draining = true;
                return true;
            }
        }

        public bool TryDequeue(out RenderCaptureResult? result)
        {
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    result = null;
                    return false;
                }
                result = _pending.Dequeue();
                _draining = _pending.Count > 0;
                return true;
            }
        }
    }

    /// <summary>
    /// Owns the single asynchronous consumer for a recording queue.
    ///
    /// The consumer's exit decision is serialized with StartRecording and
    /// TryEnqueue. This matters when a recording is restarted at the same
    /// time that the previous drain observes an empty queue: either the old
    /// consumer remains the owner, or the restart starts a new one, but there
    /// is no interval in which a completed task can hide a live queue.
    /// </summary>
    public sealed class CaptureRecordingConsumer
    {
        private readonly CaptureRecordingQueue _queue;
        private readonly Action<RenderCaptureResult> _writer;
        private readonly Action<Exception>? _reportError;
        private readonly object _workerGate = new object();
        private WorkerLease? _workerLease;
        private Task? _worker;
        private Exception? _lastError;
        private int _failureCount;

        public CaptureRecordingConsumer(CaptureRecordingQueue queue,
            Action<RenderCaptureResult> writer, Action<Exception>? reportError = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _reportError = reportError;
        }

        public bool IsConsumerRunning
        {
            get
            {
                lock (_workerGate)
                {
                    return _workerLease != null && _worker is { IsCompleted: false };
                }
            }
        }

        public int PendingCount => _queue.Count;
        public long DroppedCount => _queue.DroppedCount;
        public int FailureCount
        {
            get { lock (_workerGate) return _failureCount; }
        }

        public Exception? LastError
        {
            get { lock (_workerGate) return _lastError; }
        }

        public void StartRecording()
        {
            lock (_workerGate)
            {
                _queue.StartRecording();
                EnsureWorkerLocked();
            }
        }

        public bool TryEnqueue(RenderCaptureResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            lock (_workerGate)
            {
                bool accepted = _queue.TryEnqueue(result);
                // Check even when the queue is full. A producer can recover a
                // consumer that failed outside the encoder callback without
                // changing the queue's bounded/drop semantics.
                EnsureWorkerLocked();
                return accepted;
            }
        }

        public void StopRecording()
        {
            lock (_workerGate)
            {
                _queue.StopRecording();
                // A prior worker may have exited after an unexpected runtime
                // failure. Keep ownership of any pending results by ensuring
                // the stop path can also install their drain owner.
                EnsureWorkerLocked();
            }
        }

        /// <summary>
        /// Stops accepting frames and returns the task that owns the drain.
        /// The returned task completes only after all queued results have been
        /// handed to the writer, including when one write fails.
        /// </summary>
        public Task StopAndDrainAsync()
        {
            lock (_workerGate)
            {
                _queue.StopRecording();
                EnsureWorkerLocked();
                return _worker ?? Task.CompletedTask;
            }
        }

        private void EnsureWorkerLocked()
        {
            if (_workerLease != null && _worker is { IsCompleted: false })
            {
                return;
            }
            if (!_queue.IsRecording && _queue.Count == 0)
            {
                return;
            }

            WorkerLease lease = new WorkerLease();
            _workerLease = lease;
            _worker = Task.Run(() => ConsumeAsync(lease));
        }

        private async Task ConsumeAsync(WorkerLease lease)
        {
            try
            {
                while (true)
                {
                    if (_queue.TryDequeue(out RenderCaptureResult? result))
                    {
                        try
                        {
                            _writer(result!);
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(ex);
                        }
                        continue;
                    }

                    lock (_workerGate)
                    {
                        if (!ReferenceEquals(_workerLease, lease))
                        {
                            return;
                        }
                        // The producer and this exit decision use the same
                        // lock. A restart cannot arrive in the gap between
                        // observing empty and relinquishing ownership.
                        if (!_queue.IsRecording && _queue.Count == 0)
                        {
                            _workerLease = null;
                            _worker = null;
                            return;
                        }
                    }
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Queue/runtime failures must not become unobserved task
                // exceptions or strand the remaining owned results.
                RecordFailure(ex);
            }
            finally
            {
                lock (_workerGate)
                {
                    if (ReferenceEquals(_workerLease, lease))
                    {
                        _workerLease = null;
                        _worker = null;
                    }
                }
            }
        }

        private void RecordFailure(Exception error)
        {
            lock (_workerGate)
            {
                _lastError = error;
                _failureCount++;
            }
            try
            {
                if (_reportError != null)
                {
                    _reportError(error);
                }
                else
                {
                    Console.Error.WriteLine($"[capture] recording encoder failed: {error.Message}");
                }
            }
            catch
            {
                // Error reporting is diagnostic only and cannot be allowed to
                // terminate the consumer that still owns queued results.
            }
        }

        private sealed class WorkerLease
        {
        }
    }
}
