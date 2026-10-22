using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

/// <summary>Nonblocking producer, exclusive storage owner. Queue overflow aborts
/// this recording; completed chunks remain recoverable. No Scene crosses threads.</summary>
internal sealed class ReplayWritePump
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ReplayWritePump, byte> Active = new();
    private static int _activeCount;
    private static int _shutdownSignaled;
    static ReplayWritePump()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            LifecycleTiming.BeginShutdown("process exit");
            BeginProcessShutdown();
            var tasks = new System.Collections.Generic.List<Task>();
            foreach (var pump in Active.Keys) tasks.Add(pump.Completion);
            try
            {
                if (tasks.Count > 0
                    && !Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(2)))
                    DebugLog.Line("shutdown",
                        $"{tasks.Count} replay writer(s) left recoverable .part data at exit");
            }
            catch (AggregateException) { }
            LifecycleTiming.Shutdown("replay storage flush complete");
        };
    }

    /// <summary>
    /// Stop accepting new replay commands and let already-queued chunks drain.
    /// Called as soon as the user commits to quitting so most of the storage
    /// work finishes while the native window/audio teardown is still running.
    /// </summary>
    internal static void BeginProcessShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownSignaled, 1) != 0) return;
        foreach (var pump in Active.Keys) pump.Complete();
        LifecycleTiming.Shutdown("replay writers signaled");
    }
    internal const int MaximumCommands = 4096;
    internal const long MaximumBytes = 32L * 1024 * 1024;
    private enum Kind { Record, PostWorldRecord, Checkpoint, Frame, Event }
    private readonly record struct Command(Kind Kind, ReplayTimelineRecord Record, uint Frame, ReplayEvent Event)
    {
        internal long Bytes => Kind == Kind.PostWorldRecord ? Record.PayloadBytes + ReplayPostWorldRecord.HeaderBytes
            : Kind is Kind.Record or Kind.Checkpoint ? Record.PayloadBytes : 128;
        internal void Retain() { if (Kind is Kind.Record or Kind.PostWorldRecord or Kind.Checkpoint) Record.Retain(); }
        internal void Release() { if (Kind is Kind.Record or Kind.PostWorldRecord or Kind.Checkpoint) Record.Release(); }
    }
    private readonly Channel<Command> _queue;
    private readonly uint _origin;
    private readonly bool _postWorld;
    private readonly bool _timedShotCertificates;
    private readonly ReplayCustomWeaponContext? _customContext;
    private readonly object _phaseGate = new();
    private uint _queuedCompleted, _queuedAccepted;
    private bool _ordinaryQueued, _phaseClosing;
    private readonly long _maximumBytes;
    private long _bytes;
    private Exception? _error;
    internal Exception? Error => Volatile.Read(ref _error);
    internal long QueuedBytes => Interlocked.Read(ref _bytes);
    internal Task Completion { get; } = Task.CompletedTask;
    // The delay hook is used only by deterministic storage/backpressure checks.
    internal ReplayWritePump(string path, ReplayMetadata metadata, uint origin,
        int capacity = MaximumCommands, long maximumBytes = MaximumBytes,
        Action? beforeWrite = null, Action? finalized = null)
    {
        if (Volatile.Read(ref _shutdownSignaled) != 0)
            throw new IOException("The process is shutting down; no new replay writer can start.");
        _origin = origin; _maximumBytes = maximumBytes;
        _postWorld = metadata.FormatVersion == 8;
        _timedShotCertificates = metadata.TimedShotCertificates;
        _customContext = _postWorld ? metadata.CustomWeapons?.InspectContext() : null;
        if (_postWorld && (_customContext == null || metadata.OriginRecordingFrame != origin))
            throw new InvalidDataException("Post-world storage requires its exact custom baseline origin.");
        _queuedCompleted = _queuedAccepted = origin;
        _queue = Channel.CreateBounded<Command>(new BoundedChannelOptions(capacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        if (Interlocked.Increment(ref _activeCount) > 4)
        {
            Interlocked.Decrement(ref _activeCount);
            throw new IOException("Previous replay writers are still finishing.");
        }
        Active.TryAdd(this, 0);
        Completion = Task.Run(async () =>
        {
            ReplayWriterV3? writer = null;
            uint customEndFrame = 0;
            try
            {
                writer = new ReplayWriterV3(path, metadata);
                await foreach (var command in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    try
                    {
                        if (Error != null) continue;
                        beforeWrite?.Invoke();
                        if (Error != null) continue;
                        switch (command.Kind)
                        {
                            case Kind.Record:
                                ReplayTimelineArchive.Write(writer, command.Record, _origin);
                                if (metadata.FormatVersion is 7 or 8 && command.Record.RecordingFrame >= _origin)
                                    customEndFrame = Math.Max(customEndFrame, command.Record.RecordingFrame - _origin);
                                break;
                            case Kind.PostWorldRecord:
                                ReplayTimelineArchive.WritePostWorld(writer, command.Record, _origin, _customContext!);
                                customEndFrame = Math.Max(customEndFrame, command.Record.RecordingFrame - _origin);
                                break;
                            case Kind.Checkpoint:
                                writer.WriteCheckpoint(command.Record.RecordingFrame - _origin, command.Record.Payload);
                                if (metadata.FormatVersion is 7 or 8)
                                    customEndFrame = Math.Max(customEndFrame, command.Record.RecordingFrame - _origin);
                                break;
                            case Kind.Frame:
                                // Custom complete-world contracts have one final
                                // quiet marker. Retain the frontier while streaming;
                                // per-step markers belong to the native contract.
                                if (metadata.FormatVersion is 7 or 8) customEndFrame = Math.Max(customEndFrame, command.Frame);
                                else ReplayTimelineArchive.EndFrame(writer, command.Frame);
                                break;
                            case Kind.Event: writer.WriteEvent(command.Event); break;
                        }
                    }
                    finally { long cost = command.Bytes; command.Release(); Interlocked.Add(ref _bytes, -cost); }
                }
                if (Error == null)
                {
                    if (metadata.FormatVersion == 8 || metadata.FormatVersion == 7 && customEndFrame > 0)
                        ReplayTimelineArchive.EndFrame(writer, customEndFrame);
                    writer.Dispose(); writer = null; finalized?.Invoke();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { Abort(ex); }
            finally
            {
                try { writer?.Abort(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Abort(ex); }
                while (_queue.Reader.TryRead(out var remaining))
                { long cost = remaining.Bytes; remaining.Release(); Interlocked.Add(ref _bytes, -cost); }
                Active.TryRemove(this, out _); Interlocked.Decrement(ref _activeCount);
                if (Error != null) Console.WriteLine("[replay] Recording interrupted; recover completed chunks from its .part file. " + Error.Message);
            }
        });
    }
    private bool Enqueue(Command command)
    {
        using var perf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.Enqueue);
        if (Volatile.Read(ref _shutdownSignaled) != 0) return false;
        if (Error != null) return false;
        long cost = command.Bytes;
        if (Interlocked.Add(ref _bytes, cost) > _maximumBytes)
        {
            Interlocked.Add(ref _bytes, -cost);
            Abort(new IOException("Replay storage queue exceeded its byte budget.")); return false;
        }
        command.Retain();
        if (_queue.Writer.TryWrite(command)) return true;
        command.Release(); Interlocked.Add(ref _bytes, -cost);
        Abort(new IOException("Replay storage queue is full or closed.")); return false;
    }
    internal bool Record(ReplayTimelineRecord record) => _postWorld
        ? EnqueuePhaseRecord(record, postWorld: false) : Enqueue(new(Kind.Record, record, 0, default));
    internal bool RecordPostWorld(ReplayTimelineRecord record) => EnqueuePhaseRecord(record, postWorld: true);
    internal bool Record(ReplayCaptureRecord item)
    {
        if (item.Phase == ReplayCapturePhase.PostWorld) return RecordPostWorld(item.Record);
        if (item.Phase == ReplayCapturePhase.Ordinary) return Record(item.Record);
        Abort(new InvalidDataException("Unknown capture phase.")); return false;
    }
    private bool EnqueuePhaseRecord(ReplayTimelineRecord record, bool postWorld)
    {
        lock (_phaseGate)
        {
            if (_phaseClosing) return false;
            try
            {
                if (!_postWorld || (ulong)record.RecordingFrame != (ulong)_queuedCompleted + (postWorld ? 0UL : 1UL)
                    || postWorld && _ordinaryQueued)
                    throw new InvalidDataException("Replay fact does not match its queued completed-world frontier.");
                var packet = ReplayTimelineArchive.PhasePayload(record);
                ReplayPostWorldRecord.ValidatePayload(packet, _customContext);
                ReplayFormatV3.ValidateArchiveRecord(packet, _customContext, 8, _timedShotCertificates);
                if (!Enqueue(new(postWorld ? Kind.PostWorldRecord : Kind.Record, record, 0, default))) return false;
                if (!postWorld) { _queuedAccepted = record.RecordingFrame; _ordinaryQueued = true; }
                return true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException
                or InvalidOperationException or OverflowException)
            { Abort(error); return false; }
        }
    }
    internal bool Checkpoint(ReplayTimelineRecord record)
    {
        if (!_postWorld) return record.RecordingFrame <= _origin || Enqueue(new(Kind.Checkpoint, record, 0, default));
        lock (_phaseGate)
        {
            if (_phaseClosing) return false;
            if (record.Kind != ReplayFactKind.World || record.Payload.IsEmpty
                || record.RecordingFrame < _queuedCompleted || (ulong)record.RecordingFrame > (ulong)_queuedCompleted + 1)
            { Abort(new InvalidDataException("Replay checkpoint does not match the queued world frontier.")); return false; }
            if (record.RecordingFrame == _origin) return true; // Already retained by metadata.
            if (!Enqueue(new(Kind.Checkpoint, record, 0, default))) return false;
            _queuedAccepted = Math.Max(_queuedAccepted, record.RecordingFrame); return true;
        }
    }
    internal bool EndFrame(uint frame)
    {
        if (!_postWorld) return Enqueue(new(Kind.Frame, default, frame, default));
        lock (_phaseGate)
        {
            if (_phaseClosing) return false;
            ulong absolute = (ulong)_origin + frame;
            if (frame > ReplayFormatV3.MaxFrame || absolute > uint.MaxValue
                || absolute < _queuedCompleted || absolute > (ulong)_queuedCompleted + 1)
            { Abort(new InvalidDataException("Replay completed-world frames must be contiguous.")); return false; }
            if (!Enqueue(new(Kind.Frame, default, frame, default))) return false;
            if (absolute != _queuedCompleted) _ordinaryQueued = false;
            _queuedCompleted = (uint)absolute; return true;
        }
    }
    internal bool Event(ReplayEvent value) => Enqueue(new(Kind.Event, default, 0, value));
    internal void Complete()
    {
        if (!_postWorld) { _queue.Writer.TryComplete(); return; }
        lock (_phaseGate)
        {
            if (_phaseClosing) return;
            _phaseClosing = true;
            if (_queuedAccepted > _queuedCompleted)
            { Abort(new InvalidDataException("Replay stopped before its accepted next-frame work completed.")); return; }
            _queue.Writer.TryComplete();
        }
    }
    internal void Abort(Exception? error = null)
    {
        Interlocked.CompareExchange(ref _error, error ?? new IOException("Recording aborted."), null);
        _queue.Writer.TryComplete();
    }
}
