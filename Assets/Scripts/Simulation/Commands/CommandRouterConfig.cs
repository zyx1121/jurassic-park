using System;

namespace JurassicPark.Simulation
{
    public sealed class CommandRouterConfig
    {
        /// <summary>How many resolved commands per seat are remembered so a resend gets its original answer instead of running twice.</summary>
        public int RememberedResultsPerSeat { get; }

        /// <summary>Upper bound on commands one seat may have waiting for the next tick; more are dropped as a flood.</summary>
        public int MaxPendingPerSeat { get; }

        public CommandRouterConfig(int rememberedResultsPerSeat, int maxPendingPerSeat)
        {
            if (rememberedResultsPerSeat < 1) throw new ArgumentOutOfRangeException(nameof(rememberedResultsPerSeat));
            if (maxPendingPerSeat < 1) throw new ArgumentOutOfRangeException(nameof(maxPendingPerSeat));
            RememberedResultsPerSeat = rememberedResultsPerSeat;
            MaxPendingPerSeat = maxPendingPerSeat;
        }
    }
}
