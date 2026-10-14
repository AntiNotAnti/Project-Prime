using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Constructed frozen values test channel ceilings and victim arithmetic only.
// No native From/Record, admitted shot, claim settlement, trajectory, explosion,
// authenticated transport or replay proof is supplied by this pure fixture.
internal static class TimedShotDamageTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("Frozen shot damage: " + label); _checks++; }
    private static TimedShotDamageTemplate Template(float body = 12, float head = 16,
        float splash = 4, bool balanced = false)
        => new(body, head, splash, Vector3.Zero, Vector3.UnitZ, .25f, 1, 40, 3, balanced, 0, true);
    private static HitClaimPacket Claim(BeamType beam = BeamType.Missile, bool direct = true, bool head = false)
        => new() { Beam = (byte)beam, Flags = (byte)((direct ? HitClaimPacket.FlagDirect : 0)
            | (head ? HitClaimPacket.FlagHeadshot : 0)), HitPoint = new Vector3(0, 0, 10), Damage = 1 };
    private static TimedVictimDamageProfile Profile(Effectiveness effectiveness = Effectiveness.Normal,
        Hunter hunter = Hunter.Samus, bool balanced = false, bool transitioning = false)
    {
        Span<Effectiveness> values = stackalloc Effectiveness[9]; values.Fill(effectiveness);
        if (!NetTimedDamageProfile.TryPackEffectiveness(values, out uint packed))
            throw new InvalidOperationException("Native effectiveness contract changed.");
        return new(new(11, 7, 1, 2, 3, 0), packed, 1, 0, hunter, balanced, transitioning);
    }
    private static void ChannelsAndArmour()
    {
        var value = Template(); var body = Claim(); var head = Claim(head: true);
        var splash = Claim(direct: false, head: true);
        Check(value.Channel(body, out bool nativeHead) == 12 && !nativeHead, "direct body selects frozen body amount");
        Check(value.Channel(head, out nativeHead) == 16 && nativeHead, "unequal direct head amount supplies native head flag");
        Check(value.Channel(splash, out nativeHead) == 4 && !nativeHead, "splash is independent of body/head and cannot borrow head armour bypass");
        var spire = Profile(hunter: Hunter.Spire, balanced: true);
        Check(spire.CompleteDamage(value.Channel(body, out nativeHead), BeamType.Missile, nativeHead, false) == 11,
            "frozen body12 keeps Balanced Spire armour");
        Check(spire.CompleteDamage(value.Channel(head, out nativeHead), BeamType.Missile, nativeHead, false) == 16,
            "unequal native head channel16 bypasses body armour");
        var equal = Template(56, 56, 0); var voltHead = Claim(BeamType.VoltDriver, head: true);
        Check(equal.Channel(voltHead, out nativeHead) == 56 && !nativeHead
            && spire.CompleteDamage(56, BeamType.VoltDriver, nativeHead, false) == 50,
            "equal charged Volt56/56 cannot manufacture native head flag or armour bypass");
        foreach (float difference in new[] { 0f, 1f / 8192, 1f / 4096, 2f / 4096 })
        {
            var nearEqual = Template(10, 10 + difference);
            nearEqual.Channel(head, out nativeHead);
            Check(nativeHead == (difference > 1f / 4096), "native head flag requires more than fixed-point channel equality tolerance");
        }
        var irrelevant = head; irrelevant.Damage = ushort.MaxValue; irrelevant.HitPoint = new Vector3(999, 999, 999);
        Check(value.Channel(irrelevant, out nativeHead) == 16 && nativeHead,
            "claimed oversize and claimed position do not change retained legal channel ceiling");
    }
    private static void EffectMasks()
    {
        const byte captured = HitClaimPacket.FlagFrozen | HitClaimPacket.FlagBurning | HitClaimPacket.FlagDisrupted;
        foreach (var row in new[] { (Hunter.Kanden, BeamType.VoltDriver, HitClaimPacket.FlagDisrupted),
            (Hunter.Spire, BeamType.Magmaul, HitClaimPacket.FlagBurning) })
        foreach (bool balanced in new[] { false, true }) foreach (bool charged in new[] { false, true })
        {
            byte effects = TimedShotDamageTemplate.FilterEffects(row.Item1, row.Item2, balanced, charged,
                captured, out byte mask);
            byte expectedMask = balanced && charged ? row.Item3 : (byte)0;
            byte expectedEffects = balanced && !charged ? (byte)(captured & ~row.Item3) : captured;
            var frozen = Template() with { Effects = effects, DirectOnlyEffects = mask };
            Check(effects == expectedEffects && mask == expectedMask,
                "constructed affinity effect row preserves captured unrelated bits and gates its charged Balanced effect");
            Check(frozen.EffectsFor(true) == expectedEffects && frozen.EffectsFor(false) == (byte)(expectedEffects & ~expectedMask),
                "frozen direct/splash channels respect captured mask under each charge/rule combination");
            byte absent = TimedShotDamageTemplate.FilterEffects(row.Item1, row.Item2, balanced, charged, 0, out mask);
            Check(absent == 0 && mask == 0, "hunter/rule/charge eligibility cannot invent uncaptured effects");
        }
        foreach (var row in new[] { (Hunter.Samus, BeamType.VoltDriver), (Hunter.Kanden, BeamType.Magmaul),
            (Hunter.Spire, BeamType.VoltDriver), (Hunter.Spire, BeamType.Missile) })
        foreach (bool charged in new[] { false, true })
        {
            byte effects = TimedShotDamageTemplate.FilterEffects(row.Item1, row.Item2, true, charged,
                captured, out byte mask);
            Check(effects == captured && mask == 0,
                "ordinary hunter or different weapon retains actual captured effect bits without creating affinity restrictions");
        }
        var restricted = Template() with { Effects = HitClaimPacket.FlagDisrupted, DirectOnlyEffects = HitClaimPacket.FlagDisrupted };
        Check(!restricted.SameChannels(restricted with { DirectOnlyEffects = 0 })
            && restricted.EffectsFor(true) == HitClaimPacket.FlagDisrupted && restricted.EffectsFor(false) == 0,
            "equal effect union with unequal direct-only mask cannot merge different frozen direct/splash policy");
    }
    private static void FrozenRowFacts()
    {
        var value = Template() with { Effects = HitClaimPacket.FlagDisrupted, DirectOnlyEffects = HitClaimPacket.FlagDisrupted };
        foreach (var changed in new[] { value with { Body = 13 }, value with { Head = 17 },
            value with { Splash = 5 }, value with { Origin = Vector3.UnitX },
            value with { Reach = 41 }, value with { Curve = 0 }, value with { Balanced = true },
            value with { Effects = HitClaimPacket.FlagFrozen }, value with { DirectOnlyEffects = 0 }, value with { Present = false } })
            Check(!value.SameChannels(changed), "emission cannot merge unequal frozen damage-policy row facts");
        Check(value.SameChannels(value with { Direction = Vector3.UnitX, Radius = .5f, Speed = 2 }),
            "separate component geometry is not conflated with shared damage-policy channel equality");
        var key = new ShotKey(11, 7, 0, 2, 3, 1);
        var fire = new FireEvent(1, 500, 90, 0, FireEventKind.PressFire, (byte)BeamType.Missile,
            20, FireEvent.DoubleStateBit, FireEvent.FlagPose, Vector3.Zero, Vector3.UnitZ,
            Vector3.UnitZ, Vector3.UnitZ, SourceLife: 3);
        var source = new ShotSourceEvidence(ShotProofReason.Verified, ShotProofReason.Verified, false, 1, 10, 20);
        var first = new TimedShotCertificate(key, fire, 3, 100, TimedShotSource.AcceptedFire, source,
            new(TimedGrantKnowledge.NotGranted), TimedGrantKnowledge.NotGranted, null,
            TimedShotUnavailable.None, 1, value);
        var store = new TimedShotCertificateStore();
        Check(store.Remember(first, 100) == TimedShotRetention.Stored, "constructed completed kernel facts retain");
        Check(store.Remember(first with { Damage = Template(999, 999, 999),
            Knowledge = TimedGrantKnowledge.Granted, AuthorityFrame = 200 }, 200) == TimedShotRetention.Duplicate
            && store.TryGet(key, 500, 3, 200, out var retained) && retained.Damage == value
            && retained.Fire == fire && retained.Knowledge == TimedGrantKnowledge.NotGranted,
            "same-fire later grant/kernel state cannot recompute or replace actual frozen charge/modifier result");
        Check(store.Remember(first with { Damage = value with { DirectOnlyEffects = 0 } }, 200) == TimedShotRetention.Duplicate
            && store.TryGet(key, 500, 3, 200, out retained) && retained.Damage.EffectsFor(true) == HitClaimPacket.FlagDisrupted
            && retained.Damage.EffectsFor(false) == 0, "later equal-effect-union mask cannot broaden retained splash eligibility");
        Check(store.Remember(first with { Fire = fire with { Charge = 0 } }, 200) == TimedShotRetention.Alias
            && store.TryGet(key, 500, 3, 200, out retained) && retained.Damage.Channel(Claim(), out _) == 12,
            "changed authored charge cannot borrow or mutate prior completed channel facts");
    }
    private static void LegalCeilingsAndDistanceCounterexample()
    {
        foreach (var beam in new[] { BeamType.VoltDriver, BeamType.Magmaul })
        {
            var value = Template(10, 14, 3, balanced: true); var claim = Claim(beam);
            float ceiling = value.Channel(claim, out bool head);
            Check(Math.Abs(ceiling - 12) < .00001f && !head, "Balanced legal channel ceiling includes both range endpoints");
            foreach (float distance in new[] { 0f, 5f, 10f, 20f, 40f, float.MaxValue })
                Check(BalancedModeRules.ScaleRangeDamage(beam, 10, distance) <= ceiling,
                    "legal middle/near/far native multiplier is bounded without inventing impact distance");
            Check(value.Channel(Claim(beam, direct: false), out head) >=
                BalancedModeRules.ScaleRangeDamage(beam, 3, 0) && !head,
                "splash keeps its own legal ceiling and supplies no head flag; exact blast radius remains unproved");
        }
        Check(BalancedModeRules.ScaleRangeDamage(BeamType.VoltDriver, 10, 0)
            < BalancedModeRules.ScaleRangeDamage(BeamType.VoltDriver, 10, 40),
            "distance-zero Volt amount is not a safe ceiling for a legal far hit");
        var linear = Template(100, 100, 0) with { Reach = 10, Curve = 0 };
        var reportedBody = Claim(); // Claim HitPoint is victim body origin at Z10.
        float nativeCollisionPositionAmount = BeamProjectileEntity.GetInterpolatedValue(0, 100, 0, 9f / 10);
        float claimedBodyOriginGuess = BeamProjectileEntity.GetInterpolatedValue(0, 100, 0, 10f / 10);
        Check(nativeCollisionPositionAmount > claimedBodyOriginGuess && claimedBodyOriginGuess == 0
            && linear.Channel(reportedBody, out _) == 100,
            "constructed body-origin/collision-position counterexample refutes exact falloff reconstruction, while legal ceiling stays100");
        Console.WriteLine("LIMITATION: Channel is a legal per-launch ceiling, not impact-exact damage. Claim.HitPoint is body origin; native direct interpolation uses projectile position, Balanced uses collision position, splash uses blast centre/radius. NET02 full acceptance remains gated on trusted trajectory/explosion facts.");
    }
    private static void ZeroAndMalformed()
    {
        var claim = Claim(); var profile = Profile();
        Check(Template(0, 0, 0).Channel(claim, out bool head) == 0 && !head
            && profile.CompleteDamage(0, BeamType.Missile, false, false) == 0,
            "zero frozen channel cannot become minimum-one phantom damage");
        Check(Template(-1).Channel(claim, out _) == 0, "negative constructed channel clamps to zero ceiling");
        foreach (float malformed in new[] { float.NaN, float.PositiveInfinity })
        {
            float channel = Template(malformed).Channel(claim, out _); bool refused = false;
            try { profile.CompleteDamage(channel, BeamType.Missile, false, false); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check(!float.IsFinite(channel) && refused, "nonfinite frozen channel cannot fabricate a finite victim payout");
        }
    }
    public static int Run()
    {
        try { _checks = 0;
            Check(NetTimedShotProof.Mode is TimedGrantProofMode.Off or TimedGrantProofMode.Shadow,
                "production configuration cannot select unqualified enforcement");
            ChannelsAndArmour(); EffectMasks(); FrozenRowFacts(); LegalCeilingsAndDistanceCounterexample(); ZeroAndMalformed();
            Console.WriteLine($"PASS: {_checks} pure constructed frozen-channel/ceiling assertions; no native/admission/impact proof."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private enum Workload { Channel, ChannelAndProfile }
    private readonly record struct Case(TimedShotDamageTemplate Damage, HitClaimPacket Claim, TimedVictimDamageProfile Profile);
    private readonly record struct Sample(string Workload, int Index, int Operations, long StopwatchTicks,
        double ElapsedMilliseconds, double NanosecondsPerOperation, long ProcessCpuTicks,
        double ProcessCpuMilliseconds, long ThreadAllocatedBytes, ulong Checksum);
    private static Case[] Cases() => new[] {
        new Case(Template(), Claim(), Profile()),
        new Case(Template(), Claim(head: true), Profile(hunter: Hunter.Spire, balanced: true)),
        new Case(Template(56, 56, 3, true), Claim(BeamType.VoltDriver, head: true), Profile(Effectiveness.Half)),
        new Case(Template(14, 18, 4), Claim(BeamType.Magmaul, direct: false), Profile(hunter: Hunter.Weavel, balanced: true, transitioning: true)) };
    private static ulong Loop(Workload workload, Case[] cases, int iterations)
    {
        ulong checksum = 0;
        for (int i = 0; i < iterations; i++)
        {
            ref readonly var value = ref cases[i & 3];
            float channel = value.Damage.Channel(value.Claim, out bool head);
            checksum += workload == Workload.Channel ? BitConverter.SingleToUInt32Bits(channel)
                : value.Profile.CompleteDamage(channel, (BeamType)value.Claim.Beam, head, false);
        }
        return checksum;
    }
    public static int RunBenchmark(string[] args)
    {
        try
        {
            const int count = 5, warmup = 10000, iterations = 20000;
            var cases = Cases(); var rows = new Sample[2 * count]; int at = 0;
            using var process = Process.GetCurrentProcess();
            foreach (var workload in Enum.GetValues<Workload>())
            {
                Loop(workload, cases, warmup); ulong expected = Loop(workload, cases, iterations);
                for (int sample = 0; sample < count; sample++)
                {
                    process.Refresh(); long cpuBefore = process.TotalProcessorTime.Ticks;
                    long bytesBefore = GC.GetAllocatedBytesForCurrentThread(); long start = Stopwatch.GetTimestamp();
                    ulong checksum = Loop(workload, cases, iterations);
                    long ticks = Stopwatch.GetTimestamp() - start;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                    process.Refresh(); long cpu = process.TotalProcessorTime.Ticks - cpuBefore;
                    Check(checksum == expected && allocated == 0, "pure arithmetic checksum stays exact with zero measured caller-thread allocations");
                    rows[at++] = new(workload.ToString(), sample, iterations, ticks,
                        ticks * 1000.0 / Stopwatch.Frequency, ticks * 1e9 / Stopwatch.Frequency / iterations,
                        cpu, cpu / (double)TimeSpan.TicksPerMillisecond, allocated, checksum);
                }
            }
            string json = JsonSerializer.Serialize(new { scope = "constructed frozen-channel ceiling and victim arithmetic only",
                excluded = "setup/serialization/CPU queries/actual certificate lookup/claim validation/native collision/transport/replay",
                runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                stopwatchFrequency = Stopwatch.Frequency, warmupOperationsPerWorkload = warmup, operationsPerBatch = iterations,
                samplesPerWorkload = count, quantileQualification = "Five raw batch averages. Nearest-rank p95/p99/p99.9 all select maximum; these are not individual-operation, frame, GPU or FPS quantiles.",
                cpuQualification = "Process CPU includes runtime/background threads; this is not owner-thread CPU. Caller-thread allocation measures the warmed arithmetic loop only.",
                results = rows }, new JsonSerializerOptions { WriteIndented = true });
            if (args.Length > 1) { string path = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json); }
            Console.WriteLine(json); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
