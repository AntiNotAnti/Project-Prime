using System;

namespace MphRead.Entities
{
    // Raw legacy memory layout, retained for memory inspection tools.
    [Flags]
    public enum SpawnerFlags : byte
    {
        Suspended = 1,
        Active = 2,
        HasModel = 4,
        PlayAnimation = 8,
        CounterBit0 = 0x10,
        CounterBit1 = 0x20,
        CounterBit2 = 0x40,
        CounterBit3 = 0x80
    }
}
