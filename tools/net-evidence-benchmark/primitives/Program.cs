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

// Constructed primitive storage/scope costs. This friend entry establishes no
// actual authority session, native contact, payment, impact admission or FPS.
internal static class Program
{
    const int Samples = 30, WarmupBatches = 8, ScopeOperations = 262144, Recorders = 32;
    static long Sink;
    static readonly ShotKey Key = new(11, 7, 0, 2, 3, 1);
    static readonly ShotPaymentKey PaymentKey = new(Key, 1, 2, 3, 1, 1);
    static readonly CombatAckEntry Outcome = new() { ShotId = 1, ClaimId = 9, Result = 0,
        VictimSlot = 1, VictimGeneration = 2, VictimLife = 3, DamageApplied = 12,
        HealthAfter = 87, HalfturretHealthAfter = 40, DamageSequence = 19, Flags = CombatAckFlags.OutcomePresent };
    static readonly HitClaimPacket Claim = new() { AuthorityEpoch = 11, MatchId = 7,
        ShooterGeneration = 2, ShooterLifeId = 3, ShotId = 1, ComponentId = 1, ClaimId = 9,
        VictimSlot = 1, VictimGeneration = 2, VictimLifeId = 3, Damage = 12,
        Frame = 102, AckFrame = 100, LaunchFrame = 101, Beam = (byte)BeamType.PowerBeam, HitPoint = Vector3.UnitZ };
    static readonly NativeImpactWitness Witness = new(PaymentKey, (byte)BeamType.PowerBeam, 102,
        Vector3.UnitZ, 12, false, Vector3.Zero, Vector3.Zero, Vector3.UnitZ, true, true);
    static readonly TimedJoinClaim ClaimRow = new(1, 102, Key, Claim);
    static readonly TimedJoinPayment PaymentRow = new(2, 101, 102, PaymentKey, (byte)BeamType.PowerBeam,
        TimedJoinLane.Native, 0, TimedJoinOutcome.From(Outcome), 0, false, Witness.WithOutcome(Outcome));
    record Identity(string File, string Name, string Sha256, string Mvid);
    record Sample(int Index, int Operations, double WallMs, double ProcessCpuMs, long CallerAllocatedBytes,
        double SetupWallMs, long SetupCallerAllocatedBytes);
    record CaseResult(string Name, string Unit, string SetupPolicy, int OperationsPerBatch,
        List<Sample> Raw, object BatchWallMs, object BatchProcessCpuMs, double MeanCallerAllocatedBytesPerUnit);
    static Identity Identify(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader(); var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
        stream.Position = 0;
        return new(Path.GetFileName(path), reader.GetString(reader.GetAssemblyDefinition().Name),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), mvid);
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static object Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double P(double p) { double r = (sorted.Length - 1) * p; int lo = (int)Math.Floor(r), hi = (int)Math.Ceiling(r); return sorted[lo] + (sorted[hi] - sorted[lo]) * (r - lo); }
        return new { Median = P(.5), P95 = P(.95), P99 = P(.99), P999 = P(.999), Min = sorted[0], Max = sorted[^1],
            Method = "linear interpolation across 30 batch totals; not operation/frame tails" };
    }
    static CaseResult Measure<T>(string name, string unit, string setupPolicy, int operations,
        Func<T> setup, Action<T> run, Action<T> validate)
    {
        for (int w = 0; w < WarmupBatches; w++) { var state = setup(); run(state); validate(state); }
        var raw = new List<Sample>(Samples); using var process = Process.GetCurrentProcess();
        for (int sample = 0; sample < Samples; sample++)
        {
            long setupAlloc0 = GC.GetAllocatedBytesForCurrentThread(), setupWall0 = Stopwatch.GetTimestamp();
            var state = setup();
            long setupWall1 = Stopwatch.GetTimestamp(), setupAlloc1 = GC.GetAllocatedBytesForCurrentThread();
            process.Refresh(); var cpu0 = process.TotalProcessorTime;
            long alloc0 = GC.GetAllocatedBytesForCurrentThread(), wall0 = Stopwatch.GetTimestamp();
            run(state);
            long wall1 = Stopwatch.GetTimestamp(), alloc1 = GC.GetAllocatedBytesForCurrentThread();
            process.Refresh(); var cpu1 = process.TotalProcessorTime;
            validate(state); // Assertions/projection/enumeration are outside the measured interval.
            var row = new Sample(sample, operations, (wall1 - wall0) * 1000.0 / Stopwatch.Frequency,
                (cpu1 - cpu0).TotalMilliseconds, alloc1 - alloc0,
                (setupWall1 - setupWall0) * 1000.0 / Stopwatch.Frequency, setupAlloc1 - setupAlloc0);
            raw.Add(row); Console.WriteLine($"RAW {name} {JsonSerializer.Serialize(row)}");
        }
        return new(name, unit, setupPolicy, operations, raw, Percentiles(raw.Select(r => r.WallMs)),
            Percentiles(raw.Select(r => r.ProcessCpuMs)), raw.Average(r => (double)r.CallerAllocatedBytes / operations));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void DisabledHooks()
    {
        NetTimedCampaignJoin.Certificate(default, TimedShotRetention.Stored);
        NetTimedCampaignJoin.Component(default, true, false);
        using var scope = NetTimedCampaignJoin.Claim(0, Claim);
        NetTimedCampaignJoin.Terminal(0, Key, Outcome);
        NetTimedCampaignJoin.Payment(PaymentKey, 0, 101, Outcome, 0);
        NetTimedCampaignJoin.Continuous(default, 101, Outcome, 0, 0, 0);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RequestScope()
    {
        using var scope = NetTimedCampaignJoin.ClaimScope.Enter(0, 9, Key);
        if (NetTimedCampaignJoin.TryRequestKey(0, 9, out var value)) Sink += value.ShotId;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void WitnessScope()
    {
        using var scope = new NetNativeImpactWitness.Scope(Witness);
        if (NetNativeImpactWitness.TryCapture(out var value)) Sink += value.NativeChannel;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void AppendPair(TimedJoinRecorder recorder)
    {
        recorder.Claims.Add(ClaimRow with { Ordinal = recorder.Next() });
        recorder.Payments.Add(PaymentRow with { Ordinal = recorder.Next() });
    }
    static TimedJoinRecorder[] PreparePages(int seedRows)
    {
        var recorders = new TimedJoinRecorder[Recorders];
        for (int r = 0; r < recorders.Length; r++)
        {
            var recorder = recorders[r] = new TimedJoinRecorder(2048);
            for (int i = 0; i < seedRows; i++) AppendPair(recorder);
        }
        return recorders;
    }
    static void AssertPages(TimedJoinRecorder[] recorders, int count)
    {
        long rowBytes = Unsafe.SizeOf<TimedJoinClaim>() + Unsafe.SizeOf<TimedJoinPayment>();
        foreach (var r in recorders)
        {
            Require(r.Claims.Count == count && r.Payments.Count == count && r.Claims.Observed == count
                && r.Payments.Observed == count && r.Claims.Dropped == 0 && r.Payments.Dropped == 0, "Append counts/drop mismatch");
            Require(r.AllocatedBytes == rowBytes * (count <= 1024 ? 1024 : 2048), "Actual page payload allocation mismatch");
            Require(r.Claims.Rows().First() == ClaimRow && r.Payments.Rows().First() == PaymentRow, "First retained row overwritten");
            Require(r.Claims.Rows().Last().Ordinal == (ulong)(count * 2 - 1)
                && r.Payments.Rows().Last().Ordinal == (ulong)(count * 2), "Exact final ordinal not retained");
        }
    }
    static void AssertScopes()
    {
        Require(!NetTimedCampaignJoin.TryRequestKey(0, 9, out _) && !NetNativeImpactWitness.TryCapture(out _), "Initial scope leaked");
        using (var prior = NetTimedCampaignJoin.ClaimScope.Enter(0, 9, Key with { LifeId = 4 }))
        {
            using (var inner = NetTimedCampaignJoin.ClaimScope.Enter(0, 9, Key))
            {
                Require(NetTimedCampaignJoin.TryRequestKey(0, 9, out var exact) && exact == Key, "Exact full incoming key mismatch");
                Require(!NetTimedCampaignJoin.TryRequestKey(1, 9, out _) && !NetTimedCampaignJoin.TryRequestKey(0, 8, out _), "Wrong slot/id matched");
            }
            Require(NetTimedCampaignJoin.TryRequestKey(0, 9, out var restored) && restored == (Key with { LifeId = 4 }), "Prior claim scope lost");
        }
        var priorValue = Witness with { Frame = 103 };
        using (var prior = new NetNativeImpactWitness.Scope(priorValue))
        {
            using (var inner = new NetNativeImpactWitness.Scope(Witness))
                Require(NetNativeImpactWitness.TryCapture(out var exact) && exact == Witness, "Exact witness mismatch");
            Require(NetNativeImpactWitness.TryCapture(out var restored) && restored == priorValue, "Prior witness scope lost");
            using (var invalid = new NetNativeImpactWitness.Scope(Witness with { NativeChannel = 0 }))
                Require(!NetNativeImpactWitness.TryCapture(out _), "Invalid constructed witness accepted");
        }
        Require(!NetTimedCampaignJoin.TryRequestKey(0, 9, out _) && !NetNativeImpactWitness.TryCapture(out _), "Disposed scope leaked");
    }
    static int Main(string[] args)
    {
        Require(args.Length == 6, "usage: entry result.json client-sha client-mvid full-fixture.dll fixture-sha fixture-mvid");
        var client = Identify(typeof(NetTimedCampaignJoin).Assembly.Location);
        var entry = Identify(Assembly.GetExecutingAssembly().Location); var fixture = Identify(args[3]);
        var dependencyPaths = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Order().ToArray();
        var dependencies = dependencyPaths.Select(Identify).ToArray();
        Require(client.Sha256 == args[1] && client.Mvid.Equals(args[2], StringComparison.OrdinalIgnoreCase), "Wrong qualified engine");
        Require(fixture.Sha256 == args[4] && fixture.Mvid.Equals(args[5], StringComparison.OrdinalIgnoreCase), "Wrong qualified full fixture identity");
        Require(entry.Sha256 != fixture.Sha256 && entry.Mvid != fixture.Mvid, "Thin entry confused with full fixture");
        Require(!NetCampaignEvidence.Enabled, "Diagnostics must be disabled before loading engine types");
        Require(NetShotProof.Mode == ShotProofMode.Shadow, "Inherited proof promotion not permitted");
        // Startup-only private check establishes disabled hooks did not allocate the recorder.
        // No reflection appears in the measured loops.
        var recorderField = typeof(NetTimedCampaignJoin).GetField("_recorder", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require(recorderField != null && recorderField.GetValue(null) == null, "Unexpected existing diagnostic recorder");
        DisabledHooks(); Require(recorderField.GetValue(null) == null, "Disabled hook allocated recorder");
        AssertScopes(); RequestScope(); WitnessScope();
        var cases = new List<CaseResult>();
        cases.Add(Measure("diagnostics_disabled_hook_bundle", "six-hook bundle", "DTO/static initialization outside measurement",
            ScopeOperations, () => 0, _ => { for (int i = 0; i < ScopeOperations; i++) DisabledHooks(); },
            state => Require(recorderField.GetValue(null) == null && !NetTimedCampaignJoin.TryRequestKey(0, 9, out _), "Disabled hook mutated recorder/request")));
        cases.Add(Measure("claim_scope_exact_key", "enter/read/dispose", "constructed immutable key; no real Receive/admission", ScopeOperations,
            () => Sink, before => { for (int i = 0; i < ScopeOperations; i++) RequestScope(); },
            before => Require(Sink - before == ScopeOperations && !NetTimedCampaignJoin.TryRequestKey(0, 9, out _), "Claim scope read/restoration mismatch")));
        cases.Add(Measure("constructed_witness_scope", "enter/capture/dispose", "valid constructed value; no actual native collision", ScopeOperations,
            () => Sink, before => { for (int i = 0; i < ScopeOperations; i++) WitnessScope(); },
            before => Require(Sink - before == 12L * ScopeOperations && !NetNativeImpactWitness.TryCapture(out _), "Witness scope read/restoration mismatch")));
        cases.Add(Measure("typed_claim_payment_in_page", "claim+payment append pair", "32 recorders and first claim/payment pages prepared per batch; setup costs separate",
            Recorders * 1023, () => PreparePages(1), values => { foreach (var r in values) for (int i = 0; i < 1023; i++) AppendPair(r); },
            values => AssertPages(values, 1024)));
        cases.Add(Measure("typed_claim_payment_new_page", "claim+payment page-boundary append pair", "32 recorders with 1024 existing rows per channel; timed append includes new arrays/list growth",
            Recorders, () => PreparePages(1024), values => { foreach (var r in values) AppendPair(r); },
            values => AssertPages(values, 1025)));
        long fullSetupAlloc0 = GC.GetAllocatedBytesForCurrentThread(), fullSetupWall0 = Stopwatch.GetTimestamp();
        var rowFull = new TimedJoinRecorder(1); rowFull.Claims.Add(ClaimRow);
        var byteFull = new TimedJoinRecorder(int.MaxValue);
        while (byteFull.Payments.Dropped == 0) byteFull.Payments.Add(PaymentRow);
        long fullSetupWall1 = Stopwatch.GetTimestamp(), fullSetupAlloc1 = GC.GetAllocatedBytesForCurrentThread();
        long budget = byteFull.AllocatedBytes; int retained = byteFull.Payments.Count;
        Require(budget <= TimedJoinRecorder.MaximumBytes && budget + 1024L * Unsafe.SizeOf<TimedJoinPayment>() > TimedJoinRecorder.MaximumBytes,
            "Actual shared byte cap not reached");
        cases.Add(Measure("full_row_and_byte_budget_refusal", "row-cap+byte-cap refused pair", "real recorder payload cap filled once outside measurement; one-time costs reported separately",
            ScopeOperations, () => (RowObserved: rowFull.Claims.Observed, RowDropped: rowFull.Claims.Dropped,
                ByteObserved: byteFull.Payments.Observed, ByteDropped: byteFull.Payments.Dropped),
            _ => { for (int i = 0; i < ScopeOperations; i++) { rowFull.Claims.Add(ClaimRow with { Ordinal = 99 }); byteFull.Payments.Add(PaymentRow with { Ordinal = 99 }); } },
            before => {
                Require(rowFull.Claims.Count == 1 && rowFull.Claims.Observed == before.RowObserved + ScopeOperations
                    && rowFull.Claims.Dropped == before.RowDropped + ScopeOperations && rowFull.Claims.Rows().First() == ClaimRow, "Row refusal lost counters/retained row");
                Require(byteFull.Payments.Count == retained && byteFull.AllocatedBytes == budget
                    && byteFull.Payments.Observed == before.ByteObserved + ScopeOperations && byteFull.Payments.Dropped == before.ByteDropped + ScopeOperations
                    && byteFull.Payments.Rows().First().Ordinal == PaymentRow.Ordinal, "Byte refusal lost counters/retained rows");
            }));
        Require(byteFull.Payments.Rows().All(row => row.Ordinal == PaymentRow.Ordinal), "Byte-cap refusal overwrote retained rows");
        Require(cases.Where(c => c.Name != "typed_claim_payment_new_page").All(c => c.Raw.All(s => s.CallerAllocatedBytes == 0)),
            "Unexpected caller allocations in warmed disabled/scope/in-page/refusal primitives");
        Require(cases.Single(c => c.Name == "typed_claim_payment_new_page").Raw.All(s => s.CallerAllocatedBytes > 0), "New page did not allocate");
        Require(Identify(typeof(NetTimedCampaignJoin).Assembly.Location) == client && Identify(Assembly.GetExecutingAssembly().Location) == entry
            && Identify(args[3]) == fixture && dependencyPaths.Select(Identify).SequenceEqual(dependencies), "Physical identity changed");
        var result = new { Schema = 1,
            Scope = "Constructed value primitives only: diagnostics-disabled hook bundle, exact-key/witness scopes, opt-in recorder storage and refusal. No authority session, native collision/Emit/payment, geometry admission, replay, frame-step, gameplay or FPS claim. Storage is the production opt-in recorder implementation; it is not active Shadow gameplay.",
            Samples, WarmupBatches, FixedFixtureSeed = 11, QualifiedActualClient = client, ThinBenchmarkEntry = entry,
            FullNativeFixtureIdentityOnly = fixture, PhysicalRuntimeDependencies = dependencies,
            ActualClientUnchanged = true, EntryUnchanged = true, FullFixtureUnchanged = true, DependenciesUnchanged = true,
            SemanticAssertionsPassed = true, DisabledRecorderStayedAbsent = recorderField.GetValue(null) == null,
            ProofMode = NetShotProof.Mode.ToString(),
            ByteCapSetup = new { WallMs = (fullSetupWall1 - fullSetupWall0) * 1000.0 / Stopwatch.Frequency,
                CallerAllocatedBytes = fullSetupAlloc1 - fullSetupAlloc0, RetainedRows = retained,
                PayloadBytes = budget, MaximumPayloadBytes = TimedJoinRecorder.MaximumBytes,
                Note = "Payload budget excludes array/list/object headers; caller allocations include those. Initial refusal also occurs in setup." },
            RowSizes = new { Claim = Unsafe.SizeOf<TimedJoinClaim>(), Payment = Unsafe.SizeOf<TimedJoinPayment>() },
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Stopwatch.Frequency,
            Measurement = "30 raw serial batch totals after eight warmups per case. Process CPU includes all threads; allocation bytes count the caller thread. Delegate/no-inline wrapper/consume and loop costs included. Preparation, assertions, reflection, enumeration and JSON reporting outside timed intervals. Prior setup/validation GC can affect later timings: no forced collection or sample filtering. Median/p95/p99/p99.9 describe batch totals, never per-operation/frame tails.",
            Cases = cases, Sink };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BENCHMARK_COMPLETE " + Path.GetFileName(args[0])); return 0;
    }
}
