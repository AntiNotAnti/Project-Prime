using System;
using System.Diagnostics;
using System.Threading;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Network;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetTrafficKind
{
    Intent, SlotIntent, SnapshotFast, SlowWorld, HitClaim, CombatAck, ReliableControl,
    MapTransfer, Cosmetics, Replay, MatchResults, Discovery, Other, AckOnly, Invalid
}

public sealed record NetWireCategorySnapshot(NetTrafficKind Kind, long RxPackets, long RxBytes,
    long RxMaxDatagramBytes, long TxPackets, long TxBytes, long TxMaxDatagramBytes,
    long DequeuedPackets, double TotalQueueAgeMilliseconds, double MaxQueueAgeMilliseconds,
    long[] QueueAgeHistogram, double LifetimeRxBytesPerSecond, double LifetimeTxBytesPerSecond);

/// <summary>Counters are lifetime UDP datagram bytes, including all transport headers.
/// Capture is a best-effort concurrent view, not a transactional sampling boundary.</summary>
public sealed record NetWireSnapshot(long StartedAt, long CapturedAt, double ElapsedSeconds,
    double?[] QueueAgeBucketUpperMilliseconds, NetWireCategorySnapshot Totals,
    NetWireCategorySnapshot[] Categories);

/// <summary>Fixed storage per admitted connection or aggregate. Recording never allocates.
/// Queue timestamps are Stopwatch ticks. The last age bucket has no finite upper bound.</summary>
public sealed class NetWireTelemetry
{
    private const int KindCount = (int)NetTrafficKind.Invalid + 1, BucketCount = 12;
    private const int RxPackets = 0, RxBytes = 1, RxMax = 2, TxPackets = 3, TxBytes = 4,
        TxMax = 5, DequeueCount = 6, AgeTotal = 7, AgeMax = 8, CounterCount = 9;
    private readonly long[] _counters = new long[KindCount * CounterCount];
    private readonly long[] _histogram = new long[KindCount * BucketCount];
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public void Received(ReadOnlySpan<byte> datagram) => Record(Classify(datagram), datagram.Length, false);
    public void Sent(ReadOnlySpan<byte> datagram) => Record(Classify(datagram), datagram.Length, true);
    private void Record(NetTrafficKind kind, int bytes, bool sent)
    {
        int at = (int)kind * CounterCount + (sent ? TxPackets : RxPackets);
        Interlocked.Increment(ref _counters[at]);
        Interlocked.Add(ref _counters[at + 1], bytes);
        Maximum(ref _counters[at + 2], bytes);
    }
    public void Dequeued(PacketType type, long arrivedAt, long now)
    {
        int kind = (int)Classify(type), at = kind * CounterCount;
        long age = now > arrivedAt ? now - arrivedAt : 0;
        Interlocked.Increment(ref _counters[at + DequeueCount]);
        Interlocked.Add(ref _counters[at + AgeTotal], age);
        Maximum(ref _counters[at + AgeMax], age);
        double milliseconds = age * (1000.0 / Stopwatch.Frequency);
        int bucket = 0;
        while (bucket < BucketCount - 1 && milliseconds > (1 << bucket)) bucket++;
        Interlocked.Increment(ref _histogram[kind * BucketCount + bucket]);
    }
    private static void Maximum(ref long target, long value)
    {
        long old = Volatile.Read(ref target);
        while (value > old)
        {
            long seen = Interlocked.CompareExchange(ref target, value, old);
            if (seen == old) return;
            old = seen;
        }
    }
    public static NetTrafficKind Classify(ReadOnlySpan<byte> datagram)
    {
        if (datagram.IsEmpty || datagram.Length > NetConfig.MaxPacketSize) return NetTrafficKind.Invalid;
        if (datagram[0] != NetHeader.Marker)
            return datagram[0] == 0 ? NetTrafficKind.Invalid : Classify((PacketType)datagram[0]);
        if (!NetHeader.TryRead(datagram, out var header)) return NetTrafficKind.Invalid;
        bool ackOnly = (header.Flags & NetHeaderFlags.AckOnly) != 0;
        bool reliable = (header.Flags & NetHeaderFlags.Reliable) != 0;
        if (ackOnly) return header.Type == 0 && !reliable ? NetTrafficKind.AckOnly : NetTrafficKind.Invalid;
        if (header.Type == 0 || reliable && (datagram.Length < NetHeader.Size + 4
            || !NetReliableChannel.IsReliable(header.Type))) return NetTrafficKind.Invalid;
        return Classify(header.Type);
    }
    public static NetTrafficKind Classify(PacketType type) => type switch
    {
        PacketType.Intent => NetTrafficKind.Intent,
        PacketType.SlotIntent => NetTrafficKind.SlotIntent,
        PacketType.SnapshotFast => NetTrafficKind.SnapshotFast,
        PacketType.Snapshot or PacketType.PlayerSlowState or PacketType.WorldState => NetTrafficKind.SlowWorld,
        PacketType.HitClaim => NetTrafficKind.HitClaim,
        PacketType.CombatAck => NetTrafficKind.CombatAck,
        PacketType.MapOffer or PacketType.MapWant or PacketType.MapChunk or PacketType.MapDone
            or PacketType.MapAvailability => NetTrafficKind.MapTransfer,
        PacketType.CosmeticState => NetTrafficKind.Cosmetics,
        PacketType.ReplayWorld or PacketType.ReplayShotFact => NetTrafficKind.Replay,
        PacketType.PostMatchReport or PacketType.MatchSemanticEvent or PacketType.MatchAward => NetTrafficKind.MatchResults,
        PacketType.StatusQuery or PacketType.StatusReply or PacketType.MasterHeartbeat or PacketType.MasterQuery
            or PacketType.MasterList or PacketType.HostRequest or PacketType.HostReply => NetTrafficKind.Discovery,
        _ => NetReliableChannel.IsReliable(type) ? NetTrafficKind.ReliableControl : NetTrafficKind.Other
    };
    public NetWireSnapshot Capture()
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = Math.Max(0, (now - _startedAt) / (double)Stopwatch.Frequency);
        var categories = new NetWireCategorySnapshot[KindCount];
        var totals = new long[CounterCount];
        var histogram = new long[BucketCount];
        for (int kind = 0; kind < KindCount; kind++)
        {
            var counters = new long[CounterCount];
            var buckets = new long[BucketCount];
            for (int i = 0; i < CounterCount; i++)
            {
                counters[i] = Volatile.Read(ref _counters[kind * CounterCount + i]);
                if (i == RxMax || i == TxMax || i == AgeMax) totals[i] = Math.Max(totals[i], counters[i]);
                else totals[i] += counters[i];
            }
            for (int i = 0; i < BucketCount; i++)
            { buckets[i] = Volatile.Read(ref _histogram[kind * BucketCount + i]); histogram[i] += buckets[i]; }
            categories[kind] = Snapshot((NetTrafficKind)kind, counters, buckets, seconds);
        }
        var edges = new double?[BucketCount];
        for (int i = 0; i < BucketCount - 1; i++) edges[i] = 1 << i;
        return new(_startedAt, now, seconds, edges, Snapshot(NetTrafficKind.Other, totals, histogram, seconds), categories);
    }
    private static NetWireCategorySnapshot Snapshot(NetTrafficKind kind, long[] c, long[] buckets, double seconds)
        => new(kind, c[RxPackets], c[RxBytes], c[RxMax], c[TxPackets], c[TxBytes], c[TxMax],
            c[DequeueCount], c[AgeTotal] * (1000.0 / Stopwatch.Frequency), c[AgeMax] * (1000.0 / Stopwatch.Frequency),
            buckets, seconds > 0 ? c[RxBytes] / seconds : 0, seconds > 0 ? c[TxBytes] / seconds : 0);
}
