namespace MphRead
{
    public static class Features
    {
        public static bool ProHud { get; set; } = true;
        public static bool ProHudFixedWeapon { get; set; } = true;
        public static bool KillFeedEnabled { get; set; } = true;
        public static float ReticleOpacity { get; set; } = 1;
    }
}
namespace MphRead.Mods.Render
{
    public static class Radar
    {
        public static bool Enabled { get; set; } = true;
        public static bool ShowBackground { get; set; }
        public static bool ShowOutlines { get; set; } = true;
    }
}
