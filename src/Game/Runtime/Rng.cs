namespace MphRead
{
    /// <summary>Legacy constants and a stateless compatibility entry point.</summary>
    public static class Rng
    {
        public const uint Rng1StartValue = 0x3DE9179BU;
        public const uint Rng2StartValue = 0;
        public static uint CallRng(ref uint rng, uint value) => RngAlgorithm.Next(ref rng, value);
    }
}
