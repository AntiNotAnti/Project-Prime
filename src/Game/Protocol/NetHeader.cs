using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public enum NetMessageType : byte
    {
        Join = 1,
        Accepted = 2,
        Input = 3,
        Snapshot = 4,
        Event = 5,
        KeepAlive = 6,
        Ping = 7,
        Pong = 8,
        Refused = 9,
        Ack = 10,
        /// <summary>Bounded, server-selected developer diagnostics.</summary>
        Debug = 11,
        World = 12,
        JoinPending = 13
    }

    [Flags]
    public enum NetHeaderFlags : byte
    {
        None = 0,
        HasAck = 1,
        Unsequenced = 2
    }

    /// <summary>
    /// Authoritative envelope; all integer fields are little endian. Join and
    /// keepalive are unsequenced so loading keepalives need no access to the
    /// game thread's ACK state. They never acknowledge reliable events.
    /// </summary>
    public readonly record struct NetHeader(NetMessageType Type, NetHeaderFlags Flags,
        ulong ConnectionId, uint Sequence, uint Ack, uint AckBits)
    {
        public const ushort Magic = 0x5046;
        public const int Size = 24;
        // Protocol 9 adds explicit initial-join match routing for multi-match workers.
        public const byte Version = 9;

        public void Write(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, Magic);
            destination[2] = (byte)Type;
            destination[3] = (byte)Flags;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[4..], ConnectionId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], Ack);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], AckBits);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out NetHeader header)
        {
            header = default;
            if (source.Length < Size || source.Length > NetConfig.MaxPacketSize
                || BinaryPrimitives.ReadUInt16LittleEndian(source) != Magic
                || source[2] < (byte)NetMessageType.Join || (source[2] > (byte)NetMessageType.Debug && source[2] != (byte)NetMessageType.World && source[2] != (byte)NetMessageType.JoinPending)
                || (source[3] & ~3) != 0)
            {
                return false;
            }
            var type = (NetMessageType)source[2];
            var flags = (NetHeaderFlags)source[3];
            ulong connection = BinaryPrimitives.ReadUInt64LittleEndian(source[4..]);
            uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]);
            uint ack = BinaryPrimitives.ReadUInt32LittleEndian(source[16..]);
            uint ackBits = BinaryPrimitives.ReadUInt32LittleEndian(source[20..]);
            bool unsequenced = (flags & NetHeaderFlags.Unsequenced) != 0;
            bool handshake = type is NetMessageType.Join or NetMessageType.Refused or NetMessageType.JoinPending;
            if (unsequenced != (handshake || type == NetMessageType.KeepAlive)
                || (unsequenced && (flags != NetHeaderFlags.Unsequenced || sequence != 0))
                || ((flags & NetHeaderFlags.HasAck) == 0 && (ack != 0 || ackBits != 0))
                || (handshake ? connection != 0 : connection == 0))
            {
                return false;
            }
            header = new NetHeader(type, flags, connection, sequence, ack, ackBits);
            return true;
        }
    }
}
