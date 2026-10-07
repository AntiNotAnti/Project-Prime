using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MphRead.Entities;
using OpenTK.Mathematics;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Network;

/// <summary>Foreground Studio adapter. Reader, simulation and seeking belong to
/// an isolated player; only this presentation adapter touches foreground UI.</summary>
public static class DemoPlayback
{
    private static ReplayPlaybackSession _prepared = new(new PassiveReplaySessionHost());
    private static readonly object _joinGate = new();
    private static ReplayPreparationJob? _joinJob;
    private static PreparedReplaySource? _pendingSource;
    private static PassiveReplayPlayer? _openingPlayer;
    private static long _joinGeneration;
    private static long _pendingGeneration, _openingGeneration;
    private static string? _joinError;
    private static ReplayOpenResult _joinResult;
    private static PassiveReplayPlayer? _player;
    private static Scene? _shell;
    private static Scene? _lab;
    private static ulong _audio;
    private static bool _failed;
    private static bool _presentationFailed;
    private static PreviewBoundsLease? _previewBounds;
    /// <summary>Registers a platform's replay viewport on its render owner.
    /// Disposing an older registration cannot retire a newer platform owner.</summary>
    internal static IDisposable RegisterPreviewBounds(Func<int, int, Vector4i?> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        var lease = new PreviewBoundsLease(bounds);
        Interlocked.Exchange(ref _previewBounds, lease);
        return lease;
    }
    private sealed class PreviewBoundsLease(Func<int, int, Vector4i?> bounds) : IDisposable
    {
        internal readonly int Owner = Environment.CurrentManagedThreadId;
        internal readonly Func<int, int, Vector4i?> Bounds = bounds;
        public void Dispose()
        {
            if (Owner != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Replay viewport registration belongs to its render thread.");
            Interlocked.CompareExchange(ref _previewBounds, null, this);
        }
    }
    private static Vector4i? PlatformPreviewBounds(int width, int height)
    {
        var lease = Volatile.Read(ref _previewBounds);
        if (lease == null || lease.Owner != Environment.CurrentManagedThreadId || width <= 0 || height <= 0) return null;
        try
        {
            var bounds = lease.Bounds(width, height);
            if (bounds is not { } area || area.X < 0 || area.Y < 0 || area.Z <= 0 || area.W <= 0
                || (long)area.X + area.Z > width || (long)area.Y + area.W > height) return null;
            return area;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return null; }
    }
    internal static ReplayPlaybackSession Session => _pendingSource?.Session ?? _openingPlayer?.Current.Session ?? _player?.Current.Session ?? _prepared;
    internal static Scene? ReplicaScene => _player?.Current.Scene;
    // A failed private replay remains alive long enough to accept restart controls,
    // but must not stay attached to foreground rendering. It may have faulted before
    // HUD/presentation setup completed, so drawing it can turn the original replay
    // error into a second NullReferenceException in PlayerHud.
    internal static Scene? PresentationScene => _lab ?? (_player?.CanPresent == true && !_failed ? _player.Current.Scene : null);
    internal static bool Owns(Scene scene) => ReferenceEquals(_player?.Current.Scene, scene);
    internal static Scene? Presentation(Scene shell) => ReferenceEquals(_shell, shell) ? PresentationScene : null;
    public static bool IsActive => Session.IsActive;
    public static string? CurrentPath => Session.CurrentPath;
    /// <summary>The disposable file currently being read by playback.</summary>
    public static string? PlaybackPath => CurrentPath;
    /// <summary>Stable user-authored replay identity (for example the .ppclip descriptor).</summary>
    public static string? LogicalPath => CurrentPath is { } path
        ? ReplayVirtualClips.LogicalPath(path) : null;
    public static IReadOnlyList<ReplayEvent> Events => Session.Events;
    internal static ReplayMetadata? Metadata => Session.Metadata;
    public static uint CurrentFrame => Session.CurrentFrame;
    public static uint LastFrame => Session.LastFrame;
    public static ReplayOpenResult LastResult => _joinError != null ? _joinResult : Session.LastResult;
    public static double CurrentSeconds => Session.CurrentSeconds;
    public static double DurationSeconds => Session.DurationSeconds;
    public static bool AtEnd => Session.AtEnd;
    public static string? LastError => _joinError ?? Session.LastError;
    public static string? LastWarning => Session.LastWarning;
    public static bool Join(string path, int timeoutMs = 8000)
    {
        _ = timeoutMs;
        using var job = ReplayPreparationJob.File(path);
        long generation;
        lock (_joinGate)
        {
            generation = ++_joinGeneration;
            _joinJob?.Dispose(); _joinJob = job;
            _pendingSource?.Dispose(); _pendingSource = null;
        }
        try
        {
            var prepared = job.WaitCompleted();
            lock (_joinGate)
            {
                if (generation != _joinGeneration) { prepared.Dispose(); return false; }
                _pendingSource = prepared; _pendingGeneration = generation; _joinJob = null; _joinError = null;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            lock (_joinGate)
            {
                if (generation == _joinGeneration)
                { _joinError = ex.Message; _joinResult = ex is ReplayPreparationException failed ? failed.Result : ReplayOpenResult.Corrupt; }
            }
            return false;
        }
        finally { lock (_joinGate) { if (ReferenceEquals(_joinJob, job)) _joinJob = null; } }
    }
    // Portable launch needs the exact package before it constructs its shell room.
    // GUI Join only prepares detached data and never publishes or disposes a Scene.
    internal static bool CommitPreparedMap()
    {
        try { lock (_joinGate) { _pendingSource?.CommitPreparedMap(Environment.CurrentManagedThreadId); } return true; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        { _joinError = ex.Message; _joinResult = ex is ReplayPreparationException failed ? failed.Result : ReplayOpenResult.Corrupt; return false; }
    }
    internal static int CheckpointCount => _player?.CheckpointCount ?? 0;
    internal static string SeekDiagnostics => _player == null ? "" :
        $"{_player.CheckpointSource} restore {_player.SeekRestoreFrame} · {_player.SeekSimulationSteps} steps · {_player.SeekMilliseconds:0.0} ms · {_player.RejectedCheckpoints} rejected";
    internal static void Update(Scene shell)
    {
        if (!IsActive) return;
        if (_failed && _pendingSource == null && _openingPlayer == null)
        {
            PollFailedControls(shell);
            return;
        }
        try { UpdateCore(shell); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            FailPlayback(ex);
        }
    }
    private static void PollFailedControls(Scene shell)
    {
        _shell ??= shell;
        if (_player?.Current.Scene is not Scene replay) return;
        replay.UseReplayInput(_shell);
        replay.PollReplayControls();

        // Play/restart from Error transitions the transport into Seeking. Let
        // the private player rebuild from frame zero instead of leaving the
        // failure latch permanently in front of an otherwise recoverable replay.
        if (_player.Transport.IsSeeking)
        {
            _failed = false;
            try { UpdateCore(shell); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                FailPlayback(ex);
            }
        }
    }

    private static void FailPlayback(Exception ex)
    {
        _failed = true;
        ReplayInput.CancelScrub();
        ReplayAudioOwner.Release(_audio); _audio = 0;
        Session.FailVerification("Replay playback stopped: " + ex.Message);
        MphRead.Mods.DebugLog.Exception("replay", ex);
        Session.Transport.AfterFrame();
    }

    private static void UpdateCore(Scene shell)
    {
        _shell ??= shell;
        PreparedReplaySource? source;
        long sourceGeneration;
        lock (_joinGate) { source = _pendingSource; sourceGeneration = _pendingGeneration; _pendingSource = null; }
        if (source != null)
        {
            try
            {
                using (source)
                {
                    lock (_joinGate)
                    {
                        if (sourceGeneration != _joinGeneration) return;
                        // Exact-map publication and join-generation changes share
                        // this gate; a superseded worker cannot install its package.
                        source.CommitPreparedMap(Environment.CurrentManagedThreadId);
                    }
                    var candidate = new PassiveReplayPlayer(source, _shell.Size, new(EnableAsyncPreparation: true));
                    bool superseded;
                    PassiveReplayPlayer? priorCandidate = null;
                    lock (_joinGate)
                    {
                        superseded = sourceGeneration != _joinGeneration;
                        if (!superseded)
                        { priorCandidate = _openingPlayer; _openingPlayer = candidate; _openingGeneration = sourceGeneration; }
                    }
                    if (superseded) { candidate.Dispose(); return; }
                    priorCandidate?.Dispose();
                    ReplayAudioOwner.Release(_audio); _audio = 0;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            { _joinError = ex.Message; _joinResult = ex is ReplayPreparationException failed ? failed.Result : ReplayOpenResult.Corrupt;
              if (_player != null) return; throw; }
        }
        if (_openingPlayer != null)
        {
            PassiveReplayPlayer? superseded = null;
            lock (_joinGate)
            {
                if (_openingGeneration != _joinGeneration)
                { superseded = _openingPlayer; _openingPlayer = null; }
            }
            if (superseded != null) { superseded.Dispose(); return; }
            try
            {
                if (!_openingPlayer.Ready)
                { _openingPlayer.Update(maximumSteps: 24, maximumMilliseconds: 1); if (!_openingPlayer.Ready) return; }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                var rejected = _openingPlayer; _openingPlayer = null;
                try { rejected.Dispose(); }
                finally { _joinError = ex.Message; _joinResult = ReplayOpenResult.Corrupt; }
                if (_player != null) return;
                throw;
            }
            PassiveReplayPlayer? previous = null;
            lock (_joinGate)
            {
                if (_openingGeneration != _joinGeneration)
                { superseded = _openingPlayer; _openingPlayer = null; }
                else { previous = _player; _player = _openingPlayer; _openingPlayer = null; }
            }
            if (superseded != null) { superseded.Dispose(); return; }
            ReplayAudioOwner.Release(_audio); _audio = 0;
            previous?.Dispose(); _prepared.Stop(); _failed = false; _presentationFailed = false;
            ReplayInput.CancelScrub(); ReplayCamera.ClearBookmarks(); ReplayCamera.Reset();
            ReplayHud.Reset(); ReplayStudio.ResetCache(); ReplayKillMessagePresenter.Reset();
            _player.Current.Session.FactRead += ReplayNetworkDiagnostics.OnPacketArray;
            _player.Stepped += ReplayVerification.AfterFrame;
            _player.Replaced += (previous, replacement) =>
            {
                ReplayAudioOwner.Release(_audio); _audio = 0;
                replacement.Scene.CopyReplayView(previous); replacement.Scene.UseReplayInput(_shell);
                replacement.Session.FactRead += ReplayNetworkDiagnostics.OnPacketArray;
                ReplayHud.Reset(); ReplayKillMessagePresenter.Reset();
                ReplayNetworkDiagnostics.Reset(); ReplayVerification.SeekTo(CurrentFrame);
            };
            ReplayVerification.Reset(); ReplayNetworkDiagnostics.Reset();
            if (_player.Current.Session.HasSimulatedFrame) ReplayVerification.AfterFrame(_player.Current.Scene);
        }
        if (_player == null) return;
        Scene before = _player.Current.Scene;
        before.UseReplayInput(_shell);
        before.PollReplayControls();
        bool silent = !_player.Ready || _player.Transport.IsPaused || _player.Transport.AtEnd;
        if (silent) { ReplayAudioOwner.Release(_audio); _audio = 0; }
        _player.Update();
        Scene current = _player.Current.Scene;
        if (_player.Ready && !_presentationFailed)
        {
            try
            {
                if (_audio == 0 && !silent) _audio = ReplayAudioOwner.Acquire(current, _shell);
                SpectatorMode.Start(watchSomeone: true);
                var main = current.Players.Main;
                if (!Headless.Active && main.LoadFlags.TestFlag(LoadFlags.Active) && !main.HudReady) main.SetUpHud();
                if (!Headless.Active && main.HudReady) ReplayKillMessagePresenter.Update(current);
                if (!Headless.Active && !silent) MphRead.Sound.Sfx.Update(1f / 60);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _presentationFailed = true;
                ReplayAudioOwner.Release(_audio); _audio = 0;
                Session.WarnVerification("Replay presentation degraded: " + ex.Message);
                MphRead.Mods.DebugLog.Exception("replay", ex);
            }
        }
    }
    internal static Scene? PreparePresentation(Scene shell)
    {
        if (!ReferenceEquals(_shell, shell) && !Owns(shell)) return null;
        Scene? scene = PresentationScene;
        if (scene == null) return null;
        scene.ReplayPreviewSize = _shell!.Size;
        scene.ReplayPreviewBounds = null;
#if MPHREAD_SHELL && MPHREAD_AVALONIA
        scene.ReplayPreviewBounds = Launcher.Gui.UiSurface.Current?.ReplayViewportBounds(_shell.Size.X, _shell.Size.Y);
#endif
#if MPHREAD_SHELL
#if MPHREAD_RMLUI_POC && !ANDROID
        scene.ReplayPreviewBounds = Launcher.Gui.Shell.NativeReplayViewportBounds(_shell.Size.X, _shell.Size.Y)
            ?? scene.ReplayPreviewBounds;
#endif
#endif
        if (Volatile.Read(ref _previewBounds) != null)
            scene.ReplayPreviewBounds = PlatformPreviewBounds(_shell.Size.X, _shell.Size.Y);
        var viewportSize = scene.ReplayPreviewBounds is { } bounds
            ? new Vector2i(bounds.Z, bounds.W) : _shell.Size;
        var size = ReplayVideoExporter.OutputSize ?? viewportSize;
        if (scene.Size != size) { scene.Size = size; scene.OnResize(); }
        double hostAlpha = Render.FrameTiming.Active ? Render.FrameTiming.PresentationAlpha : 1;
        // An opening candidate has its own clock. While it prepares, rendering
        // still belongs to the published player's unchanged world and cursor.
        var presentedSession = _player?.Current.Session ?? Session;
        scene.ReplayRenderAlpha = ReplayVideoExporter.Rendering ? ReplayVideoExporter.PresentationAlpha
            : Render.FrameTiming.Active ? presentedSession.Transport.PresentationAlpha(hostAlpha) : 1;
        scene.ReplayPresentationFrame = ReplayVideoExporter.Rendering
            ? ReplayVideoExporter.PresentationFrame
            : Render.FrameTiming.Active ? presentedSession.Transport.PresentationFrame(hostAlpha) : presentedSession.CurrentFrame;
        return ReferenceEquals(scene, shell) ? null : scene;
    }
    internal static void Release(Scene shell)
    {
        if (!ReferenceEquals(_shell, shell)) return;
        lock (_joinGate)
        {
            if (_pendingSource == null && _joinJob == null) { Stop(); return; }
            // Closing a previous shell during a successful join releases native
            // ownership here while retaining only the newly prepared source.
            var player = _player; _player = null; var opening = _openingPlayer; _openingPlayer = null; _shell = null;
            var audio = _audio; _audio = 0;
            try { ReplayAudioOwner.Release(audio); }
            finally { try { player?.Dispose(); } finally { opening?.Dispose(); } }
        }
    }
    public static void PumpFrame() => Session.PumpFrame();
    public static void Stop()
    {
        ReplayPreparationJob? job; PreparedReplaySource? pending;
        lock (_joinGate)
        {
            _joinGeneration++; job = _joinJob; _joinJob = null;
            pending = _pendingSource; _pendingSource = null;
        }
        var player = _player; _player = null; var opening = _openingPlayer; _openingPlayer = null;
        Scene? lab = _lab; _lab = null;
        var audio = _audio; _audio = 0;
        _shell = null; _failed = false; _presentationFailed = false; _joinError = null;
        try { ReplayInput.CancelScrub(); ReplayAudioOwner.Release(audio); ReplayKillMessagePresenter.Reset(); }
        finally
        {
            try { job?.Dispose(); }
            finally
            {
                try { pending?.Dispose(); }
                finally
                {
                    try { player?.Dispose(); }
                    finally
                    {
                        try { opening?.Dispose(); }
                        finally
                        {
                            try { if (lab != null) try { lab.DoCleanup(); } finally { lab.UnloadGl(); } }
                            finally { _prepared.Stop(); }
                        }
                    }
                }
            }
        }
    }
    public static bool TakeControl(int slot, out string? branchPath)
    {
        branchPath = null;
        if (_player?.Ready != true || NetSession.Active || (uint)slot >= 8
            || _player.Current.Scene.Players.Items[slot].Health == 0
            || !_player.Current.Scene.Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Spawned)) return false;
        try
        {
            branchPath = ReplayLab.WriteBranch(CurrentPath!, CurrentFrame, slot);
            _lab = _player.Current.DetachScene();
            ReplayInput.CancelScrub();
            ReplayAudioOwner.Release(_audio); _audio = 0;
            _player.Dispose(); _player = null;
            _lab.BeginReplayLab(slot);
            SpectatorMode.TakeReplayControl(slot);
            ReplayVideoExporter.Cancel(); ReplayCamera.Reset(); ReplayHud.Reset(); ReplayStudio.ResetCache();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Console.WriteLine("[replay] Cannot create practice branch: " + ex.Message); return false; }
    }
    internal static void FailVerification(string error) => Session.FailVerification(error);
    internal static void WarnVerification(string warning) => Session.WarnVerification(warning);
    internal static long PlaybackArrivalTicks(uint frame) => ReplayPlaybackSession.PlaybackArrivalTicks(frame);
}
