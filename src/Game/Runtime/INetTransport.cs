using System;
using System.Collections.Generic;
using System.Net;
namespace MphRead.Mods.Network
{
    /// <summary>Host-owned transport operations; Game never opens a socket.</summary>
    public interface INetTransport : INetDatagramSink, IDisposable
    {
        int LocalPort { get; }
        long PacketsDropped { get; }
        int QueuedPackets { get; }
        int HeldIncomingPackets { get; }
        int HeldOutgoingPackets { get; }
        NetTrafficMetrics Metrics { get; }
        void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default);
        void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries);
        void AnswerPingsImmediately();
        IEnumerable<ReceivedPacket> Drain();
        void EnqueueForPlayback(byte[] data, int length);
        void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0);
        void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks);
    }
}
