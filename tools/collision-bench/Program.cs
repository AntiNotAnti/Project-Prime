using System.Diagnostics;
using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Identity;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

int samples = args.Length == 0 ? 400 : int.Parse(args[0]);
if (samples is < 20 or > 100000) throw new ArgumentOutOfRangeException(nameof(samples));
string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? throw new ArgumentException("GAME_DATA_DIRECTORY is required.");
ServerContent.Open(data, "AMHE1");
BaselineCollisionDetection.Init();
foreach (int actors in new[] { 8, 16 })
{
    var matches = new List<MatchInstance>();
    try
    {
        for (int index = 0; index < actors / 8; index++) matches.Add(Create(index));
        // Real bot world warmup supplies reproducible room-relative movement/query locations.
        for (int tick = 0; tick < 300; tick++) foreach (var match in matches) match.Tick();
        var queries = matches.SelectMany(m => m.Simulation.Scene.Players.Select(p => (m.Simulation.Scene, Player: p))).ToArray();
        CollisionResult[] results = new CollisionResult[16];
        foreach (var q in queries) { Run(false, q.Scene, q.Player, results); Run(true, q.Scene, q.Player, results); }
        ulong? expected = null;
        foreach (bool baseline in new[] { true, false })
        {
            long[] timings = new long[samples * queries.Length];
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            ulong checksum = 0;
            int offset = 0;
            for (int sample = 0; sample < samples; sample++) foreach (var query in queries)
            {
                long started = Stopwatch.GetTimestamp();
                checksum = unchecked(checksum * 1099511628211ul + Run(baseline, query.Scene, query.Player, results));
                timings[offset++] = Stopwatch.GetTimestamp() - started;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (expected.HasValue && checksum != expected.Value) throw new InvalidOperationException("Historical/current collision result checksums differ.");
            expected = checksum;
            Array.Sort(timings);
            double Micros(double percentile) => timings[Math.Min(timings.Length - 1, (int)Math.Ceiling(timings.Length * percentile) - 1)] * 1_000_000.0 / Stopwatch.Frequency;
            Console.WriteLine(JsonSerializer.Serialize(new { variant = baseline ? "historical-shared-scratch-serial" : "current-query-owned",
                actorCount = actors, scenes = matches.Count, map = "MP1 SANCTORUS", warmupTicks = 300,
                samplesPerActor = samples, queryMix = "segment+swept-sphere+radius", queries = timings.Length * 3,
                allocatedBytes = allocated, bytesPerQuery = allocated / (double)(timings.Length * 3),
                p50BatchUs = Micros(.50), p95BatchUs = Micros(.95), p99BatchUs = Micros(.99), maxBatchUs = Micros(1), checksum }));
        }
    }
    finally { foreach (var match in matches) match.Dispose(); }
}

static ulong Run(bool baseline, Scene scene, PlayerEntity player, CollisionResult[] results)
{
    Vector3 origin = player.Position + Vector3.UnitY * .5f;
    Vector3 end = origin + new Vector3(3, -.75f, 4);
    CollisionResult ray = default;
    bool hit = baseline ? BaselineCollisionDetection.CheckBetweenPoints(origin, end, TestFlags.Players, scene, ref ray)
        : CollisionDetection.CheckBetweenPoints(origin, end, TestFlags.Players, scene, ref ray);
    ulong hash = hit ? unchecked((uint)BitConverter.SingleToInt32Bits(ray.Distance)) : 0;
    int count = baseline ? BaselineCollisionDetection.CheckSphereBetweenPoints(origin, end, .35f, results.Length, true, TestFlags.Players, scene, results)
        : CollisionDetection.CheckSphereBetweenPoints(origin, end, .35f, results.Length, true, TestFlags.Players, scene, results);
    hash = unchecked(hash * 31 + (uint)count);
    for (int i = 0; i < count; i++) hash = unchecked(hash * 31 + (uint)BitConverter.SingleToInt32Bits(results[i].Distance));
    count = baseline ? BaselineCollisionDetection.CheckInRadius(origin, 1.5f, results.Length, false, TestFlags.Players, scene, results)
        : CollisionDetection.CheckInRadius(origin, 1.5f, results.Length, false, TestFlags.Players, scene, results);
    return unchecked(hash * 31 + (uint)count);
}

static MatchInstance Create(int index)
{
    var roster = Enumerable.Range(0, 8).Select(i => new RosterSeat((byte)i, null, null, $"BOT {i}", (Hunter)(i % 7), (byte)(i % 2), SeatRole.Bot, false)).ToImmutableArray();
    var spec = new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(),
        new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"), new("MP1 SANCTORUS", "benchmark", "AMHE1", "benchmark", NetHeader.Version),
        MatchTrustClass.Community, null, null, roster, ProjectPrime.Server.Shared.BotFillPolicy.Disabled,
        ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 12345, 67890);
    var match = new MatchInstance(new(spec, (uint)index + 1), new SilentTransport()); match.Start(); return match;
}

sealed class SilentTransport : INetTransport
{
    public int LocalPort => 0; public long PacketsDropped => 0; public int QueuedPackets => 0;
    public int HeldIncomingPackets => 0; public int HeldOutgoingPackets => 0; public NetTrafficMetrics Metrics { get; } = new();
    public void Dispose() { }
    public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
    public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
    public void AnswerPingsImmediately() { }
    public IEnumerable<ReceivedPacket> Drain() => Array.Empty<ReceivedPacket>();
    public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
    public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
    public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks) { }
    public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
}
