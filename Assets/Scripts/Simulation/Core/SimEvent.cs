namespace JurassicPark.Simulation
{
    /// <summary>A change that has already been committed. Subscribers react to it; they must never apply its effect a second time.</summary>
    public abstract class SimEvent
    {
        /// <summary>Tick whose commit published this event.</summary>
        public long Tick { get; internal set; }
    }

    public sealed class EntitySpawned : SimEvent
    {
        public EntityId Entity { get; }
        public EntityKind Kind { get; }
        public SeatId Owner { get; }

        public EntitySpawned(EntityId entity, EntityKind kind, SeatId owner)
        {
            Entity = entity;
            Kind = kind;
            Owner = owner;
        }
    }

    public sealed class EntityRemoved : SimEvent
    {
        public EntityId Entity { get; }
        public string Reason { get; }

        public EntityRemoved(EntityId entity, string reason)
        {
            Entity = entity;
            Reason = reason;
        }
    }
}
