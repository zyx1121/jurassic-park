using System;

namespace JurassicPark.Simulation
{
    /// <summary>Plain settings for a world. The Unity side fills this from a ScriptableObject so no gameplay number is a literal in code.</summary>
    public sealed class SimConfig
    {
        /// <summary>Length of one fixed simulation step in seconds.</summary>
        public float TickSeconds { get; }

        /// <summary>Upper bound on steps run by one Advance call, so a long stall cannot freeze the host catching up.</summary>
        public int MaxStepsPerAdvance { get; }

        public ulong Seed { get; }

        public SimConfig(float tickSeconds, int maxStepsPerAdvance, ulong seed)
        {
            if (!(tickSeconds > 0f)) throw new ArgumentOutOfRangeException(nameof(tickSeconds));
            if (maxStepsPerAdvance < 1) throw new ArgumentOutOfRangeException(nameof(maxStepsPerAdvance));
            TickSeconds = tickSeconds;
            MaxStepsPerAdvance = maxStepsPerAdvance;
            Seed = seed;
        }
    }
}
