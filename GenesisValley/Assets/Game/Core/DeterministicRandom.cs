namespace GenesisValley.Core
{
    /// <summary>Small seedable xorshift64* generator. State is saved so simulation stays reproducible after load.</summary>
    public sealed class DeterministicRandom
    {
        public ulong State { get; private set; }

        public DeterministicRandom(ulong seed) { State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

        public void Restore(ulong state) { State = state == 0 ? 0x9E3779B97F4A7C15UL : state; }

        public ulong NextULong()
        {
            ulong x = State;
            x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
            State = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform in [0,1).</summary>
        public float NextFloat() => (NextULong() >> 40) / (float)(1UL << 24);
        public float Range(float min, float max) => min + (max - min) * NextFloat();
        public int Range(int minInclusive, int maxExclusive) => minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
    }
}
