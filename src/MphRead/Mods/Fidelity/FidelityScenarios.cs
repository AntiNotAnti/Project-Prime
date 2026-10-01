using System;
using System.Collections.Generic;
using MphRead.Mods.Network;
using MphRead.Mods.Render;

namespace MphRead.Mods.Fidelity;

internal static class FidelityScenarios
{
    internal static readonly FidelityScenario[] All =
    {
        new("simulation.rng", 1, 600, 123456, "none", 1, FidelityTier.F1, "Production MatchRandom streams; no entities or assets."),
        new("simulation.fixed-step", 1, 600, 123456, "none", 1, FidelityTier.F1, "Production FrameTiming drives MatchRandom at 60 Hz across presentation rates; no player simulation."),
        new("simulation.pause-resume", 1, 8, 0, "none", 1, FidelityTier.F1, "Production clock reset, stalled-frame and catch-up policies; no player simulation."),
        new("identity.spawn-life", 1, 5, 0, "none", 1, FidelityTier.F1, "Production lifecycle tracker rejects resurrection and previous lives."),
        new("identity.slot-reuse", 1, 5, 0, "none", 1, FidelityTier.F1, "Production lifecycle tracker fences occupants and wraps nonzero generations."),
        new("identity.match-epoch", 1, 7, 0, "none", 1, FidelityTier.F1, "Production match control fences stale epochs, serial match wrap and ended-round reopening; socket-free playback admission."),
        new("weapon.continuous-phase", 1, 12, 0, "none", 1, FidelityTier.F1, "Production continuous firing clock under delayed/repeated intent; not projectile or damage validation.")
    };
    internal static List<FidelityCheckpoint> Capture(FidelityScenario scenario, int presentationHz)
    {
        var points = new List<FidelityCheckpoint>();
        switch (scenario.Id)
        {
            case "simulation.rng":
            case "simulation.fixed-step":
                var random = new MatchRandom(); random.SetRng1(scenario.Seed); random.SetRng2(scenario.Seed ^ 0xa5a5a5a5);
                void Step(int tick)
                {
                    uint first = random.GetRandomInt1(1000), second = random.GetRandomInt2(4096);
                    points.Add(FidelityOracle.Point(tick, ("rng1", random.Rng1), ("rng2", random.Rng2), ("draw1", first), ("draw2", second)));
                }
                if (scenario.Id == "simulation.rng") for (int tick = 1; tick <= scenario.Ticks; tick++) Step(tick);
                else
                {
                    if (presentationHz < 30 || presentationHz > 1000) throw new ArgumentOutOfRangeException(nameof(presentationHz));
                    FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
                    try
                    {
                        int tick = 0, frame = 0;
                        while (tick < scenario.Ticks && frame < presentationHz * 12)
                        {
                            frame++;
                            int steps = FrameTiming.Advance(1.0 / presentationHz);
                            for (int step = 0; step < steps && tick < scenario.Ticks; step++) Step(++tick);
                        }
                        double elapsed = frame / (double)presentationHz;
                        if (tick != scenario.Ticks || Math.Abs(elapsed - 10) > 2.0 / presentationHz
                            || FrameTiming.DroppedSteps != 0 || FrameTiming.Stalls != 0)
                            throw new InvalidOperationException("Production clock did not deliver 600 steps in ten seconds within presentation quantization.");
                    }
                    finally { FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); }
                }
                break;
            case "simulation.pause-resume":
                FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
                try
                {
                    double[] deltas = { 1.0 / 120, 1.0 / 120, 1.0 / 60, 0, 1.0 / 120, 0.3, 0.1, 1.0 / 60 };
                    for (int i = 0; i < deltas.Length; i++)
                    {
                        if (i == 3) FrameTiming.Reset(); // resume discards the paused accumulator
                        int steps = FrameTiming.Advance(deltas[i]);
                        points.Add(FidelityOracle.Point(i + 1, ("steps", steps), ("alpha4096", FidelityOracle.Normalize(FrameTiming.Alpha)),
                            ("totalSteps", FrameTiming.TotalSteps), ("stalls", FrameTiming.Stalls), ("dropped", FrameTiming.DroppedSteps)));
                    }
                }
                finally { FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); }
                break;
            case "identity.spawn-life":
            case "identity.slot-reuse":
                var tracker = new NetLifecycleTracker(); tracker.SetOccupant(ushort.MaxValue); tracker.BeginLife();
                void Identity(int tick, LifecycleRejection rejection = LifecycleRejection.None, bool newLife = false)
                    => points.Add(FidelityOracle.Point(tick, ("generation", tracker.Generation), ("life", tracker.LifeId),
                        ("state", (byte)tracker.State), ("rejection", (byte)rejection), ("newLife", newLife ? 1 : 0)));
                Identity(1);
                var rejected = tracker.Accept(ushort.MaxValue, 1, NetworkPlayerState.Dead, out bool changed); Identity(2, rejected, changed);
                rejected = tracker.Accept(ushort.MaxValue, 1, NetworkPlayerState.Alive, out changed); Identity(3, rejected, changed);
                if (scenario.Id == "identity.slot-reuse") tracker.SetOccupant(NetLifecycleTracker.Next(tracker.Generation));
                tracker.BeginLife(); Identity(4);
                rejected = tracker.Accept(ushort.MaxValue, 1, NetworkPlayerState.Alive, out changed); Identity(5, rejected, changed);
                break;
            case "identity.match-epoch":
                if (NetSession.Active) throw new InvalidOperationException("Identity oracle requires a fresh session.");
                NetSession.StartPlayback();
                try
                {
                    const ulong epoch = (1UL << 63) + 4;
                    (ushort Match, ulong Epoch, bool Ending)[] controls = { (ushort.MaxValue, epoch, false),
                        (1, epoch - 1, false), (1, epoch, false), (ushort.MaxValue, epoch, false),
                        (1, epoch, true), (1, epoch, false), (1, epoch + 1, false) };
                    for (int i = 0; i < controls.Length; i++)
                    {
                        var control = controls[i];
                        NetSession.ApplyMatchState(new MatchStatePacket { MatchId = control.Match,
                            AuthorityEpoch = control.Epoch, Flags = control.Ending ? MatchStatePacket.FlagEnding : (byte)0 }, false);
                        points.Add(FidelityOracle.Point(i + 1, ("match", NetSession.CurrentMatchId),
                            ("epochHigh", (uint)(NetSession.AuthorityEpoch >> 32)), ("epochLow", (uint)NetSession.AuthorityEpoch),
                            ("ending", NetSession.ServerMatch?.Ending == true ? 1 : 0)));
                    }
                }
                finally { NetSession.Stop(); }
                break;
            case "weapon.continuous-phase":
                var phase = new ContinuousWeaponPhase(1);
                for (int i = 0; i < 12; i++)
                {
                    ulong scene = (ulong)(1000 + i);
                    if (i == 8) phase.ResetSlot(0);
                    phase.Observe(0, scene, i != 6, true);
                    uint source = i < 4 ? 100u : i < 8 ? 102u : 400u;
                    ulong tick = phase.Resolve(0, scene, true, false, 9000, true, source, (uint)(i % 3), 0, out bool shared, out _);
                    points.Add(FidelityOracle.Point(i + 1, ("phase", (long)tick), ("shared", shared ? 1 : 0)));
                }
                break;
            default: throw new ArgumentException("No oracle capture for " + scenario.Id);
        }
        return points;
    }
}
