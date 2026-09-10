using System;
using System.Net;
using System.Diagnostics;
namespace MphRead.Mods.Network
{
    public readonly record struct NetKeepAlive(IPEndPoint Endpoint, ReadOnlyMemory<byte> Datagram);

    public readonly struct ReceivedPacket
    {
        public readonly IPEndPoint Sender;
        public readonly byte[] Data;
        public readonly int Length;
        public readonly long ReceivedAt;

        public ReceivedPacket(IPEndPoint sender, byte[] data, int length)
            : this(sender, data, length, Stopwatch.GetTimestamp()) { }

        // Test and in-process transport fixtures can provide the captured
        // arrival timestamp without sleeping or changing the public socket
        // construction path.
        internal ReceivedPacket(IPEndPoint sender, byte[] data, int length, long receivedAt)
        {
            Sender = sender;
            Data = data;
            Length = length;
            ReceivedAt = receivedAt;
        }

        public PacketType Type => Length > 0 ? (PacketType)Data[0] : default;
        public ReadOnlySpan<byte> Payload => Length > 0 ? Data.AsSpan(1, Length - 1) : default;
    }

}
