using System;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace MphRead.Mods.Network
{
    public enum NetConnectionState
    {
        Connecting,
        Loading,
        Ready,
        Playing,
        Disconnecting
    }

    /// <summary>
    /// Protocol state owned by one connection consumer, never a socket callback.
    /// Identity is random per admission. An authenticated-by-session-ID newer
    /// sequenced datagram can update routing; stale packets and keepalives cannot.
    /// Connection IDs are bearer session identifiers, not encrypted transport.
    /// </summary>
    public sealed class NetConnection
    {
        private ReceiveWindow _received;
        private uint _nextSequence;
        public ulong Id { get; }
        public IPEndPoint Endpoint { get; private set; }
        public NetConnectionState State { get; private set; } = NetConnectionState.Loading;
        public uint MatchId { get; private set; }
        public double LastReceived { get; private set; }
        public ReliableChannel Reliable { get; } = new();
        public NetMetrics Metrics { get; } = new();

        public NetConnection(ulong id, IPEndPoint endpoint, uint matchId, double now)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }
            Id = id;
            Endpoint = endpoint;
            MatchId = matchId;
            LastReceived = now;
        }

        public static ulong NewIdentity()
        {
            Span<byte> bytes = stackalloc byte[8];
            ulong id;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                id = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            }
            while (id == 0);
            return id;
        }

        public NetHeader CreateHeader(NetMessageType type)
        {
            NetHeaderFlags flags = _received.HasReceived ? NetHeaderFlags.HasAck : NetHeaderFlags.None;
            return new NetHeader(type, flags, Id, _nextSequence++, _received.Ack, _received.AckBits);
        }

        /// <summary>
        /// The caller must validate the full body before passing its header
        /// here. Duplicates still process ACKs but never deliver gameplay twice.
        /// </summary>
        public bool TryReceive(in NetHeader header, IPEndPoint sender, double now, out ReceiveResult result)
        {
            result = ReceiveResult.TooOld;
            if (header.ConnectionId != Id || State == NetConnectionState.Disconnecting
                || !Double.IsFinite(now) || now < LastReceived)
            {
                return false;
            }
            if ((header.Flags & NetHeaderFlags.Unsequenced) != 0)
            {
                if (header.Type != NetMessageType.KeepAlive || !sender.Equals(Endpoint))
                {
                    return false;
                }
                LastReceived = now;
                return true;
            }
            if (!sender.Equals(Endpoint))
            {
                if (_received.HasReceived && !Sequence32.IsNewer(header.Sequence, _received.Ack))
                {
                    return false;
                }
                Endpoint = sender;
            }
            result = _received.Record(header.Sequence);
            if (result == ReceiveResult.TooOld)
            {
                return false;
            }
            if ((header.Flags & NetHeaderFlags.HasAck) != 0)
            {
                Reliable.Acknowledge(header.Ack, header.AckBits);
            }
            LastReceived = now;
            return true;
        }

        public void BeginLoading(uint matchId)
        {
            if (State == NetConnectionState.Disconnecting)
            {
                return;
            }
            MatchId = matchId;
            State = NetConnectionState.Loading;
        }

        public bool Ready(uint matchId)
        {
            if (matchId != MatchId || State != NetConnectionState.Loading)
            {
                return false;
            }
            State = NetConnectionState.Ready;
            return true;
        }

        public bool StartPlaying()
        {
            if (State != NetConnectionState.Ready)
            {
                return false;
            }
            State = NetConnectionState.Playing;
            return true;
        }

        public void Disconnect() => State = NetConnectionState.Disconnecting;

        public void Send(INetDatagramSink transport, NetMessageType type, ReadOnlySpan<byte> payload = default)
        {
            Span<byte> datagram = stackalloc byte[NetConfig.MaxPacketSize];
            if (payload.Length > datagram.Length - NetHeader.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }
            CreateHeader(type).Write(datagram);
            payload.CopyTo(datagram[NetHeader.Size..]);
            transport.SendDatagram(Endpoint, datagram[..(NetHeader.Size + payload.Length)]);
        }

        public void FlushReliable(INetDatagramSink transport, double now)
        {
            Span<byte> datagram = stackalloc byte[NetConfig.MaxPacketSize];
            for (int budget = 0; budget < 8
                && Reliable.TryGetDue(now, out uint id, out ReliableEventType type, out ReadOnlyMemory<byte> payload);
                budget++)
            {
                NetHeader header = CreateHeader(type == ReliableEventType.Welcome
                    ? NetMessageType.Accepted : NetMessageType.Event);
                header.Write(datagram);
                int length = ReliableEventPacket.Write(datagram[NetHeader.Size..], id, type, payload.Span);
                transport.SendDatagram(Endpoint, datagram[..(NetHeader.Size + length)]);
                Reliable.MarkSent(id, header.Sequence, now);
            }
        }
    }
}
