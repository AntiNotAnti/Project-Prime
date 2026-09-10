using System;
using MphRead.Entities;
using MphRead.Hud;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead;

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
    private readonly Vector2i _size;
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
    private int _held;
    private (uint Match, uint Event, CombatActor Victim) _last;

    internal KillcamController(ScenePresentation live, Vector2i size,
        KeyboardState keyboard, MouseState mouse)
    {
        _live = live;
        _size = size;
        _keyboard = keyboard;
        _mouse = mouse;
        Bind(AuthoritativePlay.Current);
    }

    internal bool IsPresenting => _replay != null && _session is { IsSeeking: false };
    internal ScenePresentation RenderedPresentation => IsPresenting ? _replay! : _live;

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
        if (!TryCaptureClip(ReplayRecorder.Timeline, kill.Tick,
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

    internal void Advance()
    {
        Bind(AuthoritativePlay.Current);
        int keys = (_keyboard.IsKeyDown(Keys.Space) ? 1 : 0)
            | (_keyboard.IsKeyDown(Keys.Escape) ? 2 : 0);
        int pressed = keys & ~_held;
        _held = keys;
        if (_replay != null && pressed != 0) { Stop(); return; }

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
        { Stop(); return; }

        _replay.OnSimulationFrame();
        if (_session.IsSeeking) return;
        if (!_replayAudioActive)
        {
            _live.SetGameplayInputSuppressed(true);
            _live.SetPresentationAudio(false);
            _replay.SetPresentationAudio(true);
            _replayAudioActive = true;
            try
            {
                Sfx.BindPresentation(_replay.World);
            }
            catch (Exception error)
            {
                Console.WriteLine($"[killcam] Audio handoff failed ({error.Message}); returning live");
                Stop();
                return;
            }
        }
        if (_kill.Killer.IsValid && !_kill.IsSuicide)
            _replay.SetFreeCamera(false);
        if (_session.AtEnd)
        {
            if (++_freezeFrames >= FreezeFrames) Stop();
        }
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
            presentation = new ScenePresentation(scene, _size, _keyboard, _mouse,
                static _ => { }, Stop, session.SceneServices, session);
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
            presentation?.DoCleanup(preserveSharedAudio: true);
            session?.Dispose();
        }
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

    private void Stop()
    {
        ScenePresentation? presentation = _replay;
        ReplayPlaybackSession? session = _session;
        _replay = null;
        _session = null;
        _freezeFrames = 0;
        if (_replayAudioActive)
        {
            presentation?.SetPresentationAudio(false);
            _live.SetGameplayInputSuppressed(false);
            _live.SetPresentationAudio(true);
            _replayAudioActive = false;
            try
            {
                Sfx.BindPresentation(_live.World);
                Music.TryPlayRoomMusic(_live.World.RoomId, 0);
            }
            catch (Exception error)
            {
                Console.WriteLine($"[killcam] Live audio restore failed ({error.Message})");
            }
        }
        presentation?.DoCleanup(preserveSharedAudio: true);
        session?.Dispose();
    }

    public void Dispose()
    {
        if (_play != null) _play.LocalPlayerKilled -= OnLocalPlayerKilled;
        _play = null;
        _pending = null;
        Stop();
    }

    private readonly record struct PendingKillcam(KillEvent Kill,
        ReplayTimelineClip Clip, KillcamPolicy Policy);
}
