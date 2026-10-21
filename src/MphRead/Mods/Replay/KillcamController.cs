using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal enum FinalImpactStyle { Energy, Precision, Electrical, Explosive, Cryo, Thermal }

/// <summary>Bounded, deterministic final-kill presentation policy. Never changes
/// the authoritative match clock, shot receipts, or historical simulation.</summary>
internal static class FinalKillPresentation
{
    // Trim ten source frames to pay for nine half-speed frames. The camera
    // therefore still finishes inside the existing five-second intermission.
    internal const uint PreRollFrames = 205;
    internal const uint PostRollFrames = 85;
    internal const int SlowBefore = 5;
    internal const int SlowAfter = 4;
    internal const int FadeFrames = 10;

    internal static float Rate(uint frame, uint killFrame, bool reduceMotion)
        => !reduceMotion && (ulong)frame + SlowBefore >= killFrame
            && (ulong)frame < (ulong)killFrame + SlowAfter ? 0.5f : 1f;

    internal static FinalImpactStyle Impact(byte weapon)
        => (BeamType)unchecked((sbyte)weapon) switch
        {
            BeamType.Imperialist => FinalImpactStyle.Precision,
            BeamType.VoltDriver or BeamType.ShockCoil => FinalImpactStyle.Electrical,
            BeamType.Battlehammer or BeamType.Missile => FinalImpactStyle.Explosive,
            BeamType.Judicator => FinalImpactStyle.Cryo,
            BeamType.Magmaul => FinalImpactStyle.Thermal,
            _ => FinalImpactStyle.Energy
        };

    internal static float ImpactStrength(double presentedFrame, uint killFrame, bool reduceMotion)
    {
        if (reduceMotion || !double.IsFinite(presentedFrame)) return 0f;
        double since = presentedFrame - killFrame;
        if (since < 0 || since >= 18) return 0f;
        float remaining = 1f - (float)since / 18f;
        return remaining * remaining;
    }

    internal static float TransitionCover(int frame, int total)
    {
        float incoming = Math.Clamp(1f - frame / (float)FadeFrames, 0f, 1f);
        float outgoing = Math.Clamp((frame - total + FadeFrames) / (float)FadeFrames, 0f, 1f);
        return MathF.Max(incoming, outgoing);
    }
}

internal enum KillcamCameraMode { Chase, FirstPerson, Cinematic }
internal enum KillcamState { None, Preparing, Replay, AwaitCompletion }
internal enum KillcamEndReason { None, Completed, Skipped, Respawn, MatchChanged, Disconnected, Disabled, SceneClosed, InvalidIdentity, Unavailable, Failed }
internal readonly record struct KillcamContext(ushort MatchId, ulong Epoch, uint Frame, int LocalSlot,
    ushort LocalGeneration, ushort LocalLife, bool LocalAlive, bool Connected, bool PersonalEnabled, bool FinalEnabled);

/// <summary>Timeline and private-scene lifetime for personal/final presentation.
/// The caller keeps live simulation running and supplies authoritative lifecycle state.</summary>
internal sealed class KillcamController : IDisposable
{
    private readonly record struct KillcamCameraSnapshot(Vector3 Position, Vector3 Target,
        Vector3 Facing, float Fov, uint Frame);
    internal const uint PreRollFrames = 210;
    internal const uint PostRollFrames = 90;
    internal const uint PersonalReplayFrames = PreRollFrames + PostRollFrames;
    internal const uint FinalReplayFrames = 5 * 60;
    internal const float PlaybackRate = 1f;
    private readonly IReplayTimeline _timeline;
    private readonly Func<ReplayTimelineClip, Vector2i, PassiveReplayPlayer> _open;
    private readonly Func<IReadOnlyCollection<MatchSemanticEventPacket>>? _acceptedDeaths;
    private readonly int _sourceProtocolVersion;
    internal ReplayKillLifeStatus IdentityResolution { get; private set; } = ReplayKillLifeStatus.Unknown;
    internal uint IdentityFactEventId { get; private set; }
    private PassiveReplayPlayer? _player;
    private ReplayMarker? _candidate, _pending, _playing;
    private uint _candidateFrame, _pendingFrame, _start, _end;
    private ReplayTimelineClip? _playingClip;
    private uint _killFrame;
    internal int CheckpointCount => _player?.CheckpointCount ?? 0;
    internal long CheckpointBytes => _player?.CheckpointBytes ?? 0;
    private int _hold;
    private bool _skipArmed;
    private ulong _audio;
    private Scene? _live;
    private KillcamHud? _hud;
    private readonly Stopwatch _startup = new();
    private KillcamCameraSnapshot? _lastKillerCamera;
    private KillcamCameraSnapshot? _handoffCamera;
    private int _finalTicks, _finalLifetimeTicks;
    internal int FinalPresentationTicks => _finalTicks;
    private string? _killerName, _customWeaponName;
    internal double StartupMilliseconds { get; private set; }
    internal long ClipBytes { get; private set; }
    internal KillcamState State { get; private set; }
    internal KillcamEndReason EndReason { get; private set; }
    internal KillCamKind Kind { get; private set; }
    internal string? LastError { get; private set; }
    internal bool Active => _player != null;
    internal bool Visible => _player?.Ready == true && State is KillcamState.Replay or KillcamState.AwaitCompletion;
    internal Scene? Presentation => Visible ? _player!.Current.Scene : null;
    internal uint Frame => _player?.Current.Session.CurrentFrame ?? 0;
    internal float Progress => !Active || _end <= _start ? 1 : Math.Clamp((Frame - _start) / (float)(_end - _start), 0, 1);
    internal ReplayMarker? Playing => _playing;
    internal ReplayMarker? Candidate => _candidate;
    internal uint CandidateFrame => _candidateFrame;
    internal uint PlayingFrame => _killFrame;
    internal string AuthorityEndCause { get; private set; } = "unknown";
    internal static bool SameKillIdentity(ReplayKillIdentity? left, ReplayKillIdentity? right)
    {
        if (left is not { } a || right is not { } b) return false;
        return a.MatchId == b.MatchId && a.AuthorityEpoch == b.AuthorityEpoch
            && a.ServerTick == b.ServerTick && a.EventId == b.EventId
            && a.KillerSlot == b.KillerSlot && a.KillerGeneration == b.KillerGeneration
            && (a.KillerLifeId == 0 || b.KillerLifeId == 0 || a.KillerLifeId == b.KillerLifeId)
            && a.VictimSlot == b.VictimSlot && a.VictimGeneration == b.VictimGeneration
            && a.VictimLifeId == b.VictimLifeId;
    }
    internal static int ResolveAttackerSlot(ReplayKillIdentity kill, ReplayReplicaState state)
        => kill.KillerSlot < PlayerEntity.SlotCapacity
            && kill.KillerGeneration != 0 && state.TryGetPlayer(kill.KillerSlot, out var actor)
            && actor.SlotGeneration == kill.KillerGeneration
            && (actor.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned))
                == (PlayerState.FlagActive | PlayerState.FlagSpawned)
            && state.Occupant(kill.KillerSlot).Generation == kill.KillerGeneration
            && (kill.KillerLifeId == 0 || state.MatchesLife(kill.KillerSlot,
                kill.KillerGeneration, kill.KillerLifeId)) ? kill.KillerSlot : -1;
    internal PassiveReplayScene? Replica => _player?.Current;

    internal KillcamController(IReplayTimeline timeline, Func<ReplayTimelineClip, Vector2i, PassiveReplayPlayer>? open = null,
        Func<IReadOnlyCollection<MatchSemanticEventPacket>>? acceptedDeaths = null, int sourceProtocolVersion = 0)
    {
        _timeline = timeline; _open = open ?? ((clip, size) => new PassiveReplayPlayer(clip, size, ReplayPlayerOptions.Linear));
        _acceptedDeaths = acceptedDeaths; _sourceProtocolVersion = sourceProtocolVersion;
    }

    private bool PrepareIdentity(ReplayMarker marker, out ReplayMarker prepared, int clipProtocolVersion = 0)
    {
        prepared = marker; IdentityFactEventId = 0;
        if (Math.Max(_sourceProtocolVersion, clipProtocolVersion) < 79)
        { IdentityResolution = ReplayKillLifeStatus.LegacyUnqualified; return true; }
        if (marker.Kill is not { } kill) { IdentityResolution = ReplayKillLifeStatus.Unknown; return false; }
        var result = ReplayKillLifeResolver.Resolve(kill, _acceptedDeaths?.Invoke() ?? Array.Empty<MatchSemanticEventPacket>());
        IdentityResolution = result.Status; IdentityFactEventId = result.FactEventId;
        if (!result.Known) return false;
        prepared = marker with { Kill = result.Identity }; return true;
    }

    internal void NoteKill(ReplayMarker marker, uint frame, KillcamContext context, bool enemy = true)
    {
        if (marker.Kill is not { } kill || !IsValidIdentity(kill, context)) return;
        // Accepted marker mapping uses the recording clock, never a delayed server clock.
        if (_timeline.TryMapKillToRecordingFrame(kill, out uint mapped)) frame = mapped;
        if (enemy) { _candidate = marker; _candidateFrame = frame; }
        if (context.PersonalEnabled && context.LocalSlot == kill.VictimSlot
            && context.LocalGeneration == kill.VictimGeneration && context.LocalLife == kill.VictimLifeId)
        { _pending = marker; _pendingFrame = frame; }
    }

    internal void Update(Scene live, KillcamContext context)
    {
        try
        {
            if (!context.Connected) { Reset(KillcamEndReason.Disconnected); return; }
            if (_candidate?.Kill is { } candidate && !Matches(candidate, context))
            { Reset(KillcamEndReason.MatchChanged); return; }
            if (_pending?.Kill is { } pending)
            {
                if (!Matches(pending, context)) { _pending = null; EndReason = KillcamEndReason.MatchChanged; }
                else if (!context.PersonalEnabled) { _pending = null; EndReason = KillcamEndReason.Disabled; }
                else if (context.LocalSlot != pending.VictimSlot || context.LocalGeneration != pending.VictimGeneration
                    || context.LocalLife != pending.VictimLifeId || context.LocalAlive)
                { _pending = null; EndReason = KillcamEndReason.Respawn; }
                else if ((ulong)context.Frame > (ulong)_pendingFrame + PostRollFrames + 60)
                { _pending = null; EndReason = KillcamEndReason.Unavailable; }
                else
                {
                    // Do not mutate a pending identity until its original range
                    // can start. Both packet delivery orders consult the same
                    // retained authority facts; an active camera is never retargeted.
                    if (PrepareIdentity(_pending.Value, out var prepared))
                    {
                        var clip = Freeze(_pendingFrame);
                        if (clip != null)
                        {
                            // A direct clip caller may have had no protocol at
                            // NoteKill. Its frozen source contract is authoritative.
                            if (!PrepareIdentity(_pending.Value, out prepared, clip.PresentationProtocolVersion)) clip.Dispose();
                            else
                            {
                                if (_candidate == _pending) _candidate = prepared;
                                Start(live, clip, prepared, KillCamKind.Personal, _pendingFrame); _pending = null;
                            }
                        }
                    }
                }
            }
            if (_player == null || _playing?.Kill is not { } playing) return;
            if (!Matches(playing, context)) { Stop(KillcamEndReason.MatchChanged); return; }
            if (Kind == KillCamKind.Personal)
            {
                if (!context.PersonalEnabled) { Stop(KillcamEndReason.Disabled); return; }
                if (context.LocalSlot != playing.VictimSlot || context.LocalGeneration != playing.VictimGeneration
                    || context.LocalLife != playing.VictimLifeId || context.LocalAlive)
                { Stop(KillcamEndReason.Respawn); return; }
            }
            else if (!context.FinalEnabled) { Stop(KillcamEndReason.Disabled); return; }
            if (Kind == KillCamKind.Final)
            {
                // Linear playback: schedule whole historical 60 Hz steps at
                // half rate around the *accepted kill frame*, never by scaling
                // the live simulation or modifying recorded hit positions.
                if (++_finalLifetimeTicks > (int)FinalReplayFrames + 30)
                { Stop(KillcamEndReason.Unavailable); return; }
                bool reduceMotion = Launcher.LauncherPrefs.ReduceMotion
                    || Render.Hud.HudProfiles.Runtime.ReduceMotion;
                float rate = FinalKillPresentation.Rate(Frame, _killFrame, reduceMotion);
                if (_player.Transport.PlaybackRate != rate)
                    _player.Transport.SetPlaybackRate(rate);
            }
            _player.Update();
            if (!_player.Ready) { State = KillcamState.Preparing; return; }
            if (_startup.IsRunning) { _startup.Stop(); StartupMilliseconds = _startup.Elapsed.TotalMilliseconds; }
            if (_audio == 0)
                _audio = ReplayAudioOwner.Acquire(_player.Current.Scene, live);
            if (Kind == KillCamKind.Final) _finalTicks++;
            bool finished = Kind == KillCamKind.Final
                ? _finalTicks >= (int)FinalReplayFrames : _player.Current.Session.AtEnd;
            State = finished ? KillcamState.AwaitCompletion : KillcamState.Replay;
            if (State == KillcamState.AwaitCompletion && Kind == KillCamKind.Personal && ++_hold >= 15)
            { Stop(KillcamEndReason.Completed); return; }
            Camera(live.Size);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { LastError = ex.Message; _pending = null; Stop(KillcamEndReason.Failed); }
    }

    private ReplayTimelineClip? Freeze(uint death, uint preRoll = PreRollFrames, uint postRoll = PostRollFrames)
    {
        if (_timeline.FirstRecordingFrame is not uint first || _timeline.LastRecordingFrame is not uint last
            || first > death || (ulong)death + postRoll > last) return null;
        uint start = Math.Max(first, death > preRoll ? death - preRoll : 0);
        if (!_timeline.TryFreeze(start, death + postRoll, out var clip)) return null;
        if (clip?.RestorePoint.Kind == ReplayRestoreKind.ReplicaCheckpoint) return clip;
        clip?.Dispose(); return null;
    }

    internal bool BeginFinal(Scene live, KillcamContext context, uint endFrame, bool timedEnd, bool causalEnd)
        => BeginFinal(live, context, endFrame, timedEnd, causalEnd, out _);

    internal bool BeginFinal(Scene live, KillcamContext context, uint endFrame, bool timedEnd, bool causalEnd,
        out bool missingIdentity)
    {
        missingIdentity = false;
        AuthorityEndCause = causalEnd ? "kill" : timedEnd ? "time" : "non-causal";
        Stop(KillcamEndReason.None); _pending = null;
        if (!context.FinalEnabled || _candidate is not { Kill: { } kill } marker || !Matches(kill, context)
            || !FinalEligible(kill.ServerTick, endFrame, timedEnd, causalEnd)) return false;
        if (!PrepareIdentity(marker, out var prepared))
        { missingIdentity = true; EndReason = KillcamEndReason.Unavailable; return false; }
        var clip = Freeze(_candidateFrame, FinalKillPresentation.PreRollFrames,
            FinalKillPresentation.PostRollFrames);
        if (clip == null) { EndReason = KillcamEndReason.Unavailable; return false; }
        if (!PrepareIdentity(marker, out prepared, clip.PresentationProtocolVersion))
        { clip.Dispose(); missingIdentity = true; EndReason = KillcamEndReason.Unavailable; return false; }
        try { _candidate = prepared; Start(live, clip, prepared, KillCamKind.Final, _candidateFrame); return true; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { LastError = ex.Message; Stop(KillcamEndReason.Failed); return false; }
    }
    internal static bool FinalEligible(uint killFrame, uint endFrame, bool timedEnd, bool causalEnd)
        => endFrame >= killFrame && (causalEnd && endFrame - killFrame <= 120 || timedEnd && endFrame - killFrame <= 480);

    private void Start(Scene live, ReplayTimelineClip clip, ReplayMarker marker, KillCamKind kind, uint killFrame)
    {
        Stop(KillcamEndReason.None);
        _startup.Restart(); StartupMilliseconds = 0;
        ClipBytes = clip.RestorePoint.PayloadBytes + clip.Records.Sum(r => r.PayloadBytes);
        _playingClip = clip; // Transfer the single frozen lease to this playback.
        _killFrame = killFrame;
        _player = _open(_playingClip, live.Size); _playing = marker; Kind = kind; _live = live;
        _customWeaponName = null;
        _player.Current.Scene.ReplayPresentationHud = DrawHud;
        _start = clip.StartRecordingFrame; _end = clip.EndRecordingFrame; _hold = 0; _skipArmed = false;
        _finalTicks = _finalLifetimeTicks = 0;
        _handoffCamera = null;
        if (kind == KillCamKind.Final && live.Players.Items.Count > 0)
        {
            CameraInfo incoming = live.Players.Main.CameraInfo;
            if (float.IsFinite(incoming.Position.X) && float.IsFinite(incoming.Position.Y)
                && float.IsFinite(incoming.Position.Z) && float.IsFinite(incoming.Target.X)
                && float.IsFinite(incoming.Target.Y) && float.IsFinite(incoming.Target.Z)
                && float.IsFinite(incoming.Fov))
                _handoffCamera = new(incoming.Position, incoming.Target, incoming.Facing, incoming.Fov, 0);
        }
        _player.Transport.SetPlaybackRate(PlaybackRate);
        State = KillcamState.Preparing; EndReason = KillcamEndReason.None; LastError = null;
    }

    internal bool Input(bool down, bool pressed)
    {
        if (!Active && _pending == null) return false;
        if (!down) _skipArmed = true;
        if (_skipArmed && pressed) { _pending = null; Stop(KillcamEndReason.Skipped); }
        return true;
    }
    internal bool Skip()
    {
        if (!Active && _pending == null) return false;
        _pending = null; Stop(KillcamEndReason.Skipped); return true;
    }
    internal void Camera(Vector2i size)
    {
        if (!Visible || _playing?.Kill is not { } kill) return;
        var world = _player!.Current;
        Scene scene = world.Scene;
        double hostAlpha = Render.FrameTiming.Active ? Render.FrameTiming.PresentationAlpha : 1;
        scene.ReplayRenderAlpha = Render.FrameTiming.Active
            ? _player.Transport.PresentationAlpha(hostAlpha) : 1;
        scene.ReplayPresentationFrame = Render.FrameTiming.Active
            ? _player.Transport.PresentationFrame(hostAlpha) : Frame;
        if (scene.Size != size) { scene.Size = size; scene.OnResize(); }
        int slot = ResolveAttackerSlot(kill, world.State);
        if (slot < 0)
        {
            if (_lastKillerCamera is { } saved && Frame >= saved.Frame && Frame - saved.Frame <= 15)
            {
                scene.SetReplicaCamera(saved.Position, saved.Target, saved.Fov);
                return;
            }
            PlayerEntity? victim = world.State.Occupant(kill.VictimSlot).Generation == kill.VictimGeneration
                && (kill.VictimLifeId == 0 || world.State.MatchesLife(kill.VictimSlot,
                    kill.VictimGeneration, kill.VictimLifeId)) ? scene.Players.Items[kill.VictimSlot] : null;
            if (victim != null)
            {
                Vector3 target = victim.ReplayDrawTransform.Row3.Xyz + Vector3.UnitY * 1.2f;
                scene.SetReplicaCamera(target + new Vector3(4, 3, 4), target, 82);
            }
            else
            {
                scene.SetReplicaCamera(new Vector3(0, 3, 4), new Vector3(0, 1, 0), 82);
            }
            return;
        }
        PlayerEntity actor = scene.Players.Items[slot];
        _killerName ??= scene.GameState.Nicknames[kill.KillerSlot];
        Vector3 facing = actor.CameraInfo.Facing.LengthSquared > .0001f ? actor.CameraInfo.Facing : actor.FacingVector;
        if (scene.ReplayPoses?.SamplePresented(actor.SlotIndex, scene.ReplayRenderAlpha,
            out _, out var replicaFacing) == true)
            facing = replicaFacing;
        if (facing.LengthSquared < .0001f) facing = Vector3.UnitZ;
        facing = facing.Normalized();
        Vector3 focus = actor.ReplayDrawTransform.Row3.Xyz + Vector3.UnitY * (actor.IsAltForm ? .6f : 1.2f);
        Vector3 right = Vector3.Cross(facing, Vector3.UnitY);
        if (right.LengthSquared < .0001f) right = Vector3.UnitX;
        var mode = (KillcamCameraMode)Math.Clamp(Launcher.LauncherPrefs.KillCamCamera, 0, 2);
        if (world.State.CustomWeapons != null)
        {
            if (mode == KillcamCameraMode.FirstPerson && scene.SetCustomInspectionFirstPerson(slot)) return;
            scene.ClearCustomInspectionFirstPerson();
        }
        if (mode == KillcamCameraMode.FirstPerson && !actor.IsAltForm && actor.CameraInfo.Facing.LengthSquared > .0001f)
        {
            var eye = actor.CameraInfo.ModGetDrawPosition(scene.ReplayRenderAlpha)
                + actor.ReplayDrawTransform.Row3.Xyz - actor.SimulationDrawPosition;
            Vector3 target = eye + facing * 5;
            float fov = actor.CameraInfo.ModGetDrawFov(scene.ReplayRenderAlpha);
            if (!float.IsFinite(fov) || fov <= 0) fov = 82;
            fov = Math.Clamp(fov, 1, 175);
            scene.SetReplicaCamera(eye, target, fov);
            _lastKillerCamera = new(eye, target, facing, fov, Frame);
            return;
        }
        double relativeFrame = scene.ReplayPresentationFrame - _killFrame;
        float distance = 3.2f;
        if (mode == KillcamCameraMode.Cinematic)
        {
            if (relativeFrame < -15) distance = 4.6f;
            else if (relativeFrame <= 15) distance = 2.7f;
            else distance = 2.7f + (float)Math.Clamp((relativeFrame - 15) / 60, 0, 1) * 1.5f;
            float orbit = (float)Math.Clamp(relativeFrame / 75, 0, 1) * .55f;
            if (orbit != 0)
            {
                facing = Vector3.Transform(facing, Quaternion.FromAxisAngle(Vector3.UnitY, orbit));
                right = Vector3.Cross(facing, Vector3.UnitY);
                if (right.LengthSquared < .0001f) right = Vector3.UnitX;
            }
        }
        Vector3 camera = focus - facing * distance + right.Normalized() * .9f + Vector3.UnitY * .8f;
        CollisionResult collision = default;
        if (CollisionDetection.CheckBetweenPoints(focus, camera, TestFlags.Players, scene, ref collision))
            camera = focus + (camera - focus) * Math.Max(.05f, collision.Distance - .05f);
        Vector3 cameraTarget = focus + facing * 5;
        scene.SetReplicaCamera(camera, cameraTarget, 82);
        _lastKillerCamera = new(camera, cameraTarget, facing, 82, Frame);
    }
    internal void DrawHud(Scene scene)
    {
        if (!ReferenceEquals(scene, Presentation) || _playing is not { Kill: { } kill } marker) return;
        _hud ??= new KillcamHud(scene);
        string killer = PlayerNameCodec.Clamp(_killerName ?? "ATTACKER");
        string victim = PlayerNameCodec.Clamp(scene.GameState.Nicknames[kill.VictimSlot]);
        string weapon = KillCam.WeaponName(marker.Weapon);
        if (scene.Services is ReplaySceneServices customServices && customServices.State.CustomWeapons is { } custom)
        {
            if (custom.TryKillWeaponName(kill, out var name)) _customWeaponName = name;
            if (_customWeaponName != null) weapon = _customWeaponName;
        }
        bool headshot = ((DamageFlags)marker.DamageFlags & DamageFlags.Headshot) != 0;
        int killerHealth = scene.Services is ReplaySceneServices services
            && services.State.Occupant(kill.KillerSlot).Generation == kill.KillerGeneration
            && (kill.KillerLifeId == 0 || services.State.MatchesLife(kill.KillerSlot,
                kill.KillerGeneration, kill.KillerLifeId))
            ? scene.Players.Items[kill.KillerSlot].Health : 0;
        _hud.Draw(Kind == KillCamKind.Final, killer, victim, weapon, headshot,
            killerHealth, Progress,
            _end > _start ? (_killFrame - _start) / (float)(_end - _start) : 1,
            ((float)_killFrame - Frame) / 60);
    }
    internal void FailPresentation(Exception error)
    { LastError = error.Message; Stop(KillcamEndReason.Failed); }
    internal static bool IsValidIdentity(ReplayKillIdentity kill, KillcamContext context, Scene? scene = null)
        => Matches(kill, context) && kill.MatchId != 0 && kill.AuthorityEpoch != 0 && kill.EventId != 0
            && kill.KillerGeneration != 0 && kill.VictimGeneration != 0
            && kill.VictimLifeId != 0 && kill.KillerSlot < PlayerEntity.SlotCapacity
            && kill.VictimSlot < PlayerEntity.SlotCapacity && kill.KillerSlot != kill.VictimSlot
            && (scene == null || kill.KillerSlot < scene.Players.Items.Count && kill.VictimSlot < scene.Players.Items.Count
                && scene.Players.Items[kill.KillerSlot] != null && scene.Players.Items[kill.VictimSlot] != null);
    internal void CancelPersonal()
    { _pending = null; if (Kind == KillCamKind.Personal) Stop(KillcamEndReason.MatchChanged); }
    private static bool Matches(ReplayKillIdentity kill, KillcamContext context) => kill.MatchId == context.MatchId && kill.AuthorityEpoch == context.Epoch;
    internal void Stop(KillcamEndReason reason)
    {
        // Start owns the live input handoff. An offline scene calls Reset on
        // every update/draw even though no killcam ever started; attaching it
        // in Update erased its input snapshots every step and made all pointer
        // samples look like startup baselines. Release this ownership once.
        Scene? live = _live;
        _live = null;
        ReplayAudioOwner.Release(_audio); _audio = 0;
        _startup.Stop();
        _hud = null; _lastKillerCamera = null; _handoffCamera = null;
        _finalTicks = _finalLifetimeTicks = 0;
        _killerName = null; _player?.Dispose(); _player = null; _playingClip?.Dispose(); _playingClip = null; _playing = null; State = KillcamState.None; Kind = KillCamKind.None;
        EndReason = reason;
        if (live != null && live.Players.Items.Count > 0)
        { live.Players.Main.Controls.ClearAll(); live.Players.Main.ModForgetInputDeltas(); }
    }
    internal void Reset(KillcamEndReason reason)
    { Stop(reason); _pending = _candidate = null; IdentityResolution = ReplayKillLifeStatus.Unknown; IdentityFactEventId = 0; }
    public void Dispose() => Reset(KillcamEndReason.SceneClosed);
}
