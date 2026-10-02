using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Entities;
using OpenTK.Mathematics;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Network;

/// <summary>Foreground Studio adapter. Reader, simulation and seeking belong to
/// an isolated player; only this presentation adapter touches foreground UI.</summary>
public static class DemoPlayback
{
    private static ReplayPlaybackSession _prepared = new(new PassiveReplaySessionHost());
    private static PassiveReplayPlayer? _player;
    private static Scene? _shell;
    private static Scene? _lab;
    private static ulong _audio;
    private static bool _failed;
    private static bool _presentationFailed;
    internal static ReplayPlaybackSession Session => _player?.Current.Session ?? _prepared;
    internal static Scene? ReplicaScene => _player?.Current.Scene;
    // A failed private replay remains alive long enough to accept restart controls,
    // but must not stay attached to foreground rendering. It may have faulted before
    // HUD/presentation setup completed, so drawing it can turn the original replay
    // error into a second NullReferenceException in PlayerHud.
    internal static Scene? PresentationScene => _lab ?? (_player?.Ready == true && !_failed ? _player.Current.Scene : null);
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
    public static ReplayOpenResult LastResult => Session.LastResult;
    public static double CurrentSeconds => Session.CurrentSeconds;
    public static double DurationSeconds => Session.DurationSeconds;
    public static bool AtEnd => Session.AtEnd;
    public static string? LastError => Session.LastError;
    public static string? LastWarning => Session.LastWarning;
    public static bool Join(string path, int timeoutMs = 8000)
    {
        Stop();
        _prepared = new(new PassiveReplaySessionHost());
        _failed = false; _presentationFailed = false;
        bool opened = _prepared.Join(path, timeoutMs);
        if (opened) { ReplayInput.CancelScrub(); ReplayCamera.ClearBookmarks(); ReplayCamera.Reset(); ReplayHud.Reset(); ReplayStudio.ResetCache(); }
        return opened;
    }
    internal static int CheckpointCount => _player?.CheckpointCount ?? 0;
    internal static string SeekDiagnostics => _player == null ? "" :
        $"{_player.CheckpointSource} restore {_player.SeekRestoreFrame} · {_player.SeekSimulationSteps} steps · {_player.SeekMilliseconds:0.0} ms · {_player.RejectedCheckpoints} rejected";
    internal static void Update(Scene shell)
    {
        if (!IsActive) return;
        if (_failed)
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
        if (_player == null)
        {
            var transport = _prepared.Transport;
            uint? seek = transport.SeekTarget;
            _player = new PassiveReplayPlayer(_prepared.CurrentPath!, _shell.Size);
            _player.Current.Session.FactRead += ReplayNetworkDiagnostics.OnPacketArray;
            _player.Stepped += ReplayVerification.AfterFrame;
            _player.Replaced += (previous, replacement) =>
            {
                ReplayAudioOwner.Release(_audio); _audio = 0;
                replacement.Scene.CopyReplayView(previous); replacement.Scene.UseReplayInput(_shell);
                replacement.Session.FactRead += ReplayNetworkDiagnostics.OnPacketArray;
                ReplayHud.Reset(); ReplayNetworkDiagnostics.Reset(); ReplayVerification.SeekTo(CurrentFrame);
            };
            ReplayVerification.Reset(); ReplayNetworkDiagnostics.Reset();
            _player.Transport.CopyPreferences(transport);
            if (seek.HasValue) _player.Transport.ContinueSeek(seek.Value, transport.ResumeAfterSeek);
            else if (transport.IsPaused) _player.Transport.Pause();
            _prepared.Dispose();
            if (_player.Current.Session.HasSimulatedFrame) ReplayVerification.AfterFrame(_player.Current.Scene);
        }
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
#if MPHREAD_SHELL
        scene.ReplayPreviewBounds = Launcher.Gui.UiSurface.Current?.ReplayViewportBounds(_shell.Size.X, _shell.Size.Y);
#endif
        var viewportSize = scene.ReplayPreviewBounds is { } bounds
            ? new Vector2i(bounds.Z, bounds.W) : _shell.Size;
        var size = ReplayVideoExporter.OutputSize ?? viewportSize;
        if (scene.Size != size) { scene.Size = size; scene.OnResize(); }
        double hostAlpha = Render.FrameTiming.Active ? Render.FrameTiming.PresentationAlpha : 1;
        scene.ReplayRenderAlpha = ReplayVideoExporter.Rendering ? ReplayVideoExporter.PresentationAlpha
            : Render.FrameTiming.Active ? Session.Transport.PresentationAlpha(hostAlpha) : 1;
        scene.ReplayPresentationFrame = ReplayVideoExporter.Rendering
            ? ReplayVideoExporter.PresentationFrame
            : Render.FrameTiming.Active ? Session.Transport.PresentationFrame(hostAlpha) : CurrentFrame;
        return ReferenceEquals(scene, shell) ? null : scene;
    }
    internal static void Release(Scene shell)
    { if (ReferenceEquals(_shell, shell)) Stop(); }
    public static void PumpFrame() => Session.PumpFrame();
    public static void Stop()
    {
        ReplayInput.CancelScrub();
        ReplayAudioOwner.Release(_audio); _audio = 0;
        _player?.Dispose(); _player = null;
        Scene? lab = _lab; _lab = null;
        lab?.DoCleanup(); lab?.UnloadGl(); _shell = null;
        _prepared.Stop(); _failed = false; _presentationFailed = false;
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
