using System;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Transport commands go to DemoPlayback's existing ReplayController and camera.</summary>
public sealed class TheatrePlaybackController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly ITheatreBackend _backend;
    private TheatrePlaybackSnapshot? _snapshot;
    private TheatrePlaybackAction? _engineCommand;
    private string _error = "";
    private bool _disposed;

    public TheatrePlaybackController(ITheatreBackend backend) => _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    public TheatrePlaybackSnapshot Snapshot()
    {
        VerifyOwner();
        bool active = !_disposed && DemoPlayback.IsActive;
        var next = new TheatrePlaybackSnapshot(_lifetime, _snapshot?.Revision ?? 0, active,
            active ? ReplayController.CurrentFrame : 0, active ? ReplayController.DurationFrames : 0,
            active ? ReplayController.State.ToString() : "Inactive", active ? ReplayController.PlaybackRate : 1,
            active && (ReplayController.IsPaused || ReplayController.AtEnd), ReplayCamera.Mode.ToString(),
            _error.Length > 0 ? _error : DemoPlayback.LastError ?? "");
        if (_snapshot == null || next != _snapshot) _snapshot = next with { Revision = (_snapshot?.Revision ?? 0) + 1 };
        return _snapshot;
    }

    public bool Dispatch(TheatrePlaybackAction action, int position = 0)
    {
        VerifyOwner();
        if (_disposed || !Enum.IsDefined(action)) return false;
        if (action is TheatrePlaybackAction.Back or TheatrePlaybackAction.Fullscreen)
        { _engineCommand ??= action; return true; }
        if (!DemoPlayback.IsActive) return false;
        switch (action)
        {
            case TheatrePlaybackAction.TogglePause: ReplayController.TogglePause(); break;
            case TheatrePlaybackAction.JumpBack: Jump(-600); break;
            case TheatrePlaybackAction.JumpForward: Jump(600); break;
            case TheatrePlaybackAction.Restart: ReplayController.Restart(); break;
            case TheatrePlaybackAction.Step: ReplayController.StepForward(); break;
            case TheatrePlaybackAction.NextRate:
                int rate = (Array.IndexOf(ReplayController.Rates, ReplayController.PlaybackRate) + 1) % ReplayController.Rates.Length;
                ReplayController.SetPlaybackRate(ReplayController.Rates[rate]); break;
            case TheatrePlaybackAction.NextCamera:
                ReplayCamera.Director = false; ReplayCamera.PlayTrack = false;
                ReplayCamera.SetMode((ReplayCameraMode)(((int)ReplayCamera.Mode + 1) % 4)); break;
            case TheatrePlaybackAction.PreviousPlayer: SpectatorMode.CyclePrevious(); break;
            case TheatrePlaybackAction.NextPlayer: SpectatorMode.CycleNext(); break;
            case TheatrePlaybackAction.Seek:
                ReplayController.Seek((uint)Math.Round(Math.Clamp(position, 0, 1000) * ReplayController.DurationFrames / 1000d), resume: false); break;
            case TheatrePlaybackAction.Studio:
                if (DemoPlayback.LogicalPath is not { } path) { _error = "Open a replay first."; return false; }
                _error = _backend.OpenStudio(path, out string? problem) ? "Opening Project Prime Studio" : problem ?? "Studio could not start.";
                break;
            default: return false;
        }
        return true;
    }

    public void Jump(int frames)
    {
        VerifyOwner();
        if (!_disposed && DemoPlayback.IsActive)
            ReplayController.Seek((uint)Math.Clamp((long)ReplayController.CurrentFrame + frames, 0, ReplayController.DurationFrames), resume: false);
    }
    public bool TryTakeEngineCommand(out TheatrePlaybackAction action)
    {
        VerifyOwner(); action = default;
        if (_disposed || _engineCommand is not { } pending) return false;
        _engineCommand = null; action = pending; return true;
    }
    public void Dispose() { VerifyOwner(); _disposed = true; _engineCommand = null; }
    private void VerifyOwner() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Replay transport belongs to the engine thread."); }
}
