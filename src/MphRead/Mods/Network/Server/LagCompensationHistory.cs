using System;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>Single simulation owner; fixed storage for eight players and 32 ticks.</summary>
    public sealed class LagCompensationHistory
    {
        public const int Capacity = 32;
        public const int PlayerCapacity = 8;
        private readonly Entry[] _entries = new Entry[Capacity * PlayerCapacity];
        public long Queries { get; private set; }
        public long Missing { get; private set; }

        private struct Entry
        {
            public bool Present;
            public uint Tick;
            public LagCompensationState State;
        }

        public void Record(uint tick, PlayerEntity player, ulong connectionId, uint lifeId)
            => Record(tick, LagCompensationState.Capture(player, connectionId, lifeId));

        public void Record(uint tick, in LagCompensationState state)
        {
            if ((uint)state.Slot >= PlayerCapacity || state.ConnectionId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            _entries[Index(state.Slot, tick)] = new Entry { Present = true, Tick = tick, State = state };
        }

        public bool TryGet(int slot, uint tick, ulong connectionId, uint lifeId, out LagCompensationState state)
        {
            Queries++;
            if ((uint)slot < PlayerCapacity && connectionId != 0)
            {
                ref readonly Entry entry = ref _entries[Index(slot, tick)];
                if (entry.Present && entry.Tick == tick
                    && entry.State.ConnectionId == connectionId && entry.State.LifeId == lifeId)
                {
                    state = entry.State;
                    return true;
                }
            }
            Missing++;
            state = default;
            return false;
        }

        // A round/map transition invalidates every prior collider, even if a
        // connection and spawn counter happen to be reused in the next room.
        public void Clear()
        {
            Array.Clear(_entries);
            Queries = 0;
            Missing = 0;
        }

        private static int Index(int slot, uint tick) => slot * Capacity + (int)(tick & (Capacity - 1));
    }
}
