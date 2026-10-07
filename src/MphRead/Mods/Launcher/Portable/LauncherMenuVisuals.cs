using System;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Tiny renderer-neutral RGB value used by the menu presentation contracts.
    /// Keeping OpenTK types out of this namespace lets any future RmlUi backend
    /// consume the same art direction without depending on the desktop GL path.
    /// </summary>
    public readonly record struct MenuRgb(float R, float G, float B)
    {
        public static MenuRgb Lerp(MenuRgb a, MenuRgb b, float amount)
        {
            float t = Math.Clamp(amount, 0f, 1f);
            return new MenuRgb(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t);
        }
    }

    /// <summary>
    /// Universal background recipe for the Project Prime deployment chamber.
    /// Activities alter the mood; hunters alter the hero accents. Neither needs
    /// a different room texture or a second gameplay Scene.
    /// </summary>
    public readonly record struct LauncherBackdropStyle(
        string Name,
        MenuRgb Top,
        MenuRgb Mid,
        MenuRgb Bottom,
        float StructureOpacity,
        float HeroHalo,
        float Fog,
        float Particles,
        float FloorGlow,
        float LeftUiDarken,
        float RightUiDarken,
        float BeamIntensity,
        float BackgroundSoftness)
    {
        public static LauncherBackdropStyle DeploymentChamber => new(
            Name: "deployment-chamber",
            Top: new MenuRgb(0.020f, 0.044f, 0.074f),
            Mid: new MenuRgb(0.036f, 0.098f, 0.148f),
            Bottom: new MenuRgb(0.010f, 0.024f, 0.043f),
            StructureOpacity: 0.78f,
            HeroHalo: 0.48f,
            Fog: 0.18f,
            Particles: 0.15f,
            FloorGlow: 0.42f,
            LeftUiDarken: 0.48f,
            RightUiDarken: 0.28f,
            BeamIntensity: 0.24f,
            BackgroundSoftness: 0.14f);
    }

    public readonly record struct LauncherActivityAmbience(
        string Name,
        MenuRgb Accent,
        MenuRgb Secondary,
        float Energy,
        float FogBias,
        float ParticleBias,
        float StructureBias,
        float FloorGrid,
        float HeroLightBias,
        float PulseSpeed,
        float Warmth)
    {
        public static LauncherActivityAmbience QuickPlay => new(
            "quick-play",
            new MenuRgb(0.12f, 0.56f, 0.96f),
            new MenuRgb(0.21f, 0.80f, 1.00f),
            Energy: 1.00f,
            FogBias: 1.00f,
            ParticleBias: 1.00f,
            StructureBias: 1.00f,
            FloorGrid: 0.52f,
            HeroLightBias: 1.00f,
            PulseSpeed: 1.00f,
            Warmth: 0.00f);

        public static LauncherActivityAmbience ServerBrowser => new(
            "server-browser",
            new MenuRgb(0.10f, 0.48f, 0.78f),
            new MenuRgb(0.16f, 0.68f, 0.84f),
            Energy: 0.72f,
            FogBias: 0.78f,
            ParticleBias: 0.55f,
            StructureBias: 1.10f,
            FloorGrid: 0.72f,
            HeroLightBias: 0.88f,
            PulseSpeed: 0.58f,
            Warmth: 0.00f);

        public static LauncherActivityAmbience OfflineBattle => new(
            "offline-battle",
            new MenuRgb(0.16f, 0.52f, 0.78f),
            new MenuRgb(0.34f, 0.72f, 0.88f),
            Energy: 0.62f,
            FogBias: 0.62f,
            ParticleBias: 0.42f,
            StructureBias: 0.90f,
            FloorGrid: 1.00f,
            HeroLightBias: 0.82f,
            PulseSpeed: 0.48f,
            Warmth: 0.02f);

        public static LauncherActivityAmbience Adventure => new(
            "adventure",
            new MenuRgb(0.40f, 0.46f, 0.72f),
            new MenuRgb(0.92f, 0.55f, 0.24f),
            Energy: 0.52f,
            FogBias: 1.28f,
            ParticleBias: 1.18f,
            StructureBias: 0.72f,
            FloorGrid: 0.22f,
            HeroLightBias: 0.92f,
            PulseSpeed: 0.36f,
            Warmth: 0.28f);
    }

    public readonly record struct LauncherHunterTheme(
        string Name,
        MenuRgb Halo,
        MenuRgb Rim,
        MenuRgb Floor,
        MenuRgb Particle,
        float AccentStrength,
        float HaloScale,
        float ParticleScale)
    {
        public static LauncherHunterTheme For(Hunter hunter) => hunter switch
        {
            Hunter.Trace => new(
                "trace",
                new MenuRgb(0.82f, 0.10f, 0.08f),
                new MenuRgb(1.00f, 0.30f, 0.16f),
                new MenuRgb(0.62f, 0.08f, 0.06f),
                new MenuRgb(1.00f, 0.22f, 0.16f),
                0.86f, 1.02f, 0.92f),
            Hunter.Samus => new(
                "samus",
                new MenuRgb(0.94f, 0.42f, 0.10f),
                new MenuRgb(1.00f, 0.72f, 0.24f),
                new MenuRgb(0.72f, 0.30f, 0.08f),
                new MenuRgb(1.00f, 0.62f, 0.20f),
                0.80f, 1.00f, 0.78f),
            Hunter.Sylux => new(
                "sylux",
                new MenuRgb(0.06f, 0.62f, 0.92f),
                new MenuRgb(0.20f, 0.90f, 1.00f),
                new MenuRgb(0.04f, 0.38f, 0.72f),
                new MenuRgb(0.20f, 0.82f, 1.00f),
                0.88f, 1.08f, 0.98f),
            Hunter.Kanden => new(
                "kanden",
                new MenuRgb(0.26f, 0.78f, 0.20f),
                new MenuRgb(0.58f, 1.00f, 0.24f),
                new MenuRgb(0.16f, 0.54f, 0.12f),
                new MenuRgb(0.46f, 1.00f, 0.22f),
                0.82f, 1.04f, 1.08f),
            Hunter.Noxus => new(
                "noxus",
                new MenuRgb(0.24f, 0.58f, 0.92f),
                new MenuRgb(0.58f, 0.88f, 1.00f),
                new MenuRgb(0.16f, 0.40f, 0.74f),
                new MenuRgb(0.66f, 0.92f, 1.00f),
                0.74f, 1.10f, 0.72f),
            Hunter.Spire => new(
                "spire",
                new MenuRgb(0.94f, 0.28f, 0.08f),
                new MenuRgb(1.00f, 0.60f, 0.18f),
                new MenuRgb(0.74f, 0.18f, 0.05f),
                new MenuRgb(1.00f, 0.42f, 0.12f),
                0.84f, 1.06f, 0.92f),
            Hunter.Weavel => new(
                "weavel",
                new MenuRgb(0.82f, 0.16f, 0.08f),
                new MenuRgb(1.00f, 0.46f, 0.18f),
                new MenuRgb(0.60f, 0.10f, 0.05f),
                new MenuRgb(1.00f, 0.34f, 0.12f),
                0.82f, 1.00f, 0.92f),
            _ => new(
                "neutral",
                new MenuRgb(0.15f, 0.54f, 0.90f),
                new MenuRgb(0.36f, 0.80f, 1.00f),
                new MenuRgb(0.10f, 0.34f, 0.66f),
                new MenuRgb(0.36f, 0.82f, 1.00f),
                0.72f, 1.00f, 0.80f)
        };
    }

    public static class LauncherMenuVisuals
    {
        public static LauncherBackdropStyle Style => LauncherBackdropStyle.DeploymentChamber;

        public static LauncherActivityAmbience Activity => LauncherBackdrop.Scene switch
        {
            LauncherBackdropScene.Play => LauncherActivityAmbience.ServerBrowser,
            LauncherBackdropScene.Offline => LauncherActivityAmbience.OfflineBattle,
            LauncherBackdropScene.Adventure => LauncherActivityAmbience.Adventure,
            _ => LauncherActivityAmbience.QuickPlay
        };

        public static LauncherHunterTheme Hunter(Hunter hunter)
            => LauncherHunterTheme.For(hunter);
    }
}
