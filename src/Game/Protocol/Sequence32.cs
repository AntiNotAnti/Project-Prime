namespace MphRead.Mods.Network
{
    /// <summary>
    /// Serial arithmetic for streams less than half the sequence space apart.
    /// Equal values and the ambiguous half-range distance are never newer.
    /// </summary>
    public static class Sequence32
    {
        public static bool IsNewer(uint value, uint previous)
        {
            uint distance = unchecked(value - previous);
            return distance != 0 && distance < 0x80000000u;
        }
    }
}
