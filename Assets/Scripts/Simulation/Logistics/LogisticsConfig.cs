using System;

namespace JurassicPark.Simulation
{
    public sealed class LogisticsConfig
    {
        /// <summary>Ticks a reservation lives without being renewed by its task.</summary>
        public int ReservationLifetimeTicks { get; }

        /// <summary>Definition id of the entity spawned to hold goods lying on the ground.</summary>
        public string GroundPileDefinitionId { get; }

        public LogisticsConfig(int reservationLifetimeTicks, string groundPileDefinitionId)
        {
            if (reservationLifetimeTicks < 1) throw new ArgumentOutOfRangeException(nameof(reservationLifetimeTicks));
            if (string.IsNullOrEmpty(groundPileDefinitionId)) throw new ArgumentException("A ground pile definition id is required.", nameof(groundPileDefinitionId));
            ReservationLifetimeTicks = reservationLifetimeTicks;
            GroundPileDefinitionId = groundPileDefinitionId;
        }
    }
}
