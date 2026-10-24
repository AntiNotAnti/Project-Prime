using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Playback, seek and checkpoint lifetime for one private presentation.
/// A failed restore never replaces the currently presented world.</summary>
internal sealed record ReplayPlayerOptions(bool EnableSeeking = true, bool EnableMemoryCheckpoints = true, bool EnableDurableCheckpoints = true)
{
    internal static readonly ReplayPlayerOptions Linear = new(false, false, false);
}

internal sealed partial class PassiveReplayPlayer : IDisposable
{
    internal const int MaximumStepsPerUpdate = 120;
    internal const double InteractiveBudgetMilliseconds = 3;
    private const int MaximumCheckpoints = 128;
    private const long MaximumCheckpointBytes = 64L * 1024 * 1024;
    private readonly SortedDictionary<uint, ReplayWorldCheckpoint>? _checkpoints;
    private readonly ReplayPlayerOptions _options;
    private readonly Func<PassiveReplayScene> _open;
    private readonly ReplayOwnerPresentationSource? _ownerPresentationSource;
    private readonly uint _firstFrame;
    private readonly Stopwatch _seekTime = new();
    private uint? _observedSeekTarget;
    private long _observedSeekGeneration;
    private readonly Dictionary<uint, long> _checkpointUse = new();
    private long _checkpointClock;
    private Task<byte[]>? _checkpointRead;
    private ReplayCheckpointIndex _checkpointReadIndex;
    private byte[]? _prefetchedCheckpoint;
    private double _checkpointReadMilliseconds;
    internal ReplayTimingWindow UpdateTimings { get; } = new();
    internal ReplayTimingWindow SeekTimings { get; } = new();
    internal double LastUpdateMilliseconds { get; private set; }
    internal double LastRebuildMilliseconds { get; private set; }
    internal double LastCheckpointReadMilliseconds { get; private set; }
    internal double LastCheckpointCaptureMilliseconds { get; private set; }
    internal long CoalescedSeeks { get; private set; }
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
    internal int RejectedDurableCheckpoints => _rejectedDurable?.Count ?? 0;
    internal string? LastCheckpointError { get; private set; }
    internal bool Ready => !Transport.IsSeeking && !Current.Session.IsWarming && _customWork == null
        && (!_customArchive || Current.Session.IsActive && Current.Session.LastResult == ReplayOpenResult.Success);

    public PassiveReplayPlayer(string path, Vector2i size, ReplayPlayerOptions? options = null)
    {
        _options = options ?? new();
        if (_options.EnableMemoryCheckpoints) _checkpoints = new();
        if (_options.EnableDurableCheckpoints) _rejectedDurable = new();
        _ownerPresentationSource = Headless.Active ? null : new(path);
        _open = () => new(path, size, _ownerPresentationSource);
        try { Current = _open(); } catch { _ownerPresentationSource?.Dispose(); throw; }
        Transport.SeekingEnabled = _options.EnableSeeking;
        // Nested ranges retain original source clocks. A durable baseline before
        // their visible start can skip most of the hidden lead-in immediately.
        if (Current.Session.IsWarming && DurableBefore(0) is { } baseline)
            Rebuild(0, true, null, baseline);
    }
    public PassiveReplayPlayer(ReplayTimelineClip clip, Vector2i size, ReplayPlayerOptions? options = null)
    { _options = options ?? new();
        if (_options.EnableMemoryCheckpoints) _checkpoints = new();
        if (_options.EnableDurableCheckpoints) _rejectedDurable = new(); _open = () => new(clip, size); _firstFrame = clip.StartRecordingFrame; Current = _open(); Transport.SeekingEnabled = _options.EnableSeeking; }

    public void Seek(uint frame, bool resume = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_options.EnableSeeking) throw new InvalidOperationException("Linear replay playback does not support seeking.");
        frame = Math.Clamp(frame, _firstFrame, Current.Session.LastFrame);
        if (Transport.SeekTarget == frame && Transport.ResumeAfterSeek == resume) return;
        Transport.Seek(frame, resume);
    }

    /// <summary>Yields between fixed steps after the interactive time budget.
    /// A single simulation step or validated scene restore is indivisible;
    /// timings expose those overruns. Offline tools can opt into an infinite budget.</summary>
    public int Update(int maximumSteps = MaximumStepsPerUpdate, double maximumMilliseconds = InteractiveBudgetMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (double.IsNaN(maximumMilliseconds) || maximumMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));
        long began = Stopwatch.GetTimestamp();
        uint? target = Transport.SeekTarget;
        if (target.HasValue && (_observedSeekTarget != target || _observedSeekGeneration != Transport.SeekGeneration))
        {
            if (_seekTime.IsRunning) CoalescedSeeks++;
            _seekTime.Restart(); SeekSimulationSteps = 0; SeekRestoreFrame = Current.Session.CurrentFrame;
            _observedSeekTarget = target; _observedSeekGeneration = Transport.SeekGeneration;
        }
        LastRebuildMilliseconds = LastCheckpointReadMilliseconds = LastCheckpointCaptureMilliseconds = 0;
        try { return UpdateCore(maximumSteps, maximumMilliseconds); }
        finally
        {
            LastUpdateMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
            UpdateTimings.Add(LastUpdateMilliseconds);
            if (!Transport.IsSeeking && !Current.Session.IsWarming && _seekTime.IsRunning)
            {
                _seekTime.Stop(); SeekMilliseconds = _seekTime.Elapsed.TotalMilliseconds;
                SeekTimings.Add(SeekMilliseconds); _observedSeekTarget = null;
            }
        }
    }

    private int UpdateCore(int maximumSteps, double maximumMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        maximumSteps = Math.Clamp(maximumSteps, 1, MaximumStepsPerUpdate);
        if (_customArchive) return UpdateCustom(maximumSteps, maximumMilliseconds);
        long updateStart = Stopwatch.GetTimestamp();
        bool BudgetAvailable() => Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds < maximumMilliseconds;
        uint? target = Transport.SeekTarget;
        bool resume = Transport.ResumeAfterSeek;
        // Leave rebuild requests untouched until their immutable disk read is
        // ready. New requests replace the target; at most one worker is admitted.
        if (target is uint requested && DurableBefore(requested) is { } candidate)
        {
            var memory = _checkpoints?.LastOrDefault(p => p.Key <= requested) ?? default;
            uint diskFrame = Current.Session.CheckpointVisibleFrame(candidate);
            bool useful = memory.Value == null || memory.Key < diskFrame;
            if (useful && (Transport.RequiresRebuild || diskFrame > Current.Session.CurrentFrame + MaximumStepsPerUpdate))
            {
                if (!PrepareCheckpoint(candidate)) return 0;
            }
        }
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
                Rebuild(target.Value, resume, durable.HasValue ? null : checkpoint.Value, durable);
            if (checkpoint.Value != null && durable == null && _checkpoints!.ContainsKey(checkpoint.Key))
                _checkpointUse[checkpoint.Key] = ++_checkpointClock;
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
            if (_checkpoints != null && !seeking && BudgetAvailable() && frame % 300 == 0 && !_checkpoints.ContainsKey(frame))
            {
                long captureStart = Stopwatch.GetTimestamp();
                try { Remember(ReplayWorldCheckpoint.Capture(Current)); }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
                { LastCheckpointError = ex.Message; }
                finally { LastCheckpointCaptureMilliseconds = Stopwatch.GetElapsedTime(captureStart).TotalMilliseconds; }
            }
        }
        return steps;
    }
    private ReplayCheckpointIndex? DurableBefore(uint target)
    {
        if (_rejectedDurable == null) return null;
        foreach (var index in Current.Session.DurableCheckpoints.Reverse())
            if (Current.Session.CheckpointVisibleFrame(index) <= target && !_rejectedDurable.Contains(index.Offset)) return index;
        return null;
    }
    private bool PrepareCheckpoint(ReplayCheckpointIndex index)
    {
        if (_checkpointRead != null && _checkpointReadIndex != index)
        {
            if (!_checkpointRead.IsCompleted) return false;
            _ = _checkpointRead.Exception; _checkpointRead = null; _prefetchedCheckpoint = null;
        }
        if (_checkpointRead == null)
        {
            _checkpointReadIndex = index;
            Func<byte[]> read = Current.Session.PrepareCheckpointRead(index);
            _checkpointRead = Task.Run(() =>
            {
                long began = Stopwatch.GetTimestamp();
                try { return read(); }
                finally { _checkpointReadMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds; }
            });
            return false;
        }
        if (!_checkpointRead.IsCompleted) return false;
        try { _prefetchedCheckpoint = _checkpointRead.GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or ArgumentException)
        {
            RejectedCheckpoints++; LastCheckpointError = ex.Message; _rejectedDurable!.Add(index.Offset);
            _checkpointRead = null; _prefetchedCheckpoint = null;
            return false; // Earlier durable fallbacks must be prefetched on the next update too.
        }
        return true;
    }
    private void Rebuild(uint target, bool resume, ReplayWorldCheckpoint? checkpoint, ReplayCheckpointIndex? durable = null)
    {
        long rebuildStart = Stopwatch.GetTimestamp();
        PassiveReplayScene? replacement = null;
        ReplayWorldCheckpoint? loaded = null;
        string? fallbackWarning = null;
        try
        {
            if (durable is { } disk)
            {
                long readStart = Stopwatch.GetTimestamp();
                try { checkpoint = loaded = Current.Session.LoadCheckpoint(disk, requireIndexedFirePresentation: Current.Scene.ReplayPoses?.IndexedFirePresentation != false,
                    prefetched: _checkpointReadIndex == disk ? _prefetchedCheckpoint : null); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                {
                    RejectedCheckpoints++; LastCheckpointError = ex.Message; _rejectedDurable!.Add(disk.Offset);
                    fallbackWarning = "Durable checkpoint fallback: " + ex.Message;
                    durable = null; checkpoint = null;
                }
                finally
                {
                    LastCheckpointReadMilliseconds = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds
                        + (_prefetchedCheckpoint != null ? _checkpointReadMilliseconds : 0);
                    _checkpointRead = null; _prefetchedCheckpoint = null;
                }
            }
            replacement = _open();
            if (fallbackWarning != null) replacement.Session.WarnVerification(fallbackWarning);
            CheckpointSource = checkpoint == null ? "initial world" : durable.HasValue ? "file" : "memory";
            if (checkpoint != null)
            {
                try { checkpoint.Restore(replacement, durable?.Frame); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                {
                    RejectedCheckpoints++; LastCheckpointError = ex.Message;
                    if (durable is { } rejected) _rejectedDurable!.Add(rejected.Offset);
                    else { CheckpointBytes -= checkpoint.Bytes.Length + 128; _checkpoints!.Remove(checkpoint.Frame); _checkpointUse.Remove(checkpoint.Frame); checkpoint.Dispose(); }
                    replacement.Dispose(); replacement = _open(); CheckpointSource = "initial world";
                }
            }
            replacement.Session.Transport.CopyPreferences(Transport);
            replacement.Session.Transport.ContinueSeek(target, resume);
            _observedSeekGeneration = replacement.Session.Transport.SeekGeneration;
            SeekRestoreFrame = replacement.Session.CurrentFrame;
            var previous = Current; Current = replacement; replacement = null;
            try { Replaced?.Invoke(previous.Scene, Current); }
            finally { previous.Dispose(); }
        }
        finally { replacement?.Dispose(); loaded?.Dispose(); LastRebuildMilliseconds = Stopwatch.GetElapsedTime(rebuildStart).TotalMilliseconds; }
    }
    private void Remember(ReplayWorldCheckpoint checkpoint)
    {
        if (_checkpoints == null) { checkpoint.Dispose(); return; }
        long cost = checkpoint.Bytes.Length + 128;
        if (cost > MaximumCheckpointBytes) { checkpoint.Dispose(); return; }
        while (_checkpoints.Count >= MaximumCheckpoints || CheckpointBytes + cost > MaximumCheckpointBytes)
        {
            uint oldest = _checkpointUse.MinBy(pair => pair.Value).Key;
            var evicted = _checkpoints[oldest]; _checkpoints.Remove(oldest); _checkpointUse.Remove(oldest);
            CheckpointBytes -= evicted.Bytes.Length + 128; evicted.Dispose();
        }
        _checkpoints.Add(checkpoint.Frame, checkpoint); _checkpointUse[checkpoint.Frame] = ++_checkpointClock; CheckpointBytes += cost;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Current.Dispose(); }
        finally
        {
            _ownerPresentationSource?.Dispose();
            RetireCustomPreparation();
            if (_checkpoints != null) foreach (var checkpoint in _checkpoints.Values) checkpoint.Dispose();
            _checkpoints?.Clear(); _checkpointUse.Clear(); CheckpointBytes = 0;
            if (_checkpointRead is { } read) _ = read.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
            _checkpointRead = null; _prefetchedCheckpoint = null;
        }
    }
}
