namespace MphRead.Mods.Network
{
    public enum ReceiveResult
    {
        Newest,
        OutOfOrder,
        Duplicate,
        TooOld
    }

    /// <summary>
    /// One stream's receipt history. Owned by the connection's consumer thread.
    /// A reordered datagram is acknowledged without replacing newer state.
    /// The caller must not publish an ACK until HasReceived is true.
    /// </summary>
    public struct ReceiveWindow
    {
        public bool HasReceived { get; private set; }
        public uint Ack { get; private set; }
        public uint AckBits { get; private set; }

        public ReceiveResult Record(uint sequence)
        {
            if (!HasReceived)
            {
                HasReceived = true;
                Ack = sequence;
                return ReceiveResult.Newest;
            }
            if (sequence == Ack)
            {
                return ReceiveResult.Duplicate;
            }
            if (Sequence32.IsNewer(sequence, Ack))
            {
                uint distance = unchecked(sequence - Ack);
                // C# masks a uint's shift count to five bits: handle 32
                // separately, or a jump of 32 preserves obsolete history.
                AckBits = distance > 32 ? 0
                    : distance == 32 ? 0x80000000u
                    : (AckBits << (int)distance) | (1u << ((int)distance - 1));
                Ack = sequence;
                return ReceiveResult.Newest;
            }
            uint age = unchecked(Ack - sequence);
            if (age > 32)
            {
                return ReceiveResult.TooOld;
            }
            uint mask = 1u << ((int)age - 1);
            if ((AckBits & mask) != 0)
            {
                return ReceiveResult.Duplicate;
            }
            AckBits |= mask;
            return ReceiveResult.OutOfOrder;
        }

        public static bool IsAcknowledged(uint sequence, uint ack, uint ackBits)
        {
            uint age = unchecked(ack - sequence);
            return age == 0 || (age <= 32 && (ackBits & (1u << ((int)age - 1))) != 0);
        }
    }
}
