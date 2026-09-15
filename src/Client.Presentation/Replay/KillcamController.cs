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

/// <summary>Independent terminal presentation owned by the live scene.</summary>
internal enum FinalSequenceState
{
    None,
    Replay,
    GameOver,
    AwaitCompletion
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
    internal const uint FinalGameOverFrames = 105;
    internal const uint FinalResultWaitFrames = 30;
    internal const uint FinalReplaySeekFrames = 120;
    internal const uint FinalCausalTickLag = 1;

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
    private long _liveHudVersion;
    private bool _liveSuppressionActive;
    private bool _liveInputWasSuppressed;
    private bool _liveHudWasSuppressed;
    private bool _liveAudioWasActive;
    private Music.PresentationAudioSnapshot _liveMusicSnapshot;
    private (uint Match, uint Event, CombatActor Victim) _last;
    private KillEvent? _latestObservedKill;
    private MatchEvent? _finalEnded;
    private ReplayTimelineClip? _finalClip;
    private FinalSequenceState _finalState;
    private bool _finalDecisionPending;
    private bool _finalClipCaptureAttempted;
    private uint _finalResultWaitFrames;
    private uint _finalReplaySeekFrames;
    private uint _finalGameOverFrames;
    private bool _finalSuppressionActive;

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
    internal ScenePresentation RenderedPresentation
        => _replay != null && _session is { IsSeeking: false }
            && (_finalState == FinalSequenceState.Replay || IsPresenting)
            ? _replay : _live;
    internal FinalSequenceState FinalState => _finalState;
    internal bool FinalSequenceActive => _finalState != FinalSequenceState.None;
    internal bool BlocksSceneCompletion => DoesBlockSceneCompletion(_finalState);
    internal bool FinalSequenceReplayActive => _finalState == FinalSequenceState.Replay;
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

    internal static bool IsBindingPressed(Keybind binding,
        in WindowInputSnapshot input)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Type == ButtonType.Key)
        {
            Keys expected = (Keys)(int)binding.Key;
            if (expected == Keys.Unknown) return false;
            foreach (WindowKeyEvent key in input.KeyEvents
                ?? Array.Empty<WindowKeyEvent>())
            {
                if (key.Down && !key.Repeat && key.Key == expected) return true;
            }
            return false;
        }
        if (binding.Type == ButtonType.Mouse)
        {
            MouseButton expected = (MouseButton)(int)binding.MouseButton;
            foreach (WindowMouseButtonEvent button in input.MouseButtonEvents
                ?? Array.Empty<WindowMouseButtonEvent>())
            {
                if (button.Down && button.Button == expected) return true;
            }
            return false;
        }
        return binding.Type == ButtonType.ScrollUp && input.Wheel.Y > 0
            || binding.Type == ButtonType.ScrollDown && input.Wheel.Y < 0;
    }

    internal void SubmitCommand(KillcamCommand command)
    {
        if (command == KillcamCommand.Skip
            && (_finalState == FinalSequenceState.Replay
                || _finalState == FinalSequenceState.None && IsActive))
            _pendingCommand = KillcamCommand.Skip;
    }

    internal void SubmitTouchSkip() => SubmitCommand(KillcamCommand.Skip);

    internal void SubmitInput(WindowInputSnapshot input)
    {
        bool desktopPressed = IsBindingPressed(InputSettings.Current.Shoot, input);
        // A host that has no keyboard event queue can pass the default value
        // type. Keep that path valid so Android can poll controller state
        // without allocating a synthetic snapshot every render frame.
        foreach (WindowKeyEvent key in input.KeyEvents ?? Array.Empty<WindowKeyEvent>())
        {
            if (key.Down && !key.Repeat && key.Key is Keys.Space or Keys.Escape)
            {
                desktopPressed = true;
                break;
            }
        }

        GamepadButtons current = GamepadInput.State.Buttons;
        GamepadButtons rising = current & ~_gamepadHeld;
        _gamepadHeld = current;
        GamepadButtons configuredSkip = PadBindings.Get(PadAction.Menu)
            | PadBindings.Get(PadAction.Jump) | PadBindings.Get(PadAction.Morph)
            | PadBindings.Get(PadAction.Shoot);
        bool gamepadPressed = (rising & configuredSkip) != 0;
        KillcamCommand command = TranslateCommand(desktopPressed,
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
        if (_play != null)
        {
            _play.LocalPlayerKilled -= OnLocalPlayerKilled;
            _play.AuthoritativeKillObserved -= OnAuthoritativeKillObserved;
            _play.AuthoritativeMatchEndedAccepted -= OnAuthoritativeMatchEndedAccepted;
        }
        _play = play;
        _latestObservedKill = null;
        _finalEnded = null;
        if (_play != null)
        {
            _play.LocalPlayerKilled += OnLocalPlayerKilled;
            _play.AuthoritativeKillObserved += OnAuthoritativeKillObserved;
            _play.AuthoritativeMatchEndedAccepted += OnAuthoritativeMatchEndedAccepted;
        }
    }

    private void OnAuthoritativeKillObserved(KillEvent kill)
    {
        if (_play == null || _play.Client.Accepted.MatchId != kill.MatchId)
            return;
        // Keep accepting facts while the terminal result is still pending so
        // a later same-tick kill can replace an earlier candidate. Once the
        // decision is committed, the immutable clip/state machine is fenced.
        if (_finalState != FinalSequenceState.None && !_finalDecisionPending)
            return;
        if (_latestObservedKill is { } previous
            && previous.MatchId == kill.MatchId && previous.Id == kill.Id
            && previous.Victim == kill.Victim) return;
        _latestObservedKill = kill;
    }

    private void OnLocalPlayerKilled(KillEvent kill)
    {
        if (FinalSequenceActive) return;
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

    private void OnAuthoritativeMatchEndedAccepted(MatchEvent ended)
    {
        if (FinalSequenceActive || _play == null
            || _play.Client.Accepted.MatchId != ended.MatchId)
            return;

        _finalEnded = ended;
        _finalState = FinalSequenceState.GameOver;
        _finalClipCaptureAttempted = false;
        _finalResultWaitFrames = 0;
        _finalGameOverFrames = 0;
        _finalClip = null;
        _finalDecisionPending = false;
        // A terminal edge owns the picture even when an ordinary local-death
        // killcam is already presenting. Cleanup is intentionally restore-free
        // so live HUD/input/audio cannot flash back for one frame.
        if (_replay != null || _session != null || _pending != null
            || _pendingClip != null)
            Stop(KillcamEndReason.MatchChanged, restoreLive: false);
        EnterFinalSuppression();

        // This is deliberately a single freeze attempt, immediately after
        // ReplayRecorder.RecordEvent(message) in AuthoritativePlay.DrainEvents.
        // The latest observed kill is the only candidate. Score-goal and
        // survival endings still require a same-tick causal kill; a timed
        // Battle ending may replay its most recent retained enemy kill.
        if (!_finalClipCaptureAttempted)
        {
            _finalClipCaptureAttempted = true;
            if (_latestObservedKill is not { } latest
                || !IsFinalReplayFact(latest, ended)
                || !TryCaptureFinalClip(ReplayRecorder.Timeline, latest, ended,
                    out ReplayTimelineClip? clip) || clip == null)
            {
                EnterFinalGameOver("missing-or-invalid-latest-kill");
                return;
            }
            _finalClip = clip;
            _finalDecisionPending = true;
        }
        // Resolve on the following simulation boundary so terminal result
        // replication can arrive without delaying or holding scene completion.
    }

    private void TryResolveFinalDecision()
    {
        if (!_finalDecisionPending || _finalEnded is not { } ended) return;
        if (_latestObservedKill is not { } candidate
            || _finalClip is not { } clip
            || !IsFinalReplayFact(candidate, ended)
            || !TryMapKillToClip(clip, candidate, out _))
        {
            EnterFinalGameOver("candidate-or-clip-fence");
            return;
        }
        MatchResult? result = _live.World.Match.Result;
        if (result == null && ++_finalResultWaitFrames < FinalResultWaitFrames)
            return;

        _finalDecisionPending = false;
        if (result == null)
        {
            EnterFinalGameOver("result-timeout");
            return;
        }
        if (!IsFinalReplayEligible(candidate, ended, result))
        {
            EnterFinalGameOver($"result-ineligible-{result.Rules.Mode}-{result.EndReason}"
                + $"-killTick-{candidate.Tick}");
            return;
        }
        if (TryStartFinalReplay(candidate, clip))
        {
            _finalState = FinalSequenceState.Replay;
            _finalReplaySeekFrames = 0;
            DebugLog.Line("killcam/final", $"state=replay match={ended.MatchId} "
                + $"tick={candidate.Tick} kill={candidate.Id}");
            return;
        }
        EnterFinalGameOver("replay-start-failed");
    }

    /// <summary>
    /// Conservative final-replay gate. MatchEnded is only a terminal edge.
    /// Causal endings require the authoritative one-tick boundary and winner
    /// alignment; timed Battle endings may present the most recent retained
    /// enemy kill.
    /// </summary>
    internal static bool IsFinalReplayEligible(in KillEvent kill,
        in MatchEvent ended, MatchResult? result)
    {
        if (result == null || !ended.IsValid
            || ended.Kind != MatchEventKind.MatchEnded
            || !kill.IsValid || kill.MatchId != ended.MatchId
            || !IsKillAtOrBeforeEnd(kill, ended) || result.MatchId != ended.MatchId
            || !IsFinalEnemyKiller(kill) || result.ResultSlots.IsDefaultOrEmpty)
            return false;

        bool timedBattle = result.Rules.Mode is MatchMode.Battle or MatchMode.TeamBattle
            && result.EndReason == MatchEndReason.TimeLimit;
        bool causalBattle = result.Rules.Mode is MatchMode.Battle or MatchMode.TeamBattle
            && result.EndReason == MatchEndReason.ScoreGoal
            && IsCausalFinalKill(kill, ended);
        bool causalSurvival = result.Rules.Mode is MatchMode.Survival or MatchMode.TeamSurvival
            && result.EndReason == MatchEndReason.Survival
            && IsCausalFinalKill(kill, ended);
        if (!timedBattle && !causalBattle && !causalSurvival) return false;

        int winnerSlot = result.ResultSlots[0];
        if ((uint)winnerSlot >= (uint)result.Players.Length
            || !result.Players[winnerSlot].Active
            || (uint)kill.Killer.Slot >= (uint)result.Players.Length
            || (uint)kill.Victim.Slot >= (uint)result.Players.Length
            || !result.Players[kill.Killer.Slot].Active
            || !result.Players[kill.Victim.Slot].Active)
            return false;
        if (timedBattle && result.Rules.Mode == MatchMode.Battle)
            return true;
        if (timedBattle && result.Rules.Mode == MatchMode.TeamBattle)
        {
            int timedKillerTeam = result.Players[kill.Killer.Slot].TeamIndex;
            int timedVictimTeam = result.Players[kill.Victim.Slot].TeamIndex;
            return timedKillerTeam >= 0 && timedVictimTeam >= 0
                && timedKillerTeam != timedVictimTeam;
        }
        if (result.Rules.Mode is MatchMode.Battle or MatchMode.Survival)
            return winnerSlot == kill.Killer.Slot;
        if (result.Rules.Mode is not (MatchMode.TeamBattle or MatchMode.TeamSurvival))
            return false;
        int killerTeam = result.Players[kill.Killer.Slot].TeamIndex;
        int victimTeam = result.Players[kill.Victim.Slot].TeamIndex;
        int winnerTeam = result.Players[winnerSlot].TeamIndex;
        return killerTeam >= 0 && killerTeam == winnerTeam
            && victimTeam >= 0 && victimTeam != killerTeam;
    }

    internal static bool IsFinalEnemyKiller(in KillEvent kill)
        => IsValidEnemyKiller(kill)
            && kill.Killer.ConnectionId != kill.Victim.ConnectionId
            && kill.SourceKind != KillSourceKind.Environment
            && (kill.Flags & (KillEventFlags.Suicide | KillEventFlags.TeamKill)) == 0;

    /// <summary>Freezes exactly the pre-kill window ending at the accepted kill.</summary>
    internal static bool TryCaptureFinalClip(IReplayTimeline timeline,
        in KillEvent kill, in MatchEvent ended, out ReplayTimelineClip? clip)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        clip = null;
        if (!ended.IsValid || ended.Kind != MatchEventKind.MatchEnded
            || !kill.IsValid || kill.MatchId != ended.MatchId
            || !IsKillAtOrBeforeEnd(kill, ended)
            || !timeline.TryMapKillToRecordingFrame(kill, out uint killFrame))
            return false;
        uint requestedStart = killFrame > PreKillFrames
            ? killFrame - PreKillFrames : 0;
        uint actualStart = requestedStart;
        if (!timeline.TryGetRestorePoint(actualStart,
                out ReplayRestorePoint? restore) || restore == null)
        {
            if (!TryGetEarliestRestoreAtOrBefore(timeline, killFrame,
                    out restore) || restore == null)
                return false;
            actualStart = restore.RecordingFrame;
        }
        if (actualStart > killFrame
            || !timeline.TryFreeze(actualStart, killFrame,
                out ReplayTimelineClip? candidate) || candidate == null
            || !IsUsableClip(candidate, kill, killFrame))
            return false;
        clip = candidate;
        return true;
    }

    internal static bool IsFinalSequenceActive(FinalSequenceState state)
        => state != FinalSequenceState.None;

    internal static bool DoesBlockSceneCompletion(FinalSequenceState state)
        => state is FinalSequenceState.Replay or FinalSequenceState.GameOver;

    private static bool IsFinalReplayFact(in KillEvent kill,
        in MatchEvent ended)
        => ended.IsValid && ended.Kind == MatchEventKind.MatchEnded
            && kill.IsValid && kill.MatchId == ended.MatchId
            && IsKillAtOrBeforeEnd(kill, ended) && IsFinalEnemyKiller(kill);

    internal static bool IsKillAtOrBeforeEnd(in KillEvent kill,
        in MatchEvent ended)
        => kill.Tick == ended.Tick || Sequence32.IsNewer(ended.Tick, kill.Tick);

    internal static bool IsCausalFinalKill(in KillEvent kill,
        in MatchEvent ended)
        => IsKillAtOrBeforeEnd(kill, ended)
            && unchecked(ended.Tick - kill.Tick) <= FinalCausalTickLag;

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
        => SpectatorCameraMode.Chase;

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
                if (FinalSequenceActive) EnterFinalGameOver("advance-exception");
                else Stop(KillcamEndReason.Failure);
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
        if (FinalSequenceActive)
        {
            AdvanceFinalSequence();
            return;
        }
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
        if (!_replayAudioActive && !BeginReplayHandoff()) return;
        if (_kill.Killer.IsValid && !_kill.IsSuicide)
            _replay.SetFreeCamera(false);
        if (_session.AtEnd)
        {
            if (++_endFreezeFrames >= EndFreezeFrames) Stop(KillcamEndReason.Completed);
        }
    }

    private void AdvanceFinalSequence()
    {
        if (_play == null || _finalEnded is not { } ended
            || _play.Client.Accepted.MatchId != ended.MatchId
            || _play.State is AuthoritativePlay.TerminalState.Disposed
                or AuthoritativePlay.TerminalState.Transitioning)
        {
            EndFinalSequence();
            return;
        }

        if (_finalDecisionPending)
        {
            TryResolveFinalDecision();
            if (_finalDecisionPending) return;
        }

        if (_finalState == FinalSequenceState.Replay)
        {
            if (_pendingCommand == KillcamCommand.Skip)
            {
                _pendingCommand = KillcamCommand.None;
                EnterFinalGameOver("final-replay-skip");
                return;
            }
            if (_replay == null || _session == null)
            {
                EnterFinalGameOver("final-replay-missing-session");
                return;
            }
            _replay.OnSimulationFrame();
            if (_session.IsSeeking)
            {
                _state = KillcamState.Seeking;
                if (FinalReplaySeekExpired(++_finalReplaySeekFrames))
                    EnterFinalGameOver("final-replay-seek-timeout");
                return;
            }
            _finalReplaySeekFrames = 0;
            if (!TryEnsureFinalReplayFocus())
            {
                EnterFinalGameOver("final-replay-focus-unavailable");
                return;
            }
            _state = KillcamState.Presenting;
            if (_session.AtEnd) EnterFinalGameOver("final-replay-complete");
            return;
        }

        if (_finalState == FinalSequenceState.GameOver)
        {
            if (++_finalGameOverFrames < FinalGameOverFrames) return;
            _finalState = FinalSequenceState.AwaitCompletion;
            DebugLog.Line("killcam/final", "state=await-completion");
        }
    }

    private bool TryStartFinalReplay(KillEvent kill, ReplayTimelineClip clip)
    {
        try
        {
            if (!TryMapKillToClip(clip, kill, out uint killFrame))
                return false;
            _killFrame = killFrame;
            _clipStart = clip.StartRecordingFrame;
            _clipEnd = clip.EndRecordingFrame;
            _tailFramesCaptured = 0;
            var pending = new PendingKillcamCapture(kill,
                KillcamPolicy.Immediate, killFrame, clip.StartRecordingFrame,
                clip.EndRecordingFrame, clip) { ActualStart = clip.StartRecordingFrame };
            if (!Start(pending, clip, final: true)) return false;
            return ActivateReplayAudio();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-final-start", error);
            return false;
        }
    }

    private static bool TryMapKillToClip(ReplayTimelineClip clip,
        in KillEvent kill, out uint recordingFrame)
    {
        recordingFrame = 0;
        if (clip.EndRecordingFrame < clip.StartRecordingFrame) return false;
        foreach (ReplayTimelineRecord record in clip.Records)
        {
            if (!ReplayTimelineEventReader.IsExactKill(record, kill)) continue;
            recordingFrame = record.RecordingFrame;
            return true;
        }
        foreach (ReplayTimelineRecord record in clip.RestorePoint.Records)
        {
            if (!ReplayTimelineEventReader.IsExactKill(record, kill)) continue;
            recordingFrame = record.RecordingFrame;
            return true;
        }
        return false;
    }

    private bool BeginReplayHandoff()
    {
        if (!BeginLiveSuppression()) return false;
        if (_replayAudioActive) return true;
        if (!ActivateReplayAudio())
        {
            Stop(KillcamEndReason.Failure);
            return false;
        }
        return true;
    }

    private bool BeginLiveSuppression()
    {
        if (_liveSuppressionActive) return true;
        _liveInputWasSuppressed = _live.GameplayInputSuppressed;
        _liveAudioWasActive = _live.PresentationAudioActive;
        _liveMusicSnapshot = Music.CapturePresentationAudio();
        GamepadInput.BeginGameplayInputQuarantine();
        _live.SetGameplayInputSuppressed(true);
        _live.SetPresentationAudio(false);
        _live.WorldFeedback.ClearPendingNotices();
        _liveInputVersion = _live.GameplayInputSuppressionVersion;
        _liveAudioVersion = _live.PresentationAudioVersion;
        _liveSuppressionActive = true;
        return true;
    }

    private bool ActivateReplayAudio()
    {
        if (_replayAudioActive) return true;
        if (_replay == null) return false;
        try
        {
            _replay.SetPresentationAudio(true);
            _replayAudioActive = true;
            Sfx.BindPresentation(_replay.World);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.WriteLine($"[killcam] Audio handoff failed ({error.Message}); returning live");
            return false;
        }
    }

    private void EnterFinalSuppression()
    {
        _finalSuppressionActive = true;
        _liveHudWasSuppressed = _live.GameplayHudSuppressed;
        _live.SetGameplayHudSuppressed(true);
        _liveHudVersion = _live.GameplayHudSuppressionVersion;
        if (!_liveSuppressionActive)
        {
            BeginLiveSuppression();
        }
        else
        {
            // The ordinary replay was torn down synchronously just before
            // this handoff. Refresh the versions so the final sequence owns
            // the current suppression writes and can restore only itself.
            _live.SetGameplayInputSuppressed(true);
            _live.SetPresentationAudio(false);
            _liveInputVersion = _live.GameplayInputSuppressionVersion;
            _liveAudioVersion = _live.PresentationAudioVersion;
        }
    }

    internal static bool FinalReplaySeekExpired(uint waitedFrames)
        => waitedFrames >= FinalReplaySeekFrames;

    private void EnterFinalGameOver(string reason)
    {
        DebugLog.Line("killcam/final", $"state=game-over reason={reason} "
            + $"match={_finalEnded?.MatchId ?? 0} "
            + $"endedTick={_finalEnded?.Tick ?? 0} "
            + $"kill={_latestObservedKill?.Id ?? 0}");
        if (_replay != null || _session != null)
            Stop(KillcamEndReason.Completed, restoreLive: false);
        _finalState = FinalSequenceState.GameOver;
        _finalDecisionPending = false;
        _finalGameOverFrames = 0;
        _state = KillcamState.Idle;
        _live.AdditionalOverlay = DrawFinalGameOverOverlay;
    }

    private void EndFinalSequence()
    {
        if (_replay != null || _session != null)
            Stop(KillcamEndReason.MatchChanged, restoreLive: false);
        RestoreFinalSuppression();
        _finalState = FinalSequenceState.None;
        _finalDecisionPending = false;
        _finalEnded = null;
        _finalClip = null;
        _finalClipCaptureAttempted = false;
        _latestObservedKill = null;
        _live.AdditionalOverlay = null;
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
            return SetReplayFocus(_kill.Killer, SpectatorCameraMode.Chase);
        return _kill.Victim.IsValid
            && IsReplayActorAvailable(_kill.Victim)
            && SetReplayFocus(_kill.Victim, SpectatorCameraMode.Chase);
    }

    private bool TryEnsureFinalReplayFocus()
        => _replay != null && IsFinalEnemyKiller(_kill)
            && IsReplayActorAvailable(_kill.Killer)
            && SetReplayFocus(_kill.Killer, SpectatorCameraMode.Chase);

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

    private bool Start(in PendingKillcamCapture pending, ReplayTimelineClip clip,
        bool final = false)
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
                static _ => { }, () => Stop(KillcamEndReason.SceneClosing,
                    restoreLive: !final),
                _live.Timing, session.SceneServices, session);
            session.BuildPlayers(scene);
            MatchRules? rules = session.InitialRules;
            if (rules == null) return false;
            presentation.AddRoom(rules.RoomKey, rules.Mode.ToLegacyMode(),
                playerCount: session.Modern.Roster.Length);
            presentation.SpectatorCamera.ConfigureKillcam(focus, focusMode);
            presentation.AdditionalOverlay = final ? DrawFinalReplayOverlay : DrawOverlay;
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
        hud.DrawText2D(242, 8, Align.Right, 0, "FIRE TO SKIP", scale: .5f);
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

    private void DrawFinalReplayOverlay(ScenePresentation presentation)
    {
        DrawOverlay(presentation);
        if (presentation.World.LocalPlayer is { } localPlayer)
            localPlayer.GetPresentation().DrawText2D(128, 196, Align.Center, 0,
                "FINAL ELIMINATION", maxLength: 32, scale: .48f);
    }

    private void DrawFinalGameOverOverlay(ScenePresentation presentation)
    {
        if (presentation.World.LocalPlayer is not { } localPlayer) return;
        PlayerPresentation hud = localPlayer.GetPresentation();
        presentation.DrawHudFlatBox(20, 124, 236, 198,
            new Vector4(0, 0, 0, .78f));
        hud.DrawText2D(128, 136, Align.Center, 0, "GAME OVER",
            maxLength: 32, scale: .86f);
        string winner = GetFinalWinnerLabel(presentation.World.Match.Result);
        if (winner != "GAME OVER")
            hud.DrawText2D(128, 165, Align.Center, 0, winner,
                maxLength: 36, scale: .58f);
    }

    internal static string GetFinalWinnerLabel(MatchResult? result)
    {
        if (result == null || result.ResultSlots.IsDefaultOrEmpty
            || (uint)result.ResultSlots[0] >= (uint)result.Players.Length)
            return "GAME OVER";
        int winnerSlot = result.ResultSlots[0];
        if (result.Rules.Mode is MatchMode.Battle or MatchMode.Survival)
        {
            string nickname = result.Players[winnerSlot].Nickname;
            return String.IsNullOrWhiteSpace(nickname)
                ? "GAME OVER" : $"WINNER: {nickname}";
        }
        int team = result.Players[winnerSlot].TeamIndex;
        return (uint)team < 8 ? $"WINNER: TEAM {team + 1}" : "GAME OVER";
    }

    private void Stop(KillcamEndReason reason, bool restoreLive = true)
    {
        ScenePresentation? presentation = _replay;
        ReplayPlaybackSession? session = _session;
        bool ownedPresentation = presentation != null || session != null
            || _replayAudioActive || _liveSuppressionActive
            || _pending != null || _pendingClip != null
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
            try
            {
                presentation?.SetPresentationAudio(false);
            }
            catch (Exception error)
            {
                DebugLog.Exception("killcam-replay-audio", error);
            }
        }
        if (restoreLive && !_finalSuppressionActive)
            RestoreLiveSuppression();
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

    private void RestoreFinalSuppression()
    {
        if (!_finalSuppressionActive) return;
        _finalSuppressionActive = false;
        RestoreLiveSuppression();
        try
        {
            _live.TryRestoreGameplayHudSuppressed(_liveHudWasSuppressed,
                _liveHudVersion);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-final-hud-restore", error);
        }
    }

    private void RestoreLiveSuppression()
    {
        if (!_liveSuppressionActive) return;
        _liveSuppressionActive = false;
        bool audioRestored = false;
        try
        {
            GamepadInput.EndGameplayInputQuarantine();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-input-quarantine", error);
        }
        try
        {
            _live.TryRestoreGameplayInputSuppressed(_liveInputWasSuppressed,
                _liveInputVersion);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-input-restore", error);
        }
        try
        {
            // The live simulation keeps accepting authoritative events while
            // the isolated replay owns drawing. Discard those presentation
            // notices before audio is restored so they cannot burst afterward.
            _live.WorldFeedback.ClearPendingNotices();
            audioRestored = _live.TryRestorePresentationAudio(_liveAudioWasActive,
                _liveAudioVersion);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DebugLog.Exception("killcam-audio-restore", error);
        }
        _replayAudioActive = false;
        if (audioRestored)
        {
            try
            {
                Sfx.BindPresentation(_live.World);
                if (!Music.TryRestorePresentationAudio(_liveMusicSnapshot))
                    Music.TryPlayRoomMusic(_live.World.RoomId, 0);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                DebugLog.Exception("killcam-final-audio-bind", error);
            }
        }
    }

    public void Dispose()
    {
        if (_play != null)
        {
            _play.LocalPlayerKilled -= OnLocalPlayerKilled;
            _play.AuthoritativeKillObserved -= OnAuthoritativeKillObserved;
            _play.AuthoritativeMatchEndedAccepted -= OnAuthoritativeMatchEndedAccepted;
        }
        if (FinalSequenceActive)
        {
            EndFinalSequence();
        }
        else
        {
            Stop(KillcamEndReason.SceneClosing);
        }
        _play = null;
    }
}
