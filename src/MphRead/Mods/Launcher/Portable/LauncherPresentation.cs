using System;
using System.Diagnostics;

namespace MphRead.Mods.Launcher;

/// <summary>One render-thread presentation timeline. No simulation or network timers.</summary>
public sealed class MenuMotionState
{
    private double _last = double.NaN;
    public double Time { get; private set; }
    public double EnergyTime { get; private set; }
    public LauncherActivityAmbience Activity { get; private set; } = LauncherActivityAmbience.ServerBrowser;
    public LauncherHunterTheme Theme { get; private set; } = LauncherHunterTheme.For(Hunter.Samus);
    public readonly float[] HunterWeights = new float[7];
    public readonly float[] Occupancy = new float[8];
    public readonly float[] ReadyPulse = new float[8];
    public readonly MenuRgb[] SlotColors = new MenuRgb[8];
    private readonly bool[] _ready = new bool[8];
    private readonly int[] _identity = new int[8];
    public float Launch { get; private set; }
    public bool Reduced { get; private set; }

    public void Advance(double now, bool focused, bool reduced, LauncherActivityAmbience target, Hunter hunter)
    {
        bool first = double.IsNaN(_last);
        double elapsed = first ? 0 : Math.Max(0, now - _last);
        // A suspended window resumes from its last pose without fast-forwarding.
        float dt = !focused || elapsed > .25 ? 0 : (float)elapsed;
        _last = now; Reduced = reduced;
        float blend = first || reduced ? 1 : 1 - MathF.Exp(-dt / .18f);
        Activity = Blend(Activity, target, blend);
        Theme = Blend(Theme, LauncherHunterTheme.For(hunter), blend);
        for (int i = 0; i < 7; i++) HunterWeights[i] = Lerp(HunterWeights[i], (int)hunter == i ? 1 : 0, blend);
        if (!reduced) { Time += dt; EnergyTime += dt * Activity.PulseSpeed; }
        for (int i = 0; i < 8; i++)
        {
            bool occupied = LauncherLobbyVisuals.Active && (LauncherLobbyVisuals.OccupiedMask & (1 << i)) != 0;
            int identity = occupied ? LauncherLobbyVisuals.IdentityAt(i) : -1;
            bool ready = occupied && (LauncherLobbyVisuals.ReadyMask & (1 << i)) != 0;
            if (_identity[i] != identity) { if (occupied) Occupancy[i] = 0; _ready[i] = false; _identity[i] = identity; }
            if (ready && !_ready[i]) ReadyPulse[i] = reduced ? 0 : 1;
            _ready[i] = ready;
            Occupancy[i] = Lerp(Occupancy[i], occupied ? 1 : 0, first || reduced ? 1 : 1 - MathF.Exp(-dt / .14f));
            ReadyPulse[i] *= reduced ? 0 : MathF.Exp(-dt / .3f);
            if (occupied) SlotColors[i] = MenuRgb.Lerp(SlotColors[i], LauncherHunterTheme.For(LauncherLobbyVisuals.HunterAt(i)).Rim, blend);
        }
        // Corrections/cancellation immediately change the target; no client countdown.
        float launch = LauncherLobbyVisuals.Active && LauncherLobbyVisuals.Starting
            ? 1f / (1f + (float)Math.Max(0, LauncherLobbyVisuals.CountdownSeconds)) : 0;
        Launch = Lerp(Launch, launch, blend);
    }
    private static float Lerp(float a, float b, float t) => t >= 1 ? b : a + (b-a)*t;
    private static LauncherActivityAmbience Blend(LauncherActivityAmbience a, LauncherActivityAmbience b, float t) => b with
    {
        Accent = MenuRgb.Lerp(a.Accent, b.Accent, t),
        Secondary = MenuRgb.Lerp(a.Secondary, b.Secondary, t),
        Energy = Lerp(a.Energy, b.Energy, t),
        FogBias = Lerp(a.FogBias, b.FogBias, t),
        ParticleBias = Lerp(a.ParticleBias, b.ParticleBias, t),
        StructureBias = Lerp(a.StructureBias, b.StructureBias, t),
        FloorGrid = Lerp(a.FloorGrid, b.FloorGrid, t),
        HeroLightBias = Lerp(a.HeroLightBias, b.HeroLightBias, t),
        PulseSpeed = Lerp(a.PulseSpeed, b.PulseSpeed, t),
        Warmth = Lerp(a.Warmth, b.Warmth, t),
    };
    private static LauncherHunterTheme Blend(LauncherHunterTheme a, LauncherHunterTheme b, float t) => b with
    {
        Halo = MenuRgb.Lerp(a.Halo, b.Halo, t),
        Rim = MenuRgb.Lerp(a.Rim, b.Rim, t),
        Floor = MenuRgb.Lerp(a.Floor, b.Floor, t),
        Particle = MenuRgb.Lerp(a.Particle, b.Particle, t),
        AccentStrength = Lerp(a.AccentStrength, b.AccentStrength, t),
        HaloScale = Lerp(a.HaloScale, b.HaloScale, t),
        ParticleScale = Lerp(a.ParticleScale, b.ParticleScale, t),
    };
}

public static class LauncherPresentation
{
    public static MenuMotionState Motion { get; } = new();
    public static bool Active { get; set; }
    public static double Seconds => LauncherPrefs.ReduceMotion ? 0 : Motion.Time;
    public static void BeginFrame(Hunter hunter, bool focused = true)
    {
        Active = true;
        Motion.Advance(
        Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, focused, LauncherPrefs.ReduceMotion,
        LauncherMenuVisuals.Activity, hunter);
    }
}
