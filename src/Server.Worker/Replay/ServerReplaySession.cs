using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Replay;

public sealed record ServerReplayStatus(Guid ReplayId, string State, string? Error);

/// <summary>
/// Immutable, read-only replay writer measurements. Queue counters are
/// published from atomic fields so the heartbeat reader never touches the
/// writer's channel or mutable replay state.
/// </summary>
public readonly record struct ServerReplayDiagnosticsSnapshot(
    long QueueDepth, long QueueHighWater, bool QueueOverflowed,
    long CapturedFrames, long WrittenFrames);

internal readonly record struct ReplayClock(
    ulong FrameCount,
    ulong LiveFrames,
    float ElapsedTime,
    float GlobalElapsedTime,
    uint Rng1,
    uint Rng2)
{
    internal const int EncodedSize = 33;

    internal void Write(Span<byte> destination)
    {
        if (destination.Length < EncodedSize)
            throw new ArgumentException("Replay clock destination is too small.", nameof(destination));
        destination[0] = (byte)ReplayRecordKind.Clock;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[1..], FrameCount);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[9..], LiveFrames);
        BinaryPrimitives.WriteSingleLittleEndian(destination[17..], ElapsedTime);
        BinaryPrimitives.WriteSingleLittleEndian(destination[21..], GlobalElapsedTime);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[25..], Rng1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[29..], Rng2);
    }

    internal byte[] ToArray()
    {
        byte[] bytes = new byte[EncodedSize];
        Write(bytes);
        return bytes;
    }
}

internal readonly record struct ReplayCapture(ObserverFrame Frame, ReplayClock Clock, uint Phase);

/// <summary>One background owner writes the same indexed replay format as Client.
/// The simulation only submits immutable observer facts; overflow fails recording explicitly.</summary>
public sealed class ServerReplaySession
{
    private readonly Channel<ReplayCapture> _frames = Channel.CreateBounded<ReplayCapture>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly string _path;
    private readonly ReplayMapIdentity? _mapIdentity;
    private ServerReplayStatus _status;
    private bool _hasTick;
    private uint _lastTick;
    private long _queueDepth;
    private long _queueHighWater;
    private long _capturedFrames;
    private long _writtenFrames;
    private int _queueOverflowed;
    public Guid ReplayId { get; } = Guid.NewGuid();
    public ServerReplayStatus Status => Volatile.Read(ref _status);
    public Task Completion { get; }
    public bool Ready => Status.State == "recording";
    public ServerReplayDiagnosticsSnapshot Diagnostics => new(
        Math.Max(0, Interlocked.Read(ref _queueDepth)),
        Math.Max(0, Interlocked.Read(ref _queueHighWater)),
        Volatile.Read(ref _queueOverflowed) != 0,
        Math.Max(0, Interlocked.Read(ref _capturedFrames)),
        Math.Max(0, Interlocked.Read(ref _writtenFrames)));

    public ServerReplaySession(string directory,
        ReplayMapIdentity? mapIdentity = null)
    {
        _path = Path.Combine(Path.GetFullPath(directory), ReplayId.ToString("D") + ReplayFile.Extension);
        _mapIdentity = mapIdentity;
        _status = new(ReplayId, "opening", null);
        Completion = Task.Run(WriteAsync);
    }

    internal void Capture(ObserverFrame frame, Scene scene)
    {
        if (Status.State is not ("opening" or "recording")) return;
        if (frame.CaptureOverflowed)
        {
            Volatile.Write(ref _status, new(ReplayId, "failed",
                "Authoritative presentation capture overflowed; the replay is incomplete."));
            _frames.Writer.TryComplete();
            return;
        }
        if (_hasTick && unchecked(frame.Tick - _lastTick) != 1)
        {
            Volatile.Write(ref _status, new(ReplayId, "failed", "Authoritative capture gap; the replay is incomplete."));
            _frames.Writer.TryComplete(); return;
        }
        _hasTick = true; _lastTick = frame.Tick;
        ReplayClock clock = new(scene.FrameCount, scene.LiveFrames, scene.ElapsedTime,
            scene.GlobalElapsedTime, scene.Random.Rng1, scene.Random.Rng2);
        long depth = Interlocked.Increment(ref _queueDepth);
        if (!_frames.Writer.TryWrite(new(frame, clock, scene.Match.PhaseRevision)))
        {
            Interlocked.Decrement(ref _queueDepth);
            Volatile.Write(ref _queueOverflowed, 1);
            Volatile.Write(ref _status, new(ReplayId, "failed", "Replay queue capacity exceeded; the file is an incomplete prefix."));
            _frames.Writer.TryComplete();
            return;
        }
        ObserveQueueHighWater(depth);
        Interlocked.Increment(ref _capturedFrames);
    }

    public void Complete(uint? expectedFinalTick = null)
    {
        if (expectedFinalTick.HasValue && (!_hasTick || _lastTick != expectedFinalTick.Value))
            Volatile.Write(ref _status, new(ReplayId, "failed", "Final authoritative capture is missing; the replay is incomplete."));
        var status = Status;
        if (status.State is "opening" or "recording") Interlocked.CompareExchange(ref _status, new(ReplayId, "draining", null), status);
        _frames.Writer.TryComplete();
    }

    private async Task WriteAsync()
    {
        try
        {
            using var writer = new ReplayWriter(_path, indexed: true);
            var opening = Status;
            if (opening.State == "opening") Interlocked.CompareExchange(ref _status, new(ReplayId, "recording", null), opening);
            uint? origin = null; uint previousKeyframe = 0, previousRoster = uint.MaxValue;
            var indexer = new ReplayEventIndexer();
            var feedback = new ServerReplayFeedback();
            byte[] clockBytes = new byte[ReplayClock.EncodedSize];
            await foreach (var captured in _frames.Reader.ReadAllAsync())
            {
                Interlocked.Decrement(ref _queueDepth);
                Interlocked.Increment(ref _writtenFrames);
                var source = captured.Frame;
                if (!source.Complete) continue;
                origin ??= source.Tick;
                uint frame = unchecked(source.Tick - origin.Value);
                feedback.Bind(source, captured.Phase);
                bool keyframe = frame == 0 || frame - previousKeyframe >= 300;
                byte[][]? presentation = keyframe ? feedback.Checkpoint() : null;
                byte[] mapIdentity = MapIdentity(source);
                if (keyframe)
                {
                    var records = new List<byte[]> { Match(source), mapIdentity, new byte[] { (byte)ReplayRecordKind.Perspective, byte.MaxValue },
                        Record(ReplayRecordKind.Roster, source.Roster!), Record(ReplayRecordKind.Snapshot, source.Snapshot!) };
                    foreach (var batch in source.World!) records.Add(Record(ReplayRecordKind.World, batch));
                    records.Add(captured.Clock.ToArray()); records.AddRange(presentation!);
                    writer.WriteKeyframe(frame, records); previousKeyframe = frame;
                }
                // Indexed checkpoints are additive: sequential readers skip their chunks.
                if (frame == 0)
                {
                    writer.WriteRecord(frame, Match(source));
                    writer.WriteRecord(frame, mapIdentity);
                    writer.WriteRecord(frame, new byte[] { (byte)ReplayRecordKind.Perspective, byte.MaxValue });
                }
                captured.Clock.Write(clockBytes);
                writer.WriteRecord(frame, clockBytes);
                if (source.RosterRevision != previousRoster || frame == 0)
                { writer.WriteRecord(frame, Record(ReplayRecordKind.Roster, source.Roster!)); previousRoster = source.RosterRevision; }
                if (source.FreshSnapshot || frame == 0) writer.WriteRecord(frame, Record(ReplayRecordKind.Snapshot, source.Snapshot!));
                if (source.FreshWorld || frame == 0) foreach (var batch in source.World!) writer.WriteRecord(frame, Record(ReplayRecordKind.World, batch));
                if (frame == 0) foreach (var record in presentation!) writer.WriteRecord(frame, record);
                ReplayMarker terminalWorld = indexer.ForTerminalWorld(writer.ProtocolVersion,
                    source.World!.Any(HasMatchEnd));
                if (terminalWorld != ReplayMarker.None)
                {
                    writer.WriteRecord(frame, Record(ReplayRecordKind.World, source.World![0]), terminalWorld);
                }
                foreach (var item in source.Events)
                {
                    byte[] bytes = new byte[item.Payload.Length + 2]; bytes[0] = (byte)ReplayRecordKind.Event;
                    item.Payload.AsSpan(0, 4).CopyTo(bytes.AsSpan(1)); bytes[5] = (byte)item.Type;
                    item.Payload.AsSpan(4).CopyTo(bytes.AsSpan(6));
                    writer.WriteRecord(frame, bytes, indexer.ForEvent(
                        new(source.MatchId, item.Type, item.Payload.AsMemory(4)), NetHeader.Version));
                }
                feedback.Apply(source);
            }
            if (Status.State != "failed") Volatile.Write(ref _status,
                new(ReplayId, origin.HasValue ? "complete" : "failed", origin.HasValue ? null : "No complete authoritative baseline was captured."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Volatile.Write(ref _status, new(ReplayId, "failed", "Replay storage failed: " + e.GetType().Name)); }
    }

    private static bool HasMatchEnd(byte[] batch)
    {
        for (int offset = WorldPacket.HeaderSize; offset + WorldRecord.Size <= batch.Length; offset += WorldRecord.Size)
            if (WorldRecord.TryRead(batch.AsSpan(offset, WorldRecord.Size), out var state)
                && state.Kind == WorldRecordKind.Match && state.E != 0) return true;
        return false;
    }

    private static byte[] Match(ObserverFrame frame)
    {
        byte[] bytes = new byte[1 + MatchTransitionPacket.Size]; bytes[0] = (byte)ReplayRecordKind.Match;
        new MatchTransitionPacket(frame.MatchId, frame.Tick, frame.Rules).Write(bytes.AsSpan(1)); return bytes;
    }
    private byte[] MapIdentity(ObserverFrame frame)
    {
        ReplayMapIdentity identity = _mapIdentity
            ?? new ReplayMapIdentity(frame.Rules.RoomKey, null, null);
        if (!identity.RoomKey.Equals(frame.Rules.RoomKey,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Replay map identity does not match the captured room.");
        return ReplayMapIdentityCodec.WriteRecord(identity);
    }
    private static byte[] Record(ReplayRecordKind kind, byte[] payload)
    { byte[] bytes = new byte[payload.Length + 1]; bytes[0] = (byte)kind; payload.CopyTo(bytes, 1); return bytes; }

    private void ObserveQueueHighWater(long depth)
    {
        if (depth <= Volatile.Read(ref _queueHighWater)) return;
        Interlocked.Exchange(ref _queueHighWater, depth);
    }
}
