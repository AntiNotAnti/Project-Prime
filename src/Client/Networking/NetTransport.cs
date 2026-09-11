using System;
using System.Collections.Generic;
using System.Net;
namespace MphRead.Mods.Network
{
    /// <summary>Client transport façade; platform implementation is private to each executable.</summary>
    public sealed class NetTransport : INetTransport
    {
        private readonly UdpTransport _transport;
        public NetTransport(int port) => _transport = new UdpTransport(port);
        public const int MaxQueuedPackets = UdpTransport.MaxQueuedPackets;
        public const int MaxPacketsPerDrain = UdpTransport.MaxPacketsPerDrain;
        public const int MaxKeepAlives = UdpTransport.MaxKeepAlives;
        public static ref long TotalPacketsDropped => ref UdpTransport.TotalPacketsDropped;
        public static ref long TotalPacketsSent => ref UdpTransport.TotalPacketsSent;
        public int LocalPort => _transport.LocalPort;
        public long PacketsDropped => _transport.PacketsDropped;
        public int QueuedPackets => _transport.QueuedPackets;
        public int HeldIncomingPackets => _transport.HeldIncomingPackets;
        public int HeldOutgoingPackets => _transport.HeldOutgoingPackets;
        public NetTrafficMetrics Metrics => _transport.Metrics;
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) => _transport.SetKeepAlive(target, datagram);
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) => _transport.SetKeepAlives(entries);
        public void SetKeepAliveDescriptors(ReadOnlySpan<NetKeepAliveDescriptor> entries)
            => _transport.SetKeepAliveDescriptors(entries);
        public void AnswerPingsImmediately() => _transport.AnswerPingsImmediately();
        public IEnumerable<ReceivedPacket> Drain() => _transport.Drain();
        public void EnqueueForPlayback(byte[] data, int length) => _transport.EnqueueForPlayback(data, length);
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0)
            => _transport.Send(target, type, payload, extraHoldTicks);
        void INetDatagramSink.SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram) => _transport.SendDatagram(endpoint, datagram);
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks = 0)
            => _transport.SendDatagram(target, datagram, extraHoldTicks);
        public void Dispose() => _transport.Dispose();
    }
}
