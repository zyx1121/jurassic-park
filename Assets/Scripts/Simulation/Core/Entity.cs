namespace JurassicPark.Simulation
{
    /// <summary>Authoritative record of one thing in the world. Only simulation code mutates it; presentation reads snapshots.</summary>
    public sealed class Entity
    {
        public EntityId Id { get; }
        public EntityKind Kind { get; }

        /// <summary>Content definition key, for example a unit rawcode from the original map.</summary>
        public string DefinitionId { get; }

        public SeatId Owner { get; internal set; }
        public SimVector2 Position { get; internal set; }

        /// <summary>False from the moment removal is requested, even though the record stays resolvable until the tick commits.</summary>
        public bool IsAlive { get; private set; } = true;

        internal Entity(EntityId id, EntityKind kind, string definitionId, SeatId owner, SimVector2 position)
        {
            Id = id;
            Kind = kind;
            DefinitionId = definitionId;
            Owner = owner;
            Position = position;
        }

        /// <summary>Called by World.Despawn only, so liveness can never diverge from the removal queue and its event.</summary>
        internal void MarkRemoved() => IsAlive = false;

        public override string ToString() => $"{Id} {Kind}:{DefinitionId} {Owner} at {Position}";
    }
}
