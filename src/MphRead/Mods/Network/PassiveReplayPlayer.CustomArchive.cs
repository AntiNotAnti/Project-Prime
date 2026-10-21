using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons.Packages;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal sealed partial class PassiveReplayPlayer
{
    private readonly bool _customArchive;
    private readonly string? _customPath, _customStore;
    private readonly ReplayMetadata? _customBaseline;
    private readonly uint _customDuration;
    private readonly int _customThread;
    private Task<PreparedCustomReplayArchive>? _customWork;
    private CancellationTokenSource? _customCancellation;
    private uint? _customGoal;
    private uint _customWorkTarget;
    private bool _customResume, _customNeedsPreparation;
    private long _customSeekGeneration;
    internal bool IsPreparingCustomArchive => _customWork != null;

    internal static PassiveReplayPlayer OpenCustomArchive(PreparedCustomReplayArchive prepared, string storeRoot, Vector2i size)
        => new(prepared, storeRoot, size);

    private PassiveReplayPlayer(PreparedCustomReplayArchive prepared, string storeRoot, Vector2i size)
    {
        _customArchive = true; _customPath = prepared.Path; _customStore = Path.GetFullPath(storeRoot);
        _customThread = Environment.CurrentManagedThreadId;
        _options = new(EnableMemoryCheckpoints: false);
        _open = () => throw new InvalidOperationException("Custom archives require their prepared resource owner.");
        Current = PassiveReplayScene.OpenCustomArchiveInspection(prepared, size, reconstructToTarget: false);
        try
        {
            _customBaseline = Current.Session.Metadata!; _customDuration = Current.Session.LastFrame;
            Transport.Begin(); Transport.AfterFrame();
            SeekRestoreFrame = Current.Session.CurrentFrame;
            CheckpointSource = SeekRestoreFrame == 0 ? "initial world" : "file";
            uint target = checked(prepared.TargetFrame - _customBaseline.OriginRecordingFrame - _customBaseline.LeadInFrames);
            if (target > Current.Session.CurrentFrame || Current.Session.IsWarming)
            {
                _customGoal = target; _customResume = true;
                Transport.ContinueSeek(target, resume: true);
            }
        }
        catch { Current.Dispose(); throw; }
    }

    private int UpdateCustom(int maximumSteps, double maximumMilliseconds)
    {
        if (Environment.CurrentManagedThreadId != _customThread)
            throw new InvalidOperationException("Custom archive stepping and replacement require their Scene thread.");
        try { return _customClip != null ? UpdateCustomClip(maximumSteps, maximumMilliseconds)
            : UpdateCustomCore(maximumSteps, maximumMilliseconds); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            LastCheckpointError = error.Message; _customGoal = null; _customNeedsPreparation = false;
            RetireCustomPreparation(); Current.Session.FailVerification("Custom archive player stopped: " + error.Message);
            Transport.Fail(); throw;
        }
    }
    private int UpdateCustomCore(int maximumSteps, double maximumMilliseconds)
    {
        bool newSeek = _customSeekGeneration != Transport.SeekGeneration;
        _customSeekGeneration = Transport.SeekGeneration;
        bool rebuild = Transport.TakeRebuild(out uint rebuilt, out bool resume);
        uint? requested = rebuild ? rebuilt : Transport.SeekTarget ?? (newSeek ? Current.Session.CurrentFrame : null);
        if (requested is uint target)
        {
            _customResume = rebuild ? resume : Transport.ResumeAfterSeek;
            if (_customGoal != target || newSeek && _customWork == null && !_customNeedsPreparation)
            {
                _customGoal = target;
                // Every control seek changes the head-fact timeline generation.
                // Restore into a fresh Scene to rebind the exact retained history,
                // including a seek to the current frame, rather than cancelling it.
                _customNeedsPreparation = true;
                _customCancellation?.Cancel();
                _seekTime.Restart(); SeekSimulationSteps = 0; SeekRestoreFrame = Current.Session.CurrentFrame;
            }
        }

        if (_customWork != null)
        {
            if (!_customWork.IsCompleted) return 0;
            var work = _customWork; var cancellation = _customCancellation!;
            _customWork = null; _customCancellation = null;
            try
            {
                if (cancellation.IsCancellationRequested || _customGoal != _customWorkTarget)
                {
                    if (work.Status == TaskStatus.RanToCompletion) work.Result.Dispose();
                    else _ = work.Exception;
                }
                else
                {
                    using var prepared = work.GetAwaiter().GetResult();
                    if (!prepared.MatchesArchiveBaseline(_customBaseline!, _customDuration))
                        throw new InvalidDataException("The custom archive baseline changed during playback.");
                    ReplaceCustom(prepared, _customWorkTarget, _customResume);
                    _customNeedsPreparation = false;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { cancellation.Dispose(); }
        }
        if (_customNeedsPreparation && _customGoal is uint prepareTarget)
        {
            string path = _customPath!, store = _customStore!;
            uint absolute = checked(_customBaseline!.OriginRecordingFrame + _customBaseline.LeadInFrames + prepareTarget);
            _customWorkTarget = prepareTarget;
            var cancellation = _customCancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            _customWork = Task.Run(() => PreparedCustomReplayArchive.Prepare(path,
                new WeaponPackageStore(store), absolute, token), token);
            return 0;
        }

        long began = Stopwatch.GetTimestamp();
        int due = Math.Min(maximumSteps, Transport.FramesDue()), steps = 0;
        bool seeking = Transport.IsSeeking;
        while (steps < due && (!seeking || steps == 0 || Stopwatch.GetElapsedTime(began).TotalMilliseconds < maximumMilliseconds))
        {
            if (!Current.StepCustomArchiveInspection()) { Transport.AfterFrame(); break; }
            Transport.AfterFrame(); steps++;
            if (seeking) SeekSimulationSteps++;
            Stepped?.Invoke(Current.Scene);
            if (seeking && !Transport.IsSeeking) break;
        }
        if (!Transport.IsSeeking)
        {
            _customGoal = null;
            if (_seekTime.IsRunning) { _seekTime.Stop(); SeekMilliseconds = _seekTime.Elapsed.TotalMilliseconds; }
        }
        return steps;
    }

    private void ReplaceCustom(PreparedCustomReplayArchive prepared, uint target, bool resume)
    {
        PassiveReplayScene? replacement = PassiveReplayScene.OpenCustomArchiveInspection(prepared,
            Current.Scene.Size, reconstructToTarget: false);
        try
        {
            replacement.Session.Transport.Begin(); replacement.Session.Transport.AfterFrame();
            replacement.Session.Transport.CopyPreferences(Transport);
            replacement.Session.Transport.ContinueSeek(target, resume);
            _customSeekGeneration = replacement.Session.Transport.SeekGeneration;
            SeekRestoreFrame = replacement.Session.CurrentFrame; CheckpointSource = SeekRestoreFrame == 0 ? "initial world" : "file";
            var previous = Current; Current = replacement; replacement = null;
            try { Replaced?.Invoke(previous.Scene, Current); }
            finally { previous.Dispose(); }
        }
        finally { replacement?.Dispose(); }
    }

    private void RetireCustomPreparation()
    {
        _customCancellation?.Cancel(); _customCancellation?.Dispose(); _customCancellation = null;
        var work = _customWork; _customWork = null;
        if (work != null) _ = work.ContinueWith(static completed =>
        {
            if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
            else _ = completed.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
