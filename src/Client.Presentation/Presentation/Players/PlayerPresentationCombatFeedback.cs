using System;
using MphRead.Combat;
using MphRead.Hud;
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

        /// <summary>Consume discrete world notices once from the scene HUD owner.</summary>
        internal void ConsumeWorldFeedbackAudio()
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
                        FeedbackAudio.PlayPickupAcquired((ItemType)value.A, notice.ReceiptTick);
                        if ((ItemType)value.A is ItemType.DoubleDamage or ItemType.Cloak
                            or ItemType.Deathalt or ItemType.OmegaCannon)
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
                if (cue.HasValue)
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
        private uint _awardHudRevision;
        private MatchAward? _activeAward;
        private CombatActor _feedbackSoundIdentity = CombatActor.None;
        internal void SynchronizeReplayFeedbackAudio()
        {
            ResetVisorPresentation();
            _feedbackSoundIdentity = Presentation.CombatFeedback.Local;
            _feedbackSoundSequence = Presentation.CombatFeedback.State.MarkerAudioSequence;
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
            if (_feedbackSoundIdentity != feedback.Local)
            {
                _feedbackSoundIdentity = feedback.Local;
                _feedbackSoundSequence = 0;
            }
            if (_feedbackSoundSequence != feedback.State.MarkerAudioSequence)
            {
                _feedbackSoundSequence = feedback.State.MarkerAudioSequence;
                if (localView && marker != HitMarkerKind.None && CombatFeedbackSettings.HitMarkers == HitMarkerMode.VisualAndAudio)
                    Presentation.FeedbackAudio.Play(marker == HitMarkerKind.Kill ? FeedbackCue.Kill
                        : marker == HitMarkerKind.Headshot ? FeedbackCue.Headshot : FeedbackCue.Hit, tick);
            }
            if (localView) Presentation.FeedbackAudio.ObserveHealth(feedback.Local, (ushort)_player.Health, tick);
            // DrawHudObjects is called only for the scene's active HUD owner,
            // so this drains the scene queue exactly once per presentation.
            Presentation.ConsumeWorldFeedbackAudio();
            if (localView && _awardHudRevision != Presentation.AwardHud.Revision)
            {
                _awardHudRevision = Presentation.AwardHud.Revision;
                _activeAward = null;
            }
            if (localView && _activeAward is null && Presentation.AwardHud.TryDequeue(out MatchAward award))
                _activeAward = award;
            if (localView && _activeAward is { } activeAward && CombatFeedback.Age(tick, activeAward.Tick) < 120)
            {
                DrawText2D(128, 54, Align.Center, 0, AwardText(activeAward.Kind),
                    new ColorRgba(255, 224, 96, 255), scale: .75f);
            }
            else if (localView && _activeAward is { }) _activeAward = null;
            WorldFeedback world = Presentation.WorldFeedback;
            if (world.Message.Length > 0 && CombatFeedback.Age(tick, world.Tick) < 120)
            {
                bool pickupNotice = world.LastKind == WorldSignalKind.PickupConsumed;
                DrawText2D(128, pickupNotice ? 42 : 62, Align.Center, 0, world.Message,
                    scale: pickupNotice ? .5f : .7f);
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
                uint age = CombatFeedback.Age(tick, feedback.State.MarkerTick);
                uint duration = CombatFeedback.MarkerDuration(marker);
                float fade = duration == 0 ? 0 : Math.Clamp((duration - age) / (float)duration, 0, 1);
                float punch = marker == HitMarkerKind.Predicted
                    ? .85f + .15f * Math.Min(age / 2f, 1f)
                    : 1f + .1f * Math.Max(0, 1f - age / 2f);
                float hudScale = Features.CustomCrosshair
                    ? Mods.Render.Crosshair.Scale : Features.ReticleScale;
                float scale = .75f * hudScale * punch;
                int markerX = Math.Clamp((int)MathF.Round(CurrentReticlePosition.X * 256), 0, 255);
                int markerY = Math.Clamp((int)MathF.Round(CurrentReticlePosition.Y * 192), 0, 191);
                string text = marker == HitMarkerKind.Kill ? "[X]" : marker == HitMarkerKind.Headshot ? "[+]"
                    : marker == HitMarkerKind.Predicted ? "x" : "][";
                ColorRgba color = marker == HitMarkerKind.Kill ? new ColorRgba(255, 64, 64, 255)
                    : marker == HitMarkerKind.Headshot ? new ColorRgba(255, 255, 64, 255)
                    : marker == HitMarkerKind.Predicted ? new ColorRgba(210, 210, 210, (byte)(150 * fade))
                    : new ColorRgba(255, 255, 255, (byte)(255 * fade));
                DrawText2D(markerX, markerY, Align.Center, 0, text, color, scale: scale);
            }
            int row = 0;
            for (int i = 0; i < feedback.FeedCount; i++)
            {
                KillFeedEntry entry = feedback.FeedAt(i);
                if (CombatFeedback.Age(tick, entry.Tick) >= CombatFeedback.FeedTicks) continue;
                DrawText2D(252, 22 + row++ * 8, Align.Right, 0, entry.Text, maxLength: 44, scale: .65f);
            }
            if (identityView && (!Mods.SpectatorMode.IsSpectating || ReplayPlayback.IsModern)
                && feedback.State.Dead && _player.Health == 0)
            {
                DrawText2D(128, 54, Align.Center, 0, feedback.State.RecapHeading, maxLength: 36, scale: .8f);
                DrawText2D(128, 64, Align.Center, 0, feedback.State.RecapFinal, maxLength: 36, scale: .75f);
                int start = System.Math.Max(0, feedback.History.Count - 4);
                for (int i = start; i < feedback.History.Count; i++)
                    DrawText2D(128, 76 + (i - start) * 8, Align.Center, 0, feedback.History[i].Text, maxLength: 44, scale: .65f);
            }
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
