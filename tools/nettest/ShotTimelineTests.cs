using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ShotTimelineTests
{
    // Compile the measurement loop directly as a hot method. The original
    // Tiered loop measured a fixed 128 bytes; the tiering-disabled control
    // measured zero. Retain both results and isolate steady-state observation.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureFull(ShotTimelineBuffer buffer, in ShotTimelineRow row)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) buffer.Add(row);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureDisabled(ShotKey key, in FireEvent fire)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) NetCampaignEvidence.TimelineFire("source-emitted", key, fire);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static int Run(bool enabled)
    {
        try
        {
            void Check(bool value, string message) => NetArchitectureTests.Check(value, message);
            var key = new ShotKey(11, 2, 0, 3, 4, uint.MaxValue);
            var row = default(ShotTimelineRow) with { Stage = "source-emitted", Key = key, Timestamp = 100 };
            var buffer = new ShotTimelineBuffer(2);
            buffer.Add(row);
            buffer.Add(row with { Key = key with { LifeId = 5, ShotId = 1 }, Timestamp = 101 });
            buffer.Add(row with { Key = default });
            var rows = buffer.Capture();
            Check(rows.Length == 2 && rows[0].Key != rows[1].Key && rows[0].Key.ShotId == uint.MaxValue,
                "timeline preserves complete lifecycle and wrapping shot identity");
            Check(buffer.UnkeyedRows == 1, "missing identity is incomplete evidence, never a paid key");
            rows[0] = default;
            Check(buffer.Capture()[0] == row, "report capture is detached from retained samples");
            buffer.Add(row); // Warm the overflow path before allocation measurement.
            long observationBytes = MeasureFull(buffer, row);
            Check(observationBytes == 0, $"full timeline observation allocation: {observationBytes} bytes");
            Check(buffer.Overflow == 10001 && buffer.Capture().Length == 2,
                $"full timeline remains bounded; overflow={buffer.Overflow}");
            bool invalid = false;
            try { _ = new ShotTimelineBuffer(0); } catch (ArgumentOutOfRangeException) { invalid = true; }
            Check(invalid, "invalid evidence capacity fails explicitly");

            Check(NetCampaignEvidence.TimelineEnabled == enabled, "route uses actual requested process evidence mode");
            var fire = new FireEvent(key.ShotId, 900, 700, 247, FireEventKind.PressFire,
                (byte)BeamType.Imperialist, 5, FireEvent.ScopedStateBit) with
            { PoseFlags = FireEvent.FlagPose, Origin = new(1,2,3),
                Aim = OpenTK.Mathematics.Vector3.UnitZ, Direction = OpenTK.Mathematics.Vector3.UnitZ };
            NetCampaignEvidence.TimelineFire("source-emitted", key, fire);
            var report = JsonSerializer.SerializeToElement(NetCampaignEvidence.CaptureShotTimeline());
            var observed = report.GetProperty("Rows");
            Check(report.GetProperty("Schema").GetInt32() == 2
                && report.GetProperty("Clock").GetProperty("Frequency").GetInt64() > 0,
                "clock domain and measured frequency are explicit");
            if (enabled)
            {
                Check(observed.GetArrayLength() == 1 && observed[0].GetProperty("Timestamp").GetInt64() > 0,
                    "enabled evidence records real process monotonic time");
                var sample = observed[0];
                Check(sample.GetProperty("SourceFrame").GetUInt32() == 900
                    && sample.GetProperty("AckFrame").GetUInt32() == 700
                    && sample.GetProperty("AckSubFrame").GetByte() == 247
                    && sample.GetProperty("Phase").GetUInt32() == 0,
                    "authored fractional ACK is retained; scoped modifier bits are not a continuous phase");
                Check(sample.GetProperty("Pose").GetProperty("Origin").GetProperty("X").GetSingle() == 1
                    && sample.GetProperty("Pose").GetProperty("Charge").GetByte() == 5
                    && report.GetProperty("Provenance").GetProperty("Pose").GetString()!.Contains("never hit authority"),
                    "schema2 retains authored pose and discloses presentation-only provenance");
                var outcome = new CombatAckEntry { ShotId = key.ShotId, VictimSlot = 1,
                    VictimGeneration = 8, VictimLife = 9, DamageSequence = uint.MaxValue };
                NetCampaignEvidence.TimelinePayment("payment-committed",
                    new ShotPaymentKey(key, 1, 8, 9, 7, 1), (byte)BeamType.Imperialist,
                    outcome: outcome, claimId: 37);
                NetCampaignEvidence.TimelineContinuous("payment-committed",
                    new ContinuousPaymentKey(key with { ShotId = 55 }, new NetTargetIdentity(0x82, 8, 9), 0),
                    outcome, claimId: 13);
                report = JsonSerializer.SerializeToElement(NetCampaignEvidence.CaptureShotTimeline());
                Check(report.GetProperty("Rows")[1].GetProperty("DamageEventId").GetUInt32() == uint.MaxValue,
                    "full damage sequence is preserved without ushort truncation");
                Check(report.GetProperty("Rows")[1].GetProperty("ClaimId").GetUInt16() == 37
                    && report.GetProperty("Rows")[2].GetProperty("ClaimId").GetUInt16() == 13
                    && report.GetProperty("Rows")[2].GetProperty("Phase").GetUInt32() == 55
                    && report.GetProperty("Rows")[2].GetProperty("Key").GetProperty("ShotId").GetUInt32() == key.ShotId
                    && outcome.ClaimId == 0,
                    "payment claims and continuous phase retain exact identity without changing gameplay outcomes");
            }
            else
            {
                long disabledBytes = MeasureDisabled(key, fire);
                Check(observed.GetArrayLength() == 0 && disabledBytes == 0,
                    "disabled evidence captures no rows and allocates nothing per observation");
            }
            Console.WriteLine($"PASS shot timeline storage/clock/identity ({(enabled ? "enabled" : "disabled")})");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
