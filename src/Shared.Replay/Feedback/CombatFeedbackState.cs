namespace MphRead.Combat
{
    public enum HitMarkerMode { Off, Visual, VisualAndAudio }
    public enum HitMarkerKind { None, Hit, Headshot, Kill }

    public static class CombatFeedbackSettings
    {
        public static HitMarkerMode HitMarkers { get; set; } = HitMarkerMode.Visual;
        public static bool HeadshotCue { get; set; } = true;
        public static bool KillConfirmation { get; set; } = true;
    }

    public sealed class CombatFeedbackState
    {
        public HitMarkerKind Marker { get; internal set; }
        public uint MarkerTick { get; internal set; }
        public uint MarkerSequence { get; internal set; }
        public bool Dead { get; internal set; }
        public ushort FinalDamage { get; internal set; }
        public string RecapHeading { get; internal set; } = "";
        public string RecapFinal { get; internal set; } = "";
        internal void Reset()
        {
            Marker = HitMarkerKind.None;
            MarkerTick = MarkerSequence = 0;
            Dead = false;
            FinalDamage = 0;
            RecapHeading = RecapFinal = "";
        }
    }
}
