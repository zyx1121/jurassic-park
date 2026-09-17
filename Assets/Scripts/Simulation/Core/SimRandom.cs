using System;

namespace JurassicPark.Simulation
{
    /// <summary>Seeded generator owned by the world, so a match can be replayed from its seed and command log. Never use System.Random or UnityEngine.Random in simulation code.</summary>
    public sealed class SimRandom
    {
        private ulong state;

        public SimRandom(ulong seed)
        {
            // SplitMix64 scramble so nearby seeds diverge and a zero seed is still valid for xorshift.
            ulong z = seed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            state = z ^ (z >> 31);
            if (state == 0) state = 0x9E3779B97F4A7C15UL;
        }

        public ulong NextUInt64()
        {
            ulong x = state;
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            state = x;
            return x;
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentException("Range is empty.");
            ulong span = (ulong)((long)maxExclusive - minInclusive);
            return (int)((long)(NextUInt64() % span) + minInclusive);
        }

        /// <summary>Uniform float in [minInclusive, maxInclusive], the shape the original map uses for its spawn timers.</summary>
        public float Range(float minInclusive, float maxInclusive)
        {
            if (maxInclusive < minInclusive) throw new ArgumentException("Range is inverted.");
            double unit = (NextUInt64() >> 11) * (1.0 / 9007199254740991.0);
            return (float)(minInclusive + (maxInclusive - minInclusive) * unit);
        }
    }
}
