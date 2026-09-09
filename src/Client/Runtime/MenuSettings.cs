namespace MphRead
{
    public enum SoundCapability
    {
        None = 0,
        Unsupported = 1,
        Supported = 2
    }

    public class MenuSettings()
    {
        public string RoomKey { get; set; } = "MP3 PROVING GROUND";
        public string Mode { get; set; } = "auto-select";
        public string Player1 { get; set; } = "Samus 0";
        public string Player2 { get; set; } = "none 0";
        public string Player3 { get; set; } = "none 0";
        public string Player4 { get; set; } = "none 0";
        public string Models { get; set; } = "none";
        public string MphVersion { get; set; } = "AMHE1";
        public string FhVersion { get; set; } = "AMFE0";
        public string Language { get; set; } = "English";
        public string FeedbackVolume { get; set; } = "0.7";
        public string SfxVolume { get; set; } = "0.35";
        public string MusicVolume { get; set; } = "0.50";
        public string ResolutionScale { get; set; } = "100";
        public string Lighting { get; set; } = "on";
        public string Fog { get; set; } = "on";
        // New quality keys are nullable so a settings file written before
        // R12 can be distinguished from one that explicitly selected a
        // value. GameSettings falls back to the selected preset when these
        // keys are absent, and keeps the old filtering switch meaningful.
        public string GraphicsPreset { get; set; } = "original";
        public string? TextureFilteringPreset { get; set; }
        public string? Anisotropy { get; set; }
        public string? Msaa { get; set; }
        public string? Bloom { get; set; }
        public string? DynamicVisualLights { get; set; }
        // Nullable preserves the distinction between an old key containing
        // "off" and a newer file where the legacy key was never written.
        public string? TextureFiltering { get; set; }
        public string AdvancedNetwork { get; set; } = "off";
        public string HitMarkers { get; set; } = "Visual";
        public string HeadshotCue { get; set; } = "on";
        public string KillConfirmation { get; set; } = "on";
        public string RadarStyle { get; set; } = "classic";
        public string RadarOrientation { get; set; } = "heading";
        public string ShowFps { get; set; } = "off";
        public string FrameRateCap { get; set; } = "display";
        public string CelShading { get; set; } = "off";
        public string CelBands { get; set; } = "8";
        public string CelEdge { get; set; } = "50";
        public string PointGoal { get; set; } = "7";
        public string TimeLimit { get; set; } = "7:00";
        public string TimeGoal { get; set; } = "1:30";
        public string AutoReset { get; set; } = "on";
        public string TeamPlay { get; set; } = "off";
        public string HunterRadar { get; set; } = "off";
        public string DamageLevel { get; set; } = "medium";
        public string FriendlyFire { get; set; } = "off";
        public string AffinityWeapons { get; set; } = "off";
    }

}
