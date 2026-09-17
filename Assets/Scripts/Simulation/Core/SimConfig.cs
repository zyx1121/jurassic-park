using System;

namespace JurassicPark.Simulation
{
    /// <summary>Plain settings for a world. The Unity side fills this from a ScriptableObject so no gameplay number is a literal in code.</summary>
    public sealed class SimConfig
    {
        /// <summary>Fixed simulation rate. An integer so tick-derived time is exact and deadlines are counted in ticks.</summary>
        public int TicksPerSecond { get; }

        /// <summary>Length of one fixed simulation step in seconds.</summary>
        public double TickSeconds => 1.0 / TicksPerSecond;

        /// <summary>Upper bound on steps run by one Advance call, so a long stall cannot freeze the host catching up.</summary>
        public int MaxStepsPerAdvance { get; }

        public ulong Seed { get; }

        public SimConfig(int ticksPerSecond, int maxStepsPerAdvance, ulong seed)
        {
            if (ticksPerSecond < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (maxStepsPerAdvance < 1) throw new ArgumentOutOfRangeException(nameof(maxStepsPerAdvance));
            TicksPerSecond = ticksPerSecond;
            MaxStepsPerAdvance = maxStepsPerAdvance;
            Seed = seed;
        }
    }
}
