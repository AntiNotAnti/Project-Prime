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
        {
            Sender = sender;
            Data = data;
            Length = length;
            ReceivedAt = Stopwatch.GetTimestamp();
        }

        public PacketType Type => Length > 0 ? (PacketType)Data[0] : default;
        public ReadOnlySpan<byte> Payload => Length > 0 ? Data.AsSpan(1, Length - 1) : default;
    }

}
