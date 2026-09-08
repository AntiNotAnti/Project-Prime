namespace MphRead
{
    /// <summary>The two original gameplay random streams, owned by one scene.</summary>
    public sealed class MatchRandom
    {
        private uint _rng1 = Rng.Rng1StartValue;
        private uint _rng2 = Rng.Rng2StartValue;
        public uint Rng1 => _rng1;
        public uint Rng2 => _rng2;
        public void SetRng1(uint value) => _rng1 = value;
        public void SetRng2(uint value) => _rng2 = value;
        public uint GetRandomInt1(int value) => GetRandomInt1(unchecked((uint)value));
        public uint GetRandomInt2(int value) => GetRandomInt2(unchecked((uint)value));
        public uint GetRandomInt1(uint value) => RngAlgorithm.Next(ref _rng1, value);
        public uint GetRandomInt2(uint value) => RngAlgorithm.Next(ref _rng2, value);
    }
}
