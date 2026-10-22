using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure classifier DTO benchmark only. AssemblyName nettest grants internal access.
// Does not execute full native fixture, production CurrentWorld, emission, impacts or payment.
internal static class Program
{
    const int Samples = 30, WarmupBatches = 8, Operations = 262144;
    static long Sink;
    record Identity(string Path, string Name, string Sha256, string Mvid);
    record Sample(int Index, int Operations, double WallMs, double ProcessCpuMs, long CallerAllocatedBytes);
    record CaseResult(string Name, string ExpectedValue, int OperationsPerBatch, List<Sample> Raw,
        object BatchWallMs, object BatchProcessCpuMs, double MeanCallerAllocatedBytesPerCall);
    static Identity Identify(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader(); var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
        stream.Position = 0;
        return new(Path.GetFileName(path), reader.GetString(reader.GetAssemblyDefinition().Name),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), mvid);
    }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static TimedShotCertificate Shot(bool granted)
    {
        var key = new ShotKey(11, 7, 0, 2, 3, 1);
        var fire = new FireEvent(1, 102, 100, 0, FireEventKind.PressFire, (byte)BeamType.PowerBeam,
            0, 0, FireEvent.FlagPose, Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, SourceLife: 3);
        var source = new ShotSourceEvidence(ShotProofReason.Verified, ShotProofReason.Verified, true, 0, 0, 10);
        var knowledge = granted ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted;
        var answer = new TimedGrantAnswer(knowledge, granted ? 55UL : 0);
        TimedShotFunding? funding = granted ? new(55, new(11, 7, 3, -1, 4), key with { ShotId = 0 },
            TimedPowerUp.DoubleDamage, 100, 100, 1900, 102) : null;
        var damage = new TimedShotDamageTemplate(12, 16, 0, Vector3.Zero, Vector3.UnitZ,
            .1f, 3, 0, 2, false, 0, true);
        return new(key, fire, 3, 102, TimedShotSource.NativeInput, source, answer, knowledge,
            funding, TimedShotUnavailable.None, 1, damage);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static TimedSourceCapabilities Classify(in TimedShotCertificate shot, ShotProofFamily family)
        => NetTimedSourceCapabilities.Classify(shot, family);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static TimedNativeCapability Native(ShotKey key)
        => NetTimedSourceCapabilities.NativeValue(key, 1, ShotProofFamily.Projectile, 102,
            timedScope: true, playerSource: true, weapon: (byte)BeamType.PowerBeam, room: 3);
    static object Percentiles(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        double P(double p) { double r = (sorted.Length - 1) * p; int l = (int)Math.Floor(r), h = (int)Math.Ceiling(r); return sorted[l] + (sorted[h] - sorted[l]) * (r - l); }
        return new { Median = P(.5), P95 = P(.95), P99 = P(.99), P999 = P(.999), Min = sorted[0], Max = sorted[^1],
            Method = "linear interpolation across 30 batch totals; not per-call/frame tails" };
    }
    static CaseResult Measure(string name, string expected, Action run)
    {
        for (int w = 0; w < WarmupBatches; w++) for (int i = 0; i < Operations; i++) run();
        var raw = new List<Sample>(Samples); using var process = Process.GetCurrentProcess();
        for (int sample = 0; sample < Samples; sample++)
        {
            process.Refresh(); var cpu0 = process.TotalProcessorTime;
            long alloc0 = GC.GetAllocatedBytesForCurrentThread(); long wall0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < Operations; i++) run();
            long wall1 = Stopwatch.GetTimestamp(); long alloc1 = GC.GetAllocatedBytesForCurrentThread();
            process.Refresh(); var cpu1 = process.TotalProcessorTime;
            var row = new Sample(sample, Operations, (wall1 - wall0) * 1000.0 / Stopwatch.Frequency,
                (cpu1 - cpu0).TotalMilliseconds, alloc1 - alloc0);
            raw.Add(row); Console.WriteLine($"RAW {name} {JsonSerializer.Serialize(row)}");
        }
        return new(name, expected, Operations, raw, Percentiles(raw.Select(r => r.WallMs)),
            Percentiles(raw.Select(r => r.ProcessCpuMs)), raw.Average(r => (double)r.CallerAllocatedBytes / Operations));
    }
    static void Consume(TimedSourceCapabilities value)
    { Sink += (int)value.Modifier + (int)value.ImpactExact + (value.ConstantDirectBodyChannel ? 1 : 0); }
    static int Main(string[] args)
    {
        Require(args.Length == 4, "usage: nettest.dll result.json expected-client-sha expected-client-mvid full-fixture-nettest.dll");
        var clientBefore = Identify(typeof(NetTimedSourceCapabilities).Assembly.Location);
        var entryBefore = Identify(Assembly.GetExecutingAssembly().Location); var fixture = Identify(args[3]);
        Console.WriteLine("STARTUP_IDENTITIES " + JsonSerializer.Serialize(new { QualifiedActualClient = clientBefore,
            ThinBenchmarkEntry = entryBefore, FullNativeFixtureIdentityOnly = fixture }));
        Require(clientBefore.Sha256 == args[1] && clientBefore.Mvid.Equals(args[2], StringComparison.OrdinalIgnoreCase), "Wrong frozen qualified client");
        Require(entryBefore.Sha256 != fixture.Sha256 && entryBefore.Mvid != fixture.Mvid, "Thin entry confused with full fixture");
        var granted = Shot(true); var absent = Shot(false);
        var unknown = absent with { LedgerAnswer = new(TimedGrantKnowledge.Unavailable, 0),
            Knowledge = TimedGrantKnowledge.Unavailable, Unavailable = TimedShotUnavailable.ObservationMissing };
        var supported = new TimedSourceCapabilities(TimedSourceCapability.SUPPORTED, TimedSourceCapability.UNAVAILABLE, true);
        var unavailable = new TimedSourceCapabilities(TimedSourceCapability.UNAVAILABLE, TimedSourceCapability.UNAVAILABLE, true);
        var child = new TimedSourceCapabilities(TimedSourceCapability.SHADOW_ONLY, TimedSourceCapability.SHADOW_ONLY, false);
        var homing = new TimedSourceCapabilities(TimedSourceCapability.SUPPORTED, TimedSourceCapability.SHADOW_ONLY, true);
        Require(Classify(granted, ShotProofFamily.Projectile) == supported, "Funded DTO fixture mismatch");
        Require(Classify(absent, ShotProofFamily.Projectile) == supported, "NotGranted DTO fixture mismatch");
        Require(Classify(unknown, ShotProofFamily.Projectile) == unavailable, "Unknown DTO fixture mismatch");
        Require(Classify(granted, ShotProofFamily.Child) == child, "Child DTO fixture mismatch");
        Require(Classify(granted, ShotProofFamily.Homing) == homing, "Homing DTO fixture mismatch");
        var native = Native(granted.Key);
        Require(native.SourceFrame == null && native.Capability == new TimedSourceCapabilities(
            TimedSourceCapability.UNAVAILABLE, TimedSourceCapability.UNAVAILABLE, false), "NativeValue DTO fixture mismatch");
        var cases = new List<CaseResult> {
            Measure("ordinary_funded", "SUPPORTED / UNAVAILABLE / constant body true", () => Consume(Classify(granted, ShotProofFamily.Projectile))),
            Measure("ordinary_notgranted", "SUPPORTED / UNAVAILABLE / constant body true", () => Consume(Classify(absent, ShotProofFamily.Projectile))),
            Measure("ordinary_unknown", "UNAVAILABLE / UNAVAILABLE / constant body true", () => Consume(Classify(unknown, ShotProofFamily.Projectile))),
            Measure("unsupported_child", "SHADOW_ONLY / SHADOW_ONLY / constant body false", () => Consume(Classify(granted, ShotProofFamily.Child))),
            Measure("funded_homing", "SUPPORTED / SHADOW_ONLY / constant body true", () => Consume(Classify(granted, ShotProofFamily.Homing))),
            Measure("nativevalue_ordinary_dto", "UNAVAILABLE / UNAVAILABLE / constant body false; source frame null", () => { var value = Native(granted.Key); Consume(value.Capability); Sink += value.Component; })
        };
        var clientAfter = Identify(typeof(NetTimedSourceCapabilities).Assembly.Location);
        var entryAfter = Identify(Assembly.GetExecutingAssembly().Location);
        Require(clientAfter == clientBefore && entryAfter == entryBefore, "Runtime binary changed during benchmark");
        var result = new { Scope = "Frozen NET03 pure Classify and NativeValue DTO operations only; constructed evidence establishes no authority, production CurrentWorld, native Emit, impact coverage, payment, gameplay or FPS claim.",
            Samples, WarmupBatches, OperationsPerBatch = Operations, QualifiedActualClient = clientBefore,
            ThinBenchmarkEntry = entryBefore, FullNativeFixtureIdentityOnly = fixture,
            ActualClientUnchanged = clientAfter == clientBefore, EntryUnchanged = entryAfter == entryBefore,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Stopwatch.Frequency, ProcessId = Environment.ProcessId,
            Measurement = "30 raw serial batch totals after 8 warmups. CPU includes all process threads; allocation counts caller thread only. DTO/delegate/static initialization is outside measurement. No-inline wrappers prevent loop hoisting; wrapper/delegate/consume overhead is included. No sample filtering, forced collection or outlier removal. Percentiles describe batches, not per-call/frame tails.",
            EveryMeasuredBatchZeroCallerAllocations = cases.All(c => c.Raw.All(s => s.CallerAllocatedBytes == 0)),
            Cases = cases, Sink };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BENCHMARK_COMPLETE " + Path.GetFileName(args[0])); return 0;
    }
}
