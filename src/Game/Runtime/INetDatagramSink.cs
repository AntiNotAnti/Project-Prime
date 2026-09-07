using System;
using System.Net;

namespace MphRead.Mods.Network
{
    /// <summary>Protocol output port; its host owns transport and delivery.</summary>
    public interface INetDatagramSink
    {
        void SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram);
    }
}
