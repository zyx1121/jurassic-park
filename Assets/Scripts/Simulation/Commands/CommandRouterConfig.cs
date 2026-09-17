using System;

namespace JurassicPark.Simulation
{
    public sealed class CommandRouterConfig
    {
        /// <summary>How many resolved commands per seat are remembered so a resend gets its original answer instead of running twice.</summary>
        public int RememberedResultsPerSeat { get; }

        /// <summary>Upper bound on commands one seat may have waiting for the next tick; more are dropped as a flood.</summary>
        public int MaxPendingPerSeat { get; }

        /// <summary>How far ahead of the last accepted id a new id may be. A small tolerance for a sender that skips ids; anything beyond it is refused rather than adopted, and the answer tells the sender which id to use.</summary>
        public int MaxCommandIdGap { get; }

        public CommandRouterConfig(int rememberedResultsPerSeat, int maxPendingPerSeat, int maxCommandIdGap)
        {
            if (rememberedResultsPerSeat < 1) throw new ArgumentOutOfRangeException(nameof(rememberedResultsPerSeat));
            if (maxPendingPerSeat < 1) throw new ArgumentOutOfRangeException(nameof(maxPendingPerSeat));
            if (maxCommandIdGap < 1) throw new ArgumentOutOfRangeException(nameof(maxCommandIdGap));
            RememberedResultsPerSeat = rememberedResultsPerSeat;
            MaxCommandIdGap = maxCommandIdGap;
            MaxPendingPerSeat = maxPendingPerSeat;
        }
    }
}
