using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

return Benchmark.Run(args);

internal static class Benchmark
{
    private const int Repeats = 17, WarmupBatches = 8;
    private const uint Frame = 101;
    private static ulong _sink;
    private sealed record Shape(int FactCells, int AckCells, int Pending, int Consumed, int Bytes);
    private sealed record Sample(int Index, int Iterations, double TotalMs, double NsPerIteration,
        long AllocatedBytes, double BytesPerIteration, ulong Checksum);
    private sealed record Measurement(string Name, string Unit, int OperationsPerIteration,
        int WarmupBatches, int IterationsPerBatch, Sample[] Samples,
        double MedianNsPerIteration, double P95NsPerIteration, double P99NsPerIteration,
        double MedianBytesPerIteration);

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static ShotKey Life(int slot) => new(11, 7, slot, 1, 1, 0);
    private static TimedGrantFact Fact(uint id)
    {
        int cell = (int)((id - 1) % 24), slot = cell / 3;
        return new(Life(slot), 3, id, (TimedPowerUp)(cell % 3), id,
            (int)id, 1, 100, 1900, TimedGrantEnd.Active, Frame);
    }
    private static PickupClaim Claim(ShotKey life, TimedPowerUp type, PowerUpSpawnKey spawn, uint id)
        => new(new(life.AuthorityEpoch, life.MatchId, life.ShooterSlot, life.Generation, life.LifeId, id),
            spawn, type, 100, Vector3.Zero);
    private static TimedGrantPresentation Populated()
    {
        var state = new TimedGrantPresentation();
        for (uint id = 1; id <= 24; id++) Check(state.Accept(Fact(id), Frame), "24 native slot/type cells admit");
        return state;
    }
    private static TimedGrantPresentation Worst()
    {
        var state = new TimedGrantPresentation();
        for (uint id = 1; id <= NetTimedGrants.GrantCapacity; id++)
            Check(state.Accept(Fact(id), Frame), "512 distinct consumed incarnations admit");
        foreach (var fact in state.Facts.ToArray())
        {
            var claim = Claim(fact.Life, fact.Type, fact.Spawn, 2000 + (uint)(fact.Life.ShooterSlot * 3 + (int)fact.Type));
            Check(state.Settle(new(claim, PickupResult.Grant, Frame, 100, fact.Sequence, 100, 1900), Frame),
                "24 ACK cells reuse existing consumed incarnation");
        }
        for (uint id = 0; id < TimedGrantPresentation.PredictionCapacity; id++)
        {
            int slot = (int)(id % 8); var spawn = new PowerUpSpawnKey(11, 7, 3, 10000 + (int)id, 1);
            Check(state.Predict(Claim(Life(slot), (TimedPowerUp)(id % 3), spawn, 3000 + id), Frame),
                "16 independent pending incarnations admit");
        }
        return state;
    }
    private static byte[] Save(TimedGrantPresentation state)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        state.Write(writer); writer.Flush(); return stream.ToArray();
    }
    private static TimedGrantPresentation Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream);
        var state = TimedGrantPresentation.Read(reader);
        Check(stream.Position == stream.Length, "checkpoint consumes every byte"); return state;
    }
    private static Shape Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream);
        stream.Position = 23; int facts = 0, acks = 0;
        for (int cell = 0; cell < 24; cell++)
        {
            if (reader.ReadBoolean()) { facts++; stream.Position += TimedGrantFact.Size; }
            if (reader.ReadBoolean()) { acks++; stream.Position += PickupVerdict.Size; }
        }
        int pending = reader.ReadInt32(); stream.Position += pending * (PickupClaim.Size + 4);
        int consumed = reader.ReadInt32(); stream.Position += consumed * 12 + 4;
        Check(stream.Position == stream.Length, "fixture byte accounting matches canonical writer");
        return new(facts, acks, pending, consumed, bytes.Length);
    }
    private static ulong Views(TimedGrantPresentation state, int frames)
    {
        ulong sum = 0;
        for (int frame = 0; frame < frames; frame++)
            for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
            {
                var view = state.View(Life(slot), (TimedPowerUp)type, Frame);
                sum += view.Remaining + (ulong)view.ConfirmedRemaining + view.GrantSequence + view.PredictionSequence;
            }
        return sum;
    }
    private static double Quantile(double[] sorted, double percentile)
        => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
    private static Measurement Measure(string name, string unit, int operations, int iterations, Func<int, ulong> work)
    {
        for (int i = 0; i < WarmupBatches; i++) _sink ^= work(iterations);
        var samples = new Sample[Repeats];
        for (int i = 0; i < Repeats; i++)
        {
            // Delegate, arrays and fixture construction precede the window;
            // no conversion, formatting, source hashing or JSON enters it.
            long bytes = GC.GetAllocatedBytesForCurrentThread(); long start = Stopwatch.GetTimestamp();
            ulong checksum = work(iterations);
            long end = Stopwatch.GetTimestamp(); long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            double elapsedMs = (end - start) * 1000.0 / Stopwatch.Frequency;
            samples[i] = new(i, iterations, elapsedMs, elapsedMs * 1_000_000 / iterations,
                allocated, allocated / (double)iterations, checksum);
            _sink ^= checksum;
        }
        double[] times = samples.Select(s => s.NsPerIteration).Order().ToArray();
        double[] allocations = samples.Select(s => s.BytesPerIteration).Order().ToArray();
        Check(samples.All(s => s.Checksum == samples[0].Checksum), "fixed micro-workload checksum is stable");
        return new(name, unit, operations, WarmupBatches, iterations, samples,
            times[Repeats / 2], Quantile(times, .95), Quantile(times, .99), allocations[Repeats / 2]);
    }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    private static object Identity(Assembly assembly) => new
    { assembly = assembly.GetName().Name, location = assembly.Location, mvid = assembly.ManifestModule.ModuleVersionId, sha256 = Hash(assembly.Location) };
    internal static int Run(string[] args)
    {
        string? Value(string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        if (args.Contains("--help"))
        { Console.WriteLine("--client-source SHA --expected-client-sha256 SHA256 --tool-source SHA --tool-root DIRECTORY --output JSON [--self-check]"); return 0; }
        string? output = Value("--output");
        var startedUtc = DateTimeOffset.UtcNow;
        try
        {
            string source = Value("--client-source") ?? throw new ArgumentException("--client-source required");
            string toolSource = Value("--tool-source") ?? throw new ArgumentException("--tool-source required");
            string root = Path.GetFullPath(Value("--tool-root") ?? throw new ArgumentException("--tool-root required"));
            Check(source.Length == 40 && source.All(Uri.IsHexDigit) && toolSource.Length == 40 && toolSource.All(Uri.IsHexDigit),
                "source identities must be full commit SHA values");
            Check(output != null, "--output required");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output!))!);
            string expectedClient = Value("--expected-client-sha256") ?? throw new ArgumentException("--expected-client-sha256 required");
            Check(expectedClient.Length == 64 && expectedClient.All(Uri.IsHexDigit)
                && Hash(typeof(TimedGrantPresentation).Assembly.Location).Equals(expectedClient, StringComparison.OrdinalIgnoreCase),
                "actual loaded client SHA must match the selected frozen build");
            int Constant(Type type, string name) => Convert.ToInt32(type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
            Check(Constant(typeof(NetTimedGrants), "GrantCapacity") == 512
                && Constant(typeof(TimedGrantPresentation), "PredictionCapacity") == 16
                && Constant(typeof(ReplayGrantBootstrap), "MaximumStateBytes") == 32768,
                "actual loaded client bounds match fixed workload ABI");
            var toolFiles = new[] { "Program.cs", "grant-presentation-benchmark.csproj", "README.md" }
                .ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
            var empty = new TimedGrantPresentation(); var populated = Populated(); var worst = Worst();
            byte[] populatedBefore = Save(populated), worstBytes = Save(worst);
            Shape emptyShape = Inspect(Save(empty)), populatedShape = Inspect(populatedBefore), worstShape = Inspect(worstBytes);
            Check(emptyShape is { FactCells: 0, AckCells: 0, Pending: 0, Consumed: 0 }, "empty fixture is empty");
            Check(populatedShape is { FactCells: 24, AckCells: 0, Pending: 0, Consumed: 24 }, "populated fixture is 24 native cells");
            Check(worstShape is { FactCells: 24, AckCells: 24, Pending: 16, Consumed: 512 }
                && worstBytes.Length <= ReplayGrantBootstrap.MaximumStateBytes, "worst fixture matches all bounds");
            Check(Save(Read(worstBytes)).AsSpan().SequenceEqual(worstBytes), "worst state canonical roundtrip byte equality");
            for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
                Check(populated.View(Life(slot), (TimedPowerUp)type, Frame) is { Remaining: 1799, ConfirmedRemaining: 1799 },
                    "each populated view uses authority contact+1 and native duration");
            TimedGrantFact[] duplicateFacts = populated.Facts.ToArray();
            ulong Duplicate(int frames)
            {
                ulong rejected = 0;
                for (int frame = 0; frame < frames; frame++) foreach (var fact in duplicateFacts)
                    if (!populated.Accept(fact, Frame)) rejected++;
                return rejected;
            }
            Check(Duplicate(1) == 24 && Save(populated).AsSpan().SequenceEqual(populatedBefore), "duplicate admissions preserve exact state");
            using var writeStream = new MemoryStream(ReplayGrantBootstrap.MaximumStateBytes);
            using var writer = new BinaryWriter(writeStream, System.Text.Encoding.UTF8, leaveOpen: true);
            ulong WriteWork(int iterations)
            {
                ulong sum = 0;
                for (int i = 0; i < iterations; i++)
                { writeStream.Position = 0; writeStream.SetLength(0); worst.Write(writer); writer.Flush(); sum += (ulong)writeStream.Length; }
                return sum;
            }
            ulong ReadWork(int iterations)
            {
                ulong sum = 0;
                for (int i = 0; i < iterations; i++) { var restored = Read(worstBytes); sum += (ulong)restored.PendingCount + restored.RoomIdentity; }
                return sum;
            }
            var results = args.Contains("--self-check") ? Array.Empty<Measurement>() : new[]
            {
                Measure("empty-24-views", "24 View calls at a fixed simulation frame", 24, 4096, n => Views(empty, n)),
                Measure("populated-24-views", "24 View calls at a fixed simulation frame", 24, 4096, n => Views(populated, n)),
                Measure("duplicate-24-accepts", "24 byte-identical fact Accept calls", 24, 4096, Duplicate),
                Measure("worst-write", "one canonical Write into reused stream/writer", 1, 128, WriteWork),
                Measure("worst-read", "one canonical Read including fresh stream/reader/state", 1, 32, ReadWork)
            };
            Check(Save(populated).AsSpan().SequenceEqual(populatedBefore) && Save(worst).AsSpan().SequenceEqual(worstBytes),
                "benchmarked immutable fixtures remain byte equal");
            var report = new
            {
                schema = 1, scope = "Managed micro-workloads only; no native frame/GPU/UDP/gameplay performance or acceptance claim",
                status = "PASS", measured = results.Length > 0, startedUtc, completedUtc = DateTimeOffset.UtcNow,
                clientSource = source, toolSource, sourceBinding = "Operator-supplied commit identities; actual loaded assembly hashes/MVIDs below require the corresponding frozen build receipt",
                client = Identity(typeof(TimedGrantPresentation).Assembly), tool = Identity(Assembly.GetExecutingAssembly()),
                mathematics = Identity(typeof(Vector3).Assembly), toolFiles, expectedClientSha256 = expectedClient, protocol = Constant(typeof(NetConfig), "ProtocolVersion"),
                workload = new { Frame, Repeats, WarmupBatches, emptyShape, populatedShape, worstShape,
                    worstPayloadSha256 = Convert.ToHexString(SHA256.HashData(worstBytes)).ToLowerInvariant(),
                    viewSourceClock = "Same frame101 each iteration; times do not simulate advancing frames",
                    quantiles = "Nearest rank across17 completed batch-average ns/iteration samples; p95/p99 are both maxima at this sample count, not per-call or native frame tails",
                    allocation = "Thread managed allocation during work only; Read includes decoding/stream/reader/state allocations; Write reuses preallocated stream/writer; no forced GC",
                    order = "Fixed workload order,8 complete warmup batches before17 measured batches per workload" },
                runtime = new { framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), stopwatchFrequency = Stopwatch.Frequency,
                    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") },
                results, checksumSink = _sink
            };
            File.WriteAllText(Path.GetFullPath(output!), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
            Console.WriteLine($"PASS: fixture24 cells, worst512 consumed/16 pending/24 ACK; micro-workloads={results.Length}"); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (output != null) File.WriteAllText(Path.GetFullPath(output), JsonSerializer.Serialize(new { schema = 1, status = "FAIL", error = error.ToString() }) + "\n");
            return 1;
        }
    }
}
