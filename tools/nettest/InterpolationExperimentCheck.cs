using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Offline policy experiment: actual interpolation history, synthetic schedules,
// perfect clock, no selected-delay wire contract and no runtime policy mutation.
internal static class InterpolationExperimentCheck
{
    private const int Duration = 7200, Warmup = 300;
    public static int Run(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: --interpolation-ab OUTPUT_JSON"); return 2; }
        var names = new[] { "lan", "stable-wan", "normal", "poor", "downlink-heavy", "uplink-heavy", "burst-transition" };
        var results = new List<object>();
        foreach (string name in names)
        foreach (int seed in new[] { 17, 43, 91, 173, 811 })
        {
            var random = new Random(seed);
            var packets = new List<Delivery>();
            var observations = new List<Observation>();
            for (uint tick = 0; tick < Duration; tick += 2)
            {
                var link = Link(name, tick);
                double down = Math.Max(0, link.Down + (random.NextDouble() * 2 - 1) * link.Jitter);
                double up = Math.Max(0, link.Up + (random.NextDouble() * 2 - 1) * link.Jitter);
                bool lost = random.NextDouble() < link.Loss;
                bool ackLost = random.NextDouble() < link.Loss;
                if (lost) continue;
                packets.Add(new(tick + down * .06, tick));
                if (!ackLost) observations.Add(new(tick + (down + up) * .06, (down + up) * .06));
            }
            packets.Sort((a,b) => a.Arrival.CompareTo(b.Arrival));
            observations.Sort((a,b) => a.Arrival.CompareTo(b.Arrival));
            var fixedSix = Run(name, false, packets, observations);
            var adaptive = Run(name, true, packets, observations);
            bool ageBenefit = name is not ("lan" or "stable-wan") || adaptive.MeanAgeMs <= fixedSix.MeanAgeMs - 1000d / 60;
            bool quality = adaptive.ExtrapolationFraction <= fixedSix.ExtrapolationFraction + .01
                && adaptive.HoldFraction <= fixedSix.HoldFraction + .005
                && adaptive.CorrectionP95 <= fixedSix.CorrectionP95 * 1.1 + .001;
            bool fairness = adaptive.ClampedFraction <= fixedSix.ClampedFraction + .005 && adaptive.ExcessAllowanceTicks == 0;
            results.Add(new { name, seed, FixedSix = fixedSix, Adaptive = adaptive, AgeGate = ageBenefit,
                QualityGate = quality, FairnessGate = fairness, MonotonicGate = adaptive.BackwardTicks == 0,
                Passed = ageBenefit && quality && fairness && adaptive.BackwardTicks == 0 });
        }
        File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"INTERPOLATION_AB cases={results.Count} candidate=server-rtt-half-plus-jitter output={args[1]}; fixed six remains the runtime default.");
        return 0;

    }

    private static Metrics Run(string name, bool adaptive, List<Delivery> packets, List<Observation> observations)
    {
        var history = new SnapshotInterpolation();
        var policy = new Policy();
        int next = 0, nextObservation = 0, samples = 0, extrapolated = 0, held = 0, clamped = 0, excess = 0;
        double age = 0, previousTick = double.NegativeInfinity, backwards = 0, maxError = 0;
        Vector3 previousError = default;
        var corrections = new List<double>();
        var profiles = new int[7];
        for (int now = 0; now < Duration; now++)
        {
            while (nextObservation < observations.Count && observations[nextObservation].Arrival <= now)
            {
                if (adaptive) policy.Observe(observations[nextObservation].RttTicks);
                nextObservation++;
            }
            if (adaptive) policy.Update(now);
            int delay = adaptive ? policy.Delay : 6;
            bool received = false;
            while (next < packets.Count && packets[next].Arrival <= now)
            {
                Delivery arrival = packets[next++];
                uint tick = arrival.Tick;
                SnapshotPlayer player = new() { Slot = 0, ConnectionId = 1, Life = 1, Health = 100,
                    Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
                    Position = Position(tick), Speed = Velocity(tick) * 2,
                    Aim = Vector3.UnitZ, Facing = Vector3.UnitZ, AvailableWeapons = 1 };
                received |= history.Add(new SnapshotPacket(tick, tick / 2, 1, 0, false, 0, 0), new[] { player }, (long)(arrival.Arrival * 1000000));
            }
            // Algebraically identical to selecting DelayTicks on the same live history.
            // Clock is deliberately perfect, so results are optimistic about clock estimation.
            if (!history.TryPreparePresentation(now + 6 - delay, out SnapshotPresentation frame)) continue;
            if (adaptive && frame.Tick < previousTick)
                history.TryPreparePresentation(previousTick + 6, out frame); // hold instead of rewinding during a delay increase
            if (!history.TrySample(0, frame, out SnapshotPlayer state)) throw new Exception("Missing sample");
            history.MarkPresented(frame); history.TryCaptureViewTick(out uint viewTick);
            Vector3 error = state.Position - Position(frame.Tick);
            if (now >= Warmup)
            {
                samples++; profiles[delay]++; age += (now - frame.Tick) * 1000 / 60;
                if (frame.Tick < previousTick) backwards += previousTick - frame.Tick;
                if (frame.Mode == SnapshotPresentationMode.Extrapolated) extrapolated++;
                if (frame.Mode == SnapshotPresentationMode.ExtrapolationHold) held++;
                maxError = Math.Max(maxError, error.Length);
                if (received) corrections.Add((error - previousError).Length);
                var link = Link(name, now);
                // Fairness uses the exact approved profile that produced this picture.
                // A production peer-profile epoch must retain that choice for in-flight inputs.
                uint fireTick = (uint)Math.Ceiling(now + link.Up * .06);
                uint requested = unchecked(fireTick - viewTick);
                uint allowance = Allowance(link.Down + link.Up, (uint)delay);
                uint classicAllowance = Allowance(link.Down + link.Up, 6);
                if (requested > allowance) clamped++;
                excess += (int)Math.Max(0, (long)allowance - classicAllowance);
            }
            previousTick = frame.Tick; previousError = error;
        }
        corrections.Sort();
        return new(samples, age / samples, extrapolated / (double)samples, held / (double)samples,
            corrections[(corrections.Count - 1) * 95 / 100], corrections.Count == 0 ? 0 : corrections[^1], maxError,
            clamped / (double)samples, excess, backwards, policy.Changes,
            policy.Changes * (24 + 5 + 4 + 8 + 24), policy.Observations, policy.Comparisons, profiles);
    }
    static uint Allowance(double rttMs, uint delay)
        => double.IsFinite(rttMs) && rttMs > 0 ? Math.Min(15u, (uint)Math.Min(15, Math.Ceiling(rttMs * .03)) + delay + 2) : delay;
    static (double Down, double Up, double Jitter, double Loss) Link(string name, double tick) => name switch
    {
        "lan" => (5, 5, 1, 0),
        "stable-wan" => (30, 30, 3, .002),
        "normal" => (50, 50, 20, .02),
        "poor" => (80, 80, 35, .05),
        "downlink-heavy" => (70, 10, 3, .002),
        "uplink-heavy" => (10, 70, 3, .002),
        "burst-transition" when tick >= 2400 && tick < 3000 => (70, 30, 35, .1),
        "burst-transition" => (5, 5, 1, .002),
        _ => throw new Exception(name)
    };
    static Vector3 Position(double tick) => new((float)(6 * Math.Sin(tick * Math.PI / 90)), (float)(2 * Math.Sin(tick * Math.PI / 43)), 0);
    static Vector3 Velocity(double tick) => new((float)(6 * Math.PI / 90 * Math.Cos(tick * Math.PI / 90)), (float)(2 * Math.PI / 43 * Math.Cos(tick * Math.PI / 43)), 0);
    record Delivery(double Arrival, uint Tick);
    record Observation(double Arrival, double RttTicks);
    record Metrics(int Samples, double MeanAgeMs, double ExtrapolationFraction, double HoldFraction,
        double CorrectionP95, double CorrectionMax, double PositionErrorMax, double ClampedFraction,
        int ExcessAllowanceTicks, double BackwardTicks, int ProfileChanges, int ProposedControlBytes,
        int PolicyObservations, int PolicyComparisons, int[] ProfileSamples);
    sealed class Policy
    {
        readonly double[] samples = new double[32];
        int count, cursor, stableSince, desired = 6;
        double smoothed;
        public int Delay { get; private set; } = 6;
        public int Changes { get; private set; }
        public int Observations { get; private set; }
        public int Comparisons { get; private set; }
        public void Observe(double rtt)
        {
            Observations++;
            smoothed = count == 0 ? rtt : smoothed + (rtt - smoothed) / 8;
            samples[cursor++ % samples.Length] = rtt;
            count = Math.Min(count + 1, samples.Length);
        }
        public void Update(int tick)
        {
            if (tick % 15 != 0 || count < samples.Length) return;
            Span<double> jitter = stackalloc double[32];
            for (int i = 0; i < count; i++) jitter[i] = Math.Abs(samples[i] - smoothed);
            for (int i = 1; i < count; i++)
            {
                double value = jitter[i]; int j = i - 1;
                while (j >= 0) { Comparisons++; if (jitter[j] <= value) break; jitter[j + 1] = jitter[j]; j--; }
                jitter[j + 1] = value;
            }
            int next = Math.Clamp((int)Math.Ceiling(smoothed / 2 + jitter[(count - 1) * 95 / 100] + 2), 3, 6);
            if (next != desired) { desired = next; stableSince = tick; }
            if (next > Delay) { Delay = next; Changes++; stableSince = tick; }
            else if (next < Delay && tick - stableSince >= 300) { Delay--; Changes++; stableSince = tick; }
        }
    }

}
