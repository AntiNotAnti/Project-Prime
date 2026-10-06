using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>A canonical replica consumes the same accepted facts as the timeline.
/// It never copies the live scene or borrows its input, RNG, entities or resources.
/// Network callbacks enqueue values; construction/stepping/disposal stay on the scene thread.</summary>
internal sealed class ReplayLiveWorld : IDisposable
{
    private const int MaximumPendingRecords = 8192;
    private const int MaximumPendingBytes = 4 * 1024 * 1024;
    private readonly ReplayRecorder _recorder;
    private readonly ReplayReplicaState _bootstrap = new();
    private readonly List<ReplayTimelineRecord> _pending = new(512);
    private PassiveReplayScene? _world;
    private PassiveReplayScene? _repair;
    private ReplayTimelineClip? _repairClip;
    private readonly List<ReplayTimelineRecord> _stepRecords = new(512);
    private int _repairSteps;
    private long _repairStarted;
    private bool _reset = true, _failed;
    private long _pendingBytes;
    private uint _lastCheckpoint;
    private bool _forceCheckpoint;
    internal string? LastError { get; private set; }
    internal long CaptureCount { get; private set; }
    internal double LastCaptureMilliseconds { get; private set; }
    internal double LastStepMilliseconds { get; private set; }
    internal PassiveReplayScene? World => _world;
    internal bool Preparing => _repair != null;

    internal ReplayLiveWorld(ReplayRecorder recorder)
    {
        _recorder = recorder; recorder.ProducesWorldCheckpoints = true;
        recorder.Accepted += Accept; recorder.Resetting += Reset;
    }
    private void ClearPending()
    {
        foreach (var record in _pending) record.Release();
        _pending.Clear(); _pendingBytes = 0;
    }
    private void Reset()
    {
        ClearPending(); _bootstrap.Reset();
        _reset = true; _failed = false; LastError = null; _lastCheckpoint = 0;
    }
    private bool _enabled = true;
    internal void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled; Reset(); _recorder.Timeline.Reset();
        if (enabled) _recorder.SeedWorld(Accept);
    }
    private void Accept(ReplayTimelineRecord record)
    {
        if (_failed || !_enabled) return;
        if (_pending.Count >= MaximumPendingRecords || _pendingBytes + record.PayloadBytes > MaximumPendingBytes)
        { Fail("Accepted replay facts exceeded the pending budget."); return; }
        record.Retain(); _pending.Add(record); _pendingBytes += record.PayloadBytes;
    }
    internal void Advance(uint frame, Vector2i size)
    {
        try
        {
            if (_reset || _failed) { ReleaseWorlds(); _reset = false; }
            if (_failed || !_enabled) return;
            if (_world == null)
            {
                foreach (var record in _pending)
                    if (!record.Payload.IsEmpty) _bootstrap.Accept(record.Payload, record.RecordingFrame);
                bool any = false;
                for (int slot = 0; slot < 8; slot++) any |= _bootstrap.TryGetPlayer(slot, out _);
                if (_bootstrap.Match is not { } match || !any) { ClearPending(); return; }
                ulong mapHash = NetSession.IsServer && ServerReplayRecorder.TryGetMapHash(
                    match.MatchId, match.AuthorityEpoch, match.RoomKey, out ulong preparedHash)
                    ? preparedHash : ReplayMapIdentity.Compute(match.RoomKey);
                if (mapHash == 0) throw new InvalidDataException("The capture room has no content identity.");
                _world = new PassiveReplayScene(_bootstrap.CaptureCheckpoint(), frame, mapHash, size);
                _world.Scene.ReplayPoses = ReplayPoseStream.Live(_world, _pending);
                ClearPending();
                BindMarkers(_world);
            }
            else if (_world.Session.CurrentFrame == frame && _repair == null) return;
            long started = Stopwatch.GetTimestamp();
            using (ReplayPerfTelemetry.Measure(ReplayPerfOperation.Step))
            {
                if (_repair == null && _world.Scene.ReplayPoses!.ObserveLive(_pending) is uint late)
                    BeginRepair(late, frame, size);
                if (_repair != null)
                {
                    AdvanceRepair(frame, size);
                    if (_repair != null)
                    { LastStepMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds; return; }
                }
                else
                {
                    _world.StepLive(frame, _pending);
                    ClearPending();
                }
            }
            LastStepMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (_recorder.Timeline.NeedsRestorePoint || _forceCheckpoint || frame - _lastCheckpoint >= 300)
            {
                started = Stopwatch.GetTimestamp();
                using var checkpoint = ReplayWorldCheckpoint.Capture(_world);
                if (!_recorder.AppendWorldCheckpoint(frame, _world.State.ServerTick, checkpoint.Payload))
                    throw new InvalidDataException("The timeline rejected its world checkpoint.");
                _lastCheckpoint = frame; _forceCheckpoint = false; CaptureCount++;
                LastCaptureMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            // A quiet simulation frame still extends the available clip, even
            // when no network snapshot or input arrived on that frame.
            _recorder.Timeline.AdvanceFrame(frame, _world.State.ServerTick);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Fail(ex.Message); ReleaseWorlds(); }
    }
    private void BindMarkers(PassiveReplayScene world)
        => world.Scene.ReplayShotPresented = (slot, weapon) => _recorder.Marker(
            world.Session.CurrentFrame, world.State.ServerTick,
            new(ReplayMarkerKind.WeaponFired, (byte)slot, byte.MaxValue, weapon));

    private void BeginRepair(uint source, uint frame, Vector2i size)
    {
        if (_repairStarted == 0) _repairStarted = Stopwatch.GetTimestamp();
        // A world captured at the source frame already omitted this first-seen
        // shot. Restore strictly before it, preserving the immutable carriers.
        if (!_recorder.Timeline.InvalidateWorldRestorePointsFrom(source))
            throw new InvalidDataException("Late replay fire has no retained pre-shot world; instant history is unavailable.");
        _recorder.Timeline.AdvanceFrame(frame, _world!.State.ServerTick);
        if (!_recorder.Timeline.TryGetRestorePoint(source - 1, out var baseline) || baseline == null
            || frame - baseline.RecordingFrame > 1024
            || !_recorder.Timeline.TryFreeze(baseline.RecordingFrame, frame, out var frozen) || frozen == null)
            throw new InvalidDataException("Late replay fire exceeds its bounded correction history.");
        try
        {
            _repair?.Dispose(); _repair = null;
            _repairClip?.Dispose(); _repairClip = frozen;
            _repair = new PassiveReplayScene(frozen, size);
            var correction = _repair;
            correction.Scene.ReplayShotPresented = (slot, weapon) => _recorder.Timeline.RecordRecoveredWeaponMarker(
                correction.Session.CurrentFrame, correction.State.ServerTick, (byte)slot, weapon);
            // Fill only absent source-frame telemetry; never announce historical
            // shots to live subscribers or duplicate an already recorded marker.
        }
        catch { frozen.Dispose(); _repairClip = null; throw; }
    }
    private void AdvanceRepair(uint frame, Vector2i size)
    {
        long start = Stopwatch.GetTimestamp();
        int steps = 0;
        while (_repair != null && steps < 24)
        {
            if (_repairSteps >= 1024 || Stopwatch.GetElapsedTime(_repairStarted).TotalSeconds > 5)
                throw new InvalidDataException("Replay correction exceeded its bounded owner work; instant history is unavailable.");
            if (_repair.Session.AtEnd)
            {
                _repair.Session.ContinueLive(_repair.MapHash);
                _repair.Scene.ReplayPoses!.ContinueLive();
                _repairClip?.Dispose(); _repairClip = null;
                ClearPendingThrough(_repair.Session.CurrentFrame);
            }
            if (_repair.Session.CurrentFrame >= frame)
            {
                var previous = _world;
                _world = _repair;
                _repair = null; _repairClip = null; _repairStarted = 0; _repairSteps = 0;
                previous?.Dispose();
                BindMarkers(_world);
                // Publish a fresh validated boundary after correction catches up.
                _forceCheckpoint = true;
                break;
            }
            if (_repair.Session.AtEnd == false && !_repair.Session.IsLive)
                _repair.Step();
            else
            {
                uint next = _repair.Session.CurrentFrame + 1;
                _stepRecords.Clear();
                foreach (var record in _pending)
                { if (record.RecordingFrame > next) break; _stepRecords.Add(record); }
                if (_repair.Scene.ReplayPoses!.ObserveLive(_stepRecords) is uint late)
                { _stepRecords.Clear(); BeginRepair(late, frame, size); continue; }
                _repair.StepLive(next, _stepRecords); _stepRecords.Clear(); ClearPendingThrough(next);
            }
            steps++; _repairSteps++;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 1) break;
        }
    }
    private void ClearPendingThrough(uint frame)
    {
        int count = 0;
        while (count < _pending.Count && _pending[count].RecordingFrame <= frame)
        { var record = _pending[count++]; _pendingBytes -= record.PayloadBytes; record.Release(); }
        if (count != 0) _pending.RemoveRange(0, count);
    }
    private void ReleaseWorlds()
    {
        try { try { _repair?.Dispose(); } finally { _repair = null; _repairClip?.Dispose(); _repairClip = null; } }
        finally { try { _world?.Dispose(); } finally { _world = null; } }
        _repairSteps = 0; _repairStarted = 0; _forceCheckpoint = false; _stepRecords.Clear();
    }
    private void Fail(string error)
    {
        _failed = true; LastError = error; ClearPending();
        _recorder.Timeline.Reset();
        Console.WriteLine("[replay] Live world capture unavailable: " + error);
    }
    public void Dispose()
    {
        _recorder.Accepted -= Accept; _recorder.Resetting -= Reset;
        try { ReleaseWorlds(); } finally { ClearPending(); }
    }
}
