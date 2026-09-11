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

        /// <summary>
        /// Installs an owner-provided signal for work that can wake a bounded
        /// network pump. The default is deliberately inert for legacy and
        /// test transports that are still polled by their caller.
        /// </summary>
        void SetNetworkWake(Action? signal) { }

        /// <summary>Whether this transport currently has work the owner can service.</summary>
        bool HasReadyNetworkWork => QueuedPackets > 0;

        /// <summary>
        /// Absolute <see cref="System.Diagnostics.Stopwatch"/> timestamp for
        /// the next transport-owned deadline, or <see cref="long.MaxValue"/>.
        /// </summary>
        long NextNetworkDeadlineTimestamp => long.MaxValue;

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

        /// <summary>Compatibility path for non-scheduling transports.</summary>
        void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram,
            long extraHoldTicks, NetDeliveryClass deliveryClass)
            => SendDatagram(target, datagram, extraHoldTicks);
    }
}
