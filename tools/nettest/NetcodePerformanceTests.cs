using System;
using System.Buffers.Binary;
using System.Reflection;
using MphRead.Mods.Network;
using MphRead.Mods.Network.Telemetry;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class NetcodePerformanceTests
{
    private static void Check(bool value, string message) => NetArchitectureTests.Check(value, message);
    public static int Run()
    {
        try
        {
            Sampling(); Ledgers(); RescueIndex(); FireWire(); Decode(); Aggregate(); AggregateLifecycle();
            Check(DynamicGeometryTests.Run() == 0, "geometry equivalence");
            Console.WriteLine("PASS: deterministic sampling, ledger reference comparison, direct decode equivalence and telemetry batches");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { NetShadowSampler.Mode = ShadowSamplingMode.Production; NetSession.Stop(); }
    }
    private static void Sampling()
    {
        foreach (var mode in Enum.GetValues<ShadowSamplingMode>())
        {
            NetShadowSampler.Mode = mode; int sampled = 0;
            for (uint i = 1; i <= 65536; i++)
            {
                var key = new ShotKey(19, 31, (int)(i % 8), (ushort)(i / 4096), (ushort)(i / 128), i);
                bool first = NetShadowSampler.ShouldSampleShadow(key);
                Check(first == NetShadowSampler.ShouldSampleShadow(key), "ShadowSamplerIsDeterministic");
                if (first) sampled++;
            }
            double target = mode switch { ShadowSamplingMode.Off => 0, ShadowSamplingMode.Full => 65536,
                ShadowSamplingMode.Study => 16384, _ => 4096 };
            Check(Math.Abs(sampled - target) <= target * .06, $"{mode}SamplesExpectedFraction: {sampled}");
            var decision = LagCompensationPolicy.Evaluate(28.125, new(100, 20, 90, 3), 4);
            Check(LagCompensationPolicy.Applied(decision) == 28.125, "SamplingDoesNotChangeRewind");
        }
    }
    private static object? Call(string name, params object[] args)
    {
        var method = typeof(NetHitClaims).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        if (name is "NoteLedger" or "TakeLedger" && args.Length + 1 == method.GetParameters().Length)
        {
            object[] expanded = new object[args.Length + 1]; args.CopyTo(expanded, 0); expanded[^1] = args[3];
            var result = method.Invoke(null, expanded);
            Array.Copy(expanded, args, args.Length); return result;
        }
        return method.Invoke(null, args);
    }
    private static void Ledgers()
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); var random = new Random(701);
        uint frame = 1;
        for (int i = 0; i < 4000; i++)
        {
            int shooter = random.Next(8), victim = random.Next(8);
            typeof(NetSession).GetProperty("NetFrame")!.SetValue(null, frame++);
            switch (random.Next(8))
            {
                case 0: Call("NoteLedger", shooter, victim, frame, frame, 12, false); break;
                case 1: Call("NoteRescued", shooter, victim, frame); break;
                case 2: NetHitClaims.ForgetSlot(shooter); break;
                case 3: Call("Park", shooter, new HitClaimPacket { ClaimId = (ushort)i, VictimSlot = (byte)victim }); break;
                case 4: Call("ClearLedger", shooter, victim); break;
                case 5: for (int n = 0; n < NetHitClaims.LedgerDepth; n++) Call("LedgerLive", shooter, victim, n); break;
                case 6: if (i % 17 == 0) NetHitClaims.ForgetPending(); break;
                case 7: if (i % 31 == 0) NetHitClaims.Reset(); break;
            }
            NetHitClaims.ValidateLedgerCounters();
        }
        NetHitClaims.Reset(); NetHitClaims.ValidateLedgerCounters();
        Check(NetHitClaims.ClaimsPendingCurrent + NetHitClaims.ResolvedLedgerCurrent + NetHitClaims.RescuedLedgerCurrent == 0, "LedgerCounterReset");
    }
    private static void FireWire()
    {
        var intent = new IntentPacket { Frame = 103, AckFrame = 107, FireEventCount = 2 };
        intent.FireEvents[0] = new(5, 100, 100, 128, FireEventKind.PressFire, 0, 0, 0);
        intent.FireEvents[1] = new(6, 101, 100, 240, FireEventKind.ReleaseFire, 2, 90, 0);
        byte[] wire = new byte[IntentPacket.FullSize]; intent.Write(wire);
        var read = IntentPacket.Read(wire);
        Check(read.HasFireEvents && NetFireEvents.Validate(read) && read.FireEvents[0] == intent.FireEvents[0]
            && read.FireEvents[1] == intent.FireEvents[1], "lost press/release fire history round trip independently of carrier ACK");
        Check(!IntentPacket.Read(wire.AsSpan(0, IntentPacket.LegacyFullSize)).HasFireEvents, "legacy offline intent is explicit");
        read.FireEvents[1] = read.FireEvents[1] with { ShotId = 5 };
        Check(!NetFireEvents.Validate(read), "duplicate shot IDs rejected");
        read = IntentPacket.Read(wire); read.FireEventCount = 17;
        Check(!NetFireEvents.Validate(read), "oversized fire ring rejected");
        var claim = new HitClaimPacket { ShotId = 0xFEDCBA98, LaunchFrame = 100 };
        byte[] claimBytes = new byte[HitClaimPacket.Size]; claim.Write(claimBytes);
        Check(HitClaimPacket.Read(claimBytes).ShotId == claim.ShotId, "claim ShotId round trip");
        var ack = new CombatAckEntry { ShotId = claim.ShotId };
        byte[] ackBytes = new byte[CombatAckEntry.Size]; ack.Write(ackBytes);
        Check(CombatAckEntry.Read(ackBytes).ShotId == claim.ShotId, "CombatAck ShotId round trip");
    }
    private static void RescueIndex()
    {
        var index = new NetRescueIndex();
        var reference = new System.Collections.Generic.Dictionary<uint, int>();
        var random = new Random(99);
        for (int n = 0; n < 20000; n++)
        {
            uint id = (uint)random.Next(1, 600); var key = new ShotKey(1, 2, 0, 3, 4, id);
            if (random.Next(2) == 0)
            {
                bool expected = reference.ContainsKey(id) || reference.Count < 512;
                Check(index.Insert(0, 1, key, 5, 6, 100) == expected, "bounded rescue insert reference");
                if (expected) { reference.TryGetValue(id, out int owed); reference[id] = owed + 1; }
            }
            else
            {
                bool expected = reference.TryGetValue(id, out int owed);
                Check(index.Consume(0, 1, key, 5, 6, 100) == expected, "rescue consume reference under collisions");
                if (expected) { if (owed == 1) reference.Remove(id); else reference[id] = owed - 1; }
            }
            index.Validate(); Check(index.Count == reference.Count, "rescue maintained count");
        }
        foreach (uint id in reference.Keys) Check(!index.Consume(0, 1, new(1, 2, 0, 3, 4, id), 5, 6, 821), "expired rescue never consumed");
        index.ForgetSlot(1, false); Check(index.Count == 0, "rescue lifecycle purge");
    }

    private static void Decode()
    {
        var random = new Random(111);
        byte[] canonical = new byte[4096], assembled = new byte[4096], a = new byte[PlayerState.Size], b = new byte[PlayerState.Size];
        var decoded = new PlayerState[8];
        for (int count = 2; count <= 8; count *= 2)
        {
            var receiver = new NetReplicationReceiver(); var lanes = new NetReplicationLanes();
            for (uint iteration = 1; iteration < 100; iteration++)
            {
                new SnapshotHeader { Frame = iteration, MatchId = 3, AuthorityEpoch = 4, PlayerCount = (byte)count }.Write(canonical);
                for (int i = 0; i < count; i++)
                {
                    var state = new PlayerState { SlotIndex = (byte)(count - 1 - i), SlotGeneration = 5, LifeId = 6,
                        Position = new(random.NextSingle(), random.NextSingle(), random.NextSingle()),
                        Health = (ushort)random.Next(100), Points = (short)random.Next(100), Kills = (ushort)random.Next(100),
                        Deaths = (ushort)random.Next(100), Team = (byte)(i % 2), Facing = Vector3.UnitZ };
                    state.Write(canonical.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
                }
                int tail = SnapshotHeader.Size + count * PlayerState.Size;
                canonical.AsSpan(tail, 64 + NetHealthSync.HeaderSize).Clear();
                BinaryPrimitives.WriteUInt16LittleEndian(canonical.AsSpan(tail + 64), 3);
                lanes.Prepare(canonical.AsSpan(0, tail + 64 + NetHealthSync.HeaderSize));
                Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 3, 4), "accept slow");
                Check(receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 3, 4, out _), "direct decode");
                Check(receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), assembled, 3, 4) > 0, "canonical decode");
                for (int i = 0; i < count; i++)
                {
                    decoded[i].Write(a); PlayerState.Read(assembled.AsSpan(SnapshotHeader.Size + i * PlayerState.Size)).Write(b);
                    Check(a.AsSpan().SequenceEqual(b), "direct fields equal canonical fields");
                }
            }
            var saved = decoded[0];
            lanes.Fast[SnapshotHeader.Size + SnapshotFast.PlayerSize] = lanes.Fast[SnapshotHeader.Size];
            Check(!receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 3, 4, out _)
                && decoded[0].Position == saved.Position, "duplicate slots fail before output mutation");
        }
    }

    private static void AggregateLifecycle()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prime-batch-" + Guid.NewGuid());
        try
        {
            ProductionTelemetry.Configure(new NetTelemetryConfig { Directory = root });
            foreach (bool shutdown in new[] { false, true })
            {
                ProductionTelemetry.Begin("test", "Battle", 2);
                var writer = (NetTelemetryWriter)typeof(ProductionTelemetry).GetField("_writer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                for (uint frame = 1; frame <= 7; frame++) ProductionTelemetry.RecordServerStep(frame, 2, 3, 0, 0, false);
                if (shutdown) ProductionTelemetry.Shutdown(); else ProductionTelemetry.End();
                Check(writer.WaitForExit(5000), "telemetry flush writer completes");
            }
            foreach (var path in System.IO.Directory.GetFiles(root, "*.summary.json", System.IO.SearchOption.AllDirectories))
            {
                using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                var steps = json.RootElement.GetProperty("serverStepMilliseconds");
                Check(steps.GetProperty("count").GetInt64() == 7 && steps.GetProperty("mean").GetDouble() == 2,
                    "partial telemetry flush at match end and shutdown preserves all steps");
            }
            Check(System.IO.Directory.GetFiles(root, "*.summary.json", System.IO.SearchOption.AllDirectories).Length == 2,
                "both lifecycle flushes write summaries");
        }
        finally
        {
            ProductionTelemetry.Shutdown();
            ProductionTelemetry.Configure(new NetTelemetryConfig { Enabled = false });
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    private static void Aggregate()
    {
        var batch = new ServerStepAccumulator(); var aggregate = new NetTelemetryAggregator();
        Span<NetTelemetryEvent> events = stackalloc NetTelemetryEvent[5];
        double sum = 0;
        for (uint i = 1; i <= 60; i++)
        {
            double ms = i / 10.0; sum += ms;
            Check(batch.Add(i, ms, i, 2, 3) == (i == 60), "TelemetryFlushAtInterval");
        }
        int count = batch.Flush(events); Check(count <= 5 && batch.Count == 0, "bounded telemetry events");
        for (int i = 0; i < count; i++) aggregate.Add(events[i]);
        var summary = aggregate.Capture(new(4, NetConfig.ProtocolVersion, "test", "test", "test", "test", "test", "test", 8), 1, default);
        Check(summary.ServerStepMilliseconds.Count == 60 && Math.Abs(summary.ServerStepMilliseconds.Mean - sum / 60) < 1e-10
            && summary.ServerStepMilliseconds.Maximum == 6 && summary.DroppedTicks == 2, "TelemetryAggregateMatchesRawCounts");
        batch.Add(61, 1, 5, 2, 3); Check(batch.Flush(events) == 2 && events[0].Result == 1, "partial flush");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (uint i = 0; i < 6000; i++) if (batch.Add(i, 1, 0, 0, 0)) batch.Flush(events);
        Check(allocated == GC.GetAllocatedBytesForCurrentThread(), "TelemetryStillAllocationFreeAfterWarmup");
    }
}
