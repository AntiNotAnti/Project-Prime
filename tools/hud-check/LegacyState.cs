namespace MphRead
{
    public static class Features
    {
        public static bool ProHud { get; set; } = true;
        public static bool FixedWeapon => ProHudFixedWeapon;
        public static bool ProHudFixedWeapon { get; set; } = true;
        private static bool _fixedWeapon;
        public static bool FixedWeapon { get => ProHud ? ProHudFixedWeapon : _fixedWeapon; set => _fixedWeapon=value; }
        public static bool KillFeedEnabled { get; set; } = true;
        public static float ReticleOpacity { get; set; } = 1;
    }
}
namespace MphRead.Mods.Input
{
    public static class StylusZone { public static string GuideColor { get; set; } = "#A2D3F0"; }
    public static class DsLayout { public static bool Active { get; set; } }
}
namespace MphRead.Mods.Network
{
    public static class NetHitPrediction { public static bool MarkerEnabled { get; set; } = true; }
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

namespace MphRead.Mods.Input { public static class DsLayout { public static bool Active { get; set; } } public static class StylusZone { public static string GuideColor { get; set; } = "#00CCFF"; } }
namespace MphRead.Mods.Network { public static class NetHitPrediction { public static bool MarkerEnabled { get; set; } } }
