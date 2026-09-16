using System;
using System.Collections.Generic;
using MphRead.Combat;
using MphRead.Hud;
using MphRead.Hud.Radar;
using MphRead.Mods.Audio;
using MphRead.Mods.Content;
using MphRead.Mods.Hud;
using MphRead.Mods.Network;
using MphRead.Mods.Input;
using ProjectPrime.Server.Shared;

namespace MphRead
{
    public partial class ScenePresentation
    {
        public CombatFeedback CombatFeedback { get; } = new();
        public WorldFeedback WorldFeedback { get; } = new();
        public AnnouncerService Announcer { get; }
        public OptionalPresentationAssetResolver? AnnouncerAssets { get; }
        public AwardHudQueue AwardHud { get; } = new();
        private AnnouncerAudioPresentation? _announcerAudio;
        public AnnouncerAudioPresentation AnnouncerAudio
            => _announcerAudio ??= new(World, Announcer, AnnouncerAssets);
        internal void DisposeAnnouncerAudio()
        {
            _announcerAudio?.Dispose();
            _announcerAudio = null;
        }
        private FeedbackAudio? _feedbackAudio;
        public FeedbackAudio FeedbackAudio => _feedbackAudio ??= new(World);
        internal void DisposeFeedbackAudio()
        {
            _feedbackAudio?.Dispose();
            _feedbackAudio = null;
        }

        /// <summary>
        /// Consume discrete world notices once from the scene presentation.
        /// Draining is unconditional so a muted or isolated presentation
        /// cannot replay stale pickup cues when it becomes active again.
        /// </summary>
        internal void ConsumeWorldFeedbackAudio(bool allowAudio, bool allowHaptics)
        {
            while (WorldFeedback.TryDequeueNotice(out WorldFeedbackNotice notice))
            {
                WorldEvent value = notice.Event;
                if (value.Kind == WorldSignalKind.PickupConsumed)
                {
                    // WorldFeedback only queues local pickups, but retain the
                    // identity fence here so a role/session change before the
                    // next draw cannot produce a stale local confirmation.
                    if (value.Actor == CombatFeedback.Local)
                    {
                        if (allowAudio)
                            FeedbackAudio.PlayPickupAcquired((ItemType)value.A,
                                notice.ReceiptTick);
                        if (allowHaptics
                            && (ItemType)value.A is (ItemType.DoubleDamage or ItemType.Cloak
                                or ItemType.Deathalt or ItemType.OmegaCannon))
                            GamepadHaptics.Play(HapticEvent.MajorPickup, value.Id);
                    }
                    continue;
                }

                FeedbackCue? cue = value.Kind switch
                {
                    WorldSignalKind.FlagPickedUp => FeedbackCue.ObjectiveTaken,
                    WorldSignalKind.FlagDropped or WorldSignalKind.FlagReset => FeedbackCue.ObjectiveDropped,
                    WorldSignalKind.FlagCaptured or WorldSignalKind.NodeCaptured => FeedbackCue.ObjectiveScored,
                    WorldSignalKind.PrimeChanged => FeedbackCue.PrimeChanged,
                    WorldSignalKind.PickupRespawned => FeedbackCue.PickupRespawned,
                    WorldSignalKind.OvertimeStarted => FeedbackCue.Overtime,
                    WorldSignalKind.MatchPoint => FeedbackCue.MatchPoint,
                    WorldSignalKind.NodeContested or WorldSignalKind.DefenderStateChanged => FeedbackCue.ObjectiveTaken,
                    _ => null
                };
                if (allowAudio && cue.HasValue)
                    FeedbackAudio.Play(cue.Value, notice.ReceiptTick,
                        value.Kind == WorldSignalKind.PickupRespawned ? value.Position : null);
            }
        }
    }
}

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private uint _feedbackSoundSequence;
        private uint _feedbackHapticSequence;
        private uint _awardHudRevision;
        private MatchAward? _activeAward;
        private CombatActor _feedbackSoundIdentity = CombatActor.None;
        private readonly List<HudGeometryVertex> _hitMarkerGeometry = new(48);
        internal void SynchronizeReplayFeedbackAudio()
        {
            ResetVisorPresentation();
            _feedbackSoundIdentity = Presentation.CombatFeedback.Local;
            _feedbackSoundSequence = Presentation.CombatFeedback.State.MarkerAudioSequence;
            _feedbackHapticSequence = Presentation.CombatFeedback.State.MarkerAudioSequence;
        }

        private void ModDrawCombatFeedback()
        {
            CombatFeedback feedback = Presentation.CombatFeedback;
            AuthoritativePlay? play = ClientSceneServices.PlayFor(_player._scene);
            NodeControlClient? node = (_player._scene.Services as ClientSceneServices)?.Node;
            uint tick = play?.WorldServerTick ?? ReplayPlayback.WorldServerTick ?? 0;
            HitMarkerKind marker = feedback.VisibleMarker(tick);
            bool identityView = feedback.Local.IsValid && feedback.Local == AuthoritativeActor;
            bool localView = identityView && !Mods.SpectatorMode.IsSpectating;
            bool deathRecapVisible = identityView
                && (!Mods.SpectatorMode.IsSpectating || ReplayPlayback.IsModern)
                && feedback.State.Dead && _player.Health == 0;
            if (_feedbackSoundIdentity != feedback.Local)
            {
                _feedbackSoundIdentity = feedback.Local;
                _feedbackSoundSequence = 0;
                _feedbackHapticSequence = 0;
            }
            if (_feedbackSoundSequence != feedback.State.MarkerAudioSequence)
            {
                _feedbackSoundSequence = feedback.State.MarkerAudioSequence;
                HitMarkerKind cue = feedback.State.MarkerAudioKind;
                if (localView && cue != HitMarkerKind.None)
                {
                    FeedbackCue audioCue = FeedbackAudio.MarkerCue(cue,
                        feedback.State.MarkerFlags, CombatFeedbackSettings.HeadshotCue);
                    if (audioCue == FeedbackCue.HeadshotKill
                        || CombatFeedbackSettings.HitMarkers == HitMarkerMode.VisualAndAudio)
                        Presentation.FeedbackAudio.Play(audioCue, tick);
                }
            }
            if (_feedbackHapticSequence != feedback.State.MarkerAudioSequence)
            {
                _feedbackHapticSequence = feedback.State.MarkerAudioSequence;
                HitMarkerKind cue = feedback.State.MarkerAudioKind;
                if (localView && cue != HitMarkerKind.None
                    && CombatFeedbackSettings.HitMarkers != HitMarkerMode.Off)
                {
                    GamepadHaptics.Play(cue == HitMarkerKind.Kill
                        ? HapticEvent.KillConfirm
                        : cue == HitMarkerKind.Headshot
                            ? HapticEvent.HeadshotConfirm : HapticEvent.HitConfirm,
                        feedback.State.MarkerHapticIdentity);
                }
            }
            if (localView) Presentation.FeedbackAudio.ObserveHealth(feedback.Local, (ushort)_player.Health, tick);
            if (localView && _awardHudRevision != Presentation.AwardHud.Revision)
            {
                _awardHudRevision = Presentation.AwardHud.Revision;
                _activeAward = null;
            }
            if (localView && _activeAward is null && Presentation.AwardHud.TryDequeue(out MatchAward award))
                _activeAward = award;
            if (localView && _activeAward is { } activeAward && CombatFeedback.Age(tick, activeAward.Tick) < 120)
            {
                DrawText2D(128, AwardBannerY(deathRecapVisible), Align.Center, 0,
                    AwardText(activeAward.Kind),
                    new ColorRgba(255, 224, 96, 255), scale: .75f);
            }
            else if (localView && _activeAward is { }) _activeAward = null;
            WorldFeedback world = Presentation.WorldFeedback;
            if (world.Message.Length > 0 && CombatFeedback.Age(tick, world.Tick) < 120)
            {
                bool pickupNotice = world.LastKind == WorldSignalKind.PickupConsumed;
                if (pickupNotice && Features.ProHud)
                {
                    float scale = Features.ProHudScale;
                    float aspect = HudAspectFix;
                    float x = ProHudLeftEdge(aspect) + 2 * scale * aspect;
                    float y = 190 - 22 * scale - 17 * scale;
                    DrawText2D(x, y, Align.Left, 0, ProPickupMessage(world),
                        ProHudInk, maxLength: 36, scale: .42f * scale);
                }
                else
                {
                    DrawText2D(128, pickupNotice ? 42 : 62, Align.Center, 0,
                        world.Message, scale: pickupNotice ? .5f : .7f);
                }
            }
            if (localView && play?.NodeMatchId is Guid matchId
                && node?.TransitionVoteFor(matchId) is
                    { State: MatchTransitionVoteState.Pending } ballot)
            {
                DrawText2D(128, 38, Align.Center, 0,
                    FormatTransitionVoteNotice(ballot, DateTimeOffset.UtcNow),
                    new ColorRgba(242, 154, 46, 255), maxLength: 64, scale: .58f);
            }
            if (localView && feedback.IsHeadshotNoticeVisible(tick))
                DrawText2D(128, 70, Align.Center, 0, feedback.State.HeadshotNotice.Text,
                    scale: .8f);
            if (localView && feedback.IsKillNoticeVisible(tick))
                DrawText2D(128, 78, Align.Center, 0, feedback.State.KillNotice.Text,
                    scale: .75f);
            if (localView && _player.Health > 0 && marker != HitMarkerKind.None)
            {
                float renderAlpha = float.IsFinite(Presentation.Timing.RenderAlpha)
                    ? Math.Clamp(Presentation.Timing.RenderAlpha, 0, 1) : 0;
                float age = CombatFeedback.Age(tick, feedback.State.MarkerTick)
                    + renderAlpha;
                uint duration = CombatFeedback.MarkerDuration(marker);
                float hudScale = Features.CustomCrosshair
                    ? Mods.Render.Crosshair.Scale : Features.ReticleScale;
                float pulseAge = CombatFeedback.Age(tick,
                    feedback.State.MarkerPulseTick) + renderAlpha;
                HitMarkerVisual.Build(_hitMarkerGeometry, CurrentReticlePosition,
                    marker, age, duration, pulseAge, feedback.State, hudScale,
                    Mods.Launcher.LauncherPrefs.ReducedMotion);
                if (_hitMarkerGeometry.Count >= 3)
                    Presentation.DrawHudGeometry(_hitMarkerGeometry);
            }
            int feedY = 22;
            RadarProfile radarProfile = CurrentRadarProfile;
            if (radarProfile.Style == RadarStyle.Enhanced && _player._health > 0
                && !Mods.SpectatorMode.FreeCamera)
            {
                RadarLayout radar = RadarLayoutCalculator.Calculate(radarProfile.Anchor,
                    radarProfile.Scale, radarProfile.OffsetX, radarProfile.OffsetY, HudAspectFix);
                feedY = KillFeedStartY(radar.CenterX >= 128, radar);
            }
            int row = 0;
            for (int i = 0; i < feedback.FeedCount; i++)
            {
                KillFeedEntry entry = feedback.FeedAt(i);
                if (CombatFeedback.Age(tick, entry.Tick) >= CombatFeedback.FeedTicks) continue;
                int y = feedY + row++ * 8;
                if (Features.ProHud)
                {
                    ColorRgba color = ProHudInk;
                    Presentation.DrawHudFlatBox(164, y - 1, 255, y + 7,
                        new OpenTK.Mathematics.Vector4(.02f, .03f, .05f, .55f));
                    if (entry.Killer.Slot < _player._scene.Players.Count)
                    {
                        PlayerEntity killer = _player._scene.Players[entry.Killer.Slot];
                        int team = killer.TeamIndex;
                        if (_player._scene.Match.Rules.Teams
                            && killer.CombatIdentity == entry.Killer)
                            color = ProTeamInk[Math.Clamp(team, 0,
                                ProTeamInk.Length - 1)];
                    }
                    DrawText2D(252, y, Align.Right, 0, entry.Text, color,
                        maxLength: 44, scale: .65f);
                }
                else
                {
                    DrawText2D(252, y, Align.Right, 0, entry.Text,
                        maxLength: 44, scale: .65f);
                }
            }
            if (deathRecapVisible)
            {
                DrawText2D(128, 54, Align.Center, 0, feedback.State.RecapHeading, maxLength: 36, scale: .8f);
                DrawText2D(128, 64, Align.Center, 0, feedback.State.RecapFinal, maxLength: 36, scale: .75f);
                int start = System.Math.Max(0, feedback.History.Count - 4);
                for (int i = start; i < feedback.History.Count; i++)
                    DrawText2D(128, 76 + (i - start) * 8, Align.Center, 0, feedback.History[i].Text, maxLength: 44, scale: .65f);
            }
        }

        internal static int AwardBannerY(bool deathRecapVisible)
            => deathRecapVisible ? 42 : 54;

        internal static int KillFeedStartY(bool overlapsHorizontally, RadarLayout radar)
        {
            const int defaultY = 22;
            const int rowHeight = 8;
            int feedHeight = CombatFeedback.FeedCapacity * rowHeight;
            if (!overlapsHorizontally || radar.Top >= defaultY + feedHeight
                || radar.Top + radar.Height <= defaultY)
                return defaultY;

            int below = (int)MathF.Ceiling(radar.Top + radar.Height
                + RadarLayoutCalculator.HudClearance);
            if (below + feedHeight <= 192) return below;

            return Math.Max(0, (int)MathF.Floor(radar.Top
                - RadarLayoutCalculator.HudClearance - feedHeight));
        }

        internal static string FormatTransitionVoteNotice(
            NodeMatchTransitionVoteSnapshot ballot, DateTimeOffset now)
        {
            int seconds = Math.Max(0,
                (int)Math.Ceiling((ballot.Deadline - now).TotalSeconds));
            string action = ballot.Choice == MatchTransitionChoice.Restart
                ? $"Restart {ballot.TargetMapKey}" : $"Map {ballot.TargetMapKey}";
            string response = ballot.OwnVote is null
                ? "Press Start to vote"
                : ballot.OwnVote.Value ? "Voted yes" : "Voted no";
            return $"Vote by {ballot.ProposerName}: {action} · Yes {ballot.Yes}/{ballot.Needed} · {seconds}s · {response}";
        }

        private static string AwardText(MatchAwardKind kind) => kind switch
        {
            MatchAwardKind.FirstHunt => "FIRST HUNT",
            MatchAwardKind.DoubleKill => "DOUBLE KILL",
            MatchAwardKind.TripleKill => "TRIPLE KILL",
            MatchAwardKind.Interceptor => "INTERCEPTOR",
            MatchAwardKind.Defender => "DEFENDER",
            MatchAwardKind.PrimeSlayer => "PRIME SLAYER",
            MatchAwardKind.Capture => "CAPTURE",
            MatchAwardKind.Assist => "ASSIST",
            _ => ""
        };
    }
}
