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

/// <summary>One background owner writes the same indexed replay format as Client.
/// The simulation only submits immutable observer facts; overflow fails recording explicitly.</summary>
public sealed class ServerReplaySession
{
    private sealed record Captured(ObserverFrame Frame, byte[] Clock, uint Phase);
    private readonly Channel<Captured> _frames = Channel.CreateBounded<Captured>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly string _path;
    private ServerReplayStatus _status;
    private bool _hasTick;
    private uint _lastTick;
    public Guid ReplayId { get; } = Guid.NewGuid();
    public ServerReplayStatus Status => Volatile.Read(ref _status);
    public Task Completion { get; }
    public bool Ready => Status.State == "recording";

    public ServerReplaySession(string directory)
    {
        _path = Path.Combine(Path.GetFullPath(directory), ReplayId.ToString("D") + DemoFile.Extension);
        _status = new(ReplayId, "opening", null);
        Completion = Task.Run(WriteAsync);
    }

    internal void Capture(ObserverFrame frame, Scene scene)
    {
        if (Status.State is not ("opening" or "recording")) return;
        if (_hasTick && unchecked(frame.Tick - _lastTick) != 1)
        {
            Volatile.Write(ref _status, new(ReplayId, "failed", "Authoritative capture gap; the replay is incomplete."));
            _frames.Writer.TryComplete(); return;
        }
        _hasTick = true; _lastTick = frame.Tick;
        byte[] clock = new byte[33]; clock[0] = (byte)DemoRecordKind.Clock;
        BinaryPrimitives.WriteUInt64LittleEndian(clock.AsSpan(1), scene.FrameCount);
        BinaryPrimitives.WriteUInt64LittleEndian(clock.AsSpan(9), scene.LiveFrames);
        BinaryPrimitives.WriteSingleLittleEndian(clock.AsSpan(17), scene.ElapsedTime);
        BinaryPrimitives.WriteSingleLittleEndian(clock.AsSpan(21), scene.GlobalElapsedTime);
        BinaryPrimitives.WriteUInt32LittleEndian(clock.AsSpan(25), scene.Random.Rng1);
        BinaryPrimitives.WriteUInt32LittleEndian(clock.AsSpan(29), scene.Random.Rng2);
        if (!_frames.Writer.TryWrite(new(frame, clock, scene.Match.PhaseRevision)))
        {
            Volatile.Write(ref _status, new(ReplayId, "failed", "Replay queue capacity exceeded; the file is an incomplete prefix."));
            _frames.Writer.TryComplete();
        }
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
            using var writer = new DemoWriter(_path, indexed: true);
            var opening = Status;
            if (opening.State == "opening") Interlocked.CompareExchange(ref _status, new(ReplayId, "recording", null), opening);
            uint? origin = null; uint previousKeyframe = 0, previousRoster = uint.MaxValue;
            var indexer = new ReplayEventIndexer();
            var feedback = new ServerReplayFeedback();
            await foreach (var captured in _frames.Reader.ReadAllAsync())
            {
                var source = captured.Frame;
                if (!source.Complete) continue;
                origin ??= source.Tick;
                uint frame = unchecked(source.Tick - origin.Value);
                feedback.Bind(source, captured.Phase);
                bool keyframe = frame == 0 || frame - previousKeyframe >= 300;
                byte[][]? presentation = keyframe ? feedback.Checkpoint() : null;
                if (keyframe)
                {
                    var records = new List<byte[]> { Match(source), new byte[] { (byte)DemoRecordKind.Perspective, byte.MaxValue },
                        Record(DemoRecordKind.Roster, source.Roster!), Record(DemoRecordKind.Snapshot, source.Snapshot!) };
                    foreach (var batch in source.World!) records.Add(Record(DemoRecordKind.World, batch));
                    records.Add(captured.Clock); records.AddRange(presentation!);
                    writer.WriteKeyframe(frame, records); previousKeyframe = frame;
                }
                // Indexed checkpoints are additive: sequential readers skip their chunks.
                if (frame == 0)
                {
                    writer.WriteRecord(frame, Match(source));
                    writer.WriteRecord(frame, new byte[] { (byte)DemoRecordKind.Perspective, byte.MaxValue });
                }
                writer.WriteRecord(frame, captured.Clock);
                if (source.RosterRevision != previousRoster || frame == 0)
                { writer.WriteRecord(frame, Record(DemoRecordKind.Roster, source.Roster!)); previousRoster = source.RosterRevision; }
                if (source.FreshSnapshot || frame == 0) writer.WriteRecord(frame, Record(DemoRecordKind.Snapshot, source.Snapshot!));
                if (source.FreshWorld || frame == 0) foreach (var batch in source.World!) writer.WriteRecord(frame, Record(DemoRecordKind.World, batch));
                if (frame == 0) foreach (var record in presentation!) writer.WriteRecord(frame, record);
                ReplayMarker terminalWorld = indexer.ForTerminalWorld(writer.ProtocolVersion,
                    source.World!.Any(HasMatchEnd));
                if (terminalWorld != ReplayMarker.None)
                {
                    writer.WriteRecord(frame, Record(DemoRecordKind.World, source.World![0]), terminalWorld);
                }
                foreach (var item in source.Events)
                {
                    byte[] bytes = new byte[item.Payload.Length + 2]; bytes[0] = (byte)DemoRecordKind.Event;
                    item.Payload.AsSpan(0, 4).CopyTo(bytes.AsSpan(1)); bytes[5] = (byte)item.Type;
                    item.Payload.AsSpan(4).CopyTo(bytes.AsSpan(6));
                    writer.WriteRecord(frame, bytes, indexer.ForEvent(
                        new(source.MatchId, item.Type, item.Payload.AsMemory(4)), NetHeader.Version));
                }
                feedback.Apply(source);
            }
            writer.Dispose();
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
        byte[] bytes = new byte[1 + MatchTransitionPacket.Size]; bytes[0] = (byte)DemoRecordKind.Match;
        new MatchTransitionPacket(frame.MatchId, frame.Tick, frame.Rules).Write(bytes.AsSpan(1)); return bytes;
    }
    private static byte[] Record(DemoRecordKind kind, byte[] payload)
    { byte[] bytes = new byte[payload.Length + 1]; bytes[0] = (byte)kind; payload.CopyTo(bytes, 1); return bytes; }
}
