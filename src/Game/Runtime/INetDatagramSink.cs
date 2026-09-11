using System;
using System.Net;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Semantic delivery intent carried only inside the host transport. It is
    /// never serialized into the protocol envelope.
    /// </summary>
    public enum NetDeliveryClass : byte
    {
        Auto,
        Critical,
        Reliable,
        State,
        World,
        BestEffort
    }

    /// <summary>Protocol output port; its host owns transport and delivery.</summary>
    public interface INetDatagramSink
    {
        void SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram);

        /// <summary>
        /// Compatibility overload for transports that do not schedule by
        /// semantic class. Worker transports override it to preserve QoS.
        /// </summary>
        void SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram,
            NetDeliveryClass deliveryClass)
            => SendDatagram(endpoint, datagram);
    }

    /// <summary>
    /// Optional transport seam for callers that need to know whether a
    /// datagram was accepted by the bounded outbound queue. The historical
    /// <see cref="INetDatagramSink"/> API is intentionally void, so callers
    /// must retain their deadline/retry path unless this interface is present.
    /// Implementations must return <c>true</c> only after ownership of the
    /// datagram has been accepted; a socket send attempt is not enough.
    /// </summary>
    public interface IAcceptedNetDatagramSink
    {
        bool TrySendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram,
            NetDeliveryClass deliveryClass);
    }
}
