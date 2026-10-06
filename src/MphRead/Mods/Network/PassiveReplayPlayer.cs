using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Playback, seek and checkpoint lifetime for one private presentation.
/// A failed restore never replaces the currently presented world.</summary>
internal sealed record ReplayPlayerOptions(bool EnableSeeking = true, bool EnableMemoryCheckpoints = true, bool EnableDurableCheckpoints = true,
    bool EnableAsyncPreparation = false)
{
    internal static readonly ReplayPlayerOptions Linear = new(false, false, false);
}

internal sealed class PassiveReplayPlayer : IDisposable
{
    internal const int MaximumStepsPerUpdate = 120;
    private const int MaximumCheckpoints = 128;
    private const long MaximumCheckpointBytes = 64L * 1024 * 1024;
    private readonly SortedDictionary<uint, ReplayWorldCheckpoint>? _checkpoints;
    private readonly ReplayPlayerOptions _options;
    private readonly Func<PassiveReplayScene> _open;
    private readonly string? _path;
    private readonly ReplayTimelineClip? _clip;
    private readonly bool _ownsClip;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ReplayPreparationJob? _preparation;
    private PassiveReplayScene? _candidate;
    private ReplayWorldCheckpoint? _preparingCheckpoint;
    private ReplayCheckpointIndex? _preparingDurable;
    private uint _preparingTarget;
    private bool _preparingResume;
    private long _preparingGeneration;
    private readonly uint _firstFrame;
    private readonly Stopwatch _seekTime = new();
    private bool _disposed;
    private readonly HashSet<long>? _rejectedDurable;
    internal string CheckpointSource { get; private set; } = "initial world";
    public PassiveReplayScene Current { get; private set; }
    public ReplayTransport Transport => Current.Session.Transport;
    internal event Action<Scene>? Stepped;
    internal event Action<Scene, PassiveReplayScene>? Replaced;
    internal long CheckpointBytes { get; private set; }
    internal int CheckpointCount => _checkpoints?.Count ?? 0;
    internal uint SeekRestoreFrame { get; private set; }
    internal int SeekSimulationSteps { get; private set; }
    internal double SeekMilliseconds { get; private set; }
    internal int RejectedCheckpoints { get; private set; }
    internal string? LastCheckpointError { get; private set; }
    internal bool Ready => _preparation == null && _candidate == null && !Transport.IsSeeking && !Current.Session.IsWarming;
    internal bool CanPresent => !Current.Session.IsWarming && (Ready || _preparation != null || _candidate != null);
    internal bool Preparing => _preparation != null || _candidate != null;

    public PassiveReplayPlayer(string path, Vector2i size, ReplayPlayerOptions? options = null)
    {
        _options = options ?? new();
        _path = path;
        if (_options.EnableMemoryCheckpoints) _checkpoints = new();
        if (_options.EnableDurableCheckpoints) _rejectedDurable = new();
        _open = () => new(path, size); Current = _open(); Transport.SeekingEnabled = _options.EnableSeeking;
        // Nested ranges retain original source clocks. A durable baseline before
        // their visible start can skip most of the hidden lead-in immediately.
        if (Current.Session.IsWarming && DurableBefore(0) is { } baseline)
            Rebuild(0, true, null, baseline);
    }
    public PassiveReplayPlayer(ReplayTimelineClip clip, Vector2i size, ReplayPlayerOptions? options = null)
    { _options = options ?? new();
        _clip = clip;
        if (_options.EnableMemoryCheckpoints) _checkpoints = new();
        if (_options.EnableDurableCheckpoints) _rejectedDurable = new(); _open = () => new(clip, size); _firstFrame = clip.StartRecordingFrame; Current = _open(); Transport.SeekingEnabled = _options.EnableSeeking; }

    internal PassiveReplayPlayer(PreparedReplaySource prepared, Vector2i size, ReplayPlayerOptions? options = null)
    {
        _options = options ?? new(); _path = prepared.Path;
        if (prepared.FrozenClip is { } clip) { _clip = PreparedReplaySource.Retain(clip); _ownsClip = true; _firstFrame = clip.StartRecordingFrame; }
        if (_options.EnableMemoryCheckpoints) _checkpoints = new();
        if (_options.EnableDurableCheckpoints) _rejectedDurable = new();
        _open = _path != null ? () => new(_path, size) : () => new(_clip!, size);
        try { Current = new(prepared, size, _ownerThreadId); Transport.SeekingEnabled = _options.EnableSeeking; }
        catch { if (_ownsClip) _clip?.Dispose(); throw; }
    }

    public void Seek(uint frame, bool resume = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PreparedReplaySource.RequireOwner(_ownerThreadId);
        if (!_options.EnableSeeking) throw new InvalidOperationException("Linear replay playback does not support seeking.");
        frame = Math.Clamp(frame, _firstFrame, Current.Session.LastFrame);
        _seekTime.Restart(); SeekSimulationSteps = 0; SeekRestoreFrame = Current.Session.CurrentFrame;
        Transport.Seek(frame, resume);
    }

    /// <summary>One host update. Seeking never exceeds 120 fixed steps, and leaves
    /// the target pending for the next update. No intermediate frame is presented.</summary>
    public int Update(int maximumSteps = MaximumStepsPerUpdate, double maximumMilliseconds = double.PositiveInfinity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PreparedReplaySource.RequireOwner(_ownerThreadId);
        maximumSteps = Math.Clamp(maximumSteps, 1, MaximumStepsPerUpdate);
        long updateStart = Stopwatch.GetTimestamp();
        bool BudgetAvailable() => Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds < maximumMilliseconds;
        if (Preparing)
        {
            if (_preparingGeneration != Transport.SeekGeneration) CancelPreparation();
            else return AdvancePreparation(maximumSteps, maximumMilliseconds);
        }
        uint? target = Transport.SeekTarget;
        bool resume = Transport.ResumeAfterSeek;
        bool rebuild = Transport.TakeRebuild(out uint rebuildTarget, out bool rebuildResume);
        if (rebuild) { target = rebuildTarget; resume = rebuildResume; }
        if (target.HasValue)
        {
            var checkpoint = _checkpoints?.LastOrDefault(p => p.Key <= target.Value) ?? default;
            var durable = DurableBefore(target.Value);
            if (durable is { } disk && checkpoint.Value != null && Current.Session.CheckpointVisibleFrame(disk) <= checkpoint.Key)
                durable = null;
            uint bestFrame = durable is { } chosen ? Current.Session.CheckpointVisibleFrame(chosen) : checkpoint.Key;
            if (rebuild || bestFrame > Current.Session.CurrentFrame + MaximumStepsPerUpdate)
            {
                if (_options.EnableAsyncPreparation)
                {
                    StartPreparation(target.Value, resume, durable.HasValue ? null : checkpoint.Value, durable);
                    return AdvancePreparation(maximumSteps, maximumMilliseconds);
                }
                Rebuild(target.Value, resume, durable.HasValue ? null : checkpoint.Value, durable);
            }
        }
        if (Current.Session.IsWarming)
        {
            int warmup = 0;
            while (Current.Session.IsWarming && warmup < maximumSteps && (warmup == 0 || BudgetAvailable()))
            {
                if (!Current.Step()) throw new InvalidDataException("Replay ended during its required lead-in.");
                if (!Current.Session.IsWarming) Stepped?.Invoke(Current.Scene);
                warmup++;
                if (Transport.IsSeeking) SeekSimulationSteps++;
            }
            return warmup;
        }
        int due = Math.Min(maximumSteps, Transport.FramesDue());
        bool seeking = Transport.IsSeeking;
        int steps = 0;
        for (; steps < due && (!seeking || steps == 0 || BudgetAvailable()); steps++)
        {
            if (!Current.Step()) break;
            Stepped?.Invoke(Current.Scene);
            uint frame = Current.Session.CurrentFrame;
            if (seeking) SeekSimulationSteps++;
            if (_checkpoints != null && double.IsPositiveInfinity(maximumMilliseconds) && frame % 300 == 0 && !_checkpoints.ContainsKey(frame))
            {
                try { Remember(ReplayWorldCheckpoint.Capture(Current)); }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
                { LastCheckpointError = ex.Message; }
            }
        }
        if (!Transport.IsSeeking && _seekTime.IsRunning)
        { _seekTime.Stop(); SeekMilliseconds = _seekTime.Elapsed.TotalMilliseconds; }
        return steps;
    }

    private void StartPreparation(uint target, bool resume, ReplayWorldCheckpoint? checkpoint, ReplayCheckpointIndex? durable,
        bool useLeadInCheckpoint = true)
    {
        _preparingTarget = target; _preparingResume = resume; _preparingGeneration = Transport.SeekGeneration;
        _preparingCheckpoint = checkpoint; _preparingDurable = durable;
        _preparation = _path != null ? ReplayPreparationJob.File(_path, durable, checkpoint?.Frame, useLeadInCheckpoint,
            Current.Session.Metadata?.WorldCheckpoint) : ReplayPreparationJob.Clip(_clip!);
    }
    private void CancelPreparation()
    {
        var preparation = _preparation; _preparation = null;
        var candidate = _candidate; _candidate = null;
        _preparingCheckpoint = null; _preparingDurable = null;
        try { preparation?.Dispose(); } finally { candidate?.Dispose(); }
    }
    private int AdvancePreparation(int maximumSteps, double maximumMilliseconds)
    {
        if (_preparation != null)
        {
            if (!_preparation.Completed) return 0;
            var completed = _preparation; _preparation = null;
            try
            {
                using var prepared = completed.TakeCompleted();
                if (prepared.CheckpointError is { } error && _preparingDurable is { } rejected)
                { RejectedCheckpoints++; LastCheckpointError = error; _rejectedDurable!.Add(rejected.Offset); _preparingDurable = null; }
                _candidate = new(prepared, Current.Scene.Size, _ownerThreadId, _preparingCheckpoint);
                CheckpointSource = _preparingCheckpoint != null ? "memory" : prepared.Durable.HasValue ? "file" : "initial world";
                _candidate.Session.Transport.CopyPreferences(Transport);
                _candidate.Session.Transport.ContinueSeek(_preparingTarget, _preparingResume);
                SeekRestoreFrame = _candidate.Session.CurrentFrame;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
            {
                LastCheckpointError = ex.Message;
                bool optional = _preparingDurable.HasValue || _preparingCheckpoint != null;
                if (_preparingDurable is { } disk) { RejectedCheckpoints++; _rejectedDurable!.Add(disk.Offset); }
                if (_preparingCheckpoint is { } memory)
                { RejectedCheckpoints++; CheckpointBytes -= memory.Payload.Capacity + 128; _checkpoints!.Remove(memory.Frame); memory.Dispose(); }
                CancelPreparation();
                if (optional) StartPreparation(_preparingTarget, _preparingResume, null, null, useLeadInCheckpoint: false);
                else Transport.ContinueSeek(Current.Session.CurrentFrame, resume: false);
                return 0;
            }
            finally { completed.Dispose(); }
        }
        if (_candidate == null) return 0;
        long start = Stopwatch.GetTimestamp();
        int steps = 0;
        try
        {
            while (steps < maximumSteps && (steps == 0 || Stopwatch.GetElapsedTime(start).TotalMilliseconds < maximumMilliseconds))
            {
                if (!_candidate.Session.IsWarming && !_candidate.Session.Transport.IsSeeking) break;
                if (!_candidate.Step()) throw new InvalidDataException("Replay candidate ended before its requested frame.");
                steps++; SeekSimulationSteps++;
            }
            if (_candidate.Session.IsWarming || _candidate.Session.Transport.IsSeeking) return steps;
            var previous = Current; Current = _candidate; _candidate = null;
            try { Replaced?.Invoke(previous.Scene, Current); Stepped?.Invoke(Current.Scene); }
            finally { previous.Dispose(); }
            _preparingCheckpoint = null; _preparingDurable = null;
            if (_seekTime.IsRunning) { _seekTime.Stop(); SeekMilliseconds = _seekTime.Elapsed.TotalMilliseconds; }
            return steps;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        { LastCheckpointError = ex.Message; CancelPreparation(); Transport.ContinueSeek(Current.Session.CurrentFrame, resume: false); return steps; }
    }
    private ReplayCheckpointIndex? DurableBefore(uint target)
    {
        if (_rejectedDurable == null) return null;
        foreach (var index in Current.Session.DurableCheckpoints.Reverse())
            if (Current.Session.CheckpointVisibleFrame(index) <= target && !_rejectedDurable.Contains(index.Offset)) return index;
        return null;
    }
    private void Rebuild(uint target, bool resume, ReplayWorldCheckpoint? checkpoint, ReplayCheckpointIndex? durable = null)
    {
        PassiveReplayScene? replacement = null;
        ReplayWorldCheckpoint? loaded = null;
        try
        {
            if (durable is { } disk)
            {
                try { checkpoint = loaded = Current.Session.LoadCheckpoint(disk); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                {
                    RejectedCheckpoints++; LastCheckpointError = ex.Message; _rejectedDurable!.Add(disk.Offset);
                    durable = null; checkpoint = null;
                }
            }
            replacement = _open();
            CheckpointSource = checkpoint == null ? "initial world" : durable.HasValue ? "file" : "memory";
            if (checkpoint != null)
            {
                try { checkpoint.Restore(replacement, durable?.Frame); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                {
                    RejectedCheckpoints++; LastCheckpointError = ex.Message;
                    if (durable is { } rejected) _rejectedDurable!.Add(rejected.Offset);
                    else { CheckpointBytes -= checkpoint.Payload.Capacity + 128; _checkpoints!.Remove(checkpoint.Frame); checkpoint.Dispose(); }
                    replacement.Dispose(); replacement = _open(); CheckpointSource = "initial world";
                }
            }
            replacement.Session.Transport.CopyPreferences(Transport);
            replacement.Session.Transport.ContinueSeek(target, resume);
            SeekRestoreFrame = replacement.Session.CurrentFrame;
            var previous = Current; Current = replacement; replacement = null;
            try { Replaced?.Invoke(previous.Scene, Current); }
            finally { previous.Dispose(); }
        }
        finally { replacement?.Dispose(); loaded?.Dispose(); }
    }
    private void Remember(ReplayWorldCheckpoint checkpoint)
    {
        if (_checkpoints == null) { checkpoint.Dispose(); return; }
        long cost = checkpoint.Payload.Capacity + 128;
        if (cost > MaximumCheckpointBytes) { checkpoint.Dispose(); return; }
        while (_checkpoints.Count >= MaximumCheckpoints || CheckpointBytes + cost > MaximumCheckpointBytes)
        {
            var first = _checkpoints.First(); _checkpoints.Remove(first.Key); CheckpointBytes -= first.Value.Payload.Capacity + 128; first.Value.Dispose();
        }
        _checkpoints.Add(checkpoint.Frame, checkpoint); CheckpointBytes += cost;
    }
    public void Dispose()
    {
        if (_disposed) return;
        PreparedReplaySource.RequireOwner(_ownerThreadId);
        _disposed = true;
        try { CancelPreparation(); }
        finally
        {
            try { Current.Dispose(); }
            finally
            {
                try { if (_checkpoints != null) foreach (var checkpoint in _checkpoints.Values) checkpoint.Dispose(); }
                finally { _checkpoints?.Clear(); CheckpointBytes = 0; if (_ownsClip) _clip?.Dispose(); }
            }
        }
    }
}
