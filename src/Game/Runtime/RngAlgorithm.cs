namespace MphRead
{
    public static class RngAlgorithm
    {
        public static uint Next(ref uint state, uint max)
        {
            state = unchecked(state * 0x7FF8A3EDu + 0x2AA01D31u);
            return (uint)((state >> 16) * (long)max / 0x10000L);
        }
    }
}
