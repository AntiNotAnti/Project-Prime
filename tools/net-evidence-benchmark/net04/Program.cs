using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// AssemblyName nettest is solely the production friend-assembly access mechanism.
// This entry does not contain or execute the full native nettest fixture.
internal static class Program
{
    const int Seed = 0x21613, Samples = 30, WarmupBatches = 8;
    static long Sink;
    record Identity(string Path, string Name, string Sha256, string Mvid);
    record Sample(int Index, int Operations, double WallMs, double ProcessCpuMs, long CallerAllocatedBytes);
    record CaseResult(string Name, string SetupPolicy, int OperationsPerBatch, List<Sample> Raw,
        object BatchWallMs, object BatchProcessCpuMs, double MeanCallerAllocatedBytesPerCall);
    static Identity Identify(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
        stream.Position = 0;
        return new(Path.GetFileName(path), reader.GetString(reader.GetAssemblyDefinition().Name),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), mvid);
    }
    static void Require(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); }
    static ReplayTimedShotFact Fact(uint shot, int slot, uint authority, Random entropy)
    {
        var key = new ShotKey(23, 7, slot, 2, 1, shot);
        var fire = new FireEvent(shot, (uint)entropy.NextInt64(1, uint.MaxValue), 88, 17,
            FireEventKind.PressFire, (byte)BeamType.PowerBeam, (byte)entropy.Next(256), 0,
            FireEvent.FlagPose, new Vector3(1, 2, 3), Vector3.UnitX, Vector3.UnitX,
            Vector3.UnitX, new Vector2(.2f, .3f), SourceLife: 1);
        bool granted = (shot & 1) == 0;
        var source = new ShotSourceEvidence(ShotProofReason.Verified, ShotProofReason.Verified, !granted, 0, 1, 8);
        TimedShotFunding? funding = granted ? new TimedShotFunding(19,
            new PowerUpSpawnKey(23, 7, 41, 5, 3), key with { ShotId = 0 },
            TimedPowerUp.DoubleDamage, 70, 65, 1870, 95) : null;
        var cert = new TimedShotCertificate(key, fire, 41, authority, TimedShotSource.AcceptedFire,
            source, new TimedGrantAnswer(granted ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted, granted ? 19ul : 0ul),
            granted ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted,
            funding, TimedShotUnavailable.None, 1,
            new TimedShotDamageTemplate(6 + (float)entropy.NextDouble(), 9, 2, Vector3.Zero,
                Vector3.UnitX, .25f, 1, 50, 0, false, 0, true));
        return ReplayTimedShotFact.From(cert, TimedGrantProofMode.Shadow);
    }
    static ReplayTimedShotState Fill(ReplayTimedShotFact[] facts, uint now = 1000)
    {
        var state = new ReplayTimedShotState();
        Require(state.Begin(23, 7, 41, TimedGrantProofMode.Shadow), "Begin rejected constructed scope");
        foreach (var fact in facts) Require(state.Remember(fact, now) == TimedShotRetention.Stored, "Fill failed");
        Require(state.Count == facts.Length, "Unexpected fixture count");
        return state;
    }
    static ReplayReplicaState Replica(ReplayTimedShotFact[] facts)
    {
        var state = new ReplayReplicaState();
        var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = "fixture-room",
            Mode = (byte)GameMode.Battle }.Write(packet.AsSpan(1));
        state.Accept(packet, 0);
        Require(state.TimedShots.Begin(23, 7, 41, TimedGrantProofMode.Shadow), "Replica Begin failed");
        foreach (var fact in facts) Require(state.TimedShots.Remember(fact, 1000) == TimedShotRetention.Stored, "Replica Fill failed");
        state.Advance(1000);
        return state;
    }
    static object Percentiles(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        double P(double p)
        {
            double rank = (sorted.Length - 1) * p;
            int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
        }
        return new { Median = P(.5), P95 = P(.95), P99 = P(.99), P999 = P(.999), Min = sorted[0], Max = sorted[^1],
            Method = "linear interpolation across 30 raw batch totals; not operation/frame tails" };
    }
    static CaseResult Measure(string name, int operations, string setup, Func<Action> prepare,
        Action? verify = null)
    {
        for (int w = 0; w < WarmupBatches; w++) { var run = prepare(); for (int i = 0; i < operations; i++) run(); verify?.Invoke(); }
        var raw = new List<Sample>(Samples);
        using var process = Process.GetCurrentProcess();
        for (int sample = 0; sample < Samples; sample++)
        {
            Action run = prepare();
            process.Refresh(); TimeSpan cpuStart = process.TotalProcessorTime;
            long allocStart = GC.GetAllocatedBytesForCurrentThread(); long wallStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < operations; i++) run();
            long wallEnd = Stopwatch.GetTimestamp(); long allocEnd = GC.GetAllocatedBytesForCurrentThread();
            process.Refresh(); TimeSpan cpuEnd = process.TotalProcessorTime;
            var row = new Sample(sample, operations, (wallEnd - wallStart) * 1000.0 / Stopwatch.Frequency,
                (cpuEnd - cpuStart).TotalMilliseconds, allocEnd - allocStart);
            raw.Add(row); verify?.Invoke();
            Console.WriteLine($"RAW {name} {JsonSerializer.Serialize(row)}");
        }
        return new(name, setup, operations, raw, Percentiles(raw.Select(s => s.WallMs)),
            Percentiles(raw.Select(s => s.ProcessCpuMs)), raw.Average(s => (double)s.CallerAllocatedBytes / operations));
    }
    static int Main(string[] args)
    {
        Require(args.Length == 4, "usage: nettest.dll result.json expected-client-sha expected-client-mvid full-fixture-nettest.dll");
        var clientBefore = Identify(typeof(ReplayTimedShotState).Assembly.Location);
        var entryBefore = Identify(Assembly.GetExecutingAssembly().Location);
        var fixture = Identify(args[3]);
        Console.WriteLine("STARTUP_IDENTITIES " + JsonSerializer.Serialize(new { QualifiedActualClient = clientBefore, ThinBenchmarkEntry = entryBefore, FullNativeFixtureIdentityOnly = fixture }));
        Require(clientBefore.Sha256 == args[1] && clientBefore.Mvid.Equals(args[2], StringComparison.OrdinalIgnoreCase), "Wrong qualified client SHA/MVID");
        Require(entryBefore.Sha256 != fixture.Sha256 && entryBefore.Mvid != fixture.Mvid, "Thin benchmark confused with full fixture");
        var entropy = new Random(Seed);
        var facts = Enumerable.Range(0, 2048).Select(i => Fact((uint)(i % 256 + 1), i / 256, 1000, entropy)).ToArray();
        var fact = facts[^1]; var packet = fact.Packet();
        Require(packet.Length == 216 && ReplayTimedShotFact.Size == 216, "Fixed216 contract changed");
        Require(ReplayTimedShotFact.TryRead(packet, out var roundtrip) && roundtrip == fact, "Fact216 roundtrip failed");
        var full = Fill(facts); var over = Fact(257, 7, 1000, entropy);
        Require(full.Remember(fact, 1000) == TimedShotRetention.Duplicate, "Duplicate fixture failed");
        Require(full.Remember(over, 1000) == TimedShotRetention.Capacity && full.Count == 2048, "Full capacity fixture failed");
        Require(full.TryGet(fact.Key, fact.Fire.SourceFrame, 41, 1000, out var found) && found == fact, "Last-cell read failed");
        var replica1 = Replica(new[] { facts[0] }); var replica2048 = Replica(facts);
        var baseline = new ReplayReplicaState().CaptureCheckpoint();
        var cp1 = replica1.CaptureCheckpoint(); var cp2048 = replica2048.CaptureCheckpoint();
        Require(BinaryPrimitives.ReadUInt16LittleEndian(baseline.Bytes[4..]) == 12 && (baseline.Bytes[7] & 8) == 0, "Normal header is not12");
        Require(BinaryPrimitives.ReadUInt16LittleEndian(cp1.Bytes[4..]) == 13 && (cp1.Bytes[7] & 8) != 0, "Opt-in header is not13");
        Require(cp2048.Bytes.Length <= ReplayReplicaCheckpoint.MaximumBytes, "Full checkpoint exceeds512KiB");
        var restore = new ReplayReplicaState(); restore.RestoreCheckpoint(cp2048);
        Require(restore.TimedShots.Count == 2048 && restore.CaptureCheckpoint().Bytes.SequenceEqual(cp2048.Bytes), "Full checkpoint roundtrip failed");
        var cases = new List<CaseResult>();
        foreach (var codecFact in new[] { facts[^2], facts[^1] })
        {
            var codecPacket = codecFact.Packet();
            string fundingName = codecFact.Funding.HasValue ? "funded" : "unfunded";
            Require(codecPacket.Length == 216 && ReplayTimedShotFact.TryRead(codecPacket, out var decoded) && decoded == codecFact, "Codec fixture failed");
            cases.Add(Measure($"fact216_encode_{fundingName}", 2048, "constructed canonical fact reused; Packet allocates each call", () => () => Sink += codecFact.Packet().Length));
            cases.Add(Measure($"fact216_decode_{fundingName}", 1024, "serialized216-byte input reused; actual TryRead validates and re-encodes", () => () => { Require(ReplayTimedShotFact.TryRead(codecPacket, out var value), "Decode failed"); Sink += value.Components; }));
        }
        cases.Add(Measure("full2048_duplicate_lastcell", 4096, "full state prebuilt and reused", () => () => Sink += (int)full.Remember(fact, 1000)));
        cases.Add(Measure("full2048_tryget_lastcell", 4096, "full state prebuilt and reused", () => () => { Require(full.TryGet(fact.Key, fact.Fire.SourceFrame, 41, 1000, out var value), "Read failed"); Sink += value.Components; }));
        cases.Add(Measure("full2048_tryget_missing", 4096, "full state prebuilt and reused; missing shot257", () => () => { Require(!full.TryGet(over.Key, over.Fire.SourceFrame, 41, 1000, out _), "Missing unexpectedly found"); Sink++; }));
        cases.Add(Measure("full2048_capacity_refusal", 4096, "full state prebuilt and reused; no eviction", () => () => { Require(full.Remember(over, 1000) == TimedShotRetention.Capacity, "Capacity failed"); Sink++; }));
        ReplayTimedShotState? advancing = null;
        cases.Add(Measure("full2048_advanceauthority_fixedscan", 512,
            "each batch rebuilds2048 cells outside timing; frames1001..1512 keep every fact within1024 retention; each call scans8x256",
            () => { advancing = Fill(facts); uint frame = 1000; return () => { advancing.AdvanceAuthority(++frame); Sink++; }; },
            () => Require(advancing!.Count == 2048 && advancing.AuthorityFrame == 1512, "Dense scan expired or skipped frames")));
        cases.Add(Measure("begin_fresh_setup", 8, "new state and lazy8x256 cell allocation included in each call", () => () => { var fresh = new ReplayTimedShotState(); Require(fresh.Begin(23, 7, 41, TimedGrantProofMode.Shadow), "Fresh Begin failed"); Sink++; }));
        cases.Add(Measure("begin_cached_scope", 4096, "already enabled identical scope reused; Begin early return measured", () => () => { Require(full.Begin(23, 7, 41, TimedGrantProofMode.Shadow), "Cached Begin failed"); Sink++; }));
        foreach (var (count, state) in new[] { (1, replica1), (2048, replica2048) })
        {
            cases.Add(Measure($"state_write_{count}", count == 1 ? 256 : 4,
                "state built outside measurement; fresh stream/writer and actual Write included each call",
                () => () => { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); state.TimedShots.Write(writer); writer.Flush(); Sink += stream.Length; }));
            cases.Add(Measure($"capturecheckpoint_{count}", count == 1 ? 256 : 4,
                "replica and facts built outside measurement; actual CaptureCheckpoint called each operation; no cached checkpoint",
                () => () => Sink += state.CaptureCheckpoint().Bytes.Length));
        }
        var clientAfter = Identify(typeof(ReplayTimedShotState).Assembly.Location);
        var entryAfter = Identify(Assembly.GetExecutingAssembly().Location);
        Require(clientAfter == clientBefore && entryAfter == entryBefore, "Runtime binary changed during benchmark");
        var result = new { Scope = "Focused archival value operations only. Constructed fixtures establish neither native admission nor graphics/gameplay fidelity.",
            Seed, Samples, WarmupBatches, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Stopwatch.Frequency, ProcessId = Environment.ProcessId, QualifiedActualClient = clientBefore, ThinBenchmarkEntry = entryBefore,
            FullNativeFixtureIdentityOnly = fixture, ActualClientUnchanged = clientAfter == clientBefore, EntryUnchanged = entryAfter == entryBefore,
            FactBytes = packet.Length, Slots = ReplayTimedShotState.Slots, PerSlot = ReplayTimedShotState.PerSlot,
            ConstructedFixture = "fixed seed; 1024 funded and1024 unfunded archival facts; raw granted ledger and nullable full funding included for even shot IDs; constructed AcceptedFire lane is a value field, not evidence of native admission",
            Checkpoints = new { NormalVersion = 12, NormalBytes = baseline.Bytes.Length, OptInVersion = 13,
                OneFactBytes = cp1.Bytes.Length, Full2048FactBytes = cp2048.Bytes.Length, MaximumBytes = ReplayReplicaCheckpoint.MaximumBytes,
                StateAppendixHeaderBytes = 24, FactBytes = 216, OneFactAppendixBytes = 24 + 216, FullFactAppendixBytes = 24 + 2048 * 216 },
            Measurement = "30 serial raw batches after8 warmup batches per case. Wall/CPU percentile values are batch totals, not per-operation or per-frame tails. Process CPU includes all process threads. Caller allocated bytes uses GC.GetAllocatedBytesForCurrentThread and excludes prepared fixture setup except begin_fresh_setup. No sample filtering, forced collection, or outlier removal. Quantile interpolation over30 samples is descriptive and does not establish high-percentile tails.",
            Cases = cases, Sink };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BENCHMARK_COMPLETE " + Path.GetFileName(args[0])); return 0;
    }
}
