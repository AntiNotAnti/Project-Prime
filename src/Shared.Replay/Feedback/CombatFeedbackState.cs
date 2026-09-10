namespace MphRead.Combat
{
    public enum HitMarkerMode { Off, Visual, VisualAndAudio }

    // This is deliberately independent from HitMarkerMode.  The latter is a
    // presentation choice (off/visual/audio), while Timing controls whether
    // a local client may show the additional speculative marker.  Keep the
    // original values stable: recordings and settings files contain these
    // numbers.  Predicted is append-only and is never written to old replay
    // checkpoints (see ReplayFeedbackState).
    public enum HitMarkerTiming { Confirmed, Instant }
    public enum HitMarkerKind { None, Hit, Headshot, Kill, Predicted }

    public static class CombatFeedbackSettings
    {
        public static HitMarkerMode HitMarkers { get; set; } = HitMarkerMode.Visual;
        public static HitMarkerTiming Timing { get; set; } = HitMarkerTiming.Confirmed;
        public static bool HeadshotCue { get; set; } = true;
        public static bool KillConfirmation { get; set; } = true;
    }

    public sealed class CombatFeedbackState
    {
        public HitMarkerKind Marker { get; internal set; }
        public uint MarkerTick { get; internal set; }
        public uint MarkerSequence { get; internal set; }
        public uint MarkerAudioSequence { get; internal set; }
        public bool Dead { get; internal set; }
        public ushort FinalDamage { get; internal set; }
        public string RecapHeading { get; internal set; } = "";
        public string RecapFinal { get; internal set; } = "";
        internal void Reset()
        {
            Marker = HitMarkerKind.None;
            MarkerTick = MarkerSequence = MarkerAudioSequence = 0;
            Dead = false;
            FinalDamage = 0;
            RecapHeading = RecapFinal = "";
        }
    }
}
