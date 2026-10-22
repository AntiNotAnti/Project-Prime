namespace MphRead.Mods.Cosmetics
{
    // Published by the preview UI; consumed only by the preview render pass.
    public static class CosmeticPreview
    {
        public static CosmeticLoadout? Loadout { get; set; }
        public static SkinContext Mode { get; set; } = SkinContext.Biped;
        public static float Yaw { get; set; }
        public static float Zoom { get; set; } = 1;
        public static bool LoopDeath { get; set; }
        public static int DeathRequest { get; set; }
    }
}
