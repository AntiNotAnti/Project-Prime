using System;
using System.Diagnostics;

namespace MphRead.Mods.Launcher;

/// <summary>One render-thread presentation timeline. No simulation or network timers.</summary>
public sealed class MenuMotionState
{
    private double _last = double.NaN;
    private bool _initialized;
    private uint _session;
    private Hunter _hunter;
    public double Time { get; private set; }
    public double EnergyTime { get; private set; }
    public LauncherActivityAmbience Activity { get; private set; } = LauncherActivityAmbience.ServerBrowser;
    public LauncherHunterTheme Theme { get; private set; } = LauncherHunterTheme.For(Hunter.Samus);
    public readonly float[] HunterWeights = new float[7];
    public readonly float[] Occupancy = new float[8];
    public readonly float[] ReadyPulse = new float[8];
    public readonly MenuRgb[] SlotColors = new MenuRgb[8];
    public readonly bool[] Ready = new bool[8];
    public readonly float[] JoinPulse = new float[8];
    public readonly float[] LeavePulse = new float[8];
    private readonly int[] _identity = new int[8];
    public float HeroReveal { get; private set; } = 1;
    public float SceneBlend { get; private set; }
    public float Launch { get; private set; }
    public bool Reduced { get; private set; }

    public void Advance(double now, bool focused, bool reduced, LauncherActivityAmbience target, Hunter hunter)
    {
        bool first = !_initialized;
        double elapsed = double.IsNaN(_last) ? 0 : Math.Max(0, now - _last);
        // A suspended window resumes from its last pose without fast-forwarding.
        float dt = !focused || elapsed > .25 ? 0 : (float)elapsed;
        _last = now; Reduced = reduced;
        _initialized = true;
        if (_session != LauncherLobbyVisuals.SessionVersion)
        {
            ClearSlots();
            _session = LauncherLobbyVisuals.SessionVersion;
        }
        float blend = first || reduced ? 1 : 1 - MathF.Exp(-dt / .18f);
        Activity = Blend(Activity, target, blend);
        Theme = Blend(Theme, LauncherHunterTheme.For(hunter), blend);
        for (int i = 0; i < 7; i++) HunterWeights[i] = Lerp(HunterWeights[i], (int)hunter == i ? 1 : 0, blend);
        if (!first && _hunter != hunter) HeroReveal = 0;
        _hunter = hunter;
        HeroReveal = Lerp(HeroReveal, 1, first || reduced ? 1 : 1 - MathF.Exp(-dt / .12f));
        SceneBlend = Lerp(SceneBlend, LauncherLobbyVisuals.Active ? 1 : 0, blend);
        if (!reduced) { Time += dt; EnergyTime += dt * Activity.PulseSpeed; }
        Span<int> previous = stackalloc int[8];
        Span<bool> previousReady = stackalloc bool[8];
        _identity.AsSpan().CopyTo(previous);
        Ready.AsSpan().CopyTo(previousReady);
        for (int i = 0; i < 8; i++)
        {
            bool occupied = LauncherLobbyVisuals.Active && (LauncherLobbyVisuals.OccupiedMask & (1 << i)) != 0;
            int identity = occupied ? LauncherLobbyVisuals.IdentityAt(i) : -1;
            bool ready = occupied && (LauncherLobbyVisuals.ReadyMask & (1 << i)) != 0;
            bool changed = _identity[i] != identity;
            if (changed)
            {
                // A stable actor moving to another presentation pad is not a
                // network join/leave. Only absent/present identity edges emit.
                int priorSlot = occupied ? previous.IndexOf(identity) : -1;
                bool arrived = occupied && priorSlot < 0;
                bool departed = previous[i] > 0;
                for (int other = 0; other < 8 && departed; other++)
                    if (LauncherLobbyVisuals.Active && (LauncherLobbyVisuals.OccupiedMask & (1 << other)) != 0
                        && LauncherLobbyVisuals.IdentityAt(other) == previous[i]) departed = false;
                JoinPulse[i] = arrived && !first && !reduced ? 1 : 0;
                LeavePulse[i] = !occupied && departed && !reduced ? 1 : 0;
                if (occupied) Occupancy[i] = 0;
                Ready[i] = priorSlot >= 0 && previousReady[priorSlot];
                ReadyPulse[i] = 0;
                _identity[i] = identity;
            }
            if (ready && !Ready[i]) ReadyPulse[i] = first || reduced ? 0 : 1;
            Ready[i] = ready;
            // Corrections remove the ready signal on this frame, including a
            // replacement occupying the same pad or a cancelled deployment.
            if (!ready) ReadyPulse[i] = 0;
            Occupancy[i] = Lerp(Occupancy[i], occupied ? 1 : 0, first || reduced ? 1 : 1 - MathF.Exp(-dt / .14f));
            ReadyPulse[i] *= reduced ? 0 : MathF.Exp(-dt / .3f);
            JoinPulse[i] *= reduced ? 0 : MathF.Exp(-dt / .24f);
            LeavePulse[i] *= reduced ? 0 : MathF.Exp(-dt / .18f);
            if (occupied)
            {
                MenuRgb color = TeamColor(LauncherLobbyVisuals.TeamAt(i), LauncherLobbyVisuals.HunterAt(i));
                SlotColors[i] = changed ? color : MenuRgb.Lerp(SlotColors[i], color, blend);
            }
        }
        // Corrections/cancellation immediately change the target; no client countdown.
        bool starting = LauncherLobbyVisuals.Active && LauncherLobbyVisuals.Starting;
        Launch = starting ? Lerp(Launch, 1, blend) : 0;
    }
    public void Suspend() => _last = double.NaN;

    public void Reset()
    {
        _last = double.NaN; _initialized = false; _session = 0;
        Time = EnergyTime = 0; Launch = SceneBlend = 0; HeroReveal = 1; Reduced = false;
        Activity = LauncherActivityAmbience.ServerBrowser;
        Theme = LauncherHunterTheme.For(Hunter.Samus);
        Array.Clear(HunterWeights);
        ClearSlots();
    }

    private void ClearSlots()
    {
        Array.Clear(Ready); Array.Clear(Occupancy); Array.Clear(ReadyPulse);
        Array.Clear(JoinPulse); Array.Clear(LeavePulse); Array.Clear(SlotColors);
        Array.Fill(_identity, -1);
    }

    private static MenuRgb TeamColor(int team, Hunter hunter) => team switch
    {
        0 => new(1f, .612f, 0f), 1 => new(.286f, .875f, .404f),
        2 => new(.161f, .612f, 1f), 3 => new(.871f, .349f, 1f),
        _ => LauncherHunterTheme.For(hunter).Rim
    };
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
