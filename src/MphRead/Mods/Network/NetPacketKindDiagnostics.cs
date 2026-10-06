using System;
using System.Threading;

namespace MphRead.Mods.Network;

public readonly record struct NetPacketKindSnapshot(PacketType Type, long PacketsReceived,
    long BytesReceived, long PacketsSent, long BytesSent, int PeakBytesReceived, int PeakBytesSent);

/// <summary>Optional counters for actual UDP datagrams, including retries and
/// ACK-only traffic (type zero). Bytes include the transport header. Simulated
/// loss before a socket send and playback packets do not count as wire sends.</summary>
public sealed class NetPacketKindDiagnostics
{
    private readonly long[] _receivedPackets = new long[256], _receivedBytes = new long[256];
    private readonly long[] _sentPackets = new long[256], _sentBytes = new long[256];
    private readonly int[] _receivedPeak = new int[256], _sentPeak = new int[256];

    public NetPacketKindSnapshot Capture(PacketType type)
    {
        int index = (byte)type;
        return new(type, Interlocked.Read(ref _receivedPackets[index]), Interlocked.Read(ref _receivedBytes[index]),
            Interlocked.Read(ref _sentPackets[index]), Interlocked.Read(ref _sentBytes[index]),
            Volatile.Read(ref _receivedPeak[index]), Volatile.Read(ref _sentPeak[index]));
    }

    private static int Kind(ReadOnlySpan<byte> datagram)
        => datagram.IsEmpty ? -1 : datagram[0] == NetHeader.Marker
            ? datagram.Length >= 2 ? datagram[1] : -1 : datagram[0];

    internal void Received(ReadOnlySpan<byte> datagram)
    {
        int index = Kind(datagram);
        if (index < 0) return;
        Interlocked.Increment(ref _receivedPackets[index]);
        Interlocked.Add(ref _receivedBytes[index], datagram.Length);
        Maximum(ref _receivedPeak[index], datagram.Length);
    }

    internal void Sent(ReadOnlySpan<byte> datagram)
    {
        int index = Kind(datagram);
        if (index < 0) return;
        Interlocked.Increment(ref _sentPackets[index]);
        Interlocked.Add(ref _sentBytes[index], datagram.Length);
        Maximum(ref _sentPeak[index], datagram.Length);
    }

    private static void Maximum(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target))
            && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}
