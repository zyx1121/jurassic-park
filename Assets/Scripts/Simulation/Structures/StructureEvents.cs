namespace JurassicPark.Simulation
{
    public sealed class SitePlaced : SimEvent
    {
        public EntityId Site { get; }
        public SeatId Owner { get; }
        public SitePlaced(EntityId site, SeatId owner) { Site = site; Owner = owner; }
    }

    public sealed class BuildingCompleted : SimEvent
    {
        public EntityId Building { get; }
        public BuildingCompleted(EntityId building) => Building = building;
    }

    /// <summary>Cells changed hands: something now blocks or no longer blocks them. Paths through them are stale.</summary>
    public sealed class PassabilityChanged : SimEvent
    {
        public EntityId Cause { get; }
        public bool NowBlocked { get; }
        public PassabilityChanged(EntityId cause, bool nowBlocked) { Cause = cause; NowBlocked = nowBlocked; }
    }

    public sealed class GateToggled : SimEvent
    {
        public EntityId Gate { get; }
        public bool Open { get; }
        public GateToggled(EntityId gate, bool open) { Gate = gate; Open = open; }
    }
}
