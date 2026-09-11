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

/// <summary>
/// Owns the short-lived replay scene used for an authoritative local death.
/// The live presentation is never rewound and continues to advance while this
/// controller warms and presents its immutable clip.
/// </summary>
internal sealed class KillcamController : IDisposable
{
    internal const uint LeadFrames = 300;
    internal const uint FreezeFrames = 15;

    private readonly ScenePresentation _live;
    private readonly Func<Vector2i> _sizeProvider;
    private readonly KeyboardState _keyboard;
    private readonly MouseState _mouse;
    private AuthoritativePlay? _play;
    private PendingKillcam? _pending;
    private ReplayPlaybackSession? _session;
    private ScenePresentation? _replay;
    private KillEvent _kill;
    private KillcamPolicy _policy;
    private uint _freezeFrames;
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
        KeyboardState keyboard, MouseState mouse)
    {
        _live = live;
        _sizeProvider = sizeProvider ?? throw new ArgumentNullException(nameof(sizeProvider));
        _keyboard = keyboard;
        _mouse = mouse;
        Bind(AuthoritativePlay.Current);
    }

    internal bool IsPresenting => _replay != null && _session is { IsSeeking: false };
    internal bool IsActive => _replay != null || _session != null;
    internal ScenePresentation RenderedPresentation => IsPresenting ? _replay! : _live;
    internal KillcamEndReason? LastEndReason { get; private set; }

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
        foreach (WindowKeyEvent key in input.KeyEvents)
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
        if (_last == (kill.MatchId, kill.Id, kill.Victim)) return;
        _last = (kill.MatchId, kill.Id, kill.Victim);
        KillcamPolicy policy = Mods.GameSettings.ResolveKillcamPolicy(
            _play?.Client.Accepted.Rules.KillcamPolicy ?? KillcamPolicy.Disabled);
        if (policy == KillcamPolicy.Disabled) return;
        if (!TryCaptureClip(ReplayRecorder.Timeline, kill,
                out ReplayTimelineClip? clip) || clip == null) return;
        _pending = new PendingKillcam(kill, clip, policy);
    }

    internal static bool TryCaptureClip(IReplayTimeline timeline, uint killTick,
        out ReplayTimelineClip? clip)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        clip = null;
        if (!timeline.TryMapServerTickToRecordingFrame(killTick, out uint killFrame))
            return false;
        uint startFrame = killFrame > LeadFrames ? killFrame - LeadFrames : 0;
        return timeline.TryFreeze(startFrame, killFrame, out clip) && clip != null;
    }

    internal static bool TryCaptureClip(IReplayTimeline timeline,
        in KillEvent kill, out ReplayTimelineClip? clip)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        clip = null;
        if (!timeline.TryMapKillToRecordingFrame(kill, out uint killFrame))
            return false;
        uint startFrame = killFrame > LeadFrames ? killFrame - LeadFrames : 0;
        return timeline.TryFreeze(startFrame, killFrame, out clip) && clip != null;
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
        Bind(AuthoritativePlay.Current);
        if (_replay != null && _pendingCommand == KillcamCommand.Skip)
        {
            _pendingCommand = KillcamCommand.None;
            Stop(KillcamEndReason.Skipped);
            return;
        }

        if (_replay == null && _pending is PendingKillcam pending)
        {
            if (_play == null || _play.Client.Accepted.MatchId != pending.Kill.MatchId)
            {
                _pending = null;
                return;
            }
            bool ready = pending.Policy == KillcamPolicy.Immediate
                || _live.World.Match.Phase is MatchPhase.Ending or MatchPhase.Intermission;
            if (ready)
            {
                _pending = null;
                if (pending.Policy == KillcamPolicy.Immediate
                    && _play?.KillcamLiveContextChanged(pending.Kill) != false) return;
                Start(pending);
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
        if (_session.IsSeeking) return;
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
            if (++_freezeFrames >= FreezeFrames) Stop(KillcamEndReason.Completed);
        }
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

    private void Start(in PendingKillcam pending)
    {
        var session = new ReplayPlaybackSession();
        if (!session.Join(pending.Clip)) { session.Dispose(); return; }
        ScenePresentation? presentation = null;
        try
        {
            _kill = pending.Kill;
            _policy = pending.Policy;
            int focus = _kill.Killer.IsValid && !_kill.IsSuicide
                ? _kill.Killer.Slot : _kill.Victim.Slot;
            session.SetPerspective(focus);
            var scene = new Scene(features: ClientMatchFeatures.Capture())
            {
                Services = session.SceneServices
            };
            presentation = new ScenePresentation(scene, ResolveSize(), _keyboard, _mouse,
                static _ => { }, () => Stop(KillcamEndReason.SceneClosing),
                session.SceneServices, session);
            session.BuildPlayers(scene);
            MatchRules? rules = session.InitialRules;
            if (rules == null) return;
            presentation.AddRoom(rules.RoomKey, rules.Mode.ToLegacyMode(),
                playerCount: session.Modern.Roster.Length);
            presentation.SpectatorCamera.ConfigureKillcam(focus,
                _kill.Killer.IsValid && !_kill.IsSuicide
                    ? SpectatorCameraMode.FirstPerson : SpectatorCameraMode.Chase);
            presentation.AdditionalOverlay = DrawOverlay;
            presentation.OnLoad();
            session.Seek(pending.Clip.StartRecordingFrame);
            _session = session;
            _replay = presentation;
            _freezeFrames = 0;
            presentation = null;
            session = null!;
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
        if (presentation.World.Players.Count == 0) return;
        PlayerPresentation hud = presentation.World.LocalPlayer!.GetPresentation();
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
    }

    private void Stop(KillcamEndReason reason)
    {
        ScenePresentation? presentation = _replay;
        ReplayPlaybackSession? session = _session;
        bool ownedPresentation = presentation != null || session != null
            || _replayAudioActive || _pending != null;
        _replay = null;
        _session = null;
        _freezeFrames = 0;
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
        _pending = null;
        Stop(KillcamEndReason.SceneClosing);
    }

    private readonly record struct PendingKillcam(KillEvent Kill,
        ReplayTimelineClip Clip, KillcamPolicy Policy);
}
