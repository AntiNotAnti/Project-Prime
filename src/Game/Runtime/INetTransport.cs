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
        /// <summary>
        /// Publishes transport-owned authenticated keepalives. Implementations
        /// copy descriptor state and advance counters independently of packet
        /// sequence/ACK windows.
        /// </summary>
        void SetKeepAliveDescriptors(ReadOnlySpan<NetKeepAliveDescriptor> entries) { }
        void AnswerPingsImmediately();
        IEnumerable<ReceivedPacket> Drain();
        int Drain(Span<ReceivedPacket> destination)
        {
            int count = 0;
            foreach (ReceivedPacket packet in Drain())
            {
                if (count == destination.Length) break;
                destination[count++] = packet;
            }
            return count;
        }
        void EnqueueForPlayback(byte[] data, int length);
        void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0);
        void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks);
    }
}
