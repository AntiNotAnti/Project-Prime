using MphRead.Combat;
using MphRead.Hud;
using MphRead.Mods.Network;

namespace MphRead
{
    public partial class ScenePresentation
    {
        public CombatFeedback CombatFeedback { get; } = new();
        public WorldFeedback WorldFeedback { get; } = new();
        private FeedbackAudio? _feedbackAudio;
        public FeedbackAudio FeedbackAudio => _feedbackAudio ??= new(World);
    }
}

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private uint _feedbackSoundSequence;
        private uint _worldFeedbackSequence;
        private CombatActor _feedbackSoundIdentity = CombatActor.None;
        internal void SynchronizeReplayFeedbackAudio()
        {
            _feedbackSoundIdentity = Presentation.CombatFeedback.Local;
            _feedbackSoundSequence = Presentation.CombatFeedback.State.MarkerSequence;
            _worldFeedbackSequence = Presentation.WorldFeedback.Sequence;
        }

        private void ModDrawCombatFeedback()
        {
            CombatFeedback feedback = Presentation.CombatFeedback;
            uint tick = AuthoritativePlay.Current?.WorldServerTick ?? DemoPlayback.WorldServerTick ?? 0;
            HitMarkerKind marker = feedback.VisibleMarker(tick);
            bool identityView = feedback.Local.IsValid && feedback.Local.Slot == _player.SlotIndex;
            bool localView = identityView && !Mods.SpectatorMode.IsSpectating;
            if (_feedbackSoundIdentity != feedback.Local)
            {
                _feedbackSoundIdentity = feedback.Local;
                _feedbackSoundSequence = 0;
            }
            if (_feedbackSoundSequence != feedback.State.MarkerSequence)
            {
                _feedbackSoundSequence = feedback.State.MarkerSequence;
                if (localView && marker != HitMarkerKind.None && CombatFeedbackSettings.HitMarkers == HitMarkerMode.VisualAndAudio)
                    Presentation.FeedbackAudio.Play(marker == HitMarkerKind.Kill ? FeedbackCue.Kill
                        : marker == HitMarkerKind.Headshot ? FeedbackCue.Headshot : FeedbackCue.Hit, tick);
            }
            if (localView) Presentation.FeedbackAudio.ObserveHealth(feedback.Local, (ushort)_player.Health, tick);
            WorldFeedback world = Presentation.WorldFeedback;
            if (_worldFeedbackSequence != world.Sequence)
            {
                _worldFeedbackSequence = world.Sequence;
                if (world.Message.Length > 0 && CombatFeedback.Age(tick, world.Tick) < 120)
                {
                    FeedbackCue? cue = world.LastKind switch
                    {
                        WorldSignalKind.FlagPickedUp => FeedbackCue.ObjectiveTaken,
                        WorldSignalKind.FlagDropped or WorldSignalKind.FlagReset => FeedbackCue.ObjectiveDropped,
                        WorldSignalKind.FlagCaptured or WorldSignalKind.NodeCaptured => FeedbackCue.ObjectiveScored,
                        WorldSignalKind.PrimeChanged => FeedbackCue.PrimeChanged,
                        WorldSignalKind.PickupRespawned => FeedbackCue.Pickup,
                        WorldSignalKind.OvertimeStarted => FeedbackCue.Overtime,
                        WorldSignalKind.MatchPoint => FeedbackCue.MatchPoint,
                        WorldSignalKind.NodeContested or WorldSignalKind.DefenderStateChanged => FeedbackCue.ObjectiveTaken,
                        _ => null
                    };
                    if (cue.HasValue) Presentation.FeedbackAudio.Play(cue.Value, tick,
                        world.LastKind == WorldSignalKind.PickupRespawned ? world.LastPosition : null);
                }
            }
            if (world.Message.Length > 0 && CombatFeedback.Age(tick, world.Tick) < 120)
                DrawText2D(128, 32, Align.Center, 0, world.Message, scale: .7f);
            if (localView && _player.Health > 0 && marker != HitMarkerKind.None)
            {
                string text = marker == HitMarkerKind.Kill ? "[X]" : marker == HitMarkerKind.Headshot ? "[+]" : "][";
                ColorRgba color = marker == HitMarkerKind.Kill ? new ColorRgba(255, 64, 64, 255)
                    : marker == HitMarkerKind.Headshot ? new ColorRgba(255, 255, 64, 255) : new ColorRgba(255, 255, 255, 255);
                DrawText2D(128, 90, Align.Center, 0, text, color, scale: .75f);
            }
            int row = 0;
            for (int i = 0; i < feedback.FeedCount; i++)
            {
                KillFeedEntry entry = feedback.FeedAt(i);
                if (CombatFeedback.Age(tick, entry.Tick) >= CombatFeedback.FeedTicks) continue;
                DrawText2D(252, 22 + row++ * 8, Align.Right, 0, entry.Text, maxLength: 44, scale: .65f);
            }
            if (identityView && (!Mods.SpectatorMode.IsSpectating || DemoPlayback.IsModern)
                && feedback.State.Dead && _player.Health == 0)
            {
                DrawText2D(128, 54, Align.Center, 0, feedback.State.RecapHeading, maxLength: 36, scale: .8f);
                DrawText2D(128, 64, Align.Center, 0, feedback.State.RecapFinal, maxLength: 36, scale: .75f);
                int start = System.Math.Max(0, feedback.History.Count - 4);
                for (int i = start; i < feedback.History.Count; i++)
                    DrawText2D(128, 76 + (i - start) * 8, Align.Center, 0, feedback.History[i].Text, maxLength: 44, scale: .65f);
            }
        }
    }
}
