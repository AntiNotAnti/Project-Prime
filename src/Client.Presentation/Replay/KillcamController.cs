using System;
using MphRead.Entities;
using MphRead.Hud;
using MphRead.Mods.Input;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead;

internal enum KillcamCommand
{
    None,
    Skip
}

internal enum KillcamEndReason
{
    Completed,
    Skipped,
    MatchChanged,
    NewLife,
    SceneClosing,
    Failure
}

internal enum KillcamState
{
    Idle,
    Capturing,
    Seeking,
    Presenting
}

internal readonly record struct KillcamDiagnostics(
    KillcamState State,
    KillcamPolicy Policy,
    CombatActor Victim,
    CombatActor Killer,
    uint KillFrame,
    uint ClipStart,
    uint ClipEnd,
    uint TailFramesCaptured,
    KillcamEndReason? EndReason);

/// <summary>
/// Immutable capture plan created at the authoritative kill boundary. The
/// fallback is captured immediately so the timeline may continue to evict or
/// reset without leaving a pending death with no restorable picture.
/// </summary>
internal readonly record struct PendingKillcamCapture(
    KillEvent Kill,
    KillcamPolicy Policy,
    uint KillRecordingFrame,
    uint DesiredStart,
    uint DesiredEnd,
    ReplayTimelineClip FallbackClip)
{
    internal uint KillFrame => KillRecordingFrame;
    // DesiredStart remains the requested lead boundary. ClipStart is the
    // actual restorable boundary used by the immutable clip when the rolling
    // timeline has not reached that requested frame yet.
    internal uint? ActualStart { get; init; }
    internal uint ClipStart => ActualStart ?? FallbackClip.StartRecordingFrame;
    internal uint ClipEnd => DesiredEnd;
}

internal readonly record struct KillcamCaptureWindow(uint Start, uint End);

/// <summary>
/// Owns the short-lived replay scene used for an authoritative local death.
/// The live presentation is never rewound and continues to advance while this
/// controller warms and presents its immutable clip.
/// </summary>
internal sealed class KillcamController : IDisposable
{
    internal const uint PreKillFrames = 300;
    internal const uint PostKillFrames = 45;
    internal const uint EndFreezeFrames = 15;
    // A stalled timeline must never keep a death pending forever. This is a
    // presentation-only bound; it does not pause or delay the live scene.
    internal const uint CaptureFallbackFrames = 120;

    // Compatibility names are intentionally not used by the controller. Keep
    // them internal for older focused tests compiled against the previous
    // seam while the new names remain the source of truth.
    [Obsolete("Use PreKillFrames.")]
    internal const uint LeadFrames = PreKillFrames;
    [Obsolete("Use EndFreezeFrames.")]
    internal const uint FreezeFrames = EndFreezeFrames;

    private readonly ScenePresentation _live;
    private readonly Func<Vector2i> _sizeProvider;
    private readonly KeyboardState _keyboard;
    private readonly MouseState _mouse;
    private AuthoritativePlay? _play;
    private PendingKillcamCapture? _pending;
    private KillEvent? _deferredKill;
    private KillcamPolicy _deferredPolicy;
    private uint _deferredWaitFrames;
    private ReplayTimelineClip? _pendingClip;
    private bool _pendingClipFinal;
    private uint _captureWaitFrames;
    private ReplayPlaybackSession? _session;
    private ScenePresentation? _replay;
    private KillEvent _kill;
    private KillcamPolicy _policy;
    private uint _endFreezeFrames;
    private KillcamState _state;
    private CombatActor _focusActor;
    private SpectatorCameraMode _focusMode;
    private uint _killFrame;
    private uint _clipStart;
    private uint _clipEnd;
    private uint _tailFramesCaptured;
    private ReplayTimelineClip? _activeClip;
    private bool _replayAudioActive;
    private GamepadButtons _gamepadHeld;
    private KillcamCommand _pendingCommand;
    private long _liveAudioVersion;
    private long _liveInputVersion;
    private bool _liveAudioWasActive;
    private Music.PresentationAudioSnapshot _liveMusicSnapshot;
    private (uint Match, uint Event, CombatActor Victim) _last;

    internal KillcamController(ScenePresentation live, Vector2i size,
        KeyboardState keyboard, MouseState mouse)
        : this(live, () => size, keyboard, mouse)
    {
    }

    internal KillcamController(ScenePresentation live, Func<Vector2i> sizeProvider,
        KeyboardState keyboard, MouseState mouse, AuthoritativePlay? play = null)
    {
        _live = live;
        _sizeProvider = sizeProvider ?? throw new ArgumentNullException(nameof(sizeProvider));
        _keyboard = keyboard;
        _mouse = mouse;
        Bind(play);
    }

    internal bool IsPresenting => _replay != null && _session is { IsSeeking: false };
    internal bool IsActive => _replay != null || _session != null;
    internal ScenePresentation RenderedPresentation => IsPresenting ? _replay! : _live;
    internal KillcamEndReason? LastEndReason { get; private set; }
    internal KillcamState State => _state;
    internal KillcamPolicy Policy => _policy;
    internal CombatActor Victim => _kill.Victim;
    internal CombatActor Killer => _kill.Killer;
    internal uint KillFrame => _killFrame;
    internal uint ClipStart => _clipStart;
    internal uint ClipEnd => _clipEnd;
    internal uint TailFramesCaptured => _tailFramesCaptured;
    internal KillcamEndReason? EndReason => LastEndReason;
    internal KillcamDiagnostics Diagnostics => new(_state, _policy, _kill.Victim,
        _kill.Killer, _killFrame, _clipStart, _clipEnd, _tailFramesCaptured,
        LastEndReason);

    internal PendingKillcamCapture? PendingCapture => _pending;
    internal ReplayTimelineClip? PendingClip => _pendingClip;

    /// <summary>
    /// Translate all supported one-shot surfaces in one place. Holding a
    /// button never repeats because callers submit only the rising edge.
    /// </summary>
    internal static KillcamCommand TranslateCommand(bool keyboardPressed,
        bool gamepadPressed, bool touchPressed)
        => keyboardPressed || gamepadPressed || touchPressed
            ? KillcamCommand.Skip : KillcamCommand.None;

    internal void SubmitCommand(KillcamCommand command)
    {
        if (command == KillcamCommand.Skip && IsActive)
            _pendingCommand = KillcamCommand.Skip;
    }

    internal void SubmitTouchSkip() => SubmitCommand(KillcamCommand.Skip);

    internal void SubmitInput(WindowInputSnapshot input)
    {
        bool keyboardPressed = false;
        // A host that has no keyboard event queue can pass the default value
        // type. Keep that path valid so Android can poll controller state
        // without allocating a synthetic snapshot every render frame.
        foreach (WindowKeyEvent key in input.KeyEvents ?? Array.Empty<WindowKeyEvent>())
        {
            if (key.Down && !key.Repeat && key.Key is Keys.Space or Keys.Escape)
            {
                keyboardPressed = true;
                break;
            }
        }

        GamepadButtons current = GamepadInput.State.Buttons;
        GamepadButtons rising = current & ~_gamepadHeld;
        _gamepadHeld = current;
        GamepadButtons configuredSkip = PadBindings.Get(PadAction.Menu)
            | PadBindings.Get(PadAction.Jump) | PadBindings.Get(PadAction.Morph);
        bool gamepadPressed = (rising & configuredSkip) != 0;
        KillcamCommand command = TranslateCommand(keyboardPressed,
            gamepadPressed, touchPressed: false);
        if (command != KillcamCommand.None) SubmitCommand(command);
    }

    internal void Resize(Vector2i size)
    {
        if (_replay == null || size.X <= 0 || size.Y <= 0 || _replay.Size == size) return;
        try
        {
            _replay.Size = size;
            _replay.OnResize();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-resize", error);
            Stop(KillcamEndReason.Failure);
        }
    }

    private void Bind(AuthoritativePlay? play)
    {
        if (ReferenceEquals(_play, play)) return;
        if (_play != null) _play.LocalPlayerKilled -= OnLocalPlayerKilled;
        _play = play;
        if (_play != null) _play.LocalPlayerKilled += OnLocalPlayerKilled;
    }

    private void OnLocalPlayerKilled(KillEvent kill)
    {
        if (_last == (kill.MatchId, kill.Id, kill.Victim)
            || _deferredKill is { } deferred
                && (deferred.MatchId, deferred.Id, deferred.Victim)
                    == (kill.MatchId, kill.Id, kill.Victim)) return;
        KillcamPolicy policy = Mods.GameSettings.ResolveKillcamPolicy(
            _play?.Client.Accepted.Rules.KillcamPolicy ?? KillcamPolicy.Disabled);
        if (policy == KillcamPolicy.Disabled) return;
        if (!TryCreatePendingKillcamCapture(ReplayRecorder.Timeline, kill, policy,
                out PendingKillcamCapture capture))
        {
            // Reliable combat delivery can precede the first complete rolling
            // restore by one fixed step. Keep the exact authoritative event
            // for a short bounded retry instead of silently losing this life's
            // only killcam opportunity.
            _deferredKill = kill;
            _deferredPolicy = policy;
            _deferredWaitFrames = 0;
            return;
        }
        BeginCapture(capture);
    }

    private void BeginCapture(in PendingKillcamCapture capture)
    {
        KillEvent kill = capture.Kill;
        _last = (kill.MatchId, kill.Id, kill.Victim);
        _deferredKill = null;
        _deferredWaitFrames = 0;

        _kill = kill;
        _policy = capture.Policy;
        _killFrame = capture.KillRecordingFrame;
        _clipStart = capture.FallbackClip.StartRecordingFrame;
        _clipEnd = capture.FallbackClip.EndRecordingFrame;
        _tailFramesCaptured = TailFrames(capture.KillRecordingFrame,
            capture.FallbackClip.EndRecordingFrame);
        _focusActor = ResolveFocusActor(kill);
        _pending = capture;
        _pendingClip = capture.FallbackClip;
        _pendingClipFinal = false;
        _captureWaitFrames = 0;
        _state = KillcamState.Capturing;
        LastEndReason = null;
    }

    /// <summary>
    /// Maps the exact authoritative kill event, validates that a complete
    /// restore exists for the requested lead window, and captures the
    /// kill-frame fallback before returning a mutable capture plan.
    /// </summary>
    internal static bool TryCreatePendingKillcamCapture(IReplayTimeline timeline,
        in KillEvent kill, KillcamPolicy policy, out PendingKillcamCapture capture)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        capture = default;
        if (!kill.IsValid || policy is KillcamPolicy.Disabled
            || !Enum.IsDefined(policy)
            || !timeline.TryMapKillToRecordingFrame(kill, out uint killFrame))
            return false;

        KillcamCaptureWindow window = GetCaptureWindow(killFrame);
        uint actualStart = window.Start;
        if (!timeline.TryGetRestorePoint(actualStart,
                out ReplayRestorePoint? restore) || restore == null)
        {
            if (!TryGetEarliestRestoreAtOrBefore(timeline, killFrame,
                    out restore) || restore == null)
                return false;
            actualStart = restore.RecordingFrame;
        }
        if (actualStart > killFrame || !timeline.TryFreeze(actualStart, killFrame,
                out ReplayTimelineClip? fallback) || fallback == null
            || !IsUsableClip(fallback, kill, killFrame))
            return false;
        capture = new PendingKillcamCapture(kill, policy, killFrame,
            window.Start, window.End, fallback) { ActualStart = actualStart };
        return true;
    }

    /// <summary>
    /// Finds the first complete restore available no later than the kill. The
    /// timeline interface intentionally exposes only predecessor lookup, so a
    /// bounded binary search avoids assuming restore cadence while keeping a
    /// live capture independent of the concrete rolling/file implementation.
    /// </summary>
    internal static bool TryGetEarliestRestoreAtOrBefore(IReplayTimeline timeline,
        uint maxRecordingFrame, out ReplayRestorePoint? restore)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        restore = null;
        if (timeline.FirstRecordingFrame is not uint firstFrame
            || firstFrame > maxRecordingFrame
            || !timeline.TryGetRestorePoint(maxRecordingFrame,
                out ReplayRestorePoint? latest) || latest == null)
            return false;

        uint low = firstFrame;
        uint high = maxRecordingFrame;
        while (low < high)
        {
            uint middle = low + ((high - low) >> 1);
            if (timeline.TryGetRestorePoint(middle,
                    out ReplayRestorePoint? candidate) && candidate != null)
                high = middle;
            else
                low = middle + 1;
        }
        return timeline.TryGetRestorePoint(low, out restore)
            && restore != null && restore.RecordingFrame <= maxRecordingFrame;
    }

    // Short alias kept as a pure seam for callers that describe the operation
    // as preparation rather than capture.
    internal static bool TryPrepareCapture(IReplayTimeline timeline,
        in KillEvent kill, KillcamPolicy policy, out PendingKillcamCapture capture)
        => TryCreatePendingKillcamCapture(timeline, kill, policy, out capture);

    internal static KillcamCaptureWindow GetCaptureWindow(uint killFrame)
        => new(killFrame > PreKillFrames ? killFrame - PreKillFrames : 0,
            SaturatingAdd(killFrame, PostKillFrames));

    internal static uint SaturatingAdd(uint value, uint amount)
        => uint.MaxValue - value < amount ? uint.MaxValue : value + amount;

    internal static uint TailFrames(uint killFrame, uint clipEnd)
        => clipEnd < killFrame ? 0 : clipEnd - killFrame;

    internal static bool CaptureFallbackExpired(uint waitedFrames)
        => waitedFrames >= CaptureFallbackFrames;

    internal static bool IsCaptureReady(in PendingKillcamCapture capture,
        bool hasFinalClip, MatchPhase phase)
        => hasFinalClip && (capture.Policy != KillcamPolicy.PostRound
            || phase is MatchPhase.Ending or MatchPhase.Intermission);

    internal static bool TryCaptureClip(IReplayTimeline timeline, uint killTick,
        out ReplayTimelineClip? clip)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        clip = null;
        if (!timeline.TryMapServerTickToRecordingFrame(killTick, out uint killFrame))
            return false;
        uint startFrame = killFrame > PreKillFrames ? killFrame - PreKillFrames : 0;
        return timeline.TryFreeze(startFrame, killFrame, out clip) && clip != null;
    }

    internal static bool TryCaptureClip(IReplayTimeline timeline,
        in KillEvent kill, out ReplayTimelineClip? clip)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        clip = null;
        return TryCreatePendingKillcamCapture(timeline, kill,
            KillcamPolicy.Immediate, out PendingKillcamCapture capture)
            && (clip = capture.FallbackClip) != null;
    }

    internal static bool TryFreezeLatestValid(IReplayTimeline timeline,
        in PendingKillcamCapture capture, out ReplayTimelineClip? clip)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        clip = null;
        uint start = capture.ClipStart;
        uint end = capture.KillRecordingFrame;
        if (timeline.LastRecordingFrame is uint latest)
            end = Math.Min(latest, capture.DesiredEnd);
        if (end >= capture.KillRecordingFrame && start <= capture.KillRecordingFrame
            && timeline.TryFreeze(start, end,
                out ReplayTimelineClip? latestClip) && latestClip != null
            && IsUsableClip(latestClip, capture.Kill, capture.KillRecordingFrame))
        {
            clip = latestClip;
            return true;
        }
        if (IsUsableClip(capture.FallbackClip, capture.Kill,
                capture.KillRecordingFrame))
        {
            clip = capture.FallbackClip;
            return true;
        }
        return false;
    }

    internal static bool IsUsableClip(ReplayTimelineClip clip,
        in KillEvent kill, uint killFrame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.RestorePoint == null || clip.EndRecordingFrame < killFrame
            || clip.StartRecordingFrame > killFrame)
            return false;
        foreach (ReplayTimelineRecord record in clip.RestorePoint.Records)
            if (ReplayTimelineEventReader.IsExactKill(record, kill)) return true;
        foreach (ReplayTimelineRecord record in clip.Records)
            if (ReplayTimelineEventReader.IsExactKill(record, kill)) return true;
        return false;
    }

    internal static CombatActor ResolveFocusActor(in KillEvent kill)
        => IsValidEnemyKiller(kill) ? kill.Killer : kill.Victim;

    internal static SpectatorCameraMode ResolveFocusMode(in KillEvent kill)
        => IsValidEnemyKiller(kill) ? SpectatorCameraMode.FirstPerson
            : SpectatorCameraMode.Chase;

    internal static bool IsValidEnemyKiller(in KillEvent kill)
        => kill.Killer.IsValid && kill.Victim.IsValid
            && !kill.IsSuicide && kill.Killer.Slot != kill.Victim.Slot;

    internal static bool IsExactFocus(CombatActor expected, CombatActor actual)
        => expected.IsValid && expected == actual;

    internal static float Progress(uint currentFrame, uint startFrame,
        uint endFrame)
    {
        if (endFrame <= startFrame) return currentFrame >= endFrame ? 1 : 0;
        if (currentFrame <= startFrame) return 0;
        return Math.Clamp((currentFrame - startFrame) / (float)(endFrame - startFrame),
            0, 1);
    }

    internal void Advance()
    {
        try
        {
            AdvanceCore();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam", error);
            Console.Error.WriteLine($"[killcam] Advance failed: {error.Message}");
            try
            {
                Stop(KillcamEndReason.Failure);
            }
            catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
            {
                DebugLog.Exception("killcam-cleanup", cleanupError);
                Console.Error.WriteLine($"[killcam] Cleanup failed: {cleanupError.Message}");
            }
        }
    }

    private void AdvanceCore()
    {
        if (_replay == null && _pending == null && _deferredKill is { } deferred)
        {
            if (_play == null || _play.KillcamLiveContextChanged(deferred,
                    includeNewLife: false)
                || CaptureFallbackExpired(++_deferredWaitFrames))
            {
                _deferredKill = null;
                _deferredWaitFrames = 0;
            }
            else if (TryCreatePendingKillcamCapture(ReplayRecorder.Timeline,
                    deferred, _deferredPolicy, out PendingKillcamCapture capture))
            {
                BeginCapture(capture);
            }
        }

        if (_replay != null && _pendingCommand == KillcamCommand.Skip)
        {
            _pendingCommand = KillcamCommand.None;
            Stop(KillcamEndReason.Skipped);
            return;
        }

        if (_replay == null && _pending is PendingKillcamCapture pending)
        {
            if (PendingLiveContextChanged(pending))
            {
                // In particular, an Immediate respawn is never held behind
                // capture warm-up. The authoritative live scene keeps its
                // normal input/simulation ownership throughout this branch.
                Stop(ContextEndReason());
                return;
            }

            if (!_pendingClipFinal)
            {
                IReplayTimeline timeline = ReplayRecorder.Timeline;
                bool reachedEnd = timeline.LastRecordingFrame is uint latest
                    && latest >= pending.DesiredEnd;
                if (reachedEnd)
                {
                    if (TryFreezeValid(timeline, pending, pending.DesiredEnd,
                            out ReplayTimelineClip? full) && full != null)
                    {
                        SetPendingClip(full, final: true);
                    }
                    else if (!TryUseLatestValid(timeline, pending))
                    {
                        Stop(KillcamEndReason.Failure);
                        return;
                    }
                }
                else if (CaptureFallbackExpired(++_captureWaitFrames))
                {
                    if (!TryUseLatestValid(timeline, pending))
                    {
                        Stop(KillcamEndReason.Failure);
                        return;
                    }
                }
            }

            bool phaseReady = pending.Policy != KillcamPolicy.PostRound
                || _live.World.Match.Phase is MatchPhase.Ending or MatchPhase.Intermission;
            if (_pendingClipFinal && _pendingClip is ReplayTimelineClip readyClip && phaseReady)
            {
                // Recheck immediately before allocating/loading the passive
                // scene. A same-frame respawn must win over Immediate replay.
                if (PendingLiveContextChanged(pending))
                {
                    Stop(ContextEndReason());
                    return;
                }
                if (!Start(pending, readyClip))
                {
                    Stop(KillcamEndReason.Failure);
                    return;
                }
                _pending = null;
                _pendingClip = null;
            }
        }
        if (_replay == null || _session == null) return;
        if (_play?.KillcamLiveContextChanged(_kill,
                includeNewLife: _policy == KillcamPolicy.Immediate) != false)
        {
            Stop(ContextEndReason());
            return;
        }

        _replay.OnSimulationFrame();
        if (_session.IsSeeking)
        {
            _state = KillcamState.Seeking;
            return;
        }
        if (!TryEnsureReplayFocus())
        {
            // Do not let a reused replay slot redirect the camera to the next
            // occupant. The replay scene is presentation-only, so ending it
            // is sufficient and leaves live ownership untouched.
            Stop(KillcamEndReason.MatchChanged);
            return;
        }
        _state = KillcamState.Presenting;
        if (!_replayAudioActive)
        {
            _liveAudioWasActive = _live.PresentationAudioActive;
            _liveMusicSnapshot = Music.CapturePresentationAudio();
            GamepadInput.BeginGameplayInputQuarantine();
            _live.SetGameplayInputSuppressed(true);
            _live.SetPresentationAudio(false);
            _liveInputVersion = _live.GameplayInputSuppressionVersion;
            _liveAudioVersion = _live.PresentationAudioVersion;
            _replayAudioActive = true;
            try
            {
                _replay.SetPresentationAudio(true);
                Sfx.BindPresentation(_replay.World);
            }
            catch (Exception error)
            {
                Console.WriteLine($"[killcam] Audio handoff failed ({error.Message}); returning live");
                Stop(KillcamEndReason.Failure);
                return;
            }
        }
        if (_kill.Killer.IsValid && !_kill.IsSuicide)
            _replay.SetFreeCamera(false);
        if (_session.AtEnd)
        {
            if (++_endFreezeFrames >= EndFreezeFrames) Stop(KillcamEndReason.Completed);
        }
    }

    private bool PendingLiveContextChanged(in PendingKillcamCapture pending)
    {
        if (_play == null) return true;
        return _play.KillcamLiveContextChanged(pending.Kill,
            includeNewLife: pending.Policy == KillcamPolicy.Immediate);
    }

    private static bool TryFreezeValid(IReplayTimeline timeline,
        in PendingKillcamCapture capture, uint end,
        out ReplayTimelineClip? clip)
    {
        clip = null;
        end = Math.Min(end, capture.DesiredEnd);
        uint start = capture.ClipStart;
        if (end < capture.KillRecordingFrame || start > capture.KillRecordingFrame
            || !timeline.TryFreeze(start, end,
                out ReplayTimelineClip? candidate) || candidate == null
            || !IsUsableClip(candidate, capture.Kill,
                capture.KillRecordingFrame)) return false;
        clip = candidate;
        return true;
    }

    private bool TryUseLatestValid(IReplayTimeline timeline,
        in PendingKillcamCapture capture)
    {
        if (!TryFreezeLatestValid(timeline, capture,
                out ReplayTimelineClip? clip) || clip == null) return false;
        SetPendingClip(clip, final: true);
        return true;
    }

    private void SetPendingClip(ReplayTimelineClip clip, bool final)
    {
        _pendingClip = clip;
        _pendingClipFinal = final;
        _clipStart = clip.StartRecordingFrame;
        _clipEnd = clip.EndRecordingFrame;
        _tailFramesCaptured = TailFrames(_killFrame, clip.EndRecordingFrame);
    }

    private bool TryEnsureReplayFocus()
    {
        if (_replay == null) return false;
        // The killer is the preferred shot, but a valid killer may not yet be
        // represented by the first checkpoint in the lead window. Until the
        // exact connection/life appears, use the victim's exact actor in a
        // chase shot. Never point either mode at a same-slot replacement.
        if (IsValidEnemyKiller(_kill)
            && IsReplayActorAvailable(_kill.Killer))
            return SetReplayFocus(_kill.Killer, SpectatorCameraMode.FirstPerson);
        return _kill.Victim.IsValid
            && IsReplayActorAvailable(_kill.Victim)
            && SetReplayFocus(_kill.Victim, SpectatorCameraMode.Chase);
    }

    private bool IsReplayActorAvailable(CombatActor expected)
    {
        if (_replay == null || !expected.IsValid
            || expected.Slot >= _replay.World.Players.Count) return false;
        PlayerEntity player = _replay.World.Players[expected.Slot];
        return IsExactFocus(expected, player.CombatIdentity)
            && player.LoadFlags.TestFlag(LoadFlags.Active);
    }

    private bool SetReplayFocus(CombatActor actor, SpectatorCameraMode mode)
    {
        if (_replay == null || !IsReplayActorAvailable(actor)) return false;
        if (_focusActor != actor || _focusMode != mode)
        {
            _focusActor = actor;
            _focusMode = mode;
            _replay.SpectatorCamera.ConfigureKillcam(actor.Slot, mode);
        }
        return true;
    }

    private KillcamEndReason ContextEndReason()
    {
        if (_play == null || _play.Client.Failure != null) return KillcamEndReason.Failure;
        if (_play.State != AuthoritativePlay.TerminalState.Active)
            return KillcamEndReason.SceneClosing;
        if (_play.Client.Accepted.MatchId != _kill.MatchId
            || _play.LocalSlot != _kill.Victim.Slot)
            return KillcamEndReason.MatchChanged;
        return _policy == KillcamPolicy.Immediate
            ? KillcamEndReason.NewLife : KillcamEndReason.MatchChanged;
    }

    private bool Start(in PendingKillcamCapture pending, ReplayTimelineClip clip)
    {
        var session = new ReplayPlaybackSession();
        if (!session.Join(clip)) { session.Dispose(); return false; }
        ScenePresentation? presentation = null;
        try
        {
            _kill = pending.Kill;
            _policy = pending.Policy;
            _focusActor = ResolveFocusActor(_kill);
            SpectatorCameraMode focusMode = ResolveFocusMode(_kill);
            _focusMode = focusMode;
            if (!_focusActor.IsValid) return false;
            int focus = _focusActor.Slot;
            session.SetPerspective(focus);
            var scene = new Scene(features: ClientMatchFeatures.Capture())
            {
                Services = session.SceneServices
            };
            presentation = new ScenePresentation(scene, ResolveSize(), _keyboard, _mouse,
                static _ => { }, () => Stop(KillcamEndReason.SceneClosing),
                _live.Timing, session.SceneServices, session);
            session.BuildPlayers(scene);
            MatchRules? rules = session.InitialRules;
            if (rules == null) return false;
            presentation.AddRoom(rules.RoomKey, rules.Mode.ToLegacyMode(),
                playerCount: session.Modern.Roster.Length);
            presentation.SpectatorCamera.ConfigureKillcam(focus, focusMode);
            presentation.AdditionalOverlay = DrawOverlay;
            presentation.OnLoad();
            if (!session.Seek(clip.StartRecordingFrame)) return false;
            _session = session;
            _replay = presentation;
            _activeClip = clip;
            _clipStart = clip.StartRecordingFrame;
            _clipEnd = clip.EndRecordingFrame;
            _tailFramesCaptured = TailFrames(_killFrame, clip.EndRecordingFrame);
            _endFreezeFrames = 0;
            _state = KillcamState.Seeking;
            presentation = null;
            session = null!;
            return true;
        }
        finally
        {
            try { presentation?.DoCleanup(preserveSharedAudio: true); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { DebugLog.Exception("killcam-start-cleanup", error); }
            finally
            {
                try { session?.Dispose(); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { DebugLog.Exception("killcam-start-session", error); }
            }
        }
    }

    private Vector2i ResolveSize()
    {
        Vector2i size = _sizeProvider();
        return size.X > 0 && size.Y > 0 ? size : _live.Size;
    }

    private void DrawOverlay(ScenePresentation presentation)
    {
        if (presentation.World.Players.Count == 0
            || presentation.World.LocalPlayer is not { } localPlayer) return;
        PlayerPresentation hud = localPlayer.GetPresentation();
        presentation.DrawHudFlatBox(5, 4, 251, 26,
            new Vector4(0, 0, 0, .72f));
        hud.DrawText2D(14, 8, Align.Left, 0, "KILLCAM", scale: .62f);
        hud.DrawText2D(242, 8, Align.Right, 0, "SKIP", scale: .5f);
        int killerSlot = _kill.Killer.IsValid ? _kill.Killer.Slot : -1;
        string killer = killerSlot is >= 0 and < PlayerEntity.SlotCapacity
            ? presentation.World.Roster.Nicknames[killerSlot] ?? $"Player{killerSlot + 1}"
            : "ENVIRONMENT";
        string weapon = _kill.Weapon <= 10 ? ((BeamType)_kill.Weapon).ToString()
            : _kill.SourceKind.ToString();
        string detail = (_kill.Flags & KillEventFlags.Headshot) != 0
            ? $"{weapon}  HEADSHOT" : weapon;
        presentation.DrawHudFlatBox(24, 157, 232, 185,
            new Vector4(0, 0, 0, .72f));
        hud.DrawText2D(128, 162, Align.Center, 0,
            $"ELIMINATED BY {killer}", maxLength: 40, scale: .58f);
        hud.DrawText2D(128, 174, Align.Center, 0, detail,
            maxLength: 40, scale: .5f);

        float progress = _session == null ? 0
            : Progress(_session.CurrentFrame, _clipStart, _clipEnd);
        presentation.DrawHudFlatBox(24, 188, 232, 192,
            new Vector4(.08f, .08f, .08f, .9f));
        presentation.DrawHudFlatBox(25, 189, 25 + 204 * progress, 191,
            new Vector4(.3f, .8f, .95f, .95f));
    }

    private void Stop(KillcamEndReason reason)
    {
        ScenePresentation? presentation = _replay;
        ReplayPlaybackSession? session = _session;
        bool ownedPresentation = presentation != null || session != null
            || _replayAudioActive || _pending != null || _pendingClip != null
            || _activeClip != null;
        _replay = null;
        _session = null;
        _pending = null;
        _deferredKill = null;
        _deferredWaitFrames = 0;
        _pendingClip = null;
        _pendingClipFinal = false;
        _captureWaitFrames = 0;
        _endFreezeFrames = 0;
        _activeClip = null;
        _state = KillcamState.Idle;
        _pendingCommand = KillcamCommand.None;
        _gamepadHeld = GamepadInput.State.Buttons;
        if (_replayAudioActive)
        {
            _replayAudioActive = false;
            GamepadInput.EndGameplayInputQuarantine();
            try
            {
                presentation?.SetPresentationAudio(false);
            }
            catch (Exception error)
            {
                DebugLog.Exception("killcam-replay-audio", error);
            }
            bool audioRestored = false;
            try
            {
                _live.TryRestoreGameplayInputSuppressed(false, _liveInputVersion);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                DebugLog.Exception("killcam-input-restore", error);
            }
            try
            {
                audioRestored = _live.TryRestorePresentationAudio(_liveAudioWasActive,
                    _liveAudioVersion);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                DebugLog.Exception("killcam-audio-restore", error);
            }
            if (audioRestored)
            {
                try
                {
                    Sfx.BindPresentation(_live.World);
                    if (!Music.TryRestorePresentationAudio(_liveMusicSnapshot))
                    {
                        // The snapshot is deliberately best-effort: content
                        // can be torn down while a replay is ending. Only
                        // after the live scene still owns audio may we use its
                        // current room as a safe, visible fallback.
                        Console.WriteLine("[killcam] Live music snapshot restore unavailable; using current room track.");
                        Music.TryPlayRoomMusic(_live.World.RoomId, 0);
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Console.WriteLine($"[killcam] Live audio restore failed ({error.Message})");
                }
            }
        }
        try
        {
            presentation?.DoCleanup(preserveSharedAudio: true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-cleanup", error);
        }
        finally
        {
            try { session?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { DebugLog.Exception("killcam-session", error); }
        }
        // Dispose is intentionally idempotent.  Once a clip has ended, a
        // later no-op Dispose must not replace the useful terminal reason.
        if (ownedPresentation)
            LastEndReason = reason;
    }

    public void Dispose()
    {
        if (_play != null) _play.LocalPlayerKilled -= OnLocalPlayerKilled;
        _play = null;
        Stop(KillcamEndReason.SceneClosing);
    }
}
